using System.Collections.Generic;
using System.Linq;
using Ironfront.Core.AI;
using Ironfront.Core.Victory;
using NUnit.Framework;
using UnityEngine;

namespace Ironfront.Tests
{
    public class AiRulesTests
    {
        private static readonly CommanderSettings Cfg = CommanderSettings.Normal;

        private static readonly CostTable Cost = new CostTable
        {
            Refinery = 1000, PowerPlant = 500, Factory = 1300, Harvester = 800, LightTank = 500, HeavyTank = 900,
            RefineryDrain = 15, FactoryDrain = 30, PowerPlantOutput = 50,
        };

        /// <summary>The starting position of either faction in RTS_Match.</summary>
        private static CommanderSnapshot Start() => new CommanderSnapshot
        {
            Credits = 2000, PowerProduced = 20, PowerConsumed = 15, HasHeadquarters = true,
            Refineries = 1, Harvesters = 1, OreAvailable = true, OreNearRefinery = true,
        };

        private static CommanderSnapshot ReadyBase()
        {
            var s = Start();
            s.PowerProduced = 70;
            s.PowerConsumed = 45;
            s.PowerPlants = 1;
            s.Factories = 1;
            s.Harvesters = 2;
            return s;
        }

        // ---------- Strategic states ----------

        [Test]
        public void Threat_SwitchesToDefend_Immediately()
        {
            var s = ReadyBase();
            s.ThreatStrength = 1f;
            foreach (StrategicState state in System.Enum.GetValues(typeof(StrategicState)))
                Assert.AreEqual(StrategicState.Defend, StrategyRules.Next(state, 0f, s, Cfg), state.ToString());
        }

        [Test]
        public void MinStateDuration_PreventsFlipping()
        {
            var s = ReadyBase();
            Assert.AreEqual(StrategicState.Develop, StrategyRules.Next(StrategicState.Develop, Cfg.MinStateDuration - 0.1f, s, Cfg));
            Assert.AreEqual(StrategicState.Assemble, StrategyRules.Next(StrategicState.Develop, Cfg.MinStateDuration, s, Cfg));
        }

        [Test]
        public void Develop_StaysUntilBaseIsReady()
        {
            var s = Start();
            Assert.AreEqual(StrategicState.Develop, StrategyRules.Next(StrategicState.Develop, 100f, s, Cfg));
            s.PowerPlants = 1;
            Assert.AreEqual(StrategicState.Develop, StrategyRules.Next(StrategicState.Develop, 100f, s, Cfg), "No factory yet");
        }

        [Test]
        public void EconomyLoss_LeadsToRecover_AndBackOnceRebuilt()
        {
            var s = ReadyBase();
            s.Refineries = 0;
            Assert.AreEqual(StrategicState.Recover, StrategyRules.Next(StrategicState.Assemble, 100f, s, Cfg));
            Assert.AreEqual(StrategicState.Recover, StrategyRules.Next(StrategicState.Develop, 100f, s, Cfg));
            Assert.AreEqual(StrategicState.Recover, StrategyRules.Next(StrategicState.Recover, 100f, s, Cfg));

            s.Refineries = 1;
            Assert.AreEqual(StrategicState.Assemble, StrategyRules.Next(StrategicState.Recover, 100f, s, Cfg));

            var noHarvesters = ReadyBase();
            noHarvesters.Harvesters = 0;
            Assert.AreEqual(StrategicState.Recover, StrategyRules.Next(StrategicState.Assemble, 100f, noHarvesters, Cfg));
            noHarvesters.QueuedHarvesters = 1;
            Assert.AreEqual(StrategicState.Assemble, StrategyRules.Next(StrategicState.Assemble, 100f, noHarvesters, Cfg),
                "A harvester in production counts towards recovery");
        }

