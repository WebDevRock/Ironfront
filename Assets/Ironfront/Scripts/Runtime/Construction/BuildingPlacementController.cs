using System;
using System.Collections.Generic;
using Ironfront.Buildings;
using Ironfront.Core.Construction;
using Ironfront.Core.Economy;
using Ironfront.Data;
using Ironfront.InputBindings;
using Ironfront.Match;
using UnityEngine;
using UnityEngine.AI;

namespace Ironfront.Construction
{
    /// <summary>
    /// Placement mode for the construction menu: shows a ghost footprint under the pointer, coloured by
    /// validity, and builds the structure on left click. Right click or Escape cancels.
    /// </summary>
    public class BuildingPlacementController : MonoBehaviour
    {
        [SerializeField] private Camera viewCamera;
        [SerializeField] private LayerMask groundLayers = 0;
        [Tooltip("Static scenery that buildings must keep clear of.")]
        [SerializeField] private LayerMask obstacleLayers = 0;
        [SerializeField] private LayerMask resourceLayers = 0;
        [SerializeField] private LayerMask unitLayers = 0;
        [SerializeField] private Rect mapBounds = new Rect(-50f, -50f, 100f, 100f);
        [Tooltip("Minimum gap kept between a structure and other structures/obstacles, so units can pass.")]
        [SerializeField] private float clearance = 1.5f;
        [SerializeField] private float resourceClearance = 3f;
        [SerializeField] private float gridSize = 1f;
        [SerializeField] private Material validMaterial;
        [SerializeField] private Material invalidMaterial;

        private GameObject _ghost;
        private Renderer[] _ghostRenderers;
        private bool _hasPointerPosition;

        public BuildingDefinition Pending { get; private set; }
        public bool IsPlacing => Pending != null;
        public PlacementResult CurrentResult { get; private set; }
        public Vector3 CurrentPosition { get; private set; }

        /// <summary>Feedback for the HUD (why placement failed, what was built).</summary>
        public event Action<string> Message;

        /// <summary>Raised when placement mode starts or ends.</summary>
        public event Action PlacementModeChanged;

        public event Action<Building> BuildingPlaced;

        /// <summary>Raised for buildings placed by any controller / faction (notifications, statistics).</summary>
        public static event Action<Building> AnyBuildingPlaced;

        private int PlayerFactionId => MatchController.Current != null ? MatchController.Current.PlayerFactionId : MatchController.DefaultPlayerFactionId;

