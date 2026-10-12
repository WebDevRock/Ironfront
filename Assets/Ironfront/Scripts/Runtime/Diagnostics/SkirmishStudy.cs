using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Ironfront.AI;
using Ironfront.Combat;
using Ironfront.Construction;
using Ironfront.Core.Skirmish;
using Ironfront.Core.Victory;
using Ironfront.Data;
using Ironfront.Match;
using Ironfront.Skirmish;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Ironfront.Diagnostics
{
    /// <summary>
    /// The AI-vs-AI skirmish study (Milestone 008, extended in 009–010), shared by the Editor PlayMode test
    /// (<c>SkirmishStudyPlayModeTests</c>) and the headless player runner (<see cref="SkirmishStudyRunner"/>), so both play
    /// exactly the same matches. Each run: a map, a spawn seed (which picks the start pair exactly as a player's match
    /// would) and a difficulty for each faction. Faction 2 is the scene's own commander; faction 1 gets a second
    /// commander. Both start without knowing where the other is. 10× time scale, 2700 s limit.
    /// <para>Outputs, in <c>outDir</c>: one JSON line per match in <c>skirmish_runs.jsonl</c>, periodic state and the
    /// commanders' forward-base/sale decisions in <c>skirmish_trace.txt</c>, and <c>skirmish_study.txt</c> at the end.</para>
    /// </summary>
    public static class SkirmishStudy
    {
        public const int Player = 1;
        public const int Enemy = 2;
        public const float TimeLimit = 2700f;
        public const float TimeScale = 10f;
        public const string RunsFile = "skirmish_runs.jsonl";
        public const string TraceFile = "skirmish_trace.txt";
        public const string SummaryFile = "skirmish_study.txt";

        public static readonly (string id, string scene)[] Maps =
        {
            ("dustfall_plains", "Skirmish_DustfallPlains"), ("iron_pass", "Skirmish_IronPass"),
            ("green_divide", "Skirmish_GreenDivide"), ("blackstone_basin", "Skirmish_BlackstoneBasin"),
        };

        public readonly struct Run
        {
            public readonly string Map;
            public readonly int Seed;
            public readonly DifficultyLevel F1, F2;
            public Run(string map, int seed, DifficultyLevel f1, DifficultyLevel f2) { Map = map; Seed = seed; F1 = f1; F2 = f2; }
            public override string ToString() => $"{Map}:{Seed}:{F1}:{F2}";
        }

        /// <summary>Result of a whole study.</summary>
        public sealed class Results
        {
            public readonly List<string> Lines = new List<string>();
            public int Timeouts;
            public int Planned;
        }

        /// <summary>The default 28-match plan: four maps × (four Normal mirrors, Hard v Normal, Normal v Hard, Normal v Easy).</summary>
        public static List<Run> DefaultPlan()
        {
            var plan = new List<Run>();
            foreach (var (id, _) in Maps)
            {
                foreach (int seed in new[] { 11, 23, 37, 51 }) plan.Add(new Run(id, seed, DifficultyLevel.Normal, DifficultyLevel.Normal));
                plan.Add(new Run(id, 61, DifficultyLevel.Hard, DifficultyLevel.Normal));
                plan.Add(new Run(id, 62, DifficultyLevel.Normal, DifficultyLevel.Hard));
                plan.Add(new Run(id, 71, DifficultyLevel.Normal, DifficultyLevel.Easy));
            }
            return plan;
        }

        /// <summary>Parses "map:seed:F1difficulty:F2difficulty,..." (empty → the default plan).</summary>
        public static List<Run> ParsePlan(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return DefaultPlan();
            var plan = new List<Run>();
            foreach (var item in text.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var p = item.Trim().Split(':');
                if (p.Length != 4) throw new FormatException($"Bad study run '{item}': expected map:seed:F1:F2");
                if (!Maps.Any(m => m.id == p[0])) throw new FormatException($"Unknown map '{p[0]}'");
                plan.Add(new Run(p[0], int.Parse(p[1]), (DifficultyLevel)Enum.Parse(typeof(DifficultyLevel), p[2]),
                    (DifficultyLevel)Enum.Parse(typeof(DifficultyLevel), p[3])));
            }
            return plan;
        }

        private sealed class Tally
        {
            public float MatchStart;
            public int Spent, Income, last;
            public readonly Dictionary<string, int> Produced = new Dictionary<string, int>();
            public readonly Dictionary<string, int> Built = new Dictionary<string, int>();
            public string Json(EnemyCommander ai) =>
                $"{{\"difficulty\":\"{ai.Difficulty.Level}\",\"spent\":{Spent},\"income\":{Income},\"produced\":{D(Produced)},\"built\":{D(Built)}," +
                $"\"located\":{(ai.Intel.FirstDiscovery < 0f ? -1f : ai.Intel.FirstDiscovery - MatchStart):0},\"escalation\":{ai.AttackEscalation},\"scoutMissions\":{ai.ScoutMissions},\"attacks\":{ai.AttacksLaunched}," +
                $"\"sitesCleared\":{ai.Intel.Count(Core.AI.EnemyIntel.SiteState.Empty)},\"rejectedScoutPoints\":{ai.RejectedScoutPoints}," +
                $"\"repairOrders\":{ai.RepairsOrdered},\"buildingRepairs\":{ai.BuildingRepairsStarted},\"vehiclesRepaired\":{Buildings.RepairLedger.VehiclesRepaired(ai.FactionId)}," +
                $"\"repairCreditsVehicles\":{Buildings.RepairLedger.VehicleCredits(ai.FactionId)},\"repairCreditsBuildings\":{Buildings.RepairLedger.BuildingCredits(ai.FactionId)}," +
                $"\"mcvs\":{ai.McvsProduced},\"mcvsLost\":{ai.McvsLost},\"forwardBases\":{ai.ForwardBasesEstablished}," +
                $"\"firstForwardBase\":{(ai.FirstForwardBaseTime < 0f ? -1f : ai.FirstForwardBaseTime - MatchStart):0},\"deployAttempts\":{ai.DeployAttempts},\"deployFailures\":{ai.DeployFailures}," +
                $"\"forwardBasesAtEnd\":{Buildings.Building.All.Count(b => b.FactionId == ai.FactionId && b.Definition != null && b.Definition.Id == "forward_command")}," +
                $"\"structuresSold\":{ai.StructuresSold},\"saleRefunds\":{Buildings.BuildingSale.RefundedBy(ai.FactionId)}}}";

            private static string D(Dictionary<string, int> d) => "{" + string.Join(",", d.OrderBy(k => k.Key).Select(k => $"\"{k.Key}\":{k.Value}")) + "}";
        }

        private static string KindOf(GameObject go)
        {
            var b = go.GetComponent<Buildings.Building>();
            if (b != null && b.Definition != null) return b.Definition.Id;
            var u = go.GetComponent<Units.UnitIdentity>();
            return u != null && u.Definition != null ? u.Definition.Id : go.name;
        }

        /// <summary>
        /// Plays every run of <paramref name="plan"/> in turn, appending to the output files in <paramref name="outDir"/>.
        /// <paramref name="label"/> goes into the summary header (e.g. "editor" or "headless").
        /// </summary>
        public static IEnumerator RunPlan(IReadOnlyList<Run> plan, string outDir, Results results, string label = "")
        {
            Directory.CreateDirectory(outDir);
            string outFile = Path.Combine(outDir, RunsFile);
            string traceFile = Path.Combine(outDir, TraceFile);
            results.Planned = plan.Count;
            foreach (var run in plan)
                yield return RunMatch(run, outFile, traceFile, results);

            var sb = new StringBuilder();
            sb.AppendLine($"Skirmish study{(string.IsNullOrEmpty(label) ? "" : " (" + label + ")")}: {results.Lines.Count} AI-vs-AI matches, {TimeScale:0}x time scale");
            foreach (var l in results.Lines) sb.AppendLine(l);
            File.WriteAllText(Path.Combine(outDir, SummaryFile), sb.ToString());
            Debug.Log("[Skirmish] study complete: " + results.Lines.Count + " matches, " + results.Timeouts + " timeouts");
            SkirmishSession.Reset();
        }

        private static IEnumerator RunMatch(Run run, string outFile, string traceFile, Results results)
        {
            string map = run.Map;
            int seed = run.Seed;
            SkirmishSession.Select(new SkirmishSettings { MapId = map, Difficulty = run.F2, Seed = seed });
            string scene = Maps.First(m => m.id == map).scene;
            var op = SceneManager.LoadSceneAsync(scene, LoadSceneMode.Single); // every map scene is in the build settings
            while (!op.isDone) yield return null;
            yield return null;
            yield return null;

            var init = SkirmishInitializer.Current;
            var enemyAi = EnemyCommander.All.Single(c => c.FactionId == Enemy);
            enemyAi.SetSeed(seed * 7919 + 2);
            var go = new GameObject("PlayerCommander(Study)");
            go.SetActive(false);
            var playerAi = go.AddComponent<EnemyCommander>();
            var f1Difficulty = init.Catalog != null ? init.Catalog.FindDifficulty(run.F1) : null;
            if (f1Difficulty == null) throw new InvalidOperationException($"No AiDifficulty asset for {run.F1} in the map catalog");
            playerAi.Configure(Player, f1Difficulty, Object.FindAnyObjectByType<BuildingPlacementController>());
            playerAi.SetSeed(seed * 7919 + 1);
            go.SetActive(true);
            yield return null;

            var match = MatchController.Current;
            var flow = MatchFlowController.Current;
            var tally = new Dictionary<int, Tally> { { Player, new Tally() }, { Enemy, new Tally() } };
            foreach (int f in new[] { Player, Enemy })
            {
                var t = tally[f];
                var wallet = match.GetFaction(f).Wallet;
                t.last = wallet.Credits;
                wallet.Changed += balance =>
                {
                    int delta = balance - t.last;
                    if (delta < 0) t.Spent -= delta; else t.Income += delta;
                    t.last = balance;
                };
            }
            float firstContact = -1f, start = Time.time;
            tally[Player].MatchStart = tally[Enemy].MatchStart = start;
            Action<Buildings.VehicleFactory, GameObject> onProduced = (fac, unit) =>
            {
                if (fac != null && unit != null && tally.TryGetValue(fac.FactionId, out var t)) t.Produced[KindOf(unit)] = t.Produced.TryGetValue(KindOf(unit), out int c) ? c + 1 : 1;
            };
            Action<Buildings.Building> onBuilt = b =>
            {
                if (b != null && tally.TryGetValue(b.FactionId, out var t)) t.Built[b.Definition.Id] = t.Built.TryGetValue(b.Definition.Id, out int c) ? c + 1 : 1;
            };
            Action<Health, float, Health> onDamaged = (victim, dmg, attacker) =>
            {
                if (firstContact < 0f && attacker != null && victim != null && attacker.FactionId != victim.FactionId) firstContact = Time.time - start;
            };
            Buildings.VehicleFactory.AnyUnitProduced += onProduced;
            BuildingPlacementController.AnyBuildingPlaced += onBuilt;
            Health.AnyDamaged += onDamaged;

            Time.timeScale = TimeScale;
            float realStart = Time.realtimeSinceStartup;
            // Milestone 010: the commanders' forward-base and sale decisions go to the trace as they happen.
            var keys = new[] { "Mobile Command", "Forward", "forward", "Deploy", "Selling", "ProduceMcv" };
            Action<string> log1 = e => { if (keys.Any(e.Contains)) File.AppendAllText(traceFile, $"{map} seed {seed}   F1 {e}\n"); };
            Action<string> log2 = e => { if (keys.Any(e.Contains)) File.AppendAllText(traceFile, $"{map} seed {seed}   F2 {e}\n"); };
            playerAi.Logged += log1;
            enemyAi.Logged += log2;
            float nextTrace = 0f;
            while (!flow.IsOver && Time.time - start < TimeLimit)
            {
                if (Time.time - start >= nextTrace)
                {
                    nextTrace += 120f;
                    string F(EnemyCommander ai, int f)
                    {
                        var s = ai.LastSnapshot;
                        return $"F{f} {ai.State,-8} army {s.ArmyCount,2} ({s.ArmyStrength:0.0}) atk {ai.AttacksLaunched,2}/{ai.AttackersCount,2} esc {ai.AttackEscalation} " +
                               $"cr {match.GetFaction(f).Wallet.Credits,5} inc {tally[f].Income,6} harv {s.Harvesters} ref {s.Refineries} fac {s.Factories} " +
                               $"def {s.GuardTowers}/{s.AtTurrets} knownEnemy {s.KnownEnemyStrength:0.0} obj '{ai.Objective}'";
                    }
                    File.AppendAllText(traceFile, $"{map} seed {seed} t={Time.time - start:0} | {F(playerAi, Player)} | {F(enemyAi, Enemy)}\n");
                }
                yield return null;
            }
            float seconds = Time.time - start;
            Time.timeScale = 1f;
            Buildings.VehicleFactory.AnyUnitProduced -= onProduced;
            BuildingPlacementController.AnyBuildingPlaced -= onBuilt;
            Health.AnyDamaged -= onDamaged;
            playerAi.Logged -= log1;
            enemyAi.Logged -= log2;

            string result = !flow.IsOver ? "timeout"
                : flow.Outcome.Result == MatchResult.Draw ? $"draw ({flow.Outcome.Reason})" : $"F{flow.Outcome.WinnerFactionId} won";
            if (!flow.IsOver) results.Timeouts++;
            var r = init.Resolved;
            string line = $"{{\"map\":\"{map}\",\"size\":{init.Map.Size:0},\"seed\":{seed},\"f1Slot\":{r.PlayerSlot},\"f2Slot\":{r.EnemySlot}," +
                          $"\"separation\":{(StartSlot.Find(r.PlayerSlot).Headquarters - StartSlot.Find(r.EnemySlot).Headquarters).magnitude:0}," +
                          $"\"result\":\"{result}\",\"winner\":{(flow.IsOver ? flow.Outcome.WinnerFactionId : 0)},\"seconds\":{seconds:0},\"firstContact\":{firstContact:0}," +
                          $"\"realSeconds\":{Time.realtimeSinceStartup - realStart:0},\"f1\":{tally[Player].Json(playerAi)},\"f2\":{tally[Enemy].Json(enemyAi)}}}";
            results.Lines.Add(line);
            File.AppendAllText(outFile, line + "\n");
            Debug.Log($"[Skirmish] {map} seed {seed} {run.F1} (slot {r.PlayerSlot}) vs {run.F2} (slot {r.EnemySlot}): {result} after {seconds:0}s; " +
                      $"located F1 {playerAi.Intel.FirstDiscovery - start:0}s F2 {enemyAi.Intel.FirstDiscovery - start:0}s, contact {firstContact:0}s");
            if (!flow.IsOver)
                Debug.Log("[Skirmish] TIMEOUT: " + string.Join(" | ", playerAi.Log.NewestFirst().Where(e => !e.Contains("Scout")).Take(10)) + " || " +
                          string.Join(" | ", enemyAi.Log.NewestFirst().Where(e => !e.Contains("Scout")).Take(10)) +
                          $" || F1 {playerAi.State} army {playerAi.LastSnapshot.ArmyCount} credits {match.GetFaction(Player).Wallet.Credits}; " +
                          $"F2 {enemyAi.State} army {enemyAi.LastSnapshot.ArmyCount} credits {match.GetFaction(Enemy).Wallet.Credits}");
        }
    }
}
