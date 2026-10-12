using System;
using System.Collections.Generic;
using System.Text;
using Ironfront.Core.Skirmish;
using UnityEngine;

namespace Ironfront.Core.Maps
{
    /// <summary>
    /// What the validator needs to ask about a battlefield (Milestone 008). Points are on the ground plane (x, z).
    /// Implemented over the real NavMesh / physics / placement rules for scenes, and over a grid for tests.
    /// </summary>
    public interface IMapProbe
    {
        /// <summary>Is there walkable ground within <paramref name="tolerance"/> metres of the point?</summary>
        bool IsWalkable(Vector2 p, float tolerance);

        /// <summary>Ground path length between two points, each snapped to walkable ground within its radius; +∞ if unreachable.</summary>
        float PathLength(Vector2 from, Vector2 to, float snapFrom, float snapTo);

        /// <summary>
        /// Would a structure of this type pass building placement at <paramref name="center"/>, given the map terrain
        /// and the structures in <paramref name="placed"/> only (nothing else is assumed to be built)?
        /// </summary>
        bool CanPlace(string structureId, Vector2 center, bool facesSouth, IReadOnlyList<PlannedStructure> placed);
    }

    public readonly struct PlannedStructure
    {
        public readonly string Id;
        public readonly Vector2 Center;
        public readonly bool FacesSouth;

        public PlannedStructure(string id, Vector2 center, bool facesSouth)
        {
            Id = id;
            Center = center;
            FacesSouth = facesSouth;
        }
    }

    /// <summary>A battlefield as the validator sees it.</summary>
    public sealed class MapSpec
    {
        public sealed class Slot
        {
            public int Index;
            public string Name = "";
            public Vector2 Headquarters, Refinery, Harvester;
            /// <summary>Opponent slots the scene stores for this slot (checked against the rules).</summary>
            public int[] StoredOpponents;
            /// <summary>Where harvesters unload (refinery front).</summary>
            public Vector2 RefineryDock;
            public bool FacesSouth;
        }

        public sealed class Deposit
        {
            public string Name;
            public Vector2 Position;
            public int Amount;
            public float Radius = 4f;
            /// <summary>Intended to be fought over: both slots must reach it at a similar distance.</summary>
            public bool Contested;
        }

        public string Name = "Map";
        public Rect Bounds;
        public SpawnPairRules SpawnRules = new SpawnPairRules();
        /// <summary>Compare each slot's stored opponents with the rules (false while the builder computes them).</summary>
        public bool CheckStoredTable = true;
        public readonly List<Slot> Slots = new List<Slot>();
        public readonly List<Deposit> Deposits = new List<Deposit>();
        /// <summary>Solid interiors of terrain obstacles (should hold no walkable ground).</summary>
        public readonly List<Rect> ObstacleCores = new List<Rect>();
        /// <summary>Full obstacle extents (excluded from the walkable-coverage sample).</summary>
        public readonly List<Rect> Obstacles = new List<Rect>();
        /// <summary>Points ground units must be able to reach (map centre, approaches, ...).</summary>
        public readonly List<Vector2> StrategicPoints = new List<Vector2>();
        /// <summary>The same map rectangle as configured on each system that needs one (camera, placement, fog, ...).</summary>
        public readonly List<(string system, Rect rect)> SystemBounds = new List<(string, Rect)>();
    }

    public sealed class MapValidationThresholds
    {
        public int MinReachableOrePerSlot = 15000;
        public int MinSlots = 6;
        /// <summary>Ore that must lie within <see cref="StartingResourceRadius"/> (ground distance) of every start.</summary>
        public int MinStartingOre = 8000;
        public float StartingResourceRadius = 60f;
        /// <summary>Ground distance from the headquarters to the nearest ore field.</summary>
        public float MaxHomeFieldDistance = 45f;
        /// <summary>Path-weighted ore score difference between slots (fraction of the larger).</summary>
        public float MaxOpportunityImbalance = 0.15f;
        /// <summary>Longer / shorter path to a contested field.</summary>
        public float MaxContestedPathRatio = 1.25f;
        public int MinExpansionSites = 5;
        public float ExpansionRadius = 28f;
        public string ExpansionStructure = "vehicle_factory";
        /// <summary>Walkable fraction of open (obstacle-free) sample points.</summary>
        public float MinOpenGroundCoverage = 0.9f;
        public float CoverageSpacing = 4f;
        /// <summary>Distance scale of the opportunity score: ore at this path distance counts half.</summary>
        public float OpportunityDistanceScale = 80f;
    }