        [Test]
        public void Assemble_AttacksOnlyWithEnoughUnitsAndStrength()
        {
            var s = ReadyBase();
            s.LightTanks = Cfg.EffectiveAttackSize - 1;
            s.ArmyStrength = 20f;
            Assert.AreEqual(StrategicState.Assemble, StrategyRules.Next(StrategicState.Assemble, 100f, s, Cfg), "Too few units");

            s.LightTanks = Cfg.EffectiveAttackSize;
            s.ArmyStrength = Cfg.AttackStrengthThreshold - 0.5f;
            Assert.AreEqual(StrategicState.Assemble, StrategyRules.Next(StrategicState.Assemble, 100f, s, Cfg), "Too weak (damaged)");

            s.ArmyStrength = Cfg.AttackStrengthThreshold;
            Assert.AreEqual(StrategicState.Attack, StrategyRules.Next(StrategicState.Assemble, 100f, s, Cfg));
        }

        [Test]
        public void Attack_RetreatsWhenTheGroupIsMostlyLost()
        {
            var s = ReadyBase();
            s.AttackersAlive = 4;
            s.AttackStartStrength = 10f;
            s.AttackStrength = 4f;
            Assert.AreEqual(StrategicState.Attack, StrategyRules.Next(StrategicState.Attack, 100f, s, Cfg));
            s.AttackStrength = 3f;
            Assert.AreEqual(StrategicState.Recover, StrategyRules.Next(StrategicState.Attack, 100f, s, Cfg));
            s.AttackersAlive = 0;
            s.AttackStrength = 0f;
            Assert.AreEqual(StrategicState.Recover, StrategyRules.Next(StrategicState.Attack, 100f, s, Cfg));
        }

        [Test]
        public void Defend_HoldsWhileThreatened_ThenCalmsDownAfterMinDuration()
        {
            var s = ReadyBase();
            Assert.AreEqual(StrategicState.Defend, StrategyRules.Next(StrategicState.Defend, 1f, s, Cfg), "Just cleared: stay alert");
            Assert.AreEqual(StrategicState.Assemble, StrategyRules.Next(StrategicState.Defend, 100f, s, Cfg));
            s.Factories = 0;
            Assert.AreEqual(StrategicState.Develop, StrategyRules.Next(StrategicState.Defend, 100f, s, Cfg));
        }

        [Test]
        public void EndGame_NoOreLeft_CommitsWhateverArmyRemains()
        {
            var s = ReadyBase();
            s.OreAvailable = false;
            s.OreNearRefinery = false;
            s.LightTanks = 2;
            s.ArmyStrength = 2f;
            Assert.AreEqual(StrategicState.Attack, StrategyRules.Next(StrategicState.Assemble, 100f, s, Cfg), "Below the normal attack size");
            Assert.AreEqual(StrategicState.Attack, StrategyRules.Next(StrategicState.Recover, 100f, s, Cfg));
            Assert.AreEqual(StrategicState.Attack, StrategyRules.Next(StrategicState.Develop, 100f, s, Cfg));

            s.Refineries = 0; // broken economy that can no longer be repaired
            Assert.AreEqual(StrategicState.Attack, StrategyRules.Next(StrategicState.Recover, 100f, s, Cfg), "No pointless recovery");

            s.AttackersAlive = 2; s.AttackStartStrength = 10f; s.AttackStrength = 1f;
            Assert.AreEqual(StrategicState.Attack, StrategyRules.Next(StrategicState.Attack, 100f, s, Cfg), "No retreat with nothing to rebuild");

            s.LightTanks = 0; s.ArmyStrength = 0f;
            Assert.AreNotEqual(StrategicState.Attack, StrategyRules.Next(StrategicState.Recover, 100f, s, Cfg), "No army: nothing to commit");
        }

        [Test]
        public void NoHeadquarters_StateUnchanged()
        {
            var s = ReadyBase();
            s.HasHeadquarters = false;
            s.ThreatStrength = 5f;
            Assert.AreEqual(StrategicState.Assemble, StrategyRules.Next(StrategicState.Assemble, 100f, s, Cfg));
        }

