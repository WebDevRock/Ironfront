using UnityEngine;

namespace Ironfront.EditorTools.Art
{
    /// <summary>
    /// Milestone 008 terrain families for the skirmish maps. Each builds a styled mesh that stays inside the obstacle's
    /// collider box (the gameplay shape, see <see cref="EnvironmentBuilder.Obstacles"/>); the box itself is unchanged.
    /// Chosen by keyword in the obstacle name: Ridge, Mesa, Dune, Forest, Stack, Pipe, Ruin, Tanks, Slag.
    /// </summary>
    public static class TerrainStyles
    {
        /// <summary>Materials tinted for one map (falls back to the shared Milestone 006 materials).</summary>
        public sealed class Mats
        {
            public Material Rock, RockDark, Shrub, Foliage, FoliageDark, Bark, Sand, Rust;
        }

        public static Mats For(MapPalette p, ArtLibrary a)
        {
            if (p == null || p.IsDefault)
                return new Mats { Rock = a.Rock, RockDark = a.RockDark, Shrub = a.Shrub, Foliage = a.Shrub, FoliageDark = a.Shrub, Bark = a.RockDark, Sand = a.Rock, Rust = a.PaintIndustrial };
            Material M(string n, Color c, float smooth = 0.08f) => ArtLibrary.Lit($"{n}_{p.Name}", c, smooth, 0f, a.Detail, 1f);
            return new Mats
            {
                Rock = p.Rock.a > 0f ? M("Rock", p.Rock) : a.Rock,
                RockDark = p.RockDark.a > 0f ? M("RockDark", p.RockDark) : a.RockDark,
                Shrub = p.Shrub.a > 0f ? M("Shrub", p.Shrub, 0.04f) : a.Shrub,
                Foliage = M("Foliage", p.Foliage, 0.05f),
                FoliageDark = M("FoliageDark", p.FoliageDark, 0.05f),
                Bark = M("Bark", p.Bark),
                Sand = M("Sand", Color.Lerp(p.Grass, p.Patch.a > 0f ? (Color)p.Patch : p.Grass, 0.6f) * 1.04f, 0.05f),
                Rust = ArtLibrary.Lit("Rust_" + p.Name, new Color(0.42f, 0.24f, 0.14f), 0.2f, 0.25f, a.Detail, 1f),
            };
        }

        /// <summary>Builds the mesh for a styled obstacle. Returns false when the name has none of these keywords.</summary>
        public static bool Build(MeshKit k, string name, Vector3 size, Vector3 floor, ArtLibrary a, Mats m, System.Random rng)
        {
            if (name.Contains("Ridge")) Ridge(k, m, size, floor, rng);
            else if (name.Contains("Mesa")) Mesa(k, m, size, floor, rng);
            else if (name.Contains("Dune")) Dune(k, m, size, floor, rng);
            else if (name.Contains("Forest")) Forest(k, m, size, floor, rng);
            else if (name.Contains("Stack")) Stack(k, a, m, size, floor);
            else if (name.Contains("Pipe")) Pipe(k, a, m, size, floor);
            else if (name.Contains("Ruin")) Ruin(k, a, m, size, floor, rng);
            else if (name.Contains("Tanks")) Tanks(k, a, m, size, floor, rng);
            else if (name.Contains("Slag")) Slag(k, m, size, floor, rng);
            else return false;
            return true;
        }

        private static float R(System.Random rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);

