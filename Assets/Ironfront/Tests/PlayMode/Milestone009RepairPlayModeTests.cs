using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Ironfront.AI;
using Ironfront.Buildings;
using Ironfront.Combat;
using Ironfront.Commands;
using Ironfront.Construction;
using Ironfront.Core.AI;
using Ironfront.Core.Repair;
using Ironfront.Core.Skirmish;
using Ironfront.Data;
using Ironfront.Match;
using Ironfront.Selection;
using Ironfront.Skirmish;
using Ironfront.UI;
using Ironfront.Units;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Ironfront.Tests
{
    /// <summary>
    /// Milestone 009 repairs in a real skirmish map (Dustfall Plains, fixed seed, computer commanders disabled unless a
    /// test needs one): the Repair Pad queue with ten vehicles, independent pads, pausing for power and credits, building
    /// repairs, cancellation / transfer / new orders, destruction, the HUD path, restart cleanup and the AI.
    /// </summary>
    public class Milestone009RepairPlayModeTests
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
            // No income during the tests: repair spending is measured on the wallet.
            foreach (var hv in Object.FindObjectsByType<Economy.Harvester>()) { hv.StopHarvesting(); hv.enabled = false; }
            _match = MatchController.Current;
            _placement = Object.FindAnyObjectByType<BuildingPlacementController>();
            _match.PlayerFaction.Wallet.Add(50000);
            _match.PlayerFaction.RegisterStructure("test_generator", 400, 0);
            _hq = Building.All.First(b => b.FactionId == Player && b.Definition.Id == "headquarters").transform.position;
            Place("vehicle_factory", Player, 18f); // prerequisite for the pad
        }

        [TearDown]
        public void Restore()
        {
            Time.timeScale = 1f;
            SkirmishSession.Reset();
        }

        private BuildingDefinition Def(string id) => _match.Setup.ConstructionOptions.First(d => d.Id == id);

        /// <summary>Places a structure for a faction at the nearest valid site towards the map centre.</summary>
        private Building Place(string id, int faction, float distance, Vector3? near = null)
        {
            var def = Def(id);
            var anchor = near ?? _hq;
            var towards = Vector3.Lerp(anchor, Vector3.zero, 0.3f);
            bool south = anchor.z > 0f; // fronts face the map centre, as the commander builds
            foreach (var c in SiteSearch.Ranked(anchor, towards, distance, distance + 60f, 2f))
                if (_placement.Validate(def, c, south) == Core.Construction.PlacementResult.Valid
                    && _placement.TryPlace(def, c, faction, out var b, out _, south))
                    return b;
            Assert.Fail("no valid site for " + id);
            return null;
        }

        private RepairPad PlacePad(float distance = 36f, Vector3? near = null)
        {
            var pad = Place("repair_pad", Player, distance, near).GetComponent<RepairPad>();
            Assert.IsNotNull(pad);
            return pad;
        }

        private IEnumerator Assembled(RepairPad pad)
        {
            Time.timeScale = 8f;
            float end = Time.time + RepairSettings.Current.PadAssemblyTime + 5f;
            while (!pad.IsAssembled && Time.time < end) yield return null;
            Assert.IsTrue(pad.IsAssembled, "the pad assembles before it can repair");
        }

        private GameObject SpawnDamaged(string unitId, Vector3 at, float fraction = 0.3f)
        {
            var factory = Def("vehicle_factory");
            var def = factory.ProducibleUnits.First(u => u.Id == unitId);
            var p = UnityEngine.AI.NavMesh.SamplePosition(at, out var hit, 6f, UnityEngine.AI.NavMesh.AllAreas) ? hit.position : at;
            var go = FactionSpawner.Spawn(def.Prefab, p, Quaternion.identity, Player);
            var h = go.GetComponent<Health>();
            h.ApplyDamage(h.Max * (1f - fraction) / Mathf.Max(0.05f, 1f - h.Armour), Enemy);
            Assert.Less(h.Current / h.Max, fraction + 0.05f);
            return go;
        }

        private List<GameObject> SpawnGroup(int n, Vector3 around, string unitId = "light_tank")
        {
            var list = new List<GameObject>();
            for (int i = 0; i < n; i++)
                list.Add(SpawnDamaged(unitId, around + new Vector3((i % 5) * 3.5f, 0f, (i / 5) * 3.5f)));
            return list;
        }

        private static float PerHp(GameObject unit)
        {
            var def = unit.GetComponent<UnitIdentity>().Definition;
            var h = unit.GetComponent<Health>();
            return RepairMeter.CostPerHealthFor(def.Cost, h.Max, RepairSettings.Current.VehicleCostFraction, def.RepairCostMultiplier);
        }

        // ---------- Queue ----------

        [UnityTest, Timeout(300000)]
        public IEnumerator TenVehicles_OneInTheBayAtATime_FifoAllRepaired_PaidByHealthRestored()
        {
            var pad = PlacePad();
            yield return Assembled(pad);
            var units = SpawnGroup(10, pad.BayPoint + (pad.BayPoint - _hq).normalized * 30f);
            yield return null;
            var wallet = _match.PlayerFaction.Wallet;
            int startCredits = wallet.Credits;
            float expected = units.Sum(u => (u.GetComponent<Health>().Max - u.GetComponent<Health>().Current) * PerHp(u));

            var order = new List<int>();
            foreach (var u in units)
            {
                Assert.IsTrue(RepairClient.Request(u.GetComponent<Health>(), pad, out var c));
                order.Add(c.Id);
                Assert.IsTrue(RepairClient.Request(u.GetComponent<Health>(), pad, out _), "a second order to the same pad keeps the place");
            }
            Assert.AreEqual(10, pad.Queue.Count, "each vehicle queued once");

            var completed = new List<int>();
            System.Action<RepairPad, RepairClient> onDone = (p, c) => { if (p == pad) completed.Add(c.Id); };
            RepairPad.AnyRepairCompleted += onDone;
            int maxRepairing = 0, maxInBay = 0;
            float end = Time.time + 600f;
            while (completed.Count < 10 && Time.time < end)
            {
                var clients = units.Where(u => u != null).Select(u => u.GetComponent<RepairClient>()).ToList();
                maxRepairing = Mathf.Max(maxRepairing, clients.Count(c => c.State == RepairState.Repairing));
                maxInBay = Mathf.Max(maxInBay, units.Count(u => (u.transform.position - pad.BayPoint).sqrMagnitude <= pad.BayRadius * pad.BayRadius));
                yield return null;
            }
            RepairPad.AnyRepairCompleted -= onDone;
            float settle = Time.time + 20f;
            while (units.Any(u => u.GetComponent<RepairClient>().IsBusy) && Time.time < settle) yield return null;
            Time.timeScale = 1f;

            Assert.AreEqual(10, completed.Count, "all ten repaired: " + string.Join(",", units.Select(u => u.GetComponent<RepairClient>().State + " " + u.GetComponent<RepairClient>().Machine.Reason)));
            CollectionAssert.AreEqual(order, completed, "first in, first out");
            Assert.AreEqual(1, maxRepairing, "exactly one vehicle is repaired at a time");
            Assert.LessOrEqual(maxInBay, 1, "never two vehicles in the bay");
            Assert.IsTrue(units.All(u => u.GetComponent<Health>().Current >= u.GetComponent<Health>().Max - 0.01f));
            Assert.IsTrue(units.All(u => u.GetComponent<RepairClient>().State == RepairState.Completed));
            int spent = startCredits - wallet.Credits;
            Assert.That(spent, Is.InRange(Mathf.FloorToInt(expected) - 10, Mathf.CeilToInt(expected)), $"credits follow health restored (expected about {expected:0})");
            Assert.AreEqual(spent, pad.CreditsSpent);
            Assert.AreEqual(0, pad.Queue.Count);
            Assert.IsTrue(pad.Queue.BayFree, "no stale reservation");
            Assert.AreEqual(10, pad.Completed);
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator TwoPads_WorkIndependently_AndTheBestPadSpreadsTheLoad()
        {
            var a = PlacePad(30f);
            var b = PlacePad(30f, a.BayPoint + (a.BayPoint - _hq).normalized * 5f);
            Assert.AreNotEqual(a, b);
            yield return Assembled(a);
            yield return Assembled(b);
            var groupA = SpawnGroup(3, a.BayPoint + new Vector3(-20f, 0f, 12f));
            var groupB = SpawnGroup(3, b.BayPoint + new Vector3(20f, 0f, 12f));
            foreach (var u in groupA) RepairClient.Request(u.GetComponent<Health>(), a, out _);
            foreach (var u in groupB) RepairClient.Request(u.GetComponent<Health>(), b, out _);
            bool both = false;
            float end = Time.time + 300f;
            while (Time.time < end && !(both && groupA.Concat(groupB).All(u => !u.GetComponent<RepairClient>().IsBusy)))
            {
                if (a.IsRepairingNow && b.IsRepairingNow) both = true;
                yield return null;
            }
            Time.timeScale = 1f;
            Assert.IsTrue(both, "each pad repairs its own vehicle at the same time");
            Assert.AreEqual(3, a.Completed);
            Assert.AreEqual(3, b.Completed);

            // Best pad: with one pad busy, a new order goes to the other.
            var extra = SpawnGroup(2, a.BayPoint + new Vector3(-10f, 0f, 20f));
            RepairClient.Request(extra[0].GetComponent<Health>(), a, out _);
            var best = RepairPad.Best(Player, extra[1].transform.position);
            Assert.AreEqual(b, best, "the idle pad has the shorter wait");
        }

        // ---------- Pausing ----------

        [UnityTest, Timeout(200000)]
        public IEnumerator PowerLossAndCredits_PauseTheRepair_AndItResumes()
        {
            var pad = PlacePad();
            yield return Assembled(pad);
            var unit = SpawnGroup(1, pad.BayPoint + new Vector3(0f, 0f, 15f))[0];
            var h = unit.GetComponent<Health>();
            RepairClient.Request(h, pad, out var client);
            float end = Time.time + 60f;
            while (client.State != RepairState.Repairing && Time.time < end) yield return null;
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(RepairPad.Status.Repairing, pad.State);

            var faction = _match.PlayerFaction;
            faction.RegisterStructure("test_drain", 0, 100000);
            yield return null;
            float before = h.Current;
            yield return new WaitForSeconds(1.5f);
            Assert.AreEqual(RepairPad.Status.NoPower, pad.State);
            Assert.AreEqual(before, h.Current, 0.01f, "no repair on low power");
            Assert.AreEqual(RepairState.Repairing, client.State, "the vehicle keeps its place");
            faction.UnregisterStructure("test_drain", 0, 100000);
            yield return new WaitForSeconds(1f);
            Assert.Greater(h.Current, before, "resumes when power returns");

            // Credits run out: paused, then resumes when credits arrive.
            var wallet = faction.Wallet;
            wallet.TrySpend(wallet.Credits);
            yield return new WaitForSeconds(1f);
            float broke = h.Current;
            yield return new WaitForSeconds(1.5f);
            Assert.AreEqual(RepairPad.Status.NoCredits, pad.State);
            Assert.Less(h.Current - broke, 3f, "at most a sub-credit of health without credits");
            wallet.Add(5000);
            yield return new WaitForSeconds(1f);
            Assert.Greater(h.Current, broke + 3f, "resumes automatically");
            Time.timeScale = 1f;
        }

        // ---------- Buildings ----------

        [UnityTest, Timeout(200000)]
        public IEnumerator BuildingRepair_IsGradual_Paid_Pausable_AndStopsAtFull()
        {
            var refinery = Building.All.First(b => b.FactionId == Player && b.Definition.Id == "refinery");
            var h = refinery.GetComponent<Health>();
            h.ApplyDamage(h.Max * 0.6f / (1f - h.Armour), Enemy);
            float damaged = h.Current;
            var repair = refinery.Repair;
            Assert.IsNotNull(repair, "every building can be repaired");
            Assert.IsFalse(repair.IsOn);
            var wallet = _match.PlayerFaction.Wallet;
            int start = wallet.Credits;

            // Through the HUD toggle, like the player.
            var selection = Object.FindAnyObjectByType<SelectionManager>();
            selection.SelectStructure(refinery);
            yield return null;
            var hud = RepairHud.Ensure();
            hud.Refresh();
            Assert.IsTrue(hud.IsShown);
            var toggle = Object.FindObjectsByType<Button>().First(b => b.name == "Repair_Toggle");
            toggle.onClick.Invoke();
            Assert.IsTrue(repair.IsOn);
            yield return new WaitForSeconds(1f);
            Assert.Greater(h.Current, damaged, "health rises");
            Assert.Less(h.Current, damaged + h.Max * 0.1f, "but gradually (no instant repair)");
            hud.Refresh();
            StringAssert.Contains("Repairing", hud.StatusText);

            // Paid in proportion.
            float restored = h.Current - damaged;
            float perHp = RepairMeter.CostPerHealthFor(refinery.Definition.Cost, h.Max, RepairSettings.Current.BuildingCostFraction);
            Assert.That(start - wallet.Credits, Is.InRange(Mathf.FloorToInt(restored * perHp) - 1, Mathf.CeilToInt(restored * perHp) + 1));

            // Under attack it still repairs (damage and repair both apply).
            h.ApplyDamage(10f, Enemy);
            Assert.IsTrue(repair.IsOn);

            // Off stops; insufficient credits pause; credits resume.
            toggle.onClick.Invoke();
            Assert.IsFalse(repair.IsOn);
            float off = h.Current;
            yield return new WaitForSeconds(0.6f);
            Assert.AreEqual(off, h.Current, 0.01f);
            repair.SetRepairing(true);
            wallet.TrySpend(wallet.Credits);
            yield return new WaitForSeconds(0.8f);
            Assert.AreEqual(BuildingRepair.Status.PausedNoCredits, repair.State);
            wallet.Add(20000);
            Time.timeScale = 8f;
            float end = Time.time + 1.3f / RepairSettings.Current.BuildingRatePerSecond; // a full repair, with margin
            while (repair.IsOn && Time.time < end) yield return null;
            Time.timeScale = 1f;
            Assert.AreEqual(h.Max, h.Current, 0.01f, "stops at maximum health");
            Assert.AreEqual(BuildingRepair.Status.Complete, repair.State);
            Assert.IsFalse(repair.SetRepairing(true), "nothing to repair at full health");
        }

        // ---------- Orders ----------

        [UnityTest, Timeout(200000)]
        public IEnumerator NewOrders_Cancel_TransferMovesTheTicket_AndCancelButtonWorks()
        {
            var a = PlacePad(30f);
            var b = PlacePad(30f, a.BayPoint + (a.BayPoint - _hq).normalized * 5f);
            var units = SpawnGroup(3, a.BayPoint + new Vector3(0f, 0f, 25f));
            var c0 = units[0].GetComponent<Health>();
            RepairClient.Request(c0, a, out var client0);
            Assert.IsTrue(a.Queue.Contains(client0.Id));
            units[0].GetComponent<CombatUnit>().OrderMove(_hq + new Vector3(10f, 0f, 0f));
            Assert.AreEqual(RepairState.Cancelled, client0.State, "a move order cancels the repair");
            Assert.IsFalse(a.Queue.Contains(client0.Id));

            RepairClient.Request(units[1].GetComponent<Health>(), a, out var client1);
            RepairClient.Request(units[1].GetComponent<Health>(), b, out _);
            Assert.IsFalse(a.Queue.Contains(client1.Id), "transferred cleanly");
            Assert.IsTrue(b.Queue.Contains(client1.Id));

            // Player commands through the HUD.
            var selection = Object.FindAnyObjectByType<SelectionManager>();
            selection.Selection.Replace(new[] { units[2].GetComponent<SelectableUnit>() });
            var commands = Object.FindAnyObjectByType<UnitCommandController>();
            yield return null;
            var hud = RepairHud.Ensure();
            hud.Refresh();
            Object.FindObjectsByType<Button>().First(x => x.name == "Repair_Send").onClick.Invoke();
            var client2 = RepairClient.Of(units[2].GetComponent<Health>());
            Assert.IsNotNull(client2);
            Assert.IsTrue(client2.IsBusy, commands.LastRepairMessage);
            yield return null;
            hud.Refresh();
            Object.FindObjectsByType<Button>(FindObjectsInactive.Include).First(x => x.name == "Repair_Cancel").onClick.Invoke();
            Assert.IsFalse(client2.IsBusy);
            Assert.IsFalse(a.Queue.Contains(client2.Id) || b.Queue.Contains(client2.Id));

            // Attack-move and stop also cancel.
            RepairClient.Request(units[2].GetComponent<Health>(), a, out _);
            commands.IssueStop();
            Assert.IsFalse(client2.IsBusy, "stop cancels");
        }

        [UnityTest]
        public IEnumerator WithoutAPad_TheHudExplainsWhatToBuild()
        {
            var unit = SpawnGroup(1, _hq + new Vector3(15f, 0f, 15f))[0];
            var selection = Object.FindAnyObjectByType<SelectionManager>();
            selection.Selection.Replace(new[] { unit.GetComponent<SelectableUnit>() });
            yield return null;
            var commands = Object.FindAnyObjectByType<UnitCommandController>();
            Assert.AreEqual(0, commands.IssueRepair());
            StringAssert.Contains("build one", commands.LastRepairMessage);
            var hud = RepairHud.Ensure();
            yield return new WaitForSecondsRealtime(4.5f); // the order message fades, the standing hint remains
            hud.Refresh();
            StringAssert.Contains("No Repair Pad", hud.StatusText);
        }

        // ---------- Destruction ----------

        [UnityTest, Timeout(200000)]
        public IEnumerator Destruction_OfQueuedVehicle_BayVehicle_AndPad_LeavesNothingStale()
        {
            var pad = PlacePad();
            yield return Assembled(pad);
            var units = SpawnGroup(4, pad.BayPoint + new Vector3(-15f, 0f, 15f));
            var clients = units.Select(u => { RepairClient.Request(u.GetComponent<Health>(), pad, out var c); return c; }).ToList();
            float end = Time.time + 60f;
            while (clients[0].State != RepairState.Repairing && Time.time < end) yield return null;
            Assert.AreEqual(RepairState.Repairing, clients[0].State);

            int id2 = clients[2].Id;
            units[2].GetComponent<Health>().ApplyDamage(1e7f, Enemy); // queued vehicle destroyed
            yield return null;
            yield return null;
            Assert.IsFalse(pad.Queue.Contains(id2));
            Assert.AreEqual(2, pad.Queue.WaitingCount, "two left waiting behind the bay");

            int id0 = clients[0].Id;
            units[0].GetComponent<Health>().ApplyDamage(1e7f, Enemy); // destroyed in the bay
            yield return null;
            yield return null;
            Assert.AreNotEqual(id0, pad.Queue.Active);
            end = Time.time + 60f;
            while (clients[1].State != RepairState.Repairing && Time.time < end) yield return null;
            Assert.AreEqual(RepairState.Repairing, clients[1].State, "the next vehicle gets the bay");

            pad.GetComponent<Health>().ApplyDamage(1e7f, Enemy); // pad destroyed
            yield return null;
            yield return null;
            Time.timeScale = 1f;
            Assert.IsTrue(clients[1].State == RepairState.Cancelled && clients[3].State == RepairState.Cancelled);
            Assert.IsFalse(RepairPad.All.Contains(pad));
            LogAssert.NoUnexpectedReceived();
        }

        // ---------- Restart ----------

        [UnityTest, Timeout(200000)]
        public IEnumerator Restart_LeavesNoRepairState()
        {
            var pad = PlacePad();
            var units = SpawnGroup(3, pad.BayPoint + new Vector3(0f, 0f, 20f));
            var ids = units.Select(u => { RepairClient.Request(u.GetComponent<Health>(), pad, out var c); return c.Id; }).ToList();
            MatchFlowController.Current.Restart();
            yield return null;
            yield return null;
            Assert.AreEqual(0, RepairPad.All.Count, "no pad survives the reload");
            Assert.IsTrue(ids.All(i => RepairClient.Find(i) == null), "no repair client survives");
            Assert.AreEqual(1, Object.FindObjectsByType<RepairHud>().Length, "one repair panel");
        }

        // ---------- AI ----------

        [UnityTest, Timeout(300000)]
        public IEnumerator Computer_UsesItsPadAndRepairsItsBuildings_ByTheSameRules()
        {
            var ai = Object.FindObjectsByType<EnemyCommander>(FindObjectsInactive.Include).Single(c => c.FactionId == Enemy);
            var enemyHq = Building.All.First(b => b.FactionId == Enemy && b.Definition.Id == "headquarters").transform.position;
            var faction = _match.GetFaction(Enemy);
            faction.Wallet.Add(20000);
            faction.RegisterStructure("test_generator", 400, 0);
            Place("vehicle_factory", Enemy, 18f, enemyHq);
            yield return null; // the factory registers with its faction on Start (the pad's prerequisite)
            var pad = Place("repair_pad", Enemy, 30f, enemyHq).GetComponent<RepairPad>();
            Time.timeScale = 8f;
            float end = Time.time + 30f;
            while (!pad.IsAssembled && Time.time < end) yield return null;

            var factoryDef = Def("vehicle_factory");
            var light = factoryDef.ProducibleUnits.First(u => u.Id == "light_tank");
            var tanks = new List<Health>();
            for (int i = 0; i < 6; i++)
            {
                var go = FactionSpawner.Spawn(light.Prefab, enemyHq + new Vector3(10f + i * 3f, 0f, enemyHq.z > 0 ? -18f : 18f), Quaternion.identity, Enemy);
                tanks.Add(go.GetComponent<Health>());
            }
            yield return null;
            tanks[0].ApplyDamage(tanks[0].Max * 0.75f / (1f - tanks[0].Armour), Player); // badly damaged
            tanks[1].ApplyDamage(tanks[1].Max * 0.1f / (1f - tanks[1].Armour), Player);  // scratched
            var refinery = Building.All.First(b => b.FactionId == Enemy && b.Definition.Id == "refinery");
            var rh = refinery.GetComponent<Health>();
            rh.ApplyDamage(rh.Max * 0.5f / (1f - rh.Armour), Player);

            ai.enabled = true;
            end = Time.time + 20f;
            while (Time.time < end && (RepairClient.Of(tanks[0]) == null || !refinery.Repair.IsOn)) { ai.Decide(); yield return new WaitForSeconds(0.5f); }
            Time.timeScale = 1f;
            Assert.IsNotNull(RepairClient.Of(tanks[0]), "the badly damaged tank is sent for repair");
            Assert.IsTrue(RepairClient.Of(tanks[0]).IsBusy || RepairClient.Of(tanks[0]).State == RepairState.Completed);
            Assert.AreEqual(pad, RepairClient.Of(tanks[0]).Pad);
            Assert.IsTrue(RepairClient.Of(tanks[1]) == null || !RepairClient.Of(tanks[1]).IsBusy, "a scratched tank stays with the army");
            Assert.IsTrue(refinery.Repair.IsOn || rh.Current >= rh.Max - 0.01f, "important structures are repaired");
            Assert.Greater(ai.RepairsOrdered, 0);
        }
    }
}
