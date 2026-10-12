using System;
using System.Collections.Generic;
using Ironfront.Core.Procedural;
using Ironfront.EditorTools.Art;
using UnityEngine;

namespace Ironfront.EditorTools
{
    /// <summary>
    /// Milestone 008 (with the larger-maps addendum): the four skirmish battlefields as data. Each layout is generated
    /// deterministically from its seed with a symmetry (dihedral D4 for the 8-start maps, point symmetry for
    /// Blackstone Basin) so that start positions are equivalent by construction; the map validator then measures
    /// every start and every pair (separation, fairness, reachability) and decides which pairs may be used.
    /// </summary>
    public static class SkirmishMapLayouts
    {
        public sealed class Slot
        {
            public int Id;
            public string Name;
            public Vector3 Hq;
            /// <summary>+1 / -1: towards the map centre along x, and the build facing along z (+1 = fronts north).</summary>
            public int Sx => Hq.x > 0.5f ? -1 : 1;
            public int Fz => Hq.z > 0f ? -1 : 1;
            public Vector3 Refinery => Hq + new Vector3(13f * Sx, 0f, 1f * Fz);
            public Vector3 Harvester => Hq + new Vector3(14f * Sx, 0f, 10f * Fz);
            public float HarvesterYaw => Sx > 0 ? 90f : -90f;
            public Vector3 HomeA => Hq + new Vector3(28f * Sx, 0f, -1f * Fz);
            public Vector3 HomeB => Hq + new Vector3(29f * Sx, 0f, 7f * Fz);
        }

        public sealed class Layout
        {
            public string Id, Name, Description, Tagline;
            public float Size;
            public int Seed;
            public MapPalette Palette;
            public float FogCell = 2f;
            public float MinSpawnSeparation;
            public float MaxSpawnImbalance = 0.12f;
            public int HomeOre = 5000;
            public readonly List<Slot> Slots = new List<Slot>();
            public readonly List<(string name, Vector3 ground, Vector3 size)> Obstacles = new List<(string, Vector3, Vector3)>();
            public readonly List<(string name, Vector3 position, int amount)> Ore = new List<(string, Vector3, int)>();
            public readonly List<Vector3[]> Roads = new List<Vector3[]>();
            public readonly List<Vector3> Strategic = new List<Vector3>();

            public string SceneName => "Skirmish_" + Name.Replace(" ", "");
            public string ScenePath => $"{SkirmishMapBuilder.ScenesDir}/{SceneName}.unity";
            public float Half => Size * 0.5f;

            public (string name, Vector3 center, Vector3 size)[] ObstacleBoxes()
            {
                var list = new (string, Vector3, Vector3)[Obstacles.Count];
                for (int i = 0; i < Obstacles.Count; i++)
                {
                    var (n, g, s) = Obstacles[i];
                    list[i] = (n, new Vector3(g.x, s.y * 0.5f, g.z), s);
                }
                return list;
            }
        }

        public static IReadOnlyList<Layout> All => new[] { DustfallPlains(), IronPass(), GreenDivide(), BlackstoneBasin() };

        // ---------- Symmetry ----------

        /// <summary>The eight symmetries of the square (x, z) → (x', z'); <c>swap</c> = the box's x/z extents swap.</summary>
        private static readonly (Func<Vector2, Vector2> map, bool swap)[] D4 =
        {
            (p => new Vector2(p.x, p.y), false), (p => new Vector2(-p.y, p.x), true), (p => new Vector2(-p.x, -p.y), false), (p => new Vector2(p.y, -p.x), true),
            (p => new Vector2(p.x, -p.y), false), (p => new Vector2(-p.x, p.y), false), (p => new Vector2(p.y, p.x), true), (p => new Vector2(-p.y, -p.x), true),
        };

        private static readonly (Func<Vector2, Vector2> map, bool swap)[] PointSym =
        {
            (p => new Vector2(p.x, p.y), false), (p => new Vector2(-p.x, -p.y), false),
        };

