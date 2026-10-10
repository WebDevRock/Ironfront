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
        private Collider _collider;
        private bool _isStructure;
        private bool _isArmed;
        private string _displayName;

        public HealthModel Model => _model;
        public float Current => _model != null ? _model.Current : 0f;
        public float Max => _model != null ? _model.Max : 0f;
        public float Armour => _model != null ? _model.Armour : 0f;
        public bool IsStructure => _isStructure;
        public bool IsArmed => _isArmed;
        public int FactionId => FactionMember.Of(this);
        public float AimHeight => aimHeight;

        // ICombatTarget. "this != null" uses Unity's lifetime check, so a destroyed object reports dead.
        public bool IsAlive => this != null && _model != null && !_model.IsDead;
        public Vector3 Position => transform.position;
        public string DisplayName => _displayName ?? name;

        /// <summary>Raised with the damage dealt after armour.</summary>
        public event Action<float> Damaged;
        public event Action<Health> Died;

        private void Awake()
        {
            _collider = GetComponent<Collider>();
            _isArmed = GetComponent<CombatUnit>() != null;

            float max = fallbackMaxHealth, armour = fallbackArmour;
            var building = GetComponent<Building>();
            var identity = GetComponent<UnitIdentity>();
            if (building != null && building.Definition != null)
            {
                _isStructure = true;
                max = building.Definition.MaxHealth;
                armour = building.Definition.Armour;
                _displayName = building.Definition.DisplayName;
            }
            else if (identity != null && identity.Definition != null)
            {
                max = identity.Definition.MaxHealth;
                armour = identity.Definition.Armour;
                _displayName = identity.Definition.DisplayName;
            }

            _model = new HealthModel(max, armour);
            _model.Died += OnModelDied;
        }

        private void OnEnable() { if (!s_All.Contains(this)) s_All.Add(this); }
        private void OnDisable() => s_All.Remove(this);

        /// <summary>
        /// Applies damage from <paramref name="attackerFaction"/>. Same-faction damage is ignored unless
        /// <paramref name="allowFriendlyFire"/>. Returns the damage dealt after armour.
        /// </summary>
        public float ApplyDamage(float rawDamage, int attackerFaction, bool allowFriendlyFire = false)
        {
            if (!IsAlive) return 0f;
            if (!DamageRules.CanDamage(attackerFaction, FactionId, allowFriendlyFire)) return 0f;
            float dealt = _model.ApplyDamage(rawDamage);
            if (dealt > 0f) Damaged?.Invoke(dealt);
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
        }
    }
}
