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

        public static void MuzzleFlash(Vector3 position) =>
            Burst("MuzzleFlash", position, 0.4f, 0.9f, 0.08f, Visuals != null ? Visuals.MuzzleFlash : null);

        public static void Impact(Vector3 position) =>
            Burst("Impact", position, 0.3f, 1.4f, 0.25f, Visuals != null ? Visuals.Impact : null);

        public static void Explosion(Vector3 position, float size) =>
            Burst("Explosion", position, size * 0.3f, size, 0.6f, Visuals != null ? Visuals.Explosion : null);

        /// <summary>A sphere that grows from <paramref name="from"/> to <paramref name="to"/> scale and disappears.</summary>
        public static void Burst(string name, Vector3 position, float from, float to, float lifetime, Material material)
        {
            var go = Primitive(PrimitiveType.Sphere, name, position, Vector3.one * from, material);
            go.AddComponent<TransientEffect>().Init(Vector3.one * from, Vector3.one * to, lifetime);
        }

        /// <summary>A flat ring marker on the ground (order feedback).</summary>
        public static void GroundMarker(Vector3 position, float diameter, Material material, float lifetime = 0.8f)
        {
            var start = new Vector3(diameter, 0.02f, diameter);
            var end = new Vector3(diameter * 0.3f, 0.02f, diameter * 0.3f);
            var go = Primitive(PrimitiveType.Cylinder, "OrderMarker", position + Vector3.up * 0.05f, start, material);
            go.AddComponent<TransientEffect>().Init(start, end, lifetime);
        }
    }

    /// <summary>Scales an effect over its lifetime, then destroys it.</summary>
    public class TransientEffect : MonoBehaviour
    {
        private Vector3 _from, _to;
        private float _lifetime, _age;

        public void Init(Vector3 from, Vector3 to, float lifetime)
        {
            _from = from;
            _to = to;
            _lifetime = Mathf.Max(0.01f, lifetime);
        }

        private void Update()
        {
            _age += Time.deltaTime;
            float t = _age / _lifetime;
            if (t >= 1f)
            {
                Destroy(gameObject);
                return;
            }
            transform.localScale = Vector3.Lerp(_from, _to, t);
        }
    }
}