        private void Awake()
        {
            if (viewCamera == null) viewCamera = Camera.main;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => AnyBuildingPlaced = null;

        private void OnDisable()
        {
            if (IsPlacing) CancelPlacement();
        }

        /// <summary>Enters placement mode for the player's faction. Returns false if it cannot be built now.</summary>
        public bool BeginPlacement(BuildingDefinition definition)
        {
            if (definition == null) return false;
            var match = MatchController.Current;
            if (match == null) return false;

            var check = ConstructionRules.Check(definition, match.PlayerFaction);
            if (check != BuildCheck.Ok)
            {
                Message?.Invoke($"{definition.DisplayName}: {Describe(check, definition)}");
                return false;
            }

            if (IsPlacing) DestroyGhost();
            Pending = definition;
            CreateGhost(definition);
            RtsInputMode.IsPlacingBuilding = true;
            PlacementModeChanged?.Invoke();
            return true;
        }

        public void CancelPlacement()
        {
            if (!IsPlacing) return;
            EndPlacement();
        }

        private void EndPlacement()
        {
            Pending = null;
            DestroyGhost();
            RtsInputMode.IsPlacingBuilding = false;
            PlacementModeChanged?.Invoke();
        }

        private void Update()
        {
            if (!IsPlacing) return;

            var cancel = RtsInput.Cancel;
            var command = RtsInput.Command;
            if ((cancel != null && cancel.WasPressedThisFrame()) || (command != null && command.WasPressedThisFrame()))
            {
                RtsInputMode.ConsumePointerThisFrame();
                CancelPlacement();
                return;
            }

            Vector3 ground = default;
            _hasPointerPosition = !RtsInput.PointerOverUI() && TryGetGroundPoint(RtsInput.PointerPosition, out ground);
            if (_ghost != null) _ghost.SetActive(_hasPointerPosition);
            if (!_hasPointerPosition) return;

            Vector2 snapped = PlacementRules.Snap(new Vector2(ground.x, ground.z), gridSize);
            CurrentPosition = new Vector3(snapped.x, 0f, snapped.y);
            CurrentResult = Validate(Pending, CurrentPosition);
            UpdateGhost();

            var select = RtsInput.Select;
            if (select != null && select.WasPressedThisFrame())
            {
                RtsInputMode.ConsumePointerThisFrame();
                if (TryPlace(Pending, CurrentPosition, PlayerFactionId, out _, out string reason))
                    EndPlacement();
                else
                    Message?.Invoke(reason);
            }
        }

        /// <summary>Checks the world-space centre for <paramref name="definition"/> against all placement rules.</summary>
        public PlacementResult Validate(BuildingDefinition definition, Vector3 center, bool facesSouth = false)
        {
            var candidate = definition.FootprintAt(center, facesSouth);

            var structures = new List<Footprint>();
            foreach (var b in Building.All)
                if (b.Definition != null) structures.Add(b.Footprint);

            // Query a region that covers the padded body and the apron.
            Rect query = PlacementRules.Expand(candidate.Body, Mathf.Max(clearance, resourceClearance));
            if (candidate.HasApron) query = Union(query, candidate.Apron);

            var result = PlacementRules.Evaluate(candidate, mapBounds, structures,
                OverlapRects(query, obstacleLayers), OverlapRects(query, resourceLayers), OverlapRects(query, unitLayers),
                clearance, resourceClearance);
            if (result != PlacementResult.Valid) return result;

            // The site must be on walkable ground (not inside a baked obstacle hole).
            if (!NavMesh.SamplePosition(center, out var hit, 1f, NavMesh.AllAreas)) return PlacementResult.OverlapsObstacle;
            if (candidate.HasApron &&
                !NavMesh.SamplePosition(candidate.Apron.center.ToXZ(), out hit, 1f, NavMesh.AllAreas))
                return PlacementResult.BlocksAccess;

            return PlacementResult.Valid;
        }

        /// <summary>
        /// Validates, charges and spawns a structure for <paramref name="factionId"/>. Credits are only spent
        /// when the structure is actually placed.
        /// </summary>
        /// <param name="facesSouth">Rotate the building 180° so its front (dock/exit) faces −Z. The player's
        /// placement tool always builds facing north; the computer commander faces its buildings towards its opponent.</param>
        public bool TryPlace(BuildingDefinition definition, Vector3 center, int factionId, out Building placed, out string reason,
            bool facesSouth = false)
        {
            placed = null;
            var match = MatchController.Current;
            if (match == null || definition == null || definition.Prefab == null)
            {
                reason = "Construction unavailable";
                return false;
            }

            var faction = match.GetFaction(factionId);
            var check = ConstructionRules.Check(definition, faction);
            if (check != BuildCheck.Ok)
            {
                reason = $"{definition.DisplayName}: {Describe(check, definition)}";
                return false;
            }

            var result = Validate(definition, center, facesSouth);
            if (result != PlacementResult.Valid)
            {
                reason = PlacementRules.Describe(result);
                return false;
            }

            if (ConstructionRules.TryPurchase(definition, faction) != BuildCheck.Ok)
            {
                reason = $"{definition.DisplayName}: insufficient credits";
                return false;
            }

            var go = FactionSpawner.Spawn(definition.Prefab, new Vector3(center.x, 0f, center.z), BuildingDefinition.Facing(facesSouth), factionId);
            go.name = definition.DisplayName;
            placed = go.GetComponent<Building>();

            reason = $"{definition.DisplayName} constructed (-{definition.Cost})";
            if (factionId == PlayerFactionId) Message?.Invoke(reason); // computer factions build silently
            BuildingPlaced?.Invoke(placed);
            AnyBuildingPlaced?.Invoke(placed);
            return true;
        }

        public bool TryGetGroundPoint(Vector2 screenPosition, out Vector3 point)
        {
            point = default;
            if (viewCamera == null) return false;
            if (!Physics.Raycast(viewCamera.ScreenPointToRay(screenPosition), out var hit, 500f, groundLayers, QueryTriggerInteraction.Ignore))
                return false;
            point = hit.point;
            return true;
        }

        private static string Describe(BuildCheck check, BuildingDefinition def)
        {
            switch (check)
            {
                case BuildCheck.MissingPrerequisite:
                    var names = new List<string>();
                    foreach (var p in def.Prerequisites) if (p != null) names.Add(p.DisplayName);
                    return "requires " + string.Join(", ", names);
                case BuildCheck.InsufficientFunds: return $"insufficient credits (needs {def.Cost})";
                default: return "ok";
            }
        }

        private static List<Rect> OverlapRects(Rect area, LayerMask mask)
        {
            var rects = new List<Rect>();
            if (mask == 0) return rects;
            var center = new Vector3(area.center.x, 5f, area.center.y);
            var half = new Vector3(area.width * 0.5f, 10f, area.height * 0.5f);
            foreach (var c in Physics.OverlapBox(center, half, Quaternion.identity, mask, QueryTriggerInteraction.Ignore))
            {
                var b = c.bounds;
                rects.Add(new Rect(b.min.x, b.min.z, b.size.x, b.size.z));
            }
            return rects;
        }

        private static Rect Union(Rect a, Rect b) =>
            Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));

        // ---- Ghost ----

        private void CreateGhost(BuildingDefinition def)
        {
            _ghost = new GameObject("PlacementGhost");
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            Destroy(body.GetComponent<Collider>());
            body.transform.SetParent(_ghost.transform, false);
            body.transform.localScale = new Vector3(def.FootprintSize.x, 2f, def.FootprintSize.y);
            body.transform.localPosition = new Vector3(0f, 1f, 0f);

            if (def.ApronDepth > 0f)
            {
                var apron = GameObject.CreatePrimitive(PrimitiveType.Cube);
                apron.name = "Apron";
                Destroy(apron.GetComponent<Collider>());
                apron.transform.SetParent(_ghost.transform, false);
                apron.transform.localScale = new Vector3(def.FootprintSize.x, 0.1f, def.ApronDepth);
                apron.transform.localPosition = new Vector3(0f, 0.05f, def.FootprintSize.y * 0.5f + def.ApronDepth * 0.5f);
            }

            _ghostRenderers = _ghost.GetComponentsInChildren<Renderer>();
            foreach (var r in _ghostRenderers) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _ghost.SetActive(false);
        }

        private void UpdateGhost()
        {
            if (_ghost == null) return;
            _ghost.transform.position = CurrentPosition;
            var mat = CurrentResult == PlacementResult.Valid ? validMaterial : invalidMaterial;
            if (mat == null) return;
            foreach (var r in _ghostRenderers) r.sharedMaterial = mat;
        }

        private void DestroyGhost()
        {
            if (_ghost != null) Destroy(_ghost);
            _ghost = null;
            _ghostRenderers = null;
        }
    }

    internal static class Vector2Extensions
    {
        public static Vector3 ToXZ(this Vector2 v) => new Vector3(v.x, 0f, v.y);
    }
}
