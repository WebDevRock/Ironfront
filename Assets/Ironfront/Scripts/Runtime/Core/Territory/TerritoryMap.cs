using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ironfront.Core.Territory
{
    /// <summary>A command structure that grants construction territory (Milestone 010).</summary>
    public readonly struct TerritoryAnchor
    {
        public readonly int Id;
        public readonly int Faction;
        /// <summary>Centre on the XZ plane (x → x, z → y).</summary>
        public readonly Vector2 Centre;
        public readonly float Radius;
        /// <summary>Other factions may not build or deploy inside this radius (once they know the structure).</summary>
        public readonly float ExclusionRadius;

        public TerritoryAnchor(int id, int faction, Vector2 centre, float radius, float exclusionRadius)
        {
            Id = id;
            Faction = faction;
            Centre = centre;
            Radius = radius;
            ExclusionRadius = exclusionRadius;
        }
    }

    /// <summary>
    /// Pure construction-territory model (Milestone 010). Each faction's buildable area is the union of its own anchors'
    /// circles; it is never shared with another faction. Footprints are tested as whole rectangles, not by their centre.
    /// </summary>
    public sealed class TerritoryMap
    {
        /// <summary>Spacing of the boundary/interior samples used when no single circle holds the whole rectangle.</summary>
        public const float SampleStep = 0.5f;

        private readonly Dictionary<int, TerritoryAnchor> _anchors = new Dictionary<int, TerritoryAnchor>();

        /// <summary>Incremented on every change (overlays rebuild only when it moves).</summary>
        public int Version { get; private set; }
        public int Count => _anchors.Count;
        public IEnumerable<TerritoryAnchor> Anchors => _anchors.Values;

        public event Action<int> Changed;

        public void Add(TerritoryAnchor anchor)
        {
            if (anchor.Radius <= 0f && anchor.ExclusionRadius <= 0f) return;
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
                if (a.Faction == faction && a.Radius > 0f) yield return a;
        }

        /// <summary>Copies the faction's territory anchors into <paramref name="into"/> (cleared first; no allocation).</summary>
        public void CopyAnchors(int faction, List<TerritoryAnchor> into)
        {
            into.Clear();
            foreach (var a in _anchors.Values)
                if (a.Faction == faction && a.Radius > 0f) into.Add(a);
        }

        public bool HasTerritory(int faction)
        {
            foreach (var a in _anchors.Values)
                if (a.Faction == faction && a.Radius > 0f) return true;
            return false;
        }

        /// <summary>Is the point inside the faction's territory?</summary>
        public bool Covers(int faction, Vector2 p)
        {
            foreach (var a in _anchors.Values)
                if (a.Faction == faction && a.Radius > 0f && (p - a.Centre).sqrMagnitude <= a.Radius * a.Radius) return true;
            return false;
        }

        /// <summary>Is the whole rectangle inside the union of the faction's territory circles?</summary>
        public bool Covers(int faction, Rect r)
        {
            if (r.width <= 0f || r.height <= 0f) return Covers(faction, r.center);
            bool any = false;
            foreach (var a in _anchors.Values)
            {
                if (a.Faction != faction || a.Radius <= 0f) continue;
                any = true;
                // A circle is convex: holding all four corners means holding the rectangle.
                if (CircleHoldsRect(a.Centre, a.Radius, r)) return true;
            }
            if (!any) return false;

            // Several circles: every sample on the boundary and inside must be covered by one of them.
            int nx = Mathf.Max(1, Mathf.CeilToInt(r.width / SampleStep));
            int ny = Mathf.Max(1, Mathf.CeilToInt(r.height / SampleStep));
            for (int i = 0; i <= nx; i++)
                for (int j = 0; j <= ny; j++)
                {
                    var p = new Vector2(r.xMin + r.width * i / nx, r.yMin + r.height * j / ny);
                    if (!Covers(faction, p)) return false;
                }
            return true;
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

        public static bool CircleHoldsRect(Vector2 c, float radius, Rect r)
        {
            float r2 = radius * radius;
            return (new Vector2(r.xMin, r.yMin) - c).sqrMagnitude <= r2 && (new Vector2(r.xMax, r.yMin) - c).sqrMagnitude <= r2
                && (new Vector2(r.xMin, r.yMax) - c).sqrMagnitude <= r2 && (new Vector2(r.xMax, r.yMax) - c).sqrMagnitude <= r2;
        }

        /// <summary>Shortest distance from a point to a rectangle (0 inside).</summary>
        public static float DistanceToRect(Vector2 p, Rect r)
        {
            float dx = Mathf.Max(r.xMin - p.x, 0f, p.x - r.xMax);
            float dy = Mathf.Max(r.yMin - p.y, 0f, p.y - r.yMax);
            return Mathf.Sqrt(dx * dx + dy * dy);
        }
    }
}
