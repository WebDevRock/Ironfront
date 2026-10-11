using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ironfront.Buildings;
using Ironfront.CameraControl;
using Ironfront.Combat;
using Ironfront.Data;
using Ironfront.Match;
using Ironfront.Selection;
using Ironfront.Units;
using UnityEngine;

namespace Ironfront.Diagnostics
{
    /// <summary>
    /// Captures a fixed set of representative screenshots (developed base, tanks, harvesting, large battle,
    /// interface) so visual changes can be compared before/after with identical framing. Started in a player
    /// with <c>-ironfrontShowcase -showcaseOut &lt;dir&gt;</c>; shows the main menu (if built), loads RTS_Match, disables the computer commander
    /// so the layout is reproducible, stages each shot, writes PNGs and quits.
    /// </summary>
    public class ShowcaseRunner : MonoBehaviour
    {
        public const string CommandLineSwitch = "-ironfrontShowcase";
        public const string OutputSwitch = "-showcaseOut";

        private RtsCameraController _camera;
        private Canvas _hud;
        private string _outDir;
        private readonly List<GameObject> _staged = new List<GameObject>();

        public bool IsFinished { get; private set; }
        public List<string> Written { get; } = new List<string>();

        private IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs();
            int idx = Array.IndexOf(args, OutputSwitch);
            _outDir = idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : Path.Combine(Application.persistentDataPath, "showcase");
            Directory.CreateDirectory(_outDir);
            DontDestroyOnLoad(gameObject);
            Application.runInBackground = true; // keep capturing if the window loses focus

            // Title screen first (Milestone 006), when the build has one.
            if (Application.CanStreamedLevelBeLoaded(UI.GameMenu.MainMenuScene))
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene(UI.GameMenu.MainMenuScene);
                yield return new WaitForSecondsRealtime(3f);
                yield return Shot("00_main_menu", Vector3.zero, 0f, hud: true);
            }
            UnityEngine.SceneManagement.SceneManager.LoadScene(ShowcaseBootstrap.SceneName);
            yield return null;
            yield return null;
            _camera = FindAnyObjectByType<RtsCameraController>();
            _hud = FindObjectsByType<Canvas>().FirstOrDefault(c => c.name == "HUD Canvas");
            foreach (var ai in FindObjectsByType<AI.EnemyCommander>()) ai.enabled = false;
            var setup = MatchController.Current.Setup;
            var factory = setup.ConstructionOptions.First(d => d.Id == "vehicle_factory");
            var power = setup.ConstructionOptions.First(d => d.Id == "power_plant");
            var refinery = setup.ConstructionOptions.First(d => d.Id == "refinery");
            var light = factory.ProducibleUnits.First(u => u.Id == "light_tank");
            var heavy = factory.ProducibleUnits.First(u => u.Id == "heavy_tank");
            var harvester = factory.ProducibleUnits.First(u => u.Id == "harvester");
            // Milestone 007 content (optional so the runner also works on older builds).
            var recon = factory.ProducibleUnits.FirstOrDefault(u => u.Id == "recon");
            var artillery = factory.ProducibleUnits.FirstOrDefault(u => u.Id == "rocket_artillery");
            var lab = setup.ConstructionOptions.FirstOrDefault(d => d.Id == "research_centre");
            var tower = setup.ConstructionOptions.FirstOrDefault(d => d.Id == "guard_tower");
            var atTurret = setup.ConstructionOptions.FirstOrDefault(d => d.Id == "at_turret");

