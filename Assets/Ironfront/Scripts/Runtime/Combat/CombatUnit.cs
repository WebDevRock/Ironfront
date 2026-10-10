using System;
using System.Collections.Generic;
using Ironfront.Core.Combat;
using Ironfront.Data;
using Ironfront.Match;
using Ironfront.Units;
using UnityEngine;

namespace Ironfront.Combat
{
    /// <summary>
    /// Gives a unit a weapon and combat orders. Wraps a <see cref="CombatOrderMachine"/> and implements its
    /// body: movement through <see cref="UnitMover"/> (the existing NavMesh movement), targeting through
    /// the <see cref="Health"/> registry and line-of-sight raycasts, and firing through <see cref="Projectile"/>.
    /// </summary>
    [RequireComponent(typeof(UnitMover), typeof(Health))]
    public class CombatUnit : MonoBehaviour, ICombatBody
    {
        private static readonly List<CombatUnit> s_All = new List<CombatUnit>();
        public static IReadOnlyList<CombatUnit> All => s_All;

        [Tooltip("Optional; otherwise the weapon comes from the unit's UnitDefinition.")]
        [SerializeField] private WeaponDefinition weaponOverride;
        [Tooltip("Rotated towards the target (visual only).")]
        [SerializeField] private Transform turret;
        [SerializeField] private Transform muzzle;
        [Tooltip("Layers that block line of sight (scenery and structures).")]
        [SerializeField] private LayerMask lineOfSightBlockers = 1;
        [SerializeField] private float turretTurnSpeed = 240f;

        private UnitMover _mover;
        private Health _health;
        private WeaponCooldown _cooldown;

        public WeaponDefinition Weapon { get; private set; }
        public CombatOrderMachine Machine { get; private set; }
        public Health CurrentTarget => Machine?.Target as Health;
        public int ShotsFired { get; private set; }
        public float CooldownRemaining => _cooldown != null ? _cooldown.Remaining : 0f;

        /// <summary>Raised with the target each time a shot is fired.</summary>
        public event Action<Health> Fired;

        private void Awake()
        {
            _mover = GetComponent<UnitMover>();
            _health = GetComponent<Health>();
            var identity = GetComponent<UnitIdentity>();
            Weapon = weaponOverride != null ? weaponOverride : identity != null && identity.Definition != null ? identity.Definition.Weapon : null;
            if (Weapon == null)
            {
                Debug.LogError($"[Ironfront] {name} has a CombatUnit but no weapon.", this);
                enabled = false;
                return;
            }
            _cooldown = new WeaponCooldown(Weapon.Cooldown);
            Machine = new CombatOrderMachine(CombatSettings.ForWeapon(Weapon.Range, Weapon.AcquisitionRange, Weapon.RequiresLineOfSight));
            // Spread target scans over frames instead of every unit scanning in the same frame.
            Machine.OffsetScan((s_spawnIndex++ % 8) * Machine.Settings.ScanInterval / 8f);
        }

        private void OnEnable()
        {
            if (!s_All.Contains(this)) s_All.Add(this);
            if (_health != null) _health.DamagedBy += OnDamaged;
        }

        private void OnDisable()
        {
            s_All.Remove(this);
            if (_health != null) _health.DamagedBy -= OnDamaged;
        }

        private void OnDamaged(float amount, Health attacker)
        {
            if (Machine != null && attacker != null && attacker != _health) Machine.NotifyAttacked(this, attacker);
        }

        private void Update()
        {
            using var perfScope = Ironfront.Diagnostics.PerfMarkers.CombatTick.Auto();
            if (Machine == null || !_health.IsAlive) return;
            _cooldown.Tick(Time.deltaTime);
            Machine.Tick(Time.deltaTime, this);
            AimTurret();
        }

        // ---------- Orders (used by commands, scenario scripts and tests) ----------

        public void OrderMove(Vector3 destination) => Machine?.OrderMove(this, destination);
        public void OrderAttack(Health target) => Machine?.OrderAttack(this, target);
        public void OrderAttackMove(Vector3 destination) => Machine?.OrderAttackMove(this, destination);
        public void OrderStop() => Machine?.OrderStop(this);
        public void OrderHold() => Machine?.OrderHold(this);

