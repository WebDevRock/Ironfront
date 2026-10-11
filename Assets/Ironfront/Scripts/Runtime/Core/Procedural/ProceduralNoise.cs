using System;

namespace Ironfront.Core.Procedural
{
    /// <summary>
    /// Deterministic, optionally tileable value noise for procedural textures and placement. Pure maths, no
    /// Unity dependencies: the same seed always produces the same result on every machine.
    /// </summary>
    public static class ProceduralNoise
    {
        /// <summary>Hash of an integer lattice point to [0, 1).</summary>
        public static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)seed * 2246822519u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }

        private static int Wrap(int v, int period) => period <= 0 ? v : ((v % period) + period) % period;

        private static float Smooth(float t) => t * t * (3f - 2f * t);

        /// <summary>
        /// Hermite step: 0 at or below <paramref name="edge0"/>, 1 at or above <paramref name="edge1"/>, smooth in
        /// between (GLSL smoothstep). Note: Unity's Mathf.SmoothStep interpolates between its first two arguments
        /// instead, which is not a mask.
        /// </summary>
        public static float Step(float edge0, float edge1, float x)
        {
            if (edge1 == edge0) return x < edge0 ? 0f : 1f;
            float t = (x - edge0) / (edge1 - edge0);
            t = t < 0f ? 0f : t > 1f ? 1f : t;
            return Smooth(t);
        }

        /// <summary>
        /// Value noise in [0, 1). With <paramref name="period"/> &gt; 0 the noise repeats every
        /// <paramref name="period"/> lattice cells in both axes (seamless tiling).
        /// </summary>
        public static float Value(float x, float y, int seed, int period = 0)
        {
            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
            float fx = Smooth(x - x0), fy = Smooth(y - y0);
            int xa = Wrap(x0, period), xb = Wrap(x0 + 1, period), ya = Wrap(y0, period), yb = Wrap(y0 + 1, period);
            float a = Hash(xa, ya, seed), b = Hash(xb, ya, seed), c = Hash(xa, yb, seed), d = Hash(xb, yb, seed);
            float ab = a + (b - a) * fx, cd = c + (d - c) * fx;
            return ab + (cd - ab) * fy;
        }

        /// <summary>
        /// Fractal (fBm) value noise in [0, 1). <paramref name="u"/>, <paramref name="v"/> in [0, 1) map to one tile
        /// of <paramref name="baseCells"/> cells; every octave doubles the cell count and stays tileable.
        /// </summary>
        public static float Fbm(float u, float v, int baseCells, int octaves, int seed, bool tileable = true)
        {
            float sum = 0f, amplitude = 1f, total = 0f;
            int cells = Math.Max(1, baseCells);
            for (int o = 0; o < octaves; o++)
            {
                sum += Value(u * cells, v * cells, seed + o * 101, tileable ? cells : 0) * amplitude;
                total += amplitude;
                amplitude *= 0.5f;
                cells *= 2;
            }
            return sum / total;
        }
    }
}
