using System;
using System.Collections.Generic;
using System.IO;
using Ironfront.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Ironfront.EditorTools.Art
{
    /// <summary>
    /// Generates the interface art (Milestone 006): 9-sliced chamfered panel / button / bar sprites, credits and
    /// power glyphs, the Ironfront emblem, and portrait icons rendered from the procedural models in an isolated
    /// preview scene (the open scene is not touched). Everything is drawn by code.
    /// </summary>
    public static class UiArtBuilder
    {
        public const string UiDir = ArtLibrary.Root + "/UI";
        public const string SkinPath = UiDir + "/UiSkin.asset";
        private static readonly Color IconFaction = new Color(0.2f, 0.45f, 0.95f);

        public static UiSkin Generate()
        {
            RtsSandboxBuilder.EnsureFolder(UiDir);
            var skin = AssetDatabase.LoadAssetAtPath<UiSkin>(SkinPath);
            if (skin == null)
            {
                skin = ScriptableObject.CreateInstance<UiSkin>();
                AssetDatabase.CreateAsset(skin, SkinPath);
            }
            var so = new SerializedObject(skin);
            so.FindProperty("panel").objectReferenceValue = Panel("UI_Panel", 64, 10, 1.6f, top: 1f, bottom: 0.82f, 16);
            so.FindProperty("button").objectReferenceValue = Panel("UI_Button", 48, 6, 1.2f, top: 1f, bottom: 0.8f, 10);
            so.FindProperty("bar").objectReferenceValue = Panel("UI_Bar", 16, 3, 0f, top: 1f, bottom: 0.88f, 5);
            so.FindProperty("creditsIcon").objectReferenceValue = Glyph("UI_Credits", 64, CreditsGlyph);
            so.FindProperty("powerIcon").objectReferenceValue = Glyph("UI_Power", 64, PowerGlyph);
            so.FindProperty("emblem").objectReferenceValue = Glyph("UI_Emblem", 256, EmblemGlyph);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(skin);
            AssetDatabase.SaveAssets();
            return skin;
        }

        public static UiSkin Load() => AssetDatabase.LoadAssetAtPath<UiSkin>(SkinPath);

        // ---------- Sprites ----------

        /// <summary>White chamfered rectangle with a brighter rim and a subtle vertical gradient (tinted by Image.color).</summary>
        private static Sprite Panel(string name, int size, int chamfer, float rim, float top, float bottom, int border)
        {
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float cover = 0f, rimCover = 0f;
                for (int sy = 0; sy < 4; sy++)
                for (int sx = 0; sx < 4; sx++)
                {
                    float fx = x + (sx + 0.5f) / 4f, fy = y + (sy + 0.5f) / 4f;
                    float d = InsideChamfer(fx, fy, size, chamfer);
                    if (d >= 0f)
                    {
                        cover += 1f / 16f;
                        if (d < rim) rimCover += 1f / 16f;
                    }
                }
                float g = Mathf.Lerp(bottom, top, (float)y / (size - 1));
                float v = cover > 0f ? Mathf.Lerp(g * 0.86f, 1f, rimCover / cover) : 0f;
                px[y * size + x] = new Color(v, v, v, cover);
            }
            return SaveSprite(name, size, size, px, new Vector4(border, border, border, border));
        }

        /// <summary>Signed distance-ish value: &gt;= 0 inside the chamfered rect, equal to the distance to the nearest edge.</summary>
        private static float InsideChamfer(float x, float y, int size, int chamfer)
        {
            float dx = Mathf.Min(x, size - x), dy = Mathf.Min(y, size - y);
            float edge = Mathf.Min(dx, dy);
            float diag = (dx + dy - chamfer) * 0.7071f;
            return Mathf.Min(edge, diag);
        }

        private static Sprite Glyph(string name, int size, Func<float, float, Color> shape)
        {
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                Color acc = Color.clear;
                for (int sy = 0; sy < 3; sy++)
                for (int sx = 0; sx < 3; sx++)
                {
                    float u = (x + (sx + 0.5f) / 3f) / size, v = (y + (sy + 0.5f) / 3f) / size;
                    acc += shape(u, v) / 9f;
                }
                px[y * size + x] = acc;
            }
            return SaveSprite(name, size, size, px, Vector4.zero);
        }

        private static bool InPolygon(Vector2 p, Vector2[] poly)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                if ((poly[i].y > p.y) != (poly[j].y > p.y) && p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                    inside = !inside;
            return inside;
        }

        /// <summary>Faceted ore crystal (the currency is refined ore).</summary>
        private static Color CreditsGlyph(float u, float v)
        {
            var p = new Vector2(u, v);
            var outline = new[] { new Vector2(0.5f, 0.95f), new Vector2(0.8f, 0.68f), new Vector2(0.68f, 0.08f), new Vector2(0.32f, 0.08f), new Vector2(0.2f, 0.68f) };
            if (!InPolygon(p, outline)) return Color.clear;
            bool left = u < 0.5f - (v - 0.5f) * 0.0f;
            var c = ArtLibrary.OreAmber;
            if (v > 0.68f) c = Color.Lerp(c, Color.white, 0.35f);
            else if (left) c *= 0.82f;
            c.a = 1f;
            return c;
        }

        /// <summary>Lightning bolt.</summary>
        private static Color PowerGlyph(float u, float v)
        {
            var bolt = new[] { new Vector2(0.58f, 0.96f), new Vector2(0.22f, 0.46f), new Vector2(0.46f, 0.46f), new Vector2(0.36f, 0.04f), new Vector2(0.8f, 0.58f), new Vector2(0.55f, 0.58f) };
            return InPolygon(new Vector2(u, v), bolt) ? new Color(1f, 0.9f, 0.45f, 1f) : Color.clear;
        }

        /// <summary>Ironfront emblem: an armoured shield with a forward chevron and a horizon bar (original design).</summary>
        private static Color EmblemGlyph(float u, float v)
        {
            var p = new Vector2(u, v);
            var shield = new[] { new Vector2(0.5f, 0.97f), new Vector2(0.92f, 0.82f), new Vector2(0.86f, 0.4f), new Vector2(0.5f, 0.04f), new Vector2(0.14f, 0.4f), new Vector2(0.08f, 0.82f) };
            var inner = new[] { new Vector2(0.5f, 0.9f), new Vector2(0.85f, 0.78f), new Vector2(0.8f, 0.42f), new Vector2(0.5f, 0.12f), new Vector2(0.2f, 0.42f), new Vector2(0.15f, 0.78f) };
            var chevron = new[] { new Vector2(0.5f, 0.72f), new Vector2(0.76f, 0.46f), new Vector2(0.76f, 0.33f), new Vector2(0.5f, 0.58f), new Vector2(0.24f, 0.33f), new Vector2(0.24f, 0.46f) };
            bool bar = v > 0.22f && v < 0.28f && u > 0.32f && u < 0.68f;
            if (!InPolygon(p, shield)) return Color.clear;
            if (InPolygon(p, chevron) || bar) return new Color(1f, 0.72f, 0.28f, 1f);
            if (InPolygon(p, inner)) return new Color(0.14f, 0.16f, 0.18f, 1f);
            return new Color(0.62f, 0.64f, 0.66f, 1f);
        }

        private static Sprite SaveSprite(string name, int w, int h, Color[] px, Vector4 border)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels(px);
            string path = $"{UiDir}/{name}.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spriteBorder = border;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        // ---------- Model icons ----------

        /// <summary>Renders a portrait of each model and assigns it to the definitions' icon field.</summary>
        public static int RenderIcons(EconomyContentBuilder.Content c)
        {
            var a = EconomyContentBuilder.Art;
            var jobs = new List<(Object def, string name, Action<Transform> build)>
            {
                (c.Headquarters, "Icon_Headquarters", t => StructureModels.Headquarters(t, a)),
                (c.Refinery, "Icon_Refinery", t => StructureModels.Refinery(t, a)),
                (c.PowerPlant, "Icon_PowerPlant", t => StructureModels.PowerPlant(t, a)),
                (c.VehicleFactory, "Icon_VehicleFactory", t => StructureModels.VehicleFactory(t, a)),
                (c.Harvester, "Icon_Harvester", t => VehicleModels.Harvester(t, a)),
                (c.LightTank, "Icon_LightTank", t => VehicleModels.LightTank(t, a)),
                (c.HeavyTank, "Icon_HeavyTank", t => VehicleModels.HeavyTank(t, a)),
                (c.Expansion.Recon, "Icon_Recon", t => ExpansionModels.Recon(t, a)),
                (c.Expansion.Artillery, "Icon_RocketArtillery", t => ExpansionModels.RocketArtillery(t, a)),
                (c.Expansion.GuardTower, "Icon_GuardTower", t => ExpansionModels.GuardTower(t, a)),
                (c.Expansion.AtTurret, "Icon_AntiArmourTurret", t => ExpansionModels.AntiArmourTurret(t, a)),
                (c.Expansion.ResearchCentre, "Icon_ResearchCentre", t => ExpansionModels.ResearchCentre(t, a)),
                (c.Repair.RepairPad, "Icon_RepairPad", t => RepairModels.RepairPad(t, a)),
                (c.Command.Mcv, "Icon_Mcv", t => CommandModels.Mcv(t, a)),
                (c.Command.ForwardCommand, "Icon_ForwardCommand", t => CommandModels.ForwardCommand(t, a)),
            };
            foreach (var (def, name, build) in jobs)
            {
                var sprite = RenderIcon(name, build, a);
                var so = new SerializedObject(def);
                so.FindProperty("icon").objectReferenceValue = sprite;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(def);
            }
            AssetDatabase.SaveAssets();
            return jobs.Count;
        }

        private static Sprite RenderIcon(string name, Action<Transform> build, ArtLibrary a)
        {
            const int size = 256;
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                GameObject Add(GameObject go)
                {
                    SceneManager.MoveGameObjectToScene(go, scene);
                    return go;
                }
                var key = Add(new GameObject("Key")).AddComponent<Light>();
                key.type = LightType.Directional;
                key.intensity = 1.5f;
                key.color = new Color(1f, 0.93f, 0.82f);
                key.transform.rotation = Quaternion.Euler(42f, -30f, 0f);
                var fill = Add(new GameObject("Fill")).AddComponent<Light>();
                fill.type = LightType.Directional;
                fill.intensity = 0.55f;
                fill.color = new Color(0.6f, 0.7f, 0.9f);
                fill.transform.rotation = Quaternion.Euler(25f, 150f, 0f);

                var root = Add(new GameObject("Model"));
                build(root.transform);
                var block = new MaterialPropertyBlock();
                foreach (var r in root.GetComponentsInChildren<Renderer>())
                {
                    block.Clear();
                    if (r.sharedMaterial == a.Faction) block.SetColor("_BaseColor", IconFaction);
                    else if (r.sharedMaterial == a.FactionGlow) block.SetColor("_EmissionColor", IconFaction * 2.5f);
                    else continue;
                    r.SetPropertyBlock(block);
                }
                var bounds = new Bounds(root.transform.position + Vector3.up, Vector3.one);
                foreach (var r in root.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);

                var camGo = Add(new GameObject("Camera"));
                var cam = camGo.AddComponent<Camera>();
                cam.scene = scene;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                cam.fieldOfView = 20f;
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = 200f;
                var data = camGo.AddComponent<UniversalAdditionalCameraData>();
                data.renderPostProcessing = false;
                data.renderShadows = false;
                var dir = Quaternion.Euler(28f, -140f, 0f);
                float radius = bounds.extents.magnitude;
                float dist = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.02f;
                camGo.transform.SetPositionAndRotation(bounds.center - dir * Vector3.forward * dist, dir);

                var rt = RenderTexture.GetTemporary(size, size, 24, RenderTextureFormat.ARGB32);
                rt.antiAliasing = 4;
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                cam.targetTexture = null;
                RenderTexture.ReleaseTemporary(rt);
                var px = tex.GetPixels();
                Object.DestroyImmediate(tex);
                return SaveSprite(name, size, size, px, Vector4.zero);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
