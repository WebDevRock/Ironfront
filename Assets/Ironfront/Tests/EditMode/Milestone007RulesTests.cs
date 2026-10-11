using System.Collections.Generic;
using Ironfront.Core.AI;
using Ironfront.Core.Combat;
using Ironfront.Core.Economy;
using Ironfront.Core.Research;
using Ironfront.Core.Vision;
using NUnit.Framework;
using UnityEngine;

namespace Ironfront.Tests
{
    /// <summary>Milestone 007 pure rules: visibility grid, research, weapon classes, scouting, AI expansion, min range and seeking.</summary>
    public class Milestone007RulesTests
    {
        // ---------- Visibility grid ----------

        private static VisibilityGrid Grid() => new VisibilityGrid(-50f, -50f, 100f, 100f, 1f, new[] { 1, 2 });

        [Test]
        public void Grid_StartsUnexplored_RevealMakesVisibleAndExplored()
        {
            var g = Grid();
            Assert.AreEqual(VisibilityGrid.Unexplored, g.StateAt(1, 0f, 0f));
            g.Reveal(1, 0f, 0f, 10f);
            Assert.IsTrue(g.IsVisible(1, 0f, 0f));
            Assert.IsTrue(g.IsVisible(1, 9f, 0f), "Inside the radius");
            Assert.IsFalse(g.IsVisible(1, 13f, 0f), "Outside the radius");
            Assert.IsFalse(g.IsVisible(1, 8f, 8f), "Circle, not square (11.3 m away)");
            Assert.IsTrue(g.IsExplored(1, 0f, 0f));
        }

        [Test]
        public void Grid_NewTick_KeepsExploredButClearsVisible()
        {
            var g = Grid();
            g.Reveal(1, 0f, 0f, 5f);
            g.BeginTick();
            Assert.AreEqual(VisibilityGrid.Explored, g.StateAt(1, 0f, 0f), "Remembered terrain is explored, not visible");
            Assert.IsFalse(g.IsVisible(1, 0f, 0f));
            g.Reveal(1, 0f, 0f, 5f);
            Assert.AreEqual(VisibilityGrid.Visible, g.StateAt(1, 0f, 0f));
        }

        [Test]
        public void Grid_FactionsAreIndependent()
        {
            var g = Grid();
            g.Reveal(1, 20f, 20f, 8f);
            Assert.IsTrue(g.IsVisible(1, 20f, 20f));
            Assert.IsFalse(g.IsExplored(2, 20f, 20f), "Faction 2 learns nothing from faction 1's sight");
            Assert.AreEqual(0f, g.ExploredFraction(2));
            Assert.Greater(g.ExploredFraction(1), 0f);
        }

        [Test]
        public void Grid_EdgeAndOutsideAreSafe()
        {
            var g = Grid();
            Assert.DoesNotThrow(() => g.Reveal(1, -49.5f, -49.5f, 20f));
            Assert.IsTrue(g.IsVisible(1, -49.5f, -49.5f));
            Assert.AreEqual(VisibilityGrid.Unexplored, g.StateAt(1, -60f, 0f), "Outside the map is never revealed");
            Assert.DoesNotThrow(() => g.Reveal(99, 0f, 0f, 5f), "Unknown faction is ignored");
        }

        [Test]
        public void Grid_AnyVisible_SeesStructureCorner()
        {
            var g = Grid();
            g.Reveal(1, 10f, 0f, 2f);
            Assert.IsFalse(g.IsVisible(1, 7f, 0f), "Centre is not visible");
            Assert.IsTrue(g.AnyVisible(1, 7f, 0f, 2.5f, 2.5f), "A footprint corner is");
        }

        // ---------- Research ----------

        private class Topic : IResearchTopic
        {
            public string Id { get; set; }
            public string DisplayName => Id;
            public int Cost { get; set; }
            public float ResearchTime { get; set; }
            public UpgradeEffect Effect { get; set; }
            public float Value { get; set; }
            public IReadOnlyList<string> RequiredResearch { get; set; } = new string[0];
        }

        private static readonly Topic Armour = new Topic { Id = "composite_armour", Cost = 1000, ResearchTime = 45f, Effect = UpgradeEffect.VehicleArmour, Value = 0.1f };
        private static readonly Topic Weapons = new Topic { Id = "enhanced_weapons", Cost = 1200, ResearchTime = 50f, Effect = UpgradeEffect.WeaponDamage, Value = 0.2f, RequiredResearch = new[] { "composite_armour" } };
        private static readonly Topic Harvest = new Topic { Id = "advanced_harvesting", Cost = 800, ResearchTime = 35f, Effect = UpgradeEffect.HarvestRate, Value = 0.4f };