        [Test]
        public void Aggressiveness_ScalesAttackSize()
        {
            var c = Cfg;
            c.Aggressiveness = 0f;
            Assert.AreEqual(9, c.EffectiveAttackSize);
            c.Aggressiveness = 0.5f;
            Assert.AreEqual(6, c.EffectiveAttackSize);
            c.Aggressiveness = 1f;
            Assert.AreEqual(3, c.EffectiveAttackSize);
        }

        // ---------- Economy planner ----------

        [Test]
        public void Planner_FollowsTheSameOpeningAsThePlayer()
        {
            var s = Start();
            Assert.AreEqual(AiActionType.BuildPowerPlant, EconomyPlanner.Next(s, StrategicState.Develop, Cfg, Cost).Type, "Spare power 5 < margin");

            s.PowerProduced = 70; s.PowerPlants = 1; s.Credits = 1500;
            Assert.AreEqual(AiActionType.BuildFactory, EconomyPlanner.Next(s, StrategicState.Develop, Cfg, Cost).Type);

            s.Factories = 1; s.PowerConsumed = 45; s.Credits = 900;
            Assert.AreEqual(AiActionType.ProduceHarvester, EconomyPlanner.Next(s, StrategicState.Develop, Cfg, Cost).Type);

            s.QueuedHarvesters = 1; s.Credits = Cost.LightTank + Cfg.CreditReserve;
            Assert.AreEqual(AiActionType.ProduceLightTank, EconomyPlanner.Next(s, StrategicState.Develop, Cfg, Cost).Type);
            s.Credits = Cost.LightTank + Cfg.CreditReserve - 1;
            Assert.AreEqual(AiActionType.SaveFor, EconomyPlanner.Next(s, StrategicState.Develop, Cfg, Cost).Type, "Reserve kept");
        }

        [Test]
        public void Planner_SavesForTopPriority_InsteadOfSpendingOnLowerOnes()
        {
            var s = ReadyBase();
            s.Refineries = 0;
            s.Credits = 900; // can afford tanks, not a refinery
            var a = EconomyPlanner.Next(s, StrategicState.Recover, Cfg, Cost);
            Assert.AreEqual(AiActionType.SaveFor, a.Type);
            Assert.AreEqual(AiActionType.BuildRefinery, a.Goal);
        }

        [Test]
        public void Planner_ReplacesLostHarvesters()
        {
            var s = ReadyBase();
            s.Harvesters = 0;
            s.Credits = 5000;
            Assert.AreEqual(AiActionType.ProduceHarvester, EconomyPlanner.Next(s, StrategicState.Recover, Cfg, Cost).Type);
            Assert.AreEqual(2, EconomyPlanner.DesiredHarvesters(s, Cfg));
        }

        [Test]
        public void Planner_RebuildsFactory_AndPowerFirst()
        {
            var s = ReadyBase();
            s.Factories = 0;
            s.PowerConsumed = 15;
            s.Credits = 5000;
            Assert.AreEqual(AiActionType.BuildFactory, EconomyPlanner.Next(s, StrategicState.Recover, Cfg, Cost).Type);

            s.PowerPlants = 0;
            s.PowerProduced = 20;
            Assert.AreEqual(AiActionType.BuildPowerPlant, EconomyPlanner.Next(s, StrategicState.Recover, Cfg, Cost).Type);
        }

        [Test]
        public void Planner_ExpandsWhenLocalOreIsExhausted_AndIgnoresRefineriesWithNoOreLeft()
        {
            var s = ReadyBase();
            s.LightTanks = 3;
            s.Credits = 1200;
            s.OreNearRefinery = false;
            Assert.AreEqual(AiActionType.BuildRefinery, EconomyPlanner.Next(s, StrategicState.Assemble, Cfg, Cost).Type);

            s.OreAvailable = false;
            s.Refineries = 0;
            var a = EconomyPlanner.Next(s, StrategicState.Recover, Cfg, Cost);
            Assert.AreNotEqual(AiActionType.BuildRefinery, a.Type, "No point building a refinery with no ore on the map");
        }