        /// <summary>Mountain ridge: a chain of tall faceted crags along the long axis with scree at the feet.</summary>
        private static void Ridge(MeshKit k, Mats m, Vector3 size, Vector3 floor, System.Random rng)
        {
            bool alongX = size.x >= size.z;
            float length = alongX ? size.x : size.z, thick = alongX ? size.z : size.x;
            int n = Mathf.Max(1, Mathf.RoundToInt(length / (thick * 0.7f)));
            for (int i = 0; i < n; i++)
            {
                float t = -length * 0.5f + length * (i + 0.5f) / n;
                float along = t + R(rng, -0.2f, 0.2f) * thick * 0.3f;
                float side = R(rng, -0.1f, 0.1f) * thick;
                var p = floor + (alongX ? new Vector3(along, 0f, side) : new Vector3(side, 0f, along));
                float w = thick * R(rng, 0.62f, 0.72f);
                EnvironmentBuilder.Boulder(k, (i % 3 == 1) ? m.RockDark : m.Rock, p, w, size.y * R(rng, 0.72f, 1f), rng);
            }
            // Lower shoulders between the crags (keeps the silhouette continuous).
            for (int i = 0; i <= n; i++)
            {
                float t = -length * 0.5f + length * i / n;
                t = Mathf.Clamp(t, -length * 0.5f + thick * 0.35f, length * 0.5f - thick * 0.35f);
                var p = floor + (alongX ? new Vector3(t, 0f, 0f) : new Vector3(0f, 0f, t));
                EnvironmentBuilder.Boulder(k, m.RockDark, p, thick * 0.6f, size.y * R(rng, 0.38f, 0.55f), rng);
            }
        }

        /// <summary>Desert mesa: stepped sandstone block with darker strata and a flat top.</summary>
        private static void Mesa(MeshKit k, Mats m, Vector3 size, Vector3 floor, System.Random rng)
        {
            var b = new Vector2(size.x * 0.96f, size.z * 0.96f);
            float h1 = size.y * 0.42f, h2 = size.y * 0.36f;
            k.Block(m.Rock, floor, b, b * 0.88f, h1);
            k.Block(m.RockDark, floor + Vector3.up * h1, b * 0.88f, b * 0.86f, size.y * 0.05f);
            k.Block(m.Rock, floor + Vector3.up * (h1 + size.y * 0.05f), b * 0.86f, b * 0.72f, h2, new Vector2(R(rng, -0.3f, 0.3f), R(rng, -0.3f, 0.3f)));
            k.Block(m.RockDark, floor + Vector3.up * (h1 + h2 + size.y * 0.05f), b * 0.72f, b * 0.66f, size.y * 0.17f);
            for (int i = 0; i < 5; i++)
            {
                var p = floor + new Vector3(R(rng, -0.42f, 0.42f) * size.x, 0f, R(rng, -0.42f, 0.42f) * size.z);
                EnvironmentBuilder.Boulder(k, m.RockDark, p, Mathf.Min(size.x, size.z) * 0.18f, size.y * 0.15f, rng);
            }
        }

        /// <summary>Low wind-shaped sand ridge.</summary>
        private static void Dune(MeshKit k, Mats m, Vector3 size, Vector3 floor, System.Random rng)
        {
            bool alongX = size.x >= size.z;
            float length = alongX ? size.x : size.z, thick = alongX ? size.z : size.x;
            int n = Mathf.Max(1, Mathf.RoundToInt(length / thick));
            for (int i = 0; i < n; i++)
            {
                float t = -length * 0.5f + length * (i + 0.5f) / n;
                var p = floor + (alongX ? new Vector3(t, 0f, 0f) : new Vector3(0f, 0f, t));
                float r = thick * 0.48f, h = size.y * R(rng, 0.8f, 1f);
                k.Push(p, alongX ? Quaternion.identity : Quaternion.Euler(0f, 90f, 0f), new Vector3(length / n / thick * 1.25f, 1f, 1f));
                k.Lathe(m.Sand, new[] { new Vector2(r, 0f), new Vector2(r * 0.86f, h * 0.35f), new Vector2(r * 0.5f, h * 0.8f), new Vector2(0f, h) }, 10, Vector3.zero, capTop: false);
                k.Pop();
            }
        }

