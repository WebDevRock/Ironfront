using System.Collections.Generic;
using Ironfront.Core.AI;
using Ironfront.Core.Construction;
using Ironfront.Core.Economy;
using Ironfront.Core.Territory;
using NUnit.Framework;
using UnityEngine;

namespace Ironfront.Tests
{
    /// <summary>
    /// Milestone 010 pure rules: build range from every structure (edge-to-edge gap, creeping bases, ownership, known-only
    /// exclusion, removal), sale refunds and the sale state machine, the MCV deployment state machine and the computer's
    /// forward-base and selling rules.
    /// </summary>
    public class Milestone010TerritoryTests
    {
        private static Rect At(float cx, float cz, float w, float h) => new Rect(cx - w * 0.5f, cz - h * 0.5f, w, h);

        // ---------- Territory: build range from every structure ----------

        private static TerritoryAnchor Building(int id, int faction, float cx, float cz, float size = 8f, float range = 10f, float exclusion = 0f) =>
            new TerritoryAnchor(id, faction, At(cx, cz, size, size), range, exclusion);

        [Test]
        public void BuildRange_IsMeasuredFromTheNearestStructuresEdge_NotAFixedCentre()
        {
            var map = new TerritoryMap();
            map.Add(Building(1, 1, 0f, 0f)); // 8 x 8 at the origin: edges at ±4
            Assert.IsTrue(map.Covers(1, At(4f + 1.5f + 3f, 0f, 6f, 6f)), "1.5 m gap: inside");
            Assert.IsTrue(map.Covers(1, At(4f + 10f + 3f, 0f, 6f, 6f)), "exactly 10 m gap: inside");
            Assert.IsFalse(map.Covers(1, At(4f + 10.5f + 3f, 0f, 6f, 6f)), "10.5 m gap: outside");
            Assert.AreEqual(0.5f, map.Gap(1, At(4f + 10.5f + 3f, 0f, 6f, 6f), out var nearest), 1e-4f);
            Assert.AreEqual(1, nearest.Id);
            Assert.IsFalse(map.Covers(1, At(40f, 0f, 6f, 6f)), "far away");
            // Diagonal: the gap is measured corner to corner.
            Assert.IsTrue(map.Covers(1, At(4f + 6f + 3f, 4f + 6f + 3f, 6f, 6f)), "8.5 m corner gap");
            Assert.IsFalse(map.Covers(1, At(4f + 8f + 3f, 4f + 8f + 3f, 6f, 6f)), "11.3 m corner gap");
        }

        [Test]
        public void BuildRange_GrowsWithEveryStructure_SoBasesCanCreep()
        {
            var map = new TerritoryMap();
            map.Add(Building(1, 1, 0f, 0f));
            var far = At(31f, 0f, 6f, 6f); // 24 m from the first structure
            Assert.IsFalse(map.Covers(1, far));
            map.Add(Building(2, 1, 16f, 0f, 6f)); // a power plant 9 m from the first structure
            Assert.IsTrue(map.Covers(1, far), "the new structure extends the area (9 m gap to it)");
            Assert.IsTrue(map.HasTerritory(1));
            var cover = new List<TerritoryAnchor>();
            map.CopyAnchors(1, cover);
            Assert.AreEqual(2, cover.Count, "ordinary structures are anchors too");
        }

        [Test]
        public void BuildRange_IsNeverSharedBetweenFactions()
        {
            var map = new TerritoryMap();
            map.Add(Building(1, 2, 0f, 0f, 8f, 10f, 40f));
            Assert.IsFalse(map.Covers(1, At(10f, 0f, 4f, 4f)), "next to an enemy structure is not our territory");
            Assert.IsTrue(map.Covers(2, At(10f, 0f, 4f, 4f)));
            Assert.IsFalse(map.HasTerritory(1));
        }

