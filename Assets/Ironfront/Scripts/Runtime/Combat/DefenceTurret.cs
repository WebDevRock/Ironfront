using System;
using System.Collections.Generic;
using Ironfront.Buildings;
using Ironfront.Core.Combat;
using Ironfront.Data;
using Ironfront.Match;
using Ironfront.Vision;
using UnityEngine;

namespace Ironfront.Combat
{
    /// <summary>
    /// Automated base defence (Guard Tower, Anti-Armour Turret; Milestone 007). Uses the same
    /// <see cref="CombatOrderMachine"/> as units, permanently on Hold: it engages hostile targets its faction can
    /// see within range, rotates its turret head, and fires through <see cref="Projectile"/> with the weapon's
    /// effectiveness. Power-dependent defences stop firing while their owner is low on power.
    /// </summary>
    [RequireComponent(typeof(Building), typeof(Health))]
    public class DefenceTurret : MonoBehaviour, ICombatBody, ITargetAwareness, IFiringWeapon
    {
        private static readonly List<TargetCandidate<Health>> s_candidates = new List<TargetCandidate<Health>>(64);

        [SerializeField] private Transform turret;
        [SerializeField] private Transform muzzle;
        [SerializeField] private LayerMask lineOfSightBlockers = 1;
        [SerializeField] private float turnSpeed = 160f;

        private Building _building;
        private Health _health;
        private WeaponCooldown _cooldown;

        public WeaponDefinition Weapon { get; private set; }
        public CombatOrderMachine Machine { get; private set; }
        public Health CurrentTarget => Machine?.Target as Health;
        public int ShotsFired { get; private set; }
        public Transform Muzzle => muzzle;
        /// <summary>False while a power-dependent defence lacks power.</summary>
        public bool IsOnline
        {
            get
            {
                if (_building == null || _building.Definition == null || !_building.Definition.NeedsPower) return true;
                var match = MatchController.Current;
                return match == null || !match.GetFaction(_building.FactionId).Power.IsLowPower;
            }
        }

        public event Action<Health> Fired;

        private void Awake()
        {
            _building = GetComponent<Building>();
            _health = GetComponent<Health>();
            Weapon = _building.Definition != null ? _building.Definition.Weapon : null;
            if (Weapon == null)
            {
                Debug.LogError($"[Ironfront] {name} has a DefenceTurret but its definition has no weapon.", this);
                enabled = false;
                return;
            }
            _cooldown = new WeaponCooldown(Weapon.Cooldown);
            Machine = new CombatOrderMachine(CombatSettings.ForWeapon(Weapon.Range, Weapon.Range, Weapon.RequiresLineOfSight, Weapon.MinRange));
            Machine.OrderHold(this);
        }

        private void Update()
        {
            if (Machine == null || !_health.IsAlive) return;
            _cooldown.Tick(Time.deltaTime);
            if (!IsOnline) return; // unpowered: no scanning, no firing
            Machine.Tick(Time.deltaTime, this);
            Aim();
        }

        private void Aim()
        {
            if (turret == null) return;
            var t = CurrentTarget;
            if (t == null || !t.IsAlive) return;
            Vector3 dir = t.AimPoint(turret.position) - turret.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) return;
            turret.rotation = Quaternion.RotateTowards(turret.rotation, Quaternion.LookRotation(dir), turnSpeed * Time.deltaTime);
        }

        // ---------- ICombatBody ----------

        public Vector3 Position => transform.position;
        public float DistanceTo(ICombatTarget target) => target is Health h && h != null ? h.DistanceFrom(transform.position) : float.MaxValue;

        public bool HasLineOfSight(ICombatTarget target)
        {
            if (!(target is Health h) || h == null) return false;
            Vector3 from = muzzle != null ? muzzle.position : transform.position + Vector3.up * 2f;
            Vector3 to = h.AimPoint(from);
            if (!Physics.Linecast(from, to, out var hit, lineOfSightBlockers, QueryTriggerInteraction.Ignore)) return true;
            var owner = hit.collider.GetComponentInParent<Health>();
            return owner == h || owner == _health;
        }

        public ICombatTarget FindTarget(float radius, ICombatTarget ignore)
        {
            int me = _health.FactionId;
            Vector3 pos = transform.position;
            s_candidates.Clear();
            var all = Health.All;
            for (int i = 0; i < all.Count; i++)
            {
                var h = all[i];
                if (h == _health || !h.IsAlive || ReferenceEquals(h, ignore)) continue;
                if (!DamageRules.AreHostile(me, h.FactionId) || !VisionSystem.CanSee(me, h)) continue;
                Vector3 p = h.transform.position;
                float dx = p.x - pos.x, dz = p.z - pos.z, reach = radius + h.BoundingRadius + 3f;
                if (dx * dx + dz * dz > reach * reach) continue;
                float d = h.DistanceFrom(pos);
                if (d <= radius && d >= Weapon.MinRange) s_candidates.Add(new TargetCandidate<Health>(h, d, h.IsArmed, h.IsStructure));
            }
            return TargetSelector.TryChoose(s_candidates, radius, out var best) ? best : null;
        }

        public void MoveTo(Vector3 destination) { }
        public void ChaseTo(ICombatTarget target) { }
        public void Halt() { }
        public bool HasArrived => true;

        public bool TryFire(ICombatTarget target)
        {
            if (!(target is Health h) || h == null || !h.IsAlive || !IsOnline) return false;
            if (!_cooldown.TryFire()) return false;
            Vector3 from = muzzle != null ? muzzle.position : transform.position + Vector3.up * 2f;
            float upgrade = CombatUnit.DamageUpgrade(_health.FactionId);
            int salvo = Weapon.Salvo;
            for (int i = 0; i < salvo; i++)
                Projectile.Launch(from, h, Weapon.Damage / salvo, _health.FactionId, Weapon.AllowFriendlyFire, Weapon.ProjectileSpeed,
                    Weapon.ProjectileSize, _health, Weapon, upgrade, i * 0.08f, salvo > 1 ? i : -1);
            ShotsFired++;
            Fired?.Invoke(h);
            return true;
        }

        public bool CanSee(ICombatTarget target) => !(target is Health h) || h == null || VisionSystem.CanSee(_health.FactionId, h);
    }
}
