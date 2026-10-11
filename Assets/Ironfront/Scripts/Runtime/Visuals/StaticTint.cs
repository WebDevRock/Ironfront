using UnityEngine;

namespace Ironfront.Visuals
{
    /// <summary>
    /// Fixed livery colour for decorative models outside a match (main-menu diorama), where there is no faction
    /// owner. Same property-block approach as FactionColorizer.
    /// </summary>
    public class StaticTint : MonoBehaviour
    {
        [SerializeField] private Color color = Color.white;
        [SerializeField] private Renderer[] renderers = new Renderer[0];
        [SerializeField] private Renderer[] emissiveRenderers = new Renderer[0];
        [SerializeField] private float emissionIntensity = 2.5f;

        private void Awake()
        {
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", color);
            foreach (var r in renderers) if (r != null) r.SetPropertyBlock(block);
            var glow = new MaterialPropertyBlock();
            glow.SetColor("_EmissionColor", color * emissionIntensity);
            foreach (var r in emissiveRenderers) if (r != null) r.SetPropertyBlock(glow);
        }
    }
}
