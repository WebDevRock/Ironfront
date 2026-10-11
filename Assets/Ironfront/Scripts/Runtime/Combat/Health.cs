using System;
using System.Collections.Generic;
using Ironfront.Buildings;
using Ironfront.Core.Combat;
using Ironfront.Match;
using Ironfront.Units;
using UnityEngine;

namespace Ironfront.Combat
{
    /// <summary>
    /// Hit points for any unit or structure. Max health and armour come from the unit's or building's
    /// definition when present. On death the object raises <see cref="Died"/> / <see cref="AnyDied"/> and is
    /// destroyed; other systems drop their references through those events and their own OnDisable.
    /// </summary>
    [DisallowMultipleComponent]
    public class Health : MonoBehaviour, ICombatTarget
    {
        private static readonly List<Health> s_All = new List<Health>();
        public static IReadOnlyList<Health> All => s_All;

        /// <summary>Raised for every death, before the object is destroyed.</summary>
        public static event Action<Health> AnyDied;

        [Tooltip("Used when there is no unit or building definition.")]
        [SerializeField, Min(1f)] private float fallbackMaxHealth = 100f;
        [SerializeField, Range(0f, 0.9f)] private float fallbackArmour;
        [Tooltip("Height of the aim point above the pivot.")]
        [SerializeField] private float aimHeight = 0.8f;
        [Tooltip("Scale of the explosion when destroyed.")]
        [SerializeField] private float explosionSize = 3f;

        private HealthModel _model;
        private float _baseArmour;
        private ArmourClass _armourClass = ArmourClass.Light;

        /// <summary>Fog-of-war participant on the same object (set by VisionEntity; null without fog).</summary>
        [System.NonSerialized] public Ironfront.Vision.VisionEntity Vision;
        private Collider _collider;
        private FactionMember _member;
        private bool _isStructure;
        private bool _isArmed;
        private string _displayName;

        public HealthModel Model => _model;
        public float Current => _model != null ? _model.Current : 0f;
        public float Max => _model != null ? _model.Max : 0f;
        public float Armour => _model != null ? _model.Armour : 0f;
        public bool IsStructure => _isStructure;
        public bool IsArmed => _isArmed;
        /// <summary>Weapon-effectiveness class (Milestone 007): structures, or the unit definition's class.</summary>
        public ArmourClass ArmourClass => _armourClass;
        public int FactionId => _member != null ? _member.FactionId : FactionMember.Of(this);

        private float _boundingRadius = -1f;

        /// <summary>Horizontal radius enclosing the collider (cheap pre-filter for range checks). Computed on
        /// first use, once collider bounds are valid.</summary>
        public float BoundingRadius
        {
            get
            {
                if (_boundingRadius < 0f && _collider != null)
                {
                    var e = _collider.bounds.extents;
                    float r = Mathf.Sqrt(e.x * e.x + e.z * e.z);
                    if (r > 0f) _boundingRadius = r;
                    return r > 0f ? r : 50f; // unknown yet: never reject by pre-filter
                }
                return _boundingRadius < 0f ? 50f : _boundingRadius;
            }
        }
        public float AimHeight => aimHeight;

        // ICombatTarget. "this != null" uses Unity's lifetime check, so a destroyed object reports dead.
        public bool IsAlive => this != null && _model != null && !_model.IsDead;
        public Vector3 Position => transform.position;
        public string DisplayName => _displayName ?? name;

        /// <summary>Faction of whoever damaged this object most recently (null if never damaged by an enemy).</summary>
        public int? LastAttackerFaction { get; private set; }

        /// <summary>Raised with the damage dealt after armour.</summary>
        public event Action<float> Damaged;
        /// <summary>Raised with the damage dealt and the attacker (may be null, e.g. for scripted damage).</summary>
        public event Action<float, Health> DamagedBy;
        /// <summary>Raised for every damage event in the scene (notifications, statistics).</summary>
        public static event Action<Health, float, Health> AnyDamaged;
        public event Action<Health> Died;