        [Test]
        public void Exclusion_AppliesToKnownEnemyCommandStructuresOnly()
        {
            var map = new TerritoryMap();
            map.Add(Building(7, 2, 100f, 0f, 9f, 10f, 50f));
            map.Add(Building(8, 1, 0f, 0f, 8f, 10f, 70f));
            var near = At(55f, 0f, 9f, 9f);  // 40.5 m from the enemy centre: inside its 50 m zone
            Assert.IsTrue(map.Excluded(1, near, null, out var blocker));
            Assert.AreEqual(7, blocker.Id);
            Assert.IsFalse(map.Excluded(1, near, a => false, out _), "an unseen structure excludes nothing (no information leak)");
            Assert.IsFalse(map.Excluded(1, At(30f, 0f, 9f, 9f), null, out _), "own anchors never exclude");
            Assert.IsFalse(map.Excluded(1, At(40f, 0f, 9f, 9f), null, out _), "55.5 m away: outside the zone");
            Assert.AreEqual(0f, TerritoryMap.DistanceToRect(new Vector2(1f, 1f), At(0f, 0f, 4f, 4f)));
            Assert.AreEqual(3f, TerritoryMap.DistanceToRect(new Vector2(5f, 0f), At(0f, 0f, 4f, 4f)), 1e-4f);
            Assert.AreEqual(5f, TerritoryMap.RectDistance(At(0f, 0f, 4f, 4f), At(0f, 9f, 4f, 4f)), 1e-4f);
        }

        [Test]
        public void BuildRange_ShrinksWhenAStructureIsRemoved_AndEveryChangeIsVersioned()
        {
            var map = new TerritoryMap();
            var changed = new List<int>();
            map.Changed += f => changed.Add(f);
            map.Add(Building(1, 1, 0f, 0f));
            map.Add(Building(2, 1, 120f, 0f, 9f));
            int v = map.Version;
            Assert.IsTrue(map.Covers(1, At(132f, 0f, 6f, 6f)));
            Assert.IsTrue(map.Remove(2));
            Assert.IsFalse(map.Covers(1, At(132f, 0f, 6f, 6f)), "the area around the removed structure disappears");
            Assert.IsTrue(map.Covers(1, At(12f, 0f, 6f, 6f)), "the rest is unaffected");
            Assert.Greater(map.Version, v);
            Assert.IsFalse(map.Remove(2), "removing twice is a no-op");
            CollectionAssert.AreEqual(new[] { 1, 1, 1 }, changed);
            map.Add(new TerritoryAnchor(3, 1, At(0f, 0f, 4f, 4f), 0f, 0f));
            Assert.IsFalse(map.Contains(3), "an anchor without range or exclusion is ignored");
            var outline = TerritoryMap.Outline(At(0f, 0f, 8f, 8f), 10f, 1f);
            Assert.Greater(outline.Count, 40);
            foreach (var p in outline)
                Assert.AreEqual(10f, TerritoryMap.DistanceToRect(p, At(0f, 0f, 8f, 8f)), 0.05f, "outline lies exactly at the range");
        }

        // ---------- Sales ----------

        [Test]
        public void Refund_IsHalfTheValueScaledByHealth_Deterministic()
        {
            Assert.AreEqual(500, SaleRules.Refund(1000, 1000f, 1000f));
            Assert.AreEqual(250, SaleRules.Refund(1000, 500f, 1000f));
            Assert.AreEqual(650, SaleRules.Refund(1300, 1500f, 1500f));
            Assert.AreEqual(144, SaleRules.Refund(1300, 333f, 1500f), "floor(1300 x 0.5 x 0.222)");
            Assert.AreEqual(2500, SaleRules.Refund(5000, 2500f, 2500f), "forward command centre: half the MCV price");
            Assert.AreEqual(0, SaleRules.Refund(1000, 0f, 1000f), "destroyed: nothing");
            Assert.AreEqual(0, SaleRules.Refund(0, 2000f, 2000f), "unpriced (HQ): nothing");
            Assert.AreEqual(500, SaleRules.Refund(1000, 1200f, 1000f), "never above the maximum share");
            Assert.AreEqual(SaleRules.Refund(1000, 377f, 1000f), SaleRules.Refund(1000, 377f, 1000f));
        }