        [Test]
        public void Research_ChargesTakesTime_AndApplies()
        {
            var r = new ResearchState();
            var w = new ResourceWallet(3000);
            string completed = null;
            r.Completed += id => completed = id;

            Assert.AreEqual(ResearchCheck.Ok, r.TryStart(Armour, true, w));
            Assert.AreEqual(2000, w.Credits, "Paid in full at start");
            r.Tick(44f, 1f);
            Assert.IsFalse(r.IsResearched(Armour.Id));
            Assert.AreEqual(44f / 45f, r.Progress01, 1e-4f);
            r.Tick(1.5f, 1f);
            Assert.IsTrue(r.IsResearched(Armour.Id));
            Assert.AreEqual(Armour.Id, completed);
            Assert.AreEqual(0.1f, r.ArmourBonus, 1e-5f);
            Assert.IsNull(r.Current);
        }

        [Test]
        public void Research_CannotBePurchasedTwice()
        {
            var r = new ResearchState();
            var w = new ResourceWallet(5000);
            r.TryStart(Armour, true, w);
            Assert.AreEqual(ResearchCheck.InProgress, r.TryStart(Armour, true, w));
            r.Tick(100f, 1f);
            Assert.AreEqual(ResearchCheck.AlreadyResearched, r.TryStart(Armour, true, w));
            Assert.AreEqual(4000, w.Credits, "Charged once");
            r.Complete(Armour);
            Assert.AreEqual(0.1f, r.ArmourBonus, 1e-5f, "Completing again does not stack");
        }

        [Test]
        public void Research_OneAtATime_CancelRefunds()
        {
            var r = new ResearchState();
            var w = new ResourceWallet(5000);
            r.TryStart(Harvest, true, w);
            Assert.AreEqual(ResearchCheck.Busy, r.TryStart(Armour, true, w));
            Assert.IsTrue(r.Cancel(w));
            Assert.AreEqual(5000, w.Credits, "Full refund");
            Assert.AreEqual(ResearchCheck.Ok, r.TryStart(Armour, true, w));
        }

        [Test]
        public void Research_RequiresFacilityPrerequisiteAndFunds()
        {
            var r = new ResearchState();
            Assert.AreEqual(ResearchCheck.MissingFacility, r.TryStart(Armour, false, new ResourceWallet(5000)));
            Assert.AreEqual(ResearchCheck.MissingPrerequisite, r.TryStart(Weapons, true, new ResourceWallet(5000)));
            var poor = new ResourceWallet(999);
            Assert.AreEqual(ResearchCheck.InsufficientFunds, r.TryStart(Armour, true, poor));
            Assert.AreEqual(999, poor.Credits);
            r.Complete(Armour);
            Assert.AreEqual(ResearchCheck.Ok, r.Check(Weapons, true, new ResourceWallet(5000)));
        }

        [Test]
        public void Research_SpeedScalesProgress_ZeroPauses()
        {
            var r = new ResearchState();
            r.TryStart(Harvest, true, new ResourceWallet(5000));
            r.Tick(35f, 0.5f);
            Assert.IsFalse(r.IsResearched(Harvest.Id), "Half speed on low power");
            r.Tick(100f, 0f);
            Assert.AreEqual(0.5f, r.Progress01, 1e-4f, "Paused without a facility");
            r.Tick(35.2f, 0.5f);
            Assert.IsTrue(r.IsResearched(Harvest.Id));
            Assert.AreEqual(1.4f, r.HarvestMultiplier, 1e-5f);
        }

        [Test]
        public void Research_NewStateHasNoEffects()
        {
            var first = new ResearchState();
            first.Complete(Armour);
            first.Complete(Harvest);
            var next = new ResearchState(); // a new match creates a new FactionState
            Assert.AreEqual(0f, next.ArmourBonus);
            Assert.AreEqual(1f, next.DamageMultiplier);
            Assert.AreEqual(1f, next.HarvestMultiplier);
            Assert.AreEqual(0, next.CompletedTopics.Count);
        }

        // ---------- Weapon classes ----------