        [Test]
        public void Planner_ArmyCapAndQueueLimit_StopUnlimitedProduction()
        {
            var s = ReadyBase();
            s.Credits = 100000;
            s.LightTanks = Cfg.MaxArmy;
            s.Refineries = Cfg.MaxRefineries;
            s.Harvesters = Cfg.MaxHarvesters;
            s.Factories = Cfg.MaxFactories;
            Assert.AreEqual(AiActionType.None, EconomyPlanner.Next(s, StrategicState.Assemble, Cfg, Cost).Type);

            s.LightTanks = 0;
            s.QueuedLight = Cfg.MaxQueuedUnits * s.Factories;
            Assert.AreEqual(AiActionType.None, EconomyPlanner.Next(s, StrategicState.Assemble, Cfg, Cost).Type, "Queues full");
        }

        [Test]
        public void Planner_KeepsReserve_ExceptWhenDefending()
        {
            var s = ReadyBase();
            s.Credits = Cost.LightTank + Cfg.CreditReserve - 1;
            Assert.AreEqual(AiActionType.SaveFor, EconomyPlanner.Next(s, StrategicState.Assemble, Cfg, Cost).Type);
            Assert.AreEqual(AiActionType.ProduceLightTank, EconomyPlanner.Next(s, StrategicState.Defend, Cfg, Cost).Type);
        }

        [Test]
        public void Planner_DefendingPrioritisesUnitsOverExtraHarvesters()
        {
            var s = ReadyBase();
            s.Harvesters = 1;
            s.Credits = 900;
            Assert.AreEqual(AiActionType.ProduceHarvester, EconomyPlanner.Next(s, StrategicState.Assemble, Cfg, Cost).Type);
            var d = EconomyPlanner.Next(s, StrategicState.Defend, Cfg, Cost).Type;
            Assert.IsTrue(d == AiActionType.ProduceHeavyTank || d == AiActionType.ProduceLightTank, d.ToString());
        }

        [Test]
        public void Planner_NeverChoosesAnActionItCannotAfford()
        {
            var rng = new System.Random(42);
            for (int i = 0; i < 2000; i++)
            {
                var s = new CommanderSnapshot
                {
                    Credits = rng.Next(0, 6000),
                    PowerProduced = rng.Next(0, 150), PowerConsumed = rng.Next(0, 150),
                    HasHeadquarters = rng.Next(10) > 0,
                    Refineries = rng.Next(0, 3), PowerPlants = rng.Next(0, 3), Factories = rng.Next(0, 3),
                    Harvesters = rng.Next(0, 5), QueuedHarvesters = rng.Next(0, 2),
                    LightTanks = rng.Next(0, 10), HeavyTanks = rng.Next(0, 6), QueuedLight = rng.Next(0, 2), QueuedHeavy = rng.Next(0, 2),
                    ThreatStrength = rng.Next(3) == 0 ? 2f : 0f,
                    KnownEnemyHeavy = rng.Next(0, 5), KnownEnemyLight = rng.Next(0, 5),
                    OreAvailable = rng.Next(5) > 0, OreNearRefinery = rng.Next(3) > 0,
                };
                var state = (StrategicState)rng.Next(0, 5);
                var a = EconomyPlanner.Next(s, state, Cfg, Cost);
                int price = PriceOf(a.Type);
                Assert.LessOrEqual(price, s.Credits, $"{a} with {s.Credits} credits");
            }
        }

        private static int PriceOf(AiActionType t)
        {
            switch (t)
            {
                case AiActionType.BuildRefinery: return Cost.Refinery;
                case AiActionType.BuildPowerPlant: return Cost.PowerPlant;
                case AiActionType.BuildFactory: return Cost.Factory;
                case AiActionType.ProduceHarvester: return Cost.Harvester;
                case AiActionType.ProduceLightTank: return Cost.LightTank;
                case AiActionType.ProduceHeavyTank: return Cost.HeavyTank;
                default: return 0;
            }
        }

        // ---------- Composition & targets ----------

