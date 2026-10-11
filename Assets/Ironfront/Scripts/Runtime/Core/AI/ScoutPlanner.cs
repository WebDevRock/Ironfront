using System;
using Ironfront.Core.Vision;

namespace Ironfront.Core.AI
{
    /// <summary>
    /// Picks where a scout should go next using only the faction's own exploration record: the nearest
    /// unexplored sample point, with a bias towards a point of interest (e.g. the presumed enemy side) and
    /// away from points recently assigned. Pure and deterministic.
    /// </summary>
    public static class ScoutPlanner
    {
        /// <param name="spacing">Distance between candidate sample points (metres).</param>
        /// <param name="interestX">Point the commander most wants to learn about (enemy direction).</param>
        /// <param name="interestWeight">0 = pure nearest-first, 1 = strongly prefer points near the interest.</param>
        /// <returns>False when everything sampled is already explored.</returns>
        public static bool NextWaypoint(VisibilityGrid grid, int faction, float fromX, float fromZ, float interestX, float interestZ,
            float spacing, float interestWeight, out float x, out float z, float avoidX = float.NaN, float avoidZ = float.NaN,
            float avoidRadius = 0f)
        {
            x = z = 0f;
            if (grid == null || spacing <= 0f) return false;
            float best = float.MaxValue;
            bool found = false;
            float sizeX = grid.Width * grid.CellSize, sizeZ = grid.Height * grid.CellSize;
            for (float pz = grid.MinZ + spacing * 0.5f; pz < grid.MinZ + sizeZ; pz += spacing)
            {
                for (float px = grid.MinX + spacing * 0.5f; px < grid.MinX + sizeX; px += spacing)
                {
                    if (grid.IsExplored(faction, px, pz)) continue;
                    if (!float.IsNaN(avoidX) && Sq(px - avoidX) + Sq(pz - avoidZ) < avoidRadius * avoidRadius) continue;
                    float travel = (float)Math.Sqrt(Sq(px - fromX) + Sq(pz - fromZ));
                    float interest = (float)Math.Sqrt(Sq(px - interestX) + Sq(pz - interestZ));
                    float score = travel * (1f - interestWeight) + interest * interestWeight;
                    if (score < best)
                    {
                        best = score;
                        x = px;
                        z = pz;
                        found = true;
                    }
                }
            }
            return found;
        }

        private static float Sq(float v) => v * v;
    }
}
