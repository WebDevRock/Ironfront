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

        private Health _target;
        private Vector3 _lastAimPoint;
        private float _damage;
        private int _attackerFaction;
        private bool _allowFriendlyFire;
        private float _speed;
        private float _age;

        public static Projectile Launch(Vector3 from, Health target, float damage, int attackerFaction, bool allowFriendlyFire,
            float speed, float size)
        {
            var visuals = CombatFx.Visuals;
            var go = CombatFx.Primitive(PrimitiveType.Sphere, "Projectile", from, Vector3.one * size,
                visuals != null ? visuals.Projectile : null);
            var p = go.AddComponent<Projectile>();
            p._target = target;
            p._lastAimPoint = target.AimPoint(from);
            p._damage = damage;
            p._attackerFaction = attackerFaction;
            p._allowFriendlyFire = allowFriendlyFire;
            p._speed = speed;
            return p;
        }

        private void Update()
        {
            _age += Time.deltaTime;
            if (_target != null && _target.IsAlive) _lastAimPoint = _target.AimPoint(transform.position);

            Vector3 toAim = _lastAimPoint - transform.position;
            float step = _speed * Time.deltaTime;
            if (toAim.magnitude <= step || _age >= MaxLifetime)
            {
                transform.position = _lastAimPoint;
                if (_target != null && _target.IsAlive) _target.ApplyDamage(_damage, _attackerFaction, _allowFriendlyFire);
                CombatFx.Impact(_lastAimPoint);
                Destroy(gameObject);
                return;
            }
            transform.position += toAim.normalized * step;
        }
    }
}