        private static IEnumerable<(Vector2 p, bool swap)> Orbit(Vector2 p, (Func<Vector2, Vector2> map, bool swap)[] group)
        {
            var seen = new List<Vector2>();
            foreach (var (map, swap) in group)
            {
                var q = map(p);
                bool dup = false;
                foreach (var s in seen) if ((s - q).sqrMagnitude < 1f) { dup = true; break; }
                if (dup) continue;
                seen.Add(q);
                yield return (q, swap);
            }
        }

        private static Vector3 V(Vector2 p) => new Vector3(p.x, 0f, p.y);
        private static Vector2 Polar(float r, float deg) => new Vector2(r * Mathf.Cos(deg * Mathf.Deg2Rad), r * Mathf.Sin(deg * Mathf.Deg2Rad));

        private static void OreOrbit(Layout l, string name, Vector2 p, int amount, (Func<Vector2, Vector2>, bool)[] group)
        {
            int i = 0;
            foreach (var (q, _) in Orbit(p, group)) l.Ore.Add(($"{name}_{i++}", V(q), amount));
        }

        private static void ObstacleOrbit(Layout l, string name, Vector2 p, Vector3 size, (Func<Vector2, Vector2>, bool)[] group)
        {
            int i = 0;
            foreach (var (q, swap) in Orbit(p, group))
                l.Obstacles.Add(($"{name}_{i++}", V(q), swap ? new Vector3(size.z, size.y, size.x) : size));
        }

        private static readonly string[] Compass =
        {
            "East", "East-north-east", "North-east", "North-north-east", "North", "North-north-west", "North-west", "West-north-west",
            "West", "West-south-west", "South-west", "South-south-west", "South", "South-south-east", "South-east", "East-south-east",
        };

        public static string CompassName(Vector3 p)
        {
            float a = Mathf.Atan2(p.z, p.x) * Mathf.Rad2Deg;
            if (a < 0f) a += 360f;
            return Compass[Mathf.RoundToInt(a / 22.5f) % 16];
        }

        /// <summary>Adds slots (ids in order of angle, counter-clockwise from east) and each slot's home ore field.</summary>
        private static void AddSlots(Layout l, IEnumerable<Vector2> positions, string suffix = "")
        {
            var list = new List<Vector2>(positions);
            list.Sort((a, b) => Angle(a).CompareTo(Angle(b)));
            foreach (var p in list)
            {
                var s = new Slot { Id = l.Slots.Count, Hq = V(p) };
                s.Name = CompassName(s.Hq) + suffix;
                l.Slots.Add(s);
                l.Ore.Add(($"Ore_Home{s.Id}_A", s.HomeA, l.HomeOre));
                l.Ore.Add(($"Ore_Home{s.Id}_B", s.HomeB, l.HomeOre));
                // Road from the base towards the map centre and to the home field.
                var towards = Vector3.Lerp(s.Hq, Vector3.zero, 0.45f);
                l.Roads.Add(new[] { s.Hq + new Vector3(0f, 0f, 6f * s.Fz), s.Refinery + new Vector3(0f, 0f, 5f * s.Fz), Vector3.Lerp(s.HomeA, s.HomeB, 0.5f) });
                l.Roads.Add(new[] { s.Hq + new Vector3(0f, 0f, 6f * s.Fz), Vector3.Lerp(s.Hq, towards, 0.5f) + new Vector3(6f, 0f, -4f), towards });
            }
        }

        private static float Angle(Vector2 p)
        {
            float a = Mathf.Atan2(p.y, p.x);
            return a < 0f ? a + Mathf.PI * 2f : a;
        }

