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
using Ironfront.Core.Skirmish;
using Ironfront.Data;
using Ironfront.Economy;
using Ironfront.Match;
using Ironfront.Selection;
using Ironfront.Skirmish;
using Ironfront.UI;
using Ironfront.Units;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Ironfront.Tests
{
    /// <summary>
    /// Milestone 010 in a real skirmish map (Dustfall Plains, fixed seed; computer commanders and harvesters off unless a
    /// test needs them). Covers construction territory, the territory overlay, building sales, MCV production and
    /// deployment, the Forward Command Centre, the Repair Pad, victory rules, the computer commander and restart.
    /// </summary>
    public class Milestone010PlayModeTests
    {
        private const int Player = 1;
        private const int Enemy = 2;
        private MatchController _match;
        private BuildingPlacementController _placement;
        private Vector3 _hq;

        [UnitySetUp]
        public IEnumerator Load()
        {
            SkirmishSession.Reset();
            SkirmishSession.Select(new SkirmishSettings { MapId = "dustfall_plains", Seed = 1 });
            SceneManager.LoadScene("Skirmish_DustfallPlains", LoadSceneMode.Single);
            yield return null;
            yield return null;
            foreach (var ai in EnemyCommander.All.ToList()) ai.enabled = false;
            foreach (var hv in Object.FindObjectsByType<Harvester>()) { hv.StopHarvesting(); hv.enabled = false; }
            _match = MatchController.Current;
            _placement = Object.FindAnyObjectByType<BuildingPlacementController>();
            _match.PlayerFaction.Wallet.Add(50000);
            _match.PlayerFaction.RegisterStructure("test_generator", 400, 0);
            _hq = Building.All.First(b => b.FactionId == Player && b.Definition.Id == "headquarters").transform.position;
        }

        [TearDown]
        public void Restore()
        {
            Time.timeScale = 1f;
            SkirmishSession.Reset();
        }

        private BuildingDefinition Def(string id) => _match.Setup.ConstructionOptions.First(d => d.Id == id);
        private UnitDefinition McvDef => Def("vehicle_factory").ProducibleUnits.First(u => u.Id == "mcv");
        private BuildingDefinition CentreDef => McvDef.Prefab.GetComponent<MobileCommandVehicle>().DeploysInto;
        private int Credits => _match.PlayerFaction.Wallet.Credits;
        private static Vector3 Towards(Vector3 from, float t) => Vector3.Lerp(from, Vector3.zero, t);

        /// <summary>Places a structure inside the faction's territory, nearest to <paramref name="distance"/> from the anchor.</summary>
        private Building Place(string id, int faction, float distance, Vector3? near = null)
        {
            var def = Def(id);
            var anchor = near ?? _hq;
            foreach (var c in SiteSearch.Ranked(anchor, Towards(anchor, 0.3f), distance, distance + 40f, 2f))
                if (_placement.Validate(def, c, false, faction) == PlacementResult.Valid && _placement.TryPlace(def, c, faction, out var b, out _))
                    return b;
            Assert.Fail("no valid site for " + id);
            return null;
        }

        /// <summary>A point where a Forward Command Centre may be deployed, about <paramref name="distance"/> from the HQ.</summary>
        private Vector3 DeploySite(float distance, int faction = Player, Vector3? from = null)
        {
            var anchor = from ?? _hq;
            foreach (var c in SiteSearch.Ranked(anchor, Towards(anchor, 0.5f), distance, distance + 40f, 3f))
                if (_placement.ValidateDeployment(CentreDef, c, faction, null) == DeployCheck.Valid) return c;
            Assert.Fail("no deployment site");
            return default;
        }

        private MobileCommandVehicle SpawnMcv(Vector3 at, int faction = Player)
        {
            var p = NavMesh.SamplePosition(at, out var hit, 3f, NavMesh.AllAreas) ? hit.position : at;
            var go = FactionSpawner.Spawn(McvDef.Prefab, p, Quaternion.identity, faction);
            return go.GetComponent<MobileCommandVehicle>();
        }

        private IEnumerator Until(System.Func<bool> condition, float gameSeconds)
        {
            float end = Time.time + gameSeconds;
            while (!condition() && Time.time < end) yield return null;
        }

        /// <summary>Spawns, deploys and waits for an MCV at a site; returns the new centre.</summary>
        private IEnumerator Establish(Vector3 site, List<Building> result, float healthFraction = 1f)
        {
            var mcv = SpawnMcv(site);
            yield return null;
            yield return null;
            if (healthFraction < 1f)
            {
                var h = mcv.Health;
                h.ApplyDamage(h.Max * (1f - healthFraction) / (1f - h.Armour), Enemy);
            }
            Assert.AreEqual(DeployCheck.Valid, mcv.Deploy(), "deploy at a valid site");
            Time.timeScale = 8f;
            yield return Until(() => mcv == null, mcv.DeploySeconds + 5f);
            Time.timeScale = 1f;
            Assert.IsTrue(mcv == null, "the vehicle was replaced");
            result.Add(Building.All.Last(b => b.FactionId == Player && b.Definition.Id == "forward_command"));
            yield return null;
        }

        // ---------- Territory ----------

        [UnityTest, Timeout(120000)]
        public IEnumerator Construction_OutsideTerritory_IsRejected_AndTheWholeFootprintMustFit()
        {
            var factory = Def("vehicle_factory");
            // Far from the base: the terrain allows it, territory does not.
            Vector3 far = default;
            foreach (var c in SiteSearch.Ranked(_hq, Towards(_hq, 0.5f), 120f, 160f, 4f))
                if (_placement.Validate(factory, c) == PlacementResult.Valid) { far = c; break; }
            Assert.AreNotEqual(default(Vector3), far);
            Assert.AreEqual(PlacementResult.OutsideTerritory, _placement.Validate(factory, far, false, Player));
            int before = Credits;
            Assert.IsFalse(_placement.TryPlace(factory, far, Player, out _, out var reason));
            StringAssert.Contains("Outside your construction territory", reason);
            Assert.AreEqual(before, Credits, "nothing is charged");

            // A footprint whose centre is inside but whose corner sticks out is refused too.
            var hqDef = Building.All.First(b => b.FactionId == Player && b.Definition.Id == "headquarters").Definition;
            Assert.AreEqual(55f, hqDef.TerritoryRadius);
            float radius = hqDef.TerritoryRadius;
            var dir = (Vector3.zero - _hq).normalized;
            bool foundEdge = false;
            for (float d = radius - 4f; d > radius - 6f; d -= 0.5f)
            {
                var c = _hq + dir * d;
                c = new Vector3(Mathf.Round(c.x), 0f, Mathf.Round(c.z));
                if (!TerritorySystem.CoversPoint(Player, c)) continue;
                var r = _placement.Validate(factory, c, false, Player);
                if (r == PlacementResult.OutsideTerritory) { foundEdge = true; break; }
            }
            Assert.IsTrue(foundEdge, "centre inside the territory, footprint partly outside: refused");

            // Inside: built.
            var b = Place("vehicle_factory", Player, 18f);
            Assert.IsTrue(TerritorySystem.Covers(Player, b.Footprint));
            Assert.AreEqual(before - factory.Cost, Credits);
            yield return null;
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Overlay_ShowsTerritoryOnlyWhilePlacing_AndNeverUnseenEnemyZones()
        {
            yield return null;
            var overlay = TerritoryOverlay.Ensure();
            Assert.IsNotNull(overlay);
            yield return null;
            Assert.IsFalse(overlay.IsShown, "hidden when not constructing");
            Assert.IsTrue(_placement.BeginPlacement(Def("power_plant")));
            yield return null;
            yield return null;
            Assert.IsTrue(overlay.IsShown);
            Assert.Greater(overlay.FillCells, 1500, "the HQ territory is filled (π·55² / 4 m² ≈ 2400 cells)");
            Assert.Greater(overlay.EdgeSegments, 100);
            Assert.AreEqual(0, overlay.EnemySegments, "the enemy base has not been seen: no zone is drawn");
            _placement.CancelPlacement();
            yield return null;
            Assert.IsFalse(overlay.IsShown);
        }

        // ---------- Sales ----------

        [UnityTest, Timeout(120000)]
        public IEnumerator Sale_RefundsOnceByHealth_CancelAndDuplicatesRefused_AndCleansUp()
        {
            var plant = Place("power_plant", Player, 16f);
            yield return null;
            var h = plant.GetComponent<Health>();
            h.ApplyDamage(h.Max * 0.4f / (1f - h.Armour), Enemy);
            float fraction = h.Current / h.Max;
            int produced = _match.PlayerFaction.Power.Produced;
            int start = Credits;

            Assert.IsTrue(plant.Sale.Begin(out _));
            Assert.IsFalse(plant.Sale.Begin(out var dup), "duplicate request refused");
            StringAssert.Contains("Already", dup);
            Assert.IsFalse(plant.Repair.SetRepairing(true), "no repairs while dismantling");
            Time.timeScale = 4f;
            yield return Until(() => plant.Sale.Progress01 > 0.5f, 10f);
            Assert.IsTrue(plant.Sale.Cancel(), "cancel before completion");
            yield return new WaitForSeconds(3f);
            Assert.AreEqual(start, Credits, "a cancelled sale pays nothing");
            Assert.IsTrue(plant != null && plant.Sale.State == SaleState.Idle);

            int expected = SaleRules.Refund(plant.Definition.SaleValue, h.Current, h.Max, TerritorySettings.Current.RefundFraction);
            Assert.AreEqual((int)System.Math.Floor(500 * 0.5f * fraction + 1e-4), expected);
            Assert.IsTrue(plant.Sale.Begin(out _));
            yield return Until(() => plant == null, TerritorySettings.Current.SellSeconds + 4f);
            Time.timeScale = 1f;
            Assert.IsTrue(plant == null, "the structure is removed");
            Assert.AreEqual(start + expected, Credits, "refunded exactly once");
            Assert.AreEqual(produced - 50, _match.PlayerFaction.Power.Produced, "its power is gone");
            Assert.AreEqual(1, BuildingSale.SoldBy(Player));
            yield return new WaitForSeconds(1f);
            Assert.AreEqual(start + expected, Credits, "no second refund later");
        }

        [UnityTest, Timeout(150000)]
        public IEnumerator Sale_RefundsQueuedUnitsAndLastResearch_AndPaysNothingIfDestroyed()
        {
            var factory = Place("vehicle_factory", Player, 18f).GetComponent<VehicleFactory>();
            yield return null; // the factory registers on Start (the research centre's prerequisite)
            var research = Place("research_centre", Player, 20f);
            yield return null;
            var light = Def("vehicle_factory").ProducibleUnits.First(u => u.Id == "light_tank");
            int lightsBefore = Object.FindObjectsByType<UnitIdentity>().Count(u => u.Definition == light && FactionMember.Of(u) == Player);
            int start = Credits;
            for (int i = 0; i < 3; i++) Assert.AreEqual(Core.Production.EnqueueResult.Ok, factory.Enqueue(light));
            var topic = _match.Setup.Research.First();
            Assert.AreEqual(Core.Research.ResearchCheck.Ok, ResearchCentre.TryStart(Player, topic));
            int paid = start - Credits;
            Assert.AreEqual(3 * light.Cost + topic.Cost, paid);

            int factoryRefund = factory.GetComponent<Building>().Sale.EstimatedRefund;
            int researchRefund = research.Sale.EstimatedRefund;
            Assert.IsTrue(factory.GetComponent<Building>().Sale.Begin(out _));
            Assert.IsTrue(research.Sale.Begin(out _));
            Time.timeScale = 2f; // the head unit would finish in 10 s of game time; the sale takes 8
            yield return Until(() => factory == null && research == null, TerritorySettings.Current.SellSeconds + 4f);
            Time.timeScale = 1f;
            Assert.IsTrue(factory == null && research == null);
            Assert.IsNull(_match.PlayerFaction.Research.Current, "the topic in progress was cancelled with the last centre");
            int unitsProduced = Object.FindObjectsByType<UnitIdentity>().Count(u => u.Definition == light && FactionMember.Of(u) == Player) - lightsBefore;
            Assert.AreEqual(start - unitsProduced * light.Cost + factoryRefund + researchRefund, Credits,
                "queued units and research are refunded in full on top of the sale refunds");

            // Destroyed while dismantling: no refund.
            var plant = Place("power_plant", Player, 16f);
            yield return null;
            int before = Credits;
            Assert.IsTrue(plant.Sale.Begin(out _));
            yield return new WaitForSeconds(1f);
            var hp = plant.GetComponent<Health>();
            hp.ApplyDamage(hp.Max * 10f, Enemy);
            yield return new WaitForSeconds(TerritorySettings.Current.SellSeconds);
            Assert.AreEqual(before, Credits, "a destroyed structure refunds nothing");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Sale_HqCannotBeSold_CriticalStructuresAskForConfirmation()
        {
            var hq = Building.All.First(b => b.FactionId == Player && b.Definition.Id == "headquarters");
            Assert.IsFalse(hq.Definition.Sellable);
            Assert.IsFalse(hq.Sale.Begin(out var why));
            StringAssert.Contains("cannot be sold", why);

            var factory = Place("vehicle_factory", Player, 18f);
            yield return null;
            var selection = Object.FindAnyObjectByType<SelectionManager>();
            selection.Selection.Clear();
            selection.SelectStructure(factory);
            var hud = RepairHud.Ensure();
            yield return null;
            hud.Refresh();
            var sell = Object.FindObjectsByType<Button>().First(b => b.name == "Sell");
            sell.onClick.Invoke();
            Assert.IsFalse(factory.Sale.IsDismantling, "first click only asks for confirmation");
            StringAssert.Contains("critical", hud.StatusText);
            sell.onClick.Invoke();
            Assert.IsTrue(factory.Sale.IsDismantling, "confirmed");
            Object.FindObjectsByType<Button>(FindObjectsInactive.Include).First(b => b.name == "Sell_Cancel").onClick.Invoke();
            Assert.IsFalse(factory.Sale.IsDismantling);

            var plant = Place("power_plant", Player, 16f);
            yield return null;
            selection.SelectStructure(plant);
            hud.Refresh();
            Object.FindObjectsByType<Button>().First(b => b.name == "Sell").onClick.Invoke();
            Assert.IsTrue(plant.Sale.IsDismantling, "ordinary structures sell on one click");
        }

        // ---------- MCV ----------

        [UnityTest, Timeout(150000)]
        public IEnumerator Mcv_IsProducedAtTheVehicleFactory_ForItsPrice()
        {
            Assert.AreEqual(5000, McvDef.Cost);
            Assert.IsNull(McvDef.Weapon, "unarmed");
            Assert.Less(McvDef.MoveSpeed, 5f, "slower than a heavy tank");
            Assert.GreaterOrEqual(McvDef.MaxHealth, 1500f);
            var factory = Place("vehicle_factory", Player, 18f).GetComponent<VehicleFactory>();
            yield return null;
            int start = Credits;
            Assert.AreEqual(Core.Production.EnqueueResult.Ok, factory.Enqueue(McvDef));
            Assert.AreEqual(start - 5000, Credits);
            Time.timeScale = 10f;
            yield return Until(() => MobileCommandVehicle.All.Any(m => m.FactionId == Player), McvDef.BuildTime + 5f);
            Time.timeScale = 1f;
            var mcv = MobileCommandVehicle.All.Single(m => m.FactionId == Player);
            Assert.IsNotNull(mcv.GetComponent<SelectableUnit>());
            Assert.IsNull(mcv.GetComponent<CombatUnit>());
            Assert.AreEqual(DeployState.Mobile, mcv.State);
        }

        [UnityTest, Timeout(150000)]
        public IEnumerator Deploy_CreatesExactlyOneForwardCommandCentre_WithTerritory_Health_AndAnObstacle()
        {
            var site = DeploySite(95f);
            Assert.IsFalse(TerritorySystem.CoversPoint(Player, site), "outside the existing territory: allowed for deployment");
            int structures = Building.All.Count(b => b.FactionId == Player);
            int units = Object.FindObjectsByType<SelectableUnit>().Length;
            var made = new List<Building>();
            yield return Establish(site, made, healthFraction: 0.6f);
            var centre = made.Single();
            Assert.AreEqual(structures + 1, Building.All.Count(b => b.FactionId == Player), "one structure added");
            Assert.AreEqual(1, Building.All.Count(b => b.Definition.Id == "forward_command"), "exactly one centre");
            Assert.AreEqual(units, Object.FindObjectsByType<SelectableUnit>().Length, "the vehicle is gone, nothing duplicated");
            Assert.AreEqual(0, MobileCommandVehicle.All.Count);
            var h = centre.GetComponent<Health>();
            Assert.AreEqual(0.6f, h.Current / h.Max, 0.02f, "health conversion: the vehicle's health fraction");
            Assert.AreEqual(Player, centre.FactionId);
            Assert.IsTrue(TerritorySystem.Covers(Player, centre.Footprint), "new construction territory registered immediately");
            Assert.IsTrue(TerritorySystem.CoversPoint(Player, centre.transform.position + Vector3.right * 35f));
            var obstacle = centre.GetComponent<NavMeshObstacle>();
            Assert.IsTrue(obstacle != null && obstacle.enabled && obstacle.carving, "navigation obstacle in place");
            Assert.IsNotNull(centre.GetComponent<Vision.VisionEntity>(), "fog of war: the centre reveals around it");
            Assert.AreEqual(2, MatchFlowController.ProductionStructures(Player), "the HQ and the centre count as production structures");
        }

        [UnityTest, Timeout(150000)]
        public IEnumerator Deploy_IsRefusedOnInvalidSites_AndInKnownEnemyTerritoryOnly()
        {
            // Next to the HQ: the footprint overlaps it.
            var blocked = SpawnMcv(_hq + new Vector3(6f, 0f, 6f));
            yield return null;
            yield return null;
            var r = blocked.Deploy();
            Assert.AreNotEqual(DeployCheck.Valid, r);
            Assert.AreEqual(DeployState.Mobile, blocked.State, "stays mobile");
            Object.Destroy(blocked.gameObject);

            // Beside the enemy HQ: allowed while that HQ is unseen (no information leak), refused once it is known.
            var enemyHq = Building.All.First(b => b.FactionId == Enemy && b.Definition.Id == "headquarters");
            var near = DeploySite(30f, Player, enemyHq.transform.position);
            Assert.AreNotEqual(DeployCheck.EnemyTerritory, _placement.ValidateDeployment(CentreDef, near, Player, null), "unseen: not enforced");
            var scout = FactionSpawner.Spawn(Def("vehicle_factory").ProducibleUnits.First(u => u.Id == "recon").Prefab,
                NavMesh.SamplePosition(near, out var hit, 4f, NavMesh.AllAreas) ? hit.position : near, Quaternion.identity, Player);
            yield return Until(() => TerritorySystem.IsKnownTo(Player, enemyHq), 5f);
            Assert.IsTrue(TerritorySystem.IsKnownTo(Player, enemyHq), "seen by the scout");
            Assert.AreEqual(DeployCheck.EnemyTerritory, _placement.ValidateDeployment(CentreDef, near, Player, null));
            Assert.AreEqual(PlacementResult.EnemyTerritory, _placement.Validate(Def("power_plant"), near, false, Player));
            Object.Destroy(scout);
        }

        [UnityTest, Timeout(150000)]
        public IEnumerator Deploy_LocksMovement_CanBeCancelled_AndDestructionLeavesNothing()
        {
            var site = DeploySite(95f);
            var mcv = SpawnMcv(site);
            yield return null;
            yield return null;
            var mover = mcv.GetComponent<UnitMover>();
            Assert.AreEqual(DeployCheck.Valid, mcv.Deploy());
            Assert.AreEqual(DeployCheck.Busy, mcv.Deploy(), "no second deployment");
            Assert.IsFalse(mover.MoveTo(site + Vector3.forward * 30f), "immobile while deploying");
            Assert.IsFalse(RepairClient.IsRepairableVehicle(mcv, Player), "cannot be sent for repair while deploying");
            var p = mcv.transform.position;
            yield return new WaitForSeconds(2f);
            Assert.Less((mcv.transform.position - p).magnitude, 0.2f);
            Assert.IsTrue(mcv.CancelDeploy());
            Assert.IsTrue(mover.MoveTo(site + Vector3.forward * 10f), "mobile again after cancelling");
            yield return new WaitForSeconds(1f);
            mover.Stop();
            yield return null;

            Assert.AreEqual(DeployCheck.Valid, mcv.Deploy());
            Time.timeScale = 4f;
            yield return Until(() => mcv.Progress01 > 0.5f, 10f);
            int structures = Building.All.Count;
            var h = mcv.Health;
            h.ApplyDamage(h.Max * 10f, Enemy); // killed halfway
            yield return new WaitForSeconds(TerritorySettings.Current.DeploySeconds + 1f);
            Time.timeScale = 1f;
            Assert.IsTrue(mcv == null);
            Assert.AreEqual(structures, Building.All.Count, "no structure appears");
            Assert.AreEqual(0, Building.All.Count(b => b.Definition.Id == "forward_command"));
        }

        [UnityTest, Timeout(200000)]
        public IEnumerator ForwardTerritory_AllowsConstruction_AndDisappearsWhenLost_WhileBuildingsSurvive()
        {
            var site = DeploySite(100f);
            var made = new List<Building>();
            yield return Establish(site, made);
            var centre = made.Single();
            // Build next to the centre, outside the HQ's territory.
            var plant = Place("power_plant", Player, 14f, centre.transform.position);
            Assert.IsTrue(TerritorySystem.Covers(Player, plant.Footprint));
            var hqOnly = new Core.Territory.TerritoryMap();
            hqOnly.Add(new Core.Territory.TerritoryAnchor(1, Player, new Vector2(_hq.x, _hq.z), 55f, 0f));
            Assert.IsFalse(hqOnly.Covers(Player, plant.Footprint.Body), "only possible thanks to the forward centre");
            var spot = plant.transform.position + (plant.transform.position - centre.transform.position).normalized * 12f;

            // Sell the centre: the territory goes, the plant stays and keeps working.
            int start = Credits;
            int refund = centre.Sale.EstimatedRefund;
            Assert.AreEqual(2500, refund, "half the vehicle price at full health");
            Assert.IsTrue(centre.Sale.Begin(out _));
            Time.timeScale = 4f;
            yield return Until(() => centre == null, TerritorySettings.Current.SellSeconds + 4f);
            Time.timeScale = 1f;
            Assert.IsTrue(centre == null);
            Assert.AreEqual(start + refund, Credits);
            Assert.IsFalse(TerritorySystem.Covers(Player, plant.Footprint), "territory removed");
            Assert.IsTrue(plant != null && plant.GetComponent<Health>().IsAlive, "the surrounding structure survives");
            Assert.IsTrue(plant.IsRegistered, "and stays operational");
            Assert.AreEqual(PlacementResult.OutsideTerritory, _placement.Validate(Def("power_plant"), spot, false, Player), "no construction there any more");
            Assert.IsFalse(TerritorySystem.Map.Anchors.Any(a => a.Faction == Player && (a.Centre - new Vector2(plant.transform.position.x, plant.transform.position.z)).magnitude < 1f),
                "ordinary structures never become anchors");

            // Destroyed rather than sold: the same.
            var made2 = new List<Building>();
            yield return Establish(DeploySite(100f), made2);
            var second = made2.Single();
            int anchors = TerritorySystem.Map.Count;
            var hp = second.GetComponent<Health>();
            hp.ApplyDamage(hp.Max * 10f, Enemy);
            yield return null;
            Assert.AreEqual(anchors - 1, TerritorySystem.Map.Count, "a destroyed centre leaves no territory");
        }

        [UnityTest, Timeout(200000)]
        public IEnumerator Mcv_IsRepairedAtARepairPad()
        {
            Place("vehicle_factory", Player, 18f);
            yield return null;
            var pad = Place("repair_pad", Player, 30f).GetComponent<RepairPad>();
            Time.timeScale = 8f;
            yield return Until(() => pad.IsAssembled, RepairSettings.Current.PadAssemblyTime + 5f);
            var mcv = SpawnMcv(pad.BayPoint + (pad.BayPoint - _hq).normalized * 15f);
            yield return null;
            yield return null;
            var h = mcv.Health;
            h.ApplyDamage(h.Max * 0.5f / (1f - h.Armour), Enemy);
            Assert.IsTrue(RepairClient.Request(mcv, pad, out var client), "an MCV is a repairable vehicle");
            yield return Until(() => h.Current >= h.Max - 0.5f, 120f);
            Time.timeScale = 1f;
            Assert.AreEqual(h.Max, h.Current, 0.5f, "repaired to full at the pad");
        }

        // ---------- Victory ----------

        [UnityTest, Timeout(150000)]
        public IEnumerator Victory_HeadquartersModeIgnoresForwardCentres_AnnihilationCountsThem()
        {
            var made = new List<Building>();
            yield return Establish(DeploySite(95f), made);
            Assert.AreEqual(2, MatchFlowController.ProductionStructures(Player), "HQ + centre (Annihilation counts it)");
            Assert.AreEqual(VictoryMode.Headquarters, MatchFlowController.Current.Victory);
            var hq = Building.All.First(b => b.FactionId == Player && b.Definition.Id == "headquarters");
            var h = hq.GetComponent<Health>();
            h.ApplyDamage(h.Max * 10f, Enemy);
            yield return null;
            yield return null;
            Assert.IsTrue(MatchFlowController.Current.IsOver, "losing the HQ still loses in Headquarters mode");
            Assert.AreEqual(Enemy, MatchFlowController.Current.Outcome.WinnerFactionId);
            Assert.IsTrue(made[0] != null, "even though a forward centre survives");
        }

        // ---------- AI ----------

        [UnityTest, Timeout(400000)]
        public IEnumerator Computer_BuildsOnlyInItsTerritory_AndDeploysAnMcvAtAnExploredField()
        {
            var ai = Object.FindObjectsByType<EnemyCommander>(FindObjectsInactive.Include).Single(c => c.FactionId == Enemy);
            var enemyHq = Building.All.First(b => b.FactionId == Enemy && b.Definition.Id == "headquarters").transform.position;
            var playerHq = _hq;
            var faction = _match.GetFaction(Enemy);
            faction.Wallet.Add(30000);
            faction.RegisterStructure("test_generator", 400, 0);

            // Let it explore one remote field legitimately: a unit of its own drives past it.
            var field = OreDeposit.All.Where(d => !d.IsDepleted)
                .Select(d => d.transform.position)
                .Where(p => (p - enemyHq).magnitude > 80f && (p - enemyHq).magnitude < 170f && (p - playerHq).magnitude > 200f)
                .OrderBy(p => (p - enemyHq).magnitude).First();
            var scout = FactionSpawner.Spawn(Def("vehicle_factory").ProducibleUnits.First(u => u.Id == "recon").Prefab,
                NavMesh.SamplePosition(field, out var fh, 10f, NavMesh.AllAreas) ? fh.position : field, Quaternion.identity, Enemy);
            yield return new WaitForSeconds(1.5f);
            Assert.IsTrue(Vision.VisionSystem.IsExplored(Enemy, field), "explored by its own unit");
            Object.Destroy(scout);

            // An MCV of its own near home; it must choose the explored field and deploy there through the normal rules.
            var site = DeploySite(25f, Enemy, enemyHq);
            var mcv = SpawnMcv(site, Enemy);
            ai.enabled = true;
            Time.timeScale = 10f;
            yield return Until(() => Building.All.Any(b => b.FactionId == Enemy && b.Definition.Id == "forward_command"), 400f);
            Time.timeScale = 1f;
            var centre = Building.All.FirstOrDefault(b => b.FactionId == Enemy && b.Definition.Id == "forward_command");
            Assert.IsNotNull(centre, "the commander deployed its vehicle: " + string.Join(" | ", ai.Log.NewestFirst().Take(10)));
            Assert.Less((centre.transform.position - field).magnitude, 45f, "at the explored field");
            Assert.AreEqual(1, ai.ForwardBasesEstablished);
            Assert.GreaterOrEqual(ai.DeployAttempts, 1);

            // Every ordinary structure it built lies fully inside its territory.
            foreach (var b in Building.All.Where(b => b.FactionId == Enemy && b.Definition.TerritoryRadius <= 0f && b.Definition.Id != "enemy_outpost"))
                Assert.IsTrue(TerritorySystem.Covers(Enemy, b.Footprint), $"{b.Definition.DisplayName} at {b.transform.position} is inside territory");
        }

        // ---------- Restart ----------

        [UnityTest, Timeout(150000)]
        public IEnumerator Restart_LeavesNoTerritorySaleOrDeploymentState()
        {
            var made = new List<Building>();
            yield return Establish(DeploySite(95f), made);
            var plant = Place("power_plant", Player, 16f);
            plant.Sale.Begin(out _);
            var mcv = SpawnMcv(DeploySite(95f));
            yield return null;
            MatchFlowController.Current.Restart();
            yield return null;
            yield return null;
            Assert.AreEqual(0, MobileCommandVehicle.All.Count, "no vehicle survives the reload");
            Assert.AreEqual(0, BuildingSale.SoldBy(Player));
            Assert.AreEqual(0, Building.All.Count(b => b.Definition.Id == "forward_command"));
            int hqs = Building.All.Count(b => b.Definition.Id == "headquarters");
            Assert.AreEqual(hqs, TerritorySystem.Map.Count, "only the new match's headquarters are anchors");
            Assert.AreEqual(1, Object.FindObjectsByType<TerritoryOverlay>().Length, "one overlay");
        }
    }
}
