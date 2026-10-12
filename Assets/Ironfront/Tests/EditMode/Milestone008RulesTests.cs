using System;
using System.Collections.Generic;
using System.Linq;
using Ironfront.Core.AI;
using Ironfront.Core.Maps;
using Ironfront.Core.Skirmish;
using Ironfront.Core.Victory;
using Ironfront.Data;
using Ironfront.EditorTools;
using Ironfront.Skirmish;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Ironfront.Tests
{
    /// <summary>
    /// Milestone 008 pure rules: skirmish settings and session, spawn-pair selection, enemy intelligence, difficulty
    /// presets, annihilation victory, and the map validator against synthetic grid maps (including broken ones).
    /// </summary>
    public class Milestone008RulesTests
    {
        // ---------- Settings and session ----------

        [Test]
        public void Settings_Defaults_AreSensible()
        {
            var s = SkirmishSettings.Default;
            Assert.AreEqual(SkirmishSettings.DefaultMapId, s.MapId);
            Assert.AreEqual(DifficultyLevel.Normal, s.Difficulty);
            Assert.AreEqual(StartingResources.Standard, s.Resources);
            Assert.AreEqual(SkirmishSettings.RandomStart, s.StartSlot);
            Assert.AreEqual(VictoryMode.Headquarters, s.Victory);
            Assert.AreEqual(2000, SkirmishSettings.CreditsFor(s.Resources));
            Assert.Less(SkirmishSettings.CreditsFor(StartingResources.Low), 2000);
            Assert.Greater(SkirmishSettings.CreditsFor(StartingResources.High), 2000);
        }

        [Test]
        public void Settings_Sanitised_RepairsInvalidValues()
        {
            var s = new SkirmishSettings { MapId = "nope", Difficulty = (DifficultyLevel)42, Resources = (StartingResources)9, Victory = (VictoryMode)7, StartSlot = -5 };
            var clean = s.Sanitised(id => id == "iron_pass", "iron_pass");
            Assert.AreEqual("iron_pass", clean.MapId);
            Assert.AreEqual(DifficultyLevel.Normal, clean.Difficulty);
            Assert.AreEqual(StartingResources.Standard, clean.Resources);
            Assert.AreEqual(VictoryMode.Headquarters, clean.Victory);
            Assert.AreEqual(SkirmishSettings.RandomStart, clean.StartSlot);
            Assert.AreEqual("nope", s.MapId, "the original is not modified");
        }

        [Test]
        public void Session_StoresCopies_AndDoesNotLeak()
        {
            SkirmishSession.Reset();
            try
            {
                Assert.IsNull(SkirmishSession.Active);
                var chosen = new SkirmishSettings { MapId = "iron_pass", Difficulty = DifficultyLevel.Hard };
                SkirmishSession.Select(chosen);
                chosen.Difficulty = DifficultyLevel.Easy; // the caller changing its object afterwards
                Assert.AreEqual(DifficultyLevel.Hard, SkirmishSession.Active.Difficulty);
                var active = SkirmishSession.Active;
                active.Difficulty = DifficultyLevel.Easy; // a consumer changing its copy
                Assert.AreEqual(DifficultyLevel.Hard, SkirmishSession.Active.Difficulty);

                Assert.AreEqual(DifficultyLevel.Hard, SkirmishSession.SettingsFor("iron_pass").Difficulty);
                LogAssertIgnore(() =>
                {
                    var other = SkirmishSession.SettingsFor("green_divide"); // settings were for another map
                    Assert.AreEqual(DifficultyLevel.Normal, other.Difficulty);
                    Assert.AreEqual("green_divide", other.MapId);
                });

                SkirmishSession.ClearActive();
                Assert.IsNull(SkirmishSession.Active);
                Assert.AreEqual(DifficultyLevel.Hard, SkirmishSession.LastChosen.Difficulty, "the menu remembers the last choice");
                SkirmishSession.Reset();
                Assert.AreEqual(SkirmishSettings.Default, SkirmishSession.LastChosen);
            }
            finally { SkirmishSession.Reset(); }
        }

        private static void LogAssertIgnore(Action a)
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            try { a(); } finally { UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false; }
        }

        [Test]
        public void Resolve_UsesCreditsAndRecordsSeed()
        {
            var table = Table((0, new[] { 2 }), (1, new int[0]), (2, new[] { 0 }));
            var r = new SkirmishSettings { Resources = StartingResources.High, Victory = VictoryMode.Annihilation }.Resolve(77, table);
            Assert.AreEqual(4000, r.StartingCredits);
            Assert.AreEqual(VictoryMode.Annihilation, r.Victory);
            Assert.AreEqual(77, r.Seed);
            Assert.IsTrue(r.HasSpawns);
            Assert.IsTrue((r.PlayerSlot == 0 && r.EnemySlot == 2) || (r.PlayerSlot == 2 && r.EnemySlot == 0));
        }

        // ---------- Spawn selection ----------

        private static SpawnTable Table(params (int slot, int[] opponents)[] rows)
        {
            var t = new SpawnTable();
            foreach (var (slot, opp) in rows) t.Add(slot, opp);
            return t;
        }

        private static SpawnTable Ring8()
        {
            // Eight slots on a ring; valid opponents are the three farthest (as on the 8-start maps).
            var t = new SpawnTable();
            for (int i = 0; i < 8; i++) t.Add(i, new[] { (i + 3) % 8, (i + 4) % 8, (i + 5) % 8 });
            return t;
        }

        [Test]
        public void Spawn_SameSeed_SamePair()
        {
            var t = Ring8();
            Assert.IsTrue(SpawnSelection.Choose(t, -1, 1234, out int a1, out int b1));
            Assert.IsTrue(SpawnSelection.Choose(t, -1, 1234, out int a2, out int b2));
            Assert.AreEqual((a1, b1), (a2, b2));
        }

        [Test]
        public void Spawn_Random_CoversEveryValidPair_WithoutFavouringSlots()
        {
            var t = Ring8();
            var counts = new Dictionary<(int, int), int>();
            var bySlot = new int[8];
            const int n = 4800;
            for (int seed = 1; seed <= n; seed++)
            {
                Assert.IsTrue(SpawnSelection.Choose(t, -1, seed, out int p, out int e));
                CollectionAssert.Contains(t.OpponentsOf(p), e, "only valid pairs are drawn");
                counts[(p, e)] = counts.TryGetValue((p, e), out int c) ? c + 1 : 1;
                bySlot[p]++;
            }
            Assert.AreEqual(24, counts.Count, "all 24 ordered pairs occur");
            foreach (int c in bySlot) Assert.That(c, Is.InRange(n / 8 * 0.8, n / 8 * 1.2), "player slots are drawn about equally often");
        }

        [Test]
        public void Spawn_RequestedSlot_IsKept_OpponentIsValid()
        {
            var t = Ring8();
            var opponents = new HashSet<int>();
            for (int seed = 1; seed < 200; seed++)
            {
                Assert.IsTrue(SpawnSelection.Choose(t, 5, seed, out int p, out int e));
                Assert.AreEqual(5, p);
                CollectionAssert.Contains(t.OpponentsOf(5), e);
                opponents.Add(e);
            }
            Assert.AreEqual(3, opponents.Count, "the opponent still varies between matches");
        }

        [Test]
        public void Spawn_RequestedSlotWithoutPartners_FallsBackToRandomValidPair()
        {
            var t = Table((0, new[] { 2 }), (1, new int[0]), (2, new[] { 0 }));
            Assert.IsTrue(SpawnSelection.Choose(t, 1, 9, out int p, out int e));
            Assert.AreNotEqual(1, p);
            Assert.IsFalse(SpawnSelection.Choose(new SpawnTable(), -1, 9, out _, out _));
        }

        [Test]
        public void SpawnRules_RequireSeparationFairnessAndUsableSlots()
        {
            var rules = new SpawnPairRules { MinSeparation = 100f, MaxOpportunityImbalance = 0.1f };
            Assert.IsTrue(SpawnSelection.IsValidPair(150f, 100f, 95f, rules));
            Assert.IsFalse(SpawnSelection.IsValidPair(80f, 100f, 100f, rules), "too close");
            Assert.IsFalse(SpawnSelection.IsValidPair(float.PositiveInfinity, 100f, 100f, rules), "not connected");
            Assert.IsFalse(SpawnSelection.IsValidPair(150f, 100f, 80f, rules), "unfair");
            var slots = new[] { 0, 1, 2 };
            var table = SpawnSelection.ValidPairs(slots, s => s != 2, (a, b) => 150f, s => 100f, rules);
            CollectionAssert.AreEqual(new[] { 1 }, table.OpponentsOf(0));
            CollectionAssert.IsEmpty(table.OpponentsOf(2), "an unusable slot gets no opponents");
        }

        // ---------- Intelligence ----------

        private static EnemyIntel ThreeSites() => new EnemyIntel(new[] { (1, 100f, 0f), (2, 0f, 100f), (3, -100f, 0f) });

        [Test]
        public void Intel_StartsWithoutKnowledge_AndOnlyGuessesUnexploredSites()
        {
            var intel = ThreeSites();
            Assert.IsFalse(intel.EnemyLocated);
            Assert.IsNull(intel.KnownEnemyFocus());
            var site = intel.NextSiteToInvestigate(90f, 0f, 0f);
            Assert.AreEqual(1, site.Id, "nearest unexplored site first");
        }

        [Test]
        public void Intel_SeenEmptySite_IsCrossedOff()
        {
            var intel = ThreeSites();
            intel.Observe(10f, (x, z) => x > 50f, new EnemyIntel.Sighting[0]);
            Assert.AreEqual(EnemyIntel.SiteState.Empty, intel.Sites.First(s => s.Id == 1).State);
            Assert.AreEqual(2, intel.NextSiteToInvestigate(90f, 0f, 10f).Id, "never the same explored site again");
        }

        [Test]
        public void Intel_Sighting_LocatesEnemy_PrefersHeadquarters_AndGoesStale()
        {
            var intel = ThreeSites();
            var seen = new[]
            {
                new EnemyIntel.Sighting(-95f, 5f, false, 20f),
                new EnemyIntel.Sighting(-100f, -4f, true, 18f),
            };
            intel.Observe(20f, (x, z) => false, seen);
            Assert.IsTrue(intel.EnemyLocated);
            Assert.AreEqual(20f, intel.FirstDiscovery);
            Assert.AreEqual((-100f, -4f), intel.KnownEnemyFocus().Value, "the headquarters is the focus");
            Assert.AreEqual(EnemyIntel.SiteState.EnemyBase, intel.Sites.First(s => s.Id == 3).State);
            Assert.IsTrue(intel.HasCurrentIntel(30f));
            Assert.IsFalse(intel.HasCurrentIntel(20f + intel.StaleAfter + 1f), "old observations are outdated");

            // Every other site explored: the outdated enemy base is the one to re-check.
            float later = 20f + intel.StaleAfter + 10f;
            intel.Observe(later, (x, z) => x >= 0f, seen);
            var next = intel.NextSiteToInvestigate(0f, 0f, later);
            Assert.AreEqual(3, next.Id);
            Assert.IsNull(intel.NextSiteToInvestigate(0f, 0f, 25f), "nothing to do while the intelligence is current and every site explored");
        }

        // ---------- Victory ----------

        [Test]
        public void Annihilation_EliminatesOnlyWithoutProductionAndArmedUnits()
        {
            Assert.IsFalse(MatchOutcomeRules.IsEliminated(1, 0), "a factory keeps a faction alive");
            Assert.IsFalse(MatchOutcomeRules.IsEliminated(0, 1), "an armed unit keeps a faction alive");
            Assert.IsTrue(MatchOutcomeRules.IsEliminated(0, 0));
            var ongoing = MatchOutcomeRules.EvaluateAnnihilation(new[] { (1, 1, 0), (2, 0, 3) });
            Assert.IsFalse(ongoing.IsOver);
            var win = MatchOutcomeRules.EvaluateAnnihilation(new[] { (1, 0, 2), (2, 0, 0) });
            Assert.AreEqual(MatchResult.Victory, win.Result);
            Assert.AreEqual(1, win.WinnerFactionId);
            Assert.AreEqual(EndReason.Annihilated, win.Reason);
            var draw = MatchOutcomeRules.EvaluateAnnihilation(new[] { (1, 0, 0), (2, 0, 0) });
            Assert.AreEqual(MatchResult.Draw, draw.Result);
            Assert.AreEqual(EndReason.SimultaneousAnnihilation, draw.Reason);
            Assert.IsTrue(MatchOutcomeRules.IsLastStand(0));
            Assert.IsFalse(MatchOutcomeRules.IsLastStand(1));
        }

        [Test]
        public void HeadquartersMode_Unchanged()
        {
            var o = MatchOutcomeRules.Evaluate(new[] { (1, true), (2, false) });
            Assert.AreEqual(MatchResult.Victory, o.Result);
            Assert.AreEqual(EndReason.HeadquartersDestroyed, o.Reason);
        }

        // ---------- Difficulty ----------

        [Test]
        public void Difficulty_NormalPreset_IsTheMilestone007Behaviour()
        {
            var n = CommanderSettings.Normal;
            Assert.AreEqual(6, n.AttackArmySize);
            Assert.AreEqual(12, n.MaxArmy);
            Assert.AreEqual(300, n.CreditReserve);
            Assert.IsFalse(n.NoCounterBuilding);
            Assert.IsFalse(n.AdvancedCounters);
            Assert.AreEqual(0f, n.AttackStrengthEdge);
            Assert.AreEqual(0f, n.EarliestAttack);
        }

        [Test]
        public void Difficulty_Presets_AreOrdered()
        {
            var e = CommanderSettings.Easy;
            var n = CommanderSettings.Normal;
            var h = CommanderSettings.Hard;
            Assert.Less(e.MaxArmy, n.MaxArmy);
            Assert.Less(n.MaxArmy, h.MaxArmy);
            Assert.Less(e.MaxHarvesters, h.MaxHarvesters);
            Assert.LessOrEqual(e.ReconCount, n.ReconCount);
            Assert.Less(n.ReconCount, h.ReconCount);
            Assert.IsTrue(e.NoCounterBuilding);
            Assert.IsTrue(h.AdvancedCounters);
            Assert.Greater(e.EarliestAttack, 0f);
            Assert.Greater(h.AttackStrengthEdge, 1f);
        }

        [Test]
        public void Difficulty_Composition_EasyFixedMix_HardStrongerCounter()
        {
            var s = new CommanderSnapshot { KnownEnemyHeavy = 4, KnownEnemyLight = 1 };
            Assert.AreEqual(CompositionRules.FixedHeavyShare, CompositionRules.DesiredHeavyShare(s, CommanderSettings.Easy));
            Assert.AreEqual(CompositionRules.CounterHeavyShare, CompositionRules.DesiredHeavyShare(s, CommanderSettings.Normal));
            Assert.AreEqual(CompositionRules.AdvancedCounterHeavyShare, CompositionRules.DesiredHeavyShare(s, CommanderSettings.Hard));
        }

        [Test]
        public void Difficulty_AttackTiming_EasyWaits_HardNeedsEdgeUnlessAtCap()
        {
            var ready = new CommanderSnapshot { HasHeadquarters = true, OreAvailable = true, LightTanks = 8, ArmyStrength = 10f, MatchTime = 100f };
            Assert.IsTrue(StrategyRules.ReadyToAttack(ready, CommanderSettings.Normal));
            Assert.IsFalse(StrategyRules.ReadyToAttack(ready, CommanderSettings.Easy), "Easy holds back early in the match");
            var later = ready;
            later.MatchTime = 400f;
            Assert.IsTrue(StrategyRules.ReadyToAttack(later, CommanderSettings.Easy));

            var hard = CommanderSettings.Hard;
            var outmatched = ready;
            outmatched.KnownEnemyStrength = 12f;
            Assert.IsFalse(StrategyRules.ReadyToAttack(outmatched, hard), "Hard waits for a strength edge");
            outmatched.LightTanks = hard.MaxArmy;
            Assert.IsTrue(StrategyRules.ReadyToAttack(outmatched, hard), "...but not forever: at the army cap it attacks");
        }

        [Test]
        public void Difficulty_Assets_DifferOnlyInBehaviour()
        {
            var assets = new[] { "Easy", "Normal", "Hard" }
                .Select(n => AssetDatabase.LoadAssetAtPath<AiDifficulty>($"{SkirmishMapBuilder.AiDir}/{n}.asset")).ToList();
            Assert.IsTrue(assets.All(a => a != null), "difficulty assets exist");
            CollectionAssert.AreEqual(new[] { DifficultyLevel.Easy, DifficultyLevel.Normal, DifficultyLevel.Hard }, assets.Select(a => a.Level));
            Assert.AreEqual(1, assets.Select(a => a.VisionRange).Distinct().Count(), "no difficulty sees further");
            Assert.AreEqual(CommanderSettings.Normal.MaxArmy, assets[1].Strategy.MaxArmy);
            Assert.IsTrue(assets[0].Strategy.NoCounterBuilding);
            Assert.IsTrue(assets[2].Strategy.AdvancedCounters);
            Assert.Greater(assets[0].DecisionInterval, assets[2].DecisionInterval);
        }

        [Test]
        public void Recon_FirstScoutComesEarly_WhileEnemyUnknown()
        {
            var cost = new CostTable { Recon = 450, LightTank = 500, HeavyTank = 900, Harvester = 800, Refinery = 1000, PowerPlant = 500, Factory = 1300 };
            var s = new CommanderSnapshot { HasHeadquarters = true, OreAvailable = true, Credits = 2000, Factories = 1, Refineries = 1, Harvesters = 2, PowerPlants = 1, PowerProduced = 100 };
            var a = ExpansionRules.Next(s, StrategicState.Develop, CommanderSettings.Normal, cost);
            Assert.AreEqual(AiActionType.ProduceRecon, a.Type, "enemy not located: scout first");
            s.EnemyBaseKnown = true;
            a = ExpansionRules.Next(s, StrategicState.Develop, CommanderSettings.Normal, cost);
            Assert.AreNotEqual(AiActionType.ProduceRecon, a.Type, "located: the scout waits for an army as before");
        }

        [Test]
        public void Pacing_FailedAttacksEscalate_TheNextAttackAndTheArmyCap()
        {
            var cfg = CommanderSettings.Normal;
            var s = new CommanderSnapshot { HasHeadquarters = true, OreAvailable = true, LightTanks = cfg.EffectiveAttackSize, ArmyStrength = 20f, MatchTime = 600f };
            Assert.IsTrue(StrategyRules.ReadyToAttack(s, cfg));
            s.AttackEscalation = cfg.EscalationStep;
            Assert.IsFalse(StrategyRules.ReadyToAttack(s, cfg), "after a failed attack the next group is bigger");
            s.LightTanks += cfg.EscalationStep;
            Assert.IsTrue(StrategyRules.ReadyToAttack(s, cfg));
            Assert.AreEqual(cfg.MaxArmy + cfg.EscalationStep, s.ArmyCap(cfg), "and the army cap grows with it");
            Assert.Greater(cfg.MaxEscalation, 0);
        }

        [Test]
        public void Pacing_LargeMaps_RaiseEconomyAndArmyCaps_SmallMapsUnchanged()
        {
            var n = CommanderSettings.Normal;
            var small = n.ScaledForMap(100f);
            Assert.AreEqual(n.MaxHarvesters, small.MaxHarvesters);
            Assert.AreEqual(n.MaxArmy, small.MaxArmy);
            Assert.AreEqual(n.ExpansionCredits, small.ExpansionCredits);
            var large = n.ScaledForMap(600f);
            Assert.Greater(large.MaxHarvesters, n.MaxHarvesters);
            Assert.Greater(large.MaxRefineries, n.MaxRefineries);
            Assert.Greater(large.MaxArmy, n.MaxArmy);
            Assert.Less(large.ExpansionCredits, n.ExpansionCredits);
            Assert.AreEqual(n.CreditReserve, large.CreditReserve, "only caps change");
            Assert.AreEqual(0, small.SecondFactoryHarvesters);
            Assert.Greater(large.SecondFactoryHarvesters, 0);
            var cost = new CostTable { Recon = 450, LightTank = 500, HeavyTank = 900, Harvester = 800, Refinery = 1000, PowerPlant = 500, Factory = 1300 };
            var rich = new CommanderSnapshot { HasHeadquarters = true, OreAvailable = true, OreNearRefinery = true, Credits = 600, Factories = 1, Refineries = 3,
                Harvesters = 6, PowerPlants = 3, PowerProduced = 300, EnemyBaseKnown = true, Recon = 1, LightTanks = 6, ResearchCentres = 1, GuardTowers = 2, AtTurrets = 2 };
            var a = EconomyPlanner.Next(rich, StrategicState.Assemble, large, cost);
            Assert.AreEqual(AiActionType.SaveFor, a.Type, a.ToString());
            Assert.AreEqual(AiActionType.BuildFactory, a.Goal, "a strong economy on a large map saves for its second factory");
            Assert.Less(CommanderSettings.Easy.ScaledForMap(600f).MaxArmy, large.MaxArmy, "difficulty order is kept");
            Assert.Less(large.MaxArmy, CommanderSettings.Hard.ScaledForMap(600f).MaxArmy);
        }

        [Test]
        public void Pacing_ReplacementScouts_AreThrottled()
        {
            var cost = new CostTable { Recon = 450, LightTank = 500, HeavyTank = 900, Harvester = 800, Refinery = 1000, PowerPlant = 500, Factory = 1300 };
            var s = new CommanderSnapshot { HasHeadquarters = true, OreAvailable = true, Credits = 3000, Factories = 1, Refineries = 1, Harvesters = 2, PowerPlants = 1, PowerProduced = 100,
                LightTanks = 4, EnemyBaseKnown = true };
            Assert.AreEqual(AiActionType.ProduceRecon, ExpansionRules.Next(s, StrategicState.Assemble, CommanderSettings.Normal, cost).Type);
            s.ReconOnCooldown = true;
            Assert.AreNotEqual(AiActionType.ProduceRecon, ExpansionRules.Next(s, StrategicState.Assemble, CommanderSettings.Normal, cost).Type);
        }

        // ---------- Map validator (synthetic grid maps) ----------

        /// <summary>A grid world: blocked rectangles, 1 m cells, 8-connected paths, simple rectangular footprints.</summary>
        private sealed class GridProbe : IMapProbe
        {
            private readonly Rect _bounds;
            private readonly bool[,] _blocked;
            private readonly int _n;

            public GridProbe(Rect bounds, IEnumerable<Rect> walls)
            {
                _bounds = bounds;
                _n = Mathf.RoundToInt(bounds.width);
                _blocked = new bool[_n, _n];
                foreach (var w in walls)
                    for (int x = 0; x < _n; x++)
                    for (int z = 0; z < _n; z++)
                        if (w.Contains(Center(x, z))) _blocked[x, z] = true;
            }

            private Vector2 Center(int x, int z) => new Vector2(_bounds.xMin + x + 0.5f, _bounds.yMin + z + 0.5f);

            private bool Cell(Vector2 p, out int x, out int z)
            {
                x = Mathf.FloorToInt(p.x - _bounds.xMin);
                z = Mathf.FloorToInt(p.y - _bounds.yMin);
                return x >= 0 && z >= 0 && x < _n && z < _n;
            }

            private bool Snap(Vector2 p, float radius, out int sx, out int sz)
            {
                sx = sz = -1;
                float best = float.MaxValue;
                int r = Mathf.CeilToInt(radius);
                if (!Cell(p, out int cx, out int cz)) return false;
                for (int dx = -r; dx <= r; dx++)
                for (int dz = -r; dz <= r; dz++)
                {
                    int x = cx + dx, z = cz + dz;
                    if (x < 0 || z < 0 || x >= _n || z >= _n || _blocked[x, z]) continue;
                    float d = (Center(x, z) - p).magnitude;
                    if (d <= radius + 0.71f && d < best) { best = d; sx = x; sz = z; }
                }
                return sx >= 0;
            }

            public bool IsWalkable(Vector2 p, float tolerance) => Snap(p, tolerance, out _, out _);

            public float PathLength(Vector2 from, Vector2 to, float snapFrom, float snapTo)
            {
                if (!Snap(from, snapFrom, out int ax, out int az) || !Snap(to, snapTo, out int bx, out int bz)) return float.PositiveInfinity;
                var dist = new float[_n, _n];
                for (int x = 0; x < _n; x++) for (int z = 0; z < _n; z++) dist[x, z] = float.PositiveInfinity;
                var open = new SortedSet<(float d, int x, int z)>();
                dist[ax, az] = 0f;
                open.Add((0f, ax, az));
                while (open.Count > 0)
                {
                    var (d, x, z) = open.Min;
                    open.Remove(open.Min);
                    if (x == bx && z == bz) return d;
                    for (int dx = -1; dx <= 1; dx++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        int nx = x + dx, nz = z + dz;
                        if (nx < 0 || nz < 0 || nx >= _n || nz >= _n || _blocked[nx, nz]) continue;
                        float nd = d + (dx != 0 && dz != 0 ? 1.414f : 1f);
                        if (nd >= dist[nx, nz]) continue;
                        open.Remove((dist[nx, nz], nx, nz));
                        dist[nx, nz] = nd;
                        open.Add((nd, nx, nz));
                    }
                }
                return float.PositiveInfinity;
            }

            private static Vector2 SizeOf(string id) => id == "headquarters" ? new Vector2(8f, 8f) : id == "refinery" ? new Vector2(7f, 6f) : new Vector2(8f, 7f);

            public bool CanPlace(string id, Vector2 c, bool facesSouth, IReadOnlyList<PlannedStructure> placed)
            {
                var s = SizeOf(id);
                var body = new Rect(c - s * 0.5f, s);
                if (!_bounds.Contains(body.min) || !_bounds.Contains(body.max)) return false;
                for (float x = body.xMin + 0.5f; x < body.xMax; x += 1f)
                for (float z = body.yMin + 0.5f; z < body.yMax; z += 1f)
                    if (!Cell(new Vector2(x, z), out int cx, out int cz) || _blocked[cx, cz]) return false;
                foreach (var p in placed)
                {
                    var ps = SizeOf(p.Id);
                    var other = new Rect(p.Center - ps * 0.5f - Vector2.one * 1.5f, ps + Vector2.one * 3f);
                    if (other.Overlaps(body)) return false;
                }
                return true;
            }
        }

        /// <summary>A 160 m test map: six starts on a ring, each with a home field, plus a central field.</summary>
        private static (MapSpec spec, List<Rect> walls) TestMap()
        {
            var spec = new MapSpec { Name = "Test ring", Bounds = new Rect(-80f, -80f, 160f, 160f) };
            spec.SpawnRules = new SpawnPairRules { MinSeparation = 90f, MaxOpportunityImbalance = 0.12f };
            for (int i = 0; i < 6; i++)
            {
                float a = (30f + i * 60f) * Mathf.Deg2Rad;
                var hq = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 60f;
                var inward = -hq.normalized;
                var slot = new MapSpec.Slot
                {
                    Index = i, Name = "S" + i, Headquarters = hq, Refinery = hq + inward * 12f, Harvester = hq + inward * 12f + new Vector2(0f, 6f),
                    RefineryDock = hq + inward * 16f, FacesSouth = hq.y > 0f,
                };
                spec.Slots.Add(slot);
                spec.Deposits.Add(new MapSpec.Deposit { Name = "Home" + i, Position = hq + inward * 24f, Amount = 9000, Radius = 2.5f });
            }
            spec.Deposits.Add(new MapSpec.Deposit { Name = "Contested_Centre", Position = Vector2.zero, Amount = 6000, Radius = 2.5f, Contested = true });
            spec.CheckStoredTable = false;
            return (spec, new List<Rect>());
        }

        private static MapValidationThresholds Small => new MapValidationThresholds
        {
            MinReachableOrePerSlot = 15000, MinStartingOre = 8000, StartingResourceRadius = 40f, MinExpansionSites = 3, ExpansionRadius = 22f,
            CoverageSpacing = 4f, OpportunityDistanceScale = 40f,
        };

        [Test]
        public void Validator_ValidMap_Passes_AndDerivesFairPairs()
        {
            var (spec, walls) = TestMap();
            var r = MapValidator.Validate(spec, new GridProbe(spec.Bounds, walls), Small);
            Assert.IsTrue(r.Passed, r.ToString());
            Assert.IsNotNull(r.SpawnTable);
            // Neighbours on the ring are 60 m apart (too close); the three far slots are valid opponents.
            foreach (int s in r.SpawnTable.Slots)
                CollectionAssert.AreEquivalent(new[] { (s + 2) % 6, (s + 3) % 6, (s + 4) % 6 }, r.SpawnTable.OpponentsOf(s));
        }

        [Test]
        public void Validator_DisconnectedRegion_IsReported()
        {
            var (spec, walls) = TestMap();
            walls.Add(new Rect(-1f, -80f, 2f, 160f)); // a full-height wall splits the map in two
            var r = MapValidator.Validate(spec, new GridProbe(spec.Bounds, walls), Small);
            Assert.IsFalse(r.Passed);
            Assert.IsTrue(r.Has("slot-reach"), r.ToString());
            Assert.IsTrue(r.Has("deposit-reach"), r.ToString());
        }

        [Test]
        public void Validator_BlockedChokepoint_IsReported()
        {
            var (spec, walls) = TestMap();
            // A wall with a single pass in the middle (open map) — then the pass is blocked.
            walls.Add(new Rect(-80f, -1f, 74f, 2f));
            walls.Add(new Rect(6f, -1f, 74f, 2f));
            var open = MapValidator.Validate(spec, new GridProbe(spec.Bounds, walls), Small);
            Assert.IsFalse(open.Has("slot-reach"), "with the pass open the halves are connected:\n" + open);
            walls.Add(new Rect(-6f, -1f, 12f, 2f));
            var blocked = MapValidator.Validate(spec, new GridProbe(spec.Bounds, walls), Small);
            Assert.IsTrue(blocked.Has("slot-reach"), blocked.ToString());
        }

        [Test]
        public void Validator_UnreachableDeposit_IsReported()
        {
            var (spec, walls) = TestMap();
            // Box the central (contested) field in completely.
            walls.Add(new Rect(-8f, -8f, 16f, 2f));
            walls.Add(new Rect(-8f, 6f, 16f, 2f));
            walls.Add(new Rect(-8f, -8f, 2f, 16f));
            walls.Add(new Rect(6f, -8f, 2f, 16f));
            var r = MapValidator.Validate(spec, new GridProbe(spec.Bounds, walls), Small);
            Assert.IsTrue(r.Has("deposit-reach"), r.ToString());
            Assert.IsTrue(r.Has("contested"), r.ToString());
        }

        [Test]
        public void Validator_StartInsideObstacle_MakesSlotUnusable()
        {
            var (spec, walls) = TestMap();
            var hq = spec.Slots[2].Headquarters;
            walls.Add(new Rect(hq.x - 3f, hq.y - 3f, 6f, 6f));
            var r = MapValidator.Validate(spec, new GridProbe(spec.Bounds, walls), Small);
            Assert.IsTrue(r.Has("start-buildings"), r.ToString());
            CollectionAssert.IsEmpty(r.SpawnTable.OpponentsOf(2), "a blocked start is never offered");
            foreach (int s in r.SpawnTable.Slots) CollectionAssert.DoesNotContain(r.SpawnTable.OpponentsOf(s), 2);
        }

        [Test]
        public void Validator_TooFewSlots_AndStaleTable_AreReported()
        {
            var (spec, walls) = TestMap();
            spec.Slots.RemoveRange(4, 2);
            spec.CheckStoredTable = true;
            foreach (var s in spec.Slots) s.StoredOpponents = new[] { (s.Index + 1) % 4 };
            var r = MapValidator.Validate(spec, new GridProbe(spec.Bounds, walls), Small);
            Assert.IsTrue(r.Has("slots"), r.ToString());
            Assert.IsTrue(r.Has("spawn-table"), r.ToString());
        }

        [Test]
        public void Validator_UnequalStarts_AreNotPaired()
        {
            var (spec, walls) = TestMap();
            spec.Deposits[0].Amount = 30000; // slot 0 is far richer than everyone else
            var r = MapValidator.Validate(spec, new GridProbe(spec.Bounds, walls), Small);
            CollectionAssert.IsEmpty(r.SpawnTable.OpponentsOf(0), r.ToString());
            Assert.IsTrue(r.Has("spawn-pairs"));
        }

        // ---------- Content ----------

        [Test]
        public void Layouts_MeetTheAddendumTargets()
        {
            var expected = new Dictionary<string, float> { { "dustfall_plains", 600f }, { "iron_pass", 400f }, { "green_divide", 600f }, { "blackstone_basin", 400f } };
            var layouts = SkirmishMapLayouts.All;
            Assert.AreEqual(4, layouts.Count);
            foreach (var l in layouts)
            {
                Assert.AreEqual(expected[l.Id], l.Size, l.Name);
                Assert.GreaterOrEqual(l.Slots.Count, 6, l.Name);
                Assert.AreEqual(l.Slots.Count, l.Slots.Select(s => s.Id).Distinct().Count());
                foreach (var s in l.Slots)
                {
                    Assert.Less(Mathf.Abs(s.Hq.x), l.Half - 20f);
                    Assert.Less(Mathf.Abs(s.Hq.z), l.Half - 20f);
                }
            }
            // Deterministic: building the layout twice gives the same content.
            var a = SkirmishMapLayouts.GreenDivide();
            var b = SkirmishMapLayouts.GreenDivide();
            Assert.AreEqual(a.Obstacles.Count, b.Obstacles.Count);
            Assert.AreEqual(a.Obstacles[a.Obstacles.Count / 2].ground, b.Obstacles[b.Obstacles.Count / 2].ground);
        }

        [Test]
        public void Catalog_ListsFourBuiltMaps_WithPreviewsAndScenesInBuild()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<MapCatalog>(SkirmishMapBuilder.CatalogPath);
            Assert.IsNotNull(catalog);
            Assert.AreEqual(4, catalog.Maps.Count);
            Assert.AreEqual(3, catalog.Difficulties.Count);
            var build = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => System.IO.Path.GetFileNameWithoutExtension(s.path)).ToList();
            foreach (var m in catalog.Maps)
            {
                Assert.IsNotNull(m.Preview, m.DisplayName + " preview");
                Assert.IsFalse(string.IsNullOrEmpty(m.Description));
                Assert.GreaterOrEqual(m.StartNames.Count, 6);
                CollectionAssert.Contains(build, m.SceneName);
            }
        }
    }
}
