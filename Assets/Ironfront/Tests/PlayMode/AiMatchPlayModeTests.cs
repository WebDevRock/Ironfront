using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Ironfront.AI;
using Ironfront.Buildings;
using Ironfront.Combat;
using Ironfront.Construction;
using Ironfront.Core.AI;
using Ironfront.Core.Construction;
using Ironfront.Core.Economy;
using Ironfront.Core.Victory;
using Ironfront.Data;
using Ironfront.Economy;
using Ironfront.InputBindings;
using Ironfront.Match;
using Ironfront.Units;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ironfront.Tests
{
    /// <summary>
    /// The RTS_Match scene with the computer commander. Time is accelerated; rules are unchanged.
    /// Where a test needs the AI further along than its opening, it says so and sets that up explicitly
    /// (extra credits or units for the test fixture) - the commander code itself never receives bonuses.
    /// </summary>
    public class AiMatchPlayModeTests
    {
        private const int Player = 1;
        private const int Enemy = 2;

        private MatchController _match;
        private MatchFlowController _flow;
        private BuildingPlacementController _placement;
        private UnitDefinition _light, _heavy;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene("RTS_Match", LoadSceneMode.Single);
            yield return null;
            yield return null;
            _match = MatchController.Current;
            _flow = MatchFlowController.Current;
            _placement = Object.FindAnyObjectByType<BuildingPlacementController>();
            var factory = _match.Setup.ConstructionOptions.First(d => d.Id == "vehicle_factory");
            _light = factory.ProducibleUnits.First(u => u.Id == "light_tank");
            _heavy = factory.ProducibleUnits.First(u => u.Id == "heavy_tank");
        }

        [TearDown]
        public void RestoreTime() => Time.timeScale = 1f;

        // ---------- Helpers ----------

        private static EnemyCommander Ai(int faction = Enemy) => EnemyCommander.All.First(c => c.FactionId == faction);

        private static List<Building> Structures(int faction, string id = null) =>
            Building.All.Where(b => b.FactionId == faction && b.Definition != null && (id == null || b.Definition.Id == id)
                                    && b.GetComponent<Health>().IsAlive).ToList();

        private static List<Health> Units(int faction) =>
            Health.All.Where(h => h.IsAlive && h.FactionId == faction && h.GetComponent<UnitMover>() != null).ToList();

        private static int Harvesters(int faction) => Units(faction).Count(h => h.GetComponent<Harvester>() != null);
        private static int Tanks(int faction) => Units(faction).Count(h => h.GetComponent<CombatUnit>() != null);

        private static void Kill(Health h, int byFaction) => h.ApplyDamage(1e7f, byFaction);

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        private static IEnumerator WaitFor(Func<bool> condition, float gameSeconds, Action perFrame = null)
        {
            float t = 0f;
            while (!condition() && t < gameSeconds)
            {
                perFrame?.Invoke();
                t += Time.deltaTime;
                yield return null;
            }
        }

        private CombatUnit Spawn(UnitDefinition def, Vector3 position, int faction)
        {
            var go = Object.Instantiate(def.Prefab, position, Quaternion.identity);
            go.GetComponent<FactionMember>().FactionId = faction;
            return go.GetComponent<CombatUnit>();
        }

        // ---------- Match setup ----------

        [Test]
        public void MatchScene_StartsBothFactionsEqually_WithDistinctColours()
        {
            Assert.IsNotNull(_flow);
            CollectionAssert.AreEqual(new[] { Player, Enemy }, _flow.Competitors);
            foreach (int f in new[] { Player, Enemy })
            {
                Assert.AreEqual(1, Structures(f, "headquarters").Count, $"Faction {f} HQ");
                Assert.AreEqual(1, Structures(f, "refinery").Count, $"Faction {f} refinery");
                Assert.AreEqual(1, Harvesters(f), $"Faction {f} harvester");
                Assert.AreEqual(0, Tanks(f));
                Assert.AreEqual(2000, _match.GetFaction(f).Wallet.Credits);
            }
            Assert.AreNotEqual(_match.Setup.ColorOf(Player), _match.Setup.ColorOf(Enemy));
            Assert.AreEqual(4, OreDeposit.All.Count);

            // Point-symmetric: each faction's HQ is the same distance from its nearest ore fields.
            var playerHq = Structures(Player, "headquarters")[0].transform.position;
            var enemyHq = Structures(Enemy, "headquarters")[0].transform.position;
            var pd = OreDeposit.All.Select(d => Vector3.Distance(d.transform.position, playerHq)).OrderBy(x => x).Take(2).ToArray();
            var ed = OreDeposit.All.Select(d => Vector3.Distance(d.transform.position, enemyHq)).OrderBy(x => x).Take(2).ToArray();
            Assert.AreEqual(pd[0], ed[0], 0.01f);
            Assert.AreEqual(pd[1], ed[1], 0.01f);

            // Refinery docks (where harvesters unload) are equally far from each faction's ore fields.
            var playerDock = Structures(Player, "refinery")[0].FrontPoint;
            var enemyDock = Structures(Enemy, "refinery")[0].FrontPoint;
            var pDock = OreDeposit.All.Select(d => Vector3.Distance(Flat(d.transform.position), Flat(playerDock))).OrderBy(x => x).Take(2).ToArray();
            var eDock = OreDeposit.All.Select(d => Vector3.Distance(Flat(d.transform.position), Flat(enemyDock))).OrderBy(x => x).Take(2).ToArray();
            Assert.AreEqual(pDock[0], eDock[0], 0.01f, "Dock to main ore");
            Assert.AreEqual(pDock[1], eDock[1], 0.01f, "Dock to secondary ore");
            Assert.IsTrue(Structures(Enemy, "refinery")[0].FacesSouth && !Structures(Player, "refinery")[0].FacesSouth,
                "Both refineries face their opponent");
            Assert.AreEqual(pDock[1] > EnemyCommander.LocalOreRadius, eDock[1] > EnemyCommander.LocalOreRadius,
                "Secondary ore is on the same side of the expansion threshold for both");

            Assert.AreEqual(1, EnemyCommander.All.Count, "Only the enemy is computer-controlled");
            Assert.AreEqual(Enemy, Ai().FactionId);
        }

        // ---------- Economy & construction ----------

        [UnityTest, Timeout(900000)]
        public IEnumerator Ai_HarvestsBuildsAndProducesArmy_UnderTheSameRulesAsThePlayer()
        {
            Time.timeScale = 8f;
            var wallet = _match.GetFaction(Enemy).Wallet;
            int minCredits = int.MaxValue;
            int creditGains = 0, oreDelivered = 0, spentOnBuildings = 0;
            var seenRefineries = new HashSet<Refinery>();
            int last = wallet.Credits;
            wallet.Changed += c => { if (c > last) creditGains += c - last; last = c; };
            _placement.BuildingPlaced += b => { if (b.FactionId == Enemy) spentOnBuildings += b.Definition.Cost; };

            void TrackRefineries()
            {
                foreach (var r in Refinery.All)
                    if (r.FactionId == Enemy && seenRefineries.Add(r)) r.Delivered += ore => oreDelivered += ore;
                minCredits = Mathf.Min(minCredits, wallet.Credits);
            }

            yield return WaitFor(() => Tanks(Enemy) >= 2 && Structures(Enemy, "vehicle_factory").Count > 0, 480f, TrackRefineries);

            Assert.GreaterOrEqual(Structures(Enemy, "power_plant").Count, 1, "AI built a power plant");
            Assert.GreaterOrEqual(Structures(Enemy, "vehicle_factory").Count, 1, "AI built a factory");
            Assert.GreaterOrEqual(Tanks(Enemy), 2, "AI produced combat units");
            Assert.Greater(oreDelivered, 0, "AI harvested real ore");
            Assert.AreEqual(oreDelivered, creditGains, "Every credit the AI gained came from ore deliveries (no bonuses)");
            Assert.GreaterOrEqual(minCredits, 0);
            Assert.IsFalse(_match.GetFaction(Enemy).Power.IsLowPower && Structures(Enemy, "power_plant").Count == 0);

            // Same placement rules: no overlapping or out-of-bounds structures, power plant before the factory's drain.
            var all = Building.All.Where(b => b.Definition != null).ToList();
            var map = new Rect(-50f, -50f, 100f, 100f);
            foreach (var b in all)
            {
                var others = all.Where(o => o != b).Select(o => o.Footprint).ToList();
                var result = PlacementRules.Evaluate(b.Footprint, map, others, new Rect[0], new Rect[0], new Rect[0], 1.5f, 3f);
                Assert.AreEqual(PlacementResult.Valid, result, $"{b.name} at {b.transform.position} breaks placement rules");
            }
            Debug.Log($"[Test] AI economy: delivered {oreDelivered}, spent on buildings {spentOnBuildings}, " +
                      $"units {Units(Enemy).Count}, decisions {Ai().Decisions}\n" + string.Join("\n", Ai().Log.NewestFirst()));
        }

        // ---------- Faction isolation ----------

        [UnityTest, Timeout(600000)]
        public IEnumerator AiBuildings_AreNeverSeenAsThePlayers_AndThePlayerHudIgnoresThem()
        {
            // Every factory must already belong to its real owner when it announces itself.
            var seenAtEnable = new List<int>();
            void OnFactories()
            {
                foreach (var f in VehicleFactory.All) seenAtEnable.Add(f.FactionId);
            }
            VehicleFactory.FactoriesChanged += OnFactories;

            _match.GetFaction(Enemy).Wallet.Add(3000); // fixture: let the AI reach its factory quickly
            Time.timeScale = 8f;
            yield return WaitFor(() => Structures(Enemy, "vehicle_factory").Count > 0, 120f);
            VehicleFactory.FactoriesChanged -= OnFactories;
            yield return null;

            Assert.IsTrue(Structures(Enemy, "vehicle_factory").Count > 0, "AI built a factory");
            Assert.IsTrue(seenAtEnable.Count > 0 && seenAtEnable.All(f => f == Enemy), "Factory owner was correct from the first event");
            var hud = Object.FindAnyObjectByType<Ironfront.UI.EconomyHud>();
            Assert.IsNull(hud.ActiveFactory, "The player has no factory; the AI's must not appear in the player's sidebar");

            // Units the AI produces are its own from the start too.
            int enemyBefore = Units(Enemy).Count;
            yield return WaitFor(() => Units(Enemy).Count > enemyBefore, 120f);
            Assert.IsTrue(Units(Enemy).Count > enemyBefore, "AI produced a unit");
            Assert.AreEqual(0, Tanks(Player), "No unit ended up on the player's side");
        }

        // ---------- Defence ----------

        [UnityTest, Timeout(600000)]
        public IEnumerator Ai_DefendsItsBase_AgainstARaid()
        {
            var ai = Ai();
            var defenders = new[]
            {
                Spawn(_heavy, new Vector3(36f, 0f, 30f), Enemy),
                Spawn(_light, new Vector3(39f, 0f, 30f), Enemy),
                Spawn(_light, new Vector3(33f, 0f, 30f), Enemy),
            };
            var raiders = new[] { Spawn(_light, new Vector3(20f, 0f, 26f), Player), Spawn(_light, new Vector3(23f, 0f, 24f), Player) };
            yield return null;
            Time.timeScale = 4f;

            bool sawDefend = false;
            yield return WaitFor(() => raiders.All(r => r == null), 90f, () => sawDefend |= ai.State == StrategicState.Defend);

            Assert.IsTrue(sawDefend, "Commander entered Defend");
            Assert.IsTrue(raiders.All(r => r == null), "Raiders destroyed");
            Assert.IsTrue(ai.Log.NewestFirst().Any(l => l.Contains("Defend")), "Decision logged");
            Assert.IsTrue(Structures(Enemy, "headquarters").Count == 1);
        }

        // ---------- Attack (and the AI can win) ----------

        [UnityTest, Timeout(900000)]
        public IEnumerator Ai_AssemblesAttacksAndCanWin_ByDestroyingThePlayerHeadquarters()
        {
            var ai = Ai();
            // Test fixture: skip the build-up by handing the AI the credits it would earn, then an assembled army.
            _match.GetFaction(Enemy).Wallet.Add(4000);
            Time.timeScale = 8f;
            yield return WaitFor(() => Structures(Enemy, "vehicle_factory").Count > 0 && Structures(Enemy, "power_plant").Count > 0, 120f);
            Assert.IsTrue(Structures(Enemy, "vehicle_factory").Count > 0, "AI built its factory");

            var rally = ai.RallyPoint;
            for (int i = 0; i < 4; i++) Spawn(_heavy, rally + new Vector3(i * 3f - 4.5f, 0f, -3f), Enemy);
            for (int i = 0; i < 4; i++) Spawn(_light, rally + new Vector3(i * 3f - 4.5f, 0f, 3f), Enemy);

            yield return WaitFor(() => ai.AttacksLaunched > 0, 120f);
            Assert.Greater(ai.AttacksLaunched, 0, "Attack launched once the group was assembled");

            yield return WaitFor(() => _flow.IsOver, 600f);
            Assert.IsTrue(_flow.IsOver, "Match ended");
            Assert.AreEqual(MatchResult.Victory, _flow.Outcome.Result);
            Assert.AreEqual(Enemy, _flow.Outcome.WinnerFactionId, "The AI destroyed the undefended player HQ");
            Assert.IsTrue(ai.IsHalted, "Commander stopped at match end");
            Debug.Log($"[Test] AI attack won at {_flow.EndTime:0}s\n" + string.Join("\n", ai.Log.NewestFirst()));
        }

        // ---------- Recovery ----------

        [UnityTest, Timeout(900000)]
        public IEnumerator Ai_RecoversFromLosingHarvestersRefineryAndFactory()
        {
            var ai = Ai();
            _match.GetFaction(Enemy).Wallet.Add(10000); // fixture: enough to rebuild everything quickly
            Time.timeScale = 8f;
            yield return WaitFor(() => Structures(Enemy, "vehicle_factory").Count > 0, 120f);
            Assert.IsTrue(Structures(Enemy, "vehicle_factory").Count > 0);

            foreach (var h in Units(Enemy).Where(u => u.GetComponent<Harvester>() != null).ToList()) Kill(h, Player);
            yield return null;
            Assert.AreEqual(0, Harvesters(Enemy));
            yield return WaitFor(() => Harvesters(Enemy) > 0, 120f);
            Assert.Greater(Harvesters(Enemy), 0, "Replaced lost harvesters");

            foreach (var r in Structures(Enemy, "refinery")) Kill(r.GetComponent<Health>(), Player);
            yield return null;
            Assert.AreEqual(0, Structures(Enemy, "refinery").Count);
            yield return WaitFor(() => Structures(Enemy, "refinery").Count > 0, 120f);
            Assert.Greater(Structures(Enemy, "refinery").Count, 0, "Rebuilt a refinery");

            foreach (var f in Structures(Enemy, "vehicle_factory")) Kill(f.GetComponent<Health>(), Player);
            yield return null;
            yield return WaitFor(() => Structures(Enemy, "vehicle_factory").Count > 0, 120f);
            Assert.Greater(Structures(Enemy, "vehicle_factory").Count, 0, "Rebuilt the factory");
            Assert.GreaterOrEqual(_match.GetFaction(Enemy).Wallet.Credits, 0);
            Debug.Log("[Test] Recovery log:\n" + string.Join("\n", ai.Log.NewestFirst()));
        }

        // ---------- Match end & restart ----------

        [UnityTest]
        public IEnumerator DestroyingEnemyHq_IsVictory_StopsAi_AndRestartGivesAFreshMatch()
        {
            var ai = Ai();
            int healthCount = Health.All.Count;
            Kill(Structures(Enemy, "headquarters")[0].GetComponent<Health>(), Player);
            yield return null;
            yield return null;

            Assert.IsTrue(_flow.IsOver);
            Assert.AreEqual(MatchResult.Victory, _flow.Outcome.Result);
            Assert.AreEqual(Player, _flow.Outcome.WinnerFactionId);
            Assert.AreEqual(0f, Time.timeScale, "Simulation paused");
            var view = Object.FindAnyObjectByType<MatchResultView>();
            Assert.IsTrue(view.IsShown);
            Assert.AreEqual("VICTORY", view.TitleText);
            Assert.AreEqual(1, MatchStatisticsTracker.Current.Stats.For(Player).EnemyStructuresDestroyed, "the kill is credited");
            Assert.AreEqual(1, MatchStatisticsTracker.Current.Stats.For(Enemy).StructuresLost);
            StringAssert.Contains("Enemy structures destroyed", view.StatisticsText, "the result screen shows statistics");
            yield return null;
            Assert.IsTrue(ai.IsHalted, "No more strategic commands after the end");

            var oldFlow = _flow;
            RtsInputMode.IsTargeting = true; // simulate a mode left active at the end
            _flow.Restart();
            yield return null;
            yield return null;
            yield return null;

            var flow = MatchFlowController.Current;
            Assert.IsNotNull(flow);
            Assert.AreNotSame(oldFlow, flow);
            Assert.IsFalse(flow.IsOver);
            Assert.AreEqual(1f, Time.timeScale);
            Assert.IsFalse(RtsInputMode.IsTargeting || RtsInputMode.IsPlacingBuilding, "No stale input mode");
            Assert.AreEqual(1, Structures(Enemy, "headquarters").Count);
            Assert.AreEqual(2000, MatchController.Current.GetFaction(Enemy).Wallet.Credits);
            Assert.AreEqual(healthCount, Health.All.Count, "Fresh objects, nothing left over");
            Assert.AreEqual(1, EnemyCommander.All.Count);
            Assert.IsFalse(EnemyCommander.All[0].IsHalted);
            Assert.IsFalse(Object.FindAnyObjectByType<MatchResultView>().IsShown);
        }

        [UnityTest]
        public IEnumerator DestroyingPlayerHq_IsDefeat()
        {
            Kill(Structures(Player, "headquarters")[0].GetComponent<Health>(), Enemy);
            yield return null;
            yield return null;
            Assert.IsTrue(_flow.IsOver);
            Assert.AreEqual(Enemy, _flow.Outcome.WinnerFactionId);
            Assert.AreEqual("DEFEAT", Object.FindAnyObjectByType<MatchResultView>().TitleText);
        }

        [UnityTest]
        public IEnumerator BothHqsDestroyedInTheSameFrame_IsADraw()
        {
            var p = Structures(Player, "headquarters")[0].GetComponent<Health>();
            var e = Structures(Enemy, "headquarters")[0].GetComponent<Health>();
            Kill(p, Enemy);
            Kill(e, Player);
            yield return null;
            yield return null;
            Assert.IsTrue(_flow.IsOver);
            Assert.AreEqual(MatchResult.Draw, _flow.Outcome.Result);
            Assert.AreEqual("DRAW", Object.FindAnyObjectByType<MatchResultView>().TitleText);
        }

        // ---------- AI vs AI ----------

        [UnityTest, Timeout(1500000)]
        public IEnumerator AiVsAi_Simulation_PlaysAMatchUnderIdenticalRules()
        {
            var enemyAi = Ai();
            var playerAiGo = new GameObject("PlayerCommander(Test)");
            playerAiGo.SetActive(false);
            var playerAi = playerAiGo.AddComponent<EnemyCommander>();
            playerAi.Configure(Player, enemyAi.Difficulty, _placement);
            playerAiGo.SetActive(true);
            yield return null;

            Time.timeScale = 10f;
            var cfg = enemyAi.Difficulty.Strategy;
            int unitCap = cfg.MaxArmy + cfg.MaxHarvesters + cfg.MaxQueuedUnits * cfg.MaxFactories;
            var minCredits = new Dictionary<int, int> { { Player, int.MaxValue }, { Enemy, int.MaxValue } };
            var maxUnits = new Dictionary<int, int> { { Player, 0 }, { Enemy, 0 } };
            double unitSeconds = 0;
            long pathsAtStart = UnitMover.PathRequests;
            float simulated = 0f;

            yield return WaitFor(() => _flow.IsOver, 1500f, () =>
            {
                simulated += Time.deltaTime;
                foreach (int f in new[] { Player, Enemy })
                {
                    minCredits[f] = Mathf.Min(minCredits[f], _match.GetFaction(f).Wallet.Credits);
                    int n = Units(f).Count;
                    maxUnits[f] = Mathf.Max(maxUnits[f], n);
                    unitSeconds += n * Time.deltaTime;
                }
            });

            double pathRate = (UnitMover.PathRequests - pathsAtStart) / Math.Max(1.0, unitSeconds);
            string outcome = _flow.IsOver
                ? (_flow.Outcome.Result == MatchResult.Draw ? $"draw ({_flow.Outcome.Reason})" : $"faction {_flow.Outcome.WinnerFactionId} won")
                : "no winner within the time limit";
            string ore = string.Join(", ", OreDeposit.All.Select(d => $"{d.name} {d.Remaining}"));
            Debug.Log($"[Test] AI-vs-AI: {outcome} after {simulated:0}s game time. Ore left: {ore}. " +
                      $"Credits P{_match.GetFaction(Player).Wallet.Credits}/E{_match.GetFaction(Enemy).Wallet.Credits}, " +
                      $"units P{Units(Player).Count}/E{Units(Enemy).Count}, states P {playerAi.State}/E {enemyAi.State}. " +
                      $"Player AI: built {playerAi.BuildingsPlaced}, produced {playerAi.UnitsProduced}, attacks {playerAi.AttacksLaunched}, max units {maxUnits[Player]}. " +
                      $"Enemy AI: built {enemyAi.BuildingsPlaced}, produced {enemyAi.UnitsProduced}, attacks {enemyAi.AttacksLaunched}, max units {maxUnits[Enemy]}. " +
                      $"Path requests per unit-second {pathRate:0.00}\n--- Player AI log ---\n{string.Join("\n", playerAi.Log.NewestFirst())}" +
                      $"\n--- Enemy AI log ---\n{string.Join("\n", enemyAi.Log.NewestFirst())}");

            foreach (var ai in new[] { playerAi, enemyAi })
            {
                Assert.GreaterOrEqual(ai.BuildingsPlaced, 2, $"Faction {ai.FactionId} built its base");
                Assert.GreaterOrEqual(ai.UnitsProduced, 3, $"Faction {ai.FactionId} produced units");
            }
            Assert.Greater(playerAi.AttacksLaunched + enemyAi.AttacksLaunched, 0, "At least one attack was launched");
            Assert.IsTrue(_flow.IsOver, "The match reached an outcome (victory, or a draw once neither side could continue)");
            Assert.GreaterOrEqual(minCredits[Player], 0);
            Assert.GreaterOrEqual(minCredits[Enemy], 0);
            Assert.LessOrEqual(maxUnits[Player], unitCap, "No runaway production");
            Assert.LessOrEqual(maxUnits[Enemy], unitCap, "No runaway production");
            Assert.Less(pathRate, 3.0, "Path requests stay well below per-frame rates");
        }
    }
}