        /// <summary>Is the ground point clear of bases, ore fields, strategic points and existing obstacles (with margins)?</summary>
        private static bool Clear(Layout l, Vector2 c, Vector2 halfSize, float baseRadius, float oreRadius, float gap)
        {
            float reach = Mathf.Sqrt(halfSize.x * halfSize.x + halfSize.y * halfSize.y);
            if (Mathf.Abs(c.x) + halfSize.x > l.Half - 6f || Mathf.Abs(c.y) + halfSize.y > l.Half - 6f) return false;
            foreach (var s in l.Slots)
            {
                var hq = new Vector2(s.Hq.x, s.Hq.z);
                var home = new Vector2((s.HomeA.x + s.HomeB.x) * 0.5f, (s.HomeA.z + s.HomeB.z) * 0.5f);
                if ((c - hq).magnitude < baseRadius + reach || (c - home).magnitude < oreRadius + 6f + reach) return false;
            }
            foreach (var (_, p, _) in l.Ore)
                if ((c - new Vector2(p.x, p.z)).magnitude < oreRadius + reach) return false;
            foreach (var p in l.Strategic)
                if ((c - new Vector2(p.x, p.z)).magnitude < 12f + reach) return false;
            foreach (var (_, g, s) in l.Obstacles)
                if (Mathf.Abs(c.x - g.x) < halfSize.x + s.x * 0.5f + gap && Mathf.Abs(c.y - g.z) < halfSize.y + s.z * 0.5f + gap) return false;
            return true;
        }

        /// <summary>
        /// Scatters obstacles in a region (given by a sampler), replicated by the symmetry group; rejected when any copy
        /// would crowd a base, an ore field, a strategic point or another obstacle.
        /// </summary>
        private static void Scatter(Layout l, System.Random rng, int count, string family, Func<System.Random, Vector2> sample,
            Func<System.Random, Vector3> size, (Func<Vector2, Vector2>, bool)[] group, float baseRadius = 46f, float oreRadius = 14f, float gap = 9f)
        {
            int placed = 0;
            for (int attempt = 0; attempt < count * 40 && placed < count; attempt++)
            {
                var p = sample(rng);
                var s = size(rng);
                bool ok = true;
                foreach (var (q, swap) in Orbit(p, group))
                {
                    var half = swap ? new Vector2(s.z, s.x) * 0.5f : new Vector2(s.x, s.z) * 0.5f;
                    if (!Clear(l, q, half, baseRadius, oreRadius, gap)) { ok = false; break; }
                }
                if (!ok) continue;
                ObstacleOrbit(l, $"{family}{placed:00}", p, s, group);
                placed++;
            }
        }

        private static float R(System.Random rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);

        /// <summary>A point in the D4 fundamental wedge (angles 0..45 degrees) between two radii.</summary>
        private static Func<System.Random, Vector2> Wedge(float rMin, float rMax, float aMin = 3f, float aMax = 42f) =>
            rng => Polar(Mathf.Sqrt(R(rng, rMin * rMin, rMax * rMax)), R(rng, aMin, aMax));

        // ---------- Dustfall Plains ----------

        /// <summary>600 m open desert: eight starts on a wide ring, mesas and dunes, rich fields everywhere.</summary>
        public static Layout DustfallPlains()
        {
            var l = new Layout
            {
                Id = "dustfall_plains", Name = "Dustfall Plains", Size = 600f, Seed = 21, Palette = MapPalette.Desert,
                MinSpawnSeparation = 300f, HomeOre = 6000, FogCell = 2f,
                Tagline = "Open desert · 600 m · 8 starts · generous ore",
                Description = "Vast sand flats broken by mesas and dune ridges. Eight possible starts on a wide ring; " +
                              "the enemy could be anywhere, so scout early. Broad lanes and rich ore favour big armoured battles.",
            };
            var rng = new System.Random(l.Seed);
            var slots = new List<Vector2>();
            foreach (var (q, _) in Orbit(Polar(215f, 22.5f), D4)) slots.Add(q);
            AddSlots(l, slots);
            OreOrbit(l, "Ore_Outer", Polar(255f, 0f), 7000, D4);
            OreOrbit(l, "Ore_Ring", Polar(135f, 22.5f), 5000, D4);
            OreOrbit(l, "Ore_Contested_Centre", Polar(40f, 45f), 6000, D4);
            OreOrbit(l, "Ore_Contested_Axis", Polar(95f, 0f), 5000, D4);
            foreach (var (q, _) in Orbit(Polar(60f, 22.5f), D4)) l.Strategic.Add(V(q));
            Scatter(l, rng, 6, "DP_Mesa", Wedge(30f, 285f), r => new Vector3(R(r, 8f, 16f), R(r, 5f, 9f), R(r, 7f, 13f)), D4);
            Scatter(l, rng, 6, "DP_Dune", Wedge(30f, 285f), r => r.NextDouble() < 0.5 ? new Vector3(R(r, 16f, 28f), 1.6f, 3.5f) : new Vector3(3.5f, 1.6f, R(r, 16f, 28f)), D4);
            Scatter(l, rng, 4, "DP_Rock", Wedge(30f, 285f), r => new Vector3(R(r, 3f, 6f), R(r, 2f, 3.5f), R(r, 3f, 6f)), D4);
            return l;
        }