    public enum IssueSeverity { Warning, Error }

    public readonly struct MapIssue
    {
        public readonly IssueSeverity Severity;
        public readonly string Check;
        public readonly string Message;

        public MapIssue(IssueSeverity severity, string check, string message)
        {
            Severity = severity;
            Check = check;
            Message = message;
        }

        public override string ToString() => $"{(Severity == IssueSeverity.Error ? "ERROR" : "warning")} [{Check}] {Message}";
    }

    public sealed class MapValidationReport
    {
        public string MapName;
        /// <summary>The valid spawn pairs derived from the rules (the builder stores it in the scene).</summary>
        public SpawnTable SpawnTable;
        public readonly List<MapIssue> Issues = new List<MapIssue>();
        /// <summary>Measured values (for reports and balance discussion).</summary>
        public readonly List<(string name, float value)> Metrics = new List<(string, float)>();

        public bool Passed
        {
            get
            {
                foreach (var i in Issues) if (i.Severity == IssueSeverity.Error) return false;
                return true;
            }
        }

        public int ErrorCount
        {
            get
            {
                int n = 0;
                foreach (var i in Issues) if (i.Severity == IssueSeverity.Error) n++;
                return n;
            }
        }

        public bool Has(string check)
        {
            foreach (var i in Issues) if (i.Check == check && i.Severity == IssueSeverity.Error) return true;
            return false;
        }

        public float Metric(string name)
        {
            foreach (var (n, v) in Metrics) if (n == name) return v;
            return float.NaN;
        }

