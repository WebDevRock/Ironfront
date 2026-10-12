#if UNITY_EDITOR
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
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ironfront.Tests
{
    /// <summary>
    /// Milestone 008 AI-vs-AI skirmish study on the four battlefields. Each run: a map, a spawn seed (which picks the
    /// start pair exactly as a player's match would), and a difficulty for each faction. Both commanders start
    /// without knowing where the other is. Records outcome, duration, start slots, map size, enemy-discovery and
    /// first-contact times, income and production to Temp/skirmish_runs.jsonl and a summary to Temp/skirmish_study.txt.
    /// Asserts only that every match reaches an outcome (no strategic deadlock). Explicit: about 1–4 minutes per match.
    /// Plan override: EditorPrefs "Ironfront.SkirmishRuns" = "map:seed:F1difficulty:F2difficulty,...".
    /// </summary>
    [Explicit("Long-running AI-vs-AI study; run on demand.")]
    public class SkirmishStudyPlayModeTests
    {
        private const int Player = 1;
        private const int Enemy = 2;
        private const float TimeLimit = 2700f;

        private static readonly (string id, string scene)[] Maps =
        {
            ("dustfall_plains", "Skirmish_DustfallPlains"), ("iron_pass", "Skirmish_IronPass"),
            ("green_divide", "Skirmish_GreenDivide"), ("blackstone_basin", "Skirmish_BlackstoneBasin"),
        };

        private static List<(string map, int seed, DifficultyLevel f1, DifficultyLevel f2)> DefaultPlan()
        {
            var plan = new List<(string, int, DifficultyLevel, DifficultyLevel)>();
            foreach (var (id, _) in Maps)
            {
                foreach (int seed in new[] { 11, 23, 37, 51 }) plan.Add((id, seed, DifficultyLevel.Normal, DifficultyLevel.Normal));
                plan.Add((id, 61, DifficultyLevel.Hard, DifficultyLevel.Normal));
                plan.Add((id, 62, DifficultyLevel.Normal, DifficultyLevel.Hard));
                plan.Add((id, 71, DifficultyLevel.Normal, DifficultyLevel.Easy));
            }
            return plan;
        }

        private static AiDifficulty Asset(DifficultyLevel level) =>
            AssetDatabase.LoadAssetAtPath<AiDifficulty>($"Assets/Ironfront/Data/AI/{level}.asset");

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
                $"\"repairOrders\":{ai.RepairsOrdered},\"buildingRepairs\":{ai.BuildingRepairsStarted},\"vehiclesRepaired\":{Ironfront.Buildings.RepairLedger.VehiclesRepaired(ai.FactionId)}," +
                $"\"repairCreditsVehicles\":{Ironfront.Buildings.RepairLedger.VehicleCredits(ai.FactionId)},\"repairCreditsBuildings\":{Ironfront.Buildings.RepairLedger.BuildingCredits(ai.FactionId)}}}";

            private static string D(Dictionary<string, int> d) => "{" + string.Join(",", d.OrderBy(k => k.Key).Select(k => $"\"{k.Key}\":{k.Value}")) + "}";
        }

        private static string KindOf(GameObject go)
        {
            var b = go.GetComponent<Ironfront.Buildings.Building>();
            if (b != null && b.Definition != null) return b.Definition.Id;
            var u = go.GetComponent<Ironfront.Units.UnitIdentity>();
            return u != null && u.Definition != null ? u.Definition.Id : go.name;
        }

        [UnityTest, Timeout(14400000)]
        public IEnumerator AiVsAi_AllMaps_SeededStarts()
        {
            LogAssert.ignoreFailingMessages = true; // outcome study; unrelated editor noise must not abort it
            var plan = DefaultPlan();
            string custom = EditorPrefs.GetString("Ironfront.SkirmishRuns", "");
            if (!string.IsNullOrWhiteSpace(custom))
            {
                plan.Clear();
                foreach (var item in custom.Split(','))
                {
                    var p = item.Trim().Split(':');
                    plan.Add((p[0], int.Parse(p[1]), (DifficultyLevel)System.Enum.Parse(typeof(DifficultyLevel), p[2]),
                        (DifficultyLevel)System.Enum.Parse(typeof(DifficultyLevel), p[3])));
                }
            }

            string outFile = Path.Combine(Application.dataPath, "..", "Temp", "skirmish_runs.jsonl");
            var lines = new List<string>();
            int timeouts = 0;
            foreach (var (map, seed, d1, d2) in plan)
            {
                SkirmishSession.Select(new SkirmishSettings { MapId = map, Difficulty = d2, Seed = seed });
                string scene = Maps.First(m => m.id == map).scene;
                var op = EditorSceneManager.LoadSceneAsyncInPlayMode($"Assets/Scenes/Maps/{scene}.unity", new LoadSceneParameters(LoadSceneMode.Single));
                while (!op.isDone) yield return null;
                yield return null;
                yield return null;

                var init = SkirmishInitializer.Current;
                var enemyAi = EnemyCommander.All.Single(c => c.FactionId == Enemy);
                enemyAi.SetSeed(seed * 7919 + 2);
                var go = new GameObject("PlayerCommander(Study)");
                go.SetActive(false);
                var playerAi = go.AddComponent<EnemyCommander>();
                playerAi.Configure(Player, Asset(d1), Object.FindAnyObjectByType<BuildingPlacementController>());
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
                System.Action<Ironfront.Buildings.VehicleFactory, GameObject> onProduced = (fac, unit) =>
                {
                    if (fac != null && unit != null && tally.TryGetValue(fac.FactionId, out var t)) t.Produced[KindOf(unit)] = t.Produced.TryGetValue(KindOf(unit), out int c) ? c + 1 : 1;
                };
                System.Action<Ironfront.Buildings.Building> onBuilt = b =>
                {
                    if (b != null && tally.TryGetValue(b.FactionId, out var t)) t.Built[b.Definition.Id] = t.Built.TryGetValue(b.Definition.Id, out int c) ? c + 1 : 1;
                };
                System.Action<Health, float, Health> onDamaged = (victim, dmg, attacker) =>
                {
                    if (firstContact < 0f && attacker != null && victim != null && attacker.FactionId != victim.FactionId) firstContact = Time.time - start;
                };
                Ironfront.Buildings.VehicleFactory.AnyUnitProduced += onProduced;
                BuildingPlacementController.AnyBuildingPlaced += onBuilt;
                Health.AnyDamaged += onDamaged;

                Time.timeScale = 10f;
                float realStart = Time.realtimeSinceStartup;
                string traceFile = Path.Combine(Application.dataPath, "..", "Temp", "skirmish_trace.txt");
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
                Ironfront.Buildings.VehicleFactory.AnyUnitProduced -= onProduced;
                BuildingPlacementController.AnyBuildingPlaced -= onBuilt;
                Health.AnyDamaged -= onDamaged;

                string result = !flow.IsOver ? "timeout"
                    : flow.Outcome.Result == MatchResult.Draw ? $"draw ({flow.Outcome.Reason})" : $"F{flow.Outcome.WinnerFactionId} won";
                if (!flow.IsOver) timeouts++;
                var r = init.Resolved;
                string line = $"{{\"map\":\"{map}\",\"size\":{init.Map.Size:0},\"seed\":{seed},\"f1Slot\":{r.PlayerSlot},\"f2Slot\":{r.EnemySlot}," +
                              $"\"separation\":{(StartSlot.Find(r.PlayerSlot).Headquarters - StartSlot.Find(r.EnemySlot).Headquarters).magnitude:0}," +
                              $"\"result\":\"{result}\",\"winner\":{(flow.IsOver ? flow.Outcome.WinnerFactionId : 0)},\"seconds\":{seconds:0},\"firstContact\":{firstContact:0}," +
                              $"\"realSeconds\":{Time.realtimeSinceStartup - realStart:0},\"f1\":{tally[Player].Json(playerAi)},\"f2\":{tally[Enemy].Json(enemyAi)}}}";
                lines.Add(line);
                File.AppendAllText(outFile, line + "\n");
                Debug.Log($"[Skirmish] {map} seed {seed} {d1} (slot {r.PlayerSlot}) vs {d2} (slot {r.EnemySlot}): {result} after {seconds:0}s; " +
                          $"located F1 {playerAi.Intel.FirstDiscovery - start:0}s F2 {enemyAi.Intel.FirstDiscovery - start:0}s, contact {firstContact:0}s");
                if (!flow.IsOver)
                    Debug.Log("[Skirmish] TIMEOUT: " + string.Join(" | ", playerAi.Log.NewestFirst().Where(e => !e.Contains("Scout")).Take(10)) + " || " +
                              string.Join(" | ", enemyAi.Log.NewestFirst().Where(e => !e.Contains("Scout")).Take(10)) +
                              $" || F1 {playerAi.State} army {playerAi.LastSnapshot.ArmyCount} credits {match.GetFaction(Player).Wallet.Credits}; " +
                              $"F2 {enemyAi.State} army {enemyAi.LastSnapshot.ArmyCount} credits {match.GetFaction(Enemy).Wallet.Credits}");
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Skirmish study: {lines.Count} AI-vs-AI matches, 10x time scale");
            foreach (var l in lines) sb.AppendLine(l);
            File.WriteAllText(Path.Combine(Application.dataPath, "..", "Temp", "skirmish_study.txt"), sb.ToString());
            Debug.Log("[Skirmish] study complete: " + lines.Count + " matches, " + timeouts + " timeouts");
            SkirmishSession.Reset();
            Assert.AreEqual(plan.Count, lines.Count);
            Assert.AreEqual(0, timeouts, "every match reached an outcome");
        }
    }
}
#endif