        // ---------- Iron Pass ----------

        /// <summary>
        /// 400 m mountains: ridges on x = ±67 and z = ±67 cut the map into nine valleys joined by narrow passes. The eight
        /// outer valleys hold the starts; the central basin holds the richest, most contested ore.
        /// </summary>
        public static Layout IronPass()
        {
            var l = new Layout
            {
                Id = "iron_pass", Name = "Iron Pass", Size = 400f, Seed = 33, Palette = MapPalette.Mountain,
                MinSpawnSeparation = 200f, HomeOre = 5000, FogCell = 2f, MaxSpawnImbalance = 0.12f,
                Tagline = "Mountain valleys · 400 m · 8 starts · chokepoints",
                Description = "Ridges split the map into nine valleys joined by narrow passes. Eight valleys hold the starts; " +
                              "the central basin holds the richest ore. Hold the passes, shell over the ridges, and scout " +
                              "each valley to find the enemy.",
            };
            var rng = new System.Random(l.Seed);
            const float line = 67f, thick = 12f, height = 10f, pass = 10f;
            // Pass centres along each ridge line: the boundary between two outer valleys (±134) and between an edge
            // valley and the basin (0).
            float[] passes = { -134f, 0f, 134f };
            int seg = 0;
            foreach (float side in new[] { -line, line })
            {
                float from = -l.Half;
                var cuts = new List<float>();
                foreach (float p in passes) { cuts.Add(p - pass * 0.5f); cuts.Add(p + pass * 0.5f); }
                cuts.Add(l.Half);
                bool solid = true;
                foreach (float to in cuts)
                {
                    if (solid)
                        for (float a = from; a < to - 0.5f; a += 22f)
                        {
                            float b = Mathf.Min(to, a + 22f), c = (a + b) * 0.5f, len = b - a;
                            float h = height * R(rng, 0.85f, 1.15f);
                            l.Obstacles.Add(($"IP_Ridge_V{seg++}", new Vector3(side, 0f, c), new Vector3(thick, h, len)));
                            l.Obstacles.Add(($"IP_Ridge_H{seg++}", new Vector3(c, 0f, side), new Vector3(len, h, thick)));
                        }
                    from = to;
                    solid = !solid;
                }
            }
            foreach (float side in new[] { -line, line })
                foreach (float p in passes)
                {
                    l.Strategic.Add(new Vector3(side, 0f, p));
                    l.Strategic.Add(new Vector3(p, 0f, side));
                }

            var slots = new List<Vector2>();
            foreach (var (q, _) in Orbit(new Vector2(150f, 0f), D4)) slots.Add(q);
            foreach (var (q, _) in Orbit(new Vector2(142f, 142f), D4)) slots.Add(q);
            AddSlots(l, slots, " valley");
            // Corner valleys have no pass to the basin: an extra valley field compensates.
            OreOrbit(l, "Ore_UpperValley", Polar(250f, 45f), 6000, D4);
            OreOrbit(l, "Ore_EdgeValley", new Vector2(115f, 45f), 3500, D4);
            OreOrbit(l, "Ore_Contested_Basin", Polar(30f, 45f), 6000, D4);
            l.Obstacles.Add(("IP_Rock_Spire", Vector3.zero, new Vector3(10f, 8f, 10f)));
            // Crags inside the valleys (kept away from the ridge lines and passes).
            Scatter(l, rng, 3, "IP_Rock", rng2 =>
            {
                var p = new Vector2(R(rng2, 80f, 190f), R(rng2, 5f, 190f));
                return p.y > p.x ? new Vector2(p.y, p.x) : p;
            }, r => new Vector3(R(r, 5f, 9f), R(r, 3f, 6f), R(r, 5f, 9f)), D4, baseRadius: 44f, gap: 12f);
            return l;
        }

