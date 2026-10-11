using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ironfront.CameraControl;
using Ironfront.Audio;
using Ironfront.Combat;
using Ironfront.Commands;
using Ironfront.EditorTools.Art;
using Ironfront.Construction;
using Ironfront.Match;
using Ironfront.Selection;
using Ironfront.UI;
using Ironfront.Units;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Ironfront.EditorTools
{
    /// <summary>
    /// Builds the RTS_Sandbox scene, its materials, the unit prefab and baked NavMesh from code,
    /// so the scene can be regenerated deterministically. Menu: Ironfront > Build RTS Sandbox Scene.
    /// </summary>
    public static class RtsSandboxBuilder
    {
        public const string ScenePath = "Assets/Scenes/RTS_Sandbox.unity";
        private const string Root = "Assets/Ironfront";
        internal const string MaterialsDir = Root + "/Materials";
        internal const string PrefabsDir = Root + "/Prefabs";
        internal const string DataDir = Root + "/Data";
        internal const string NavDir = Root + "/Navigation";
        public const string UnitPrefabPath = PrefabsDir + "/Unit.prefab";
        private const string NavMeshDataPath = NavDir + "/RTS_Sandbox_NavMesh.asset";

        public const string GroundLayerName = "Ground";
        public const string UnitsLayerName = "Units";
        public const string StructuresLayerName = "Structures";
        public const string ResourcesLayerName = "Resources";
        internal const int GroundLayerIndex = 8;
        internal const int UnitsLayerIndex = 9;
        internal const int StructuresLayerIndex = 10;
        internal const int ResourcesLayerIndex = 11;

        private const float MapSize = 100f;

        private static readonly (string name, Color color)[] UnitColors =
        {
            ("Red", new Color(0.85f, 0.2f, 0.2f)),
            ("Blue", new Color(0.2f, 0.4f, 0.9f)),
            ("Yellow", new Color(0.95f, 0.8f, 0.15f)),
            ("Purple", new Color(0.6f, 0.3f, 0.85f)),
            ("Orange", new Color(0.95f, 0.5f, 0.1f)),
        };

        private static readonly Vector3[] UnitStartPositions =
        {
            new Vector3(-24f, 0f, -22f), new Vector3(-21f, 0f, -20f), new Vector3(-18f, 0f, -22f),
            new Vector3(-22f, 0f, -17f), new Vector3(-17f, 0f, -18f),
        };

        private static readonly (string name, Vector3 center, Vector3 size)[] Obstacles =
        {
            ("Obstacle_CentralWall", new Vector3(0f, 1.5f, 0f), new Vector3(36f, 3f, 2f)),
            ("Obstacle_BlockWest", new Vector3(-25f, 1.5f, 10f), new Vector3(6f, 3f, 6f)),
            ("Obstacle_BlockEast", new Vector3(25f, 1.5f, -12f), new Vector3(8f, 3f, 4f)),
            ("Obstacle_Tower", new Vector3(10f, 3f, -20f), new Vector3(4f, 6f, 4f)),
            ("Obstacle_NorthWall", new Vector3(15f, 1.5f, 15f), new Vector3(2f, 3f, 12f)),
            ("Obstacle_Rock", new Vector3(-8f, 1f, 25f), new Vector3(5f, 2f, 5f)),
        };

        [MenuItem("Ironfront/Build RTS Sandbox Scene")]
        public static void BuildFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build();
        }

        /// <summary>Entry point for automation. Rebuilds and saves everything; returns a summary.</summary>
        public static string Build()
        {
            EnsureFolder(MaterialsDir);
            EnsureFolder(PrefabsDir);
            EnsureFolder(NavDir);
            EnsureLayers();

            var mats = CreateMaterials();
            var unitPrefab = CreateUnitPrefab(mats["SelectionRing"]);
            EconomyContentBuilder.CreateAssets(mats["SelectionRing"]);
            GeneratePresentationAssets();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var economy = EconomyContentBuilder.Load(); // reload: NewScene unloads unreferenced assets

            CreateLighting();
            var env = new GameObject("Environment");
            var ground = CreateGround(env.transform, mats["Ground"]);
            CreateObstacles(env.transform, mats["Obstacle"]);
            var layout = new EnvironmentBuilder.Layout { Name = "Sandbox", Obstacles = Obstacles, Seed = 3 };
            layout.Bases.Add(EconomyContentBuilder.HeadquartersPosition);
            layout.Ore.Add(EconomyContentBuilder.DepositPosition);
            layout.Roads.Add(new[] { new Vector3(-41f, 0f, -35f), new Vector3(-34f, 0f, -33f), new Vector3(-24f, 0f, -34f), new Vector3(-12f, 0f, -36f) });
            layout.Roads.Add(new[] { new Vector3(-30f, 0f, -33f), new Vector3(-22f, 0f, -20f), new Vector3(-30f, 0f, 0f), new Vector3(-36f, 0f, 22f) });
            ApplyEnvironment(env.transform, ground, layout);

            var cam = CreateCameraRig();
            EnvironmentBuilder.Lighting(cam);
            var units = CreateUnits(unitPrefab, mats);
            var (selection, _, placement, orders) = CreateSystems(cam, economy);
            CreateUI(selection, placement, orders);
            EconomyContentBuilder.PopulateScene(economy);
            CombatContentBuilder.PopulateScenario(economy.Combat);

            var surface = CreateNavMesh();

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new IOException("Failed to save " + ScenePath);

            AddSceneToBuildSettingsFirst(ScenePath);
            AssetDatabase.SaveAssets();

            string summary = $"RTS_Sandbox built: {units.Count} scouts, {Obstacles.Length} obstacles, " +
                             $"{EconomyContentBuilder.Summary}, {CombatContentBuilder.Summary}, " +
                             $"navmesh={(surface.navMeshData != null ? AssetDatabase.GetAssetPath(surface.navMeshData) : "MISSING")}";
            Debug.Log("[Ironfront] " + summary);
            return summary;
        }

        // ---------- Assets ----------

        /// <summary>Milestone 006: synthesised audio, interface sprites and model icons (after the prefabs exist).</summary>
        internal static void GeneratePresentationAssets()
        {
            AudioContentBuilder.Generate();
            UiTheme.Skin = UiArtBuilder.Generate();
            UiArtBuilder.RenderIcons(EconomyContentBuilder.Load());
        }

        internal static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        internal static void EnsureLayers()
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("layers");
            SetLayer(layers, GroundLayerIndex, GroundLayerName);
            SetLayer(layers, UnitsLayerIndex, UnitsLayerName);
            SetLayer(layers, StructuresLayerIndex, StructuresLayerName);
            SetLayer(layers, ResourcesLayerIndex, ResourcesLayerName);
            tagManager.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetLayer(SerializedProperty layers, int index, string name)
        {
            var element = layers.GetArrayElementAtIndex(index);
            if (string.IsNullOrEmpty(element.stringValue)) element.stringValue = name;
            else if (element.stringValue != name)
                throw new System.InvalidOperationException($"Layer {index} is already used by '{element.stringValue}'.");
        }

        internal static Dictionary<string, Material> CreateMaterials()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (lit == null || unlit == null) throw new System.InvalidOperationException("URP shaders not found.");

            var mats = new Dictionary<string, Material>
            {
                ["Ground"] = CreateOrUpdateMaterial("Ground", lit, new Color(0.36f, 0.45f, 0.27f)),
                ["Obstacle"] = CreateOrUpdateMaterial("Obstacle", lit, new Color(0.45f, 0.45f, 0.48f)),
                ["SelectionRing"] = CreateOrUpdateMaterial("SelectionRing", unlit, new Color(0.2f, 1f, 0.3f)),
            };
            foreach (var (name, color) in UnitColors)
                mats["Unit_" + name] = CreateOrUpdateMaterial("Unit_" + name, lit, color);
            return mats;
        }

        internal static Material CreateOrUpdateMaterial(string name, Shader shader, Color color)
        {
            string path = $"{MaterialsDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            mat.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        internal static GameObject CreateUnitPrefab(Material ringMaterial)
        {
            var unit = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            unit.name = "Unit";
            unit.layer = UnitsLayerIndex;

            AddAgent(unit, radius: 0.5f, height: 2f, baseOffset: 1f, speed: 7f); // capsule pivot is at its centre
            unit.AddComponent<UnitMover>();
            unit.AddComponent<FactionMember>();
            var selectable = unit.AddComponent<SelectableUnit>();
            unit.AddComponent<Ironfront.Vision.VisionEntity>();

            // Facing marker so orientation is readable.
            var nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
            nose.name = "Nose";
            Object.DestroyImmediate(nose.GetComponent<Collider>());
            nose.transform.SetParent(unit.transform, false);
            nose.transform.localPosition = new Vector3(0f, 0.45f, 0.45f);
            nose.transform.localScale = new Vector3(0.35f, 0.25f, 0.3f);
            nose.layer = UnitsLayerIndex;

            var ring = AddSelectionRing(unit.transform, ringMaterial, -0.97f, 1.7f);
            SetRef(selectable, "selectionIndicator", ring);

            // Unarmed scout: can be attacked and destroyed (100 HP, no armour).
            var health = unit.AddComponent<Health>();
            var hso = new SerializedObject(health);
            hso.FindProperty("fallbackMaxHealth").floatValue = 100f;
            hso.FindProperty("aimHeight").floatValue = 0f; // capsule pivot is at its centre
            hso.FindProperty("explosionSize").floatValue = 2f;
            hso.ApplyModifiedPropertiesWithoutUndo();
            var bar = unit.AddComponent<HealthBar>();
            var bso = new SerializedObject(bar);
            bso.FindProperty("height").floatValue = 1.7f;
            bso.FindProperty("width").floatValue = 1.4f;
            bso.ApplyModifiedPropertiesWithoutUndo();

            var prefab = PrefabUtility.SaveAsPrefabAsset(unit, UnitPrefabPath, out bool ok);
            Object.DestroyImmediate(unit);
            if (!ok || prefab == null) throw new IOException("Failed to save unit prefab.");
            return prefab;
        }

        internal static NavMeshAgent AddAgent(GameObject go, float radius, float height, float baseOffset, float speed)
        {
            var agent = go.AddComponent<NavMeshAgent>();
            agent.radius = radius;
            agent.height = height;
            agent.baseOffset = baseOffset;
            agent.speed = speed;
            agent.angularSpeed = 720f;
            agent.acceleration = 25f;
            agent.stoppingDistance = 0.1f;
            agent.autoBraking = true;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
            agent.enabled = false; // UnitMover enables it in Start, after the NavMesh is loaded
            return agent;
        }

        internal static GameObject AddSelectionRing(Transform parent, Material material, float localY, float diameter)
        {
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "SelectionIndicator";
            Object.DestroyImmediate(ring.GetComponent<Collider>());
            ring.transform.SetParent(parent, false);
            ring.transform.localPosition = new Vector3(0f, localY, 0f);
            ring.transform.localScale = new Vector3(diameter, 0.02f, diameter);
            ring.layer = parent.gameObject.layer;
            var ringRenderer = ring.GetComponent<MeshRenderer>();
            ringRenderer.sharedMaterial = material;
            ringRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ring.SetActive(false);
            return ring;
        }

        internal static void SetRef(Object target, string property, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(property);
            if (prop == null) throw new System.ArgumentException($"{target.GetType().Name} has no serialized field {property}.");
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        internal static void SetInt(Object target, string property, int value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(property);
            if (prop == null) throw new System.ArgumentException($"{target.GetType().Name} has no serialized field {property}.");
            prop.intValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---------- Scene content ----------

        internal static void CreateLighting()
        {
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.shadows = LightShadows.Soft;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.6f, 0.7f);
            RenderSettings.ambientEquatorColor = new Color(0.4f, 0.42f, 0.4f);
            RenderSettings.ambientGroundColor = new Color(0.2f, 0.2f, 0.2f);
        }

        /// <summary>Milestone 006: painted ground, backdrop, props and render settings for a layout.</summary>
        internal static void ApplyEnvironment(Transform env, GameObject ground, EnvironmentBuilder.Layout layout)
        {
            var art = EconomyContentBuilder.Art;
            EnvironmentBuilder.ConfigurePipeline();
            EnvironmentBuilder.StyleGround(ground, EnvironmentBuilder.GroundMaterial(layout, art));
            EnvironmentBuilder.Backdrop(env, layout, art);
            EnvironmentBuilder.Props(env, layout, art);
        }

        internal static GameObject CreateGround(Transform parent, Material material)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane); // 10x10 units at scale 1
            ground.name = "Ground";
            ground.transform.SetParent(parent, false);
            ground.transform.localScale = new Vector3(MapSize / 10f, 1f, MapSize / 10f);
            ground.layer = GroundLayerIndex;
            ground.GetComponent<MeshRenderer>().sharedMaterial = material;
            GameObjectUtility.SetStaticEditorFlags(ground, StaticEditorFlags.BatchingStatic);
            return ground;
        }

        private static void CreateObstacles(Transform parent, Material material) => CreateObstacles(parent, material, Obstacles);

        /// <summary>
        /// Obstacles: same names, positions and collider bounds as before (navigation, line of sight, placement and
        /// minimap are unchanged); Milestone 006 gives each a styled mesh instead of a grey cube.
        /// </summary>
        internal static void CreateObstacles(Transform parent, Material material, (string name, Vector3 center, Vector3 size)[] set) =>
            EnvironmentBuilder.Obstacles(parent, set, EconomyContentBuilder.Art);

        internal static Camera CreateCameraRig()
        {
            var rig = new GameObject("CameraRig");
            rig.transform.position = new Vector3(-28f, 0f, -33f);

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(rig.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 400f;
            cam.fieldOfView = 50f;
            cam.clearFlags = CameraClearFlags.Skybox;
            camGo.AddComponent<AudioListener>();

            var controller = rig.AddComponent<RtsCameraController>();
            var so = new SerializedObject(controller);
            so.FindProperty("controlledCamera").objectReferenceValue = cam;
            so.FindProperty("boundsMin").vector2Value = new Vector2(-MapSize / 2f, -MapSize / 2f);
            so.FindProperty("boundsMax").vector2Value = new Vector2(MapSize / 2f, MapSize / 2f);
            so.ApplyModifiedPropertiesWithoutUndo();

            // Place the camera now so the saved scene already shows the RTS view.
            camGo.transform.position = rig.transform.position + Core.CameraMath.OrbitOffset(55f, 0f, 35f);
            camGo.transform.rotation = Quaternion.Euler(55f, 0f, 0f);
            return cam;
        }

        private static List<SelectableUnit> CreateUnits(GameObject prefab, Dictionary<string, Material> mats)
        {
            var root = new GameObject("Units").transform;
            var units = new List<SelectableUnit>();
            for (int i = 0; i < UnitColors.Length; i++)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
                go.name = $"Unit_{i + 1:00}_{UnitColors[i].name}";
                go.transform.position = UnitStartPositions[i] + Vector3.up * 1f;
                go.transform.rotation = Quaternion.Euler(0f, 45f, 0f);
                go.GetComponent<MeshRenderer>().sharedMaterial = mats["Unit_" + UnitColors[i].name];
                go.transform.Find("Nose").GetComponent<MeshRenderer>().sharedMaterial = mats["Unit_" + UnitColors[i].name];
                units.Add(go.GetComponent<SelectableUnit>());
            }
            return units;
        }

        internal static (SelectionManager, UnitCommandController, BuildingPlacementController, OrderInputController) CreateSystems(
            Camera cam, EconomyContentBuilder.Content economy)
        {
            var matchGo = new GameObject("Match");
            var match = matchGo.AddComponent<MatchController>();
            SetRef(match, "setup", economy.MatchSetup);
            matchGo.AddComponent<MatchStatisticsTracker>();
            matchGo.AddComponent<Ironfront.Vision.VisionSystem>();
            matchGo.AddComponent<Ironfront.Vision.FogOfWarView>();

            var systems = new GameObject("RTS Systems");
            var selection = systems.AddComponent<SelectionManager>();
            var commands = systems.AddComponent<UnitCommandController>();
            var placement = systems.AddComponent<BuildingPlacementController>();
            var orders = systems.AddComponent<OrderInputController>();
            var feedback = systems.AddComponent<OrderFeedback>();
            var groups = systems.AddComponent<ControlGroupInput>();
            SetRef(groups, "selection", selection);
            SetRef(groups, "cameraController", cam.transform.parent.GetComponent<RtsCameraController>());

            SetRef(selection, "viewCamera", cam);
            SetInt(selection, "unitLayers", 1 << UnitsLayerIndex);
            SetInt(selection, "structureLayers", 1 << StructuresLayerIndex);

            SetRef(commands, "selectionManager", selection);
            SetInt(commands, "groundLayers", 1 << GroundLayerIndex);
            SetInt(commands, "resourceLayers", 1 << ResourcesLayerIndex);
            SetInt(commands, "targetLayers", (1 << UnitsLayerIndex) | (1 << StructuresLayerIndex));
            SetRef(orders, "commands", commands);
            SetRef(orders, "selection", selection);
            SetRef(feedback, "commands", commands);
            SetRef(feedback, "selectionManager", selection);

            SetRef(placement, "viewCamera", cam);
            SetInt(placement, "groundLayers", 1 << GroundLayerIndex);
            SetInt(placement, "obstacleLayers", 1 << 0); // Environment/Obstacles are on Default
            SetInt(placement, "resourceLayers", 1 << ResourcesLayerIndex);
            SetInt(placement, "unitLayers", 1 << UnitsLayerIndex);
            SetRef(placement, "validMaterial", economy.GhostValid);
            SetRef(placement, "invalidMaterial", economy.GhostInvalid);

            return (selection, commands, placement, orders);
        }

        internal static GameObject CreateUI(SelectionManager selection, BuildingPlacementController placement, OrderInputController orders)
        {
            var esGo = new GameObject("EventSystem");
            esGo.AddComponent<EventSystem>();
            esGo.AddComponent<InputSystemUIInputModule>();

            var canvasGo = new GameObject("HUD Canvas");
            canvasGo.layer = LayerMask.NameToLayer("UI");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            // Scale the HUD with resolution (was constant pixel size: oversized at 720p, tiny at 4K).
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600f, 900f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            UiTheme.Skin = UiArtBuilder.Load();
            SetRef(canvasGo.AddComponent<UiSkinProvider>(), "skin", UiTheme.Skin);

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // HUD panel (raycast target: clicks on it must not reach the selection system).
            var panel = NewUiObject("SelectionPanel", canvasGo.transform);
            var panelRect = (RectTransform)panel.transform;
            panelRect.anchorMin = panelRect.anchorMax = panelRect.pivot = new Vector2(0f, 1f);
            panelRect.anchoredPosition = new Vector2(10f, -10f);
            panelRect.sizeDelta = new Vector2(680f, 112f);
            var panelImage = panel.AddComponent<Image>();
            UiTheme.ApplyPanel(panelImage, UiTheme.Panel);
            panelImage.raycastTarget = true;

            // Portrait of the selected type (left), text to its right.
            var portraitGo = NewUiObject("Portrait", panel.transform);
            var portraitRect = (RectTransform)portraitGo.transform;
            portraitRect.anchorMin = portraitRect.anchorMax = portraitRect.pivot = new Vector2(0f, 0.5f);
            portraitRect.anchoredPosition = new Vector2(6f, 0f);
            portraitRect.sizeDelta = new Vector2(100f, 100f);
            var portrait = portraitGo.AddComponent<Image>();
            portrait.preserveAspect = true;
            portrait.raycastTarget = false;
            portraitGo.SetActive(false);

            var labelGo = NewUiObject("Label", panel.transform);
            var labelRect = (RectTransform)labelGo.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(10f, 6f);
            labelRect.offsetMax = new Vector2(-10f, -6f);
            var label = labelGo.AddComponent<Text>();
            labelRect.offsetMin = new Vector2(112f, 6f);
            label.font = font;
            label.fontSize = 14;
            label.color = UiTheme.Text;
            label.raycastTarget = false;
            label.text = "Selected units: 0";

            var hud = panel.AddComponent<SelectionHud>();
            var hudSo = new SerializedObject(hud);
            hudSo.FindProperty("selectionManager").objectReferenceValue = selection;
            hudSo.FindProperty("label").objectReferenceValue = label;
            hudSo.FindProperty("portrait").objectReferenceValue = portrait;
            hudSo.ApplyModifiedPropertiesWithoutUndo();

            // Drag-select rectangle.
            var box = NewUiObject("SelectionBox", canvasGo.transform);
            var boxRect = (RectTransform)box.transform;
            boxRect.anchorMin = boxRect.anchorMax = boxRect.pivot = Vector2.zero;
            var boxImage = box.AddComponent<Image>();
            boxImage.color = new Color(0.3f, 1f, 0.4f, 0.18f);
            boxImage.raycastTarget = false;
            var outline = box.AddComponent<Outline>();
            outline.effectColor = new Color(0.3f, 1f, 0.4f, 0.9f);
            outline.effectDistance = new Vector2(1f, -1f);
            var boxView = box.AddComponent<SelectionBoxView>();
            var boxSo = new SerializedObject(boxView);
            boxSo.FindProperty("canvas").objectReferenceValue = canvas;
            boxSo.ApplyModifiedPropertiesWithoutUndo();
            box.SetActive(false);

            SetRef(selection, "selectionBox", boxView);

            // Economy sidebar (raycast target, so clicks on it never reach the world).
            var sidebar = NewUiObject("EconomyPanel", canvasGo.transform);
            var sidebarRect = (RectTransform)sidebar.transform;
            sidebarRect.anchorMin = sidebarRect.anchorMax = sidebarRect.pivot = new Vector2(1f, 1f);
            sidebarRect.anchoredPosition = new Vector2(-10f, -10f);
            sidebarRect.sizeDelta = new Vector2(320f, 720f);
            var sidebarImage = sidebar.AddComponent<Image>();
            UiTheme.ApplyPanel(sidebarImage, UiTheme.Panel);
            sidebarImage.raycastTarget = true;
            var layout = sidebar.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 10, 10);
            layout.spacing = 4f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            var economyHud = sidebar.AddComponent<EconomyHud>();
            SetRef(economyHud, "content", sidebarRect);
            SetRef(economyHud, "placement", placement);
            SetRef(economyHud, "selectionManager", selection);
            SetRef(economyHud, "font", font);

            // Combat orders panel (bottom centre; shown while armed units are selected).
            var ordersPanel = NewUiObject("OrdersPanel", canvasGo.transform);
            var ordersRect = (RectTransform)ordersPanel.transform;
            ordersRect.anchorMin = ordersRect.anchorMax = ordersRect.pivot = new Vector2(0.5f, 0f);
            ordersRect.anchoredPosition = new Vector2(0f, 10f);
            ordersRect.sizeDelta = new Vector2(560f, 76f);
            var ordersImage = ordersPanel.AddComponent<Image>();
            UiTheme.ApplyPanel(ordersImage, UiTheme.Panel);
            ordersImage.raycastTarget = true;
            var ordersLayout = ordersPanel.AddComponent<VerticalLayoutGroup>();
            ordersLayout.padding = new RectOffset(8, 8, 8, 6);
            ordersLayout.spacing = 4f;
            ordersLayout.childControlHeight = true;
            ordersLayout.childControlWidth = true;
            ordersLayout.childForceExpandHeight = false;

            var ordersHud = canvasGo.AddComponent<OrdersHud>(); // on the canvas: the panel itself gets hidden
            SetRef(ordersHud, "panel", ordersRect);
            SetRef(ordersHud, "orders", orders);
            SetRef(ordersHud, "selectionManager", selection);
            SetRef(ordersHud, "font", font);

            CreateFeedbackUI(canvasGo.transform, placement);
            CreateAudio(selection, placement, canvasGo.GetComponent<NotificationCenter>());
            return canvasGo;
        }

        /// <summary>Scene audio: the generated sound bank player plus the router from gameplay events to sounds.</summary>
        internal static void CreateAudio(SelectionManager selection, BuildingPlacementController placement, NotificationCenter notifications)
        {
            var go = new GameObject("Audio");
            var audio = go.AddComponent<GameAudio>();
            SetRef(audio, "bank", AudioContentBuilder.LoadBank());
            var router = go.AddComponent<AudioEventRouter>();
            SetRef(router, "selection", selection);
            SetRef(router, "commands", Object.FindAnyObjectByType<UnitCommandController>());
            SetRef(router, "placement", placement);
            SetRef(router, "notifications", notifications);
        }

        /// <summary>Minimap, notification feed, placement hint, tooltip panel and F1 help overlay.</summary>
        private static void CreateFeedbackUI(Transform canvas, BuildingPlacementController placement)
        {
            var cameraController = Object.FindAnyObjectByType<RtsCameraController>();

            // Minimap (bottom-left).
            var mapFrame = NewUiObject("MinimapPanel", canvas);
            var mapRect = (RectTransform)mapFrame.transform;
            mapRect.anchorMin = mapRect.anchorMax = mapRect.pivot = Vector2.zero;
            mapRect.anchoredPosition = new Vector2(10f, 10f);
            mapRect.sizeDelta = new Vector2(210f, 210f);
            var frameImage = mapFrame.AddComponent<Image>();
            UiTheme.ApplyPanel(frameImage, UiTheme.PanelStrong);
            frameImage.raycastTarget = true;
            var mapGo = NewUiObject("Map", mapFrame.transform);
            UiTheme.Stretch((RectTransform)mapGo.transform, 5f);
            var raw = mapGo.AddComponent<RawImage>();
            raw.raycastTarget = true;
            var minimap = mapGo.AddComponent<Minimap>();
            SetRef(minimap, "image", raw);
            SetRef(minimap, "cameraController", cameraController);
            SetInt(minimap, "obstacleLayers", 1 << 0);

            // Notification feed (top centre, between the selection panel and the sidebar).
            var feedGo = NewUiObject("NotificationFeed", canvas);
            var feedRect = (RectTransform)feedGo.transform;
            feedRect.anchorMin = feedRect.anchorMax = feedRect.pivot = new Vector2(0.5f, 1f);
            feedRect.anchoredPosition = new Vector2(165f, -10f);
            feedRect.sizeDelta = new Vector2(560f, 130f);
            var feedLayout = feedGo.AddComponent<VerticalLayoutGroup>();
            feedLayout.childControlHeight = true;
            feedLayout.childControlWidth = true;
            feedLayout.childForceExpandHeight = false;
            feedLayout.childAlignment = TextAnchor.UpperCenter;
            feedLayout.spacing = 2f;
            var center = canvas.gameObject.AddComponent<NotificationCenter>();
            SetRef(center, "feed", feedRect);
            SetRef(center, "cameraController", cameraController);
            SetRef(center, "placement", placement);

            // Placement hint (follows the cursor while placing).
            var hintGo = NewUiObject("PlacementHint", canvas);
            var hintRect = (RectTransform)hintGo.transform;
            hintRect.anchorMin = hintRect.anchorMax = Vector2.zero;
            hintRect.pivot = new Vector2(0f, 1f);
            hintRect.sizeDelta = new Vector2(300f, 64f);
            var hintImage = hintGo.AddComponent<Image>();
            UiTheme.ApplyPanel(hintImage, UiTheme.PanelStrong);
            hintImage.raycastTarget = false;
            var hintLabel = UiTheme.Label(hintGo.transform, "Label", 13, UiTheme.Text);
            UiTheme.Stretch(hintLabel.rectTransform, 6f);
            var hint = canvas.gameObject.AddComponent<PlacementHint>();
            SetRef(hint, "placement", placement);
            SetRef(hint, "panel", hintRect);
            SetRef(hint, "label", hintLabel);
            hintGo.SetActive(false);

            // F1 help overlay (centred).
            var helpGo = NewUiObject("HelpOverlay", canvas);
            var helpRect = (RectTransform)helpGo.transform;
            helpRect.anchorMin = helpRect.anchorMax = helpRect.pivot = new Vector2(0.5f, 0.5f);
            helpRect.sizeDelta = new Vector2(820f, 360f);
            var helpImage = helpGo.AddComponent<Image>();
            UiTheme.ApplyPanel(helpImage, UiTheme.PanelStrong);
            helpImage.raycastTarget = true;
            var keysLabel = UiTheme.Label(helpGo.transform, "Keys", 15, UiTheme.Accent);
            UiTheme.Stretch(keysLabel.rectTransform, 18f);
            var descriptionsLabel = UiTheme.Label(helpGo.transform, "Descriptions", 15, UiTheme.Text);
            UiTheme.Stretch(descriptionsLabel.rectTransform, 18f);
            descriptionsLabel.rectTransform.offsetMin = new Vector2(200f, 18f); // second column
            var help = canvas.gameObject.AddComponent<HelpOverlay>();
            SetRef(help, "panel", helpGo);
            SetRef(help, "keysLabel", keysLabel);
            SetRef(help, "descriptionsLabel", descriptionsLabel);

            // Reminder that help exists (above the minimap).
            var helpHint = UiTheme.Label(canvas, "HelpHint", 12, UiTheme.Muted);
            helpHint.rectTransform.anchorMin = helpHint.rectTransform.anchorMax = helpHint.rectTransform.pivot = Vector2.zero;
            helpHint.rectTransform.anchoredPosition = new Vector2(12f, 224f);
            helpHint.rectTransform.sizeDelta = new Vector2(240f, 18f);
            helpHint.text = "F1: controls    Space: last alert    F10: menu";
            UiTheme.Shadow(helpHint);

            // Tooltip panel (last: drawn on top).
            var tipGo = NewUiObject("Tooltip", canvas);
            var tipRect = (RectTransform)tipGo.transform;
            tipRect.anchorMin = tipRect.anchorMax = tipRect.pivot = Vector2.zero;
            tipRect.sizeDelta = new Vector2(280f, 60f);
            var tipImage = tipGo.AddComponent<Image>();
            UiTheme.ApplyPanel(tipImage, UiTheme.PanelStrong);
            tipImage.raycastTarget = false;
            var tipOutline = tipGo.AddComponent<Outline>();
            tipOutline.effectColor = UiTheme.Accent;
            var tipLabel = UiTheme.Label(tipGo.transform, "Label", 13, UiTheme.Text);
            UiTheme.Stretch(tipLabel.rectTransform, 8f);
            var tooltip = canvas.gameObject.AddComponent<TooltipView>();
            SetRef(tooltip, "panel", tipRect);
            SetRef(tooltip, "label", tipLabel);

            // Pause / options menu (F10 or the MENU button), above everything but the tooltip.
            UiBuild.GameMenu(canvas);
            tipGo.transform.SetAsLastSibling();
        }

        internal static GameObject NewUiObject(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);
            return go;
        }

        private static NavMeshSurface CreateNavMesh() => CreateNavMesh(NavMeshDataPath);

        internal static NavMeshSurface CreateNavMesh(string navMeshDataPath)
        {
            var navGo = new GameObject("Navigation");
            var surface = navGo.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            // Units, structures and ore deposits are excluded: structures and deposits carve the NavMesh at
            // runtime with NavMeshObstacles, so the bake stays valid as buildings are added or ore runs out.
            surface.layerMask = ~(1 << UnitsLayerIndex) & ~(1 << StructuresLayerIndex) & ~(1 << ResourcesLayerIndex)
                                & ~(1 << LayerMask.NameToLayer("UI"));
            surface.agentTypeID = 0; // Humanoid default: radius 0.5, height 2

            surface.BuildNavMesh();
            if (surface.navMeshData == null) throw new System.InvalidOperationException("NavMesh bake produced no data.");

            if (AssetDatabase.LoadAssetAtPath<NavMeshData>(navMeshDataPath) != null)
                AssetDatabase.DeleteAsset(navMeshDataPath);
            AssetDatabase.CreateAsset(surface.navMeshData, navMeshDataPath);
            EditorUtility.SetDirty(surface);
            return surface;
        }

        internal static void AddSceneToBuildSettingsLast(string path)
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != path).ToList();
            scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        internal static void AddSceneToBuildSettingsFirst(string path)
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != path).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
