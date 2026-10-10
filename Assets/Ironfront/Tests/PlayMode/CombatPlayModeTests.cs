using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Ironfront.Buildings;
using Ironfront.Combat;
using Ironfront.Commands;
using Ironfront.Core.Combat;
using Ironfront.Data;
using Ironfront.Economy;
using Ironfront.Match;
using Ironfront.Selection;
using Ironfront.Testing;
using Ironfront.Units;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ironfront.Tests
{
    /// <summary>
    /// Combat in the real RTS_Sandbox scene. Test units are spawned in the open south-east of the map, away
    /// from the player base and the enemy scenario. Time runs 4x faster; behaviour is unchanged.
    /// </summary>
    public class CombatPlayModeTests
    {
        private const float TimeScale = 4f;
        private const int Player = 1;
        private const int Enemy = 2;

        private MatchController _match;
        private SelectionManager _selection;
        private UnitCommandController _commands;
        private UnitDefinition _light, _heavy;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            SceneManager.LoadScene("RTS_Sandbox", LoadSceneMode.Single);
            yield return null;
            yield return null;
            _match = MatchController.Current;
            _selection = Object.FindAnyObjectByType<SelectionManager>();
            _commands = Object.FindAnyObjectByType<UnitCommandController>();
            var factory = _match.Setup.ConstructionOptions.First(d => d.Id == "vehicle_factory");
            _light = factory.ProducibleUnits.First(u => u.Id == "light_tank");
            _heavy = factory.ProducibleUnits.First(u => u.Id == "heavy_tank");
            Time.timeScale = TimeScale;
        }

        [TearDown]
        public void RestoreTime() => Time.timeScale = 1f;

        // ---------- Helpers ----------

        private static GameObject Spawn(GameObject prefab, Vector3 position, int faction)
        {
            var go = Object.Instantiate(prefab, position, Quaternion.identity);
            go.GetComponent<FactionMember>().FactionId = faction;
            return go;
        }

        private CombatUnit SpawnTank(UnitDefinition def, Vector3 position, int faction) =>
            Spawn(def.Prefab, position, faction).GetComponent<CombatUnit>();

        /// <summary>An unarmed enemy target: a copy of one of the scene's capsule scouts.</summary>
        private static Health SpawnScout(Vector3 position, int faction)
        {
            var template = GameObject.Find("Unit_01_Red");
            var go = Object.Instantiate(template, position + Vector3.up, Quaternion.identity);
            go.name = "TestScout";
            go.GetComponent<FactionMember>().FactionId = faction;
            return go.GetComponent<Health>();
        }

        private void Select(params CombatUnit[] units) =>
            _selection.Selection.Replace(units.Select(u => u.GetComponent<SelectableUnit>()));

        private static IEnumerator WaitFor(Func<bool> condition, float gameSeconds)
        {
            float t = 0f;
            while (!condition() && t < gameSeconds)
            {
                t += Time.deltaTime;
                yield return null;
            }
        }

        private static bool Gone(Object o) => o == null;

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        // ---------- Scenario & data ----------

        [UnityTest]
        public IEnumerator Scenario_HasDistinctEnemyFaction_WithStationaryMovingAndBuildingTargets()
        {
            var enemies = CombatUnit.All.Where(u => FactionMember.Of(u) == Enemy).ToList();
            Assert.AreEqual(5, enemies.Count, "Five enemy test tanks");
            Assert.AreEqual(3, enemies.Count(u => u.GetComponent<ScenarioStartOrder>() != null), "Three holding");
            var patrols = enemies.Where(u => u.GetComponent<PatrolRoute>() != null).ToList();
            Assert.AreEqual(2, patrols.Count, "Two patrolling");
            var outpost = Building.All.Single(b => b.Definition.Id == "enemy_outpost");
            Assert.AreEqual(Enemy, outpost.FactionId);
            Assert.IsNotNull(outpost.GetComponent<Health>());

            // Enemy tint differs from the player's.
            var block = new MaterialPropertyBlock();
            var tinted = enemies[0].GetComponent<FactionColorizer>();
            Assert.IsNotNull(tinted);
            var turret = enemies[0].transform.Find("TurretPivot/Turret").GetComponent<Renderer>();
            turret.GetPropertyBlock(block);
            Color expected = _match.Setup.ColorOf(Enemy), actual = block.GetColor("_BaseColor");
            Assert.Less(Mathf.Abs(expected.r - actual.r) + Mathf.Abs(expected.g - actual.g) + Mathf.Abs(expected.b - actual.b), 0.01f,
                $"Enemy turret tinted {expected}, was {actual}");
            Assert.AreNotEqual(_match.Setup.ColorOf(Player), _match.Setup.ColorOf(Enemy));

            var start = patrols.Select(p => p.transform.position).ToList();
            yield return WaitFor(() => false, 3f);
            for (int i = 0; i < patrols.Count; i++)
                Assert.Greater(Flat(patrols[i].transform.position - start[i]).magnitude, 2f, "Patrol units move");
            Assert.IsTrue(enemies.Where(u => u.GetComponent<ScenarioStartOrder>() != null)
                .All(u => u.Machine.Order == CombatOrderType.Hold));
        }

        [Test]
        public void LightAndHeavyTank_DifferInEveryCombatStat()
        {
            Assert.AreNotEqual(_light.MaxHealth, _heavy.MaxHealth);
            Assert.AreNotEqual(_light.Armour, _heavy.Armour);
            Assert.AreNotEqual(_light.MoveSpeed, _heavy.MoveSpeed);
            Assert.AreNotEqual(_light.Cost, _heavy.Cost);
            Assert.AreNotEqual(_light.Weapon.Damage, _heavy.Weapon.Damage);
            Assert.AreNotEqual(_light.Weapon.Range, _heavy.Weapon.Range);
            Assert.AreNotEqual(_light.Weapon.Cooldown, _heavy.Weapon.Cooldown);
            Assert.AreNotEqual(_light.Prefab, _heavy.Prefab);
        }

        [Test]
        public void FriendlyFire_IsIgnored_EnemyFireDamages()
        {
            var harvester = Object.FindAnyObjectByType<Harvester>().GetComponent<Health>();
            Assert.AreEqual(0f, harvester.ApplyDamage(100f, Player));
            Assert.AreEqual(harvester.Max, harvester.Current);
            Assert.AreEqual(70f, harvester.ApplyDamage(100f, Enemy), 1e-3f, "Harvester armour 30%");
        }

        // ---------- Orders ----------

        [UnityTest]
        public IEnumerator Attack_ClosesInFiresAtCooldown_DestroysTarget_AndCleansUp()
        {
            var heavy = SpawnTank(_heavy, new Vector3(30f, 0f, -45f), Player);
            var enemy = SpawnTank(_light, new Vector3(30f, 0f, -24f), Enemy);
            yield return null;
            enemy.OrderHold();
            var enemyHealth = enemy.GetComponent<Health>();

            var shotTimes = new List<float>();
            var shotRanges = new List<float>();
            heavy.Fired += t => { shotTimes.Add(Time.time); shotRanges.Add(t.DistanceFrom(heavy.transform.position)); };

            Select(heavy);
            Assert.AreEqual(1, _commands.IssueAttack(enemyHealth));
            Assert.AreEqual(CombatOrderType.Attack, heavy.Machine.Order);

            yield return WaitFor(() => Gone(enemy), 60f);

            Assert.IsTrue(Gone(enemy), "Enemy destroyed");
            Assert.IsFalse(Health.All.Contains(enemyHealth));
            Assert.IsFalse(CombatUnit.All.Contains(enemy));
            Assert.GreaterOrEqual(shotTimes.Count, 4, "300 HP / 63 per hit = 5 hits (one may still be in flight)");
            Assert.IsTrue(shotRanges.All(d => d <= _heavy.Weapon.Range + 0.01f), "Only fired within range");
            for (int i = 1; i < shotTimes.Count; i++)
                Assert.GreaterOrEqual(shotTimes[i] - shotTimes[i - 1], _heavy.Weapon.Cooldown - 0.05f, "Cooldown respected");

            yield return null;
            Assert.AreEqual(CombatOrderType.Idle, heavy.Machine.Order);
            Assert.IsNull(heavy.CurrentTarget);
            StringAssert.Contains("destroyed", heavy.DescribeOrder());
            Assert.IsTrue(_selection.Selection.Contains(heavy.GetComponent<SelectableUnit>()), "Survivor stays selected and controllable");
        }

        [UnityTest]
        public IEnumerator MultiUnit_AttackBuilding_DestroysIt_UnregistersIt_AndFreesNavigation()
        {
            var outpostDef = Building.All.First(b => b.Definition.Id == "enemy_outpost").Definition;
            var outpost = Spawn(outpostDef.Prefab, new Vector3(36f, 0f, -40f), Enemy).GetComponent<Building>();
            var attackers = new[]
            {
                SpawnTank(_heavy, new Vector3(31f, 0f, -24f), Player),
                SpawnTank(_heavy, new Vector3(36f, 0f, -24f), Player),
                SpawnTank(_light, new Vector3(41f, 0f, -24f), Player),
            };
            yield return WaitFor(() => false, 1.5f); // carving starts once the obstacle has been stationary (0.5 s)
            var enemyFaction = _match.GetFaction(Enemy);
            int outpostsBefore = enemyFaction.Structures.Count("enemy_outpost");
            Assert.IsFalse(NavMesh.SamplePosition(outpost.transform.position, out _, 0.3f, NavMesh.AllAreas), "Outpost carves the NavMesh");

            Select(attackers);
            Assert.AreEqual(3, _commands.IssueAttack(outpost.GetComponent<Health>()));
            yield return WaitFor(() => Gone(outpost), 90f);

            Assert.IsTrue(Gone(outpost), "Outpost destroyed by the group");
            Assert.AreEqual(outpostsBefore - 1, enemyFaction.Structures.Count("enemy_outpost"));
            Assert.IsTrue(attackers.All(a => a != null && a.GetComponent<Health>().IsAlive));
            Assert.IsTrue(attackers.All(a => a.ShotsFired > 0), "Every unit in the group fired");

            yield return WaitFor(() => false, 1f);
            Assert.IsTrue(NavMesh.SamplePosition(new Vector3(36f, 0f, -40f), out _, 0.5f, NavMesh.AllAreas),
                "NavMesh carve removed after destruction");
            Assert.IsTrue(attackers.All(a => a.Machine.Order == CombatOrderType.Idle));
        }

        [UnityTest]
        public IEnumerator AttackMove_EngagesThreatOnTheWay_ThenReachesDestination()
        {
            var heavy = SpawnTank(_heavy, new Vector3(18f, 0f, -44f), Player);
            var enemy = SpawnTank(_light, new Vector3(32f, 0f, -35f), Enemy);
            yield return null;
            enemy.OrderHold();

            Select(heavy);
            var destination = new Vector3(46f, 0f, -44f);
            Assert.AreEqual(1, _commands.IssueAttackMove(destination));
            Assert.AreEqual(CombatOrderType.AttackMove, heavy.Machine.Order);

            yield return WaitFor(() => Gone(enemy), 60f);
            Assert.IsTrue(Gone(enemy), "Threat on the route was destroyed");
            Assert.Greater(heavy.ShotsFired, 0);

            yield return WaitFor(() => heavy.Machine.Order == CombatOrderType.Idle, 30f);
            Assert.AreEqual(CombatOrderType.Idle, heavy.Machine.Order, "Attack-move completed");
            Assert.Less(Flat(heavy.transform.position - destination).magnitude, 3f, "Resumed and reached the destination");
        }

        [UnityTest]
        public IEnumerator Move_TravelsPastEnemyWithoutFiring()
        {
            var light = SpawnTank(_light, new Vector3(18f, 0f, -30f), Player);
            var scout = SpawnScout(new Vector3(32f, 0f, -26f), Enemy);
            yield return null;

            Select(light);
            var destination = new Vector3(46f, 0f, -30f);
            _commands.IssueMove(destination);
            Assert.AreEqual(CombatOrderType.Move, light.Machine.Order);

            yield return WaitFor(() => light.Machine.Order != CombatOrderType.Move, 30f);
            Assert.AreEqual(0, light.ShotsFired, "Move never engages, even with an enemy in range on the way");
            Assert.Less(Flat(light.transform.position - destination).magnitude, 2f);
            Assert.IsTrue(scout.IsAlive);
        }

        [UnityTest]
        public IEnumerator Hold_EngagesOnlyInRange_AndNeverPursues()
        {
            var heavy = SpawnTank(_heavy, new Vector3(30f, 0f, -40f), Player);
            var far = SpawnScout(new Vector3(36f, 0f, -16f), Enemy);
            yield return WaitFor(() => false, 0.5f);
            Select(heavy);
            _commands.IssueHold();
            Vector3 holdAt = heavy.transform.position;

            yield return WaitFor(() => false, 4f);
            Assert.AreEqual(0, heavy.ShotsFired, "Enemy out of range: no fire");
            Assert.Less(Flat(heavy.transform.position - holdAt).magnitude, 0.3f, "...and no pursuit");

            var near = SpawnScout(new Vector3(33f, 0f, -31f), Enemy);
            yield return WaitFor(() => Gone(near), 20f);
            Assert.IsTrue(Gone(near), "Target in range destroyed");
            Assert.Greater(heavy.ShotsFired, 0);
            Assert.Less(Flat(heavy.transform.position - holdAt).magnitude, 0.3f, "Fired without moving");
            Assert.AreEqual(CombatOrderType.Hold, heavy.Machine.Order, "Still holding after the kill");
            Assert.IsTrue(far.IsAlive);
        }

        [UnityTest]
        public IEnumerator Stop_CancelsAttack_AndUnitComesToRest()
        {
            var light = SpawnTank(_light, new Vector3(20f, 0f, -45f), Player);
            var scout = SpawnScout(new Vector3(46f, 0f, -45f), Enemy);
            yield return null;
            Select(light);
            _commands.IssueAttack(scout);
            yield return WaitFor(() => false, 1.5f);
            Assert.Greater(light.GetComponent<NavMeshAgent>().velocity.magnitude, 0.5f, "Closing in");

            _commands.IssueStop();
            Assert.AreEqual(CombatOrderType.Idle, light.Machine.Order);
            Assert.IsNull(light.CurrentTarget);
            yield return WaitFor(() => false, 1.5f);
            Vector3 rest = light.transform.position;
            yield return WaitFor(() => false, 2f);
            Assert.Less(Flat(light.transform.position - rest).magnitude, 0.2f, "Stopped and stays stopped");
            Assert.AreEqual(0, light.ShotsFired);
        }

        [UnityTest]
        public IEnumerator LineOfSight_WallBlocksFire_AttackOrderRepositionsToShoot()
        {
            // Central wall: x -18..18, z -1..1, 3 m high.
            var heavy = SpawnTank(_heavy, new Vector3(0f, 0f, -5f), Player);
            var scout = SpawnScout(new Vector3(0f, 0f, 4f), Enemy);
            yield return WaitFor(() => false, 0.5f);
            Select(heavy);
            _commands.IssueHold();
            Assert.LessOrEqual(scout.DistanceFrom(heavy.transform.position), _heavy.Weapon.Range, "Target is within range");
            Assert.IsFalse(heavy.HasLineOfSight(scout), "...but behind the wall");

            yield return WaitFor(() => false, 4f);
            Assert.AreEqual(0, heavy.ShotsFired, "No shots through the wall");

            _commands.IssueAttack(scout);
            yield return WaitFor(() => Gone(scout), 90f);
            Assert.IsTrue(Gone(scout), "Repositioned around the wall and destroyed the target");
            Assert.Greater(heavy.ShotsFired, 0);
        }

        [UnityTest]
        public IEnumerator MovingTarget_IsTracked_ThenNextTargetReacquired()
        {
            var heavy = SpawnTank(_heavy, new Vector3(35f, 0f, -26f), Player);
            var runner = SpawnScout(new Vector3(25f, 0f, -44f), Enemy);
            var runnerMover = runner.GetComponent<UnitMover>();
            var ends = new[] { new Vector3(46f, 0f, -44f), new Vector3(20f, 0f, -44f) };
            int leg = 0;
            yield return null;
            runnerMover.MoveTo(ends[0]);

            Select(heavy);
            _commands.IssueAttack(runner);
            float t = 0f;
            while (!Gone(runner) && t < 60f)
            {
                if (runnerMover.HasArrived) runnerMover.MoveTo(ends[++leg % 2]);
                t += Time.deltaTime;
                yield return null;
            }
            Assert.IsTrue(Gone(runner), "Moving target destroyed");
            yield return null;
            Assert.AreEqual(CombatOrderType.Idle, heavy.Machine.Order);

            // Target lost: a new enemy entering range is acquired automatically.
            int shots = heavy.ShotsFired;
            var next = SpawnScout(heavy.transform.position + new Vector3(6f, 0f, -6f), Enemy);
            yield return WaitFor(() => Gone(next), 20f);
            Assert.IsTrue(Gone(next), "Reacquired and destroyed the next target");
            Assert.Greater(heavy.ShotsFired, shots);
        }

        [UnityTest]
        public IEnumerator SelectedUnitDestroyed_IsRemovedEverywhere_WithoutErrors()
        {
            var light = SpawnTank(_light, new Vector3(30f, 0f, -40f), Player);
            var killer = SpawnTank(_heavy, new Vector3(30f, 0f, -31f), Enemy);
            yield return null;
            killer.OrderHold();
            Select(light);
            var lightUnit = light.GetComponent<SelectableUnit>();
            var lightHealth = light.GetComponent<Health>();
            var orders = Object.FindAnyObjectByType<OrderInputController>();
            Assert.IsTrue(orders.HasArmedSelection);

            yield return WaitFor(() => Gone(light), 60f);
            Assert.IsTrue(Gone(light), "Selected light tank destroyed by the heavy");
            Assert.AreEqual(0, _selection.Selection.Count, "Removed from the selection");
            Assert.IsFalse(SelectableUnit.All.Contains(lightUnit));
            Assert.IsFalse(Health.All.Contains(lightHealth));
            Assert.IsFalse(CombatUnit.All.Contains(light));
            Assert.IsFalse(orders.HasArmedSelection);

            yield return WaitFor(() => false, 1f); // HUD, feedback and the killer's targeting keep running
            Assert.IsNull(killer.CurrentTarget, "Killer dropped the dead target");
            Assert.AreEqual(CombatOrderType.Hold, killer.Machine.Order);
            Assert.Greater(killer.ShotsFired, 0);
        }
    }
}