            // Player base: add power, factory and an extra harvester; enemy base gets the same.
            var north = BuildingDefinition.Facing(false);
            var south = BuildingDefinition.Facing(true);
            Stage(power.Prefab, new Vector3(-43f, 0f, -29f), north, 1);
            Stage(power.Prefab, new Vector3(-43f, 0f, -22f), north, 1);
            Stage(factory.Prefab, new Vector3(-29f, 0f, -25f), north, 1);
            Stage(harvester.Prefab, new Vector3(-20f, 0f, -33f), Quaternion.Euler(0f, 90f, 0f), 1);
            if (lab != null) Stage(lab.Prefab, new Vector3(-43f, 0f, -14f), north, 1);
            if (tower != null) Stage(tower.Prefab, new Vector3(-21f, 0f, -24f), north, 1);
            if (atTurret != null) Stage(atTurret.Prefab, new Vector3(-17f, 0f, -29f), north, 1);
            Stage(power.Prefab, new Vector3(43f, 0f, 29f), south, 2);
            Stage(factory.Prefab, new Vector3(29f, 0f, 25f), south, 2);
            for (int i = 0; i < 6; i++)
            {
                var def = i % 3 == 2 ? heavy : light;
                Stage(def.Prefab, new Vector3(-37f + (i % 3) * 3.2f, 0f, -31f + (i / 3) * 3.4f), Quaternion.Euler(0f, 20f, 0f), 1);
            }

            // Let harvesters reach the ore at accelerated time.
            Time.timeScale = 4f;
            yield return new WaitForSecondsRealtime(4f);
            Time.timeScale = 1f;

            yield return Shot("03_harvesting", new Vector3(-20f, 0f, -38f), 34f, hud: false);
            yield return Shot("01_player_base", new Vector3(-34f, 0f, -34f), 46f, hud: false);

            // Tanks: both models in both faction colours, close up.
            var tanks = new Vector3(30f, 0f, -38f);
            var face = Quaternion.Euler(0f, 200f, 0f);
            var models = new[]
            {
                Stage(light.Prefab, tanks + new Vector3(-5f, 0f, 1.5f), face, 1),
                Stage(heavy.Prefab, tanks + new Vector3(-1.5f, 0f, 1.5f), face, 1),
                Stage(light.Prefab, tanks + new Vector3(2f, 0f, -1.5f), face, 2),
                Stage(heavy.Prefab, tanks + new Vector3(5.5f, 0f, -1.5f), face, 2),
            };
            var extra = new List<GameObject>();
            if (recon != null) extra.Add(Stage(recon.Prefab, tanks + new Vector3(-8.5f, 0f, 1.5f), face, 1));
            if (artillery != null) extra.Add(Stage(artillery.Prefab, tanks + new Vector3(9f, 0f, -1.5f), face, 2));
            foreach (var m in models.Concat(extra)) m.GetComponent<CombatUnit>().enabled = false; // display only: no shooting
            yield return new WaitForSeconds(0.5f);
            yield return Shot("02_tanks", tanks, extra.Count > 0 ? 17f : 12f, hud: false);

            // Milestone 007: defences and research centre in the base, fog overview, research interface.
            if (lab != null)
            {
                yield return Shot("08_defences_research", new Vector3(-30f, 0f, -22f), 34f, hud: false);
                yield return Shot("09_fog_overview", new Vector3(0f, 0f, 0f), 60f, hud: true);
                var sel = FindAnyObjectByType<SelectionManager>();
                var labBuilding = Building.All.FirstOrDefault(b => b.FactionId == 1 && b.Definition == lab);
                var research = setup.Research.FirstOrDefault();
                if (research != null) Buildings.ResearchCentre.TryStart(1, research);
                yield return new WaitForSeconds(2f);
                if (sel != null && labBuilding != null) sel.SelectStructure(labBuilding);
                yield return Shot("10_research_interface", new Vector3(-34f, 0f, -24f), 40f, hud: true);
                sel?.SelectStructure(null);
            }

            // Interface: a selected factory with a queue, plus selected tanks.
            var selection = FindAnyObjectByType<SelectionManager>();
            var playerFactory = VehicleFactory.FirstOwnedBy(1);
            if (playerFactory != null)
            {
                playerFactory.Enqueue(heavy);
                playerFactory.Enqueue(light);
            }
            yield return new WaitForSeconds(1.5f);
            if (selection != null && playerFactory != null) selection.SelectStructure(playerFactory.Building);
            yield return Shot("05_interface_structure", new Vector3(-34f, 0f, -32f), 40f, hud: true);
            if (selection != null)
                selection.Selection.Replace(_staged.Where(g => g != null).Select(g => g.GetComponent<SelectableUnit>())
                    .Where(u => u != null && u.GetComponent<CombatUnit>() != null && FactionMember.Of(u) == 1).Take(6));
            yield return Shot("05_interface_units", new Vector3(-34f, 0f, -32f), 40f, hud: true);
            selection?.Selection.Clear();