        [Test]
        public void Sale_RefundsExactlyOnce_RefusesDuplicates_AndCanBeCancelled()
        {
            var sale = new SaleProcess(8f);
            int paid = 0, calls = 0;
            Assert.IsTrue(sale.Begin(alive: true));
            Assert.IsFalse(sale.Begin(alive: true), "a second request while dismantling is refused");
            Assert.IsFalse(sale.Tick(7.9f, true, () => 500, a => { paid += a; calls++; }));
            Assert.AreEqual(0, calls, "nothing before the duration");
            Assert.IsTrue(sale.Tick(0.2f, true, () => 500, a => { paid += a; calls++; }));
            Assert.AreEqual(SaleState.Sold, sale.State);
            for (int i = 0; i < 5; i++) sale.Tick(10f, true, () => 500, a => { paid += a; calls++; });
            Assert.AreEqual(1, calls);
            Assert.AreEqual(500, paid);
            Assert.IsFalse(sale.Begin(true), "a sold structure cannot be sold again");

            var cancelled = new SaleProcess(8f);
            cancelled.Begin(true);
            cancelled.Tick(5f, true, () => 500, a => paid += a);
            Assert.IsTrue(cancelled.Cancel());
            Assert.AreEqual(SaleState.Idle, cancelled.State);
            Assert.AreEqual(0f, cancelled.Progress01);
            Assert.IsFalse(cancelled.Tick(10f, true, () => 500, a => paid += a), "a cancelled sale pays nothing");
            Assert.AreEqual(500, paid);
        }

        [Test]
        public void Sale_DestroyedBuildings_NeverRefund()
        {
            var sale = new SaleProcess(8f);
            Assert.IsFalse(sale.Begin(alive: false), "cannot start selling a destroyed structure");
            sale.Begin(true);
            int paid = 0;
            sale.Tick(4f, true, () => 500, a => paid += a);
            Assert.IsFalse(sale.Tick(10f, alive: false, () => 500, a => paid += a), "destroyed while dismantling");
            Assert.AreEqual(0, paid);
            Assert.AreNotEqual(SaleState.Sold, sale.State);
        }

        // ---------- Deployment ----------

        [Test]
        public void Deploy_InvalidSite_IsRefused_ValidSite_ConvertsExactlyOnce()
        {
            var d = new DeployProcess(15f);
            Assert.AreEqual(DeployCheck.EnemyTerritory, d.Begin(true, DeployCheck.EnemyTerritory));
            Assert.AreEqual(DeployState.Mobile, d.State);
            Assert.AreEqual(DeployCheck.Valid, d.Begin(true, DeployCheck.Valid));
            Assert.AreEqual(DeployState.Deploying, d.State);
            Assert.AreEqual(DeployCheck.Busy, d.Begin(true, DeployCheck.Valid), "no second deployment");
            int converted = 0;
            Assert.IsFalse(d.Tick(14f, true, () => DeployCheck.Valid, () => { converted++; return true; }));
            Assert.AreEqual(14f / 15f, d.Progress01, 1e-4f);
            Assert.IsTrue(d.Tick(1.1f, true, () => DeployCheck.Valid, () => { converted++; return true; }));
            for (int i = 0; i < 3; i++) d.Tick(20f, true, () => DeployCheck.Valid, () => { converted++; return true; });
            Assert.AreEqual(1, converted, "conversion happens once");
            Assert.AreEqual(DeployState.Converted, d.State);
            Assert.AreEqual(DeployCheck.Busy, d.Begin(true, DeployCheck.Valid), "a converted vehicle cannot deploy again");
        }

