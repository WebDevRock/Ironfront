using Ironfront.Core.Combat;
using Ironfront.Data;
using UnityEngine;

namespace Ironfront.Combat
{
    /// <summary>
    /// A visible shell that homes on its target and applies damage on arrival. If the target is destroyed
    /// first, the shell flies to the last aim point and only plays the impact.
    ///
    /// Milestone 007: with a <see cref="WeaponDefinition"/> the damage is scaled by the weapon's effectiveness
    /// against the victim's armour class and by research; ballistic weapons (rocket artillery) fly an arc to
    /// where the target was at launch, so moving targets can dodge, and splash weapons damage every hostile
    /// within their radius. Salvo rockets launch with a short stagger and spread.
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
        private WeaponDefinition _weapon;
        private float _multiplier = 1f;
        private float _delay;
        private bool _ballistic;
        private Vector3 _start, _impact;
        private float _flightTime, _arc;
        private Renderer _renderer;
        private float _smokeTimer;

        public static Projectile Launch(Vector3 from, Health target, float damage, int attackerFaction, bool allowFriendlyFire,
            float speed, float size, Health attacker = null) =>
            Launch(from, target, damage, attackerFaction, allowFriendlyFire, speed, size, attacker, null, 1f, 0f, -1);

        /// <param name="weapon">Effectiveness, splash and ballistic behaviour (null = plain homing shell).</param>
        /// <param name="damageMultiplier">Research bonus of the firing faction.</param>
        /// <param name="delay">Seconds before the shell leaves the launcher (salvo stagger).</param>
        /// <param name="salvoIndex">Position in a salvo (-1 = single shot), spreads ballistic impact points.</param>
        public static Projectile Launch(Vector3 from, Health target, float damage, int attackerFaction, bool allowFriendlyFire,
            float speed, float size, Health attacker, WeaponDefinition weapon, float damageMultiplier, float delay, int salvoIndex)
        {
            var visuals = CombatFx.Visuals;
            bool ballistic = weapon != null && weapon.Ballistic;
            var scale = ballistic ? new Vector3(size * 0.6f, size * 0.6f, size * 2.6f) : Vector3.one * size;
            var go = FxPool.Get(PrimitiveType.Sphere, "Projectile", from, scale, visuals != null ? visuals.Projectile : null);
            var p = go.GetComponent<Projectile>();
            if (p == null)
            {
                p = go.AddComponent<Projectile>();
                p.AddTracer(visuals);
            }
            p._renderer = go.GetComponent<Renderer>();
            if (p._trail != null)
            {
                p._trail.Clear();
                p._trail.widthMultiplier = size * (ballistic ? 0.6f : 0.9f);
                p._trail.time = ballistic ? 0.35f : 0.12f;
                p._trail.emitting = delay <= 0f;
            }
            p._age = 0f;
            p._inFlight = true;
            p._target = target;
            p._lastAimPoint = target.AimPoint(from);
            p._damage = damage;
            p._attackerFaction = attackerFaction;
            p._allowFriendlyFire = allowFriendlyFire;
            p._speed = speed;
            p._attacker = attacker;
            p._weapon = weapon;
            p._multiplier = damageMultiplier;
            p._delay = delay;
            p._ballistic = ballistic;
            if (ballistic)
            {
                p._start = from;
                Vector3 aim = target.transform.position;
                if (salvoIndex >= 0)
                {
                    float a = salvoIndex * 2.4f;
                    aim += new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (0.4f + 0.5f * salvoIndex);
                }
                aim.y = target.transform.position.y + 0.2f;
                p._impact = aim;
                float distance = Vector3.Distance(new Vector3(from.x, 0f, from.z), new Vector3(aim.x, 0f, aim.z));
                p._flightTime = Mathf.Max(0.3f, distance / Mathf.Max(1f, speed));
                p._arc = distance * 0.28f;
            }
            if (p._renderer != null) p._renderer.forceRenderingOff = delay > 0f;
            Alive++;
            return p;
        }

        private bool _inFlight;
        private TrailRenderer _trail;

