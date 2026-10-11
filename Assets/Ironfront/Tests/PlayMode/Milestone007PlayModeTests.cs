using System.Collections;
using System.Linq;
using Ironfront.Buildings;
using Ironfront.Combat;
using Ironfront.Commands;
using Ironfront.Construction;
using Ironfront.Core.Production;
using Ironfront.Core.Research;
using Ironfront.Data;
using Ironfront.Match;
using Ironfront.Vision;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ironfront.Tests
{
    /// <summary>
    /// Milestone 007 content in the real RTS_Match scene (fog on, computer commanders disabled): fog hides and
    /// protects enemies, recon reveals them, defences fire and depend on power, research flows and upgrades
    /// existing and new units, and artillery is gated, respects its minimum range and splashes.
    /// </summary>
    public class Milestone007PlayModeTests
    {
        private const int Player = 1;
        private const int Enemy = 2;
        private MatchController _match;
        private VehicleFactory _factory;
        private UnitDefinition _light, _heavy, _recon, _artillery;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            SceneManager.LoadScene("RTS_Match", LoadSceneMode.Single);
            yield return null;
            yield return null;
            _match = MatchController.Current;
            foreach (var ai in Object.FindObjectsByType<AI.EnemyCommander>()) ai.enabled = false;
            var factoryDef = _match.Setup.ConstructionOptions.First(d => d.Id == "vehicle_factory");
            _light = factoryDef.ProducibleUnits.First(u => u.Id == "light_tank");
            _heavy = factoryDef.ProducibleUnits.First(u => u.Id == "heavy_tank");
            _recon = factoryDef.ProducibleUnits.First(u => u.Id == "recon");
            _artillery = factoryDef.ProducibleUnits.First(u => u.Id == "rocket_artillery");
            _match.PlayerFaction.Wallet.Add(20000);
            _match.PlayerFaction.RegisterStructure("test_generator", 400, 0); // keep power healthy unless a test says otherwise
            _factory = Place("vehicle_factory", new Vector3(-38f, 0f, -12f)).GetComponent<VehicleFactory>(); // tech prerequisite
            yield return null;
        }

        [TearDown]
        public void RestoreTime() => Time.timeScale = 1f;

        private static GameObject Spawn(UnitDefinition def, Vector3 p, int faction, bool armed = true)
        {
            var go = FactionSpawner.Spawn(def.Prefab, p, Quaternion.LookRotation(faction == Player ? Vector3.forward : Vector3.back), faction);
            if (!armed) go.GetComponent<CombatUnit>().enabled = false;
            return go;
        }

        private BuildingDefinition Def(string id) => _match.Setup.ConstructionOptions.First(d => d.Id == id);

        private Building Place(string id, Vector3 p)
        {
            var placement = Object.FindAnyObjectByType<BuildingPlacementController>();
            Assert.IsTrue(placement.TryPlace(Def(id), p, Player, out var placed, out var reason), reason);
            return placed;
        }

        private static IEnumerator Until(System.Func<bool> condition, float seconds)
        {
            float end = Time.time + seconds;
            while (!condition() && Time.time < end) yield return null;
        }

        private static IEnumerator VisionTick() => Until(() => false, 0.25f); // vision ticks at 10 Hz

        // ---------- Fog of war ----------

        [UnityTest]
        public IEnumerator Fog_HidesEnemy_UntilRecon_ThenNotRemembered()
        {
            Assert.IsTrue(VisionSystem.Current != null && VisionSystem.Current.FogEnabled, "fog of war is on in the match");
            var enemy = Spawn(_light, new Vector3(0f, 0f, 10f), Enemy, armed: false);
            var enemyHealth = enemy.GetComponent<Health>();
            var commands = Object.FindAnyObjectByType<UnitCommandController>();
            Object.FindAnyObjectByType<CameraControl.RtsCameraController>().Focus(enemy.transform.position);
            yield return VisionTick();

            Assert.IsFalse(VisionSystem.CanSee(Player, enemyHealth), "map centre is not in the player's sight");
            Assert.IsFalse(VisionEntity.IsDrawn(enemy.GetComponent<VisionEntity>()), "hidden enemies are not drawn");
            Assert.IsTrue(enemy.GetComponentsInChildren<Renderer>().All(r => r.forceRenderingOff));
            Vector2 screen = Camera.main.WorldToScreenPoint(enemy.transform.position + Vector3.up * 0.8f);
            Assert.IsNull(commands.RaycastEnemy(screen), "hidden enemies cannot be clicked for an attack order");
            Assert.IsFalse(VisionSystem.IsExplored(Player, enemy.transform.position), "centre starts unexplored");

            var recon = Spawn(_recon, new Vector3(0f, 0f, -12f), Player, armed: false);
            yield return VisionTick();
            Assert.IsTrue(VisionSystem.CanSee(Player, enemyHealth), "recon (sight 28) reveals the enemy 22 m away");
            Assert.IsTrue(VisionEntity.IsDrawn(enemy.GetComponent<VisionEntity>()));
            Assert.AreEqual(enemyHealth, commands.RaycastEnemy(screen), "revealed enemies can be targeted");

            Object.Destroy(recon);
            yield return VisionTick();
            Assert.IsFalse(VisionSystem.CanSee(Player, enemyHealth), "units are not remembered once out of sight");
            Assert.IsTrue(VisionSystem.IsExplored(Player, enemy.transform.position), "but the terrain stays explored");
            Assert.IsFalse(VisionEntity.IsDrawn(enemy.GetComponent<VisionEntity>()), "intel is never shown as current");
        }

        [UnityTest]
        public IEnumerator Fog_FactionsHaveSeparateVision()
        {
            var playerTank = Spawn(_light, new Vector3(0f, 0f, -5f), Player, armed: false);
            var enemyTank = Spawn(_light, new Vector3(0f, 0f, 30f), Enemy, armed: false);
            yield return VisionTick();
            Assert.IsFalse(VisionSystem.CanSee(Enemy, playerTank.GetComponent<Health>()), "35 m: out of the enemy's 15 m sight");
            Assert.IsFalse(VisionSystem.CanSee(Player, enemyTank.GetComponent<Health>()));
            enemyTank.transform.position = new Vector3(0f, 0f, 6f);
            yield return VisionTick();
            Assert.IsTrue(VisionSystem.CanSee(Enemy, playerTank.GetComponent<Health>()));
            Assert.IsTrue(VisionSystem.CanSee(Player, enemyTank.GetComponent<Health>()));
        }

        // ---------- Defences ----------

        [UnityTest]
        public IEnumerator GuardTower_ShredsLightVehicles()
        {
            var tower = Place("guard_tower", new Vector3(-21f, 0f, -24f)).GetComponent<DefenceTurret>();
            Assert.IsNotNull(tower);
            var recon = Spawn(_recon, new Vector3(-21f, 0f, -14f), Enemy, armed: false);
            var health = recon.GetComponent<Health>();
            Time.timeScale = 2f;
            yield return Until(() => !health.IsAlive, 10f);
            Assert.Greater(tower.ShotsFired, 0, "the tower engages an enemy in range");
            Assert.IsFalse(health.IsAlive, "a recon vehicle does not survive a guard tower");
        }

        [UnityTest]
        public IEnumerator AtTurret_NeedsPower_AndBeatsHeavyArmour()
        {
            var turret = Place("at_turret", new Vector3(-17f, 0f, -29f)).GetComponent<DefenceTurret>();
            _match.PlayerFaction.RegisterStructure("test_blackout", 0, 2000);
            yield return null;
            Assert.IsFalse(turret.IsOnline, "offline on low power");
            var heavy = Spawn(_heavy, new Vector3(-17f, 0f, -18f), Enemy, armed: false);
            var health = heavy.GetComponent<Health>();
            yield return new WaitForSeconds(3f);
            Assert.AreEqual(0, turret.ShotsFired, "no shots while offline");

            _match.PlayerFaction.UnregisterStructure("test_blackout", 0, 2000);
            yield return null;
            Assert.IsTrue(turret.IsOnline);
            Time.timeScale = 2f;
            yield return Until(() => turret.ShotsFired >= 2, 10f);
            Assert.GreaterOrEqual(turret.ShotsFired, 2);
            turret.enabled = false;
            yield return new WaitForSeconds(1f); // let the last shell land
            float lost = health.Max - health.Current;
            // 95 x 1.5 vs heavy, minus 35 % armour = about 93 per hit: more than a heavy tank's own 70 x 0.65 per shot.
            Assert.Greater(lost / turret.ShotsFired, 70f, "anti-armour shots hit heavies hard");
        }

        // ---------- Research ----------

        [UnityTest]
        public IEnumerator Research_ArmourAppliesToExistingAndNewUnits_AndCannotRepeat()
        {
            var armour = _match.Setup.Research.First(r => r.Id == "composite_armour");
            var existing = Spawn(_light, new Vector3(-30f, 0f, -30f), Player, armed: false).GetComponent<Health>();
            yield return null;
            float baseArmour = existing.Armour;
            Assert.AreEqual(ResearchCheck.MissingFacility, ResearchCentre.TryStart(Player, armour), "needs a Research Centre");

            Place("research_centre", new Vector3(-29f, 0f, -25f));
            yield return null;
            int credits = _match.PlayerFaction.Wallet.Credits;
            Assert.AreEqual(ResearchCheck.Ok, ResearchCentre.TryStart(Player, armour));
            Assert.AreEqual(credits - armour.Cost, _match.PlayerFaction.Wallet.Credits, "paid when started");
            Assert.AreEqual(ResearchCheck.InProgress, ResearchCentre.TryStart(Player, armour));
            Assert.AreEqual(0f, existing.Armour - baseArmour, 1e-5f, "no effect before completion");

            Time.timeScale = 10f;
            var research = _match.PlayerFaction.Research;
            yield return Until(() => research.IsResearched(armour.Id), 80f); // game seconds (45 s topic)
            Time.timeScale = 1f;
            Assert.IsTrue(research.IsResearched(armour.Id), "research completes over time");
            Assert.AreEqual(baseArmour + armour.Value, existing.Armour, 1e-4f, "existing unit upgraded");

            var fresh = Spawn(_light, new Vector3(-28f, 0f, -30f), Player, armed: false).GetComponent<Health>();
            yield return null;
            Assert.AreEqual(baseArmour + armour.Value, fresh.Armour, 1e-4f, "new unit upgraded");
            var enemyTank = Spawn(_light, new Vector3(30f, 0f, 30f), Enemy, armed: false).GetComponent<Health>();
            yield return null;
            Assert.AreEqual(baseArmour, enemyTank.Armour, 1e-4f, "the other faction is unaffected");

            credits = _match.PlayerFaction.Wallet.Credits;
            Assert.AreEqual(ResearchCheck.AlreadyResearched, ResearchCentre.TryStart(Player, armour));
            Assert.AreEqual(credits, _match.PlayerFaction.Wallet.Credits, "never charged twice");
        }

        [UnityTest]
        public IEnumerator Research_DoesNotLeakIntoNextMatch()
        {
            var armour = _match.Setup.Research.First(r => r.Id == "composite_armour");
            _match.PlayerFaction.Research.Complete(armour);
            SceneManager.LoadScene("RTS_Match", LoadSceneMode.Single);
            yield return null;
            yield return null;
            Assert.IsFalse(MatchController.Current.PlayerFaction.Research.IsResearched(armour.Id));
            Assert.AreEqual(0f, MatchController.Current.PlayerFaction.Research.ArmourBonus);
        }

        // ---------- Rocket artillery ----------

        [UnityTest]
        public IEnumerator Artillery_IsLockedUntilResearchCentre()
        {
            Assert.IsFalse(_factory.IsUnlocked(_artillery));
            int credits = _match.PlayerFaction.Wallet.Credits;
            Assert.AreEqual(EnqueueResult.Locked, _factory.Enqueue(_artillery));
            Assert.AreEqual(credits, _match.PlayerFaction.Wallet.Credits, "nothing charged");
            Place("research_centre", new Vector3(-29f, 0f, -25f));
            yield return null;
            Assert.IsTrue(_factory.IsUnlocked(_artillery));
            Assert.AreEqual(EnqueueResult.Ok, _factory.Enqueue(_artillery));
        }

        [UnityTest]
        public IEnumerator Artillery_HoldsFireInsideMinimumRange_AndSplashesClusters()
        {
            var arty = Spawn(_artillery, new Vector3(0f, 0f, -20f), Player).GetComponent<CombatUnit>();
            var close = Spawn(_light, new Vector3(0f, 0f, -15f), Enemy, armed: false);
            Time.timeScale = 2f;
            yield return new WaitForSeconds(1.5f);
            Assert.AreEqual(0, arty.ShotsFired, "no firing at 5 m (minimum range 9)");
            Object.Destroy(close);

            // A tight cluster 20 m away, spotted by a recon so the artillery may engage.
            Spawn(_recon, new Vector3(0f, 0f, -12f), Player, armed: false);
            var a = Spawn(_light, new Vector3(-0.8f, 0f, 0f), Enemy, armed: false).GetComponent<Health>();
            var b = Spawn(_light, new Vector3(0.8f, 0f, 0f), Enemy, armed: false).GetComponent<Health>();
            yield return Until(() => a.Current < a.Max && b.Current < b.Max, 15f);
            Assert.Greater(arty.ShotsFired, 0, "fires at targets inside its band");
            Assert.Less(a.Current, a.Max);
            Assert.Less(b.Current, b.Max, "splash damages both vehicles in the cluster");
        }
    }
}
