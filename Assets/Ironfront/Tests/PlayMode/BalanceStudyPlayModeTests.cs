#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Ironfront.AI;
using Ironfront.Combat;
using Ironfront.Construction;
using Ironfront.Economy;
using Ironfront.Core.Victory;
using Ironfront.Match;
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
    /// AI-vs-AI balance study: identical Normal commanders for both factions, several variation seeds, each seed
    /// played from both starting layouts (RTS_Match and RTS_Match_Swapped). Records outcomes; it asserts only that
    /// every match reaches an outcome. Explicit (long: about 1–3 minutes per match at 10× speed).
    /// Results: Logs "[Balance]" lines and writes Temp/balance_study.txt. To replay selected matches set the
    /// EditorPrefs key "Ironfront.BalanceRuns", e.g. "37:standard,37:swapped" (empty = full study).
    /// A match that times out logs a state dump (units, orders, credits, ore, AI logs).
    /// </summary>
    [Explicit("Long-running balance study; run on demand.")]
    public class BalanceStudyPlayModeTests
    {
        private const int Player = 1;
        private const int Enemy = 2;
        private static readonly int[] Seeds = { 11, 23, 37, 51, 73 };

        /// <summary>Per-faction statistics gathered from gameplay events during one match (Milestone 007 analysis).</summary>
        private sealed class FactionTally
        {
            public int StartCredits, Spent, Income;
            public readonly Dictionary<string, int> Produced = new Dictionary<string, int>();
            public readonly Dictionary<string, int> Built = new Dictionary<string, int>();
            public readonly Dictionary<string, int> Lost = new Dictionary<string, int>();
            public readonly Dictionary<string, float> DamageBy = new Dictionary<string, float>();
            public readonly List<string> Research = new List<string>();
            public readonly List<string> ResearchStarted = new List<string>();
            public float FirstAttack = -1f;

            public string Json()
            {
                string D<T>(Dictionary<string, T> d) => "{" + string.Join(",", d.OrderBy(k => k.Key).Select(k => $"\"{k.Key}\":{System.Convert.ToSingle(k.Value):0.#}")) + "}";
                return $"{{\"spent\":{Spent},\"income\":{Income},\"produced\":{D(Produced)},\"built\":{D(Built)},\"lost\":{D(Lost)}," +
                       $"\"damageBy\":{D(DamageBy)},\"research\":[{string.Join(",", Research.Select(r => $"\"{r}\""))}]," +
                       $"\"researchStarted\":[{string.Join(",", ResearchStarted.Select(r => $"\"{r}\""))}],\"firstAttack\":{FirstAttack:0}}}";
            }
        }

        private static string KindOf(Health h)
        {
            if (h == null) return "unknown";
            var b = h.GetComponent<Ironfront.Buildings.Building>();
            if (b != null && b.Definition != null) return b.Definition.Id;
            var u = h.GetComponent<Ironfront.Units.UnitIdentity>();
            return u != null && u.Definition != null ? u.Definition.Id : h.name;
        }

        private static void Add<T>(Dictionary<string, T> d, string key, T v, System.Func<T, T, T> plus) =>
            d[key] = d.TryGetValue(key, out var old) ? plus(old, v) : v;

        private struct Run
        {
            public int Seed;
            public bool Swapped;
            public string Result;
            public int Winner;
            public float Seconds;
        }

        [UnityTest, Timeout(7200000)]
        public IEnumerator AiVsAi_MultipleSeeds_BothStartingPositions()
        {
            // This study measures outcomes only. Over ~10 minutes, unrelated Editor packages (the unused AI Assistant's
            // relay process) can log errors, which would otherwise fail the whole study mid-way.
            LogAssert.ignoreFailingMessages = true;
            var runs = new List<Run>();
            var plan = new List<(int seed, bool swapped)>();
            string custom = EditorPrefs.GetString("Ironfront.BalanceRuns", "");
            if (string.IsNullOrWhiteSpace(custom))
                foreach (int s in Seeds) { plan.Add((s, false)); plan.Add((s, true)); }
            else
                foreach (var item in custom.Split(','))
                {
                    var p = item.Trim().Split(':');
                    plan.Add((int.Parse(p[0]), p.Length > 1 && p[1] == "swapped"));
                }

            foreach (var (seed, swapped) in plan)
            {
                string path = swapped ? "Assets/Scenes/RTS_Match_Swapped.unity" : "Assets/Scenes/RTS_Match.unity";
                var op = EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Single));
                while (!op.isDone) yield return null;
                yield return null;
                yield return null;

                var enemyAi = EnemyCommander.All.Single(c => c.FactionId == Enemy);
                enemyAi.SetSeed(seed * 7919 + 2);
                var go = new GameObject("PlayerCommander(Balance)");
                go.SetActive(false);
                var playerAi = go.AddComponent<EnemyCommander>();
                playerAi.Configure(Player, enemyAi.Difficulty, Object.FindAnyObjectByType<BuildingPlacementController>());
                playerAi.SetSeed(seed * 7919 + 1);
                go.SetActive(true);
                yield return null;

                var flow = MatchFlowController.Current;
                var match = MatchController.Current;
                var tally = new Dictionary<int, FactionTally> { { Player, new FactionTally() }, { Enemy, new FactionTally() } };
                var last = new Dictionary<int, int>();
                foreach (int f in new[] { Player, Enemy })
                {
                    int faction = f;
                    var wallet = match.GetFaction(faction).Wallet;
                    tally[faction].StartCredits = wallet.Credits;
                    last[faction] = wallet.Credits;
                    wallet.Changed += balance =>
                    {
                        int delta = balance - last[faction];
                        if (delta < 0) tally[faction].Spent -= delta; else tally[faction].Income += delta;
                        last[faction] = balance;
                    };
                }
                System.Action<Ironfront.Buildings.VehicleFactory, GameObject> onProduced = (fac, unit) =>
                {
                    if (fac != null && unit != null && tally.TryGetValue(fac.FactionId, out var t)) Add(t.Produced, KindOf(unit.GetComponent<Health>()), 1, (a, b) => a + b);
                };
                System.Action<Ironfront.Buildings.Building> onBuilt = b =>
                {
                    if (b != null && tally.TryGetValue(b.FactionId, out var t)) Add(t.Built, b.Definition.Id, 1, (a, c) => a + c);
                };
                System.Action<Health> onDied = h =>
                {
                    if (h != null && tally.TryGetValue(h.FactionId, out var t)) Add(t.Lost, KindOf(h), 1, (a, b) => a + b);
                };
                System.Action<Health, float, Health> onDamaged = (victim, dmg, attacker) =>
                {
                    if (attacker != null && victim != null && tally.TryGetValue(attacker.FactionId, out var t) && attacker.FactionId != victim.FactionId)
                        Add(t.DamageBy, KindOf(attacker), dmg, (a, b) => a + b);
                };
                Ironfront.Buildings.VehicleFactory.AnyUnitProduced += onProduced;
                BuildingPlacementController.AnyBuildingPlaced += onBuilt;
                Health.AnyDied += onDied;
                Health.AnyDamaged += onDamaged;
                ResearchHook(tally);

                Time.timeScale = 10f;
                float simulated = 0f;
                while (!flow.IsOver && simulated < 1500f)
                {
                    simulated += Time.deltaTime;
                    if (tally[Player].FirstAttack < 0f && playerAi.AttacksLaunched > 0) tally[Player].FirstAttack = simulated;
                    if (tally[Enemy].FirstAttack < 0f && enemyAi.AttacksLaunched > 0) tally[Enemy].FirstAttack = simulated;
                    yield return null;
                }
                Time.timeScale = 1f;
                Ironfront.Buildings.VehicleFactory.AnyUnitProduced -= onProduced;
                BuildingPlacementController.AnyBuildingPlaced -= onBuilt;
                Health.AnyDied -= onDied;
                Health.AnyDamaged -= onDamaged;

                var run = new Run { Seed = seed, Swapped = swapped, Seconds = simulated };
                if (!flow.IsOver)
                {
                    run.Result = "timeout";
                    Debug.Log("[Balance] TIMEOUT STATE\n" + DumpState(playerAi, enemyAi));
                }
                else if (flow.Outcome.Result == MatchResult.Draw) run.Result = $"draw ({flow.Outcome.Reason})";
                else
                {
                    run.Winner = flow.Outcome.WinnerFactionId;
                    run.Result = $"F{run.Winner} won";
                }
                runs.Add(run);
                string line = $"{{\"seed\":{seed},\"swapped\":{(swapped ? "true" : "false")},\"result\":\"{run.Result}\",\"winner\":{run.Winner},\"seconds\":{simulated:0}," +
                              $"\"f1\":{tally[Player].Json()},\"f2\":{tally[Enemy].Json()}}}";
                File.AppendAllText(Path.Combine(Application.dataPath, "..", "Temp", "balance_runs.jsonl"), line + "\n");
                Debug.Log($"[Balance] seed {seed} {(swapped ? "swapped (F1 north-east)" : "standard (F1 south-west)")}: " +
                          $"{run.Result} after {simulated:0}s; F1 built {playerAi.BuildingsPlaced}/produced {playerAi.UnitsProduced}/attacks {playerAi.AttacksLaunched}, " +
                          $"F2 built {enemyAi.BuildingsPlaced}/produced {enemyAi.UnitsProduced}/attacks {enemyAi.AttacksLaunched}");
            }

            int n = runs.Count;
            int f1 = runs.Count(r => r.Winner == Player);
            int f2 = runs.Count(r => r.Winner == Enemy);
            int sw = runs.Count(r => r.Winner != 0 && ((r.Winner == Player) != r.Swapped)); // winner started south-west
            int ne = runs.Count(r => r.Winner != 0) - sw;
            int other = n - f1 - f2;
            var sb = new StringBuilder();
            sb.AppendLine($"Balance study: {n} AI-vs-AI matches, Normal vs Normal, 10x time scale");
            sb.AppendLine($"Faction 1 wins {f1}, faction 2 wins {f2}, draws/timeouts {other}");
            sb.AppendLine($"South-west start wins {sw}, north-east start wins {ne}");
            foreach (var r in runs)
                sb.AppendLine($"seed {r.Seed,3}  {(r.Swapped ? "swapped " : "standard")}  {r.Result,-28} {r.Seconds,5:0}s");
            Debug.Log("[Balance] " + sb);
            File.WriteAllText(Path.Combine(Application.dataPath, "..", "Temp", "balance_study.txt"), sb.ToString());

            Assert.AreEqual(plan.Count, n);
            Assert.IsTrue(runs.All(r => r.Result != "timeout"), "every match reached an outcome");
        }

        /// <summary>Records research started and completed per faction, with the match time.</summary>
        private static void ResearchHook(Dictionary<int, FactionTally> tally)
        {
            var match = MatchController.Current;
            float start = Time.time;
            foreach (var kv in tally)
            {
                var t = kv.Value;
                var research = match.GetFaction(kv.Key).Research;
                research.Started += id => t.ResearchStarted.Add($"{id}@{Time.time - start:0}");
                research.Completed += id => t.Research.Add($"{id}@{Time.time - start:0}");
            }
        }

        private static string DumpState(EnemyCommander f1, EnemyCommander f2)
        {
            var sb = new StringBuilder();
            var match = MatchController.Current;
            foreach (var ai in new[] { f1, f2 })
            {
                var f = match.GetFaction(ai.FactionId);
                sb.AppendLine($"F{ai.FactionId}: state {ai.State} ({ai.TimeInState:0}s), objective '{ai.Objective}', credits {f.Wallet.Credits}, " +
                              $"attackers {ai.AttackersCount}, rally {ai.RallyPoint}");
                foreach (var h in Health.All.Where(h => h.FactionId == ai.FactionId))
                {
                    var cu = h.GetComponent<CombatUnit>();
                    string order = cu != null ? cu.DescribeOrder() : (h.IsStructure ? "structure" : "unarmed");
                    var harv = h.GetComponent<Harvester>();
                    if (harv != null) order = "harvester";
                    sb.AppendLine($"   {h.DisplayName,-16} at {h.transform.position:F0} hp {h.Model.Fraction:P0} : {order}");
                }
                sb.AppendLine("   log: " + string.Join(" | ", ai.Log.NewestFirst().Take(12)));
            }
            sb.AppendLine("Ore: " + string.Join(", ", OreDeposit.All.Select(d => $"{d.name} {d.Remaining}")));
            return sb.ToString();
        }
    }
}
#endif
