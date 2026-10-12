using System.Collections.Generic;
using Ironfront.Buildings;
using Ironfront.Construction;
using Ironfront.Core.Construction;
using Ironfront.Core.Territory;
using Ironfront.Data;
using Ironfront.Match;
using Ironfront.Selection;
using Ironfront.Units;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Ironfront.UI
{
    /// <summary>
    /// Construction territory overlay (Milestone 010), shown only while placing a structure or while a Mobile Command
    /// Vehicle is selected:
    /// <list type="bullet">
    /// <item>a translucent cyan fill over the player's buildable area (2 m cells, so overlapping circles never
    /// double-tint) and a bright ribbon along the outer edge of the union;</item>
    /// <item>red ribbons around the exclusion zones of enemy command structures the player has <b>seen</b> (never
    /// unseen ones, so the overlay reveals nothing);</item>
    /// <item>with an MCV selected: the Forward Command Centre footprint at the vehicle (cyan valid / red invalid) and the
    /// territory it would grant.</item>
    /// </list>
    /// Meshes are rebuilt only when the territory changes or a new enemy structure becomes known. They are drawn by
    /// <see cref="TerritoryOverlayPass"/> after the fog-of-war pass, so they stay readable over dark, unexplored ground
    /// (they never show anything about the enemy that has not been seen). Installs itself next to the placement controller.
    /// </summary>
    public class TerritoryOverlay : MonoBehaviour
    {
        private const float EdgeWidth = 0.6f, Lift = 0.12f;

        private BuildingPlacementController _placement;
        private SelectionManager _selection;
        private GameObject _root;
        private MeshFilter _fill, _edge, _enemy, _deploy, _deployRing;
        private MeshRenderer _deployRenderer;
        private int _builtVersion = -1, _builtKnown = -1;
        private float _checkTimer;
        private readonly List<TerritoryAnchor> _anchors = new List<TerritoryAnchor>();
        private readonly List<(Mesh, Material)> _draws = new List<(Mesh, Material)>();

        public bool IsShown => _root != null && _root.activeSelf;
        /// <summary>Last deployment check of the selected MCV (for the HUD and tests).</summary>
        public DeployCheck DeployPreview { get; private set; } = DeployCheck.Valid;
        public MobileCommandVehicle PreviewVehicle { get; private set; }
        /// <summary>Overlay mesh sizes (tests): fill cells and edge segments of the last rebuild.</summary>
        public int FillCells { get; private set; }
        public int EdgeSegments { get; private set; }
        public int EnemySegments { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Ensure();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Ensure();

        public static TerritoryOverlay Ensure()
        {
            var existing = FindAnyObjectByType<TerritoryOverlay>();
            if (existing != null) return existing;
            var placement = FindAnyObjectByType<BuildingPlacementController>();
            if (placement == null) return null;
            return placement.gameObject.AddComponent<TerritoryOverlay>();
        }

        private void Start()
        {
            _placement = GetComponent<BuildingPlacementController>();
            _selection = FindAnyObjectByType<SelectionManager>();
            var s = TerritorySettings.Current;
            _root = new GameObject("TerritoryOverlay");
            _fill = Layer("Fill", s.FillMaterial);
            _edge = Layer("Edge", s.EdgeMaterial);
            _enemy = Layer("EnemyEdge", s.EnemyEdgeMaterial);
            _deployRing = Layer("DeployTerritory", s.EdgeMaterial);
            _deploy = Layer("DeployFootprint", s.EdgeMaterial);
            _deployRenderer = _deploy.GetComponent<MeshRenderer>();
            _root.SetActive(false);
        }

        private void OnDestroy()
        {
            TerritoryOverlayPass.Clear();
            if (_root != null) Destroy(_root);
        }

        private void OnDisable() => TerritoryOverlayPass.Clear();

        /// <summary>Hands the visible layers to the post-fog render pass for this frame.</summary>
        private void SubmitDraws(bool deploy)
        {
            _draws.Clear();
            void Add(MeshFilter f) { if (f != null && f.gameObject.activeSelf) _draws.Add((f.sharedMesh, f.GetComponent<MeshRenderer>().sharedMaterial)); }
            Add(_fill);
            Add(_enemy);
            Add(_edge);
            if (deploy) { Add(_deployRing); Add(_deploy); }
            TerritoryOverlayPass.Submit(_draws);
        }

        private MeshFilter Layer(string name, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = new Mesh { name = "Territory" + name, indexFormat = IndexFormat.UInt32 };
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.enabled = false; // drawn after the fog by TerritoryOverlayPass, not in the normal transparent queue
            return mf;
        }

        private int Player => MatchController.Current != null ? MatchController.Current.PlayerFactionId : MatchController.DefaultPlayerFactionId;

        private MobileCommandVehicle SelectedVehicle()
        {
            if (_selection == null) return null;
            foreach (var u in _selection.Selection.Items)
            {
                if (u == null) continue;
                var m = u.GetComponent<MobileCommandVehicle>();
                if (m != null && m.FactionId == Player) return m;
            }
            return null;
        }

        private void Update()
        {
            if (_root == null) return;
            var mcv = SelectedVehicle();
            bool show = (_placement != null && _placement.IsPlacing) || mcv != null;
            if (_root.activeSelf != show) _root.SetActive(show);
            PreviewVehicle = mcv;
            if (!show)
            {
                TerritoryOverlayPass.Clear();
                return;
            }

            int known = KnownEnemyAnchors();
            var map = TerritorySystem.Map;
            if (map.Version != _builtVersion || known != _builtKnown) Rebuild(map, known);

            _deploy.gameObject.SetActive(mcv != null);
            _deployRing.gameObject.SetActive(mcv != null);
            SubmitDraws(mcv != null);
            if (mcv == null) return;
            _checkTimer -= Time.unscaledDeltaTime;
            if (_checkTimer <= 0f)
            {
                _checkTimer = 0.25f;
                DeployPreview = mcv.IsDeploying ? DeployCheck.Valid : mcv.CheckSite();
                var s = TerritorySettings.Current;
                _deployRenderer.sharedMaterial = DeployPreview == DeployCheck.Valid ? s.EdgeMaterial : s.EnemyEdgeMaterial;
                var def = mcv.DeploysInto;
                if (def != null)
                {
                    var c = mcv.SiteCentre;
                    var size = def.FootprintSize;
                    BuildRect(_deploy.sharedMesh, new Rect(c.x - size.x * 0.5f, c.z - size.y * 0.5f, size.x, size.y));
                    BuildRing(_deployRing.sharedMesh, new Vector2(c.x, c.z), def.TerritoryRadius, dashed: true);
                }
            }
        }

        private int KnownEnemyAnchors()
        {
            int n = 0, player = Player;
            foreach (var b in TerritorySystem.Anchors)
                if (b.FactionId != player && TerritorySystem.IsKnownTo(player, b)) n++;
            return n;
        }

        /// <summary>Rebuilds the fill and edge meshes (public for tests).</summary>
        public void Rebuild() => Rebuild(TerritorySystem.Map, KnownEnemyAnchors());

        private void Rebuild(TerritoryMap map, int known)
        {
            _builtVersion = map.Version;
            _builtKnown = known;
            int player = Player;
            map.CopyAnchors(player, _anchors);
            float cell = TerritorySettings.Current.OverlayCell;

            // Fill: one quad per covered cell.
            var v = new List<Vector3>();
            var t = new List<int>();
            if (_anchors.Count > 0)
            {
                float xMin = float.MaxValue, xMax = float.MinValue, zMin = float.MaxValue, zMax = float.MinValue;
                foreach (var a in _anchors)
                {
                    xMin = Mathf.Min(xMin, a.Centre.x - a.Radius); xMax = Mathf.Max(xMax, a.Centre.x + a.Radius);
                    zMin = Mathf.Min(zMin, a.Centre.y - a.Radius); zMax = Mathf.Max(zMax, a.Centre.y + a.Radius);
                }
                xMin = Mathf.Floor(xMin / cell) * cell;
                zMin = Mathf.Floor(zMin / cell) * cell;
                for (float x = xMin; x < xMax; x += cell)
                    for (float z = zMin; z < zMax; z += cell)
                    {
                        var c = new Vector2(x + cell * 0.5f, z + cell * 0.5f);
                        if (!map.Covers(player, c)) continue;
                        float y = GroundY(c.x, c.y) + Lift * 0.5f;
                        int i0 = v.Count;
                        v.Add(new Vector3(x, y, z)); v.Add(new Vector3(x, y, z + cell)); v.Add(new Vector3(x + cell, y, z + cell)); v.Add(new Vector3(x + cell, y, z));
                        t.Add(i0); t.Add(i0 + 1); t.Add(i0 + 2); t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 3);
                    }
            }
            FillCells = v.Count / 4;
            Apply(_fill.sharedMesh, v, t);

            // Edge: arcs of each circle not inside another of the player's circles.
            v.Clear(); t.Clear();
            EdgeSegments = 0;
            foreach (var a in _anchors)
                EdgeSegments += AddArc(v, t, a.Centre, a.Radius, p =>
                {
                    foreach (var o in _anchors)
                        if (o.Id != a.Id && (p - o.Centre).sqrMagnitude < (o.Radius - 0.05f) * (o.Radius - 0.05f)) return false;
                    return true;
                }, dashed: false);
            Apply(_edge.sharedMesh, v, t);

            // Known enemy exclusion zones.
            v.Clear(); t.Clear();
            EnemySegments = 0;
            foreach (var b in TerritorySystem.Anchors)
            {
                if (b.FactionId == player || !TerritorySystem.IsKnownTo(player, b) || b.Definition.ExclusionRadius <= 0f) continue;
                var p = b.transform.position;
                EnemySegments += AddArc(v, t, new Vector2(p.x, p.z), b.Definition.ExclusionRadius, _ => true, dashed: false);
            }
            Apply(_enemy.sharedMesh, v, t);
        }

        private void BuildRing(Mesh mesh, Vector2 centre, float radius, bool dashed)
        {
            var v = new List<Vector3>();
            var t = new List<int>();
            if (radius > 0f) AddArc(v, t, centre, radius, _ => true, dashed);
            Apply(mesh, v, t);
        }

        private void BuildRect(Mesh mesh, Rect r)
        {
            var v = new List<Vector3>();
            var t = new List<int>();
            var corners = new[] { new Vector2(r.xMin, r.yMin), new Vector2(r.xMin, r.yMax), new Vector2(r.xMax, r.yMax), new Vector2(r.xMax, r.yMin) };
            for (int i = 0; i < 4; i++) AddSegment(v, t, corners[i], corners[(i + 1) % 4]);
            Apply(mesh, v, t);
        }

        private int AddArc(List<Vector3> v, List<int> t, Vector2 c, float radius, System.Func<Vector2, bool> include, bool dashed)
        {
            int segments = Mathf.Clamp(Mathf.CeilToInt(2f * Mathf.PI * radius / 1.5f), 24, 512);
            int n = 0;
            for (int i = 0; i < segments; i++)
            {
                if (dashed && i % 2 == 1) continue;
                float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments;
                var p0 = c + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * radius;
                var p1 = c + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * radius;
                if (!include((p0 + p1) * 0.5f)) continue;
                AddSegment(v, t, p0, p1);
                n++;
            }
            return n;
        }

        private static void AddSegment(List<Vector3> v, List<int> t, Vector2 p0, Vector2 p1)
        {
            var dir = (p1 - p0).normalized;
            var side = new Vector2(-dir.y, dir.x) * EdgeWidth * 0.5f;
            float y0 = GroundY(p0.x, p0.y) + Lift, y1 = GroundY(p1.x, p1.y) + Lift;
            int i0 = v.Count;
            v.Add(new Vector3(p0.x - side.x, y0, p0.y - side.y));
            v.Add(new Vector3(p0.x + side.x, y0, p0.y + side.y));
            v.Add(new Vector3(p1.x + side.x, y1, p1.y + side.y));
            v.Add(new Vector3(p1.x - side.x, y1, p1.y - side.y));
            t.Add(i0); t.Add(i0 + 1); t.Add(i0 + 2); t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 3);
        }

        private static readonly int GroundMask = 1 << 8; // "Ground" layer

        private static float GroundY(float x, float z) =>
            Physics.Raycast(new Vector3(x, 60f, z), Vector3.down, out var hit, 120f, GroundMask, QueryTriggerInteraction.Ignore) ? hit.point.y : 0f;

        private static void Apply(Mesh mesh, List<Vector3> v, List<int> t)
        {
            mesh.Clear();
            mesh.SetVertices(v);
            // Both windings so the overlay is visible regardless of the camera side (it lies flat on the ground).
            var both = new List<int>(t.Count * 2);
            both.AddRange(t);
            for (int i = 0; i < t.Count; i += 3) { both.Add(t[i]); both.Add(t[i + 2]); both.Add(t[i + 1]); }
            mesh.SetTriangles(both, 0);
            mesh.RecalculateBounds();
        }
    }
}
