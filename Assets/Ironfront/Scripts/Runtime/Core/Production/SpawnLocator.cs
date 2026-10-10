using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ironfront.Core.Production
{
    /// <summary>Chooses where a newly produced unit appears.</summary>
    public static class SpawnLocator
    {
        /// <summary>
        /// Candidate spawn points: the origin first, then rings of 8·k points at radius k·spacing, each ring
        /// starting in the <paramref name="forward"/> direction.
        /// </summary>
        public static IEnumerable<Vector3> Candidates(Vector3 origin, Vector3 forward, float spacing, int rings)
        {
            forward.y = 0f;
            forward = forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;

            yield return origin;
            for (int ring = 1; ring <= rings; ring++)
            {
                int points = 8 * ring;
                for (int i = 0; i < points; i++)
                {
                    // Alternate either side of forward: 0, +1, -1, +2, -2 ...
                    int step = (i + 1) / 2 * (i % 2 == 0 ? -1 : 1);
                    float angle = step * 360f / points;
                    yield return origin + Quaternion.Euler(0f, angle, 0f) * forward * (ring * spacing);
                }
            }
        }

        /// <summary>Returns the first candidate accepted by <paramref name="isFree"/>.</summary>
        public static bool TryFind(Vector3 origin, Vector3 forward, float spacing, int rings,
            Func<Vector3, bool> isFree, out Vector3 spot)
        {
            foreach (var candidate in Candidates(origin, forward, spacing, rings))
            {
                if (isFree(candidate))
                {
                    spot = candidate;
                    return true;
                }
            }
            spot = origin;
            return false;
        }
    }
}
