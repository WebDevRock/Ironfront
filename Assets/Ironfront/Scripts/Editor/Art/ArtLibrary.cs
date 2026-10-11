using System;
using System.IO;
using Ironfront.Core.Procedural;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ironfront.EditorTools.Art
{
    /// <summary>
    /// Generates Ironfront's shared textures and materials (Milestone 006 art direction). Everything is created
    /// procedurally from code: no imported or third-party art. Assets live under Assets/Ironfront/Art and are
    /// regenerated in place (GUIDs preserved) each time the scene builders run.
    /// </summary>
    public sealed class ArtLibrary
    {
        public const string Root = "Assets/Ironfront/Art";
        public const string TexturesDir = Root + "/Textures";
        public const string MaterialsDir = Root + "/Materials";
        public const string MeshesDir = Root + "/Meshes";

        // ---- Palette (see Docs/Milestone-006-Art-Direction.md) ----
        public static readonly Color OliveLight = new Color(0.56f, 0.55f, 0.43f);
        public static readonly Color OliveDark = new Color(0.30f, 0.33f, 0.27f);
        public static readonly Color Concrete = new Color(0.56f, 0.54f, 0.50f);
        public static readonly Color ConcreteDark = new Color(0.38f, 0.37f, 0.36f);
        public static readonly Color SteelDark = new Color(0.17f, 0.18f, 0.19f);
        public static readonly Color SteelLight = new Color(0.56f, 0.58f, 0.60f);
        public static readonly Color HazardYellow = new Color(0.95f, 0.70f, 0.12f);
        public static readonly Color OreAmber = new Color(1.00f, 0.60f, 0.10f);
        public static readonly Color GlowWarm = new Color(1.00f, 0.74f, 0.42f);
        public static readonly Color GlowCool = new Color(0.55f, 0.85f, 1.00f);

        // ---- Hard-surface materials ----
        public Material PaintLight, PaintHeavy, PaintIndustrial, Concrete_, ConcreteDark_, SteelDark_, SteelLight_, Rubber, Track,
            Hazard, Faction, FactionGlow, GlowWarm_, GlowCool_, Glass, OreCrystal, OreRock, Rock, RockDark, Shrub, Wreck, Asphalt;

        // ---- Particle materials ----
        public Material FxFlash, FxFire, FxSpark, FxSmoke, FxDust, FxScorch, FxRing, FxTracer, FxGlint;

        // ---- Shared textures ----
        public Texture2D Detail, Tread, HazardTex, GroundDetail;

        private static ArtLibrary s_cached;

        /// <summary>When false (tests), generated meshes are attached directly instead of being saved as assets.</summary>
        public static bool PersistMeshes = true;

        /// <summary>
        /// Unsaved stand-in materials for tests: same slots as <see cref="Create"/> but nothing is written to disk.
        /// </summary>
        public static ArtLibrary InMemory()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            Material M(string n) => new Material(lit) { name = n };
            return new ArtLibrary
            {
                PaintLight = M("PaintLight"), PaintHeavy = M("PaintHeavy"), PaintIndustrial = M("PaintIndustrial"), Concrete_ = M("Concrete"),
                ConcreteDark_ = M("ConcreteDark"), SteelDark_ = M("SteelDark"), SteelLight_ = M("SteelLight"), Rubber = M("Rubber"), Track = M("Track"),
                Hazard = M("Hazard"), Faction = M("Faction"), FactionGlow = M("FactionGlow"), GlowWarm_ = M("GlowWarm"), GlowCool_ = M("GlowCool"),
                Glass = M("Glass"), OreCrystal = M("OreCrystal"), OreRock = M("OreRock"), Rock = M("Rock"), RockDark = M("RockDark"), Shrub = M("Shrub"),
                Wreck = M("Wreck"), Asphalt = M("Asphalt"),
            };
        }

        /// <summary>Creates or refreshes every texture and material. Cheap enough to call per scene build.</summary>
        public static ArtLibrary Create()
        {
            RtsSandboxBuilder.EnsureFolder(TexturesDir);
            RtsSandboxBuilder.EnsureFolder(MaterialsDir);
            RtsSandboxBuilder.EnsureFolder(MeshesDir);
            var a = new ArtLibrary();

            a.Detail = Texture("Surface_Detail", 256, (u, v) =>
            {
                // Subtle mottling plus faint panel seams every quarter tile.
                float n = ProceduralNoise.Fbm(u, v, 8, 4, 11);
                float grain = ProceduralNoise.Fbm(u, v, 64, 2, 12);
                float seam = Seam(u * 4f) * Seam(v * 2f + (Mathf.Floor(u * 4f) % 2f) * 0.5f);
                float g = Mathf.Lerp(0.80f, 1.0f, n) * Mathf.Lerp(0.95f, 1.0f, grain) * Mathf.Lerp(0.82f, 1f, seam);
                return new Color(g, g, g, 1f);
            });
            a.Tread = Texture("Track_Tread", 128, (u, v) =>
            {
                float bar = Mathf.Repeat(v * 8f, 1f) < 0.42f ? 0.55f : 1f;
                float edge = Mathf.Abs(u - 0.5f) > 0.42f ? 0.7f : 1f;
                float g = bar * edge * Mathf.Lerp(0.85f, 1f, ProceduralNoise.Fbm(u, v, 16, 2, 21));
                return new Color(g, g, g, 1f);
            });
            a.HazardTex = Texture("Hazard_Stripes", 128, (u, v) =>
            {
                bool yellow = Mathf.Repeat((u + v) * 4f, 1f) < 0.5f;
                float wear = Mathf.Lerp(0.8f, 1f, ProceduralNoise.Fbm(u, v, 8, 3, 31));
                var c = yellow ? HazardYellow : new Color(0.08f, 0.08f, 0.08f);
                return c * wear;
            });
            a.GroundDetail = Texture("Ground_Detail", 256, (u, v) =>
            {
                float n = ProceduralNoise.Fbm(u, v, 16, 4, 41);
                float pebbles = ProceduralNoise.Value(u * 96f, v * 96f, 42, 96);
                float g = Mathf.Lerp(0.78f, 1.08f, n) * (pebbles > 0.86f ? 0.85f : 1f);
                return new Color(g * 0.5f, g * 0.5f, g * 0.5f, 1f); // 0.5 = neutral for the x2 detail blend
            });

            Material L(string name, Color c, float smooth, float metal, Texture2D tex = null, float tiles = 1f) =>
                Lit(name, c, smooth, metal, tex ?? a.Detail, tiles);

            a.PaintLight = L("Paint_LightTank", OliveLight, 0.32f, 0.15f);
            a.PaintHeavy = L("Paint_HeavyTank", OliveDark, 0.32f, 0.15f);
            a.PaintIndustrial = L("Paint_Industrial", new Color(0.78f, 0.62f, 0.25f), 0.3f, 0.15f);
            a.Concrete_ = L("Concrete", Concrete, 0.12f, 0f);
            a.ConcreteDark_ = L("Concrete_Dark", ConcreteDark, 0.12f, 0f);
            a.SteelDark_ = L("Steel_Dark", SteelDark, 0.45f, 0.6f);
            a.SteelLight_ = L("Steel_Light", SteelLight, 0.5f, 0.7f);
            a.Rubber = L("Rubber", new Color(0.11f, 0.11f, 0.11f), 0.2f, 0f);
            a.Track = Lit("Track", new Color(0.2f, 0.2f, 0.2f), 0.25f, 0.3f, a.Tread, 1f);
            a.Hazard = Lit("Hazard", Color.white, 0.3f, 0.1f, a.HazardTex, 1f);
            a.Faction = L("Faction_Livery", Color.white, 0.38f, 0.15f);
            a.FactionGlow = Emissive("Faction_Glow", new Color(0.2f, 0.2f, 0.2f), Color.white, 2.5f);
            a.GlowWarm_ = Emissive("Glow_Warm", new Color(0.3f, 0.25f, 0.2f), GlowWarm, 2.2f);
            a.GlowCool_ = Emissive("Glow_Cool", new Color(0.2f, 0.25f, 0.3f), GlowCool, 2.2f);
            a.Glass = Lit("Glass", new Color(0.10f, 0.14f, 0.18f), 0.92f, 0.2f, null, 1f);
            a.OreCrystal = Emissive("Ore_Crystal", OreAmber, new Color(1f, 0.5f, 0.08f), 0.9f, smoothness: 0.85f);
            a.OreRock = L("Ore_Rock", new Color(0.30f, 0.25f, 0.21f), 0.15f, 0f);
            a.Rock = L("Rock", new Color(0.50f, 0.46f, 0.40f), 0.1f, 0f);
            a.RockDark = L("Rock_Dark", new Color(0.36f, 0.33f, 0.29f), 0.1f, 0f);
            a.Shrub = L("Shrub", new Color(0.42f, 0.42f, 0.24f), 0.05f, 0f);
            a.Wreck = L("Wreck", new Color(0.07f, 0.065f, 0.06f), 0.15f, 0.3f);
            a.Asphalt = L("Asphalt", new Color(0.25f, 0.25f, 0.25f), 0.2f, 0f);

            // Particle textures (white RGB, shape in alpha) and materials.
            var soft = Sprite("Fx_Soft", 64, (x, y) => Falloff(Radius(x, y), 1f, 2.2f));
            var flare = Sprite("Fx_Flare", 64, (x, y) =>
            {
                float r = Radius(x, y);
                float rays = Mathf.Pow(Mathf.Abs(Mathf.Cos(Mathf.Atan2(y, x) * 3f)), 12f) * Falloff(r, 1f, 1.2f);
                return Mathf.Clamp01(Falloff(r, 0.7f, 2.5f) + rays * 0.6f);
            });
            var smoke = Sprite("Fx_Smoke", 128, (x, y) =>
            {
                float u = x * 0.5f + 0.5f, v = y * 0.5f + 0.5f;
                float n = ProceduralNoise.Fbm(u, v, 4, 4, 51);
                return Mathf.Clamp01(Falloff(Radius(x, y), 1f, 1.6f) * Mathf.Lerp(0.35f, 1.25f, n));
            });
            var spark = Sprite("Fx_Spark", 32, (x, y) => Falloff(Mathf.Sqrt(x * x * 1f + y * y * 1f), 1f, 3f));
            var ring = Sprite("Fx_Ring", 128, (x, y) =>
            {
                float r = Radius(x, y);
                return Mathf.Clamp01(1f - Mathf.Abs(r - 0.8f) / 0.16f) * (r < 1f ? 1f : 0f);
            });
            var scorch = Sprite("Fx_Scorch", 128, (x, y) =>
            {
                float u = x * 0.5f + 0.5f, v = y * 0.5f + 0.5f;
                float n = ProceduralNoise.Fbm(u, v, 5, 4, 61);
                return Mathf.Clamp01((1f - Radius(x, y) * (0.8f + n * 0.5f)) * 1.6f) * 0.9f;
            });

            // Additive materials are tinted above 1 (HDR) so the bloom pass makes fire, flashes and tracers glow.
            a.FxFlash = Particle("Fx_Flash", flare, additive: true, intensity: 3f);
            a.FxFire = Particle("Fx_Fire", smoke, additive: true, intensity: 2.2f);
            a.FxSpark = Particle("Fx_Spark", spark, additive: true, intensity: 3f);
            a.FxSmoke = Particle("Fx_Smoke", smoke, additive: false);
            a.FxDust = Particle("Fx_Dust", smoke, additive: false);
            a.FxScorch = Particle("Fx_Scorch", scorch, additive: false);
            a.FxRing = Particle("Fx_Ring", ring, additive: true, intensity: 1.5f);
            a.FxTracer = Particle("Fx_Tracer", soft, additive: true, intensity: 3f);
            a.FxGlint = Particle("Fx_Glint", flare, additive: true, intensity: 2.5f);

            AssetDatabase.SaveAssets();
            // Asset import validation can reset _EMISSION on newly created materials; re-assert it after saving.
            foreach (var m in new[] { a.FactionGlow, a.GlowWarm_, a.GlowCool_, a.OreCrystal })
            {
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                m.EnableKeyword("_EMISSION");
                EditorUtility.SetDirty(m);
            }
            AssetDatabase.SaveAssets();
            s_cached = a;
            return a;
        }

        /// <summary>The library created by the most recent <see cref="Create"/> in this domain, or a new one.</summary>
        public static ArtLibrary Current => s_cached ?? Create();

        // ---------- Textures ----------

        private static float Seam(float t)
        {
            float f = Mathf.Repeat(t, 1f);
            return f < 0.012f || f > 0.988f ? 0f : 1f;
        }

        private static float Radius(float x, float y) => Mathf.Sqrt(x * x + y * y);

        private static float Falloff(float r, float radius, float power) => r >= radius ? 0f : Mathf.Pow(1f - r / radius, power);

        /// <summary>Tileable colour texture; <paramref name="f"/> receives (u, v) in [0, 1).</summary>
        public static Texture2D Texture(string name, int size, Func<float, float, Color> f, bool srgb = true)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                px[y * size + x] = f((float)x / size, (float)y / size);
            tex.SetPixels(px);
            return Save(tex, name, TextureWrapMode.Repeat, srgb, alpha: false);
        }

        /// <summary>Particle sprite: white with the shape in alpha; <paramref name="f"/> receives (x, y) in [-1, 1].</summary>
        private static Texture2D Sprite(string name, int size, Func<float, float, float> f)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float fx = (x + 0.5f) / size * 2f - 1f, fy = (y + 0.5f) / size * 2f - 1f;
                px[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(f(fx, fy)));
            }
            tex.SetPixels(px);
            return Save(tex, name, TextureWrapMode.Clamp, srgb: true, alpha: true);
        }

        /// <summary>Writes a PNG under Art/Textures, imports it with sensible settings and returns the asset.</summary>
        public static Texture2D Save(Texture2D tex, string name, TextureWrapMode wrap, bool srgb, bool alpha, int maxSize = 2048)
        {
            string path = $"{TexturesDir}/{name}.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = srgb;
            importer.alphaSource = alpha ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
            importer.alphaIsTransparency = alpha;
            importer.wrapMode = wrap;
            importer.mipmapEnabled = true;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 4;
            importer.maxTextureSize = maxSize;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ---------- Materials ----------

        private static Material LoadOrCreate(string name, Shader shader)
        {
            string path = $"{MaterialsDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            return mat;
        }

        public static Material Lit(string name, Color color, float smoothness, float metallic, Texture2D baseMap, float tiles)
        {
            var mat = LoadOrCreate(name, Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", color);
            mat.SetTexture("_BaseMap", baseMap);
            mat.SetTextureScale("_BaseMap", new Vector2(tiles, tiles));
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", metallic);
            mat.SetFloat("_EnvironmentReflections", 1f);
            mat.DisableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", Color.black);
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        public static Material Emissive(string name, Color baseColor, Color emission, float intensity, float smoothness = 0.5f)
        {
            var mat = Lit(name, baseColor, smoothness, 0f, null, 1f);
            mat.SetTexture("_BaseMap", null);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            mat.SetColor("_EmissionColor", emission * intensity);
            mat.EnableKeyword("_EMISSION");
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>URP Particles/Unlit material, alpha-blended or additive, vertex colour multiplied.</summary>
        public static Material Particle(string name, Texture2D tex, bool additive, float intensity = 1f)
        {
            var mat = LoadOrCreate(name, Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", new Color(intensity, intensity, intensity, 1f));
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", additive ? 2f : 0f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_SrcBlendAlpha", additive ? (float)BlendMode.SrcAlpha : (float)BlendMode.One);
            mat.SetFloat("_DstBlendAlpha", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetFloat("_Cull", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.DisableKeyword("_SOFTPARTICLES_ON");
            if (additive) mat.EnableKeyword("_BLENDMODE_ADD"); else mat.DisableKeyword("_BLENDMODE_ADD");
            mat.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ---------- Meshes ----------

        /// <summary>Saves (or updates in place, keeping the GUID) a generated mesh under Art/Meshes.</summary>
        public static Mesh SaveMesh(Mesh mesh)
        {
            string path = $"{MeshesDir}/{mesh.name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }
            existing.Clear();
            EditorUtility.CopySerialized(mesh, existing);
            UnityEngine.Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        /// <summary>Builds the kit into a saved mesh and attaches it to a new child renderer.</summary>
        public static MeshRenderer Attach(Transform parent, string objectName, string meshName, MeshKit kit, bool shadows = true)
        {
            var mesh = PersistMeshes ? SaveMesh(kit.Build(meshName)) : kit.Build(meshName);
            var go = new GameObject(objectName);
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            var mats = new Material[kit.Materials.Count];
            for (int i = 0; i < mats.Length; i++) mats[i] = kit.Materials[i];
            r.sharedMaterials = mats;
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return r;
        }
    }
}
