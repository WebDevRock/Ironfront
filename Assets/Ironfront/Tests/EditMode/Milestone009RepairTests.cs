using System.Collections.Generic;
using System.Linq;
using Ironfront.Core.AI;
using Ironfront.Core.Combat;
using Ironfront.Core.Repair;
using NUnit.Framework;
using UnityEngine;

namespace Ironfront.Tests
{
    /// <summary>
    /// Milestone 009 pure rules: repair costs and gradual restoration (<see cref="RepairMeter"/>), the pad's FIFO queue
    /// (<see cref="RepairPadQueue"/>), the vehicle order state machine with fakes (every state, timeouts, navigation
    /// failure, unexpected transitions) and the AI repair rules.
    /// </summary>
    public class Milestone009RepairTests
    {
        // ---------- Health ----------

        [Test]
        public void Health_Restore_IsBoundedAndRefusedWhenDead()
        {
            var h = new HealthModel(100f, 0f);
            h.ApplyDamage(40f);
            Assert.AreEqual(25f, h.Restore(25f), 1e-4f);
            Assert.AreEqual(15f, h.Restore(100f), 1e-4f, "never above max");
            Assert.AreEqual(100f, h.Current, 1e-4f);
            Assert.AreEqual(0f, h.Restore(5f));
            h.ApplyDamage(1000f);
            Assert.IsTrue(h.IsDead);
            Assert.AreEqual(0f, h.Restore(50f), "destroyed things cannot be repaired");
        }

        // ---------- Meter ----------

        private sealed class Wallet
        {
            public int Credits;
            public int Spent;
            public bool TrySpend(int n)
            {
                if (n > Credits) return false;
                Credits -= n;
                Spent += n;
                return true;
            }
        }

        [Test]
        public void Meter_CostIsProportionalToHealthRestored()
        {
            // A 900-credit vehicle with 600 HP at 40 %: 0.6 credits per HP; a full repair costs 360.
            float perHp = RepairMeter.CostPerHealthFor(900, 600f, 0.4f);
            Assert.AreEqual(0.6f, perHp, 1e-5f);
            var meter = new RepairMeter(perHp, 36f); // 6 % of 600 per second
            var w = new Wallet { Credits = 10000 };
            float restored = 0f;
            for (int i = 0; i < 100; i++) restored += meter.Step(0.1f, 600f - restored, w.Credits, w.TrySpend);
            Assert.AreEqual(360f, restored, 0.01f, "gradual: 10 s at 36 HP/s");
            Assert.AreEqual(216, w.Spent, "360 HP x 0.6 credits");
            Assert.AreEqual(meter.TotalCharged, w.Spent);
            Assert.Less(meter.Carry, 1f);
        }

        [Test]
        public void Meter_StopsAtMaximumHealth()
        {
            var meter = new RepairMeter(1f, 50f);
            var w = new Wallet { Credits = 1000 };
            float missing = 30f, total = 0f;
            for (int i = 0; i < 20; i++)
            {
                float r = meter.Step(0.25f, missing, w.Credits, w.TrySpend);
                total += r;
                missing -= r;
            }
            Assert.AreEqual(30f, total, 1e-3f, "restores exactly the missing health");
            Assert.AreEqual(30, w.Spent);
            Assert.AreEqual(0f, meter.Step(1f, 0f, w.Credits, w.TrySpend), "nothing to do at full health");
        }

        [Test]
        public void Meter_PausesWithoutCredits_AndResumesWhenTheyArrive()
        {
            var meter = new RepairMeter(2f, 10f); // 2 credits per HP
            var w = new Wallet { Credits = 6 };
            float total = 0f;
            for (int i = 0; i < 10; i++) total += meter.Step(0.5f, 100f, w.Credits, w.TrySpend);
            Assert.AreEqual(0, w.Credits, "spent what it had");
            Assert.LessOrEqual(total, 3.5f, "no more health than paid for (plus under one credit carried)");
            Assert.IsTrue(meter.PausedForCredits);
            float paused = meter.Step(0.5f, 100f, w.Credits, w.TrySpend);
            Assert.AreEqual(0f, paused, "paused");
            w.Credits = 100;
            float resumed = meter.Step(0.5f, 100f, w.Credits, w.TrySpend);
            Assert.Greater(resumed, 0f, "resumes by itself");
            Assert.IsFalse(meter.PausedForCredits);
        }

