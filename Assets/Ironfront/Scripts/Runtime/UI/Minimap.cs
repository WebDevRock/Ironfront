using Ironfront.Buildings;
using Ironfront.CameraControl;
using Ironfront.Combat;
using Ironfront.Commands;
using Ironfront.Core;
using Ironfront.Economy;
using Ironfront.Match;
using Ironfront.Selection;
using Ironfront.Units;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Ironfront.UI
{
    /// <summary>
    /// Top-down map of the whole battlefield: terrain obstacles, ore fields, structures (footprints) and units
    /// in faction colours, selected units highlighted, the camera's view outline and blinking alert pings.
    /// Left-click/drag centres the camera; right-click orders the selection to move there (or attack-move while
    /// the attack-move cursor is active). Redrawn a few times per second, not every frame.
    /// </summary>
    public class Minimap : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        [SerializeField] private RawImage image;
        [SerializeField] private RtsCameraController cameraController;
        [SerializeField] private Camera viewCamera;
        [SerializeField] private UnitCommandController commands;
        [SerializeField] private OrderInputController orders;
        [SerializeField] private int resolution = 160;
        [SerializeField] private float refreshInterval = 0.2f;
        [Tooltip("Layers treated as static terrain obstacles.")]
        [SerializeField] private LayerMask obstacleLayers = 1;

        [Tooltip("Map background and terrain colours (Milestone 008: per battlefield).")]
        [SerializeField] private Color groundColor = new Color32(52, 66, 44, 255);
        [SerializeField] private Color obstacleColor = new Color32(105, 108, 115, 255);
        private static readonly Color32 Ore = new Color32(240, 190, 40, 255);
        private static readonly Color32 Selected = new Color32(255, 255, 255, 255);
        private static readonly Color32 View = new Color32(230, 236, 245, 255);
        private static readonly Color32 Alert = new Color32(255, 70, 50, 255);

        private Texture2D _texture;
        private Color32[] _background;
        private Color32[] _pixels;
        private Rect _world;
        private float _timer;
        private readonly Vector3[] _corners = new Vector3[4];
        private static readonly Vector2[] ViewportCorners = { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };

        public Rect WorldRect => _world;
        public Texture2D Texture => _texture;
        public int Redraws { get; private set; }

        private void Start()
        {
            if (cameraController == null) cameraController = FindAnyObjectByType<RtsCameraController>();
            if (viewCamera == null) viewCamera = Camera.main;
            if (commands == null) commands = FindAnyObjectByType<UnitCommandController>();
            if (orders == null) orders = FindAnyObjectByType<OrderInputController>();

            var min = cameraController != null ? cameraController.BoundsMin : new Vector2(-50f, -50f);
            var max = cameraController != null ? cameraController.BoundsMax : new Vector2(50f, 50f);
            _world = Rect.MinMaxRect(min.x, min.y, max.x, max.y);

            _texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "Minimap",
            };
            _background = new Color32[resolution * resolution];
            _pixels = new Color32[_background.Length];
            PaintBackground();
            if (image != null) image.texture = _texture;
            Redraw();
        }

        private void OnDestroy()
        {
            if (_texture != null) Destroy(_texture);
        }

        private void Update()
        {
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = refreshInterval;
            Redraw();
        }

        private void PaintBackground()
        {
            for (int i = 0; i < _background.Length; i++) _background[i] = (Color32)groundColor;
            foreach (var c in FindObjectsByType<Collider>())
            {
                if (((1 << c.gameObject.layer) & obstacleLayers) == 0) continue;
                if (c.GetComponentInParent<Building>() != null || c.GetComponentInParent<Health>() != null) continue;
                var b = c.bounds;
                if (b.size.x > _world.width * 0.9f && b.size.z > _world.height * 0.9f) continue; // the ground itself
                FillRect(_background, b.min, b.max, (Color32)obstacleColor);
            }
        }

        /// <summary>Redraws dynamic content into the texture (called on a timer; public for tests).</summary>
        public void Redraw()
        {
            if (_texture == null) return;
            System.Array.Copy(_background, _pixels, _pixels.Length);
            var setup = MatchController.Current != null ? MatchController.Current.Setup : null;

            // Indexed loops: foreach over the IReadOnlyList registries would box an enumerator per redraw.
            int player = Ironfront.Vision.VisionSystem.LocalFaction;
            var deposits = OreDeposit.All;
            for (int i = 0; i < deposits.Count; i++)
            {
                var d = deposits[i];
                if (d.IsDepleted || !Ironfront.Vision.VisionSystem.IsExplored(player, d.transform.position)) continue;
                var r = d.HarvestRadius * 0.8f;
                FillRect(_pixels, d.transform.position - new Vector3(r, 0f, r), d.transform.position + new Vector3(r, 0f, r), Ore);
            }

            var buildings = Building.All;
            for (int i = 0; i < buildings.Count; i++)
            {
                var b = buildings[i];
                if (b.Definition == null || !Ironfront.Vision.VisionEntity.IsDrawn(b)) continue; // enemy structures once seen
                var f = b.Footprint.Body;
                Color32 c = setup != null ? (Color32)setup.ColorOf(b.FactionId) : (Color32)Color.gray;
                FillRect(_pixels, new Vector3(f.xMin, 0f, f.yMin), new Vector3(f.xMax, 0f, f.yMax), c);
                if (b.IsSelected) OutlineRect(new Vector3(f.xMin, 0f, f.yMin), new Vector3(f.xMax, 0f, f.yMax), Selected);
            }

            var units = SelectableUnit.All;
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (!Ironfront.Vision.VisionEntity.IsDrawn(u)) continue; // enemy units only while in sight
                var p = MinimapMath.WorldToPixel(u.transform.position, _world, resolution);
                Color32 c = u.IsSelected ? Selected : setup != null ? (Color32)setup.ColorOf(FactionMember.Of(u)) : (Color32)Color.gray;
                Plot(p.x, p.y, c);
                Plot(p.x + 1, p.y, c);
                Plot(p.x, p.y + 1, c);
                Plot(p.x + 1, p.y + 1, c);
            }

            ApplyFog(player);
            DrawCameraView();
            DrawAlert();

            _texture.SetPixels32(_pixels);
            _texture.Apply(false);
            Redraws++;
        }

        /// <summary>Fog of war on the minimap: unexplored nearly black, explored but not in sight dimmed.</summary>
        private void ApplyFog(int faction)
        {
            var sys = Ironfront.Vision.VisionSystem.Current;
            if (sys == null || !sys.FogEnabled) return;
            var grid = sys.Grid;
            float sx = _world.width / resolution, sz = _world.height / resolution;
            for (int y = 0; y < resolution; y++)
            {
                float wz = _world.yMin + (y + 0.5f) * sz;
                for (int x = 0; x < resolution; x++)
                {
                    byte state = grid.StateAt(faction, _world.xMin + (x + 0.5f) * sx, wz);
                    if (state == Ironfront.Core.Vision.VisibilityGrid.Visible) continue;
                    int i = y * resolution + x;
                    var c = _pixels[i];
                    if (state == Ironfront.Core.Vision.VisibilityGrid.Unexplored) _pixels[i] = new Color32(8, 10, 12, 255);
                    else _pixels[i] = new Color32((byte)(c.r * 0.5f), (byte)(c.g * 0.5f), (byte)(c.b * 0.55f), 255);
                }
            }
        }

        private void DrawCameraView()
        {
            if (viewCamera == null) return;
            var corners = _corners;
            for (int i = 0; i < 4; i++)
            {
                if (!MinimapMath.RayToGround(viewCamera.ViewportPointToRay(ViewportCorners[i]), out corners[i]))
                    corners[i] = viewCamera.transform.position + viewCamera.transform.forward * 200f; // horizon: clamp far
                // Clamp to the map so the outline stays visible when the view extends past an edge.
                corners[i].x = Mathf.Clamp(corners[i].x, _world.xMin, _world.xMax);
                corners[i].z = Mathf.Clamp(corners[i].z, _world.yMin, _world.yMax);
            }
            for (int i = 0; i < 4; i++) Line(corners[i], corners[(i + 1) % 4], View);
        }

        private void DrawAlert()
        {
            var center = NotificationCenter.Current;
            if (center == null || !center.LastAlertPosition.HasValue) return;
            float age = Time.unscaledTime - center.LastAlertTime;
            if (age > 5f || ((int)(age * 4f)) % 2 == 1) return; // blink for 5 s
            var p = MinimapMath.WorldToPixel(center.LastAlertPosition.Value, _world, resolution);
            int r = 4 + (int)(age * 2f) % 3;
            for (int a = 0; a < 24; a++)
            {
                float t = a / 24f * Mathf.PI * 2f;
                Plot(p.x + Mathf.RoundToInt(Mathf.Cos(t) * r), p.y + Mathf.RoundToInt(Mathf.Sin(t) * r), Alert);
            }
        }

        // ---------- Input ----------

        public void OnPointerDown(PointerEventData eventData) => HandlePointer(eventData);

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left) HandlePointer(eventData);
        }

        private void HandlePointer(PointerEventData eventData)
        {
            if (image == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(image.rectTransform, eventData.position,
                    eventData.pressEventCamera, out var local)) return;
            var rect = image.rectTransform.rect;
            var uv = new Vector2((local.x - rect.xMin) / rect.width, (local.y - rect.yMin) / rect.height);
            var world = MinimapMath.UvToWorld(uv, _world);

            if (eventData.button == PointerEventData.InputButton.Left)
            {
                if (orders != null && orders.IsTargeting && commands != null)
                {
                    commands.IssueAttackMove(world);
                    orders.CancelTargeting();
                }
                else if (cameraController != null) cameraController.Focus(world);
            }
            else if (eventData.button == PointerEventData.InputButton.Right && commands != null)
            {
                commands.IssueMove(world);
            }
        }

        /// <summary>World position for a normalised minimap coordinate (tests, tooling).</summary>
        public Vector3 UvToWorld(Vector2 uv) => MinimapMath.UvToWorld(uv, _world);

        // ---------- Pixel helpers ----------

        private void Plot(int x, int y, Color32 c)
        {
            if (x < 0 || y < 0 || x >= resolution || y >= resolution) return;
            _pixels[y * resolution + x] = c;
        }

        private void FillRect(Color32[] target, Vector3 min, Vector3 max, Color32 c)
        {
            var a = MinimapMath.WorldToPixel(min, _world, resolution);
            var b = MinimapMath.WorldToPixel(max, _world, resolution);
            for (int y = a.y; y <= b.y; y++)
            for (int x = a.x; x <= b.x; x++)
                target[y * resolution + x] = c;
        }

        private void OutlineRect(Vector3 min, Vector3 max, Color32 c)
        {
            Line(min, new Vector3(max.x, 0f, min.z), c);
            Line(new Vector3(max.x, 0f, min.z), max, c);
            Line(max, new Vector3(min.x, 0f, max.z), c);
            Line(new Vector3(min.x, 0f, max.z), min, c);
        }

        private void Line(Vector3 from, Vector3 to, Color32 c)
        {
            var a = MinimapMath.WorldToUv(from, _world) * resolution;
            var b = MinimapMath.WorldToUv(to, _world) * resolution;
            int steps = Mathf.Clamp(Mathf.CeilToInt(Vector2.Distance(a, b)), 1, resolution * 3);
            for (int i = 0; i <= steps; i++)
            {
                var p = Vector2.Lerp(a, b, i / (float)steps);
                Plot(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y), c);
            }
        }
    }
}
