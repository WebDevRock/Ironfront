using System;
using System.Collections.Generic;
using Ironfront.Combat;
using Ironfront.Core.Construction;
using Ironfront.Core.Territory;
using Ironfront.Match;
using UnityEngine;

namespace Ironfront.Buildings
{
    /// <summary>
    /// Construction territory at runtime (Milestone 010, build range from every structure after the director's playtest).
    /// Every structure becomes an anchor of the pure <see cref="TerritoryMap"/> — its footprint plus its build range —
    /// while it is registered with its faction, and stops being one when it unregisters (destroyed, sold, unloaded).
    /// Command structures also carry their exclusion radius. Driven by <see cref="Building.AnyRegistrationChanged"/>:
    /// no per-frame recalculation.
    /// An enemy anchor's exclusion zone only applies to a faction that has seen that structure (sampled twice a second by
    /// <see cref="TerritoryTracker"/>), so territory rules never reveal a hidden base.
    /// </summary>
    public static class TerritorySystem
    {
        private static readonly TerritoryMap s_map = new TerritoryMap();
        private static readonly Dictionary<int, Building> s_buildings = new Dictionary<int, Building>();
        private static readonly HashSet<(int anchor, int faction)> s_known = new HashSet<(int, int)>();
        private static bool s_hooked;
        private static readonly Dictionary<Building, int> s_ids = new Dictionary<Building, int>();
        private static int s_nextId;

        /// <summary>Stable anchor id of a building for this session.</summary>
        public static int IdOf(Building b)
        {
            if (!s_ids.TryGetValue(b, out int id)) s_ids[b] = id = ++s_nextId;
            return id;
        }

        public static TerritoryMap Map { get { Hook(); return s_map; } }

        /// <summary>A faction's territory changed (anchor added or removed).</summary>
        public static event Action<int> Changed;
        /// <summary>A command structure stopped granting territory (destroyed, sold or unloaded).</summary>
        public static event Action<Building> AnchorLost;
        public static event Action<Building> AnchorGained;

        private static void Hook()
        {
            if (s_hooked) return;
            s_hooked = true;
            Building.AnyRegistrationChanged += OnRegistrationChanged;
            // Buildings that registered before the first query (scene start order) are picked up once.
            foreach (var b in Building.All) OnRegistrationChanged(b);
        }

        /// <summary>Clears match state (called when a match starts).</summary>
        public static void ResetMatch()
        {
            s_map.Clear();
            s_buildings.Clear();
            s_known.Clear();
            Hook();
            foreach (var b in Building.All) OnRegistrationChanged(b);
        }

        public static bool IsAnchorDefinition(Data.BuildingDefinition d) => d != null;

        /// <summary>
        /// A structure was just placed: it extends the build range at once, not a frame later when it registers with its
        /// faction (two placements in the same frame — the computer, tests — can build on each other).
        /// </summary>
        public static void OnPlaced(Building b)
        {
            Hook();
            Add(b);
        }

        /// <summary>A structure was disabled or destroyed (also before it ever registered): its range goes with it.</summary>
        public static void OnDisabled(Building b)
        {
            if (b == null || !s_hooked || !s_ids.TryGetValue(b, out int id) || !s_map.Contains(id)) return;
            s_map.Remove(id);
            s_buildings.Remove(id);
            s_ids.Remove(b);
            s_known.RemoveWhere(k => k.anchor == id);
            Changed?.Invoke(b.FactionId);
            AnchorLost?.Invoke(b);
        }

        private static void Add(Building b)
        {
            if (b == null || !IsAnchorDefinition(b.Definition) || !b.isActiveAndEnabled) return;
            int id = IdOf(b);
            if (s_map.Contains(id)) return;
            s_buildings[id] = b;
            s_map.Add(new TerritoryAnchor(id, b.FactionId, b.Footprint.Body, b.Definition.BuildRange, b.Definition.ExclusionRadius));
            Changed?.Invoke(b.FactionId);
            AnchorGained?.Invoke(b);
        }

        private static void OnRegistrationChanged(Building b)
        {
            if (b == null || !IsAnchorDefinition(b.Definition)) return;
            int id = IdOf(b);
            if (b.IsRegistered && b.isActiveAndEnabled)
            {
                if (s_map.Contains(id)) return;
                s_buildings[id] = b;
                s_map.Add(new TerritoryAnchor(id, b.FactionId, b.Footprint.Body, b.Definition.BuildRange, b.Definition.ExclusionRadius));
                Changed?.Invoke(b.FactionId);
                AnchorGained?.Invoke(b);
            }
            else if (s_map.Remove(id))
            {
                s_buildings.Remove(id);
                s_ids.Remove(b);
                s_known.RemoveWhere(k => k.anchor == id);
                Changed?.Invoke(b.FactionId);
                AnchorLost?.Invoke(b);
            }
        }

        /// <summary>Does the faction own construction territory at all?</summary>
        public static bool HasTerritory(int faction) => Map.HasTerritory(faction);

        /// <summary>Is the footprint (body and apron) within build range of one of the faction's structures?</summary>
        public static bool Covers(int faction, in Footprint fp) => Map.Covers(faction, fp.HasApron ? Union(fp.Body, fp.Apron) : fp.Body);

        public static bool CoversPoint(int faction, Vector3 p) => Map.Covers(faction, new Vector2(p.x, p.z));

        /// <summary>Does the footprint touch the exclusion zone of an enemy command structure this faction has seen?</summary>
        public static bool InKnownEnemyZone(int faction, in Footprint fp, out Building blocker)
        {
            blocker = null;
            Hook();
            Rect all = fp.HasApron ? Union(fp.Body, fp.Apron) : fp.Body;
            if (!s_map.Excluded(faction, all, a => IsKnownTo(faction, a.Id), out var anchor)) return false;
            s_buildings.TryGetValue(anchor.Id, out blocker);
            return true;
        }

        /// <summary>Has <paramref name="faction"/> seen the command structure behind this anchor (own anchors always)?</summary>
        public static bool IsKnownTo(int faction, int anchorId)
        {
            if (s_buildings.TryGetValue(anchorId, out var b) && b != null && b.FactionId == faction) return true;
            return s_known.Contains((anchorId, faction));
        }

        public static bool IsKnownTo(int faction, Building b) => b != null && IsKnownTo(faction, IdOf(b));

        /// <summary>Command structures currently granting territory or exclusion.</summary>
        public static IEnumerable<Building> Anchors
        {
            get
            {
                Hook();
                foreach (var b in s_buildings.Values) if (b != null) yield return b;
            }
        }

        /// <summary>Marks every anchor visible to a faction as known (TerritoryTracker; public for tests and tools).</summary>
        public static void SampleSightings(IReadOnlyList<int> factions)
        {
            Hook();
            foreach (var kv in s_buildings)
            {
                var b = kv.Value;
                if (b == null) continue;
                var h = b.GetComponent<Health>();
                if (h == null || !h.IsAlive) continue;
                foreach (int f in factions)
                    if (f != b.FactionId && !s_known.Contains((kv.Key, f)) && Vision.VisionSystem.CanSee(f, h))
                        s_known.Add((kv.Key, f));
            }
        }

        private static Rect Union(Rect a, Rect b) =>
            Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_map.Clear();
            s_buildings.Clear();
            s_known.Clear();
            s_ids.Clear();
            if (s_hooked) Building.AnyRegistrationChanged -= OnRegistrationChanged;
            s_hooked = false;
            Changed = null;
            AnchorLost = null;
            AnchorGained = null;
        }
    }
}