        [Test]
        public void Meter_CostForMatchesWhatIsCharged()
        {
            float perHp = RepairMeter.CostPerHealthFor(1000, 1000f, 0.5f);
            Assert.AreEqual(250, RepairMeter.CostFor(500f, perHp), "half the health of a 1000-credit building costs 250");
            Assert.AreEqual(0f, RepairMeter.CostPerHealthFor(0, 1000f, 0.5f), "zero price: the caller must supply a value");
        }

        // ---------- Queue ----------

        [Test]
        public void Queue_IsFifo_RefusesDuplicates_AndHasOneActive()
        {
            var q = new RepairPadQueue();
            for (int i = 1; i <= 12; i++) Assert.IsTrue(q.Enqueue(i));
            Assert.IsFalse(q.Enqueue(5), "each vehicle once");
            Assert.AreEqual(12, q.WaitingCount);
            var order = new List<int>();
            for (int round = 0; round < 12; round++)
            {
                Assert.AreEqual(RepairPadQueue.None, q.TryGrant(false), "never while the bay is physically occupied");
                int g = q.TryGrant(true);
                Assert.AreNotEqual(RepairPadQueue.None, g);
                Assert.AreEqual(RepairPadQueue.None, q.TryGrant(true), "only one in the bay");
                order.Add(g);
                q.Release(g);
                Assert.AreEqual(RepairPadQueue.None, q.TryGrant(true), "next waits until the previous has vacated");
                q.MarkVacated(g);
            }
            CollectionAssert.AreEqual(Enumerable.Range(1, 12).ToList(), order, "first in, first out");
            Assert.AreEqual(0, q.Count);
        }

        [Test]
        public void Queue_RemovalShiftsPositions_AndActiveRemovalKeepsTheBayClosed()
        {
            var q = new RepairPadQueue();
            foreach (int i in new[] { 1, 2, 3, 4 }) q.Enqueue(i);
            Assert.AreEqual(1, q.TryGrant(true));
            Assert.AreEqual(0, q.PositionOf(2));
            Assert.IsTrue(q.Remove(3, gone: true));
            Assert.AreEqual(1, q.PositionOf(4), "vehicles behind move up");
            Assert.IsTrue(q.Remove(1, gone: false)); // cancelled while still standing in the bay
            Assert.AreEqual(1, q.Vacating);
            Assert.AreEqual(RepairPadQueue.None, q.TryGrant(true));
            q.MarkVacated(1);
            Assert.AreEqual(2, q.TryGrant(true));
            Assert.IsTrue(q.Remove(2, gone: true), "destroyed in the bay");
            Assert.IsTrue(q.BayFree, "a destroyed vehicle frees the bay at once");
            var all = q.Clear();
            CollectionAssert.AreEquivalent(new[] { 4 }, all);
            Assert.AreEqual(0, q.Count);
        }

        // ---------- Order machine (fakes) ----------

        private sealed class FakeVehicle : IRepairVehicle
        {
            public Vector3 Pos;
            public Vector3? Target;
            public float Speed = 5f;
            public bool Alive = true;
            public float Health = 0.5f;
            public bool Blocked;      // cannot move at all
            public bool BlockedRoute; // agent reports an incomplete route
            public int Moves, Holds;

