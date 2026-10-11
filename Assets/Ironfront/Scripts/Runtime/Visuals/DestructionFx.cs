using Ironfront.Audio;
using Ironfront.Buildings;
using Ironfront.Combat;
using UnityEngine;

namespace Ironfront.Visuals
{
    /// <summary>
    /// Destruction presentation, driven by <see cref="Health.Died"/>. Structures get a staggered multi-blast
    /// sequence and a smoke column; vehicles leave a charred, burning wreck that sinks into the ground after a
    /// few seconds. The wreck is pure decoration: no collider, no Health, never selectable or targetable.
    /// (Health itself still plays the basic explosion for every death.)
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class DestructionFx : MonoBehaviour
    {
        [Tooltip("Mesh parts copied into the wreck (vehicles only).")]
        [SerializeField] private MeshFilter[] wreckParts = new MeshFilter[0];

        private Health _health;

        private void Awake()
        {
            _health = GetComponent<Health>();
            _health.Died += OnDied;
        }

        private void OnDestroy()
        {
            if (_health != null) _health.Died -= OnDied;
        }

        private void OnDied(Health h)
        {
            var building = GetComponent<Building>();
            if (building != null && building.Definition != null)
            {
                var size = building.Definition.FootprintSize;
                float height = GetComponent<Collider>() is BoxCollider box ? box.size.y : 3f;
                if (Ironfront.Vision.VisionSystem.PlayerCanSee(transform.position)) FxLibrary.StructureDestroyed(transform.position, size, height);
                GameAudio.Play(SoundId.StructureDestroyed, transform.position);
                return;
            }
            GameAudio.Play(wreckParts.Length > 0 && h.Max >= 500f ? SoundId.ExplosionLarge : SoundId.ExplosionSmall, transform.position);
            SpawnWreck();
        }

        private void SpawnWreck()
        {
            var visuals = CombatFx.Visuals;
            if (!VisualFeatures.Wrecks || wreckParts.Length == 0 || visuals == null || visuals.Debris == null) return;
            if (!Ironfront.Vision.VisionSystem.PlayerCanSee(transform.position)) return; // deaths in the fog leave no visible trace
            var wreck = new GameObject(name + "_Wreck") { layer = 2 };
            wreck.transform.SetPositionAndRotation(transform.position, transform.rotation);
            foreach (var part in wreckParts)
            {
                if (part == null || part.sharedMesh == null) continue;
                var go = new GameObject(part.name) { layer = 2 };
                go.transform.SetParent(wreck.transform, false);
                go.transform.position = part.transform.position;
                go.transform.rotation = part.transform.rotation * Quaternion.Euler(Random.Range(-4f, 4f), Random.Range(-15f, 15f), Random.Range(-4f, 4f));
                go.transform.localScale = part.transform.lossyScale;
                go.AddComponent<MeshFilter>().sharedMesh = part.sharedMesh;
                var r = go.AddComponent<MeshRenderer>();
                var mats = new Material[part.sharedMesh.subMeshCount];
                for (int i = 0; i < mats.Length; i++) mats[i] = visuals.Debris;
                r.sharedMaterials = mats;
            }
            wreck.AddComponent<WreckBurner>();
        }
    }
}
