using System.Collections;
using Ironfront.Combat;
using Ironfront.Data;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ironfront.Visuals
{
    /// <summary>
    /// Shared particle effects (Milestone 006). One world-space ParticleSystem per effect type lives in the scene
    /// and effects are emitted into it with <see cref="ParticleSystem.Emit(ParticleSystem.EmitParams, int)"/>, so
    /// spawning an effect creates no GameObjects at all (the cheapest possible pooling). Created lazily on first use
    /// from the materials in <see cref="CombatVisuals"/>; when no match/visuals exist, every call is a no-op.
    ///
    /// Effects are purely presentational: gameplay code raises events (shot fired, hit, death...) and visual
    /// components call into this library. Nothing here reads or changes combat state.
    /// </summary>
    public class FxLibrary : MonoBehaviour
    {
        private static FxLibrary s_instance;

        private ParticleSystem _flash, _fire, _spark, _smoke, _dust, _scorch, _ring, _glint, _debris, _steam;
        private ParticleSystem.EmitParams _p;

        // View culling: effects well outside the camera view are skipped (planes cached once per frame).
        private readonly Plane[] _planes = new Plane[6];
        private int _planesFrame = -1;
        private Camera _camera;

        /// <summary>Effects skipped because they were off screen or over a particle budget (diagnostics).</summary>
        public static int Skipped { get; private set; }

        /// <summary>Particles emitted since load (diagnostics / tests).</summary>
        public static int Emitted { get; private set; }

        /// <summary>The scene's library, created on demand; null when no combat visuals are configured.</summary>
        public static FxLibrary Instance
        {
            get
            {
                if (s_instance != null) return s_instance;
                if (!VisualFeatures.Particles) return null;
                var visuals = CombatFx.Visuals;
                if (visuals == null || visuals.FxSmoke == null) return null;
                var go = new GameObject("FxLibrary") { layer = 2 };
                s_instance = go.AddComponent<FxLibrary>();
                s_instance.Build(visuals);
                return s_instance;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_instance = null;
            Emitted = 0;
            Skipped = 0;
        }

        /// <summary>True if a sphere around <paramref name="p"/> is inside (or near) the main camera's view.</summary>
        private bool InView(Vector3 p, float radius)
        {
            if (_planesFrame != Time.frameCount)
            {
                _planesFrame = Time.frameCount;
                if (_camera == null) _camera = Camera.main;
                if (_camera == null) return true;
                GeometryUtility.CalculateFrustumPlanes(_camera, _planes);
            }
            if (_camera == null) return true;
            for (int i = 0; i < 6; i++)
                if (_planes[i].GetDistanceToPoint(p) < -radius) return false;
            return true;
        }

        /// <summary>Gate for an effect at <paramref name="p"/>: visible, and the main system below its budget.</summary>
        private static FxLibrary Gate(Vector3 p, float radius, ParticleSystem budgetSystem = null, float budget = 1f)
        {
            var fx = Instance;
            if (fx == null) return null;
            if (!fx.InView(p, radius)) { Skipped++; return null; }
            var ps = budgetSystem ?? fx._smoke;
            if (budget < 1f && ps != null && ps.particleCount > ps.main.maxParticles * budget) { Skipped++; return null; }
            return fx;
        }

        // ---------- Construction ----------

        private void Build(CombatVisuals v)
        {
            var ground = new GameObject("GroundPlane").transform;
            ground.SetParent(transform, false);

            _scorch = Make("Scorch", v.FxScorch, 160, ParticleSystemRenderMode.HorizontalBillboard, 60f);
            Fade(_scorch, new[] { (0f, 1f), (0.85f, 1f), (1f, 0f) });

            _dust = Make("Dust", v.FxDust, 1500, ParticleSystemRenderMode.Billboard, 20f);
            Grow(_dust, 0.5f, 1.6f);
            Fade(_dust, new[] { (0f, 0f), (0.15f, 1f), (1f, 0f) });
            Drag(_dust, 0.12f);

            _smoke = Make("Smoke", v.FxSmoke, 2000, ParticleSystemRenderMode.Billboard, 10f);
            Grow(_smoke, 0.45f, 1.5f);
            Fade(_smoke, new[] { (0f, 0f), (0.1f, 1f), (0.6f, 0.7f), (1f, 0f) });
            Drag(_smoke, 0.25f);

            _steam = Make("Steam", v.FxSmoke, 400, ParticleSystemRenderMode.Billboard, 9f);
            Grow(_steam, 0.4f, 2.2f);
            Fade(_steam, new[] { (0f, 0f), (0.2f, 1f), (1f, 0f) });

            _fire = Make("Fire", v.FxFire, 1200, ParticleSystemRenderMode.Billboard, 0f);
            Grow(_fire, 0.6f, 1.2f);
            Tint(_fire, new Color(1f, 0.95f, 0.75f), new Color(1f, 0.55f, 0.15f), new Color(0.45f, 0.08f, 0.02f));
            Drag(_fire, 0.6f);

            _ring = Make("Shockwave", v.FxRing, 64, ParticleSystemRenderMode.HorizontalBillboard, 0f);
            Grow(_ring, 0.15f, 1f);
            Fade(_ring, new[] { (0f, 1f), (1f, 0f) });

            _flash = Make("Flash", v.FxFlash, 300, ParticleSystemRenderMode.Billboard, -5f);
            Grow(_flash, 1f, 0.3f);
            Fade(_flash, new[] { (0f, 1f), (1f, 0f) });

            _glint = Make("Glint", v.FxGlint, 300, ParticleSystemRenderMode.Billboard, -5f);
            Grow(_glint, 0.2f, 1f, 0.2f);
            Fade(_glint, new[] { (0f, 0f), (0.3f, 1f), (1f, 0f) });

            _spark = Make("Sparks", v.FxSpark, 1500, ParticleSystemRenderMode.Stretch, -5f);
            var sr = _spark.GetComponent<ParticleSystemRenderer>();
            sr.velocityScale = 0.05f;
            sr.lengthScale = 1.5f;
            var sm = _spark.main;
            sm.gravityModifier = 1.4f;
            Fade(_spark, new[] { (0f, 1f), (0.7f, 1f), (1f, 0f) });

            if (v.Debris != null)
            {
                _debris = Make("Debris", v.Debris, 500, ParticleSystemRenderMode.Mesh, 0f);
                var dr = _debris.GetComponent<ParticleSystemRenderer>();
                dr.mesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
                dr.shadowCastingMode = ShadowCastingMode.Off; // small and short-lived: not worth an extra shadow-pass draw in big fights
                var dm = _debris.main;
                dm.gravityModifier = 1.6f;
                dm.startRotation3D = true;
                var col = _debris.collision;
                col.enabled = true;
                col.type = ParticleSystemCollisionType.Planes;
                col.SetPlane(0, ground);
                col.dampen = 0.55f;
                col.bounce = 0.25f;
                col.lifetimeLoss = 0f;
                Grow(_debris, 1f, 1f, 0.6f);
            }
        }

        private ParticleSystem Make(string name, Material material, int max, ParticleSystemRenderMode mode, float sortingFudge)
        {
            var go = new GameObject(name) { layer = 2 };
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max;
            main.startSpeed = 0f;
            main.startLifetime = 1f;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = material;
            r.renderMode = mode;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortingFudge = sortingFudge;
            r.maxParticleSize = 4f;
            ps.Play();
            return ps;
        }

        private static void Grow(ParticleSystem ps, float from, float to, float startTime = 0f)
        {
            var s = ps.sizeOverLifetime;
            s.enabled = true;
            var curve = new AnimationCurve(new Keyframe(0f, from), new Keyframe(Mathf.Max(0.001f, startTime), from), new Keyframe(1f, to));
            if (startTime <= 0f) curve = new AnimationCurve(new Keyframe(0f, from, 0f, (to - from) * 2f), new Keyframe(1f, to, 0f, 0f));
            s.size = new ParticleSystem.MinMaxCurve(1f, curve);
        }

        private static void Fade(ParticleSystem ps, (float t, float a)[] alpha)
        {
            var c = ps.colorOverLifetime;
            c.enabled = true;
            var g = new Gradient();
            var keys = new GradientAlphaKey[alpha.Length];
            for (int i = 0; i < alpha.Length; i++) keys[i] = new GradientAlphaKey(alpha[i].a, alpha[i].t);
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, keys);
            c.color = g;
        }

        private static void Tint(ParticleSystem ps, Color a, Color b, Color c)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(a, 0f), new GradientColorKey(b, 0.35f), new GradientColorKey(c, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.9f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
        }

        private static void Drag(ParticleSystem ps, float drag)
        {
            var l = ps.limitVelocityOverLifetime;
            l.enabled = true;
            l.limit = 100f;
            l.drag = drag;
            l.multiplyDragByParticleSize = false;
        }

        // ---------- Emission ----------

        private void Emit(ParticleSystem ps, Vector3 position, Vector3 velocity, float size, float lifetime, Color color, float rotation = -1f)
        {
            if (ps == null) return;
            _p = default;
            _p.position = position;
            _p.velocity = velocity;
            _p.startSize = size;
            _p.startLifetime = lifetime;
            _p.startColor = color;
            _p.rotation = rotation < 0f ? Random.Range(0f, 360f) : rotation;
            _p.applyShapeToPosition = false;
            ps.Emit(_p, 1);
            Emitted++;
        }

        private static Vector3 Spread(float horizontal, float upMin, float upMax)
        {
            var d = Random.insideUnitCircle * horizontal;
            return new Vector3(d.x, Random.Range(upMin, upMax), d.y);
        }

        // ---------- Public effects ----------

        /// <summary>Gun flash, a lick of flame and a puff of propellant smoke along <paramref name="forward"/>.</summary>
        public static void MuzzleFlash(Vector3 position, Vector3 forward, float scale)
        {
            var fx = Gate(position, 4f);
            if (fx == null) return;
            fx.Emit(fx._flash, position + forward * 0.15f * scale, forward * 0.5f, 1.1f * scale, 0.07f, new Color(1f, 0.85f, 0.55f));
            for (int i = 0; i < 2; i++)
                fx.Emit(fx._fire, position + forward * (0.2f + i * 0.2f) * scale, forward * (4f + i * 2f), 0.35f * scale, 0.1f, Color.white);
            if (fx._smoke.particleCount < fx._smoke.main.maxParticles * 0.6f)
                fx.Emit(fx._smoke, position + forward * 0.3f, forward * Random.Range(1f, 2.2f) + Spread(0.3f, 0.2f, 0.5f),
                    0.5f * scale, Random.Range(0.7f, 1.0f), new Color(0.72f, 0.7f, 0.66f, 0.45f));
        }

        /// <summary>Shell impact: flash, sparks, a smoke puff and ground dust.</summary>
        public static void Impact(Vector3 position, float scale)
        {
            var fx = Gate(position, 4f);
            if (fx == null) return;
            fx.Emit(fx._flash, position, Vector3.zero, 0.9f * scale, 0.07f, new Color(1f, 0.75f, 0.4f));
            int sparks = Mathf.RoundToInt(3 * scale) + 2;
            for (int i = 0; i < sparks; i++)
                fx.Emit(fx._spark, position, Random.onUnitSphere * Random.Range(3f, 7f) + Vector3.up * 2f, Random.Range(0.04f, 0.08f),
                    Random.Range(0.2f, 0.45f), new Color(1f, 0.7f, 0.3f));
            fx.Emit(fx._smoke, position, Spread(0.4f, 0.4f, 0.9f), 0.6f * scale, Random.Range(0.8f, 1.3f), new Color(0.35f, 0.33f, 0.3f, 0.6f));
            if (position.y < 1.2f && fx._dust.particleCount < fx._dust.main.maxParticles * 0.6f)
                fx.Emit(fx._dust, new Vector3(position.x, 0.2f, position.z), Spread(0.8f, 0.2f, 0.6f), 0.9f * scale, 1.0f,
                    new Color(0.55f, 0.48f, 0.36f, 0.5f));
        }

        /// <summary>Vehicle or generic explosion: fireball, smoke, sparks, debris, shockwave and a scorch mark.</summary>
        public static void Explosion(Vector3 position, float size)
        {
            var fx = Gate(position, size * 3f + 4f);
            if (fx == null) return;
            size = Mathf.Max(0.5f, size);
            fx.Emit(fx._flash, position, Vector3.zero, size * 1.8f, 0.14f, new Color(1f, 0.85f, 0.6f));
            int fire = 6 + Mathf.RoundToInt(size * 2f);
            for (int i = 0; i < fire; i++)
                fx.Emit(fx._fire, position + Random.insideUnitSphere * size * 0.25f, Random.onUnitSphere * size * 0.9f + Vector3.up * size * 0.5f,
                    size * Random.Range(0.45f, 0.75f), Random.Range(0.45f, 0.85f), Color.white);
            int smoke = 3 + Mathf.RoundToInt(size * 0.7f);
            for (int i = 0; i < smoke; i++)
            {
                float g = Random.Range(0.12f, 0.24f);
                fx.Emit(fx._smoke, position + Random.insideUnitSphere * size * 0.3f, Spread(size * 0.35f, 0.8f, 1.8f),
                    size * Random.Range(0.8f, 1.2f), Random.Range(1.8f, 2.8f), new Color(g, g * 0.95f, g * 0.9f, 0.85f));
            }
            int sparks = 8 + Mathf.RoundToInt(size * 2f);
            for (int i = 0; i < sparks; i++)
                fx.Emit(fx._spark, position, Random.onUnitSphere * Random.Range(5f, 12f) + Vector3.up * 3f, Random.Range(0.05f, 0.1f),
                    Random.Range(0.4f, 0.9f), new Color(1f, 0.65f, 0.25f));
            fx.Debris(position, 4 + Mathf.RoundToInt(size), size);
            fx.Emit(fx._ring, new Vector3(position.x, 0.15f, position.z), Vector3.zero, size * 2.6f, 0.35f, new Color(1f, 0.7f, 0.4f, 0.8f));
            fx.Emit(fx._scorch, new Vector3(position.x, 0.03f, position.z), Vector3.zero, size * 1.2f, 30f, new Color(1f, 1f, 1f, 0.85f));
            fx.Emit(fx._dust, new Vector3(position.x, 0.3f, position.z), Spread(size, 0.2f, 0.5f), size * 1.4f, 1.8f,
                new Color(0.52f, 0.46f, 0.36f, 0.55f));
        }

        private void Debris(Vector3 position, int count, float size)
        {
            if (_debris == null) return;
            for (int i = 0; i < count; i++)
            {
                _p = default;
                _p.position = position + Random.insideUnitSphere * size * 0.2f;
                _p.velocity = Spread(Random.Range(2f, 5f), 3f, 7f);
                _p.startSize = Random.Range(0.1f, 0.22f) * Mathf.Clamp(size / 3f, 0.6f, 2f);
                _p.startLifetime = Random.Range(1.6f, 2.6f);
                _p.startColor = Color.white;
                _p.rotation3D = new Vector3(Random.Range(0f, 360f), Random.Range(0f, 360f), Random.Range(0f, 360f));
                _p.angularVelocity3D = Random.insideUnitSphere * 400f;
                _p.applyShapeToPosition = false;
                _debris.Emit(_p, 1);
                Emitted++;
            }
        }

        /// <summary>Destruction of a structure: several staggered blasts across the footprint, then a smoke column.</summary>
        public static void StructureDestroyed(Vector3 center, Vector2 footprint, float height)
        {
            var fx = Instance;
            if (fx == null) return;
            fx.StartCoroutine(fx.StructureSequence(center, footprint, height));
        }

        private IEnumerator StructureSequence(Vector3 center, Vector2 footprint, float height)
        {
            float size = Mathf.Max(footprint.x, footprint.y);
            for (int i = 0; i < 5; i++)
            {
                var p = center + new Vector3(Random.Range(-0.4f, 0.4f) * footprint.x, Random.Range(0.3f, 1f) * height, Random.Range(-0.4f, 0.4f) * footprint.y);
                Explosion(p, size * Random.Range(0.3f, 0.45f));
                yield return new WaitForSeconds(Random.Range(0.12f, 0.3f));
            }
            Emit(_scorch, new Vector3(center.x, 0.035f, center.z), Vector3.zero, size * 1.5f, 45f, new Color(1f, 1f, 1f, 0.95f));
            Debris(center + Vector3.up * height * 0.5f, 18, size * 0.4f);
            float end = Time.time + 5f;
            while (Time.time < end)
            {
                var p = center + new Vector3(Random.Range(-0.35f, 0.35f) * footprint.x, 0.5f, Random.Range(-0.35f, 0.35f) * footprint.y);
                float g = Random.Range(0.1f, 0.2f);
                Emit(_smoke, p, Spread(0.4f, 1.2f, 2.2f), size * Random.Range(0.25f, 0.4f), Random.Range(3f, 4.5f), new Color(g, g, g, 0.8f));
                if (Random.value < 0.5f) Emit(_fire, p, Spread(0.3f, 0.8f, 1.6f), size * 0.18f, Random.Range(0.4f, 0.7f), Color.white);
                yield return new WaitForSeconds(0.08f);
            }
        }

        /// <summary>One smoke puff (damage, chimneys). <paramref name="grey"/> 0 = black, 1 = white.</summary>
        public static void Smoke(Vector3 position, Vector3 velocity, float size, float grey, float alpha = 0.7f, float lifetime = 2.5f)
        {
            // Ambient / damage smoke yields to combat smoke when the system is busy.
            var fx = Gate(position, size * 2f + 2f, null, 0.55f);
            if (fx == null) return;
            fx.Emit(fx._smoke, position, velocity, size, lifetime * Random.Range(0.8f, 1.2f), new Color(grey, grey * 0.97f, grey * 0.93f, alpha));
        }

        /// <summary>Light, slow white vapour (cooling towers).</summary>
        public static void Steam(Vector3 position, float size)
        {
            var fx = Gate(position, size * 3f + 2f);
            if (fx == null) return;
            fx.Emit(fx._steam, position + Random.insideUnitSphere * size * 0.2f, Spread(0.15f, 0.6f, 1.0f), size, Random.Range(3f, 4.5f),
                new Color(0.9f, 0.9f, 0.88f, 0.28f));
        }

        /// <summary>One flame tongue (burning wrecks and heavily damaged objects).</summary>
        public static void Flame(Vector3 position, float size)
        {
            var fx = Gate(position, size * 2f + 2f);
            if (fx == null) return;
            fx.Emit(fx._fire, position + Random.insideUnitSphere * size * 0.2f, Spread(0.2f, 0.8f, 1.5f), size * Random.Range(0.8f, 1.2f),
                Random.Range(0.35f, 0.6f), Color.white);
        }

        /// <summary>Track / wheel dust kicked up by a moving vehicle.</summary>
        public static void Dust(Vector3 position, Vector3 velocity, float size, float alpha = 0.35f)
        {
            var fx = Instance;
            if (fx == null) return;
            if (fx._dust.particleCount > fx._dust.main.maxParticles * 0.5f) { Skipped++; return; } // movement dust is lowest priority
            fx.Emit(fx._dust, position, velocity, size * Random.Range(0.8f, 1.2f), Random.Range(1f, 1.6f), new Color(0.56f, 0.49f, 0.37f, alpha));
        }

        /// <summary>A ring of dust (construction start / completion).</summary>
        public static void DustRing(Vector3 center, Vector2 footprint)
        {
            var fx = Instance;
            if (fx == null) return;
            int n = Mathf.RoundToInt((footprint.x + footprint.y) * 1.5f);
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                Vector3 edge;
                float px = footprint.x * 0.5f, pz = footprint.y * 0.5f, per = 2f * (footprint.x + footprint.y), d = t * per;
                if (d < footprint.x) edge = new Vector3(-px + d, 0f, -pz);
                else if ((d -= footprint.x) < footprint.y) edge = new Vector3(px, 0f, -pz + d);
                else if ((d -= footprint.y) < footprint.x) edge = new Vector3(px - d, 0f, pz);
                else edge = new Vector3(-px, 0f, pz - (d - footprint.x));
                var outward = new Vector3(edge.x, 0f, edge.z).normalized;
                fx.Emit(fx._dust, center + edge + Vector3.up * 0.3f, outward * Random.Range(1f, 2.5f) + Vector3.up * 0.4f,
                    Random.Range(1.2f, 2f), Random.Range(1.2f, 2f), new Color(0.6f, 0.53f, 0.4f, 0.55f));
            }
        }

        /// <summary>Ore sparkle (harvesting, unloading at a refinery, idle deposits).</summary>
        public static void Glint(Vector3 position, float size = 0.5f)
        {
            var fx = Gate(position, 2f);
            if (fx == null) return;
            fx.Emit(fx._glint, position, Vector3.up * 0.3f, size, Random.Range(0.4f, 0.7f), new Color(1f, 0.75f, 0.3f));
        }
    }
}