            public Vector3 Position => Pos;
            public bool IsAlive => Alive;
            public bool IsFullHealth => Health >= 1f;
            public bool MoveTo(Vector3 p) { Target = p; Moves++; return true; }
            public void HoldStill() { Target = null; Holds++; }
            public bool HasArrived => Target == null || (Target.Value - Pos).magnitude < 0.2f;
            public bool RouteBlocked => BlockedRoute;

            public void Step(float dt)
            {
                if (Target == null || Blocked) return;
                var d = Target.Value - Pos;
                float m = d.magnitude;
                Pos = m <= Speed * dt ? Target.Value : Pos + d / m * Speed * dt;
            }
        }

        private sealed class FakePad : IRepairPadSite
        {
            public bool Alive = true;
            public RepairPadQueue Q = new RepairPadQueue();
            public bool IsAlive => Alive;
            public RepairPadQueue Queue => Q;
            public Vector3 WaitPoint(int position) => new Vector3(-10f - position * 4f, 0f, 0f);
            public Vector3 BayPoint => Vector3.zero;
            public float BayRadius => 3f;
            public int ExitPointCount => 2;
            public Vector3 ExitPoint(int attempt) => new Vector3(12f, 0f, attempt * 6f);

            /// <summary>Pad side of a tick: grant when free, mark the leaving vehicle out, repair the one in the bay.</summary>
            public void Tick(IList<(RepairOrderMachine m, FakeVehicle v)> clients, float dt)
            {
                foreach (var (m, v) in clients)
                    if (Q.Vacating == m.ClientId && (v.Pos.magnitude > BayRadius || !v.Alive)) Q.MarkVacated(m.ClientId);
                bool clear = clients.All(c => !c.v.Alive || c.v.Pos.magnitude > BayRadius || c.m.ClientId == Q.Active);
                if (Q.BayFree) Q.TryGrant(clear);
                foreach (var (m, v) in clients)
                    if (m.State == RepairState.Repairing && Q.Active == m.ClientId) v.Health = Mathf.Min(1f, v.Health + 0.2f * dt);
            }
        }

        private static void Run(FakePad pad, List<(RepairOrderMachine m, FakeVehicle v)> clients, float seconds, float dt = 0.1f,
            System.Action<float> each = null)
        {
            for (float t = 0f; t < seconds; t += dt)
            {
                pad.Tick(clients, dt);
                foreach (var (m, v) in clients)
                {
                    m.Tick(dt);
                    v.Step(dt);
                }
                each?.Invoke(t);
            }
        }

        private static List<(RepairOrderMachine m, FakeVehicle v)> Clients(FakePad pad, int n)
        {
            var list = new List<(RepairOrderMachine, FakeVehicle)>();
            for (int i = 0; i < n; i++)
            {
                var v = new FakeVehicle { Pos = new Vector3(-40f, 0f, i * 3f) };
                var m = new RepairOrderMachine(i + 1, v, RepairTimeouts.Default);
                Assert.IsTrue(m.Start(pad));
                list.Add((m, v));
            }
            return list;
        }

        [Test]
        public void Machine_TenVehicles_OneAtATime_FifoAndAllRepaired()
        {
            var pad = new FakePad();
            var clients = Clients(pad, 10);
            int maxRepairing = 0;
            var completionOrder = new List<int>();
            Run(pad, clients, 400f, each: _ =>
            {
                int repairing = clients.Count(c => c.m.State == RepairState.Repairing || c.m.State == RepairState.EnteringPad);
                maxRepairing = Mathf.Max(maxRepairing, repairing);
                int inBay = clients.Count(c => c.v.Pos.magnitude <= pad.BayRadius);
                Assert.LessOrEqual(inBay, 1, "never two vehicles in the bay");
                foreach (var c in clients)
                    if (c.m.State == RepairState.Completed && !completionOrder.Contains(c.m.ClientId)) completionOrder.Add(c.m.ClientId);
            });
            Assert.AreEqual(1, maxRepairing, "exactly one vehicle holds the bay at a time");
            Assert.IsTrue(clients.All(c => c.m.State == RepairState.Completed), string.Join(",", clients.Select(c => c.m.State + ":" + c.m.Reason)));
            Assert.IsTrue(clients.All(c => c.v.Health >= 1f));
            CollectionAssert.AreEqual(Enumerable.Range(1, 10).ToList(), completionOrder, "first in, first out");
            Assert.AreEqual(0, pad.Q.Count);
            Assert.IsTrue(pad.Q.BayFree, "no stale reservation");
        }