        [Test]
        public void Deploy_Cancel_FinalCheckFailure_AndDestruction_LeaveTheVehicleMobileWithoutAStructure()
        {
            int converted = 0;
            var cancel = new DeployProcess(15f);
            cancel.Begin(true, DeployCheck.Valid);
            cancel.Tick(5f, true, null, () => { converted++; return true; });
            Assert.IsTrue(cancel.Cancel());
            Assert.AreEqual(DeployState.Mobile, cancel.State);
            Assert.AreEqual(0f, cancel.Progress01);

            var blocked = new DeployProcess(15f);
            blocked.Begin(true, DeployCheck.Valid);
            Assert.IsFalse(blocked.Tick(16f, true, () => DeployCheck.OverlapsStructure, () => { converted++; return true; }));
            Assert.AreEqual(DeployState.Mobile, blocked.State, "the site became invalid: back to mobile");
            Assert.AreEqual(DeployCheck.OverlapsStructure, blocked.LastFailure);

            var killed = new DeployProcess(15f);
            killed.Begin(true, DeployCheck.Valid);
            Assert.IsFalse(killed.Tick(16f, alive: false, () => DeployCheck.Valid, () => { converted++; return true; }));
            Assert.AreEqual(DeployCheck.Destroyed, killed.Begin(false, DeployCheck.Valid));

            var failed = new DeployProcess(15f);
            failed.Begin(true, DeployCheck.Valid);
            Assert.IsFalse(failed.Tick(16f, true, () => DeployCheck.Valid, () => false), "placement refused at the end");
            Assert.AreEqual(DeployState.Mobile, failed.State);
            Assert.AreEqual(0, converted);
            Assert.AreEqual(DeployCheck.UnitsInTheWay, DeployProcess.FromPlacement(PlacementResult.OverlapsUnit));
            Assert.AreEqual("Inside enemy territory", DeployProcess.Describe(DeployCheck.EnemyTerritory));
        }

        // ---------- AI ----------

        private static CostTable Costs => new CostTable
        {
            Refinery = 1000, PowerPlant = 500, Factory = 1300, Harvester = 800, LightTank = 500, HeavyTank = 900,
            Recon = 400, Artillery = 1100, ResearchCentre = 1500, GuardTower = 600, AtTurret = 900, RepairPad = 1000, Mcv = 5000,
            RefineryDrain = 15, FactoryDrain = 30, PowerPlantOutput = 50,
        };

        private static CommanderSnapshot Expanding => new CommanderSnapshot
        {
            HasHeadquarters = true, OreAvailable = true, OreNearRefinery = false, ExpansionHaulDistance = 110f, Credits = 6000, Factories = 1, Refineries = 1, Harvesters = 2,
            PowerPlants = 2, PowerProduced = 200, LightTanks = 4, HeavyTanks = 3, Recon = 1, EnemyBaseKnown = true, GuardTowers = 2, AtTurrets = 0,
            ResearchCentres = 1, RepairPads = 1, ResearchBusy = true, MatchTime = 600f,
            RefineryBlockedByTerritory = true, ExpansionTargetKnown = true,
        };

        [Test]
        public void Ai_BuysAnMcv_OnlyWhenTheNextOreFieldIsOutsideItsTerritory()
        {
            var cfg = CommanderSettings.Normal;
            var s = Expanding;
            Assert.AreEqual(AiActionType.ProduceMcv, ForwardBaseRules.Next(s, StrategicState.Assemble, cfg, Costs).Type);
            Assert.AreEqual(AiActionType.ProduceMcv, EconomyPlanner.Next(s, StrategicState.Assemble, cfg, Costs).Type);

            var t = s; t.RefineryBlockedByTerritory = false;
            Assert.AreEqual(AiActionType.None, ForwardBaseRules.Next(t, StrategicState.Assemble, cfg, Costs).Type, "a field inside territory: build a refinery");
            t = s; t.ExpansionTargetKnown = false;
            Assert.AreEqual(AiActionType.None, ForwardBaseRules.Next(t, StrategicState.Assemble, cfg, Costs).Type, "no explored target: nothing to deploy at");
            t = s; t.Mcvs = 1;
            Assert.AreEqual(AiActionType.None, ForwardBaseRules.Next(t, StrategicState.Assemble, cfg, Costs).Type, "one vehicle at a time");
            t = s; t.QueuedMcv = 1;
            Assert.AreEqual(AiActionType.None, ForwardBaseRules.Next(t, StrategicState.Assemble, cfg, Costs).Type);
            t = s; t.ForwardBases = 2;
            Assert.AreEqual(AiActionType.None, ForwardBaseRules.Next(t, StrategicState.Assemble, cfg, Costs).Type, "Normal keeps two forward bases at most");
            Assert.AreEqual(AiActionType.ProduceMcv, ForwardBaseRules.Next(t, StrategicState.Assemble, CommanderSettings.Hard, Costs).Type, "Hard keeps three");
            t = s; t.McvLosses = cfg.MaxMcvLosses;
            Assert.AreEqual(AiActionType.None, ForwardBaseRules.Next(t, StrategicState.Assemble, cfg, Costs).Type, "stops after repeated losses");
            t = s; t.McvOnCooldown = true;
            Assert.AreEqual(AiActionType.None, ForwardBaseRules.Next(t, StrategicState.Assemble, cfg, Costs).Type);
            t = s; t.MatchTime = 100f;
            Assert.AreEqual(AiActionType.None, ForwardBaseRules.Next(t, StrategicState.Assemble, cfg, Costs).Type, "not in the opening");
            t = s; t.LightTanks = 1; t.HeavyTanks = 1;
            Assert.AreEqual(AiActionType.None, ForwardBaseRules.Next(t, StrategicState.Assemble, cfg, Costs).Type, "needs an escort");
            Assert.AreEqual(AiActionType.None, ForwardBaseRules.Next(s, StrategicState.Defend, cfg, Costs).Type, "never while defending");
            var noContent = Costs; noContent.Mcv = 0;
            Assert.AreEqual(AiActionType.None, ForwardBaseRules.Next(s, StrategicState.Assemble, cfg, noContent).Type, "older content: off");
        }