        public string DescribeOrder() => Machine != null ? Machine.Describe() : "-";

        // ---------- ICombatBody ----------

        public Vector3 Position => transform.position;

        public float DistanceTo(ICombatTarget target) =>
            target is Health h && h != null ? h.DistanceFrom(transform.position) : float.MaxValue;

        public bool HasLineOfSight(ICombatTarget target)
        {
            using var perfScope = Ironfront.Diagnostics.PerfMarkers.LineOfSight.Auto();
            if (!(target is Health h) || h == null) return false;
            Vector3 from = MuzzlePosition;
            Vector3 to = h.AimPoint(from);
            if (!Physics.Linecast(from, to, out var hit, lineOfSightBlockers, QueryTriggerInteraction.Ignore)) return true;
            return hit.collider.GetComponentInParent<Health>() == h; // the first thing hit is the target itself
        }

        // Reused between calls (main thread only) so target scans do not allocate.
        private static readonly List<TargetCandidate<Health>> s_candidates = new List<TargetCandidate<Health>>(64);
        private static int s_spawnIndex;

        public ICombatTarget FindTarget(float radius, ICombatTarget ignore)
        {
            using var perfScope = Ironfront.Diagnostics.PerfMarkers.FindTarget.Auto();
            int myFaction = _health.FactionId;
            Vector3 me = transform.position;
            s_candidates.Clear();
            var all = Health.All;
            for (int i = 0; i < all.Count; i++)
            {
                var h = all[i];
                if (h == _health || !h.IsAlive || ReferenceEquals(h, ignore)) continue;
                if (!DamageRules.AreHostile(myFaction, h.FactionId)) continue;

                // Cheap centre-distance rejection before the physics closest-point query.
                Vector3 p = h.transform.position;
                float dx = p.x - me.x, dz = p.z - me.z, reach = radius + h.BoundingRadius;
                if (dx * dx + dz * dz > reach * reach) continue;

                float d = h.DistanceFrom(me);
                if (d <= radius) s_candidates.Add(new TargetCandidate<Health>(h, d, h.IsArmed, h.IsStructure));
            }
            return TargetSelector.TryChoose(s_candidates, radius, out var best) ? best : null;
        }

        public void MoveTo(Vector3 destination) => _mover.MoveTo(destination);

        public void ChaseTo(ICombatTarget target)
        {
            if (target is Health h && h != null)
            {
                Vector3 p = h.AimPoint(transform.position);
                p.y = h.transform.position.y;
                _mover.MoveTo(p);
            }
        }

        public void Halt() => _mover.Stop();

        public bool HasArrived => _mover.HasArrived;

        public bool TryFire(ICombatTarget target)
        {
            if (!(target is Health h) || h == null || !h.IsAlive) return false;
            if (!_cooldown.TryFire()) return false;

            Vector3 from = MuzzlePosition;
            Projectile.Launch(from, h, Weapon.Damage, _health.FactionId, Weapon.AllowFriendlyFire,
                Weapon.ProjectileSpeed, Weapon.ProjectileSize, _health);
            CombatFx.MuzzleFlash(from);
            ShotsFired++;
            Fired?.Invoke(h);
            return true;
        }

        private Vector3 MuzzlePosition => muzzle != null ? muzzle.position : transform.position + Vector3.up * 1f;

        private void AimTurret()
        {
            if (turret == null) return;
            var target = CurrentTarget;
            Quaternion desired;
            if (target != null && target.IsAlive)
            {
                Vector3 dir = target.AimPoint(turret.position) - turret.position;
                dir.y = 0f;
                if (dir.sqrMagnitude < 1e-4f) return;
                desired = Quaternion.LookRotation(dir);
            }
            else
            {
                desired = transform.rotation; // face forward when idle
            }
            turret.rotation = Quaternion.RotateTowards(turret.rotation, desired, turretTurnSpeed * Time.deltaTime);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_All.Clear();
            s_candidates.Clear();
            s_spawnIndex = 0;
        }
    }
}