        [Test]
        public void ClassMultipliers_ApplyPerArmourClass()
        {
            var rockets = new ClassMultipliers { VsLight = 0.7f, VsHeavy = 1f, VsStructure = 2f };
            Assert.AreEqual(28f, WeaponClassRules.RawDamage(40f, rockets, ArmourClass.Light), 1e-4f);
            Assert.AreEqual(80f, WeaponClassRules.RawDamage(40f, rockets, ArmourClass.Structure), 1e-4f);
            Assert.AreEqual(96f, WeaponClassRules.RawDamage(40f, rockets, ArmourClass.Structure, 1.2f), 1e-4f, "Weapons upgrade");
            Assert.AreEqual(25f, WeaponClassRules.RawDamage(25f, ClassMultipliers.One, ArmourClass.Heavy), 1e-4f);
        }

        [Test]
        public void Splash_FullInsideThenFallsOff()
        {
            Assert.AreEqual(1f, WeaponClassRules.SplashFactor(0f, 3f));
            Assert.AreEqual(1f, WeaponClassRules.SplashFactor(0.9f, 3f));
            Assert.AreEqual(0.25f, WeaponClassRules.SplashFactor(3f, 3f), 1e-4f);
            Assert.AreEqual(0f, WeaponClassRules.SplashFactor(3.1f, 3f));
            float mid = WeaponClassRules.SplashFactor(2f, 3f);
            Assert.Less(mid, 1f);
            Assert.Greater(mid, 0.25f);
        }

        [Test]
        public void FiringBand_RespectsMinimumRange()
        {
            Assert.IsFalse(WeaponClassRules.InFiringBand(5f, 9f, 28f));
            Assert.IsTrue(WeaponClassRules.InFiringBand(9f, 9f, 28f));
            Assert.IsTrue(WeaponClassRules.InFiringBand(28f, 9f, 28f));
            Assert.IsFalse(WeaponClassRules.InFiringBand(28.5f, 9f, 28f));
        }

        // ---------- Scouting ----------

        [Test]
        public void Scout_GoesToUnexplored_BiasedTowardsInterest()
        {
            var g = Grid();
            g.Reveal(1, -35f, -35f, 20f); // own base corner explored
            Assert.IsTrue(ScoutPlanner.NextWaypoint(g, 1, -35f, -35f, 35f, 35f, 10f, 0f, out float nx, out float nz));
            Assert.IsFalse(g.IsExplored(1, nx, nz), "Waypoint is unexplored");
            Assert.IsTrue(ScoutPlanner.NextWaypoint(g, 1, -35f, -35f, 35f, 35f, 10f, 0.9f, out float ix, out float iz));
            Assert.Greater(ix + iz, nx + nz, "Interest weight pulls the waypoint towards the enemy side");
            Assert.Greater(ix, 20f);
            Assert.Greater(iz, 20f);
        }

        [Test]
        public void Scout_AvoidsRecentPoint_AndStopsWhenAllExplored()
        {
            var g = Grid();
            ScoutPlanner.NextWaypoint(g, 1, 0f, 0f, 0f, 0f, 10f, 0f, out float x, out float z);
            ScoutPlanner.NextWaypoint(g, 1, 0f, 0f, 0f, 0f, 10f, 0f, out float ax, out float az, x, z, 5f);
            Assert.IsFalse(Mathf.Approximately(x, ax) && Mathf.Approximately(z, az), "Avoids the last assigned point");

            for (float px = -50f; px <= 50f; px += 10f)
                for (float pz = -50f; pz <= 50f; pz += 10f)
                    g.Reveal(1, px, pz, 10f);
            Assert.IsFalse(ScoutPlanner.NextWaypoint(g, 1, 0f, 0f, 0f, 0f, 10f, 0f, out _, out _));
        }

        // ---------- AI expansion and composition ----------

        private static readonly CommanderSettings Cfg = CommanderSettings.Normal;

        private static readonly CostTable OldCost = new CostTable
        {
            Refinery = 1000, PowerPlant = 500, Factory = 1300, Harvester = 800, LightTank = 500, HeavyTank = 900,
            RefineryDrain = 15, FactoryDrain = 30, PowerPlantOutput = 50,
        };

        private static CostTable NewCost()
        {
            var c = OldCost;
            c.Recon = 450;
            c.Artillery = 1100;
            c.ResearchCentre = 1200;
            c.GuardTower = 400;
            c.AtTurret = 900;
            return c;
        }

        private static CommanderSnapshot Base() => new CommanderSnapshot
        {
            Credits = 5000, PowerProduced = 120, PowerConsumed = 45, HasHeadquarters = true, Refineries = 1, PowerPlants = 2,
            Factories = 1, Harvesters = 2, OreAvailable = true, OreNearRefinery = true,
        };