        /// <summary>Stand of conifers and broadleaf trees filling the box, with undergrowth.</summary>
        private static void Forest(MeshKit k, Mats m, Vector3 size, Vector3 floor, System.Random rng)
        {
            // Large woods (Milestone 008 maps) are thinner per square metre to keep the vertex count reasonable.
            bool large = size.x * size.z > 150f;
            float perTree = large ? 45f : 5.5f;
            int trees = Mathf.Max(2, Mathf.RoundToInt(size.x * size.z / perTree));
            for (int i = 0; i < trees; i++)
            {
                float spread = 0.5f - 0.9f / Mathf.Max(1.8f, Mathf.Min(size.x, size.z));
                var p = floor + new Vector3(R(rng, -spread, spread) * size.x, 0f, R(rng, -spread, spread) * size.z);
                float h = size.y * R(rng, 0.6f, 1f);
                k.Cylinder(m.Bark, p, 0.13f, h * 0.35f, 5);
                if (rng.NextDouble() < 0.6)
                {
                    // Conifer: three stacked cones.
                    float r = R(rng, 0.85f, 1.15f) * (large ? 1.35f : 1f);
                    for (int tier = 0, tiers = large ? 2 : 3; tier < tiers; tier++)
                    {
                        float y0 = h * (0.22f + tier * 0.22f), tr = r * (1f - tier * 0.25f);
                        k.Lathe(tier == 1 ? m.FoliageDark : m.Foliage, new[] { new Vector2(tr, 0f), new Vector2(0f, h * 0.4f) }, 7, p + Vector3.up * y0,
                            capTop: false, capBottom: true, startAngle: R(rng, 0f, 1f));
                    }
                }
                else
                {
                    // Broadleaf: two overlapping rounded crowns.
                    for (int c = 0; c < 2; c++)
                    {
                        float cr = R(rng, 0.8f, 1.1f) * (large ? 1.35f : 1f);
                        var cp = p + new Vector3(R(rng, -0.3f, 0.3f), h * (0.45f + c * 0.18f), R(rng, -0.3f, 0.3f));
                        k.Lathe(c == 0 ? m.FoliageDark : m.Foliage, new[]
                        {
                            new Vector2(cr * 0.5f, 0f), new Vector2(cr, cr * 0.45f), new Vector2(cr * 0.85f, cr * 0.95f), new Vector2(0f, cr * 1.25f),
                        }, 7, cp, capTop: false, capBottom: true, startAngle: R(rng, 0f, 1f));
                    }
                }
            }
            // Undergrowth along the edge.
            int bushes = Mathf.RoundToInt((size.x + size.z) * (large ? 0.2f : 0.6f));
            for (int i = 0; i < bushes; i++)
            {
                var p = floor + new Vector3(R(rng, -0.45f, 0.45f) * size.x, 0f, R(rng, -0.45f, 0.45f) * size.z);
                float r = R(rng, 0.35f, 0.6f);
                k.Lathe(m.Shrub, new[] { new Vector2(r, 0f), new Vector2(r * 1.1f, r * 0.5f), new Vector2(0f, r * 1.1f) }, 6, p, capTop: false);
            }
        }

        /// <summary>Industrial chimney on a concrete plinth, with rust bands.</summary>
        private static void Stack(MeshKit k, ArtLibrary a, Mats m, Vector3 size, Vector3 floor)
        {
            float r = Mathf.Min(size.x, size.z) * 0.5f;
            k.Block(a.ConcreteDark_, floor, new Vector2(size.x, size.z), new Vector2(size.x * 0.92f, size.z * 0.92f), size.y * 0.12f);
            k.Cylinder(a.Concrete_, floor + Vector3.up * size.y * 0.12f, r * 0.62f, size.y * 0.88f, 12, r * 0.42f);
            for (int i = 0; i < 3; i++)
            {
                float y = size.y * (0.4f + i * 0.2f), rr = Mathf.Lerp(r * 0.62f, r * 0.42f, (y - size.y * 0.12f) / (size.y * 0.88f)) + 0.04f;
                k.Cylinder(i == 2 ? a.Hazard : m.Rust, floor + Vector3.up * y, rr, size.y * 0.05f, 12);
            }
            k.Cylinder(a.SteelDark_, floor + Vector3.up * size.y * 0.97f, r * 0.46f, size.y * 0.03f, 12);
        }

