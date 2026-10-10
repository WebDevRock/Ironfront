using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Ironfront.Combat;
using Ironfront.Data;
using Ironfront.Match;
using Ironfront.Selection;
using Ironfront.Units;
using Unity.Profiling;
using UnityEngine;

namespace Ironfront.Diagnostics
{
    /// <summary>
    /// Repeatable performance benchmark. For each army size it spawns two equal armies (half Light, half Heavy
    /// tanks per side, player army selected) and measures an idle phase (units in place, scanning for targets)
    /// and a combat phase (both armies attack-move into each other).
    ///
    /// Collected per phase: frame time average/p50/p95/p99/max, GC allocation per frame, GC collections,
    /// managed/total memory, path requests per second, average units/projectiles alive and per-marker
    /// milliseconds (<see cref="PerfMarkers"/>; Editor and development builds only).
    ///
    /// Runs automatically in the RTS_Benchmark scene. A player started with <c>-ironfrontBenchmark</c>
    /// (optionally <c>-benchmarkOut &lt;file&gt;</c>) loads that scene, writes the results and quits.
    /// </summary>
    public class BenchmarkRunner : MonoBehaviour
    {
        public const string CommandLineSwitch = "-ironfrontBenchmark";
        public const string OutputSwitch = "-benchmarkOut";

        [SerializeField] private int[] armySizes = { 25, 50, 100, 200 };
        [SerializeField] private float warmupSeconds = 2f;
        [SerializeField] private float idleSeconds = 4f;
        [SerializeField] private float combatSeconds = 8f;
        [SerializeField] private float spacing = 2.8f;
        [SerializeField] private float armyDistance = 48f;
        [SerializeField] private bool runOnStart = true;

        public bool IsRunning { get; private set; }
        public bool IsFinished { get; private set; }
        public List<PhaseResult> Results { get; } = new List<PhaseResult>();
        public string OutputPath { get; private set; }

        [Serializable]
        public class PhaseResult
        {
            public string build;
            public int armySize;
            public string phase;
            public int frames;
            public float seconds;
            public float avgMs, p50Ms, p95Ms, p99Ms, maxMs, fps;
            public float gcKbPerFrame;
            public int gcCollections;
            public float managedMb, totalMb;
            public float pathRequestsPerSecond;
            public float avgUnitsAlive, avgProjectiles;
            public List<string> markerMs = new List<string>();
            public List<string> engine = new List<string>();
        }

        /// <summary>Built-in engine counters: (category, name, isCount). Times are reported in ms per frame.</summary>
        private static readonly (string category, string name, bool isCount)[] EngineCounters =
        {
            ("Internal", "Main Thread", false),
            ("Render", "CPU Render Thread Frame Time", false),
            ("PlayerLoop", "BehaviourUpdate", false),
            ("PlayerLoop", "LateBehaviourUpdate", false),
            ("Ai", "NavMeshManager", false),
            ("PlayerLoop", "UIEvents.WillRenderCanvases", false),
            ("Render", "Draw Calls Count", true),
            ("Render", "SetPass Calls Count", true),
            ("Render", "Batches Count", true),
        };

        private void Start()
        {
            if (runOnStart) StartCoroutine(Run());
        }