        [Test]
        public void Machine_GoesThroughEveryState()
        {
            var pad = new FakePad();
            var clients = Clients(pad, 2); // the second one has to wait its turn
            var seen = new List<RepairState>();
            clients[1].m.StateChanged += s => seen.Add(s);
            Run(pad, clients, 90f);
            CollectionAssert.IsSubsetOf(new[] { RepairState.Waiting, RepairState.EnteringPad, RepairState.Repairing, RepairState.ExitingPad, RepairState.Completed }, seen);
        }

        [Test]
        public void Machine_QueuedVehicleDestroyed_IsRemoved_OthersMoveUp()
        {
            var pad = new FakePad();
            var clients = Clients(pad, 3);
            Run(pad, clients, 5f);
            clients[1].v.Alive = false;
            Run(pad, clients, 0.2f);
            Assert.AreEqual(RepairState.Cancelled, clients[1].m.State);
            Assert.AreEqual(-1, pad.Q.PositionOf(2));
            Assert.AreEqual(0, pad.Q.PositionOf(3), "the vehicle behind moves up");
        }

        [Test]
        public void Machine_DestroyedInTheBay_FreesTheBayForTheNext()
        {
            var pad = new FakePad();
            var clients = Clients(pad, 2);
            Run(pad, clients, 60f, each: _ => { if (clients[0].m.State == RepairState.Repairing) clients[0].v.Alive = false; });
            Assert.AreEqual(RepairState.Cancelled, clients[0].m.State);
            Assert.AreEqual(RepairState.Completed, clients[1].m.State, "the next vehicle still gets its repair");
        }

        [Test]
        public void Machine_PadDestroyed_CancelsEveryone()
        {
            var pad = new FakePad();
            var clients = Clients(pad, 4);
            Run(pad, clients, 10f);
            pad.Alive = false;
            pad.Q.Clear();
            Run(pad, clients, 0.2f);
            Assert.IsTrue(clients.All(c => c.m.State == RepairState.Cancelled && c.m.Reason.Contains("destroyed")));
        }

        [Test]
        public void Machine_CancelAndTransfer_LeaveNoTicket()
        {
            var a = new FakePad();
            var b = new FakePad();
            var v = new FakeVehicle { Pos = new Vector3(-30f, 0f, 0f) };
            var m = new RepairOrderMachine(7, v, RepairTimeouts.Default);
            Assert.IsTrue(m.Start(a));
            Assert.IsFalse(m.Start(b), "one order at a time: transfer means cancel first");
            m.Cancel("transferred");
            Assert.IsFalse(a.Q.Contains(7));
            Assert.IsTrue(m.Start(b));
            Assert.IsTrue(b.Q.Contains(7));
            Assert.IsFalse(a.Q.Contains(7));
        }

        [Test]
        public void Machine_BlockedRoute_ReroutesThenCancels_WithoutTeleporting()
        {
            var pad = new FakePad();
            pad.Q.Enqueue(99);
            pad.Q.TryGrant(true); // the bay is busy, so this vehicle has to travel to a waiting slot
            var v = new FakeVehicle { Pos = new Vector3(-60f, 0f, 0f), Blocked = true };
            var m = new RepairOrderMachine(1, v, RepairTimeouts.Default);
            m.Start(pad);
            var start = v.Pos;
            var clients = new List<(RepairOrderMachine, FakeVehicle)> { (m, v) };
            Run(pad, clients, 60f);
            Assert.AreEqual(RepairState.Cancelled, m.State);
            StringAssert.Contains("blocked", m.Reason);
            Assert.AreEqual(RepairTimeouts.Default.MaxReroutes + 1, m.Reroutes, "re-routed before giving up");
            Assert.AreEqual(start, v.Pos, "never moved by anything but its own driving");
            Assert.IsFalse(pad.Q.Contains(1));
        }