        [Test]
        public void Ai_SavesForAnMcv_WhenExpansionNeedsOne()
        {
            var cfg = CommanderSettings.Normal;
            var s = Expanding;
            s.Credits = 2000;
            var a = ForwardBaseRules.Next(s, StrategicState.Assemble, cfg, Costs);
            Assert.AreEqual(AiActionType.SaveFor, a.Type, "expansion depends on it: save rather than spend on the army");
            Assert.AreEqual(AiActionType.ProduceMcv, a.Goal);
            Assert.AreEqual(AiActionType.SaveFor, EconomyPlanner.Next(s, StrategicState.Assemble, cfg, Costs).Type);
            StringAssert.Contains("exhausted", a.Reason);
            s.Credits = 5000;
            Assert.AreEqual(AiActionType.ProduceMcv, ForwardBaseRules.Next(s, StrategicState.Assemble, cfg, Costs).Type);

            var shortHaul = Expanding;
            shortHaul.ExpansionHaulDistance = 40f; // harvesters can haul from there to an existing refinery
            Assert.AreEqual(AiActionType.None, ForwardBaseRules.Next(shortHaul, StrategicState.Assemble, cfg, Costs).Type, "no saving for a short haul");
            shortHaul.Credits = 7000;
            Assert.AreEqual(AiActionType.ProduceMcv, ForwardBaseRules.Next(shortHaul, StrategicState.Assemble, cfg, Costs).Type, "but a clear surplus expands");
            var localOre = Expanding;
            localOre.OreNearRefinery = true;
            localOre.Credits = 2000;
            Assert.AreEqual(AiActionType.None, ForwardBaseRules.Next(localOre, StrategicState.Assemble, cfg, Costs).Type, "while the home field lasts, surplus only");
        }

        [Test]
        public void Ai_BuildsTowardsANearbyField_InsteadOfBuyingAnMcv()
        {
            var cfg = CommanderSettings.Normal;
            var s = Expanding;
            s.CreepDistance = 30f; // an unclaimed field 30 m from the base: build towards it
            var a = EconomyPlanner.Next(s, StrategicState.Assemble, cfg, Costs);
            Assert.AreEqual(AiActionType.ExtendBuildRange, a.Type, a.ToString());
            Assert.IsTrue(a.IsConstruction);
            Assert.AreEqual(AiActionType.None, ForwardBaseRules.Next(s, StrategicState.Assemble, cfg, Costs).Type, "no MCV for a field in creeping distance");
            s.Credits = 600; // below the plant + reserve
            Assert.AreEqual(AiActionType.None, ForwardBaseRules.Creep(s, StrategicState.Assemble, cfg, Costs).Type, "cheap: never saved for");
            s = Expanding;
            s.CreepDistance = 30f;
            Assert.AreEqual(AiActionType.None, ForwardBaseRules.Creep(s, StrategicState.Defend, cfg, Costs).Type, "not while defending");
            s.RefineryBlockedByTerritory = false;
            Assert.AreEqual(AiActionType.None, ForwardBaseRules.Creep(s, StrategicState.Assemble, cfg, Costs).Type, "a refinery site is already in range");
            s = Expanding;
            s.CreepDistance = 80f; // too far to creep: the forward base rule takes over
            Assert.AreEqual(AiActionType.None, ForwardBaseRules.Creep(s, StrategicState.Assemble, cfg, Costs).Type);
            Assert.AreEqual(AiActionType.ProduceMcv, ForwardBaseRules.Next(s, StrategicState.Assemble, cfg, Costs).Type);
        }

