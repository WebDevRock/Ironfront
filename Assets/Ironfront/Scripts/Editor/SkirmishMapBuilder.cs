using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Ironfront.AI;
using Ironfront.CameraControl;
using Ironfront.Core.AI;
using Ironfront.Core.Skirmish;
using Ironfront.Data;
using Ironfront.Economy;
using Ironfront.EditorTools.Art;
using Ironfront.Match;
using Ironfront.Skirmish;
using Ironfront.UI;
using Ironfront.Vision;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Layout = Ironfront.EditorTools.SkirmishMapLayouts.Layout;

namespace Ironfront.EditorTools
{
    /// <summary>
    /// Milestone 008: builds the four skirmish battlefields from <see cref="SkirmishMapLayouts"/>, plus the difficulty
    /// assets, the map definitions, the catalog the menu reads and a preview image per map. Each map scene has six to
    /// eight start slots and no pre-placed bases (<see cref="SkirmishInitializer"/> spawns them), its own baked NavMesh,
    /// and every map-size-dependent system (camera, placement, fog, minimap) set to the map's size.
    /// The map validator runs twice: first to derive which start pairs are valid (stored on the slots), then to check
    /// the saved scene, including that the stored spawn table matches the rules. Reports go to Temp/validation_[id].txt.
    /// Menu: Ironfront > Build Skirmish Maps / Validate Skirmish Maps.
    /// </summary>
    public static class SkirmishMapBuilder
    {
        public const string ScenesDir = "Assets/Scenes/Maps";
        public const string DataDir = RtsSandboxBuilder.DataDir + "/Maps";
        public const string AiDir = RtsSandboxBuilder.DataDir + "/AI";
        public const string PreviewDir = "Assets/Ironfront/Art/MapPreviews";
        public const string CatalogPath = DataDir + "/MapCatalog.asset";
        public const int PlayerFaction = 1;
        public const int ComputerFaction = CombatContentBuilder.EnemyFactionId;

        public static IReadOnlyList<Layout> Layouts => SkirmishMapLayouts.All;

        [MenuItem("Ironfront/Build Skirmish Maps")]
        public static void BuildFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Debug.Log("[Ironfront] " + BuildAll());
        }

        /// <summary>Difficulties, map definitions, the catalog, every map scene and preview, then the main menu.</summary>
        public static string BuildAll()
        {
            var summary = new System.Text.StringBuilder();
            var difficulties = CreateDifficulties();
            var defs = new List<MapDefinition>();
            foreach (var l in Layouts) defs.Add(CreateDefinition(l));
            var catalog = CreateCatalog(defs, difficulties);
            foreach (var l in Layouts) summary.AppendLine(Build(l, catalog));
            summary.AppendLine(MainMenuBuilder.Build());
            return summary.ToString();
        }

        /// <summary>Builds one map (assets it needs must exist: run <see cref="BuildAll"/> once first).</summary>
        public static string BuildOne(string mapId)
        {
            foreach (var l in Layouts)
                if (l.Id == mapId)
                {
                    CreateDefinition(l);
                    return Build(l, AssetDatabase.LoadAssetAtPath<MapCatalog>(CatalogPath));
                }
            return "unknown map " + mapId;
        }