        public IEnumerator Run()
        {
            if (IsRunning) yield break;
            IsRunning = true;
            int vsync = QualitySettings.vSyncCount;
            int previousFrameRate = Application.targetFrameRate;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            Time.timeScale = 1f;

            var setup = MatchController.Current != null ? MatchController.Current.Setup : null;
            var factory = setup != null ? setup.ConstructionOptions.FirstOrDefault(d => d != null && d.Id == "vehicle_factory") : null;
            var light = factory != null ? factory.ProducibleUnits.FirstOrDefault(u => u.Id == "light_tank") : null;
            var heavy = factory != null ? factory.ProducibleUnits.FirstOrDefault(u => u.Id == "heavy_tank") : null;
            if (light == null || heavy == null)
            {
                Debug.LogError("[Benchmark] Light/Heavy Tank definitions not found.");
                IsRunning = false;
                yield break;
            }

            foreach (int size in armySizes)
            {
                var units = SpawnArmies(size, light, heavy);
                yield return WaitSeconds(warmupSeconds);
                yield return Measure(size, "idle", idleSeconds);

                foreach (var u in units.Where(u => u != null))
                {
                    var c = u.GetComponent<CombatUnit>();
                    int faction = FactionMember.Of(c);
                    var target = new Vector3(u.transform.position.x, 0f, faction == 1 ? armyDistance * 0.5f : -armyDistance * 0.5f);
                    c.OrderAttackMove(target);
                }
                yield return Measure(size, "combat", combatSeconds);

                foreach (var u in units) if (u != null) Destroy(u);
                foreach (var p in FindObjectsByType<Projectile>()) Destroy(p.gameObject);
                yield return null;
                yield return null;
                GC.Collect();
                yield return WaitSeconds(0.5f);
            }

            QualitySettings.vSyncCount = vsync;
            Application.targetFrameRate = previousFrameRate;
            WriteResults();
            IsRunning = false;
            IsFinished = true;

            if (!Application.isEditor && Environment.GetCommandLineArgs().Contains(CommandLineSwitch)) Application.Quit();
        }

        private List<GameObject> SpawnArmies(int size, UnitDefinition light, UnitDefinition heavy)
        {
            var units = new List<GameObject>();
            int perSide = size / 2;
            int columns = Mathf.CeilToInt(Mathf.Sqrt(perSide * 2f));
            var selection = FindAnyObjectByType<SelectionManager>();
            var playerUnits = new List<SelectableUnit>();

            for (int side = 0; side < 2; side++)
            {
                int faction = side == 0 ? 1 : 2;
                float z0 = side == 0 ? -armyDistance * 0.5f : armyDistance * 0.5f;
                for (int i = 0; i < perSide; i++)
                {
                    int row = i / columns, col = i % columns;
                    float x = (col - (columns - 1) * 0.5f) * spacing;
                    float z = z0 + (side == 0 ? -row : row) * spacing;
                    var def = i % 2 == 0 ? light : heavy;
                    var go = FactionSpawner.Spawn(def.Prefab, new Vector3(x, 0f, z), Quaternion.LookRotation(side == 0 ? Vector3.forward : Vector3.back), faction);
                    units.Add(go);
                    if (faction == 1) playerUnits.Add(go.GetComponent<SelectableUnit>());
                }
            }
            if (selection != null) selection.Selection.Replace(playerUnits);
            return units;
        }

        private static IEnumerator WaitSeconds(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) yield return null;
        }

