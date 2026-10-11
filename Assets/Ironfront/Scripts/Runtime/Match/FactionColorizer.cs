using UnityEngine;

namespace Ironfront.Match
{
    /// <summary>
    /// Tints the listed renderers with the owning faction's colour (from MatchSetup). Livery renderers get the
    /// colour as their base colour; glow renderers (beacons, power coils) get it as emission.
    /// </summary>
    public class FactionColorizer : MonoBehaviour
    {
        [SerializeField] private Renderer[] renderers = new Renderer[0];
        [SerializeField] private Renderer[] emissiveRenderers = new Renderer[0];
        [SerializeField] private float emissionIntensity = 2.5f;

        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        private void Start() => Apply();

        public void Apply()
        {
            var match = MatchController.Current;
            if (match == null || match.Setup == null) return;
            var color = match.Setup.ColorOf(FactionMember.Of(this));
            var block = new MaterialPropertyBlock();
            block.SetColor(BaseColor, color);
            foreach (var r in renderers)
                if (r != null) r.SetPropertyBlock(block);
            if (emissiveRenderers.Length == 0) return;
            var glow = new MaterialPropertyBlock();
            glow.SetColor(EmissionColor, color * emissionIntensity);
            foreach (var r in emissiveRenderers)
                if (r != null) r.SetPropertyBlock(glow);
        }
    }
}