        [Test]
        public void Ai_NeverPlansARefinery_ItsTerritoryCannotHold()
        {
            var cfg = CommanderSettings.Normal;
            var s = Expanding;
            s.ExpansionTargetKnown = false; // nowhere to deploy either
            s.Credits = 20000;
            var a = EconomyPlanner.Next(s, StrategicState.Assemble, cfg, Costs);
            Assert.AreNotEqual(AiActionType.BuildRefinery, a.Type);
            s.RefineryBlockedByTerritory = false;
            Assert.AreEqual(AiActionType.BuildRefinery, EconomyPlanner.Next(s, StrategicState.Assemble, cfg, Costs).Type, "the M009 rule otherwise");
        }

        [Test]
        public void Ai_DefendsItsForwardBase_AndChoosesSafeSites()
        {
            var cfg = CommanderSettings.Normal;
            var s = Expanding;
            s.RefineryBlockedByTerritory = false;
            s.ForwardBases = 1;
            s.UndefendedForwardBases = 1;
            var a = ExpansionRules.Next(s, StrategicState.Assemble, cfg, Costs);
            Assert.AreEqual(AiActionType.BuildGuardTower, a.Type, "a guard tower for the forward base, beyond the home cap");

            Assert.IsTrue(ForwardBaseRules.AcceptableSite(200f, 150f, float.MaxValue));
            Assert.IsFalse(ForwardBaseRules.AcceptableSite(60f, 150f, float.MaxValue), "too close to known enemy structures");
            Assert.IsFalse(ForwardBaseRules.AcceptableSite(200f, 250f, 200f), "on the enemy's side of the map");
            Assert.IsFalse(ForwardBaseRules.AcceptableSite(200f, float.MaxValue, float.MaxValue), "unreachable");
        }

        [Test]
        public void Ai_SellsObsoleteRefineries_AndForwardBasesAboutToFall()
        {
            Assert.IsTrue(SaleRules.AiShouldSellRefinery(oreNearby: false, refineries: 2));
            Assert.IsFalse(SaleRules.AiShouldSellRefinery(oreNearby: false, refineries: 1), "never its last refinery");
            Assert.IsFalse(SaleRules.AiShouldSellRefinery(oreNearby: true, refineries: 3));
            Assert.IsTrue(SaleRules.AiShouldSellForwardBase(0.2f, underAttack: true, ownArmyNearby: 0));
            Assert.IsFalse(SaleRules.AiShouldSellForwardBase(0.2f, underAttack: true, ownArmyNearby: 2), "defend it instead");
            Assert.IsFalse(SaleRules.AiShouldSellForwardBase(0.2f, underAttack: false, ownArmyNearby: 0), "repair it instead");
            Assert.IsFalse(SaleRules.AiShouldSellForwardBase(0.6f, underAttack: true, ownArmyNearby: 0));
            Assert.IsTrue(SaleRules.AiShouldSellObsoleteForwardBase(oreInTerritory: false, factoriesInTerritory: 0), "nothing left to protect");
            Assert.IsFalse(SaleRules.AiShouldSellObsoleteForwardBase(oreInTerritory: true, factoriesInTerritory: 0));
            Assert.IsFalse(SaleRules.AiShouldSellObsoleteForwardBase(oreInTerritory: false, factoriesInTerritory: 1), "a factory still needs it");
        }
    }
}