        [Test]
        public void Machine_IncompleteRoute_GivesUpAtTheFirstStall()
        {
            var pad = new FakePad();
            pad.Q.Enqueue(99);
            pad.Q.TryGrant(true);
            var v = new FakeVehicle { Pos = new Vector3(-60f, 0f, 0f), Blocked = true, BlockedRoute = true };
            var m = new RepairOrderMachine(1, v, RepairTimeouts.Default);
            m.Start(pad);
            Run(pad, new List<(RepairOrderMachine, FakeVehicle)> { (m, v) }, RepairTimeouts.Default.StallSeconds + 1f);
            Assert.AreEqual(RepairState.Cancelled, m.State);
        }

        [Test]
        public void Machine_CannotEnterBay_GivesTheBayUp()
        {
            var pad = new FakePad();
            var clients = Clients(pad, 2);
            // The first vehicle drives to its slot, then gets stuck once it is granted the bay.
            Run(pad, clients, 60f, each: _ => { if (clients[0].m.State == RepairState.EnteringPad) clients[0].v.Blocked = true; });
            Assert.AreEqual(RepairState.Cancelled, clients[0].m.State);
            Assert.AreEqual(RepairState.Completed, clients[1].m.State, "the bay is not blocked forever");
        }

        [Test]
        public void Machine_CannotLeaveBay_IsReleased_AndThePadWaitsUntilPhysicallyClear()
        {
            var pad = new FakePad();
            var clients = Clients(pad, 2);
            Run(pad, clients, 200f, each: _ => { if (clients[0].m.State == RepairState.ExitingPad) clients[0].v.Blocked = true; });
            Assert.AreEqual(RepairState.Completed, clients[0].m.State);
            StringAssert.Contains("could not leave", clients[0].m.Reason);
            Assert.AreNotEqual(RepairState.Repairing, clients[1].m.State, "nobody is repaired while the bay is still occupied");
            Assert.IsTrue(pad.Q.BayFree);
            // The stuck vehicle finally drives off: the next one gets in.
            clients[0].v.Blocked = false;
            clients[0].v.MoveTo(new Vector3(20f, 0f, 0f));
            Run(pad, clients, 120f);
            Assert.AreEqual(RepairState.Completed, clients[1].m.State);
        }

        [Test]
        public void Machine_QueueCleared_UnderIt_CancelsSafely()
        {
            var pad = new FakePad();
            var clients = Clients(pad, 2);
            Run(pad, clients, 2f);
            pad.Q.Remove(2, gone: false); // lost its ticket unexpectedly
            Run(pad, clients, 0.2f);
            Assert.AreEqual(RepairState.Cancelled, clients[1].m.State);
            Assert.IsTrue(clients[0].m.IsActive);
        }

        // ---------- AI rules ----------