        /// <summary>Pipeline: two pipes on steel trestles along the long axis.</summary>
        private static void Pipe(MeshKit k, ArtLibrary a, Mats m, Vector3 size, Vector3 floor)
        {
            bool alongX = size.x >= size.z;
            float length = alongX ? size.x : size.z, thick = alongX ? size.z : size.x;
            k.Push(floor, alongX ? Quaternion.Euler(0f, 90f, 0f) : Quaternion.identity);
            // Local +z is now the pipe direction.
            float pr = Mathf.Min(thick * 0.22f, size.y * 0.22f);
            k.Tube(m.Rust, new Vector3(-thick * 0.22f, size.y * 0.72f, -length * 0.5f), pr, length, 10);
            k.Tube(a.SteelLight_, new Vector3(thick * 0.24f, size.y * 0.5f, -length * 0.5f), pr * 0.75f, length, 10);
            int supports = Mathf.Max(2, Mathf.RoundToInt(length / 4f));
            for (int i = 0; i <= supports; i++)
            {
                float z = Mathf.Lerp(-length * 0.5f + 0.3f, length * 0.5f - 0.3f, (float)i / supports);
                k.Box(a.SteelDark_, new Vector3(0f, size.y * 0.42f, z), new Vector3(thick * 0.9f, 0.12f, 0.2f));
                k.Box(a.SteelDark_, new Vector3(-thick * 0.38f, size.y * 0.21f, z), new Vector3(0.12f, size.y * 0.42f, 0.12f));
                k.Box(a.SteelDark_, new Vector3(thick * 0.38f, size.y * 0.21f, z), new Vector3(0.12f, size.y * 0.42f, 0.12f));
                if (i % 2 == 0) k.Box(a.ConcreteDark_, new Vector3(0f, 0.1f, z), new Vector3(thick * 0.95f, 0.2f, 0.6f));
            }
            k.Pop();
        }

        /// <summary>Gutted factory hall: broken concrete walls, steel frame and rubble.</summary>
        private static void Ruin(MeshKit k, ArtLibrary a, Mats m, Vector3 size, Vector3 floor, System.Random rng)
        {
            float t = 0.45f;
            void Wall(Vector3 c, Vector3 s) => k.Box((rng.NextDouble() < 0.5) ? a.ConcreteDark_ : a.Concrete_, floor + c + Vector3.up * s.y * 0.5f, s, 0.03f);
            int segs = Mathf.Max(2, Mathf.RoundToInt(size.x / 2.5f));
            for (int i = 0; i < segs; i++)
            {
                float w = size.x / segs;
                float x = -size.x * 0.5f + w * (i + 0.5f);
                if (rng.NextDouble() > 0.2) Wall(new Vector3(x, 0f, -size.z * 0.5f + t * 0.5f), new Vector3(w * 0.96f, size.y * R(rng, 0.35f, 1f), t));
                if (rng.NextDouble() > 0.35) Wall(new Vector3(x, 0f, size.z * 0.5f - t * 0.5f), new Vector3(w * 0.96f, size.y * R(rng, 0.3f, 0.9f), t));
            }
            int segz = Mathf.Max(2, Mathf.RoundToInt(size.z / 2.5f));
            for (int i = 0; i < segz; i++)
            {
                float w = (size.z - 2 * t) / segz;
                float z = -size.z * 0.5f + t + w * (i + 0.5f);
                if (rng.NextDouble() > 0.25) Wall(new Vector3(-size.x * 0.5f + t * 0.5f, 0f, z), new Vector3(t, size.y * R(rng, 0.3f, 1f), w * 0.96f));
                if (rng.NextDouble() > 0.25) Wall(new Vector3(size.x * 0.5f - t * 0.5f, 0f, z), new Vector3(t, size.y * R(rng, 0.3f, 0.95f), w * 0.96f));
            }
            // Steel columns and a fallen beam.
            for (int i = 0; i < 4; i++)
            {
                var c = new Vector3((i % 2 == 0 ? -1 : 1) * size.x * 0.3f, 0f, (i < 2 ? -1 : 1) * size.z * 0.3f);
                k.Box(a.SteelDark_, floor + c + Vector3.up * size.y * 0.45f, new Vector3(0.25f, size.y * 0.9f, 0.25f));
            }
            k.Push(floor + new Vector3(0f, size.y * 0.35f, 0f), Quaternion.Euler(0f, R(rng, -20f, 20f), R(rng, 10f, 25f)));
            k.Box(m.Rust, Vector3.zero, new Vector3(size.x * 0.6f, 0.22f, 0.3f));
            k.Pop();
            for (int i = 0; i < 6; i++)
            {
                var p = floor + new Vector3(R(rng, -0.35f, 0.35f) * size.x, 0f, R(rng, -0.35f, 0.35f) * size.z);
                EnvironmentBuilder.Boulder(k, a.ConcreteDark_, p, R(rng, 0.6f, 1.3f), R(rng, 0.3f, 0.7f), rng);
            }
        }

