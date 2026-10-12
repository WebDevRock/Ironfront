using Ironfront.Core.Vision;
using UnityEngine;

namespace Ironfront.Vision
{
    /// <summary>
    /// Presents the human player's fog of war. Each vision tick the player's visibility grid becomes a small
    /// single-channel texture (0 unexplored, 0.5 explored, 1 visible) that eases towards its new value so edges
    /// fade instead of popping. The texture and the map rectangle are published as global shader properties for
    /// the full-screen fog pass (Art/Shaders/FogOfWar.shader, added to the URP renderer) and are read by the
    /// minimap. Disabled (and the pass switched off) when the scene has no fog.
    /// </summary>
    public class FogOfWarView : MonoBehaviour
    {
        public static readonly int FogTexId = Shader.PropertyToID("_IronfrontFogTex");
        public static readonly int FogRectId = Shader.PropertyToID("_IronfrontFogRect");
        public static readonly int FogParamsId = Shader.PropertyToID("_IronfrontFogParams");

        [Tooltip("Darkness of never-explored ground (0..1).")]
        [SerializeField, Range(0f, 1f)] private float unexploredDarkness = 1f; // never-explored ground is black (shroud)
        [Tooltip("Darkness of explored ground that is not currently in sight.")]
        [SerializeField, Range(0f, 1f)] private float exploredDarkness = 0.48f;
        [SerializeField] private float fadeSpeed = 6f;

        private Texture2D _texture;
        private byte[] _states;
        private float[] _current;
        private byte[] _pixels;
        private VisibilityGrid _grid;
        private bool _dirty;

        public Texture2D Texture => _texture;

        private void Start()
        {
            var sys = VisionSystem.Current;
            if (sys == null || !sys.FogEnabled)
            {
                Shader.SetGlobalVector(FogParamsId, Vector4.zero);
                enabled = false;
                return;
            }
            _grid = sys.Grid;
            int n = _grid.Width * _grid.Height;
            _states = new byte[n];
            _current = new float[n];
            _pixels = new byte[n];
            _texture = new Texture2D(_grid.Width, _grid.Height, TextureFormat.R8, false)
            {
                name = "FogOfWar",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            sys.Updated += OnVisionUpdated;
            Refresh(snap: true);
            Shader.SetGlobalTexture(FogTexId, _texture);
            Shader.SetGlobalVector(FogRectId, new Vector4(_grid.MinX, _grid.MinZ, _grid.Width * _grid.CellSize, _grid.Height * _grid.CellSize));
            Shader.SetGlobalVector(FogParamsId, new Vector4(1f, unexploredDarkness, exploredDarkness, exploredDarkness));
        }

        private void OnDisable() => Shader.SetGlobalVector(FogParamsId, Vector4.zero);

        private void OnDestroy()
        {
            if (VisionSystem.Current != null) VisionSystem.Current.Updated -= OnVisionUpdated;
            if (_texture != null) Destroy(_texture);
        }

        private void OnVisionUpdated()
        {
            _grid.CopyStates(VisionSystem.LocalFaction, _states);
            _dirty = true;
        }

        private void Update()
        {
            if (_grid == null) return;
            if (_dirty || Time.frameCount % 2 == 0) Refresh(snap: false);
        }

        private void Refresh(bool snap)
        {
            if (snap) _grid.CopyStates(VisionSystem.LocalFaction, _states);
            float step = snap ? 1f : Mathf.Clamp01(Time.unscaledDeltaTime * 2f * fadeSpeed);
            bool changed = false;
            for (int i = 0; i < _states.Length; i++)
            {
                float target = _states[i] * 0.5f; // 0, 0.5, 1
                float v = _current[i];
                if (v == target) continue;
                v = Mathf.MoveTowards(v, target, step);
                _current[i] = v;
                _pixels[i] = (byte)(v * 255f);
                changed = true;
            }
            _dirty = false;
            if (!changed) return;
            _texture.SetPixelData(_pixels, 0);
            _texture.Apply(false);
        }
    }
}
