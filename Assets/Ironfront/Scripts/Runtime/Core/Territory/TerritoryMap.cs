using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ironfront.Core.Territory
{
    /// <summary>
    /// One structure's contribution to its owner's build area. Every structure is an anchor: new construction must come
    /// within <see cref="Range"/> metres of its footprint. Command structures also carry an exclusion radius.
    /// </summary>
    public readonly struct TerritoryAnchor
    {
        public readonly int Id;
        public readonly int Faction;
        /// <summary>The structure's footprint on the XZ plane (x → x, z → y).</summary>
        public readonly Rect Area;
        /// <summary>Build range measured from the footprint's edge (metres).</summary>
        public readonly float Range;
        /// <summary>Other factions may not build or deploy within this radius of the centre (once they know the structure).</summary>
        public readonly float ExclusionRadius;

        public Vector2 Centre => Area.center;

        public TerritoryAnchor(int id, int faction, Rect area, float range, float exclusionRadius)
        {
            Id = id;
            Faction = faction;
            Area = area;
            Range = range;
            ExclusionRadius = exclusionRadius;
        }
    }

    /// <summary>
    /// Pure construction-territory model. Milestone 010 used fixed circles around command structures; following the
    /// director's playtest the build area grows from <b>every</b> structure: a new footprint is inside a faction's
    /// territory when its nearest gap to any of that faction's structures is at most that structure's build range. The
    /// area is never shared between factions. Enemy exclusion zones (around command structures, known ones only) are unchanged.
    /// </summary>
    public sealed class TerritoryMap
    {
        private readonly Dictionary<int, TerritoryAnchor> _anchors = new Dictionary<int, TerritoryAnchor>();

        /// <summary>Incremented on every change (overlays rebuild only when it moves).</summary>
        public int Version { get; private set; }
        public int Count => _anchors.Count;
        public IEnumerable<TerritoryAnchor> Anchors => _anchors.Values;

        public event Action<int> Changed;

        public void Add(TerritoryAnchor anchor)
        {
            if (anchor.Range <= 0f && anchor.ExclusionRadius <= 0f) return;
            _anchors[anchor.Id] = anchor;
            Version++;
            Changed?.Invoke(anchor.Faction);
        }

        public bool Remove(int id)
        {
            if (!_anchors.TryGetValue(id, out var a)) return false;
            _anchors.Remove(id);
            Version++;
            Changed?.Invoke(a.Faction);
            return true;
        }

        public bool Contains(int id) => _anchors.ContainsKey(id);

        public void Clear()
        {
            if (_anchors.Count == 0) return;
            _anchors.Clear();
            Version++;
            Changed?.Invoke(0);
        }

        public IEnumerable<TerritoryAnchor> AnchorsOf(int faction)
        {
            foreach (var a in _anchors.Values)
                if (a.Faction == faction && a.Range > 0f) yield return a;
        }

        /// <summary>Copies the faction's build-range anchors into <paramref name="into"/> (cleared first; no allocation).</summary>
        public void CopyAnchors(int faction, List<TerritoryAnchor> into)
        {
            into.Clear();
            foreach (var a in _anchors.Values)
                if (a.Faction == faction && a.Range > 0f) into.Add(a);
        }

        public bool HasTerritory(int faction)
        {
            foreach (var a in _anchors.Values)
                if (a.Faction == faction && a.Range > 0f) return true;
            return false;
        }

        /// <summary>Is the point within build range of one of the faction's structures?</summary>
        public bool Covers(int faction, Vector2 p)
        {
            foreach (var a in _anchors.Values)
                if (a.Faction == faction && a.Range > 0f && DistanceToRect(p, a.Area) <= a.Range) return true;
            return false;
        }

        /// <summary>Is the footprint within build range of (its gap to) at least one of the faction's structures?</summary>
        public bool Covers(int faction, Rect footprint) => Gap(faction, footprint, out _) <= 0f;

        /// <summary>
        /// How far the footprint is outside the faction's build range: the smallest (gap to a structure − that structure's
        /// range), ≤ 0 when inside. +∞ without structures. <paramref name="nearest"/> is the structure that gives it.
        /// </summary>
        public float Gap(int faction, Rect footprint, out TerritoryAnchor nearest)
        {
            nearest = default;
            float best = float.PositiveInfinity;
            foreach (var a in _anchors.Values)
            {
                if (a.Faction != faction || a.Range <= 0f) continue;
                float d = RectDistance(a.Area, footprint) - a.Range;
                if (d < best) { best = d; nearest = a; }
            }
            return best;
        }

        /// <summary>
        /// Does the rectangle intersect the exclusion zone of another faction's anchor that <paramref name="isKnown"/>
        /// accepts (null = all)? <paramref name="blocker"/> is the first such anchor.
        /// </summary>
        public bool Excluded(int faction, Rect r, Func<TerritoryAnchor, bool> isKnown, out TerritoryAnchor blocker)
        {
            foreach (var a in _anchors.Values)
            {
                if (a.Faction == faction || a.ExclusionRadius <= 0f) continue;
                if (DistanceToRect(a.Centre, r) > a.ExclusionRadius) continue;
                if (isKnown != null && !isKnown(a)) continue;
                blocker = a;
                return true;
            }
            blocker = default;
            return false;
        }

        /// <summary>
        /// Points along the outline of a footprint grown by <paramref name="range"/> (a rounded rectangle), about
        /// <paramref name="step"/> metres apart, in order. For overlays and the minimap.
        /// </summary>
        public static List<Vector2> Outline(Rect area, float range, float step)
        {
            var pts = new List<Vector2>();
            step = Mathf.Max(0.25f, step);
            int arc = Mathf.Max(2, Mathf.CeilToInt(Mathf.PI * 0.5f * range / step));
            var corners = new[]
            {
                (new Vector2(area.xMax, area.yMax), 0f), (new Vector2(area.xMin, area.yMax), 90f),
                (new Vector2(area.xMin, area.yMin), 180f), (new Vector2(area.xMax, area.yMin), 270f),
            };
            for (int c = 0; c < 4; c++)
            {
                var (centre, start) = corners[c];
                for (int i = 0; i <= arc; i++)
                {
                    float ang = (start + 90f * i / arc) * Mathf.Deg2Rad;
                    pts.Add(centre + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * range);
                }
                // Straight edge to the next corner's arc.
                var next = corners[(c + 1) % 4];
                float nextAng = next.Item2 * Mathf.Deg2Rad;
                var from = pts[pts.Count - 1];
                var to = next.Item1 + new Vector2(Mathf.Cos(nextAng), Mathf.Sin(nextAng)) * range;
                int n = Mathf.FloorToInt(Vector2.Distance(from, to) / step);
                for (int i = 1; i < n; i++) pts.Add(Vector2.Lerp(from, to, (float)i / n));
            }
            return pts;
        }

        /// <summary>Shortest distance from a point to a rectangle (0 inside).</summary>
        public static float DistanceToRect(Vector2 p, Rect r)
        {
            float dx = Mathf.Max(r.xMin - p.x, 0f, p.x - r.xMax);
            float dy = Mathf.Max(r.yMin - p.y, 0f, p.y - r.yMax);
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>Shortest gap between two rectangles (0 when they touch or overlap).</summary>
        public static float RectDistance(Rect a, Rect b)
        {
            float dx = Mathf.Max(a.xMin - b.xMax, 0f, b.xMin - a.xMax);
            float dy = Mathf.Max(a.yMin - b.yMax, 0f, b.yMin - a.yMax);
            return Mathf.Sqrt(dx * dx + dy * dy);
        }
    }
}
