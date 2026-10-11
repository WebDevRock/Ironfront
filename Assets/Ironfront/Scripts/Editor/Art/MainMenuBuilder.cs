using System.IO;
using Ironfront.Audio;
using Ironfront.UI;
using Ironfront.Visuals;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Ironfront.EditorTools.Art
{
    /// <summary>
    /// Builds the MainMenu scene (first in Build Settings): a lit diorama made from the game's own procedural
    /// models (decoration only, no gameplay components), a slowly drifting camera, the Ironfront emblem and title,
    /// and Start / Options / Quit. Menu: Ironfront > Build Main Menu.
    /// </summary>
    public static class MainMenuBuilder
    {
        public const string ScenePath = "Assets/Scenes/MainMenu.unity";
        private static readonly Color Blue = new Color(0.2f, 0.45f, 0.95f);
        private static readonly Color Red = new Color(0.85f, 0.15f, 0.12f);

        [MenuItem("Ironfront/Build Main Menu")]
        public static void BuildFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Debug.Log("[Ironfront] " + Build());
        }

        public static string Build()
        {
            var a = EconomyContentBuilder.Art;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RtsSandboxBuilder.CreateLighting();

            // Diorama ground and backdrop (visual only; no colliders are needed in the menu).
            var env = new GameObject("Diorama").transform;
            var layout = new EnvironmentBuilder.Layout { Name = "Menu", MapSize = 60f, Seed = 11 };
            layout.Bases.Add(new Vector3(-8f, 0f, 6f));
            layout.Ore.Add(new Vector3(12f, 0f, -4f));
            layout.Roads.Add(new[] { new Vector3(-8f, 0f, 0f), new Vector3(0f, 0f, -3f), new Vector3(12f, 0f, -4f) });
            layout.Roads.Add(new[] { new Vector3(0f, 0f, -3f), new Vector3(-4f, 0f, -20f) });
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.SetParent(env, false);
            ground.transform.localScale = new Vector3(6f, 1f, 6f);
            Object.DestroyImmediate(ground.GetComponent<Collider>());
            EnvironmentBuilder.StyleGround(ground, EnvironmentBuilder.GroundMaterial(layout, a));
            EnvironmentBuilder.Backdrop(env, layout, a);
            EnvironmentBuilder.Props(env, layout, a);

            Place(env, "HQ", new Vector3(-8f, 0f, 6f), 180f, t => StructureModels.Headquarters(t, a), Blue, a);
            Place(env, "Factory", new Vector3(3f, 0f, 9f), 180f, t => StructureModels.VehicleFactory(t, a), Blue, a);
            Place(env, "Refinery", new Vector3(-17f, 0f, -2f), 90f, t => StructureModels.Refinery(t, a), Blue, a);
            Place(env, "Power", new Vector3(-17f, 0f, 9f), 180f, t => StructureModels.PowerPlant(t, a), Blue, a);
            Place(env, "Heavy1", new Vector3(1.5f, 0f, -0.5f), 160f, t => VehicleModels.HeavyTank(t, a), Blue, a);
            Place(env, "Light1", new Vector3(5f, 0f, 1.2f), 150f, t => VehicleModels.LightTank(t, a), Blue, a);
            Place(env, "Light2", new Vector3(-1.8f, 0f, 1.6f), 170f, t => VehicleModels.LightTank(t, a), Blue, a);
            Place(env, "Harvester", new Vector3(9.5f, 0f, -1.5f), 100f, t => VehicleModels.Harvester(t, a), Blue, a);
            Place(env, "EnemyHeavy", new Vector3(8f, 0f, -16f), -20f, t => VehicleModels.HeavyTank(t, a), Red, a);
            Place(env, "EnemyLight", new Vector3(3f, 0f, -18f), -10f, t => VehicleModels.LightTank(t, a), Red, a);
            var ore = new MeshKit();
            var rng = new System.Random(5);
            for (int i = 0; i < 12; i++)
            {
                float ang = i * 30f, r = 0.4f + (float)rng.NextDouble() * 1.6f;
                var pos = new Vector3(12f, 0f, -4f) + Quaternion.Euler(0f, ang, 0f) * Vector3.forward * r;
                float h = 0.7f + (float)rng.NextDouble() * 1.2f;
                VehicleModels.Crystal(ore, a.OreCrystal, pos, 0.18f + h * 0.08f, h, Quaternion.Euler((float)rng.NextDouble() * 24f - 12f, ang, (float)rng.NextDouble() * 24f - 12f));
            }
            ArtLibrary.Attach(env, "OreField", "Menu_Ore", ore);

            // Camera on a slowly rotating pivot.
            var pivot = new GameObject("CameraPivot").transform;
            pivot.position = new Vector3(-2f, 0f, 2f);
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(pivot, false);
            camGo.transform.localPosition = new Vector3(0f, 17f, -30f);
            camGo.transform.localRotation = Quaternion.Euler(28f, 0f, 0f);
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 40f;
            cam.farClipPlane = 400f;
            camGo.AddComponent<AudioListener>();
            EnvironmentBuilder.Lighting(cam);

            // UI.
            UiTheme.Skin = UiArtBuilder.Load();
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<InputSystemUIInputModule>();
            var canvasGo = new GameObject("Menu Canvas");
            canvasGo.layer = LayerMask.NameToLayer("UI");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600f, 900f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            RtsSandboxBuilder.SetRef(canvasGo.AddComponent<UiSkinProvider>(), "skin", UiTheme.Skin);

            // Left-hand title column over a soft gradient strip.
            var strip = RtsSandboxBuilder.NewUiObject("TitleStrip", canvasGo.transform);
            var sr = (RectTransform)strip.transform;
            sr.anchorMin = new Vector2(0f, 0f);
            sr.anchorMax = new Vector2(0f, 1f);
            sr.pivot = new Vector2(0f, 0.5f);
            sr.sizeDelta = new Vector2(560f, 0f);
            var stripImg = strip.AddComponent<Image>();
            stripImg.color = new Color(0.03f, 0.035f, 0.04f, 0.72f);
            stripImg.raycastTarget = false;

            var main = UiBuild.Rect("MainPanel", strip.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(500f, 760f));
            var emblem = UiBuild.Rect("Emblem", main, new Vector2(0.5f, 0.5f), new Vector2(0f, 250f), new Vector2(130f, 130f));
            var emblemImg = emblem.gameObject.AddComponent<Image>();
            emblemImg.sprite = UiTheme.Skin != null ? UiTheme.Skin.Emblem : null;
            emblemImg.preserveAspect = true;
            emblemImg.raycastTarget = false;
            UiBuild.Text("Title", main, "IRONFRONT", 64, UiTheme.Text, TextAnchor.MiddleCenter, new Vector2(0f, 140f), new Vector2(500f, 80f), FontStyle.Bold);
            UiBuild.Text("Tagline", main, "Build. Harvest. Hold the line.", 20, UiTheme.Accent, TextAnchor.MiddleCenter, new Vector2(0f, 88f), new Vector2(500f, 30f));
            var start = UiBuild.Button("StartButton", main, "Start Match", new Vector2(0f, -10f), new Vector2(300f, 56f), new Color(0.24f, 0.4f, 0.22f, 1f), 22);
            var options = UiBuild.Button("OptionsButton", main, "Options", new Vector2(0f, -80f), new Vector2(300f, 50f), null, 20);
            var quit = UiBuild.Button("QuitButton", main, "Quit", new Vector2(0f, -145f), new Vector2(300f, 50f), null, 20);
            UiBuild.Text("Hint", main, "Skirmish vs. computer (Normal)   ·   F1 in game for controls", 13, UiTheme.Muted, TextAnchor.MiddleCenter,
                new Vector2(0f, -320f), new Vector2(500f, 24f));
            var (optionsPanel, back) = UiBuild.AudioOptions(canvasGo.transform, new Vector2(0f, 0f));

            var menu = canvasGo.AddComponent<MainMenuController>();
            RtsSandboxBuilder.SetRef(menu, "startButton", start);
            RtsSandboxBuilder.SetRef(menu, "optionsButton", options);
            RtsSandboxBuilder.SetRef(menu, "quitButton", quit);
            RtsSandboxBuilder.SetRef(menu, "optionsBackButton", back);
            RtsSandboxBuilder.SetRef(menu, "mainPanel", main.gameObject);
            RtsSandboxBuilder.SetRef(menu, "optionsPanel", optionsPanel);
            RtsSandboxBuilder.SetRef(menu, "cameraPivot", pivot);

            var audioGo = new GameObject("Audio");
            RtsSandboxBuilder.SetRef(audioGo.AddComponent<GameAudio>(), "bank", AudioContentBuilder.LoadBank());

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("Failed to save " + ScenePath);
            RtsSandboxBuilder.AddSceneToBuildSettingsFirst(ScenePath);
            AssetDatabase.SaveAssets();
            return "MainMenu built (first scene in Build Settings)";
        }

        private static void Place(Transform parent, string name, Vector3 pos, float yaw, System.Action<Transform> build, Color faction, ArtLibrary a)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            build(go.transform);
            var tint = go.AddComponent<StaticTint>();
            var so = new SerializedObject(tint);
            so.FindProperty("color").colorValue = faction;
            so.ApplyModifiedPropertiesWithoutUndo();
            var block = new MaterialPropertyBlock();
            var list = new System.Collections.Generic.List<Renderer>();
            var glow = new System.Collections.Generic.List<Renderer>();
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (r.sharedMaterial == a.Faction) list.Add(r);
                else if (r.sharedMaterial == a.FactionGlow) glow.Add(r);
            }
            EconomyContentBuilder.SetList(so.FindProperty("renderers"), list.ToArray());
            EconomyContentBuilder.SetList(so.FindProperty("emissiveRenderers"), glow.ToArray());
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