        private IEnumerator Measure(int size, string phase, float seconds)
        {
            var frameMs = new List<float>(4096);
            var gcAlloc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            var markers = PerfMarkers.All.Select(n => (n, ProfilerRecorder.StartNew(ProfilerCategory.Scripts, n))).ToList();
            var markerTotalsNs = new double[markers.Count];
            var engine = EngineCounters.Select(c => ProfilerRecorder.StartNew(new ProfilerCategory(c.category), c.name)).ToList();
            var engineTotals = new double[engine.Count];
            double gcBytes = 0;
            int gcStart = GC.CollectionCount(0);
            long pathsStart = UnitMover.PathRequests;
            double unitsSum = 0, projectilesSum = 0;
            float start = Time.realtimeSinceStartup;

            yield return null; // first sampled frame
            while (Time.realtimeSinceStartup - start < seconds)
            {
                frameMs.Add(Time.unscaledDeltaTime * 1000f);
                if (gcAlloc.Valid) gcBytes += gcAlloc.LastValue;
                for (int i = 0; i < markers.Count; i++)
                    if (markers[i].Item2.Valid) markerTotalsNs[i] += markers[i].Item2.LastValue;
                for (int i = 0; i < engine.Count; i++)
                    if (engine[i].Valid) engineTotals[i] += engine[i].LastValue;
                unitsSum += CombatUnit.All.Count;
                projectilesSum += Projectile.Alive;
                yield return null;
            }
            float elapsed = Time.realtimeSinceStartup - start;

            var sorted = frameMs.OrderBy(x => x).ToList();
            float Pct(float p) => sorted.Count == 0 ? 0f : sorted[Mathf.Clamp(Mathf.CeilToInt(p * sorted.Count) - 1, 0, sorted.Count - 1)];
            int n = Mathf.Max(1, frameMs.Count);
            float avg = frameMs.Count == 0 ? 0f : frameMs.Average();
            var r = new PhaseResult
            {
                build = Application.isEditor ? "editor" : Debug.isDebugBuild ? "development player" : "release player",
                armySize = size,
                phase = phase,
                frames = frameMs.Count,
                seconds = elapsed,
                avgMs = avg,
                p50Ms = Pct(0.5f),
                p95Ms = Pct(0.95f),
                p99Ms = Pct(0.99f),
                maxMs = sorted.Count > 0 ? sorted[sorted.Count - 1] : 0f,
                fps = avg > 0f ? 1000f / avg : 0f,
                gcKbPerFrame = gcAlloc.Valid ? (float)(gcBytes / n / 1024.0) : -1f,
                gcCollections = GC.CollectionCount(0) - gcStart,
                managedMb = GC.GetTotalMemory(false) / (1024f * 1024f),
                totalMb = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / (1024f * 1024f),
                pathRequestsPerSecond = (UnitMover.PathRequests - pathsStart) / Mathf.Max(0.001f, elapsed),
                avgUnitsAlive = (float)(unitsSum / n),
                avgProjectiles = (float)(projectilesSum / n),
            };
            for (int i = 0; i < markers.Count; i++)
            {
                if (markers[i].Item2.Valid) r.markerMs.Add($"{markers[i].n}={markerTotalsNs[i] / n / 1e6:0.000}");
                markers[i].Item2.Dispose();
            }
            for (int i = 0; i < engine.Count; i++)
            {
                if (engine[i].Valid)
                {
                    double v = engineTotals[i] / n;
                    r.engine.Add(EngineCounters[i].isCount ? $"{EngineCounters[i].name}={v:0}" : $"{EngineCounters[i].name}={v / 1e6:0.00}ms");
                }
                engine[i].Dispose();
            }
            gcAlloc.Dispose();
            Results.Add(r);
            Debug.Log($"[Benchmark] {r.build} N={size} {phase}: avg {r.avgMs:0.00} ms ({r.fps:0} fps) p95 {r.p95Ms:0.00} p99 {r.p99Ms:0.00} max {r.maxMs:0.0} | " +
                      $"GC {r.gcKbPerFrame:0.0} KB/frame, {r.gcCollections} collections | managed {r.managedMb:0} MB total {r.totalMb:0} MB | " +
                      $"paths {r.pathRequestsPerSecond:0}/s units {r.avgUnitsAlive:0} projectiles {r.avgProjectiles:0}" +
                      (r.engine.Count > 0 ? " | engine " + string.Join(" ", r.engine) : "") +
                      (r.markerMs.Count > 0 ? " | ms/frame " + string.Join(" ", r.markerMs) : ""));
        }

        private void WriteResults()
        {
            var args = Environment.GetCommandLineArgs();
            int idx = Array.IndexOf(args, OutputSwitch);
            OutputPath = idx >= 0 && idx + 1 < args.Length
                ? args[idx + 1]
                : Path.Combine(Application.persistentDataPath, "ironfront-benchmark.json");
            var sb = new StringBuilder("[\n");
            for (int i = 0; i < Results.Count; i++)
                sb.Append("  ").Append(JsonUtility.ToJson(Results[i])).Append(i < Results.Count - 1 ? ",\n" : "\n");
            sb.Append("]\n");
            try
            {
                File.WriteAllText(OutputPath, sb.ToString());
                Debug.Log("[Benchmark] Results written to " + OutputPath);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Benchmark] Could not write results: " + e.Message);
            }
        }
    }

    /// <summary>Loads the benchmark scene when the player is started with <see cref="BenchmarkRunner.CommandLineSwitch"/>.</summary>
    public static class BenchmarkBootstrap
    {
        public const string SceneName = "RTS_Benchmark";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void MaybeStart()
        {
            if (Application.isEditor) return;
            if (!Environment.GetCommandLineArgs().Contains(BenchmarkRunner.CommandLineSwitch)) return;
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == SceneName) return;
            UnityEngine.SceneManagement.SceneManager.LoadScene(SceneName);
        }
    }
}
