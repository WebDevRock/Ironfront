using Ironfront.Data;
using Ironfront.Match;
using UnityEngine;

namespace Ironfront.Combat
{
    /// <summary>
    /// Spawns simple primitive-based combat effects using the materials in <see cref="CombatVisuals"/>.
    /// Placeholder visuals by design; effects never have colliders.
    /// </summary>
    public static class CombatFx
    {
        public static CombatVisuals Visuals =>
            MatchController.Current != null && MatchController.Current.Setup != null ? MatchController.Current.Setup.CombatVisuals : null;

        public static GameObject Primitive(PrimitiveType type, string name, Vector3 position, Vector3 scale, Material material)
        {
            using var perfScope = Ironfront.Diagnostics.PerfMarkers.FxSpawn.Auto();
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.Destroy(go.GetComponent<Collider>());
            go.layer = 2; // Ignore Raycast
            go.transform.position = position;
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (material != null) r.sharedMaterial = material;
            return go;
        }

        /// <summary>True when the particle library (Milestone 006) is configured; otherwise the sphere placeholders are used.</summary>
        private static bool HasParticles => Visuals != null && Visuals.FxSmoke != null;

        public static void MuzzleFlash(Vector3 position) => MuzzleFlash(position, Vector3.forward, 1f);

        public static void MuzzleFlash(Vector3 position, Vector3 forward, float scale)
        {
            if (HasParticles) global::Ironfront.Visuals.FxLibrary.MuzzleFlash(position, forward, scale);
            else Burst("MuzzleFlash", position, 0.4f, 0.9f, 0.08f, Visuals != null ? Visuals.MuzzleFlash : null);
        }

        public static void Impact(Vector3 position) => Impact(position, 1f);

        public static void Impact(Vector3 position, float scale)
        {
            global::Ironfront.Audio.GameAudio.Play(global::Ironfront.Audio.SoundId.Impact, position);
            if (HasParticles) global::Ironfront.Visuals.FxLibrary.Impact(position, scale);
            else Burst("Impact", position, 0.3f, 1.4f, 0.25f, Visuals != null ? Visuals.Impact : null);
        }

        public static void Explosion(Vector3 position, float size)
        {
            if (HasParticles) global::Ironfront.Visuals.FxLibrary.Explosion(position, size * 0.8f);
            else Burst("Explosion", position, size * 0.3f, size, 0.6f, Visuals != null ? Visuals.Explosion : null);
        }

        /// <summary>A sphere that grows from <paramref name="from"/> to <paramref name="to"/> scale and disappears (pooled).</summary>
        public static void Burst(string name, Vector3 position, float from, float to, float lifetime, Material material)
        {
            var go = FxPool.Get(PrimitiveType.Sphere, name, position, Vector3.one * from, material);
            Transient(go).Init(Vector3.one * from, Vector3.one * to, lifetime);
        }

        private static TransientEffect Transient(GameObject go)
        {
            var effect = go.GetComponent<TransientEffect>();
            return effect != null ? effect : go.AddComponent<TransientEffect>();
        }

        /// <summary>A flat ring marker on the ground (order feedback).</summary>
        public static void GroundMarker(Vector3 position, float diameter, Material material, float lifetime = 0.8f)
        {
            var start = new Vector3(diameter, 0.02f, diameter);
            var end = new Vector3(diameter * 0.3f, 0.02f, diameter * 0.3f);
            var go = FxPool.Get(PrimitiveType.Cylinder, "OrderMarker", position + Vector3.up * 0.05f, start, material);
            Transient(go).Init(start, end, lifetime);
        }
    }

    /// <summary>Scales an effect over its lifetime, then returns it to the pool.</summary>
    public class TransientEffect : MonoBehaviour
    {
        private Vector3 _from, _to;
        private float _lifetime, _age;

        public void Init(Vector3 from, Vector3 to, float lifetime)
        {
            _from = from;
            _to = to;
            _lifetime = Mathf.Max(0.01f, lifetime);
            _age = 0f;
            transform.localScale = from;
        }

        private void Update()
        {
            _age += Time.deltaTime;
            float t = _age / _lifetime;
            if (t >= 1f)
            {
                FxPool.Release(gameObject);
                return;
            }
            transform.localScale = Vector3.Lerp(_from, _to, t);
        }
    }
}