        // ---------- Green Divide ----------

        /// <summary>600 m forest: a ring-shaped forest divide with eight gaps, woods everywhere else, glades with ore.</summary>
        public static Layout GreenDivide()
        {
            var l = new Layout
            {
                Id = "green_divide", Name = "Green Divide", Size = 600f, Seed = 45, Palette = MapPalette.Forest,
                MinSpawnSeparation = 300f, HomeOre = 4000, FogCell = 2f,
                Tagline = "Forest and glades · 600 m · 8 starts · expansion",
                Description = "A ring of forest divides the outer lands from the central glades, broken by eight gaps; woods " +
                              "hide many side routes. Home fields are small, so expanding into the glades — and scouting the " +
                              "woods for the enemy — wins the game.",
            };
            var slots = new List<Vector2>();
            foreach (var (q, _) in Orbit(Polar(220f, 22.5f), D4)) slots.Add(q);
            AddSlots(l, slots);
            OreOrbit(l, "Ore_Glade", Polar(165f, 22.5f), 5000, D4);
            OreOrbit(l, "Ore_Glade_Axis", Polar(215f, 0f), 6000, D4);
            OreOrbit(l, "Ore_Glade_Corner", Polar(270f, 45f), 5000, D4);
            OreOrbit(l, "Ore_Contested_Inner", Polar(55f, 0f), 6000, D4);
            foreach (var (q, _) in Orbit(Polar(125f, 22.5f), D4)) l.Strategic.Add(V(q)); // the divide's gaps

            // Forest cells on a 10 m grid inside the fundamental wedge: the divide ring (r 115-135, gap at 22.5 deg)
            // plus noise-shaped woods elsewhere; consecutive cells in a row merge into one obstacle.
            const float cell = 10f;
            int forest = 0;
            int nx = Mathf.FloorToInt(l.Half / cell);
            for (float z = cell * 0.5f; z < l.Half; z += cell)
            {
                var wood = new bool[nx];
                for (int i = 0; i < nx; i++)
                {
                    float x = cell * 0.5f + i * cell;
                    if (z > x) continue; // fundamental wedge: 0..45 degrees
                    float r = Mathf.Sqrt(x * x + z * z), a = Mathf.Atan2(z, x) * Mathf.Rad2Deg;
                    bool divide = r > 115f && r < 135f && Mathf.Abs(a - 22.5f) > 6.5f;
                    float n = ProceduralNoise.Fbm(x / l.Size + 0.5f, z / l.Size + 0.5f, 7, 3, l.Seed, tileable: false);
                    bool woods = n > 0.5f && r > 145f;
                    wood[i] = (divide || woods) && Clear(l, new Vector2(x, z), new Vector2(cell, cell) * 0.5f, 44f, 14f, -0.5f);
                }
                int run = -1;
                for (int i = 0; i <= nx; i++)
                {
                    bool w = i < nx && wood[i];
                    if (w && run < 0) { run = i; continue; }
                    if (run >= 0 && (!w || i - run >= 4))
                    {
                        float x0 = run * cell, x1 = i * cell;
                        ObstacleOrbit(l, $"GD_Forest{forest++:000}", new Vector2((x0 + x1) * 0.5f, z), new Vector3(x1 - x0, 6f + (forest % 3), cell), D4);
                        run = w ? i : -1;
                    }
                }
            }
            var rng = new System.Random(l.Seed);
            Scatter(l, rng, 3, "GD_Rock", Wedge(40f, 280f), r => new Vector3(R(r, 3f, 5f), R(r, 2f, 3f), R(r, 3f, 5f)), D4);
            return l;
        }

