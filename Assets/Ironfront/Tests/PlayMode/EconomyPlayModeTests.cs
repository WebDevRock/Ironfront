using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Ironfront.Buildings;
using Ironfront.Commands;
using Ironfront.Construction;
using Ironfront.Core.Construction;
using Ironfront.Core.Economy;
using Ironfront.Core.Harvesting;
using Ironfront.Core.Production;
using Ironfront.Data;
using Ironfront.Economy;
using Ironfront.Match;
using Ironfront.Selection;
using Ironfront.Units;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Ironfront.Tests
{
    /// <summary>
    /// Economy, construction and production running in the real RTS_Sandbox scene. Time is sped up so
    /// harvest cycles and production finish quickly; the simulation itself is unchanged.
    /// </summary>
    public class EconomyPlayModeTests
    {
        private const string SceneName = "RTS_Sandbox";
        private const float TimeScale = 4f;

        // Sites that are valid in the generated scene (see EconomyContentBuilder layout).
        private static readonly Vector3 PowerPlantSite = new Vector3(-40f, 0f, -25f);
        private static readonly Vector3 FactorySite = new Vector3(-38f, 0f, -12f);

        private MatchController _match;
        private FactionState _player;
        private BuildingPlacementController _placement;
        private SelectionManager _selection;
        private UnitCommandController _commands;
        private MatchSetup _setup;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            SceneManager.LoadScene(SceneName, LoadSceneMode.Single);
            yield return null;
            yield return null;

            _match = MatchController.Current;
            _player = _match != null ? _match.PlayerFaction : null;
            _setup = _match != null ? _match.Setup : null;
            _placement = Object.FindAnyObjectByType<BuildingPlacementController>();
            _selection = Object.FindAnyObjectByType<SelectionManager>();
            _commands = Object.FindAnyObjectByType<UnitCommandController>();
        }

        [TearDown]
        public void RestoreTime() => Time.timeScale = 1f;

        private BuildingDefinition Def(string id) => _setup.ConstructionOptions.First(d => d.Id == id);

        private static List<Building> BuildingsWith(string id) =>
            Building.All.Where(b => b.Definition != null && b.Definition.Id == id).ToList();

        [Test]
        public void StartState_MatchesAcceptanceScenario()
        {
            Assert.IsNotNull(_match, "MatchController missing");
            Assert.IsNotNull(_placement, "BuildingPlacementController missing");
            Assert.AreEqual(1, BuildingsWith("headquarters").Count, "One headquarters");
            Assert.AreEqual(1, BuildingsWith("refinery").Count, "One refinery");
            Assert.AreEqual(1, Object.FindObjectsByType<Harvester>().Length, "One harvester");
            Assert.AreEqual(1, OreDeposit.All.Count, "One ore deposit");
            Assert.IsTrue(OreDeposit.All[0].Remaining > 0);

            int needed = Def("power_plant").Cost + Def("vehicle_factory").Cost;
            Assert.GreaterOrEqual(_player.Wallet.Credits, needed, "Starting credits cover a power plant and a factory");
            Assert.IsTrue(_player.Structures.Has("headquarters"));
            Assert.IsFalse(_player.Power.IsLowPower);

            var creditsLabel = Object.FindObjectsByType<UnityEngine.UI.Text>()
                .FirstOrDefault(t => t.text.StartsWith("Credits:"));
            Assert.IsNotNull(creditsLabel, "HUD shows credits");
            Assert.AreEqual($"Credits: {_player.Wallet.Credits}", creditsLabel.text);
        }

        [UnityTest]
        public IEnumerator Harvester_CollectsOre_AndCreditsIncreaseByAmountDelivered()
        {
            Time.timeScale = TimeScale;
            var deposit = OreDeposit.All[0];
            var harvester = Object.FindAnyObjectByType<Harvester>();
            int startCredits = _player.Wallet.Credits;
            int startOre = deposit.Remaining;
            var seen = new HashSet<HarvesterState>();

            float timeout = 60f;
            while (_player.Wallet.Credits < startCredits + 400 && timeout > 0f)
            {
                seen.Add(harvester.State);
                timeout -= Time.deltaTime;
                yield return null;
            }

            Assert.GreaterOrEqual(_player.Wallet.Credits, startCredits + 400, "Two loads delivered within 60 game-seconds");
            CollectionAssert.IsSupersetOf(seen, new[]
            {
                HarvesterState.MovingToSource, HarvesterState.Harvesting, HarvesterState.MovingToSink, HarvesterState.Unloading,
            });
            int mined = startOre - deposit.Remaining;
            Assert.AreEqual(mined, _player.Wallet.Credits - startCredits + harvester.Machine.Cargo,
                "Credits gained + cargo on board equals ore mined (1 credit per ore)");
        }

        [UnityTest]
        public IEnumerator DepletedDeposit_Disappears_HarvesterDeliversRemainder_AndWaits()
        {
            Time.timeScale = TimeScale;
            var deposit = OreDeposit.All[0];
            var harvester = Object.FindAnyObjectByType<Harvester>();
            deposit.SetRemaining(60);
            int depletedEvents = 0;
            void OnDepleted(OreDeposit _) => depletedEvents++;
            OreDeposit.AnyDepleted += OnDepleted;

            float timeout = 60f;
            while (harvester.State != HarvesterState.NoSourceAvailable && timeout > 0f)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }
            OreDeposit.AnyDepleted -= OnDepleted;

            Assert.AreEqual(HarvesterState.NoSourceAvailable, harvester.State, "Harvester waits once all ore is gone");
            Assert.AreEqual(0, harvester.Machine.Cargo, "Partial load was delivered");
            Assert.IsTrue(deposit.IsDepleted);
            Assert.AreEqual(1, depletedEvents);
            Assert.IsFalse(deposit.GetComponent<Collider>().enabled, "Depleted deposit can no longer be clicked");
            Assert.IsFalse(deposit.transform.Find("Crystals").gameObject.activeSelf, "Depleted deposit is no longer drawn");
            StringAssert.Contains("no ore", harvester.StatusText);
        }

        [Test]
        public void Placement_RejectsInvalidSites_AndAcceptsOpenGround()
        {
            var pp = Def("power_plant");
            var refinery = BuildingsWith("refinery")[0];

            Assert.AreEqual(PlacementResult.OverlapsStructure, _placement.Validate(pp, EconomyHq()));
            Assert.AreEqual(PlacementResult.BlocksAccess, _placement.Validate(pp, refinery.FrontPoint + Vector3.forward * 2f));
            Assert.AreEqual(PlacementResult.OverlapsObstacle, _placement.Validate(pp, new Vector3(0f, 0f, 0f)), "Central wall");
            Assert.AreEqual(PlacementResult.OutOfBounds, _placement.Validate(pp, new Vector3(49f, 0f, 0f)));
            Assert.AreEqual(PlacementResult.OverlapsResource, _placement.Validate(pp, OreDeposit.All[0].transform.position + Vector3.right * 4f));
            Assert.AreEqual(PlacementResult.Valid, _placement.Validate(pp, PowerPlantSite));
            Assert.AreEqual(PlacementResult.Valid, _placement.Validate(Def("vehicle_factory"), FactorySite));
        }

        [Test]
        public void Placement_FailedAttempts_DoNotSpendCredits()
        {
            int credits = _player.Wallet.Credits;
            Assert.IsFalse(_placement.TryPlace(Def("power_plant"), EconomyHq(), _player.Id, out _, out var reason));
            StringAssert.Contains("structure", reason);
            Assert.AreEqual(credits, _player.Wallet.Credits);

            _player.Wallet.TrySpend(_player.Wallet.Credits - 100);
            Assert.IsFalse(_placement.TryPlace(Def("power_plant"), PowerPlantSite, _player.Id, out _, out reason));
            StringAssert.Contains("insufficient", reason);
            Assert.AreEqual(100, _player.Wallet.Credits);
        }

        [UnityTest]
        public IEnumerator AcceptanceScenario_HarvestBuildPowerPlantAndFactory_ProduceTwoTanks()
        {
            Time.timeScale = TimeScale;
            int lowestCredits = int.MaxValue;

            // Construct both buildings with the starting credits.
            Assert.IsTrue(_placement.TryPlace(Def("power_plant"), PowerPlantSite, _player.Id, out var plant, out var r1), r1);
            Assert.IsTrue(_placement.TryPlace(Def("vehicle_factory"), FactorySite, _player.Id, out var factoryBuilding, out var r2), r2);
            yield return null; // let the new buildings register (Start)

            Assert.IsTrue(_player.Structures.Has("power_plant"));
            Assert.IsTrue(_player.Structures.Has("vehicle_factory"));
            Assert.IsFalse(_player.Power.IsLowPower, "Power plant covers the factory's drain");

            var factory = factoryBuilding.GetComponent<VehicleFactory>();
            var rally = factory.ExitPoint + new Vector3(4f, 0f, 6f);
            factory.SetRallyPoint(rally);

            var tankDef = factory.Building.Definition.ProducibleUnits.First(u => u.Id == "tank");
            var produced = new List<GameObject>();
            factory.UnitProduced += produced.Add;

            // Queue two tanks as soon as harvested credits allow.
            int queued = 0;
            float timeout = 240f;
            while (produced.Count < 2 && timeout > 0f)
            {
                lowestCredits = Mathf.Min(lowestCredits, _player.Wallet.Credits);
                if (queued < 2 && factory.Enqueue(tankDef) == EnqueueResult.Ok) queued++;
                timeout -= Time.deltaTime;
                yield return null;
            }

            Assert.AreEqual(2, produced.Count, "Two tanks produced within 240 game-seconds");
            Assert.GreaterOrEqual(lowestCredits, 0, "Credits never negative");

            // Produced tanks join the existing selection and movement systems.
            yield return null;
            var tanks = produced.Select(go => go.GetComponent<SelectableUnit>()).ToList();
            Assert.IsTrue(tanks.All(t => t != null && SelectableUnit.All.Contains(t)), "Tanks are registered as selectable");
            Assert.IsTrue(tanks.All(t => FactionMember.Of(t) == _player.Id), "Tanks belong to the player");
            Assert.IsTrue(tanks.All(t => t.GetComponent<NavMeshAgent>().isOnNavMesh), "Tanks are on the NavMesh");
            Assert.Greater((produced[0].transform.position - produced[1].transform.position).magnitude, 0.5f,
                "Tanks did not spawn on top of each other");

            // They head to the rally point.
            float wait = 20f;
            while (wait > 0f && tanks.Any(t => Flat(t.transform.position - rally).magnitude > 4f))
            {
                wait -= Time.deltaTime;
                yield return null;
            }
            Assert.IsTrue(tanks.All(t => Flat(t.transform.position - rally).magnitude <= 4f), "Tanks reached the rally point");

            // Click-select one tank, then box-select, then move them as a group.
            var cam = _selection.ViewCamera;
            _selection.ClickAt(cam.WorldToScreenPoint(tanks[0].transform.position + Vector3.up * 0.6f), false);
            Assert.IsTrue(_selection.Selection.Contains(tanks[0]), "Clicking a produced tank selects it");

            _selection.Selection.Replace(tanks);
            var target = new Vector3(-20f, 0f, -10f);
            Assert.AreEqual(2, _commands.IssueMove(target));
            wait = 30f;
            while (wait > 0f && tanks.Any(t => Flat(t.transform.position - target).magnitude > 3f))
            {
                wait -= Time.deltaTime;
                yield return null;
            }
            Assert.IsTrue(tanks.All(t => Flat(t.transform.position - target).magnitude <= 3f), "Tanks obeyed the move order");
        }

        [UnityTest]
        public IEnumerator Factory_CanBeSelected_AndQueueCancelRefunds()
        {
            Assert.IsTrue(_placement.TryPlace(Def("vehicle_factory"), FactorySite, _player.Id, out var b, out var reason), reason);
            yield return null;
            var factory = b.GetComponent<VehicleFactory>();

            _selection.ClickAt(_selection.ViewCamera.WorldToScreenPoint(b.transform.position + Vector3.up * 1.5f), false);
            Assert.AreSame(b, _selection.SelectedStructure, "Clicking the factory selects it");
            Assert.AreEqual(0, _selection.Selection.Count);

            var tank = factory.Building.Definition.ProducibleUnits.First(u => u.Id == "tank");
            int before = _player.Wallet.Credits;
            Assert.AreEqual(EnqueueResult.Ok, factory.Enqueue(tank));
            Assert.AreEqual(before - tank.Cost, _player.Wallet.Credits);
            Assert.IsTrue(factory.Cancel(0));
            Assert.AreEqual(before, _player.Wallet.Credits);
        }

        [UnityTest]
        public IEnumerator PlayerOrders_MoveInterruptsHarvesting_HarvestOrderResumesIt()
        {
            Time.timeScale = TimeScale;
            var harvester = Object.FindAnyObjectByType<Harvester>();
            var deposit = OreDeposit.All[0];
            _selection.Selection.Replace(new[] { harvester.GetComponent<SelectableUnit>() });

            var spot = new Vector3(-20f, 0f, -30f);
            Assert.AreEqual(1, _commands.IssueMove(spot));
            Assert.AreEqual(HarvesterState.Idle, harvester.State, "A move order takes the harvester out of its loop");

            float wait = 20f;
            while (wait > 0f && Flat(harvester.transform.position - spot).magnitude > 1.5f)
            {
                wait -= Time.deltaTime;
                yield return null;
            }
            Assert.LessOrEqual(Flat(harvester.transform.position - spot).magnitude, 1.5f, "Harvester obeyed the move order");
            Assert.AreEqual(HarvesterState.Idle, harvester.State, "It stays idle rather than wandering back");

            int credits = _player.Wallet.Credits;
            Assert.AreEqual(1, _commands.IssueHarvest(deposit));
            Assert.AreSame(deposit, harvester.Machine.Source);

            wait = 60f;
            while (wait > 0f && _player.Wallet.Credits <= credits)
            {
                wait -= Time.deltaTime;
                yield return null;
            }
            Assert.Greater(_player.Wallet.Credits, credits, "Harvest order resumed the delivery loop");
        }

        private static Vector3 EconomyHq() => BuildingsWith("headquarters")[0].transform.position;

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }
    }
}