        [Test]
        public void Expansion_KeepsOneRecon_OnlyWithNewContent()
        {
            var s = Base();
            s.LightTanks = 2;
            Assert.AreEqual(AiActionType.ProduceRecon, ExpansionRules.Next(s, StrategicState.Assemble, Cfg, NewCost()).Type);
            Assert.AreEqual(AiActionType.None, ExpansionRules.Next(s, StrategicState.Assemble, Cfg, OldCost).Type, "Older content: inactive");
            s.Recon = 1;
            Assert.AreNotEqual(AiActionType.ProduceRecon, ExpansionRules.Next(s, StrategicState.Assemble, Cfg, NewCost()).Type);
        }

        [Test]
        public void Expansion_BuildsDefencesAsCounters()
        {
            var s = Base();
            s.Recon = 1;
            s.KnownEnemyHeavy = 2;
            Assert.AreEqual(AiActionType.BuildAtTurret, ExpansionRules.Next(s, StrategicState.Assemble, Cfg, NewCost()).Type);
            s.AtTurrets = Cfg.MaxAtTurrets;
            s.RecentlyAttacked = true;
            Assert.AreEqual(AiActionType.BuildGuardTower, ExpansionRules.Next(s, StrategicState.Defend, Cfg, NewCost()).Type);
            s.GuardTowers = Cfg.MaxGuardTowers;
            Assert.AreEqual(AiActionType.None, ExpansionRules.Next(s, StrategicState.Defend, Cfg, NewCost()).Type, "Caps respected");
        }

        [Test]
        public void Expansion_TechAfterArmy_ThenResearch()
        {
            var s = Base();
            s.Recon = 1;
            s.GuardTowers = 1;
            s.LightTanks = Cfg.ResearchArmyThreshold - 1;
            Assert.AreNotEqual(AiActionType.BuildResearchCentre, ExpansionRules.Next(s, StrategicState.Assemble, Cfg, NewCost()).Type);
            s.LightTanks = Cfg.ResearchArmyThreshold;
            Assert.AreEqual(AiActionType.BuildResearchCentre, ExpansionRules.Next(s, StrategicState.Assemble, Cfg, NewCost()).Type);
            Assert.AreEqual(AiActionType.None, ExpansionRules.Next(s, StrategicState.Defend, Cfg, NewCost()).Type, "No tech while defending");

            s.ResearchCentres = 1;
            s.WantedResearch = "composite_armour";
            s.WantedResearchCost = 1000;
            var a = ExpansionRules.Next(s, StrategicState.Assemble, Cfg, NewCost());
            Assert.AreEqual(AiActionType.StartResearch, a.Type);
            Assert.AreEqual("composite_armour", a.Topic);
            s.ResearchBusy = true;
            Assert.AreEqual(AiActionType.None, ExpansionRules.Next(s, StrategicState.Assemble, Cfg, NewCost()).Type);
            s.ResearchBusy = false;
            s.Credits = 1000 + Cfg.CreditReserve - 1;
            Assert.AreEqual(AiActionType.None, ExpansionRules.Next(s, StrategicState.Assemble, Cfg, NewCost()).Type, "Keeps the reserve");
        }

        [Test]
        public void ChooseResearch_FollowsPrioritiesAndPrerequisites()
        {
            var options = new List<ExpansionRules.TopicOption>
            {
                new ExpansionRules.TopicOption { Id = "composite_armour", Cost = 1000, PrerequisitesMet = true },
                new ExpansionRules.TopicOption { Id = "enhanced_weapons", Cost = 1200, PrerequisitesMet = false },
                new ExpansionRules.TopicOption { Id = "advanced_harvesting", Cost = 800, PrerequisitesMet = true },
            };
            var s = Base();
            s.Harvesters = 1;
            Assert.AreEqual("composite_armour", ExpansionRules.ChooseResearch(s, options), "Small economy: armour first");
            s.Harvesters = 2;
            Assert.AreEqual("advanced_harvesting", ExpansionRules.ChooseResearch(s, options), "Working economy: harvesting first");

            options[0] = new ExpansionRules.TopicOption { Id = "composite_armour", Done = true, PrerequisitesMet = true };
            options[1] = new ExpansionRules.TopicOption { Id = "enhanced_weapons", PrerequisitesMet = true };
            options[2] = new ExpansionRules.TopicOption { Id = "advanced_harvesting", Done = true, PrerequisitesMet = true };
            Assert.AreEqual("enhanced_weapons", ExpansionRules.ChooseResearch(s, options));
            options[1] = new ExpansionRules.TopicOption { Id = "enhanced_weapons", Done = true, PrerequisitesMet = true };
            Assert.IsNull(ExpansionRules.ChooseResearch(s, options));
        }

