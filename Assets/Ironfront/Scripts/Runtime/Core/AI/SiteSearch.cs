using System.Collections.Generic;
using UnityEngine;

namespace Ironfront.Core.AI
{
    /// <summary>
    /// Candidate building sites around an anchor, best first. Validity is checked by the caller with the
    /// same placement rules the player uses; this only orders where to look.
    /// </summary>
    public static class SiteSearch
    {
        /// <summary>
        /// Grid points (step <paramref name="step"/>) within <paramref name="maxRadius"/> of the anchor, ordered by
        /// closeness to <paramref name="preferredDistance"/>, with a bonus for lying towards <paramref name="towards"/>.
        /// Ties are broken by quantities relative to the direction (not world axes), so the ordering is
        /// point-symmetric: mirrored factions on a mirrored map get mirrored candidates.
        /// </summary>
        public static List<Vector3> Ranked(Vector3 anchor, Vector3 towards, float preferredDistance, float maxRadius, float step,
            float directionWeight = 4f)
        {
            Vector3 dir = towards - anchor;
            dir.y = 0f;
            dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.zero;

            var scored = new List<(float score, float side, float along, Vector3 offset, Vector3 p)>();
            int n = Mathf.CeilToInt(maxRadius / step);
            for (int x = -n; x <= n; x++)
            for (int z = -n; z <= n; z++)
            {
                var offset = new Vector3(x * step, 0f, z * step);
                float d = offset.magnitude;
                if (d > maxRadius || d < 0.01f) continue;
                float score = Mathf.Abs(d - preferredDistance) - Vector3.Dot(offset / d, dir) * directionWeight;
                // Which side of the direction axis the site is on. Unlike world x/z, this is unchanged when the
                // whole situation is point-mirrored, so two factions on a symmetric map choose mirrored sites.
                float side = dir.x * offset.z - dir.z * offset.x;
                float along = Vector3.Dot(offset, dir);
                var p = anchor + offset;
                p.y = 0f;
                // Rounded so mirrored inputs with tiny float differences still compare equal.
                scored.Add((Round(score), Round(side), Round(along), offset, p));
            }
            scored.Sort((a, b) =>
            {
                int c = a.score.CompareTo(b.score);
                if (c != 0) return c;
                c = b.side.CompareTo(a.side);
                if (c != 0) return c;
                c = b.along.CompareTo(a.along);
                if (c != 0) return c;
                // Only reachable without a direction: fall back to world order.
                c = a.offset.x.CompareTo(b.offset.x);
                return c != 0 ? c : a.offset.z.CompareTo(b.offset.z);
            });

            var result = new List<Vector3>(scored.Count);
            foreach (var s in scored) result.Add(s.p);
            return result;
        }

        private static float Round(float v) => Mathf.Round(v * 1000f) / 1000f;
    }

    /// <summary>Fixed-capacity log of recent entries (oldest dropped first), for the AI debug view.</summary>
    public sealed class BoundedLog
    {
        private readonly string[] _items;
        private int _start, _count;

        public int Capacity => _items.Length;
        public int Count => _count;

        public BoundedLog(int capacity)
        {
            _items = new string[capacity < 1 ? 1 : capacity];
        }

        public void Add(string entry)
        {
            if (_count < _items.Length)
            {
                _items[(_start + _count) % _items.Length] = entry;
                _count++;
            }
            else
            {
                _items[_start] = entry;
                _start = (_start + 1) % _items.Length;
            }
        }

        /// <summary>Entries from newest to oldest.</summary>
        public IEnumerable<string> NewestFirst()
        {
            for (int i = _count - 1; i >= 0; i--) yield return _items[(_start + i) % _items.Length];
        }

        public void Clear()
        {
            _start = 0;
            _count = 0;
        }
    }
}