        /// <summary>Glowing tracer trail (visual only; pooled with the shell).</summary>
        private void AddTracer(CombatVisuals visuals)
        {
            if (visuals == null || visuals.FxTracer == null || !global::Ironfront.Visuals.VisualFeatures.Trails) return;
            _trail = gameObject.AddComponent<TrailRenderer>();
            _trail.sharedMaterial = visuals.FxTracer;
            _trail.time = 0.12f;
            _trail.minVertexDistance = 0.3f;
            _trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _trail.receiveShadows = false;
            _trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.1f));
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.9f, 0.6f), 0f), new GradientColorKey(new Color(1f, 0.5f, 0.15f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            _trail.colorGradient = g;
        }

        private void OnDestroy()
        {
            if (_inFlight) Alive--;
        }

        private void Finish()
        {
            _inFlight = false;
            if (_trail != null) _trail.emitting = false;
            _target = null;
            _attacker = null;
            _weapon = null;
            Alive--;
            FxPool.Release(gameObject);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Alive = 0;

        private void Update()
        {
            using var perfScope = Ironfront.Diagnostics.PerfMarkers.Projectiles.Auto();
            if (!_inFlight) return;
            float dt = Time.deltaTime;
            if (_delay > 0f)
            {
                _delay -= dt;
                if (_delay > 0f) return;
                if (_trail != null) { _trail.Clear(); _trail.emitting = true; }
                if (_ballistic && _attacker != null && _attacker.IsAlive) CombatFx.MuzzleFlash(_start, Vector3.up, 0.8f);
            }
            _age += dt;
            if (_renderer != null) _renderer.forceRenderingOff = !Ironfront.Vision.VisionSystem.PlayerCanSee(transform.position);

            if (_ballistic)
            {
                FlyArc();
                return;
            }

            if (_target != null && _target.IsAlive) _lastAimPoint = _target.AimPoint(transform.position);
            Vector3 toAim = _lastAimPoint - transform.position;
            float step = _speed * dt;
            if (toAim.magnitude <= step || _age >= MaxLifetime)
            {
                transform.position = _lastAimPoint;
                if (_weapon != null && _weapon.SplashRadius > 0f) Splash(_lastAimPoint);
                else if (_target != null && _target.IsAlive) Hit(_target, 1f);
                CombatFx.Impact(_lastAimPoint, transform.localScale.x * 3.5f);
                Finish();
                return;
            }
            Vector3 dir = toAim.normalized;
            transform.position += dir * step;
            transform.rotation = Quaternion.LookRotation(dir);
        }

        /// <summary>Ballistic flight to a fixed impact point (where the target was at launch), with a smoke trail.</summary>
        private void FlyArc()
        {
            float t = Mathf.Clamp01(_age / _flightTime);
            Vector3 p = Vector3.Lerp(_start, _impact, t) + Vector3.up * (4f * _arc * t * (1f - t));
            Vector3 d = p - transform.position;
            if (d.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(d.normalized);
            transform.position = p;
            _smokeTimer -= Time.deltaTime;
            if (_smokeTimer <= 0f)
            {
                _smokeTimer = 0.06f;
                Ironfront.Visuals.FxLibrary.Smoke(p, Vector3.up * 0.2f, 0.35f, 0.7f, 0.45f, 1f);
            }
            if (t < 1f) return;
            Splash(_impact);
            CombatFx.Explosion(_impact, 1.6f);
            Finish();
        }

        private void Splash(Vector3 at)
        {
            float radius = _weapon != null ? _weapon.SplashRadius : 0f;
            if (radius <= 0f)
            {
                if (_target != null && _target.IsAlive && Vector3.Distance(_target.AimPoint(at), at) < 1.5f) Hit(_target, 1f);
                return;
            }
            var all = Health.All;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                var h = all[i];
                if (h == null || !h.IsAlive) continue;
                if (!DamageRules.CanDamage(_attackerFaction, h.FactionId, _allowFriendlyFire)) continue;
                Vector3 p = h.AimPoint(at);
                p.y = at.y;
                float factor = WeaponClassRules.SplashFactor(Vector3.Distance(p, at), radius);
                if (factor > 0f) Hit(h, factor);
            }
        }

        private void Hit(Health victim, float factor)
        {
            var m = _weapon != null ? _weapon.Effectiveness : ClassMultipliers.One;
            float raw = WeaponClassRules.RawDamage(_damage * factor, m, victim.ArmourClass, _multiplier);
            victim.ApplyDamage(raw, _attackerFaction, _allowFriendlyFire, _attacker != null && _attacker.IsAlive ? _attacker : null);
        }
    }
}