        [Test]
        public void Composition_SavesForHeavy_OnlyWithNewContent()
        {
            var s = Base();
            s.LightTanks = 4;
            s.Credits = 1000; // light affordable, heavy (900 + 300 reserve) not
            Assert.AreEqual(AiActionType.SaveFor, CompositionRules.Choose(s, Cfg, NewCost(), ignoreReserve: false));
            Assert.AreEqual(AiActionType.ProduceLightTank, CompositionRules.Choose(s, Cfg, OldCost, ignoreReserve: false), "M006 behaviour kept");
            Assert.AreEqual(AiActionType.ProduceHeavyTank, CompositionRules.Choose(s, Cfg, NewCost(), ignoreReserve: true), "Emergency: buy now");
        }

        [Test]
        public void Composition_ArtilleryNeedsResearchCentreAndReason()
        {
            var s = Base();
            s.LightTanks = 3;
            s.HeavyTanks = 2;
            s.Credits = 5000;
            Assert.IsFalse(CompositionRules.WantArtillery(s, Cfg, NewCost()), "No Research Centre");
            s.ResearchCentres = 1;
            Assert.IsFalse(CompositionRules.WantArtillery(s, Cfg, NewCost()), "Small army, no known defences");
            s.KnownEnemyDefences = 1;
            Assert.IsTrue(CompositionRules.WantArtillery(s, Cfg, NewCost()));
            Assert.AreEqual(AiActionType.ProduceArtillery, CompositionRules.Choose(s, Cfg, NewCost(), false));
            s.Artillery = 2;
            Assert.IsFalse(CompositionRules.WantArtillery(s, Cfg, NewCost()), "Share reached");
        }

        [Test]
        public void EndGame_NoOre_SpendsEverythingOnUnits()
        {
            var s = Base();
            s.LightTanks = 2;
            s.Recon = 1;
            s.OreAvailable = false;
            s.Credits = 650; // a light tank, but below the reserve
            Assert.AreEqual(AiActionType.None, ExpansionRules.Next(s, StrategicState.Assemble, Cfg, NewCost()).Type, "no new investments");
            Assert.AreEqual(AiActionType.ProduceLightTank, CompositionRules.Choose(s, Cfg, NewCost(), ignoreReserve: false),
                "no saving and no reserve once income has ended");
            s.Credits = 463; // not a light tank, but a recon
            Assert.AreEqual(AiActionType.ProduceRecon, CompositionRules.Choose(s, Cfg, NewCost(), ignoreReserve: false));
            s.Credits = 650;
            s.OreAvailable = true;
            Assert.AreNotEqual(AiActionType.ProduceLightTank, CompositionRules.Choose(s, Cfg, NewCost(), ignoreReserve: false));
        }

        [Test]
        public void Composition_FewerHeaviesAgainstArtillery()
        {
            var s = Base();
            Assert.AreEqual(CompositionRules.BaseHeavyShare, CompositionRules.DesiredHeavyShare(s));
            s.KnownEnemyArtillery = 1;
            Assert.AreEqual(CompositionRules.AntiArtilleryHeavyShare, CompositionRules.DesiredHeavyShare(s));
            Assert.Less(CompositionRules.AntiArtilleryHeavyShare, CompositionRules.BaseHeavyShare);
        }

        // ---------- Minimum range and seeking (combat machine) ----------

        private class T : ICombatTarget
        {
            public Vector3 Pos;
            public bool Alive = true;
            public bool IsAlive => Alive;
            public Vector3 Position => Pos;
            public string DisplayName => "t";
        }

        private class Body : ICombatBody, ITargetAwareness
        {
            public Vector3 Pos;
            public Vector3? Dest;
            public bool Sees = true;
            public int Shots;
            public readonly List<T> Enemies = new List<T>();
            private readonly WeaponCooldown _cd = new WeaponCooldown(1f);

            public Vector3 Position => Pos;
            public float DistanceTo(ICombatTarget t) => Vector3.Distance(Pos, t.Position);
            public bool HasLineOfSight(ICombatTarget t) => true;
            public bool CanSee(ICombatTarget t) => Sees;

            public ICombatTarget FindTarget(float radius, ICombatTarget ignore)
            {
                foreach (var e in Enemies)
                    if (Sees && e.Alive && e != ignore && Vector3.Distance(Pos, e.Pos) <= radius) return e;
                return null;
            }

