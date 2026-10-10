using UnityEngine;

namespace Ironfront.Combat
{
    /// <summary>
    /// A visible shell that homes on its target and applies damage on arrival. If the target is destroyed
    /// first, the shell flies to the last aim point and only plays the impact.
    /// </summary>
    public class Projectile : MonoBehaviour
    {
        private const float MaxLifetime = 5f;

        /// <summary>Projectiles currently in flight (diagnostics).</summary>
        public static int Alive { get; private set; }

        private Health _target;
        private Health _attacker;
        private Vector3 _lastAimPoint;
        private float _damage;
        private int _attackerFaction;
        private bool _allowFriendlyFire;
        private float _speed;
        private float _age;

        public static Projectile Launch(Vector3 from, Health target, float damage, int attackerFaction, bool allowFriendlyFire,
            float speed, float size, Health attacker = null)
        {
            var visuals = CombatFx.Visuals;
            var go = FxPool.Get(PrimitiveType.Sphere, "Projectile", from, Vector3.one * size,
                visuals != null ? visuals.Projectile : null);
            var p = go.GetComponent<Projectile>();
            if (p == null) p = go.AddComponent<Projectile>();
            p._age = 0f;
            p._inFlight = true;
            p._target = target;
            p._lastAimPoint = target.AimPoint(from);
            p._damage = damage;
            p._attackerFaction = attackerFaction;
            p._allowFriendlyFire = allowFriendlyFire;
            p._speed = speed;
            p._attacker = attacker;
            Alive++;
            return p;
        }

        private bool _inFlight;

        private void OnDestroy()
        {
            if (_inFlight) Alive--;
        }

        private void Finish()
        {
            _inFlight = false;
            _target = null;
            _attacker = null;
            Alive--;
            FxPool.Release(gameObject);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Alive = 0;

        private void Update()
        {
            using var perfScope = Ironfront.Diagnostics.PerfMarkers.Projectiles.Auto();
            if (!_inFlight) return;
            _age += Time.deltaTime;
            if (_target != null && _target.IsAlive) _lastAimPoint = _target.AimPoint(transform.position);

            Vector3 toAim = _lastAimPoint - transform.position;
            float step = _speed * Time.deltaTime;
            if (toAim.magnitude <= step || _age >= MaxLifetime)
            {
                transform.position = _lastAimPoint;
                if (_target != null && _target.IsAlive)
                    _target.ApplyDamage(_damage, _attackerFaction, _allowFriendlyFire, _attacker != null && _attacker.IsAlive ? _attacker : null);
                CombatFx.Impact(_lastAimPoint);
                Finish();
                return;
            }
            transform.position += toAim.normalized * step;
        }
    }
}
