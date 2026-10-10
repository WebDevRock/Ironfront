using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ironfront.CameraControl;
using Ironfront.Commands;
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
        private const string MaterialsDir = Root + "/Materials";
        private const string PrefabsDir = Root + "/Prefabs";
        private const string NavDir = Root + "/Navigation";
        public const string UnitPrefabPath = PrefabsDir + "/Unit.prefab";
        private const string NavMeshDataPath = NavDir + "/RTS_Sandbox_NavMesh.asset";

        public const string GroundLayerName = "Ground";
        public const string UnitsLayerName = "Units";
        private const int GroundLayerIndex = 8;
        private const int UnitsLayerIndex = 9;

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

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateLighting();
            var env = new GameObject("Environment");
            CreateGround(env.transform, mats["Ground"]);
            CreateObstacles(env.transform, mats["Obstacle"]);

            var cam = CreateCameraRig();
            var units = CreateUnits(unitPrefab, mats);
            var (selection, commands) = CreateSystems(cam);
            CreateUI(selection);

            var surface = CreateNavMesh();

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new IOException("Failed to save " + ScenePath);

            AddSceneToBuildSettingsFirst(ScenePath);
            AssetDatabase.SaveAssets();

            string summary = $"RTS_Sandbox built: {units.Count} units, {Obstacles.Length} obstacles, " +
                             $"navmesh={(surface.navMeshData != null ? AssetDatabase.GetAssetPath(surface.navMeshData) : "MISSING")}";
            Debug.Log("[Ironfront] " + summary);
            return summary;
        }

        // ---------- Assets ----------

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static void EnsureLayers()
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("layers");
            SetLayer(layers, GroundLayerIndex, GroundLayerName);
            SetLayer(layers, UnitsLayerIndex, UnitsLayerName);
            tagManager.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetLayer(SerializedProperty layers, int index, string name)
        {
            var element = layers.GetArrayElementAtIndex(index);
            if (string.IsNullOrEmpty(element.stringValue)) element.stringValue = name;
            else if (element.stringValue != name)
                throw new System.InvalidOperationException($"Layer {index} is already used by '{element.stringValue}'.");
        }

        private static Dictionary<string, Material> CreateMaterials()
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

        private static Material CreateOrUpdateMaterial(string name, Shader shader, Color color)
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

        private static GameObject CreateUnitPrefab(Material ringMaterial)
        {
            var unit = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            unit.name = "Unit";
            unit.layer = UnitsLayerIndex;

            var agent = unit.AddComponent<NavMeshAgent>();
            agent.radius = 0.5f;
            agent.height = 2f;
            agent.baseOffset = 1f; // capsule pivot is at its centre
            agent.speed = 7f;
            agent.angularSpeed = 720f;
            agent.acceleration = 25f;
            agent.stoppingDistance = 0.1f;
            agent.autoBraking = true;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
            agent.enabled = false; // UnitMover enables it in Start, after the NavMesh is loaded

            unit.AddComponent<UnitMover>();
            var selectable = unit.AddComponent<SelectableUnit>();

            // Facing marker so orientation is readable.
            var nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
            nose.name = "Nose";
            Object.DestroyImmediate(nose.GetComponent<Collider>());
            nose.transform.SetParent(unit.transform, false);
            nose.transform.localPosition = new Vector3(0f, 0.45f, 0.45f);
            nose.transform.localScale = new Vector3(0.35f, 0.25f, 0.3f);
            nose.layer = UnitsLayerIndex;

            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "SelectionIndicator";
            Object.DestroyImmediate(ring.GetComponent<Collider>());
            ring.transform.SetParent(unit.transform, false);
            ring.transform.localPosition = new Vector3(0f, -0.97f, 0f);
            ring.transform.localScale = new Vector3(1.7f, 0.02f, 1.7f);
            ring.layer = UnitsLayerIndex;
            var ringRenderer = ring.GetComponent<MeshRenderer>();
            ringRenderer.sharedMaterial = ringMaterial;
            ringRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ring.SetActive(false);

            var so = new SerializedObject(selectable);
            so.FindProperty("selectionIndicator").objectReferenceValue = ring;
            so.ApplyModifiedPropertiesWithoutUndo();

            var prefab = PrefabUtility.SaveAsPrefabAsset(unit, UnitPrefabPath, out bool ok);
            Object.DestroyImmediate(unit);
            if (!ok || prefab == null) throw new IOException("Failed to save unit prefab.");
            return prefab;
        }

        // ---------- Scene content ----------

        private static void CreateLighting()
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

        private static void CreateGround(Transform parent, Material material)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane); // 10x10 units at scale 1
            ground.name = "Ground";
            ground.transform.SetParent(parent, false);
            ground.transform.localScale = new Vector3(MapSize / 10f, 1f, MapSize / 10f);
            ground.layer = GroundLayerIndex;
            ground.GetComponent<MeshRenderer>().sharedMaterial = material;
            GameObjectUtility.SetStaticEditorFlags(ground, StaticEditorFlags.BatchingStatic);
        }

        private static void CreateObstacles(Transform parent, Material material)
        {
            var root = new GameObject("Obstacles").transform;
            root.SetParent(parent, false);
            foreach (var (name, center, size) in Obstacles)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = name;
                go.transform.SetParent(root, false);
                go.transform.localPosition = center;
                go.transform.localScale = size;
                go.GetComponent<MeshRenderer>().sharedMaterial = material;
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            }
        }

        private static Camera CreateCameraRig()
        {
            var rig = new GameObject("CameraRig");
            rig.transform.position = new Vector3(-20f, 0f, -14f);

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

        private static (SelectionManager, UnitCommandController) CreateSystems(Camera cam)
        {
            var systems = new GameObject("RTS Systems");
            var selection = systems.AddComponent<SelectionManager>();
            var commands = systems.AddComponent<UnitCommandController>();

            var so = new SerializedObject(selection);
            so.FindProperty("viewCamera").objectReferenceValue = cam;
            so.FindProperty("unitLayers").intValue = 1 << UnitsLayerIndex;
            so.ApplyModifiedPropertiesWithoutUndo();

            so = new SerializedObject(commands);
            so.FindProperty("selectionManager").objectReferenceValue = selection;
            so.FindProperty("groundLayers").intValue = 1 << GroundLayerIndex;
            so.ApplyModifiedPropertiesWithoutUndo();

            return (selection, commands);
        }

        private static void CreateUI(SelectionManager selection)
        {
            var esGo = new GameObject("EventSystem");
            esGo.AddComponent<EventSystem>();
            esGo.AddComponent<InputSystemUIInputModule>();

            var canvasGo = new GameObject("HUD Canvas");
            canvasGo.layer = LayerMask.NameToLayer("UI");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            canvasGo.AddComponent<GraphicRaycaster>();

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // HUD panel (raycast target: clicks on it must not reach the selection system).
            var panel = NewUiObject("SelectionPanel", canvasGo.transform);
            var panelRect = (RectTransform)panel.transform;
            panelRect.anchorMin = panelRect.anchorMax = panelRect.pivot = new Vector2(0f, 1f);
            panelRect.anchoredPosition = new Vector2(10f, -10f);
            panelRect.sizeDelta = new Vector2(420f, 70f);
            var panelImage = panel.AddComponent<Image>();
            panelImage.color = new Color(0f, 0f, 0f, 0.55f);
            panelImage.raycastTarget = true;

            var labelGo = NewUiObject("Label", panel.transform);
            var labelRect = (RectTransform)labelGo.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(10f, 6f);
            labelRect.offsetMax = new Vector2(-10f, -6f);
            var label = labelGo.AddComponent<Text>();
            label.font = font;
            label.fontSize = 14;
            label.color = Color.white;
            label.raycastTarget = false;
            label.text = "Selected units: 0";

            var hud = panel.AddComponent<SelectionHud>();
            var hudSo = new SerializedObject(hud);
            hudSo.FindProperty("selectionManager").objectReferenceValue = selection;
            hudSo.FindProperty("label").objectReferenceValue = label;
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

            var selSo = new SerializedObject(selection);
            selSo.FindProperty("selectionBox").objectReferenceValue = boxView;
            selSo.ApplyModifiedPropertiesWithoutUndo();
        }

        private static GameObject NewUiObject(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);
            return go;
        }

        private static NavMeshSurface CreateNavMesh()
        {
            var navGo = new GameObject("Navigation");
            var surface = navGo.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = ~(1 << UnitsLayerIndex) & ~(1 << LayerMask.NameToLayer("UI"));
            surface.agentTypeID = 0; // Humanoid default: radius 0.5, height 2

            surface.BuildNavMesh();
            if (surface.navMeshData == null) throw new System.InvalidOperationException("NavMesh bake produced no data.");

            if (AssetDatabase.LoadAssetAtPath<NavMeshData>(NavMeshDataPath) != null)
                AssetDatabase.DeleteAsset(NavMeshDataPath);
            AssetDatabase.CreateAsset(surface.navMeshData, NavMeshDataPath);
            EditorUtility.SetDirty(surface);
            return surface;
        }

        private static void AddSceneToBuildSettingsFirst(string path)
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != path).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
