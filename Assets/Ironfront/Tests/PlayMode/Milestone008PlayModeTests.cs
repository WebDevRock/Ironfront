using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Ironfront.AI;
using Ironfront.Buildings;
using Ironfront.Combat;
using Ironfront.Core.Skirmish;
using Ironfront.Core.Victory;
using Ironfront.Match;
using Ironfront.Skirmish;
using Ironfront.UI;
using Ironfront.Vision;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Ironfront.Tests
{
    /// <summary>
    /// Milestone 008 in the real skirmish scenes: the chosen configuration reaches the match (map, spawns, credits,
    /// difficulty, victory), seeds reproduce and vary the starts, restart and return-to-menu leave no stale state, the
    /// victory modes end matches, fog hides the enemy start from both sides and the computer finds the enemy by scouting.
    /// </summary>
    public class Milestone008PlayModeTests
    {
        private const int Player = 1;
        private const int Enemy = 2;

        private static readonly (string id, string scene, float size)[] Maps =
        {
            ("dustfall_plains", "Skirmish_DustfallPlains", 600f),
            ("iron_pass", "Skirmish_IronPass", 400f),
            ("green_divide", "Skirmish_GreenDivide", 600f),
            ("blackstone_basin", "Skirmish_BlackstoneBasin", 400f),
        };

        [SetUp]
        public void ResetSession() => SkirmishSession.Reset();

        [TearDown]
        public void Restore()
        {
            Time.timeScale = 1f;
            SkirmishSession.Reset();
        }

        private static IEnumerator Load(SkirmishSettings settings)
        {
            SkirmishSession.Select(settings);
            string scene = Maps.First(m => m.id == settings.MapId).scene;
            SceneManager.LoadScene(scene, LoadSceneMode.Single);
            yield return null;
            yield return null;
            Assert.AreEqual(scene, SceneManager.GetActiveScene().name);
            Assert.IsNotNull(SkirmishInitializer.Current, "skirmish initializer");
            Assert.IsTrue(SkirmishInitializer.Current.BasesSpawned, "bases spawned");
        }

        private static Building Hq(int faction) =>
            Building.All.FirstOrDefault(b => b.FactionId == faction && b.Definition != null && b.Definition.Id == "headquarters");

        private static void Kill(Health h, int byFaction) => h.ApplyDamage(1e7f, byFaction);

        private static void AssertSingleManagers()
        {
            Assert.AreEqual(1, Object.FindObjectsByType<MatchController>().Length, "MatchController");
            Assert.AreEqual(1, Object.FindObjectsByType<MatchFlowController>().Length, "MatchFlowController");
            Assert.AreEqual(1, Object.FindObjectsByType<VisionSystem>().Length, "VisionSystem");
            Assert.AreEqual(1, Object.FindObjectsByType<SkirmishInitializer>().Length, "SkirmishInitializer");
            Assert.AreEqual(1, Object.FindObjectsByType<EnemyCommander>(FindObjectsInactive.Include).Length, "one computer commander");
            Assert.AreEqual(2, Building.All.Count(b => b.Definition != null && b.Definition.Id == "headquarters"), "two headquarters");
            // Nothing left over from a previous match: every structure is near its own faction's start
            // (the computer may already have placed its first power plant).
            var init = SkirmishInitializer.Current;
            foreach (var b in Building.All)
            {
                var slot = StartSlot.Find(b.FactionId == Player ? init.Resolved.PlayerSlot : init.Resolved.EnemySlot);
                Assert.Less((b.transform.position - slot.Headquarters).magnitude, 45f, $"{b.name} belongs to this match");
            }
        }

        [UnityTest]
        public IEnumerator EveryMap_StartsWithTheChosenConfiguration()
        {
            var difficulties = new[] { DifficultyLevel.Hard, DifficultyLevel.Easy, DifficultyLevel.Normal, DifficultyLevel.Hard };
            for (int i = 0; i < Maps.Length; i++)
            {
                var (id, _, size) = Maps[i];
                var settings = new SkirmishSettings
                {
                    MapId = id, Difficulty = difficulties[i], Resources = i % 2 == 0 ? StartingResources.High : StartingResources.Low,
                    StartSlot = 1, Victory = i % 2 == 0 ? VictoryMode.Annihilation : VictoryMode.Headquarters, Seed = 100 + i,
                };
                yield return Load(settings);
                var init = SkirmishInitializer.Current;
                Assert.GreaterOrEqual(StartSlot.All.Count, 6, id);
                Assert.AreEqual(1, init.Resolved.PlayerSlot, id);
                CollectionAssert.Contains(StartSlot.Find(1).ValidOpponents.ToList(), init.Resolved.EnemySlot, id + ": the opponent start is a validated pair");

                Assert.Less((Hq(Player).transform.position - StartSlot.Find(init.Resolved.PlayerSlot).Headquarters).magnitude, 0.5f, id);
                Assert.Less((Hq(Enemy).transform.position - StartSlot.Find(init.Resolved.EnemySlot).Headquarters).magnitude, 0.5f, id);
                int credits = SkirmishSettings.CreditsFor(settings.Resources);
                Assert.AreEqual(credits, MatchController.Current.GetFaction(Player).Wallet.Credits, id + " player credits");
                Assert.AreEqual(credits, MatchController.Current.GetFaction(Enemy).StartingCredits, id + " computer credits");
                Assert.AreEqual(credits, MatchController.Current.GetFaction(Player).StartingCredits, id + " player starting credits");
                Assert.AreEqual(difficulties[i], EnemyCommander.All[0].Difficulty.Level, id);
                Assert.AreEqual(settings.Victory, MatchFlowController.Current.Victory, id);
                CollectionAssert.AreEqual(new[] { Player, Enemy }, MatchFlowController.Current.Competitors.ToArray(), id);
                AssertSingleManagers();

                // Large-map systems agree on the size.
                var grid = VisionSystem.Current.Grid;
                Assert.AreEqual(size, grid.Width * grid.CellSize, 0.01f, id + " fog grid");
                Assert.AreEqual(size, Object.FindAnyObjectByType<CameraControl.RtsCameraController>().BoundsMax.x * 2f, 0.01f, id + " camera bounds");
                Assert.AreEqual(size, Object.FindAnyObjectByType<Minimap>().WorldRect.width, 0.01f, id + " minimap");

                // Neither side is shown the other's base — in the rules and on screen.
                Assert.IsFalse(VisionSystem.CanSee(Player, Hq(Enemy).GetComponent<Health>()), id + ": enemy base hidden from the player");
                yield return null;
                AssertEnemyStructuresNotRendered(id);
                Assert.IsFalse(VisionSystem.CanSee(Enemy, Hq(Player).GetComponent<Health>()), id + ": player base hidden from the computer");
            }
        }

        /// <summary>
        /// Every renderer of an enemy structure the player has never seen is switched off (regression: a vision tick
        /// before the structure's Start left all starting enemy buildings drawn through the fog).
        /// </summary>
        private static void AssertEnemyStructuresNotRendered(string where)
        {
            int checkedRenderers = 0;
            foreach (var b in Building.All)
            {
                if (b.FactionId == Player) continue;
                var h = b.GetComponent<Health>();
                if (h.Vision == null || h.Vision.SeenByPlayer) continue;
                foreach (var r in b.GetComponentsInChildren<Renderer>(true))
                {
                    if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                    Assert.IsTrue(r.forceRenderingOff, $"{where}: {b.name}/{r.name} is drawn although the player has never seen it");
                    checkedRenderers++;
                }
            }
            Assert.Greater(checkedRenderers, 0, where + ": there were hidden enemy structures to check");
        }

        [UnityTest]
        public IEnumerator FixedLayoutMatch_HidesTheEnemyBaseOnScreen()
        {
            SceneManager.LoadScene("RTS_Match", LoadSceneMode.Single);
            yield return null;
            yield return null;
            yield return null;
            AssertEnemyStructuresNotRendered("RTS_Match");
            Assert.AreEqual(1f, Shader.GetGlobalVector(FogOfWarView.FogParamsId).y, 1e-4f, "never-explored ground is fully dark");
        }

        [UnityTest]
        public IEnumerator Seeds_ReproduceAndVaryTheStarts()
        {
            var seen = new HashSet<(int, int)>();
            (int, int) first = default;
            for (int seed = 1; seed <= 6; seed++)
            {
                yield return Load(new SkirmishSettings { MapId = "dustfall_plains", Seed = seed });
                var r = SkirmishInitializer.Current.Resolved;
                Assert.Less((Hq(Enemy).transform.position - StartSlot.Find(r.EnemySlot).Headquarters).magnitude, 0.5f);
                if (seed == 1) first = (r.PlayerSlot, r.EnemySlot);
                seen.Add((r.PlayerSlot, r.EnemySlot));
            }
            Assert.Greater(seen.Count, 2, "different seeds give different starts");
            yield return Load(new SkirmishSettings { MapId = "dustfall_plains", Seed = 1 });
            Assert.AreEqual(first, (SkirmishInitializer.Current.Resolved.PlayerSlot, SkirmishInitializer.Current.Resolved.EnemySlot), "the same seed reproduces the starts");

            // Two successive normal (unseeded) matches: the session draws a new seed each time.
            var a = SkirmishInitializer.Current.Resolved.Seed;
            yield return Load(new SkirmishSettings { MapId = "dustfall_plains" });
            var s1 = SkirmishInitializer.Current.Resolved.Seed;
            yield return Load(new SkirmishSettings { MapId = "dustfall_plains" });
            Assert.AreNotEqual(s1, SkirmishInitializer.Current.Resolved.Seed);
            Assert.AreNotEqual(a, s1);
        }

        [UnityTest]
        public IEnumerator Restart_KeepsSettings_WithFreshState()
        {
            yield return Load(new SkirmishSettings { MapId = "iron_pass", Difficulty = DifficultyLevel.Easy, Resources = StartingResources.Low, Seed = 5 });
            var slots = (SkirmishInitializer.Current.Resolved.PlayerSlot, SkirmishInitializer.Current.Resolved.EnemySlot);
            MatchController.Current.PlayerFaction.Wallet.Add(5000);
            Kill(Hq(Enemy).GetComponent<Health>(), Player);
            yield return null;
            yield return null;
            Assert.IsTrue(MatchFlowController.Current.IsOver);
            Assert.AreEqual(0f, Time.timeScale, "the finished match is paused");

            MatchFlowController.Current.Restart();
            yield return null;
            yield return null;
            Assert.IsFalse(MatchFlowController.Current.IsOver);
            Assert.AreEqual(1f, Time.timeScale);
            Assert.AreEqual(1000, MatchController.Current.PlayerFaction.Wallet.Credits, "credits reset to the chosen level");
            Assert.AreEqual(DifficultyLevel.Easy, EnemyCommander.All[0].Difficulty.Level);
            Assert.AreEqual(slots, (SkirmishInitializer.Current.Resolved.PlayerSlot, SkirmishInitializer.Current.Resolved.EnemySlot), "seeded starts repeat");
            AssertSingleManagers();
        }

        [UnityTest]
        public IEnumerator Menu_LaunchesAMap_ReturnsToMenu_AndLaunchesAnother()
        {
            SceneManager.LoadScene("MainMenu", LoadSceneMode.Single);
            yield return null;
            var menu = Object.FindAnyObjectByType<MainMenuController>();
            Assert.IsNotNull(menu.Skirmish, "the main menu has the skirmish setup");
            menu.StartMatch();
            Assert.IsTrue(menu.Skirmish.IsOpen);
            Assert.AreEqual(SkirmishSettings.Default.MapId, menu.Skirmish.Current.MapId, "sensible defaults");
            menu.Skirmish.Apply(new SkirmishSettings { MapId = "green_divide", Difficulty = DifficultyLevel.Easy, Victory = VictoryMode.Annihilation, Seed = 3 });
            menu.Skirmish.StartMatch();
            yield return null;
            yield return null;
            Assert.AreEqual("Skirmish_GreenDivide", SceneManager.GetActiveScene().name);
            Assert.AreEqual(DifficultyLevel.Easy, EnemyCommander.All[0].Difficulty.Level);
            Assert.AreEqual(VictoryMode.Annihilation, MatchFlowController.Current.Victory);

            // The result screen's Main menu button (the same path as the in-game menu).
            var back = Object.FindObjectsByType<Button>(FindObjectsInactive.Include).First(b => b.name == "MainMenuButton");
            back.onClick.Invoke();
            yield return null;
            yield return null;
            Assert.AreEqual("MainMenu", SceneManager.GetActiveScene().name);
            menu = Object.FindAnyObjectByType<MainMenuController>();
            menu.StartMatch();
            Assert.AreEqual("green_divide", menu.Skirmish.Current.MapId, "the menu remembers the last choice");
            Assert.AreEqual(DifficultyLevel.Easy, menu.Skirmish.Current.Difficulty);

            menu.Skirmish.Apply(new SkirmishSettings { MapId = "blackstone_basin", Difficulty = DifficultyLevel.Hard, Resources = StartingResources.High, Seed = 8 });
            menu.Skirmish.StartMatch();
            yield return null;
            yield return null;
            Assert.AreEqual("Skirmish_BlackstoneBasin", SceneManager.GetActiveScene().name);
            Assert.AreEqual(DifficultyLevel.Hard, EnemyCommander.All[0].Difficulty.Level);
            Assert.AreEqual(VictoryMode.Headquarters, MatchFlowController.Current.Victory, "no setting leaked from the previous match");
            Assert.AreEqual(4000, MatchController.Current.PlayerFaction.Wallet.Credits);
            Assert.IsFalse(MatchFlowController.Current.IsOver);
            AssertSingleManagers();
        }

        private static GameObject SpawnLightTank(int faction, Vector3 at)
        {
            var factory = MatchController.Current.Setup.ConstructionOptions.First(d => d.Id == "vehicle_factory");
            var light = factory.ProducibleUnits.First(u => u.Id == "light_tank");
            return FactionSpawner.Spawn(light.Prefab, at, Quaternion.identity, faction);
        }

        [UnityTest]
        public IEnumerator Annihilation_RequiresEveryProducerAndArmedUnit_AndRevealsTheLastStand()
        {
            yield return Load(new SkirmishSettings { MapId = "blackstone_basin", Victory = VictoryMode.Annihilation, Seed = 4 });
            foreach (var ai in EnemyCommander.All.ToList()) ai.enabled = false;
            var enemySlot = StartSlot.Find(SkirmishInitializer.Current.Resolved.EnemySlot);
            var tank = SpawnLightTank(Enemy, enemySlot.Headquarters + new Vector3(0f, 0f, enemySlot.FacesSouth ? -16f : 16f));
            yield return null;
            Assert.IsFalse(VisionSystem.CanSee(Player, tank.GetComponent<Health>()), "hidden while the enemy can still produce");

            Kill(Hq(Enemy).GetComponent<Health>(), Player);
            yield return null;
            yield return null;
            Assert.IsFalse(MatchFlowController.Current.IsOver, "an armed unit keeps the faction in the match");
            Assert.IsTrue(MatchFlowController.Current.IsLastStand(Enemy));
            VisionSystem.Current.Tick();
            CollectionAssert.Contains(VisionSystem.Current.LastStandFactions.ToList(), Enemy);
            Assert.IsTrue(VisionSystem.CanSee(Player, tank.GetComponent<Health>()), "last stand: the remaining unit is revealed");

            Kill(tank.GetComponent<Health>(), Player);
            yield return null;
            yield return null;
            Assert.IsTrue(MatchFlowController.Current.IsOver);
            Assert.AreEqual(MatchResult.Victory, MatchFlowController.Current.Outcome.Result);
            Assert.AreEqual(Player, MatchFlowController.Current.Outcome.WinnerFactionId);
            Assert.AreEqual(EndReason.Annihilated, MatchFlowController.Current.Outcome.Reason);
        }

        [UnityTest]
        public IEnumerator HeadquartersMode_HeadquartersLossEndsTheMatch()
        {
            yield return Load(new SkirmishSettings { MapId = "iron_pass", Seed = 2 });
            foreach (var ai in EnemyCommander.All.ToList()) ai.enabled = false;
            var enemySlot = StartSlot.Find(SkirmishInitializer.Current.Resolved.EnemySlot);
            SpawnLightTank(Enemy, enemySlot.Headquarters + new Vector3(0f, 0f, enemySlot.FacesSouth ? -16f : 16f));
            Kill(Hq(Enemy).GetComponent<Health>(), Player);
            yield return null;
            yield return null;
            Assert.IsTrue(MatchFlowController.Current.IsOver);
            Assert.AreEqual(EndReason.HeadquartersDestroyed, MatchFlowController.Current.Outcome.Reason);
            Assert.AreEqual(Player, MatchFlowController.Current.Outcome.WinnerFactionId);
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator Computer_FindsTheEnemyByScouting_NotByKnowledge()
        {
            yield return Load(new SkirmishSettings { MapId = "iron_pass", Difficulty = DifficultyLevel.Normal, Seed = 12 });
            var ai = EnemyCommander.All[0];
            var playerHq = Hq(Player).transform.position;
            yield return null;
            ai.Decide();
            Assert.IsNotNull(ai.Intel);
            Assert.IsFalse(ai.Intel.EnemyLocated, "nothing is known at the start");
            Assert.AreEqual(StartSlot.All.Count - 1, ai.Intel.Sites.Count, "every other start is a candidate");
            Assert.IsTrue(ai.Intel.Sites.Any(s => (new Vector3(s.X, 0f, s.Z) - playerHq).sqrMagnitude < 1f), "the real start is only one of them");

            Time.timeScale = 10f;
            float start = Time.time;
            bool sawEnemyBeforeLocating = false;
            while (!ai.Intel.EnemyLocated && Time.time - start < 600f)
            {
                // Until the player's base is located it must not be in the commander's memory of structures.
                if (!ai.Intel.EnemyLocated && ai.Intel.Sites.Any(s => s.State == Core.AI.EnemyIntel.SiteState.EnemyBase)) sawEnemyBeforeLocating = true;
                yield return null;
            }
            Time.timeScale = 1f;
            Assert.IsFalse(sawEnemyBeforeLocating);
            Assert.IsTrue(ai.Intel.EnemyLocated, $"the computer located the enemy within 600 game seconds (took {Time.time - start:0}s)");
            var focus = ai.Intel.KnownEnemyFocus().Value;
            Assert.Less((new Vector3(focus.x, 0f, focus.z) - playerHq).magnitude, 60f, "what it found is the player's base");
            Assert.Greater(ai.ScoutMissions, 0, "scouts were sent");
            Debug.Log($"[M008] Computer located the enemy after {ai.Intel.FirstDiscovery:0}s; empty sites crossed off: {ai.Intel.Count(Core.AI.EnemyIntel.SiteState.Empty)}");
        }
    }
}