        // ---------- Blackstone Basin ----------

        /// <summary>
        /// 400 m industrial wasteland, point-symmetric, six starts. Ore is deliberately uneven — a rich central basin,
        /// deep seams on the flanks and small scattered fields — balanced by an equal yard field at every start.
        /// </summary>
        public static Layout BlackstoneBasin()
        {
            var l = new Layout
            {
                Id = "blackstone_basin", Name = "Blackstone Basin", Size = 400f, Seed = 57, Palette = MapPalette.Industrial,
                MinSpawnSeparation = 200f, HomeOre = 4000, FogCell = 2f, MaxSpawnImbalance = 0.15f,
                Tagline = "Industrial wasteland · 400 m · 6 starts · map control",
                Description = "Ruined works, pipelines and slag heaps around a rich central basin. Every start has an equal " +
                              "yard field, but the rest of the ore is uneven: deep seams on the flanks, scattered small " +
                              "fields, and the richest deposits in the open basin. Control the map to control the economy.",
            };
            var slots = new List<Vector2> { new Vector2(-150f, -146f), new Vector2(8f, -160f), new Vector2(152f, -128f) };
            var all = new List<Vector2>();
            foreach (var s in slots) foreach (var (q, _) in Orbit(s, PointSym)) all.Add(q);
            AddSlots(l, all, " works");
            OreOrbit(l, "Ore_Contested_Basin", new Vector2(-22f, 8f), 6000, PointSym);
            l.Ore.Add(("Ore_Contested_Basin_Core", Vector3.zero, 8000));
            OreOrbit(l, "Ore_Seam", new Vector2(-150f, 15f), 7000, PointSym);
            OreOrbit(l, "Ore_SeamB", new Vector2(-160f, -10f), 4000, PointSym);
            var rng = new System.Random(l.Seed);
            for (int i = 0; i < 5; i++)
            {
                var p = new Vector2(R(rng, -170f, 170f), R(rng, -120f, -40f));
                if (Clear(l, p, new Vector2(4f, 4f), 50f, 25f, 0f)) OreOrbit(l, $"Ore_Scrap{i}", p, 3000, PointSym);
            }
            l.Strategic.Add(new Vector3(0f, 0f, -40f));
            l.Strategic.Add(new Vector3(-90f, 0f, 0f));
            l.Strategic.Add(new Vector3(0f, 0f, 40f));
            l.Strategic.Add(new Vector3(90f, 0f, 0f));

            Func<System.Random, Vector2> south = r => new Vector2(R(r, -188f, 188f), R(r, -188f, -4f));
            Scatter(l, rng, 16, "BS_Ruin", south, r => new Vector3(R(r, 8f, 14f), R(r, 4f, 6f), R(r, 6f, 11f)), PointSym);
            Scatter(l, rng, 12, "BS_Pipe", south, r => r.NextDouble() < 0.5 ? new Vector3(R(r, 18f, 34f), 2.5f, 2f) : new Vector3(2f, 2.5f, R(r, 18f, 34f)), PointSym);
            Scatter(l, rng, 8, "BS_Stack", south, r => new Vector3(3f, R(r, 12f, 18f), 3f), PointSym);
            Scatter(l, rng, 7, "BS_Tanks", south, r => new Vector3(R(r, 10f, 14f), 6f, 5f), PointSym);
            Scatter(l, rng, 12, "BS_Slag", south, r => new Vector3(R(r, 6f, 10f), R(r, 3f, 5f), R(r, 6f, 10f)), PointSym);
            return l;
        }
    }
}
