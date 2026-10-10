using System.Collections.Generic;
using Ironfront.Core.Harvesting;
using NUnit.Framework;

namespace Ironfront.Tests
{
    public class HarvesterTests
    {
        private class Deposit : IOreSource
        {
            public int Remaining { get; set; }
            public bool IsDepleted => Remaining <= 0;
            public int Extract(int max)
            {
                int n = System.Math.Min(max, Remaining);
                Remaining -= n;
                return n;
            }
        }

        private class Refinery : IOreSink
        {
            public int Received;
            public void Deliver(int ore) => Received += ore;
        }

        /// <summary>Fake body: arrival happens <see cref="TravelTicks"/> ticks after a move order.</summary>
        private class Body : IHarvesterBody
        {
            public readonly List<Deposit> Deposits = new List<Deposit>();
            public Refinery Refinery = new Refinery();
            public int TravelTicks = 3;
            public object Target;
            public int TicksSinceMove;
            public int MoveOrders;

            public IOreSource FindNearestSource() => Deposits.Find(d => !d.IsDepleted);
            public IOreSink FindNearestSink() => Refinery;
            public void MoveTo(IOreSource s) { Target = s; TicksSinceMove = 0; MoveOrders++; }
            public void MoveTo(IOreSink s) { Target = s; TicksSinceMove = 0; MoveOrders++; }
            public bool IsAt(IOreSource s) => Target == s && TicksSinceMove >= TravelTicks;
            public bool IsAt(IOreSink s) => Target == s && TicksSinceMove >= TravelTicks;
        }

        private static void Run(HarvesterStateMachine m, Body body, int ticks, float dt = 0.5f)
        {
            for (int i = 0; i < ticks; i++)
            {
                body.TicksSinceMove++;
                m.Tick(dt, body);
            }
        }

        private static HarvesterStateMachine NewMachine() => new HarvesterStateMachine(100, 50f, 100f);

        [Test]
        public void FullCycle_HarvestsReturnsUnloadsAndRepeats()
        {
            var body = new Body();
            body.Deposits.Add(new Deposit { Remaining = 1000 });
            var m = NewMachine();
            var states = new List<HarvesterState>();
            m.StateChanged += states.Add;

            m.StartHarvesting(body);
            Assert.AreEqual(HarvesterState.MovingToSource, m.State);

            Run(m, body, 40);

            Assert.GreaterOrEqual(body.Refinery.Received, 200, "At least two loads delivered");
            Assert.AreEqual(0, body.Refinery.Received % 100, "Full loads of 100");
            CollectionAssert.IsSubsetOf(new[]
            {
                HarvesterState.MovingToSource, HarvesterState.Harvesting, HarvesterState.MovingToSink, HarvesterState.Unloading,
            }, states);
            Assert.AreEqual(1000 - body.Refinery.Received - m.Cargo, body.Deposits[0].Remaining, "Ore is conserved");
        }

        [Test]
        public void Cargo_NeverExceedsCapacity()
        {
            var body = new Body { TravelTicks = 1000 }; // never reaches the refinery
            body.Deposits.Add(new Deposit { Remaining = 10000 });
            var m = NewMachine();
            m.StartHarvesting(body);
            body.TicksSinceMove = 1000;
            for (int i = 0; i < 50; i++) m.Tick(0.5f, body);
            Assert.AreEqual(100, m.Cargo);
            Assert.AreEqual(HarvesterState.MovingToSink, m.State);
        }

        [Test]
        public void DepletedMidLoad_DeliversPartialLoad_ThenMovesToNextDeposit()
        {
            var body = new Body();
            var small = new Deposit { Remaining = 30 };
            var big = new Deposit { Remaining = 500 };
            body.Deposits.Add(small);
            body.Deposits.Add(big);
            var m = NewMachine();

            m.StartHarvesting(body, small);
            Run(m, body, 6); // arrive and mine 30
            Assert.IsTrue(small.IsDepleted);

            Run(m, body, 10);
            Assert.AreEqual(30, body.Refinery.Received, "Partial load delivered");
            Assert.AreSame(big, m.Source, "Switched to the next deposit");
        }

        [Test]
        public void AllDepositsDepleted_WaitsInNoSourceState_ThenResumesWhenOreAppears()
        {
            var body = new Body();
            var only = new Deposit { Remaining = 50 };
            body.Deposits.Add(only);
            var m = NewMachine();
            m.StartHarvesting(body);
            Run(m, body, 30);

            Assert.AreEqual(50, body.Refinery.Received);
            Assert.AreEqual(HarvesterState.NoSourceAvailable, m.State);
            Assert.AreEqual(0, m.Cargo);

            body.Deposits.Add(new Deposit { Remaining = 100 });
            Run(m, body, 4); // retry interval is 1 s = 2 ticks
            Assert.AreNotEqual(HarvesterState.NoSourceAvailable, m.State);
        }

        [Test]
        public void NoRefinery_WaitsWithCargo()
        {
            var body = new Body { Refinery = null };
            body.Deposits.Add(new Deposit { Remaining = 1000 });
            var m = NewMachine();
            m.StartHarvesting(body);
            Run(m, body, 20);
            Assert.AreEqual(HarvesterState.NoSinkAvailable, m.State);
            Assert.AreEqual(100, m.Cargo);

            body.Refinery = new Refinery();
            Run(m, body, 12);
            Assert.AreEqual(100, body.Refinery.Received);
        }

        [Test]
        public void Stop_KeepsCargo_AndStopsHarvesting()
        {
            var body = new Body();
            var d = new Deposit { Remaining = 1000 };
            body.Deposits.Add(d);
            var m = NewMachine();
            m.StartHarvesting(body);
            Run(m, body, 4);
            int cargo = m.Cargo;
            int remaining = d.Remaining;

            m.Stop();
            Run(m, body, 20);
            Assert.AreEqual(HarvesterState.Idle, m.State);
            Assert.AreEqual(cargo, m.Cargo);
            Assert.AreEqual(remaining, d.Remaining);
        }

        [Test]
        public void StartHarvesting_PreferredDeposit_IsUsed()
        {
            var body = new Body();
            var near = new Deposit { Remaining = 500 };
            var chosen = new Deposit { Remaining = 500 };
            body.Deposits.Add(near);
            body.Deposits.Add(chosen);
            var m = NewMachine();
            m.StartHarvesting(body, chosen);
            Assert.AreSame(chosen, m.Source);
            Assert.AreSame(chosen, body.Target);
        }

        [Test]
        public void InvalidParameters_Throw()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new HarvesterStateMachine(0, 1f, 1f));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new HarvesterStateMachine(10, 0f, 1f));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new HarvesterStateMachine(10, 1f, 0f));
        }
    }
}