        private void Awake()
        {
            _collider = GetComponent<Collider>();
            _member = GetComponentInParent<FactionMember>();
            _isArmed = GetComponent<CombatUnit>() != null || GetComponent<DefenceTurret>() != null;

            float max = fallbackMaxHealth, armour = fallbackArmour;
            var building = GetComponent<Building>();
            var identity = GetComponent<UnitIdentity>();
            if (building != null && building.Definition != null)
            {
                _isStructure = true;
                _armourClass = ArmourClass.Structure;
                max = building.Definition.MaxHealth;
                armour = building.Definition.Armour;
                _displayName = building.Definition.DisplayName;
            }
            else if (identity != null && identity.Definition != null)
            {
                max = identity.Definition.MaxHealth;
                armour = identity.Definition.Armour;
                _armourClass = identity.Definition.ArmourClass;
                _displayName = identity.Definition.DisplayName;
            }

            _baseArmour = armour;
            _model = new HealthModel(max, armour);
            _model.Died += OnModelDied;
        }

        private void OnEnable() { if (!s_All.Contains(this)) s_All.Add(this); }

        private void Start()
        {
            // Vehicles produced after an armour upgrade start with it.
            if (_isStructure || MatchController.Current == null) return;
            ApplyArmourBonus(MatchController.Current.GetFaction(FactionId).Research.ArmourBonus);
        }

        /// <summary>Sets armour to the definition value plus a research bonus (vehicles only).</summary>
        public void ApplyArmourBonus(float bonus)
        {
            if (_model == null || _isStructure) return;
            _model.SetArmour(_baseArmour + Mathf.Max(0f, bonus));
        }
        private void OnDisable() => s_All.Remove(this);

        /// <summary>
        /// Applies damage from <paramref name="attackerFaction"/>. Same-faction damage is ignored unless
        /// <paramref name="allowFriendlyFire"/>. Returns the damage dealt after armour.
        /// </summary>
        public float ApplyDamage(float rawDamage, int attackerFaction, bool allowFriendlyFire = false, Health attacker = null)
        {
            if (!IsAlive) return 0f;
            if (!DamageRules.CanDamage(attackerFaction, FactionId, allowFriendlyFire)) return 0f;
            if (attacker != null) LastAttackerFaction = attacker.FactionId; // set first: the killing blow fires Died inside ApplyDamage
            else if (DamageRules.AreHostile(attackerFaction, FactionId)) LastAttackerFaction = attackerFaction;
            float dealt = _model.ApplyDamage(rawDamage);
            if (dealt > 0f && attacker != null) Ironfront.Vision.VisionSystem.RevealAttacker(attacker, FactionId);
            if (dealt > 0f && IsAlive)
            {
                Damaged?.Invoke(dealt);
                DamagedBy?.Invoke(dealt, attacker);
                AnyDamaged?.Invoke(this, dealt, attacker);
            }
            return dealt;
        }

        /// <summary>Point on this object's collider nearest to <paramref name="from"/>, at aim height.</summary>
        public Vector3 AimPoint(Vector3 from)
        {
            Vector3 p = _collider != null && _collider.enabled ? _collider.ClosestPoint(from) : transform.position;
            p.y = transform.position.y + aimHeight;
            return p;
        }

        /// <summary>Horizontal distance from <paramref name="from"/> to this object's nearest surface.</summary>
        public float DistanceFrom(Vector3 from)
        {
            Vector3 p = _collider != null && _collider.enabled ? _collider.ClosestPoint(new Vector3(from.x, transform.position.y + aimHeight, from.z)) : transform.position;
            p.y = 0f;
            from.y = 0f;
            return Vector3.Distance(p, from);
        }

        private void OnModelDied()
        {
            Died?.Invoke(this);
            AnyDied?.Invoke(this);
            CombatFx.Explosion(transform.position + Vector3.up * aimHeight, explosionSize);
            Destroy(gameObject);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_All.Clear();
            AnyDied = null;
            AnyDamaged = null;
        }
    }
}