            public void MoveTo(Vector3 d) => Dest = d;
            public void ChaseTo(ICombatTarget t) => Dest = t.Position;
            public void Halt() => Dest = null;
            public bool HasArrived => Dest == null || Vector3.Distance(Pos, Dest.Value) < 0.05f;

            public bool TryFire(ICombatTarget t)
            {
                if (!_cd.TryFire()) return false;
                Shots++;
                return true;
            }

            public void Step(float dt)
            {
                _cd.Tick(dt);
                if (Dest != null) Pos = Vector3.MoveTowards(Pos, Dest.Value, 6f * dt);
            }
        }

        private static void Run(CombatOrderMachine m, Body b, float seconds)
        {
            for (float t = 0f; t < seconds; t += 0.1f)
            {
                m.Tick(0.1f, b);
                b.Step(0.1f);
            }
        }

        private static CombatOrderMachine Artillery() =>
            new CombatOrderMachine(CombatSettings.ForWeapon(28f, 28f, requiresLineOfSight: false, minRange: 9f));

        [Test]
        public void MinRange_BacksOffThenFires()
        {
            var b = new Body();
            var enemy = new T { Pos = new Vector3(4f, 0f, 0f) };
            var m = Artillery();
            m.OrderAttack(b, enemy);
            Run(m, b, 0.5f);
            Assert.AreEqual(0, b.Shots, "Cannot fire inside the minimum range");
            Assert.Less(b.Pos.x, 0f, "Moves away from the target instead");
            Run(m, b, 5f);
            Assert.GreaterOrEqual(Vector3.Distance(b.Pos, enemy.Pos), 9f);
            Assert.Greater(b.Shots, 0, "Fires once outside the minimum range");
        }

        [Test]
        public void MinRange_IdleIgnoresTargetTooClose_AndHoldDoesNotMove()
        {
            var b = new Body();
            b.Enemies.Add(new T { Pos = new Vector3(5f, 0f, 0f) });
            var m = Artillery();
            Run(m, b, 2f);
            Assert.AreEqual(0, b.Shots);
            Assert.IsNull(m.Target, "Auto-acquisition skips targets inside the minimum range");

            var hold = new Body();
            var close = new T { Pos = new Vector3(5f, 0f, 0f) };
            var h = Artillery();
            h.OrderHold(hold);
            h.OrderAttack(hold, close);
            h.OrderHold(hold);
            Run(h, hold, 2f);
            Assert.AreEqual(Vector3.zero, hold.Pos, "Hold never moves");
            Assert.AreEqual(0, hold.Shots);
        }

        [Test]
        public void Seeking_HeadsToLastKnownPosition_NotTheHiddenTarget()
        {
            var b = new Body();
            var enemy = new T { Pos = new Vector3(20f, 0f, 0f) };
            var m = new CombatOrderMachine(CombatSettings.ForWeapon(10f, 14f, requiresLineOfSight: true));
            m.OrderAttack(b, enemy);
            m.Tick(0.1f, b);

            b.Sees = false;
            enemy.Pos = new Vector3(20f, 0f, 30f); // moves away while hidden
            Run(m, b, 2f);
            Assert.IsTrue(m.IsSeeking, "Explicit attack keeps looking");
            Assert.IsNotNull(b.Dest);
            Assert.AreEqual(20f, b.Dest.Value.x, 0.01f);
            Assert.AreEqual(0f, b.Dest.Value.z, 0.01f, "Last known position, never the real one");

            Run(m, b, 6f);
            Assert.AreEqual(CombatOrderType.Idle, m.Order, "Gives up on arrival");
            Assert.IsNull(m.Target);
            Assert.AreEqual(0, b.Shots);
        }

        [Test]
        public void Seeking_ReengagesWhenSeenAgain()
        {
            var b = new Body();
            var enemy = new T { Pos = new Vector3(25f, 0f, 0f) };
            var m = new CombatOrderMachine(CombatSettings.ForWeapon(10f, 14f, requiresLineOfSight: true));
            m.OrderAttack(b, enemy);
            m.Tick(0.1f, b);
            b.Sees = false;
            Run(m, b, 2f);
            Assert.IsTrue(m.IsSeeking);
            b.Sees = true;
            Run(m, b, 5f);
            Assert.IsFalse(m.IsSeeking);
            Assert.AreSame(enemy, m.Target);
            Assert.Greater(b.Shots, 0);
        }
    }
}