        internal void Error(string check, string message) => Issues.Add(new MapIssue(IssueSeverity.Error, check, message));
        internal void Warn(string check, string message) => Issues.Add(new MapIssue(IssueSeverity.Warning, check, message));

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Map validation: {MapName} — {(Passed ? "PASSED" : $"FAILED ({ErrorCount} errors)")}");
            foreach (var i in Issues) sb.AppendLine("  " + i);
            foreach (var (n, v) in Metrics) sb.AppendLine($"  {n} = {v:0.###}");
            return sb.ToString();
        }
    }

    /// <summary>
    /// Automated battlefield checks (Milestone 008). Pure: everything it knows about the terrain comes from the
    /// <see cref="IMapProbe"/>. Check ids (stable, used by tests): slots, bounds, start-buildings, expansion,
    /// harvest-route, deposit-reach, slot-reach, ore-total, home-field, starting-ore, spawn-pairs, spawn-table,
    /// contested, strategic, nav-obstacle, nav-coverage. Per-slot problems make that slot unusable for spawning.
    /// </summary>
    public static class MapValidator
    {
        public const string HeadquartersId = "headquarters";
        public const string RefineryId = "refinery";

        public static MapValidationReport Validate(MapSpec map, IMapProbe probe, MapValidationThresholds t = null)
        {
            t ??= new MapValidationThresholds();
            var r = new MapValidationReport { MapName = map.Name };
            r.Metrics.Add(("mapSize", map.Bounds.width));
            r.Metrics.Add(("slots", map.Slots.Count));

            // 1. Slots exist, are distinct, inside the map and on walkable ground.
            var ids = new HashSet<int>();
            foreach (var s in map.Slots)
                if (!ids.Add(s.Index)) r.Error("slots", $"duplicate start slot id {s.Index}");
            if (map.Slots.Count < t.MinSlots)
            {
                r.Error("slots", $"{map.Slots.Count} start slots (need at least {t.MinSlots})");
                if (map.Slots.Count == 0) return r;
            }
            var usable = new Dictionary<int, bool>();
            foreach (var s in map.Slots)
            {
                usable[s.Index] = true;
                if (!map.Bounds.Contains(s.Headquarters)) SlotError(r, usable, s, "slots", $"headquarters {s.Headquarters} is outside the map");
                if (!probe.IsWalkable(s.Headquarters, 2f)) SlotError(r, usable, s, "slots", $"headquarters {s.Headquarters} is not on walkable ground");
            }

            // 2. Every system agrees on the map rectangle.
            foreach (var (system, rect) in map.SystemBounds)
                if (!Same(rect, map.Bounds))
                    r.Error("bounds", $"{system} bounds {rect} differ from the map {map.Bounds}");

            // 3. Starting structures are placeable (not inside obstacles, not overlapping each other).
            var startPlans = new Dictionary<int, List<PlannedStructure>>();
            foreach (var s in map.Slots)
            {
                var placed = new List<PlannedStructure>();
                if (!probe.CanPlace(HeadquartersId, s.Headquarters, s.FacesSouth, placed))
                    SlotError(r, usable, s, "start-buildings", $"headquarters cannot be placed at {s.Headquarters}");
                placed.Add(new PlannedStructure(HeadquartersId, s.Headquarters, s.FacesSouth));
                if (!probe.CanPlace(RefineryId, s.Refinery, s.FacesSouth, placed))
                    SlotError(r, usable, s, "start-buildings", $"refinery cannot be placed at {s.Refinery}");
                placed.Add(new PlannedStructure(RefineryId, s.Refinery, s.FacesSouth));
                if (!probe.IsWalkable(s.Harvester, 1.5f))
                    SlotError(r, usable, s, "start-buildings", $"harvester start {s.Harvester} is not on walkable ground");
                startPlans[s.Index] = placed;
            }

            // 4. Room for expansion around each headquarters (greedy, non-overlapping sites).
            foreach (var s in map.Slots)
            {
                int sites = CountExpansionSites(s, startPlans[s.Index], probe, t);
                r.Metrics.Add(($"slot{s.Index}.expansionSites", sites));
                if (sites < t.MinExpansionSites)
                    SlotError(r, usable, s, "expansion", $"only {sites} {t.ExpansionStructure} sites within {t.ExpansionRadius} m (need {t.MinExpansionSites})");
            }

            // 5-7. Reachability, economy and the home field.
            var score = new Dictionary<int, float>();
            foreach (var s in map.Slots)
            {
                float reachableOre = 0f, opportunity = 0f, nearest = float.PositiveInfinity, nearOre = 0f;
                int nearestIndex = -1;
                for (int i = 0; i < map.Deposits.Count; i++)
                {
                    var d = map.Deposits[i];
                    float len = probe.PathLength(s.Headquarters, d.Position, 14f, d.Radius + 4f);
                    if (float.IsInfinity(len))
                    {
                        SlotError(r, usable, s, "deposit-reach", $"cannot reach ore field {d.Name} at {d.Position}");
                        continue;
                    }
                    reachableOre += d.Amount;
                    opportunity += d.Amount / (1f + len / t.OpportunityDistanceScale);
                    if (len <= t.StartingResourceRadius) nearOre += d.Amount;
                    if (len < nearest) { nearest = len; nearestIndex = i; }
                }
                score[s.Index] = opportunity;
                r.Metrics.Add(($"slot{s.Index}.reachableOre", reachableOre));
                r.Metrics.Add(($"slot{s.Index}.startingOre", nearOre));
                r.Metrics.Add(($"slot{s.Index}.opportunity", opportunity));
                r.Metrics.Add(($"slot{s.Index}.homeFieldDistance", nearest));
                if (reachableOre < t.MinReachableOrePerSlot)
                    SlotError(r, usable, s, "ore-total", $"only {reachableOre:0} reachable ore (need {t.MinReachableOrePerSlot})");
                if (nearest > t.MaxHomeFieldDistance)
                    SlotError(r, usable, s, "home-field", $"nearest ore field is {nearest:0} m away (max {t.MaxHomeFieldDistance:0})");
                if (nearOre < t.MinStartingOre)
                    SlotError(r, usable, s, "starting-ore", $"only {nearOre:0} ore within {t.StartingResourceRadius:0} m (need {t.MinStartingOre})");

                // Harvesters must be able to drive between the refinery dock and the home field.
                if (nearestIndex >= 0)
                {
                    var home = map.Deposits[nearestIndex];
                    float route = probe.PathLength(s.RefineryDock, home.Position, 3f, home.Radius + 4f);
                    r.Metrics.Add(($"slot{s.Index}.harvestRoute", route));
                    if (float.IsInfinity(route))
                        SlotError(r, usable, s, "harvest-route", $"no route from the refinery dock to {home.Name}");
                }
            }

            // 8. Every pair of starts: connectivity, separation and fairness; derive the valid spawn table.
            var distance = new Dictionary<(int, int), float>();
            for (int i = 0; i < map.Slots.Count; i++)
            for (int j = i + 1; j < map.Slots.Count; j++)
            {
                var a = map.Slots[i];
                var b = map.Slots[j];
                float len = probe.PathLength(a.Headquarters, b.Headquarters, 14f, 14f);
                distance[(a.Index, b.Index)] = distance[(b.Index, a.Index)] = len;
                if (float.IsInfinity(len)) r.Error("slot-reach", $"start slots {a.Index} and {b.Index} are not connected by ground");
            }
            var slotIds = new List<int>();
            foreach (var s in map.Slots) slotIds.Add(s.Index);
            var table = SpawnSelection.ValidPairs(slotIds, id => usable[id], (x, y) => distance[(x, y)],
                id => score.TryGetValue(id, out float v) ? v : 0f, map.SpawnRules);
            r.SpawnTable = table;
            var pairs = table.OrderedPairs();
            r.Metrics.Add(("validOrderedPairs", pairs.Count));
            foreach (var (pa, pb) in pairs)
                if (pa < pb)
                    r.Metrics.Add(($"pair{pa}-{pb}", distance[(pa, pb)]));
            foreach (var s in map.Slots)
            {
                int n = table.OpponentsOf(s.Index).Count;
                r.Metrics.Add(($"slot{s.Index}.validOpponents", n));
                if (usable[s.Index] && n == 0)
                    r.Error("spawn-pairs", $"slot {s.Index} has no valid opponent (separation >= {map.SpawnRules.MinSeparation:0} m, imbalance <= {map.SpawnRules.MaxOpportunityImbalance:P0})");
            }
            if (pairs.Count < map.Slots.Count)
                r.Error("spawn-pairs", $"only {pairs.Count} valid ordered spawn pairs for {map.Slots.Count} slots");
            if (map.CheckStoredTable)
            {
                foreach (var s in map.Slots)
                {
                    var expected = table.OpponentsOf(s.Index);
                    var stored = new List<int>(s.StoredOpponents ?? new int[0]);
                    stored.Sort();
                    if (!SameList(expected, stored))
                        r.Error("spawn-table", $"slot {s.Index} stores opponents [{string.Join(",", stored)}] but the rules give [{string.Join(",", expected)}]");
                }
            }

            // Contested fields must be reachable from every start.
            foreach (var d in map.Deposits)
            {
                if (!d.Contested) continue;
                foreach (var s in map.Slots)
                    if (float.IsInfinity(probe.PathLength(s.Headquarters, d.Position, 14f, d.Radius + 4f)))
                        r.Error("contested", $"contested field {d.Name} is not reachable from slot {s.Index}");
            }

            foreach (var p in map.StrategicPoints)
                if (float.IsInfinity(probe.PathLength(map.Slots[0].Headquarters, p, 14f, 4f)))
                    r.Error("strategic", $"strategic point {p} cannot be reached");

            // 9. Navigation matches the battlefield. Recast can leave small disconnected islands inside solid boxes;
            //    those are harmless. What must never happen is ground inside an obstacle that units can drive to.
            {
                var from = map.Slots[0].Headquarters;
                foreach (var core in map.ObstacleCores)
                    if (probe.IsWalkable(core.center, 0.25f) && !float.IsInfinity(probe.PathLength(from, core.center, 14f, 0.25f)))
                        r.Error("nav-obstacle", $"ground inside the obstacle at {core.center} is reachable: navigation ignores it");
            }

            float spacing = Math.Max(t.CoverageSpacing, map.Bounds.width / 150f);
            int open = 0, walkable = 0;
            for (float z = map.Bounds.yMin + spacing * 0.5f; z < map.Bounds.yMax; z += spacing)
            for (float x = map.Bounds.xMin + spacing * 0.5f; x < map.Bounds.xMax; x += spacing)
            {
                var p = new Vector2(x, z);
                if (NearObstacle(map, p, 1.5f) || NearDeposit(map, p) || NearBase(map, p)) continue;
                if (x < map.Bounds.xMin + 1.5f || x > map.Bounds.xMax - 1.5f || z < map.Bounds.yMin + 1.5f || z > map.Bounds.yMax - 1.5f) continue;
                open++;
                if (probe.IsWalkable(p, 0.6f)) walkable++;
            }
            float coverage = open == 0 ? 0f : (float)walkable / open;
            r.Metrics.Add(("openGroundCoverage", coverage));
            if (coverage < t.MinOpenGroundCoverage)
                r.Error("nav-coverage", $"only {coverage:P0} of open ground is walkable (min {t.MinOpenGroundCoverage:P0}): navigation does not match the battlefield");

            return r;
        }

        private static void SlotError(MapValidationReport r, Dictionary<int, bool> usable, MapSpec.Slot s, string check, string message)
        {
            usable[s.Index] = false;
            r.Error(check, $"slot {s.Index} ({s.Name}): {message}");
        }

        private static bool SameList(IReadOnlyList<int> a, List<int> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private static int CountExpansionSites(MapSpec.Slot s, List<PlannedStructure> start, IMapProbe probe, MapValidationThresholds t)
        {
            var placed = new List<PlannedStructure>(start);
            int sites = 0;
            const float step = 2f;
            // Spiral-ish order: nearest candidates first, so the count reflects usable space near the base.
            var candidates = new List<Vector2>();
            for (float dz = -t.ExpansionRadius; dz <= t.ExpansionRadius; dz += step)
            for (float dx = -t.ExpansionRadius; dx <= t.ExpansionRadius; dx += step)
                if (dx * dx + dz * dz <= t.ExpansionRadius * t.ExpansionRadius)
                    candidates.Add(s.Headquarters + new Vector2(dx, dz));
            candidates.Sort((p, q) => (p - s.Headquarters).sqrMagnitude.CompareTo((q - s.Headquarters).sqrMagnitude));
            foreach (var c in candidates)
            {
                if (!probe.CanPlace(t.ExpansionStructure, c, s.FacesSouth, placed)) continue;
                placed.Add(new PlannedStructure(t.ExpansionStructure, c, s.FacesSouth));
                sites++;
                if (sites >= t.MinExpansionSites * 2) break; // enough evidence; keeps the check fast
            }
            return sites;
        }

        private static bool NearObstacle(MapSpec map, Vector2 p, float margin)
        {
            foreach (var o in map.Obstacles)
                if (p.x > o.xMin - margin && p.x < o.xMax + margin && p.y > o.yMin - margin && p.y < o.yMax + margin) return true;
            return false;
        }

        private static bool NearDeposit(MapSpec map, Vector2 p)
        {
            foreach (var d in map.Deposits)
                if ((p - d.Position).sqrMagnitude < (d.Radius + 1.5f) * (d.Radius + 1.5f)) return true;
            return false;
        }

        private static bool NearBase(MapSpec map, Vector2 p)
        {
            foreach (var s in map.Slots)
                if ((p - s.Headquarters).sqrMagnitude < 7f * 7f || (p - s.Refinery).sqrMagnitude < 7f * 7f) return true;
            return false;
        }

        private static bool Same(Rect a, Rect b) =>
            Math.Abs(a.xMin - b.xMin) < 0.01f && Math.Abs(a.yMin - b.yMin) < 0.01f && Math.Abs(a.width - b.width) < 0.01f && Math.Abs(a.height - b.height) < 0.01f;
    }
}