        [Test]
        public void Composition_CountersArmouredEnemies_AndFallsBackToLightWhenShortOfMoney()
        {
            var s = ReadyBase();
            s.Credits = 5000;
            s.LightTanks = 2;
            Assert.AreEqual(AiActionType.ProduceHeavyTank, CompositionRules.Choose(s, Cfg, Cost, false), "Below the base 40% heavy share");

            s.HeavyTanks = 2; s.LightTanks = 3; // 40%
            Assert.AreEqual(AiActionType.ProduceLightTank, CompositionRules.Choose(s, Cfg, Cost, false));
            s.KnownEnemyHeavy = 4; s.KnownEnemyLight = 1;
            Assert.AreEqual(AiActionType.ProduceHeavyTank, CompositionRules.Choose(s, Cfg, Cost, false), "Counter heavy enemies");

            s.Credits = 700;
            Assert.AreEqual(AiActionType.ProduceLightTank, CompositionRules.Choose(s, Cfg, Cost, true), "Heavy unaffordable");
            s.Credits = 100;
            Assert.AreEqual(AiActionType.None, CompositionRules.Choose(s, Cfg, Cost, true));
        }

        [Test]
        public void TargetPriorities_ProductionThenHeadquartersThenEconomy_NearestWinsTies()
        {
            float d = 30f;
            Assert.Greater(TargetPriorities.Score("vehicle_factory", d), TargetPriorities.Score("headquarters", d));
            Assert.Greater(TargetPriorities.Score("headquarters", d), TargetPriorities.Score("refinery", d));
            Assert.Greater(TargetPriorities.Score("refinery", d), TargetPriorities.Score("power_plant", d));
            Assert.Greater(TargetPriorities.Score("refinery", 5f), TargetPriorities.Score("vehicle_factory", 80f), "Much nearer target wins");
        }

        // ---------- Site search & log ----------

        [Test]
        public void SiteSearch_OrdersByPreferredDistance_WithinRadius_Deterministically()
        {
            var anchor = new Vector3(10f, 0f, 10f);
            var a = SiteSearch.Ranked(anchor, anchor + Vector3.forward, 10f, 20f, 2f);
            var b = SiteSearch.Ranked(anchor, anchor + Vector3.forward, 10f, 20f, 2f);
            CollectionAssert.AreEqual(a, b);
            Assert.IsTrue(a.All(p => (p - anchor).magnitude <= 20.001f));
            Assert.Less(Mathf.Abs((a[0] - anchor).magnitude - 10f), 2f);
            Assert.Greater(a[0].z, anchor.z, "Bias towards the requested direction");
            Assert.IsFalse(a.Contains(anchor), "Never the anchor itself");
        }

        [Test]
        public void SiteSearch_IsPointSymmetric_SoMirroredFactionsBuildMirroredBases()
        {
            var hq = new Vector3(-41f, 0f, -41f);
            var enemy = new Vector3(41f, 0f, 41f);
            foreach (var (pref, radius) in new[] { (11f, 24f), (14f, 26f), (10f, 18f) })
            {
                var mine = SiteSearch.Ranked(hq, enemy, pref, radius, 2f);
                var theirs = SiteSearch.Ranked(-hq, -enemy, pref, radius, 2f);
                Assert.AreEqual(mine.Count, theirs.Count);
                for (int i = 0; i < 40; i++)
                    Assert.Less((mine[i] + theirs[i]).magnitude, 1e-3f, $"Candidate {i}: {mine[i]} vs {theirs[i]}");
            }
        }

        [Test]
        public void BoundedLog_KeepsNewestEntries_UpToCapacity()
        {
            var log = new BoundedLog(3);
            for (int i = 1; i <= 5; i++) log.Add("e" + i);
            Assert.AreEqual(3, log.Count);
            CollectionAssert.AreEqual(new[] { "e5", "e4", "e3" }, log.NewestFirst().ToArray());
            log.Clear();
            Assert.AreEqual(0, log.Count);
        }

        [Test]
        public void ArmyStrength_WeightsHeavyAndHealth()
        {
            Assert.AreEqual(1f, ArmyMath.Strength(false, 1f));
            Assert.AreEqual(2f, ArmyMath.Strength(true, 1f));
            Assert.AreEqual(1f, ArmyMath.Strength(true, 0.5f));
            Assert.AreEqual(0f, ArmyMath.Strength(true, -1f));
        }