            // Large battle: 30 v 30 in the open eastern half.
            var armies = new List<CombatUnit>();
            for (int side = 0; side < 2; side++)
            {
                int faction = side + 1;
                for (int i = 0; i < 30; i++)
                {
                    int row = i / 10, col = i % 10;
                    var pos = new Vector3(29f + col * 2.2f, 0f, side == 0 ? 0f - row * 2.8f : 22f + row * 2.8f);
                    var def = i % 3 == 0 ? heavy : light;
                    var go = Stage(def.Prefab, pos, Quaternion.LookRotation(side == 0 ? Vector3.forward : Vector3.back), faction);
                    armies.Add(go.GetComponent<CombatUnit>());
                }
            }
            yield return new WaitForSeconds(0.3f);
            foreach (var c in armies)
            {
                int faction = FactionMember.Of(c);
                c.OrderAttackMove(new Vector3(c.transform.position.x, 0f, faction == 1 ? 30f : -8f));
            }
            yield return new WaitForSeconds(3.2f);
            yield return Shot("04_battle", new Vector3(39f, 0f, 9f), 46f, hud: false);
            yield return new WaitForSeconds(2.0f);
            yield return Shot("04_battle_late", new Vector3(39f, 0f, 9f), 46f, hud: true);

            // Pause menu and the victory screen.
            var menu = FindAnyObjectByType<UI.GameMenu>();
            if (menu != null)
            {
                menu.Open();
                yield return Shot("06_pause_menu", new Vector3(39f, 0f, 9f), 46f, hud: true);
                menu.Close();
            }
            var enemyHq = Building.All.FirstOrDefault(b => b.FactionId == 2 && b.Definition != null && b.Definition.Id == "headquarters");
            if (enemyHq != null)
            {
                var h = enemyHq.GetComponent<Health>();
                Vector3 where = enemyHq.transform.position; // the building is destroyed by the damage below
                h.ApplyDamage(h.Max * 10f, 1);
                yield return new WaitForSecondsRealtime(2.5f);
                yield return Shot("07_victory", where, 40f, hud: true);
            }

            IsFinished = true;
            Debug.Log("[Showcase] Wrote " + Written.Count + " screenshots to " + _outDir);
            if (!Application.isEditor) Application.Quit();
        }

        private GameObject Stage(GameObject prefab, Vector3 position, Quaternion rotation, int faction)
        {
            var go = FactionSpawner.Spawn(prefab, position, rotation, faction);
            _staged.Add(go);
            return go;
        }

        private IEnumerator Shot(string name, Vector3 focus, float distance, bool hud)
        {
            if (_camera != null && distance > 0f)
            {
                _camera.Focus(focus);
                _camera.SetDistance(distance, immediate: true);
            }
            if (_hud != null) _hud.enabled = hud;
            yield return null;
            yield return null;
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            string path = Path.Combine(_outDir, name + ".png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(tex);
            Written.Add(path);
            if (_hud != null) _hud.enabled = true;
        }
    }

    /// <summary>Adds a <see cref="ShowcaseRunner"/> when the player is started with its switch.</summary>
    public static class ShowcaseBootstrap
    {
        public const string SceneName = "RTS_Match";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void MaybeStart()
        {
            if (Application.isEditor) return;
            if (!Environment.GetCommandLineArgs().Contains(ShowcaseRunner.CommandLineSwitch)) return;
            if (UnityEngine.Object.FindAnyObjectByType<ShowcaseRunner>() != null) return;
            new GameObject("ShowcaseRunner").AddComponent<ShowcaseRunner>(); // persistent; drives the scene loads itself
        }
    }
}
