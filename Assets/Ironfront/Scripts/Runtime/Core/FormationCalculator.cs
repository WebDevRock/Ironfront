using System.Collections.Generic;
using UnityEngine;

namespace Ironfront.Core
{
    /// <summary>
    /// Computes formation slots and assigns units to them. Pure math, no scene access.
    /// </summary>
    public static class FormationCalculator
    {
        /// <summary>
        /// Grid formation centred on <paramref name="center"/>, with rows facing <paramref name="forward"/>
        /// (projected onto the XZ plane). The last row is centred when it is not full.
        /// </summary>
        public static List<Vector3> GetGridSlots(Vector3 center, Vector3 forward, int count, float spacing)
        {
            var slots = new List<Vector3>(Mathf.Max(count, 0));
            if (count <= 0) return slots;

            forward.y = 0f;
            forward = forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward);

            int columns = Mathf.CeilToInt(Mathf.Sqrt(count));
            int rows = Mathf.CeilToInt(count / (float)columns);

            for (int i = 0; i < count; i++)
            {
                int row = i / columns;
                int col = i % columns;
                int inRow = Mathf.Min(columns, count - row * columns);

                float x = (col - (inRow - 1) * 0.5f) * spacing;
                // Row 0 is the front row (furthest along forward).
                float z = ((rows - 1) * 0.5f - row) * spacing;

                slots.Add(center + right * x + forward * z);
            }
            return slots;
        }

        /// <summary>
        /// Greedy nearest-pair assignment of units to slots, which keeps paths short and limits crossing.
        /// Returns, for each unit index, the index of its slot. Requires slots.Count >= units.Count.
        /// </summary>
        public static int[] AssignSlots(IReadOnlyList<Vector3> unitPositions, IReadOnlyList<Vector3> slots)
        {
            int unitCount = unitPositions.Count;
            var result = new int[unitCount];
            if (unitCount == 0) return result;
            if (slots.Count < unitCount)
                throw new System.ArgumentException("Need at least one slot per unit.", nameof(slots));

            var pairs = new List<(float dist, int unit, int slot)>(unitCount * slots.Count);
            for (int u = 0; u < unitCount; u++)
                for (int s = 0; s < slots.Count; s++)
                    pairs.Add(((unitPositions[u] - slots[s]).sqrMagnitude, u, s));

            pairs.Sort((a, b) =>
            {
                int c = a.dist.CompareTo(b.dist);
                if (c != 0) return c;
                c = a.unit.CompareTo(b.unit);
                return c != 0 ? c : a.slot.CompareTo(b.slot);
            });

            var unitDone = new bool[unitCount];
            var slotTaken = new bool[slots.Count];
            int assigned = 0;
            foreach (var p in pairs)
            {
                if (unitDone[p.unit] || slotTaken[p.slot]) continue;
                result[p.unit] = p.slot;
                unitDone[p.unit] = true;
                slotTaken[p.slot] = true;
                if (++assigned == unitCount) break;
            }
            return result;
        }

        public static Vector3 Centroid(IReadOnlyList<Vector3> points)
        {
            if (points.Count == 0) return Vector3.zero;
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < points.Count; i++) sum += points[i];
            return sum / points.Count;
        }
    }
}
