using System.Collections.Generic;
using Ironfront.Core.Procedural;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Ironfront.EditorTools.Art
{
    /// <summary>
    /// Milestone 006 environment: painted ground (dirt roads, base pads, ore stains, edge falloff), a backdrop
    /// beyond the playable area with framing ridges, restyled obstacles, decorative props, lighting and
    /// post-processing.
    ///
    /// Gameplay safety rules (see the art-direction document):
    /// - The playable ground keeps its MeshCollider on the Ground layer; only its visible mesh/material change.
    /// - Obstacles keep exactly the same BoxCollider bounds on the Default layer (navigation bake, line of sight,
    ///   placement and minimap all read colliders); only their visible mesh changes, and it stays inside the box.
    /// - Every decorative prop has no collider, so it cannot change the NavMesh, placement, selection or targeting.
    ///   Props are kept out of bases, ore fields and roads; inside the map they are low enough to drive over.
    /// </summary>
    public static class EnvironmentBuilder
    {
        public class Layout
        {
            public string Name = "Map";
            public float MapSize = 100f;
            public readonly List<Vector3> Bases = new List<Vector3>();      // HQ positions (base pads)
            public readonly List<Vector3> Ore = new List<Vector3>();        // ore fields
            public readonly List<Vector3[]> Roads = new List<Vector3[]>();  // polylines
            public (string name, Vector3 center, Vector3 size)[] Obstacles = new (string, Vector3, Vector3)[0];
            public int Seed = 7;
        }

        // Steppe palette.
        private static readonly Color Grass = new Color(0.50f, 0.50f, 0.34f);
        private static readonly Color Dirt = new Color(0.58f, 0.50f, 0.37f);
        private static readonly Color Road = new Color(0.68f, 0.60f, 0.46f);
        private static readonly Color Pad = new Color(0.60f, 0.58f, 0.52f);
        private static readonly Color OreStain = new Color(0.38f, 0.29f, 0.22f);
        private static readonly Color Edge = new Color(0.36f, 0.34f, 0.27f);

        // ---------- Ground ----------

        /// <summary>Paints the ground texture for a layout and returns a material using it (plus tiled detail).</summary>
        public static Material GroundMaterial(Layout layout, ArtLibrary a)
        {
            const int res = 1024;
            float half = layout.MapSize * 0.5f;
            var tex = new Texture2D(res, res, TextureFormat.RGBA32, true);
            var px = new Color[res * res];
            for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                float u = (x + 0.5f) / res, v = (y + 0.5f) / res;
                var p = new Vector3(-half + u * layout.MapSize, 0f, -half + v * layout.MapSize);
                px[y * res + x] = GroundColor(layout, p, u, v);
            }
            tex.SetPixels(px);
            var saved = ArtLibrary.Save(tex, "Ground_" + layout.Name, TextureWrapMode.Clamp, srgb: true, alpha: false, maxSize: 2048);
            var mat = ArtLibrary.Lit("Ground_" + layout.Name, Color.white, 0.08f, 0f, saved, 1f);
            mat.SetTexture("_DetailAlbedoMap", a.GroundDetail);
            mat.SetTextureScale("_DetailAlbedoMap", new Vector2(layout.MapSize / 3.5f, layout.MapSize / 3.5f));
            mat.SetFloat("_DetailAlbedoMapScale", 1f);
            mat.EnableKeyword("_DETAIL_MULX2");
            mat.SetFloat("_EnvironmentReflections", 0f);
            mat.SetFloat("_SpecularHighlights", 0f);
            mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Color GroundColor(Layout l, Vector3 p, float u, float v)
        {
            float macro = ProceduralNoise.Fbm(u, v, 5, 5, l.Seed, tileable: false);
            float patches = ProceduralNoise.Fbm(u + 3.1f, v + 1.7f, 12, 3, l.Seed + 3, tileable: false);
            var c = Color.Lerp(Grass, Dirt, ProceduralNoise.Step(0.35f, 0.7f, macro));
            c = Color.Lerp(c, c * 0.94f, ProceduralNoise.Step(0.55f, 0.8f, patches));

            // Base pads (gravel around each headquarters: the natural building area).
            foreach (var b in l.Bases)
            {
                float d = Flat(p - b);
                float t = 1f - ProceduralNoise.Step(13f, 19f, d + (patches - 0.5f) * 4f);
                c = Color.Lerp(c, Pad * Mathf.Lerp(0.95f, 1.05f, patches), t * 0.75f);
            }
            // Dirt roads with darker wheel ruts.
            float road = 99f;
            foreach (var line in l.Roads)
                for (int i = 0; i < line.Length - 1; i++)
                    road = Mathf.Min(road, SegmentDistance(p, line[i], line[i + 1]));
            float wobble = (ProceduralNoise.Value(u * 60f, v * 60f, l.Seed + 9) - 0.5f) * 0.8f;
            float roadT = 1f - ProceduralNoise.Step(1.5f, 2.6f, road + wobble);
            c = Color.Lerp(c, Road, roadT * 0.85f);
            float rut = Mathf.Abs(road - 0.9f);
            if (road < 2f) c = Color.Lerp(c, c * 0.82f, (1f - ProceduralNoise.Step(0.05f, 0.3f, rut)) * roadT);
            // Ore-stained ground.
            foreach (var o in l.Ore)
            {
                float d = Flat(p - o) + (patches - 0.5f) * 2.5f;
                c = Color.Lerp(c, OreStain, (1f - ProceduralNoise.Step(3f, 7.5f, d)) * 0.7f);
            }
            // Darker rocky ground hugging obstacles.
            foreach (var (_, center, size) in l.Obstacles)
            {
                var local = p - center;
                float dx = Mathf.Max(0f, Mathf.Abs(local.x) - size.x * 0.5f), dz = Mathf.Max(0f, Mathf.Abs(local.z) - size.z * 0.5f);
                float d = Mathf.Sqrt(dx * dx + dz * dz);
                c = Color.Lerp(c, c * 0.72f, (1f - ProceduralNoise.Step(0f, 2.5f, d + (patches - 0.5f))) * 0.8f);
            }
            // Edge of the playable area.
            float edge = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v)) * l.MapSize;
            c = Color.Lerp(Edge, c, ProceduralNoise.Step(0.5f, 4f, edge));
            c.a = 1f;
            return c;
        }

        private static float Flat(Vector3 d) => Mathf.Sqrt(d.x * d.x + d.z * d.z);

        private static float SegmentDistance(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = new Vector2(b.x - a.x, b.z - a.z);
            var ap = new Vector2(p.x - a.x, p.z - a.z);
            float t = Mathf.Clamp01(Vector2.Dot(ap, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
            return (ap - ab * t).magnitude;
        }

        /// <summary>Replaces the ground plane's visible mesh with a quad whose UVs map exactly to the map square.</summary>
        public static void StyleGround(GameObject ground, Material mat)
        {
            const float h = 5f; // the Unity plane spans +-5 in local space and is scaled to the map size
            var k = new MeshKit { UvScale = 1f };
            k.Polygon(mat, new Vector3(-h, 0f, -h), new Vector3(-h, 0f, h), new Vector3(h, 0f, h), new Vector3(h, 0f, -h));
            var mesh = k.Build("Ground_Quad");
            var verts = mesh.vertices;
            var uv = new Vector2[verts.Length];
            for (int i = 0; i < verts.Length; i++) uv[i] = new Vector2((verts[i].x + h) / (2 * h), (verts[i].z + h) / (2 * h));
            mesh.uv = uv;
            mesh = ArtLibrary.SaveMesh(mesh);
            ground.GetComponent<MeshFilter>().sharedMesh = mesh; // the MeshCollider keeps the original plane
            var r = ground.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
        }

        // ---------- Backdrop ----------

        /// <summary>Visual-only terrain outside the playable square: a wide skirt and a ring of ridges.</summary>
        public static void Backdrop(Transform parent, Layout l, ArtLibrary a)
        {
            var root = new GameObject("Backdrop").transform;
            root.SetParent(parent, false);
            float half = l.MapSize * 0.5f;

            var skirtTex = ArtLibrary.Texture("Skirt_" + l.Name, 256, (u, v) =>
            {
                float n = ProceduralNoise.Fbm(u, v, 6, 5, l.Seed + 21);
                return Color.Lerp(Edge * 0.92f, Edge * 1.12f, n);
            });
            var skirtMat = ArtLibrary.Lit("Skirt_" + l.Name, Color.white, 0.05f, 0f, skirtTex, 1f);
            skirtMat.SetTexture("_DetailAlbedoMap", a.GroundDetail);
            skirtMat.SetTextureScale("_DetailAlbedoMap", new Vector2(40f, 40f));
            skirtMat.EnableKeyword("_DETAIL_MULX2");
            EditorUtility.SetDirty(skirtMat);

            var skirt = new MeshKit { UvScale = 1f / 120f };
            const float s = 190f;
            // A frame around the playable square (no overlap with the ground, so no z-fighting).
            skirt.Polygon(skirtMat, new Vector3(-s, -0.02f, -s), new Vector3(-s, -0.02f, s), new Vector3(-half, -0.02f, s), new Vector3(-half, -0.02f, -s));
            skirt.Polygon(skirtMat, new Vector3(half, -0.02f, -s), new Vector3(half, -0.02f, s), new Vector3(s, -0.02f, s), new Vector3(s, -0.02f, -s));
            skirt.Polygon(skirtMat, new Vector3(-half, -0.02f, half), new Vector3(-half, -0.02f, s), new Vector3(half, -0.02f, s), new Vector3(half, -0.02f, half));
            skirt.Polygon(skirtMat, new Vector3(-half, -0.02f, -s), new Vector3(-half, -0.02f, -half), new Vector3(half, -0.02f, -half), new Vector3(half, -0.02f, -s));
            var sr = ArtLibrary.Attach(root, "Skirt", "Skirt_" + l.Name, skirt, shadows: false);
            sr.receiveShadows = true;

            // Ridges: rows of faceted boulders just beyond the edge, rising with distance.
            var rocks = new MeshKit();
            var rng = new System.Random(l.Seed * 31 + 5);
            for (int side = 0; side < 4; side++)
            {
                for (float t = -half - 8f; t <= half + 8f; t += 4.5f)
                {
                    for (int row = 0; row < 3; row++)
                    {
                        float off = half + 5f + row * 4.5f + (float)rng.NextDouble() * 3f;
                        float along = t + (float)rng.NextDouble() * 3f;
                        Vector3 pos = side switch
                        {
                            0 => new Vector3(along, 0f, -off),
                            1 => new Vector3(along, 0f, off),
                            2 => new Vector3(-off, 0f, along),
                            _ => new Vector3(off, 0f, along),
                        };
                        float size = 1.6f + row * 1.3f + (float)rng.NextDouble() * 1.6f;
                        Boulder(rocks, row == 0 ? a.Rock : a.RockDark, pos, size, size * (0.32f + (float)rng.NextDouble() * 0.4f), rng);
                    }
                }
            }
            ArtLibrary.Attach(root, "Ridges", "Ridges_" + l.Name, rocks);
            GameObjectUtility.SetStaticEditorFlags(root.gameObject, StaticEditorFlags.BatchingStatic);
        }

        /// <summary>Irregular faceted rock: a jittered lathe, squashed and rotated.</summary>
        public static void Boulder(MeshKit k, Material m, Vector3 pos, float width, float height, System.Random rng)
        {
            int seg = 6 + rng.Next(3);
            float J() => 0.8f + (float)rng.NextDouble() * 0.4f;
            var profile = new[]
            {
                new Vector2(width * 0.5f * J(), -0.1f), new Vector2(width * 0.55f * J(), height * 0.35f),
                new Vector2(width * 0.38f * J(), height * 0.8f), new Vector2(width * 0.12f, height * Mathf.Min(1f, J())), new Vector2(0f, height),
            };
            k.Push(pos, Quaternion.Euler((float)rng.NextDouble() * 10f - 5f, (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 10f - 5f),
                new Vector3(1f, 1f, 0.7f + (float)rng.NextDouble() * 0.5f));
            k.Lathe(m, profile, seg, Vector3.zero, capTop: false, startAngle: (float)rng.NextDouble());
            k.Pop();
        }

        // ---------- Obstacles ----------

        /// <summary>
        /// Creates obstacles with unchanged colliders (unscaled GameObject + BoxCollider of the original size) and a
        /// styled mesh chosen by name: walls become concrete barriers, blocks bunker ruins, towers industrial silo
        /// towers and rocks rock outcrops.
        /// </summary>
        public static void Obstacles(Transform parent, (string name, Vector3 center, Vector3 size)[] set, ArtLibrary a)
        {
            var root = new GameObject("Obstacles").transform;
            root.SetParent(parent, false);
            int seed = 0;
            foreach (var (name, center, size) in set)
            {
                var go = new GameObject(name);
                go.transform.SetParent(root, false);
                go.transform.localPosition = center;
                var box = go.AddComponent<BoxCollider>();
                box.size = size;
                var k = new MeshKit();
                var rng = new System.Random(1000 + seed++);
                var floor = new Vector3(0f, -size.y * 0.5f, 0f);
                if (name.Contains("Wall")) BarrierWall(k, a, size, floor);
                else if (name.Contains("Tower")) Tower(k, a, size, floor);
                else if (name.Contains("Rock")) RockOutcrop(k, a, size, floor, rng);
                else Bunker(k, a, size, floor, rng);
                var mesh = ArtLibrary.SaveMesh(k.Build("Obstacle_" + name + "_" + Mathf.RoundToInt(size.x) + "x" + Mathf.RoundToInt(size.z)));
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                var mats = new Material[k.Materials.Count];
                for (int i = 0; i < mats.Length; i++) mats[i] = k.Materials[i];
                r.sharedMaterials = mats;
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            }
        }

        private static void BarrierWall(MeshKit k, ArtLibrary a, Vector3 size, Vector3 floor)
        {
            bool alongX = size.x >= size.z;
            float length = alongX ? size.x : size.z, thick = alongX ? size.z : size.x;
            k.Push(floor, alongX ? Quaternion.identity : Quaternion.Euler(0f, 90f, 0f));
            int panels = Mathf.Max(1, Mathf.RoundToInt(length / 3f));
            float pw = length / panels;
            for (int i = 0; i < panels; i++)
            {
                float x = -length * 0.5f + pw * (i + 0.5f);
                k.Block(a.Concrete_, new Vector3(x, 0f, 0f), new Vector2(pw - 0.12f, thick), new Vector2(pw - 0.12f, thick * 0.55f), size.y * 0.92f);
                k.Box(a.ConcreteDark_, new Vector3(x, size.y * 0.95f, 0f), new Vector3(pw - 0.05f, size.y * 0.1f, thick * 0.6f), 0.05f);
            }
            for (int i = 0; i <= panels; i++)
            {
                float x = Mathf.Clamp(-length * 0.5f + pw * i, -length * 0.5f + 0.25f, length * 0.5f - 0.25f);
                k.Block(a.ConcreteDark_, new Vector3(x, 0f, 0f), new Vector2(0.5f, thick), new Vector2(0.4f, thick * 0.7f), size.y);
            }
            k.Box(a.Hazard, new Vector3(-length * 0.5f + 0.26f, size.y * 0.5f, 0f), new Vector3(0.04f, size.y * 0.6f, thick * 0.72f));
            k.Box(a.Hazard, new Vector3(length * 0.5f - 0.26f, size.y * 0.5f, 0f), new Vector3(0.04f, size.y * 0.6f, thick * 0.72f));
            k.Pop();
        }

        private static void Tower(MeshKit k, ArtLibrary a, Vector3 size, Vector3 floor)
        {
            float r = Mathf.Min(size.x, size.z) * 0.5f;
            k.Block(a.ConcreteDark_, floor, new Vector2(size.x, size.z), new Vector2(size.x * 0.9f, size.z * 0.9f), size.y * 0.18f);
            k.Lathe(a.Concrete_, new[] { new Vector2(r * 0.82f, 0f), new Vector2(r * 0.7f, size.y * 0.55f), new Vector2(r * 0.72f, size.y * 0.6f) },
                10, floor + Vector3.up * size.y * 0.18f, capTop: false);
            k.Lathe(a.SteelLight_, new[] { new Vector2(r * 0.95f, 0f), new Vector2(r * 0.95f, size.y * 0.18f), new Vector2(r * 0.5f, size.y * 0.24f), new Vector2(0f, size.y * 0.24f) },
                12, floor + Vector3.up * size.y * 0.76f, capTop: false);
            k.Lathe(a.SteelDark_, new[] { new Vector2(r * 0.97f, 0f), new Vector2(r * 0.97f, size.y * 0.04f) }, 12, floor + Vector3.up * size.y * 0.8f, capTop: false);
            k.Cylinder(a.SteelDark_, floor + new Vector3(r * 0.4f, size.y * 0.78f, 0f), 0.06f, size.y * 0.22f, 4);
        }

        private static void Bunker(MeshKit k, ArtLibrary a, Vector3 size, Vector3 floor, System.Random rng)
        {
            k.Block(a.ConcreteDark_, floor, new Vector2(size.x, size.z), new Vector2(size.x * 0.82f, size.z * 0.82f), size.y * 0.75f);
            k.Block(a.Concrete_, floor + Vector3.up * size.y * 0.75f, new Vector2(size.x * 0.82f, size.z * 0.82f), new Vector2(size.x * 0.65f, size.z * 0.6f),
                size.y * 0.25f, new Vector2(size.x * 0.05f, 0f));
            k.Box(a.SteelDark_, floor + new Vector3(0f, size.y * 0.5f, size.z * 0.43f), new Vector3(size.x * 0.5f, size.y * 0.12f, size.z * 0.06f));
            k.Box(a.SteelDark_, floor + new Vector3(0f, size.y * 0.5f, -size.z * 0.43f), new Vector3(size.x * 0.5f, size.y * 0.12f, size.z * 0.06f));
            for (int i = 0; i < 4; i++)
            {
                var p = floor + new Vector3(((float)rng.NextDouble() - 0.5f) * size.x * 0.6f, size.y, ((float)rng.NextDouble() - 0.5f) * size.z * 0.5f);
                k.Box(a.Concrete_, p + Vector3.down * 0.15f, new Vector3(0.5f, 0.25f, 0.4f), 0.05f);
            }
        }

        private static void RockOutcrop(MeshKit k, ArtLibrary a, Vector3 size, Vector3 floor, System.Random rng)
        {
            float w = Mathf.Min(size.x, size.z);
            Boulder(k, a.Rock, floor, w * 0.95f, size.y, rng);
            Boulder(k, a.RockDark, floor + new Vector3(size.x * 0.22f, 0f, size.z * 0.2f), w * 0.55f, size.y * 0.75f, rng);
            Boulder(k, a.RockDark, floor + new Vector3(-size.x * 0.25f, 0f, -size.z * 0.18f), w * 0.5f, size.y * 0.6f, rng);
        }

        // ---------- Props ----------

        /// <summary>
        /// Collider-free clutter, merged into two meshes: low rubble at obstacle feet, dry shrubs and pebbles in the
        /// open (low enough to drive over, never on roads, bases or ore), and industrial debris (barrels, crates,
        /// sandbags, tank traps) just outside the playable square.
        /// </summary>
        public static int Props(Transform parent, Layout l, ArtLibrary a)
        {
            var root = new GameObject("Props").transform;
            root.SetParent(parent, false);
            var rng = new System.Random(l.Seed * 7 + 3);
            float half = l.MapSize * 0.5f;
            var nature = new MeshKit();
            var industry = new MeshKit();
            int count = 0;

            bool Clear(Vector3 p, float baseRadius, float oreRadius, float roadWidth)
            {
                foreach (var b in l.Bases) if (Flat(p - b) < baseRadius) return false;
                foreach (var o in l.Ore) if (Flat(p - o) < oreRadius) return false;
                foreach (var line in l.Roads)
                    for (int i = 0; i < line.Length - 1; i++)
                        if (SegmentDistance(p, line[i], line[i + 1]) < roadWidth) return false;
                foreach (var (_, c, s) in l.Obstacles)
                    if (Mathf.Abs(p.x - c.x) < s.x * 0.5f + 0.3f && Mathf.Abs(p.z - c.z) < s.z * 0.5f + 0.3f) return false;
                return true;
            }

            // Rubble skirting each obstacle (just outside the collider, at most 0.35 m tall).
            foreach (var (_, c, s) in l.Obstacles)
            {
                int n = Mathf.RoundToInt((s.x + s.z) * 0.8f);
                for (int i = 0; i < n; i++)
                {
                    float t = (float)rng.NextDouble();
                    int edge = rng.Next(4);
                    float ex = s.x * 0.5f + 0.12f, ez = s.z * 0.5f + 0.12f;
                    var p = edge switch
                    {
                        0 => new Vector3(c.x - ex + 2 * ex * t, 0f, c.z - ez),
                        1 => new Vector3(c.x - ex + 2 * ex * t, 0f, c.z + ez),
                        2 => new Vector3(c.x - ex, 0f, c.z - ez + 2 * ez * t),
                        _ => new Vector3(c.x + ex, 0f, c.z - ez + 2 * ez * t),
                    };
                    float w = 0.35f + (float)rng.NextDouble() * 0.45f;
                    Boulder(nature, (i & 1) == 0 ? a.Rock : a.RockDark, p, w, Mathf.Min(0.35f, w * 0.6f), rng);
                    count++;
                }
            }

            // Shrubs and pebbles in open ground (at most ~0.4 m: tanks drive over them).
            for (int i = 0; i < 260; i++)
            {
                var p = new Vector3(((float)rng.NextDouble() - 0.5f) * (l.MapSize - 4f), 0f, ((float)rng.NextDouble() - 0.5f) * (l.MapSize - 4f));
                if (!Clear(p, 17f, 6f, 3f)) continue;
                if (rng.NextDouble() < 0.6)
                {
                    float r = 0.2f + (float)rng.NextDouble() * 0.22f;
                    nature.Push(p, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f));
                    nature.Lathe(a.Shrub, new[] { new Vector2(r, 0f), new Vector2(r * 1.2f, r * 0.5f), new Vector2(r * 0.5f, r * 0.9f), new Vector2(0f, r) },
                        5, Vector3.zero, capTop: false);
                    nature.Pop();
                }
                else
                {
                    float w = 0.2f + (float)rng.NextDouble() * 0.35f;
                    Boulder(nature, a.Rock, p, w, w * 0.5f, rng);
                }
                count++;
            }

            // Industrial debris lining the outside of the map edge (never inside the playable square).
            for (int i = 0; i < 70; i++)
            {
                float along = ((float)rng.NextDouble() - 0.5f) * (l.MapSize - 6f);
                float outside = 0.9f + (float)rng.NextDouble() * 2.2f;
                Vector3 p = rng.Next(4) switch
                {
                    0 => new Vector3(along, 0f, -half - outside),
                    1 => new Vector3(along, 0f, half + outside),
                    2 => new Vector3(-half - outside, 0f, along),
                    _ => new Vector3(half + outside, 0f, along),
                };
                industry.Push(p, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f));
                switch (rng.Next(4))
                {
                    case 0: // barrel cluster
                        for (int b = 0; b < 3; b++)
                            industry.Cylinder(b == 0 ? a.PaintIndustrial : a.SteelDark_, new Vector3(b * 0.55f - 0.5f, 0f, (b % 2) * 0.4f), 0.24f, 0.75f, 8);
                        break;
                    case 1: // crate stack
                        industry.Box(a.PaintHeavy, new Vector3(0f, 0.4f, 0f), new Vector3(0.9f, 0.8f, 0.9f), 0.04f);
                        industry.Box(a.PaintHeavy, new Vector3(0.15f, 1.05f, 0.1f), new Vector3(0.7f, 0.5f, 0.7f), 0.04f);
                        break;
                    case 2: // sandbag arc
                        for (int b = 0; b < 4; b++)
                        {
                            var rot = Quaternion.Euler(0f, b * 22f - 33f, 0f);
                            industry.Push(rot * new Vector3(0f, 0f, 1.2f), rot);
                            industry.Box(a.Shrub, new Vector3(0f, 0.18f, 0f), new Vector3(0.6f, 0.36f, 0.35f), 0.08f);
                            industry.Pop();
                        }
                        break;
                    default: // tank-trap hedgehog
                        for (int b = 0; b < 3; b++)
                        {
                            industry.Push(new Vector3(0f, 0.4f, 0f), Quaternion.Euler(b * 60f, b * 60f, 45f));
                            industry.Box(a.SteelDark_, Vector3.zero, new Vector3(0.12f, 1.0f, 0.12f));
                            industry.Pop();
                        }
                        break;
                }
                industry.Pop();
                count++;
            }

            ArtLibrary.Attach(root, "Nature", "Props_Nature_" + l.Name, nature);
            ArtLibrary.Attach(root, "Industry", "Props_Industry_" + l.Name, industry);
            GameObjectUtility.SetStaticEditorFlags(root.gameObject, StaticEditorFlags.BatchingStatic);
            return count;
        }

        // ---------- Lighting and post-processing ----------

        public const string VolumeProfilePath = ArtLibrary.Root + "/Ironfront_PostFX.asset";

        /// <summary>Late-afternoon sun, cool sky ambient, light haze and the shared post-processing volume.</summary>
        public static void Lighting(Camera cam)
        {
            var sun = Object.FindAnyObjectByType<Light>();
            if (sun != null)
            {
                sun.color = new Color(1f, 0.9f, 0.76f);
                sun.intensity = 1.5f;
                sun.shadowStrength = 0.82f;
                sun.transform.rotation = Quaternion.Euler(47f, -38f, 0f);
            }
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.50f, 0.56f, 0.66f);
            RenderSettings.ambientEquatorColor = new Color(0.44f, 0.42f, 0.38f);
            RenderSettings.ambientGroundColor = new Color(0.24f, 0.21f, 0.18f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.0032f;
            RenderSettings.fogColor = new Color(0.66f, 0.64f, 0.6f);

            var volumeGo = new GameObject("PostProcessing");
            var volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = PostProfile();

            if (cam != null)
            {
                var data = cam.GetComponent<UniversalAdditionalCameraData>();
                if (data == null) data = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
                data.renderPostProcessing = true;
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                data.antialiasingQuality = AntialiasingQuality.Medium;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = RenderSettings.fogColor;
            }
        }

        public static VolumeProfile PostProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, VolumeProfilePath);
            }
            T Get<T>() where T : VolumeComponent
            {
                if (!profile.TryGet<T>(out var c))
                {
                    c = profile.Add<T>(true);
                    c.name = typeof(T).Name;
                    AssetDatabase.AddObjectToAsset(c, profile);
                }
                return c;
            }
            Get<Tonemapping>().mode.Override(TonemappingMode.ACES);
            var color = Get<ColorAdjustments>();
            color.postExposure.Override(0.35f);
            color.contrast.Override(10f);
            color.saturation.Override(4f);
            var bloom = Get<Bloom>();
            bloom.threshold.Override(1.05f);
            bloom.intensity.Override(0.65f);
            bloom.scatter.Override(0.6f);
            var vignette = Get<Vignette>();
            vignette.intensity.Override(0.16f);
            vignette.smoothness.Override(0.45f);
            Get<WhiteBalance>().temperature.Override(6f);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        /// <summary>Shadow distance covering the furthest RTS zoom (was 50 m; the camera reaches 60 m away).</summary>
        public static void ConfigurePipeline()
        {
            foreach (var asset in new[] { GraphicsSettings.defaultRenderPipeline, QualitySettings.renderPipeline })
            {
                if (!(asset is UniversalRenderPipelineAsset urp)) continue;
                if (urp.shadowDistance < 100f)
                {
                    urp.shadowDistance = 100f;
                    EditorUtility.SetDirty(urp);
                }
            }
        }
    }
}
