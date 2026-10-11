using UnityEngine;

namespace Ironfront.Vision
{
    /// <summary>
    /// "Last known" image of an enemy structure that was destroyed while the player could not see it. Purely
    /// visual (no collider, no Health, not targetable); it disappears as soon as the player sees the spot again,
    /// so stale intelligence is never shown as current.
    /// </summary>
    public class StructureGhost : MonoBehaviour
    {
        private float _timer;

        public static StructureGhost Create(VisionEntity source, Renderer[] renderers)
        {
            var root = new GameObject("Remembered_" + source.name) { layer = 2 };
            root.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            var block = new MaterialPropertyBlock();
            foreach (var r in renderers)
            {
                if (!(r is MeshRenderer mr) || r == null) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null || !r.gameObject.activeInHierarchy) continue;
                var go = new GameObject(r.name) { layer = 2 };
                go.transform.SetParent(root.transform, false);
                go.transform.SetPositionAndRotation(r.transform.position, r.transform.rotation);
                go.transform.localScale = r.transform.lossyScale;
                go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                var copy = go.AddComponent<MeshRenderer>();
                copy.sharedMaterials = mr.sharedMaterials;
                if (mr.HasPropertyBlock())
                {
                    mr.GetPropertyBlock(block);
                    copy.SetPropertyBlock(block);
                }
            }
            return root.AddComponent<StructureGhost>();
        }

        private void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = 0.25f;
            if (VisionSystem.PlayerCanSee(transform.position)) Destroy(gameObject);
        }
    }
}
