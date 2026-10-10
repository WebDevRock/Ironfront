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
                Time.timeScale = 10f;
                float simulated = 0f;
                while (!flow.IsOver && simulated < 1500f)
                {
                    simulated += Time.deltaTime;
                    yield return null;
                }
                Time.timeScale = 1f;

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