        /// <summary>Opens every map scene and runs the validator; writes Temp/validation_[id].txt. Returns a summary.</summary>
        [MenuItem("Ironfront/Validate Skirmish Maps")]
        public static string ValidateAll()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var l in Layouts)
            {
                EditorSceneManager.OpenScene(l.ScenePath, OpenSceneMode.Single);
                Physics.SyncTransforms();
                var report = SceneMapValidation.ValidateLoadedScene();
                File.WriteAllText(Path.Combine(Application.dataPath, "..", "Temp", $"validation_{l.Id}.txt"), report.ToString());
                sb.AppendLine($"{l.Name}: {(report.Passed ? "PASSED" : $"FAILED ({report.ErrorCount})")}");
                if (!report.Passed) Debug.LogWarning("[Ironfront] " + report);
            }
            Debug.Log("[Ironfront] Map validation\n" + sb);
            return sb.ToString();
        }

        private static string Build(Layout map, MapCatalog catalog)
        {
            RtsSandboxBuilder.EnsureFolder(ScenesDir);
            RtsSandboxBuilder.EnsureFolder(RtsSandboxBuilder.NavDir);
            RtsSandboxBuilder.EnsureLayers();
            var mats = RtsSandboxBuilder.CreateMaterials();
            RtsSandboxBuilder.CreateUnitPrefab(mats["SelectionRing"]);
            EconomyContentBuilder.CreateAssets(mats["SelectionRing"]);
            RtsSandboxBuilder.GeneratePresentationAssets();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var economy = EconomyContentBuilder.Load(); // reload after NewScene (see EconomyContentBuilder.Load)
            catalog = AssetDatabase.LoadAssetAtPath<MapCatalog>(CatalogPath);
            var definition = AssetDatabase.LoadAssetAtPath<MapDefinition>(DefinitionPath(map));
            var normal = catalog != null ? catalog.FindDifficulty(DifficultyLevel.Normal) : null;
            var ground = AssetDatabase.LoadAssetAtPath<Material>(RtsSandboxBuilder.MaterialsDir + "/Ground.mat");
            float size = map.Size, half = size * 0.5f;

            RtsSandboxBuilder.CreateLighting();
            var env = new GameObject("Environment");
            var groundGo = RtsSandboxBuilder.CreateGround(env.transform, ground, size);
            var obstacles = map.ObstacleBoxes();
            var layout = new EnvironmentBuilder.Layout { Name = map.Id, MapSize = size, Obstacles = obstacles, Seed = map.Seed, Palette = map.Palette };
            MeshKit.Tangents = false; // environment meshes only (see MeshKit.Tangents)
            try { EnvironmentBuilder.Obstacles(env.transform, obstacles, EconomyContentBuilder.Art, layout); }
            finally { MeshKit.Tangents = true; }
            foreach (var s in map.Slots) layout.Bases.Add(s.Hq);
            foreach (var (_, p, _) in map.Ore) layout.Ore.Add(p);
            foreach (var r in map.Roads) layout.Roads.Add(r);
            MeshKit.Tangents = false;
            try { RtsSandboxBuilder.ApplyEnvironment(env.transform, groundGo, layout); }
            finally { MeshKit.Tangents = true; }

            var cam = RtsSandboxBuilder.CreateCameraRig(size);
            EnvironmentBuilder.Lighting(cam, map.Palette);
            var rig = cam.transform.parent.GetComponent<RtsCameraController>();
            // Larger maps: faster panning (minimap clicks remain the quick way across).
            EconomyContentBuilder.Set(rig, so => so.FindProperty("panSpeed").floatValue = 25f * Mathf.Clamp(size / 200f, 1f, 2f));
            var (selection, _, placement, orders) = RtsSandboxBuilder.CreateSystems(cam, economy);
            var canvas = RtsSandboxBuilder.CreateUI(selection, placement, orders);

            // Map-size-dependent systems all use the same rectangle (the validator checks this).
            var bounds = new Rect(-half, -half, size, size);
            EconomyContentBuilder.Set(placement, so => so.FindProperty("mapBounds").rectValue = bounds);
            var vision = Object.FindAnyObjectByType<VisionSystem>();
            EconomyContentBuilder.Set(vision, so =>
            {
                so.FindProperty("mapMin").vector2Value = bounds.min;
                so.FindProperty("mapSize").vector2Value = bounds.size;
                so.FindProperty("cellSize").floatValue = map.FogCell;
            });
            var minimap = canvas.GetComponentInChildren<Minimap>(true);
            if (minimap != null)
                EconomyContentBuilder.Set(minimap, so =>
                {
                    so.FindProperty("groundColor").colorValue = (Color)map.Palette.MinimapGround;
                    so.FindProperty("obstacleColor").colorValue = (Color)map.Palette.MinimapObstacle;
                    so.FindProperty("resolution").intValue = size > 250f ? 256 : 160;
                });

            // Start slots (no bases: they are spawned per match). Valid opponents are filled in after validation.
            var slotRoot = new GameObject("StartSlots").transform;
            var slotObjects = new List<StartSlot>();
            foreach (var s in map.Slots) slotObjects.Add(CreateSlot(slotRoot, s));

            var resources = new GameObject("Resources").transform;
            foreach (var (name, position, amount) in map.Ore)
            {
                var deposit = (GameObject)PrefabUtility.InstantiatePrefab(economy.DepositPrefab, resources);
                deposit.name = name;
                deposit.transform.position = position;
                RtsSandboxBuilder.SetInt(deposit.GetComponent<OreDeposit>(), "initialAmount", amount);
            }

            var strategic = new GameObject("StrategicPoints").transform;
            int si = 0;
            foreach (var p in map.Strategic)
            {
                var marker = new GameObject($"{SceneMapValidation.StrategicPrefix}{si++}");
                marker.transform.SetParent(strategic, false);
                marker.transform.position = p;
            }

            var commanderGo = new GameObject("EnemyCommander");
            var commander = commanderGo.AddComponent<EnemyCommander>();
            RtsSandboxBuilder.SetInt(commander, "factionId", ComputerFaction);
            RtsSandboxBuilder.SetRef(commander, "difficulty", normal);
            RtsSandboxBuilder.SetRef(commander, "placement", placement);

            MatchSceneBuilder.CreateMatchFlow(canvas);
            MatchSceneBuilder.CreateDebugPanel(canvas);

            var initGo = new GameObject("Skirmish");
            var init = initGo.AddComponent<SkirmishInitializer>();
            RtsSandboxBuilder.SetRef(init, "map", definition);
            RtsSandboxBuilder.SetRef(init, "catalog", catalog);
            RtsSandboxBuilder.SetRef(init, "match", Object.FindAnyObjectByType<MatchController>());
            RtsSandboxBuilder.SetRef(init, "flow", Object.FindAnyObjectByType<MatchFlowController>());
            RtsSandboxBuilder.SetRef(init, "commander", commander);
            RtsSandboxBuilder.SetRef(init, "cameraRig", rig);
            RtsSandboxBuilder.SetRef(init, "headquartersPrefab", economy.Headquarters.Prefab);
            RtsSandboxBuilder.SetRef(init, "refineryPrefab", economy.Refinery.Prefab);
            RtsSandboxBuilder.SetRef(init, "harvesterPrefab", economy.Harvester.Prefab);
            RtsSandboxBuilder.SetInt(init, "playerFaction", PlayerFaction);
            RtsSandboxBuilder.SetInt(init, "computerFaction", ComputerFaction);

            // Saved camera view over the first slot (the initializer moves it to the player's base at runtime).
            rig.transform.position = Vector3.Lerp(map.Slots[0].Hq, map.Slots[0].Refinery, 0.5f);

            var surface = RtsSandboxBuilder.CreateNavMesh($"{RtsSandboxBuilder.NavDir}/{map.SceneName}_NavMesh.asset");
            Physics.SyncTransforms();

            // Pass 1: derive the valid spawn pairs from the rules and store them on the slots.
            var derive = SceneMapValidation.ValidateLoadedScene(checkStoredTable: false);
            foreach (var slot in slotObjects)
            {
                var opponents = derive.SpawnTable != null ? derive.SpawnTable.OpponentsOf(slot.Slot) : new int[0];
                EconomyContentBuilder.Set(slot, so =>
                {
                    var list = so.FindProperty("validOpponents");
                    list.arraySize = opponents.Count;
                    for (int i = 0; i < opponents.Count; i++) list.GetArrayElementAtIndex(i).intValue = opponents[i];
                });
            }

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, map.ScenePath)) throw new IOException("Failed to save " + map.ScenePath);
            RtsSandboxBuilder.AddSceneToBuildSettingsLast(map.ScenePath);

            // Pass 2: the saved scene, including the stored spawn table.
            var report = SceneMapValidation.ValidateLoadedScene();
            string preview = RenderPreview(map, definition, size);
            AssetDatabase.SaveAssets();

            string summary = $"{map.SceneName}: {size:0} m, {map.Slots.Count} starts, {report.Metric("validOrderedPairs"):0} valid spawn pairs, " +
                             $"{map.Ore.Count} ore fields, {obstacles.Length} obstacles, navmesh={(surface.navMeshData != null ? "ok" : "MISSING")}, " +
                             $"preview={preview}, validation {(report.Passed ? "PASSED" : "FAILED")}";
            Debug.Log("[Ironfront] " + summary + "\n" + report);
            File.WriteAllText(Path.Combine(Application.dataPath, "..", "Temp", $"validation_{map.Id}.txt"), summary + "\n" + report);
            return summary;
        }

        private static StartSlot CreateSlot(Transform parent, SkirmishMapLayouts.Slot s)
        {
            var go = new GameObject($"StartSlot_{s.Id}_{s.Name.Replace(" ", "")}");
            go.transform.SetParent(parent, false);
            go.transform.position = s.Hq;
            var slot = go.AddComponent<StartSlot>();
            EconomyContentBuilder.Set(slot, so =>
            {
                so.FindProperty("slot").intValue = s.Id;
                so.FindProperty("displayName").stringValue = s.Name;
                so.FindProperty("refinery").vector3Value = s.Refinery;
                so.FindProperty("harvester").vector3Value = s.Harvester;
                so.FindProperty("harvesterYaw").floatValue = s.HarvesterYaw;
            });
            return slot;
        }

        // ---------- Data assets ----------

        private static string DefinitionPath(Layout l) => $"{DataDir}/{l.Name.Replace(" ", "")}.asset";

        private static MapDefinition CreateDefinition(Layout l)
        {
            RtsSandboxBuilder.EnsureFolder(DataDir);
            var def = EconomyContentBuilder.LoadOrCreate<MapDefinition>(DefinitionPath(l));
            EconomyContentBuilder.Set(def, so =>
            {
                so.FindProperty("id").stringValue = l.Id;
                so.FindProperty("displayName").stringValue = l.Name;
                so.FindProperty("sceneName").stringValue = l.SceneName;
                so.FindProperty("description").stringValue = l.Description;
                so.FindProperty("tagline").stringValue = l.Tagline;
                so.FindProperty("size").floatValue = l.Size;
                var names = so.FindProperty("startNames");
                names.arraySize = l.Slots.Count;
                for (int i = 0; i < l.Slots.Count; i++) names.GetArrayElementAtIndex(i).stringValue = l.Slots[i].Name;
                so.FindProperty("minSpawnSeparation").floatValue = l.MinSpawnSeparation;
                so.FindProperty("maxSpawnImbalance").floatValue = l.MaxSpawnImbalance;
            });
            return def;
        }

        private static MapCatalog CreateCatalog(List<MapDefinition> maps, List<AiDifficulty> difficulties)
        {
            var catalog = EconomyContentBuilder.LoadOrCreate<MapCatalog>(CatalogPath);
            EconomyContentBuilder.Set(catalog, so =>
            {
                EconomyContentBuilder.SetList(so.FindProperty("maps"), maps.ToArray());
                EconomyContentBuilder.SetList(so.FindProperty("difficulties"), difficulties.ToArray());
            });
            AssetDatabase.SaveAssets();
            return catalog;
        }

        /// <summary>
        /// Easy / Normal / Hard assets. Only behaviour differs (decision speed, strategy switches, defend radius, memory);
        /// vision range and everything that affects game rules is identical for all three.
        /// </summary>
        public static List<AiDifficulty> CreateDifficulties()
        {
            RtsSandboxBuilder.EnsureFolder(AiDir);
            var list = new List<AiDifficulty>
            {
                Difficulty("Easy", DifficultyLevel.Easy, CommanderSettings.Easy, 3f, 18f, 10f, 4,
                    "Slow, cautious and predictable: a small army, no scouting and no reaction to your unit mix. For learning the game."),
                Difficulty("Normal", DifficultyLevel.Normal, CommanderSettings.Normal, 1.5f, 24f, 20f, 3,
                    "Balanced economy, sensible attacks and defences, counters what it sees. The standard opponent."),
                Difficulty("Hard", DifficultyLevel.Hard, CommanderSettings.Hard, 1f, 30f, 40f, 2,
                    "Expands fast, scouts with two units, counter-builds, waits for a strength edge and preserves its army. No bonuses."),
            };
            AssetDatabase.SaveAssets();
            return list;
        }

        private static AiDifficulty Difficulty(string name, DifficultyLevel level, CommanderSettings strategy, float interval,
            float defendRadius, float memory, int stuck, string summary)
        {
            var d = EconomyContentBuilder.LoadOrCreate<AiDifficulty>($"{AiDir}/{name}.asset");
            EconomyContentBuilder.Set(d, so =>
            {
                so.FindProperty("displayName").stringValue = name;
                so.FindProperty("level").enumValueIndex = (int)level;
                so.FindProperty("summary").stringValue = summary;
                so.FindProperty("decisionInterval").floatValue = interval;
                so.FindProperty("defendRadius").floatValue = defendRadius;
                so.FindProperty("threatMemorySeconds").floatValue = memory;
                so.FindProperty("stuckDecisions").intValue = stuck;
                so.FindProperty("visionRange").floatValue = 22f;
                so.FindProperty("rallyDistance").floatValue = 14f;
                var s = so.FindProperty("strategy");
                object boxed = strategy;
                foreach (var f in typeof(CommanderSettings).GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    var p = s.FindPropertyRelative(f.Name);
                    if (p == null) throw new System.InvalidOperationException("CommanderSettings." + f.Name + " is not serialized");
                    object v = f.GetValue(boxed);
                    switch (v)
                    {
                        case float fv: p.floatValue = fv; break;
                        case int iv: p.intValue = iv; break;
                        case bool bv: p.boolValue = bv; break;
                        default: throw new System.InvalidOperationException("Unsupported CommanderSettings field type " + f.FieldType);
                    }
                }
            });
            return d;
        }

        // ---------- Preview ----------

        /// <summary>
        /// Renders the open map scene top-down into a preview texture with numbered start markers (the numbers the setup
        /// menu uses) and ore dots. Returns the asset path.
        /// </summary>
        private static string RenderPreview(Layout map, MapDefinition def, float size)
        {
            RtsSandboxBuilder.EnsureFolder(PreviewDir);
            const int res = 512;
            var go = new GameObject("PreviewCamera");
            var cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = size * 0.5f;
            cam.transform.position = new Vector3(0f, 120f, 0f);
            cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = map.Palette.FogColor;
            cam.nearClipPlane = 1f;
            cam.farClipPlane = 300f;
            var data = go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            var rt = RenderTexture.GetTemporary(res, res, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            cam.targetTexture = rt;
            bool fog = RenderSettings.fog;
            RenderSettings.fog = false;
            cam.Render();
            RenderSettings.fog = fog;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, res, res), 0, 0);
            RenderTexture.active = prev;
            cam.targetTexture = null;
            RenderTexture.ReleaseTemporary(rt);
            Object.DestroyImmediate(go);

            AutoLevel(tex);
            Vector2Int Px(Vector3 w) => new Vector2Int(Mathf.RoundToInt((w.x / size + 0.5f) * res), Mathf.RoundToInt((w.z / size + 0.5f) * res));
            foreach (var (_, p, _) in map.Ore) Disc(tex, Px(p), 4, new Color(1f, 0.72f, 0.15f), 2);
            foreach (var s in map.Slots)
            {
                var c = Px(s.Hq);
                Disc(tex, c, 15, new Color(0.35f, 0.75f, 1f), 3);
                Digit(tex, c, s.Id + 1, Color.white);
            }
            tex.Apply();

            string path = $"{PreviewDir}/{map.Name.Replace(" ", "")}_Preview.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
            var saved = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (def != null) EconomyContentBuilder.Set(def, so => so.FindProperty("preview").objectReferenceValue = saved);
            return path;
        }

        /// <summary>Brightens dark renders (industrial haze) so every preview reads at menu size.</summary>
        private static void AutoLevel(Texture2D tex)
        {
            var px = tex.GetPixels();
            float sum = 0f;
            foreach (var c in px) sum += c.grayscale;
            float mean = sum / px.Length, gain = Mathf.Clamp(0.42f / Mathf.Max(0.05f, mean), 1f, 3f);
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i] * gain;
                px[i] = new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), 1f);
            }
            tex.SetPixels(px);
        }

        // 3x5 digit glyphs (rows top to bottom) for the preview's start numbers.
        private static readonly string[] Glyphs =
        {
            "111101101101111", "010110010010111", "111001111100111", "111001111001111", "101101111001001",
            "111100111001111", "111100111101111", "111001001001001", "111101111101111", "111101111001111",
        };

        private static void Digit(Texture2D t, Vector2Int c, int digit, Color color)
        {
            string g = Glyphs[Mathf.Clamp(digit, 0, 9)];
            const int scale = 3;
            for (int row = 0; row < 5; row++)
            for (int col = 0; col < 3; col++)
            {
                if (g[row * 3 + col] != '1') continue;
                for (int dy = 0; dy < scale; dy++)
                for (int dx = 0; dx < scale; dx++)
                {
                    int px = c.x + (col - 1) * scale - 1 + dx, py = c.y + (2 - row) * scale - 1 + dy;
                    if (px >= 0 && py >= 0 && px < t.width && py < t.height) t.SetPixel(px, py, color);
                }
            }
        }

        /// <summary>Ring marker (outline width <paramref name="ring"/>) with a dark border for contrast.</summary>
        private static void Disc(Texture2D t, Vector2Int c, int radius, Color color, int ring)
        {
            for (int y = -radius - 2; y <= radius + 2; y++)
            for (int x = -radius - 2; x <= radius + 2; x++)
            {
                int px = c.x + x, py = c.y + y;
                if (px < 0 || py < 0 || px >= t.width || py >= t.height) continue;
                float d = Mathf.Sqrt(x * x + y * y);
                if (d <= radius && d >= radius - ring) t.SetPixel(px, py, color);
                else if (d <= radius + 1.5f && d > radius) t.SetPixel(px, py, new Color(0.05f, 0.05f, 0.05f));
                else if (d < radius - ring) t.SetPixel(px, py, Color.Lerp(t.GetPixel(px, py), color, 0.35f));
            }
        }
    }
}
