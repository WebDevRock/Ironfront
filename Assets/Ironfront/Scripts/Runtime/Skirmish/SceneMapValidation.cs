using System.Collections.Generic;
using Ironfront.Buildings;
using Ironfront.CameraControl;
using Ironfront.Combat;
using Ironfront.Construction;
using Ironfront.Core.Maps;
using Ironfront.Data;
using Ironfront.Economy;
using Ironfront.Match;
using Ironfront.Vision;
using UnityEngine;

namespace Ironfront.Skirmish
{
    /// <summary>
    /// Builds a <see cref="MapSpec"/> from the loaded skirmish scene and validates it (Milestone 008). Reads scene
    /// objects directly (not the runtime registries), so it works on an unplayed scene in Edit Mode — the normal way
    /// to run it, before any base exists. Conventions written by the map builder: ore fields whose name contains
    /// "Contested" are contested; empty objects named "Strategic_*" are strategic points.
    /// </summary>
    public static class SceneMapValidation
    {
        public const string ContestedTag = "Contested";
        public const string StrategicPrefix = "Strategic_";

        public static MapValidationReport ValidateLoadedScene(MapValidationThresholds thresholds = null, bool checkStoredTable = true)
        {
            var spec = ReadLoadedScene(out var probe, out string problem);
            if (spec != null) spec.CheckStoredTable = checkStoredTable;
            if (spec == null)
            {
                var r = new MapValidationReport { MapName = "(scene)" };
                r.Issues.Add(new MapIssue(IssueSeverity.Error, "scene", problem));
                return r;
            }
            return MapValidator.Validate(spec, probe, thresholds);
        }

        public static MapSpec ReadLoadedScene(out IMapProbe probe, out string problem)
        {
            probe = null;
            problem = null;
            var init = Object.FindAnyObjectByType<SkirmishInitializer>();
            var placement = Object.FindAnyObjectByType<BuildingPlacementController>();
            var match = Object.FindAnyObjectByType<MatchController>();
            if (init == null || placement == null || match == null || match.Setup == null)
            {
                problem = "not a skirmish map scene (needs SkirmishInitializer, BuildingPlacementController and MatchController)";
                return null;
            }

            var defs = new Dictionary<string, BuildingDefinition>();
            void AddDef(BuildingDefinition d) { if (d != null) defs[d.Id] = d; }
            foreach (var d in match.Setup.ConstructionOptions) AddDef(d);
            AddDef(DefinitionOf(init.HeadquartersPrefab));
            AddDef(DefinitionOf(init.RefineryPrefab));
            probe = new NavMeshMapProbe(placement, defs);

            float size = init.Map != null ? init.Map.Size : 100f;
            var spec = new MapSpec
            {
                Name = init.Map != null ? init.Map.DisplayName : "(unnamed map)",
                Bounds = new Rect(-size * 0.5f, -size * 0.5f, size, size),
            };
            if (init.Map != null) spec.SpawnRules = init.Map.SpawnRules;

            defs.TryGetValue(MapValidator.RefineryId, out var refineryDef);
            foreach (var slot in Object.FindObjectsByType<StartSlot>())
            {
                var s = new MapSpec.Slot
                {
                    Index = slot.Slot,
                    Name = slot.DisplayName,
                    StoredOpponents = new List<int>(slot.ValidOpponents).ToArray(),
                    Headquarters = XZ(slot.Headquarters),
                    Refinery = XZ(slot.Refinery),
                    Harvester = XZ(slot.Harvester),
                    FacesSouth = slot.FacesSouth,
                };
                s.RefineryDock = refineryDef != null ? DockOf(refineryDef, slot.Refinery, slot.FacesSouth) : s.Refinery;
                spec.Slots.Add(s);
            }
            spec.Slots.Sort((a, b) => a.Index.CompareTo(b.Index));

            foreach (var d in Object.FindObjectsByType<OreDeposit>())
            {
                spec.Deposits.Add(new MapSpec.Deposit
                {
                    Name = d.name,
                    Position = XZ(d.transform.position),
                    Amount = d.InitialAmount,
                    Radius = d.HarvestRadius,
                    Contested = d.name.Contains(ContestedTag),
                });
            }
            spec.Deposits.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

            foreach (var c in Object.FindObjectsByType<Collider>())
            {
                if (c.isTrigger || c.gameObject.layer != 0) continue; // terrain obstacles live on Default
                if (c.GetComponentInParent<Building>() != null || c.GetComponentInParent<Health>() != null) continue;
                var b = c.bounds;
                if (b.size.x > size * 0.9f && b.size.z > size * 0.9f) continue; // the ground itself
                var rect = new Rect(b.min.x, b.min.z, b.size.x, b.size.z);
                spec.Obstacles.Add(rect);
                const float inset = 0.8f;
                if (rect.width > inset * 2f + 0.2f && rect.height > inset * 2f + 0.2f)
                    spec.ObstacleCores.Add(new Rect(rect.xMin + inset, rect.yMin + inset, rect.width - inset * 2f, rect.height - inset * 2f));
            }

            foreach (var t in Object.FindObjectsByType<Transform>())
                if (t.name.StartsWith(StrategicPrefix)) spec.StrategicPoints.Add(XZ(t.position));

            var cam = Object.FindAnyObjectByType<RtsCameraController>();
            if (cam != null) spec.SystemBounds.Add(("camera", Rect.MinMaxRect(cam.BoundsMin.x, cam.BoundsMin.y, cam.BoundsMax.x, cam.BoundsMax.y)));
            spec.SystemBounds.Add(("placement", placement.MapBounds));
            var vision = Object.FindAnyObjectByType<VisionSystem>();
            if (vision != null) spec.SystemBounds.Add(("fog", new Rect(vision.MapMin, vision.MapSize)));
            return spec;
        }

        private static BuildingDefinition DefinitionOf(GameObject prefab) =>
            prefab != null && prefab.TryGetComponent<Building>(out var b) ? b.Definition : null;

        /// <summary>Centre of the refinery's apron (where harvesters unload).</summary>
        private static Vector2 DockOf(BuildingDefinition def, Vector3 center, bool facesSouth)
        {
            var fp = def.FootprintAt(center, facesSouth);
            return fp.HasApron ? fp.Apron.center : fp.Body.center;
        }

        private static Vector2 XZ(Vector3 p) => new Vector2(p.x, p.z);
    }
}