        [Test]
        public void Ai_SendsOnlyWorthwhileRepairs()
        {
            Assert.IsFalse(RepairPlanner.ShouldSendVehicle(0.8f, 0.45f, false, false, 0, 10, 5000, 300, 200), "slightly damaged stays");
            Assert.IsTrue(RepairPlanner.ShouldSendVehicle(0.4f, 0.45f, false, false, 0, 10, 5000, 300, 200));
            Assert.IsFalse(RepairPlanner.ShouldSendVehicle(0.4f, 0.45f, true, false, 0, 10, 5000, 300, 200), "not out of a fight");
            Assert.IsTrue(RepairPlanner.ShouldSendVehicle(0.2f, 0.45f, true, false, 0, 10, 5000, 300, 200), "badly damaged pulls out");
            Assert.IsFalse(RepairPlanner.ShouldSendVehicle(0.4f, 0.45f, false, true, 0, 10, 5000, 300, 200), "not while defending");
            Assert.IsFalse(RepairPlanner.ShouldSendVehicle(0.4f, 0.45f, false, false, 5, 10, 5000, 300, 200), "at most half the army away");
            Assert.IsFalse(RepairPlanner.ShouldSendVehicle(0.4f, 0.45f, false, false, 0, 10, 400, 300, 200), "credits for production first");
            Assert.IsFalse(RepairPlanner.ShouldSendVehicle(0.1f, 0f, false, false, 0, 10, 5000, 300, 200), "repairs disabled");
        }

        [Test]
        public void Ai_BuildingRepairs_HaveHysteresisAndProtectTheReserve()
        {
            Assert.IsFalse(RepairPlanner.ShouldRepairBuilding(0.8f, 0.7f, false, 5000, 300));
            Assert.IsTrue(RepairPlanner.ShouldRepairBuilding(0.6f, 0.7f, false, 5000, 300));
            Assert.IsFalse(RepairPlanner.ShouldRepairBuilding(0.6f, 0.7f, false, 500, 300), "needs the margin to start");
            Assert.IsTrue(RepairPlanner.ShouldRepairBuilding(0.8f, 0.7f, true, 500, 300), "keeps going above the reserve");
            Assert.IsFalse(RepairPlanner.ShouldRepairBuilding(0.8f, 0.7f, true, 250, 300), "stops before production is starved");
            Assert.IsTrue(RepairPlanner.IsImportantStructure("vehicle_factory"));
            Assert.IsFalse(RepairPlanner.IsImportantStructure("enemy_outpost"));
        }

        [Test]
        public void Ai_PadChoice_PrefersShorterWait()
        {
            float near = RepairPlanner.PadWaitEstimate(true, 5f, 6, 8f, 0f);
            float far = RepairPlanner.PadWaitEstimate(true, 20f, 0, 8f, 0f);
            Assert.Less(far, near, "a longer drive beats a long queue");
            Assert.IsTrue(float.IsPositiveInfinity(RepairPlanner.PadWaitEstimate(false, 1f, 0, 8f, 0f)));
            Assert.AreEqual(12f, RepairPlanner.PadWaitEstimate(true, 5f, 0, 8f, 12f), "still assembling");
        }

        [Test]
        public void Ai_BuildsAPad_OnceAnArmyExists()
        {
            var cost = new CostTable { RepairPad = 1000, Recon = 450, LightTank = 500, HeavyTank = 900, Harvester = 800, Refinery = 1000, PowerPlant = 500, Factory = 1300 };
            var s = new CommanderSnapshot { HasHeadquarters = true, OreAvailable = true, Credits = 3000, Factories = 1, Refineries = 1, Harvesters = 2,
                PowerPlants = 1, PowerProduced = 100, LightTanks = 6, Recon = 1, EnemyBaseKnown = true, GuardTowers = 1 };
            var cfg = CommanderSettings.Normal;
            Assert.AreEqual(AiActionType.BuildRepairPad, ExpansionRules.Next(s, StrategicState.Assemble, cfg, cost).Type);
            s.RepairPads = 1;
            Assert.AreNotEqual(AiActionType.BuildRepairPad, ExpansionRules.Next(s, StrategicState.Assemble, cfg, cost).Type, "Normal stops at one");
            Assert.AreEqual(2, CommanderSettings.Hard.MaxRepairPads);
            s.RepairPads = 0;
            s.LightTanks = 3;
            Assert.AreNotEqual(AiActionType.BuildRepairPad, ExpansionRules.Next(s, StrategicState.Assemble, cfg, cost).Type, "not before an army exists");
        }
    }
}
