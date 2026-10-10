using System.Collections.Generic;

namespace Ironfront.Core.Combat
{
    /// <summary>A potential target as seen by target acquisition.</summary>
    public readonly struct TargetCandidate<T>
    {
        public readonly T Target;
        public readonly float Distance;
        public readonly bool IsArmed;
        public readonly bool IsStructure;

        public TargetCandidate(T target, float distance, bool isArmed, bool isStructure)
        {
            Target = target;
            Distance = distance;
            IsArmed = isArmed;
            IsStructure = isStructure;
        }
    }

    /// <summary>
    /// Picks what to shoot at: armed units first (they shoot back), then other units, then structures;
    /// nearest wins within each group. Candidates outside <c>maxDistance</c> are ignored.
    /// </summary>
    public static class TargetSelector
    {
        public static bool TryChoose<T>(IEnumerable<TargetCandidate<T>> candidates, float maxDistance, out T best)
        {
            best = default;
            bool found = false;
            int bestRank = int.MaxValue;
            float bestDistance = float.MaxValue;

            foreach (var c in candidates)
            {
                if (c.Distance > maxDistance) continue;
                int rank = c.IsStructure ? 2 : c.IsArmed ? 0 : 1;
                if (rank < bestRank || (rank == bestRank && c.Distance < bestDistance))
                {
                    best = c.Target;
                    bestRank = rank;
                    bestDistance = c.Distance;
                    found = true;
                }
            }
            return found;
        }
    }
}
