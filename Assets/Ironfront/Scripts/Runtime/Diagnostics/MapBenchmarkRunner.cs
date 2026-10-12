using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using Ironfront.CameraControl;
using Ironfront.Combat;
using Ironfront.Core.Skirmish;
using Ironfront.Match;
using Ironfront.Skirmish;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Ironfront.Diagnostics
{
    /// <summary>
    /// Milestone 008 performance check on the skirmish battlefields. A player started with <c>-ironfrontMapBenchmark</c>
    /// (optionally <c>-benchmarkOut &lt;file&gt;</c>) loads every map in turn with a fixed spawn seed and measures, with
    /// fog of war and the computer commander running: "opening" (camera over the player's base), "pan" (the camera
    /// sweeping across the whole map) and "battle" (50 vs 50 tanks fighting in the open middle of the map). Uses the
    /// same frame-time / GC / marker measurement as <see cref="BenchmarkRunner"/>; writes one JSON array and quits.
    /// With <c>-benchmarkShots &lt;dir&gt;</c> it also saves a screenshot of each phase (opening, mid-pan, battle).
    /// </summary>
    public class MapBenchmarkRunner : MonoBehaviour
    {
        public const string CommandLineSwitch = "-ironfrontMapBenchmark";

        private static readonly (string id, string scene)[] Maps =
        {
            ("dustfall_plains", "Skirmish_DustfallPlains"), ("iron_pass", "Skirmish_IronPass"),
            ("green_divide", "Skirmish_GreenDivide"), ("blackstone_basin", "Skirmish_BlackstoneBasin"),
        };

        private BenchmarkRunner _measure;
        private string _shots;

        private IEnumerator Shot(string name)
        {
            if (string.IsNullOrEmpty(_shots)) yield break;
            yield return new WaitForEndOfFrame();
            Directory.CreateDirectory(_shots);
            ScreenCapture.CaptureScreenshot(Path.Combine(_shots, name + ".png"));
            yield return null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void MaybeStart()
        {
            if (Application.isEditor || !Environment.GetCommandLineArgs().Contains(CommandLineSwitch)) return;
            if (FindAnyObjectByType<MapBenchmarkRunner>() != null) return;
            var go = new GameObject("MapBenchmark");
            DontDestroyOnLoad(go);
            go.AddComponent<MapBenchmarkRunner>();
        }

        private void Start()
        {
            var holder = new GameObject("MapBenchmarkMeasure");
            holder.SetActive(false);
            holder.transform.SetParent(transform, false);
            _measure = holder.AddComponent<BenchmarkRunner>();
            _measure.DisableAutoRun();
            holder.SetActive(true);
            StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            Application.runInBackground = true;
            var args = Environment.GetCommandLineArgs();
            int si = Array.IndexOf(args, "-benchmarkShots");
            if (si >= 0 && si + 1 < args.Length) _shots = args[si + 1];
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            foreach (var (id, scene) in Maps)
            {
                SkirmishSession.Select(new SkirmishSettings { MapId = id, Seed = 1 });
                SceneManager.LoadScene(scene);
                yield return null;
                yield return null;
                yield return Wait(3f);
                var init = SkirmishInitializer.Current;
                var rig = FindAnyObjectByType<RtsCameraController>();
                float size = init != null && init.Map != null ? init.Map.Size : 100f;

                yield return _measure.Measure(Mathf.RoundToInt(size), id + ":opening", 12f);
                yield return Shot($"{id}_1_base");

                StartCoroutine(Pan(rig, size, 12f));
                StartCoroutine(ShotAfter(6f, $"{id}_2_terrain"));
                yield return _measure.Measure(Mathf.RoundToInt(size), id + ":pan", 12f);

                var centre = BattleSite(size);
                SpawnBattle(centre);
                if (rig != null) rig.Focus(centre);
                yield return Wait(1f);
                StartCoroutine(ShotAfter(5f, $"{id}_3_battle"));
                yield return _measure.Measure(Mathf.RoundToInt(size), id + ":battle", 12f);
            }
            _measure.WriteResults();
            Application.Quit();
        }

        private IEnumerator ShotAfter(float seconds, string name)
        {
            yield return Wait(seconds);
            yield return Shot(name);
        }

        private static IEnumerator Wait(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) yield return null;
        }

        private static IEnumerator Pan(RtsCameraController rig, float size, float seconds)
        {
            if (rig == null) yield break;
            float start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < seconds)
            {
                float t = (Time.realtimeSinceStartup - start) / seconds * Mathf.PI * 2f;
                rig.Focus(new Vector3(Mathf.Cos(t), 0f, Mathf.Sin(t * 2f)) * size * 0.38f);
                yield return null;
            }
        }

        /// <summary>The walkable point nearest the map centre that has open ground around it.</summary>
        private static Vector3 BattleSite(float size)
        {
            for (float r = 20f; r < size * 0.4f; r += 10f)
                for (int a = 0; a < 8; a++)
                {
                    var p = Quaternion.Euler(0f, a * 45f + 22.5f, 0f) * Vector3.forward * r;
                    if (NavMesh.SamplePosition(p, out var hit, 2f, NavMesh.AllAreas) && !Physics.CheckBox(p + Vector3.up, new Vector3(14f, 0.5f, 22f), Quaternion.identity, 1))
                        return hit.position;
                }
            return Vector3.zero;
        }

        private static void SpawnBattle(Vector3 centre)
        {
            var factory = MatchController.Current.Setup.ConstructionOptions.First(d => d != null && d.Id == "vehicle_factory");
            var light = factory.ProducibleUnits.First(u => u.Id == "light_tank");
            var heavy = factory.ProducibleUnits.First(u => u.Id == "heavy_tank");
            for (int side = 0; side < 2; side++)
            {
                int faction = side + 1;
                float z0 = side == 0 ? -16f : 16f;
                for (int i = 0; i < 50; i++)
                {
                    var p = centre + new Vector3((i % 10 - 4.5f) * 2.8f, 0f, z0 + (side == 0 ? -1 : 1) * (i / 10) * 2.8f);
                    if (NavMesh.SamplePosition(p, out var hit, 3f, NavMesh.AllAreas)) p = hit.position;
                    var go = FactionSpawner.Spawn((i % 2 == 0 ? light : heavy).Prefab, p, Quaternion.LookRotation(side == 0 ? Vector3.forward : Vector3.back), faction);
                    go.GetComponent<CombatUnit>().OrderAttackMove(centre + new Vector3(0f, 0f, side == 0 ? 20f : -20f));
                }
            }
        }
    }
}
