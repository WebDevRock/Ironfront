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
            CurrentResult = Validate(Pending, CurrentPosition, false, PlayerFactionId);
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

        public Rect MapBounds => mapBounds;

        /// <summary>Checks the world-space centre for <paramref name="definition"/> against all placement rules.</summary>
        public PlacementResult Validate(BuildingDefinition definition, Vector3 center, bool facesSouth = false)
        {
            var structures = new List<Footprint>();
            foreach (var b in Building.All)
                if (b.Definition != null) structures.Add(b.Footprint);
            return Validate(definition, center, facesSouth, structures, includeUnits: true);
        }

        /// <summary>
        /// Milestone 010: the full rules for <paramref name="factionId"/> — construction territory first (the whole
        /// footprint inside the faction's territory and clear of known enemy exclusion zones), then the terrain, structure,
        /// resource, unit and navigation rules. Used by the player's placement tool, the computer commander and TryPlace.
        /// </summary>
        public PlacementResult Validate(BuildingDefinition definition, Vector3 center, bool facesSouth, int factionId)
        {
            var territory = ValidateTerritory(definition, center, facesSouth, factionId);
            return territory != PlacementResult.Valid ? territory : Validate(definition, center, facesSouth);
        }

        /// <summary>Territory part of placement: map bounds, own territory, known enemy exclusion.</summary>
        public PlacementResult ValidateTerritory(BuildingDefinition definition, Vector3 center, bool facesSouth, int factionId)
        {
            var fp = definition.FootprintAt(center, facesSouth);
            if (!PlacementRules.Contains(mapBounds, fp.Body) || (fp.HasApron && !PlacementRules.Contains(mapBounds, fp.Apron)))
                return PlacementResult.OutOfBounds;
            if (TerritorySystem.InKnownEnemyZone(factionId, fp, out _)) return PlacementResult.EnemyTerritory;
            if (!TerritorySystem.Covers(factionId, fp)) return PlacementResult.OutsideTerritory;
            return PlacementResult.Valid;
        }

        /// <summary>
        /// Milestone 010: can a Forward Command Centre (<paramref name="definition"/>) be deployed at
        /// <paramref name="center"/> by <paramref name="factionId"/>? Own territory is not required; known enemy exclusion
        /// zones, terrain, structures, ore, other units (not <paramref name="ignore"/>, the deploying vehicle) and level,
        /// walkable ground under the whole footprint are.
        /// </summary>
        public DeployCheck ValidateDeployment(BuildingDefinition definition, Vector3 center, int factionId, GameObject ignore, bool includeUnits = true)
        {
            var fp = definition.FootprintAt(center);
            if (!PlacementRules.Contains(mapBounds, fp.Body)) return DeployCheck.OutOfBounds;
            if (TerritorySystem.InKnownEnemyZone(factionId, fp, out _)) return DeployCheck.EnemyTerritory;

            var structures = new List<Footprint>();
            foreach (var b in Building.All)
                if (b.Definition != null) structures.Add(b.Footprint);
            Rect query = PlacementRules.Expand(fp.Body, Mathf.Max(clearance, resourceClearance));
            var units = new List<Rect>();
            if (includeUnits && unitLayers != 0)
            {
                var c = new Vector3(query.center.x, 5f, query.center.y);
                foreach (var col in Physics.OverlapBox(c, new Vector3(query.width * 0.5f, 10f, query.height * 0.5f), Quaternion.identity, unitLayers, QueryTriggerInteraction.Ignore))
                {
                    if (ignore != null && col.transform.IsChildOf(ignore.transform)) continue;
                    var bb = col.bounds;
                    units.Add(new Rect(bb.min.x, bb.min.z, bb.size.x, bb.size.z));
                }
            }
            var result = PlacementRules.Evaluate(fp, mapBounds, structures, OverlapRects(query, obstacleLayers), OverlapRects(query, resourceLayers),
                units, clearance, resourceClearance);
            if (result != PlacementResult.Valid) return DeployProcess.FromPlacement(result);

            // Level, walkable ground under the centre and every corner (slopes, holes, obstacle carvings).
            float tolerance = TerritorySettings.Current.DeployHeightTolerance;
            if (!NavMesh.SamplePosition(center, out var mid, 1.5f, NavMesh.AllAreas)) return DeployCheck.UnevenGround;
            var r = fp.Body;
            foreach (var corner in new[] { new Vector3(r.xMin + 0.5f, 0f, r.yMin + 0.5f), new Vector3(r.xMax - 0.5f, 0f, r.yMin + 0.5f),
                         new Vector3(r.xMin + 0.5f, 0f, r.yMax - 0.5f), new Vector3(r.xMax - 0.5f, 0f, r.yMax - 0.5f) })
            {
                var probe = new Vector3(corner.x, mid.position.y, corner.z);
                if (!NavMesh.SamplePosition(probe, out var hit, 1.5f, NavMesh.AllAreas)) return DeployCheck.UnevenGround;
                if (new Vector2(hit.position.x - probe.x, hit.position.z - probe.z).magnitude > 1f) return DeployCheck.UnevenGround;
                if (Mathf.Abs(hit.position.y - mid.position.y) > tolerance) return DeployCheck.UnevenGround;
            }
            return DeployCheck.Valid;
        }

        /// <summary>
        /// Milestone 010: places a deployed structure (Forward Command Centre) for <paramref name="factionId"/> without
        /// charging — the vehicle was paid for. The same deployment validation runs here, so nothing bypasses it.
        /// </summary>
        public bool PlaceDeployed(BuildingDefinition definition, Vector3 center, int factionId, GameObject vehicle, out Building placed, out DeployCheck check)
        {
            placed = null;
            check = definition == null || definition.Prefab == null ? DeployCheck.Busy
                : ValidateDeployment(definition, center, factionId, vehicle, includeUnits: false);
            if (check != DeployCheck.Valid) return false;
            var go = FactionSpawner.Spawn(definition.Prefab, new Vector3(center.x, 0f, center.z), Quaternion.identity, factionId);
            go.name = definition.DisplayName;
            placed = go.GetComponent<Building>();
            BuildingPlaced?.Invoke(placed);
            AnyBuildingPlaced?.Invoke(placed);
            return true;
        }

        /// <summary>
        /// Map validation (Milestone 008): the same rules against terrain, resources and the given planned structures
        /// only (existing buildings and units are ignored). Works in Edit Mode on an unplayed scene.
        /// </summary>
        public PlacementResult ValidateTerrain(BuildingDefinition definition, Vector3 center, bool facesSouth, IReadOnlyList<Footprint> planned) =>
            Validate(definition, center, facesSouth, planned, includeUnits: false);

        private PlacementResult Validate(BuildingDefinition definition, Vector3 center, bool facesSouth, IReadOnlyList<Footprint> structures, bool includeUnits)
        {
            var candidate = definition.FootprintAt(center, facesSouth);

            // Query a region that covers the padded body and the apron.
            Rect query = PlacementRules.Expand(candidate.Body, Mathf.Max(clearance, resourceClearance));
            if (candidate.HasApron) query = Union(query, candidate.Apron);

            var result = PlacementRules.Evaluate(candidate, mapBounds, structures,
                OverlapRects(query, obstacleLayers), OverlapRects(query, resourceLayers),
                includeUnits ? OverlapRects(query, unitLayers) : new List<Rect>(),
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
        /// <param name="facesSouth">Rotate the building 180Â° so its front (dock/exit) faces âˆ’Z. The player's
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

            var result = Validate(definition, center, facesSouth, factionId);
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
            // Defences show their reach while being placed (Milestone 007).
            if (def.Weapon != null)
                Ironfront.Visuals.RangeIndicator.CreateRing(_ghost.transform, def.Weapon.Range + Mathf.Max(def.FootprintSize.x, def.FootprintSize.y) * 0.5f,
                    Ironfront.Visuals.RangeIndicator.MaxColor);
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