        /// <summary>Tank farm: storage tanks with domed roofs and a ladder.</summary>
        private static void Tanks(MeshKit k, ArtLibrary a, Mats m, Vector3 size, Vector3 floor, System.Random rng)
        {
            bool alongX = size.x >= size.z;
            float length = alongX ? size.x : size.z, thick = alongX ? size.z : size.x;
            int n = Mathf.Max(1, Mathf.RoundToInt(length / thick));
            float r = Mathf.Min(thick, length / n) * 0.45f;
            k.Block(a.ConcreteDark_, floor, new Vector2(size.x, size.z), new Vector2(size.x * 0.97f, size.z * 0.97f), 0.25f);
            for (int i = 0; i < n; i++)
            {
                float t = -length * 0.5f + length * (i + 0.5f) / n;
                var p = floor + Vector3.up * 0.25f + (alongX ? new Vector3(t, 0f, 0f) : new Vector3(0f, 0f, t));
                float h = (size.y - 0.25f) * R(rng, 0.7f, 0.85f);
                var body = (i % 2 == 0) ? a.SteelLight_ : a.PaintIndustrial;
                k.Cylinder(body, p, r, h, 14);
                k.Lathe(a.SteelLight_, new[] { new Vector2(r, 0f), new Vector2(r * 0.7f, (size.y - 0.25f - h) * 0.6f), new Vector2(0f, size.y - 0.25f - h) }, 14,
                    p + Vector3.up * h, capTop: false);
                k.Cylinder(m.Rust, p + Vector3.up * h * 0.3f, r + 0.03f, 0.15f, 14);
                k.Box(a.SteelDark_, p + new Vector3(r + 0.05f, h * 0.5f, 0f), new Vector3(0.08f, h, 0.3f));
            }
        }

        /// <summary>Slag heap: dark, glassy mounds of industrial waste.</summary>
        private static void Slag(MeshKit k, Mats m, Vector3 size, Vector3 floor, System.Random rng)
        {
            float w = Mathf.Min(size.x, size.z);
            EnvironmentBuilder.Boulder(k, m.RockDark, floor, w * 0.85f, size.y, rng);
            int n = Mathf.RoundToInt((size.x + size.z) / w * 1.5f);
            for (int i = 0; i < n; i++)
            {
                var p = floor + new Vector3(R(rng, -0.32f, 0.32f) * size.x, 0f, R(rng, -0.32f, 0.32f) * size.z);
                EnvironmentBuilder.Boulder(k, (i % 2 == 0) ? m.RockDark : m.Rock, p, w * R(rng, 0.35f, 0.55f), size.y * R(rng, 0.4f, 0.75f), rng);
            }
        }
    }
}