        // ---------- Victory ----------

        [Test]
        public void Victory_LastHeadquartersStandingWins()
        {
            Assert.AreEqual(MatchResult.Ongoing, MatchOutcomeRules.Evaluate(new List<(int, bool)> { (1, true), (2, true) }).Result);
            var o = MatchOutcomeRules.Evaluate(new List<(int, bool)> { (1, true), (2, false) });
            Assert.AreEqual(MatchResult.Victory, o.Result);
            Assert.AreEqual(1, o.WinnerFactionId);
            o = MatchOutcomeRules.Evaluate(new List<(int, bool)> { (1, false), (2, true) });
            Assert.AreEqual(2, o.WinnerFactionId);
        }

        [Test]
        public void Victory_SimultaneousDestruction_IsADraw()
        {
            var o = MatchOutcomeRules.Evaluate(new List<(int, bool)> { (1, false), (2, false) });
            Assert.AreEqual(MatchResult.Draw, o.Result);
            Assert.IsTrue(o.IsOver);
        }

        [Test]
        public void Exhaustion_DrawOnlyWhenNoFactionCanAct()
        {
            Assert.AreEqual(MatchResult.Ongoing, MatchOutcomeRules.EvaluateExhaustion(new List<bool> { false, true }).Result);
            var o = MatchOutcomeRules.EvaluateExhaustion(new List<bool> { false, false });
            Assert.AreEqual(MatchResult.Draw, o.Result);
            Assert.AreEqual(EndReason.Exhaustion, o.Reason);
            Assert.AreEqual(MatchResult.Ongoing, MatchOutcomeRules.EvaluateExhaustion(new List<bool> { false }).Result);
        }

        [Test]
        public void CanStillAct_CoversArmyMoneyCargoAndRebuilding()
        {
            // armed, queued, factory, credits, cheapest, ore, refinery, harvesters, cargo, refineryCost, harvesterCost
            Assert.IsTrue(MatchOutcomeRules.CanStillAct(1, 0, false, 0, 500, false, false, 0, 0, 1000, 800), "Has an army");
            Assert.IsTrue(MatchOutcomeRules.CanStillAct(0, 1, true, 0, 500, false, false, 0, 0, 1000, 800), "Unit in production");
            Assert.IsTrue(MatchOutcomeRules.CanStillAct(0, 0, true, 500, 500, false, false, 0, 0, 1000, 800), "Can buy a tank");
            Assert.IsFalse(MatchOutcomeRules.CanStillAct(0, 0, true, 499, 500, false, true, 2, 0, 1000, 800), "Broke, no ore");
            Assert.IsTrue(MatchOutcomeRules.CanStillAct(0, 0, true, 0, 500, false, true, 1, 50, 1000, 800), "Unbanked cargo");
            Assert.IsTrue(MatchOutcomeRules.CanStillAct(0, 0, false, 0, 500, true, true, 1, 0, 1000, 800), "Still harvesting");
            Assert.IsFalse(MatchOutcomeRules.CanStillAct(0, 0, false, 300, 500, true, true, 0, 0, 1000, 800), "Ore but no harvester or factory");
            Assert.IsTrue(MatchOutcomeRules.CanStillAct(0, 0, true, 800, 500, true, true, 0, 0, 1000, 800), "Can buy a harvester");
            Assert.IsFalse(MatchOutcomeRules.CanStillAct(0, 0, false, 499, 500, true, false, 1, 0, 1000, 800), "No refinery and cannot buy one");
        }

        [Test]
        public void Victory_NeedsAtLeastTwoCompetitors()
        {
            Assert.AreEqual(MatchResult.Ongoing, MatchOutcomeRules.Evaluate(new List<(int, bool)> { (1, true) }).Result);
            Assert.AreEqual(MatchResult.Ongoing, MatchOutcomeRules.Evaluate(new List<(int, bool)>()).Result);
        }
    }
}
