using System.IO;
using Ironfront.AI;
using Ironfront.Data;
using Ironfront.Economy;
using Ironfront.Match;
using Ironfront.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Ironfront.EditorTools
{
    /// <summary>
    /// Builds RTS_Match: a complete player-vs-computer match on a point-symmetric map. Both factions start
    /// with an HQ, a refinery, a harvester and 2000 credits; each has a main and a secondary ore field at equal
    /// distances. Reuses the sandbox world/systems/UI construction and all generated content.
    /// Menu: Ironfront > Build Match Scene, or Ironfront > Build All Scenes.
    /// </summary>
    public static class MatchSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/RTS_Match.unity";
        private const string NavMeshDataPath = RtsSandboxBuilder.NavDir + "/RTS_Match_NavMesh.asset";
        private const string AiDir = RtsSandboxBuilder.DataDir + "/AI";
        private const string NormalDifficultyPath = AiDir + "/Normal.asset";

        public const int PlayerFaction = 1;
        public const int EnemyFaction = CombatContentBuilder.EnemyFactionId;

        // Fully point-symmetric around the map centre: every enemy position is the negated player position and
        // enemy buildings are rotated 180° (fronts facing south, towards the player), as are buildings the
        // computer commander places. Docks, exits and ore distances are therefore identical for both sides.
        public static readonly Vector3 PlayerHq = new Vector3(-41f, 0f, -41f);
        public static readonly Vector3 PlayerRefinery = new Vector3(-28f, 0f, -40f);
        public static readonly Vector3 PlayerHarvester = new Vector3(-27f, 0f, -31f);
        public static readonly Vector3 EnemyHq = -PlayerHq;
        public static readonly Vector3 EnemyRefinery = -PlayerRefinery;
        public static readonly Vector3 EnemyHarvester = -PlayerHarvester;

        private static readonly (string name, Vector3 position, int amount)[] OreFields =
        {
            ("Ore_PlayerMain", new Vector3(-12f, 0f, -36f), 6000),
            ("Ore_EnemyMain", new Vector3(12f, 0f, 36f), 6000),
            ("Ore_PlayerSecondary", new Vector3(-8f, 0f, -20f), 4000),
            ("Ore_EnemySecondary", new Vector3(8f, 0f, 20f), 4000),
        };

        /// <summary>The sandbox obstacles made point-symmetric so neither side has a terrain advantage.</summary>
        private static readonly (string name, Vector3 center, Vector3 size)[] Obstacles =
        {
            ("Obstacle_CentralWall", new Vector3(0f, 1.5f, 0f), new Vector3(36f, 3f, 2f)),
            ("Obstacle_BlockWest", new Vector3(-25f, 1.5f, 10f), new Vector3(6f, 3f, 6f)),
            ("Obstacle_BlockEast", new Vector3(25f, 1.5f, -10f), new Vector3(6f, 3f, 6f)),
            ("Obstacle_TowerSouth", new Vector3(10f, 3f, -20f), new Vector3(4f, 6f, 4f)),
            ("Obstacle_TowerNorth", new Vector3(-10f, 3f, 20f), new Vector3(4f, 6f, 4f)),
            ("Obstacle_WallNorthEast", new Vector3(15f, 1.5f, 15f), new Vector3(2f, 3f, 12f)),
            ("Obstacle_WallSouthWest", new Vector3(-15f, 1.5f, -15f), new Vector3(2f, 3f, 12f)),
            ("Obstacle_RockNorth", new Vector3(-8f, 1f, 25f), new Vector3(5f, 2f, 5f)),
            ("Obstacle_RockSouth", new Vector3(8f, 1f, -25f), new Vector3(5f, 2f, 5f)),
        };

        [MenuItem("Ironfront/Build Match Scene")]
        public static void BuildFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build();
        }

        [MenuItem("Ironfront/Build All Scenes")]
        public static void BuildAllFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            BuildAll();
        }

        /// <summary>Sandbox first, then the match (so the match is first in Build Settings).</summary>
        public static string BuildAll()
        {
            string a = RtsSandboxBuilder.Build();
            string b = Build();
            return a + "\n" + b;
        }

        public static string Build()
        {
            RtsSandboxBuilder.EnsureFolder(RtsSandboxBuilder.MaterialsDir);
            RtsSandboxBuilder.EnsureFolder(RtsSandboxBuilder.PrefabsDir);
            RtsSandboxBuilder.EnsureFolder(RtsSandboxBuilder.NavDir);
            RtsSandboxBuilder.EnsureFolder(AiDir);
            RtsSandboxBuilder.EnsureLayers();
            var mats = RtsSandboxBuilder.CreateMaterials();
            RtsSandboxBuilder.CreateUnitPrefab(mats["SelectionRing"]);
            EconomyContentBuilder.CreateAssets(mats["SelectionRing"]);
            CreateNormalDifficulty();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var economy = EconomyContentBuilder.Load(); // reload after NewScene (see EconomyContentBuilder.Load)
            var normal = AssetDatabase.LoadAssetAtPath<AiDifficulty>(NormalDifficultyPath);
            var ground = AssetDatabase.LoadAssetAtPath<Material>(RtsSandboxBuilder.MaterialsDir + "/Ground.mat");
            var obstacle = AssetDatabase.LoadAssetAtPath<Material>(RtsSandboxBuilder.MaterialsDir + "/Obstacle.mat");

            RtsSandboxBuilder.CreateLighting();
            var env = new GameObject("Environment");
            RtsSandboxBuilder.CreateGround(env.transform, ground);
            RtsSandboxBuilder.CreateObstacles(env.transform, obstacle, Obstacles);

            var cam = RtsSandboxBuilder.CreateCameraRig();
            var (selection, _, placement, orders) = RtsSandboxBuilder.CreateSystems(cam, economy);
            var canvas = RtsSandboxBuilder.CreateUI(selection, placement, orders);

            PlaceBase(economy, "PlayerBase", PlayerFaction, PlayerHq, PlayerRefinery, PlayerHarvester);
            PlaceBase(economy, "EnemyBase", EnemyFaction, EnemyHq, EnemyRefinery, EnemyHarvester);

            var resources = new GameObject("Resources").transform;
            foreach (var (name, position, amount) in OreFields)
            {
                var deposit = (GameObject)PrefabUtility.InstantiatePrefab(economy.DepositPrefab, resources);
                deposit.name = name;
                deposit.transform.position = position;
                RtsSandboxBuilder.SetInt(deposit.GetComponent<OreDeposit>(), "initialAmount", amount);
            }

            var commanderGo = new GameObject("EnemyCommander");
            var commander = commanderGo.AddComponent<EnemyCommander>();
            RtsSandboxBuilder.SetInt(commander, "factionId", EnemyFaction);
            RtsSandboxBuilder.SetRef(commander, "difficulty", normal);
            RtsSandboxBuilder.SetRef(commander, "placement", placement);

            CreateMatchFlow(canvas);
            CreateDebugPanel(canvas);

            var surface = RtsSandboxBuilder.CreateNavMesh(NavMeshDataPath);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new IOException("Failed to save " + ScenePath);
            RtsSandboxBuilder.AddSceneToBuildSettingsFirst(ScenePath);
            AssetDatabase.SaveAssets();

            string summary = $"RTS_Match built: 2 bases, {OreFields.Length} ore fields, {Obstacles.Length} obstacles, " +
                             $"AI {normal.DisplayName}, navmesh={(surface.navMeshData != null ? AssetDatabase.GetAssetPath(surface.navMeshData) : "MISSING")}";
            Debug.Log("[Ironfront] " + summary);
            return summary;
        }

        private static void CreateNormalDifficulty()
        {
            var normal = EconomyContentBuilder.LoadOrCreate<AiDifficulty>(NormalDifficultyPath);
            EconomyContentBuilder.Set(normal, so => so.FindProperty("displayName").stringValue = "Normal");
        }

        private static void PlaceBase(EconomyContentBuilder.Content c, string rootName, int faction, Vector3 hq, Vector3 refinery, Vector3 harvester)
        {
            var root = new GameObject(rootName).transform;
            var facing = BuildingDefinition.Facing(faction != PlayerFaction);
            Place(c.Headquarters.Prefab, root, hq, $"Headquarters_F{faction}", faction).transform.rotation = facing;
            Place(c.Refinery.Prefab, root, refinery, $"Refinery_F{faction}", faction).transform.rotation = facing;
            var h = Place(c.Harvester.Prefab, root, harvester, $"Harvester_F{faction}", faction);
            h.transform.rotation = Quaternion.Euler(0f, faction == PlayerFaction ? 90f : -90f, 0f);

            var start = new GameObject($"StartLocation_F{faction}");
            start.transform.SetParent(root, false);
            start.transform.position = hq;
            RtsSandboxBuilder.SetInt(start.AddComponent<FactionStartLocation>(), "factionId", faction);
        }

        private static GameObject Place(GameObject prefab, Transform parent, Vector3 position, string name, int faction)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.name = name;
            go.transform.position = position;
            RtsSandboxBuilder.SetInt(go.GetComponent<FactionMember>(), "factionId", faction);
            return go;
        }

        // ---------- UI ----------

        private static void CreateMatchFlow(GameObject canvas)
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var flowGo = new GameObject("MatchFlow");
            var flow = flowGo.AddComponent<MatchFlowController>();

            var panel = RtsSandboxBuilder.NewUiObject("MatchResultPanel", canvas.transform);
            var rect = (RectTransform)panel.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var bg = panel.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.75f);
            bg.raycastTarget = true; // blocks clicks on the world while shown

            var title = Label(panel.transform, "Title", font, 72, FontStyle.Bold, new Vector2(0f, 80f), new Vector2(900f, 110f));
            var detail = Label(panel.transform, "Detail", font, 22, FontStyle.Normal, new Vector2(0f, -10f), new Vector2(900f, 70f));

            var buttonGo = RtsSandboxBuilder.NewUiObject("RestartButton", panel.transform);
            var bRect = (RectTransform)buttonGo.transform;
            bRect.anchorMin = bRect.anchorMax = bRect.pivot = new Vector2(0.5f, 0.5f);
            bRect.anchoredPosition = new Vector2(0f, -100f);
            bRect.sizeDelta = new Vector2(240f, 54f);
            buttonGo.AddComponent<Image>().color = new Color(0.2f, 0.45f, 0.95f, 1f);
            var button = buttonGo.AddComponent<Button>();
            var bLabel = Label(buttonGo.transform, "Label", font, 24, FontStyle.Bold, Vector2.zero, new Vector2(240f, 54f));
            bLabel.text = "Restart match";

            var view = canvas.AddComponent<MatchResultView>(); // on the canvas: the panel itself starts hidden
            RtsSandboxBuilder.SetRef(view, "panel", panel);
            RtsSandboxBuilder.SetRef(view, "title", title);
            RtsSandboxBuilder.SetRef(view, "detail", detail);
            RtsSandboxBuilder.SetRef(view, "restartButton", button);
            RtsSandboxBuilder.SetRef(view, "flow", flow);
            RtsSandboxBuilder.SetRef(flow, "resultView", view);
            panel.transform.SetAsLastSibling();
            panel.SetActive(false);
        }

        private static void CreateDebugPanel(GameObject canvas)
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var panel = RtsSandboxBuilder.NewUiObject("AiDebugPanel", canvas.transform);
            var rect = (RectTransform)panel.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(10f, -130f);
            rect.sizeDelta = new Vector2(470f, 470f);
            var bg = panel.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0.05f, 0.7f);
            bg.raycastTarget = true;
            var text = Label(panel.transform, "Text", font, 12, FontStyle.Normal, Vector2.zero, Vector2.zero);
            var tRect = (RectTransform)text.transform;
            tRect.anchorMin = Vector2.zero;
            tRect.anchorMax = Vector2.one;
            tRect.offsetMin = new Vector2(8f, 6f);
            tRect.offsetMax = new Vector2(-8f, -6f);
            text.alignment = TextAnchor.UpperLeft;
            text.color = new Color(0.75f, 0.9f, 1f);

            var debug = canvas.AddComponent<CommanderDebugPanel>();
            RtsSandboxBuilder.SetRef(debug, "panel", panel);
            RtsSandboxBuilder.SetRef(debug, "text", text);
            panel.SetActive(false);
        }

        private static Text Label(Transform parent, string name, Font font, int size, FontStyle style, Vector2 position, Vector2 sizeDelta)
        {
            var go = RtsSandboxBuilder.NewUiObject(name, parent);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = position;
            r.sizeDelta = sizeDelta;
            var t = go.AddComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.fontStyle = style;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = Color.white;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }
    }
}
