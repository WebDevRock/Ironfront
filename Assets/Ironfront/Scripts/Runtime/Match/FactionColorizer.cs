using UnityEngine;

namespace Ironfront.Match
{
    /// <summary>Tints the listed renderers with the owning faction's colour (from MatchSetup).</summary>
    public class FactionColorizer : MonoBehaviour
    {
        [SerializeField] private Renderer[] renderers = new Renderer[0];

        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        private void Start() => Apply();

        public void Apply()
        {
            var match = MatchController.Current;
            if (match == null || match.Setup == null) return;
            var block = new MaterialPropertyBlock();
            block.SetColor(BaseColor, match.Setup.ColorOf(FactionMember.Of(this)));
            foreach (var r in renderers)
                if (r != null) r.SetPropertyBlock(block);
        }
    }
}
