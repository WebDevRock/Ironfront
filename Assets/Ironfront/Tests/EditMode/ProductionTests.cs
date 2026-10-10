using System.Collections.Generic;
using System.Linq;
using Ironfront.Core.Economy;
using Ironfront.Core.Production;
using NUnit.Framework;
using UnityEngine;

namespace Ironfront.Tests
{
    public class ProductionTests
    {
        private class Unit : IProducible
        {
            public string Id { get; set; }
            public string DisplayName => Id;
            public int Cost { get; set; }
            public float BuildTime { get; set; }
        }

        private static readonly Unit Tank = new Unit { Id = "tank", Cost = 400, BuildTime = 6f };
        private static readonly Unit Harvester = new Unit { Id = "harvester", Cost = 800, BuildTime = 10f };

        [Test]
        public void Enqueue_ChargesCostUpFront()
        {
            var w = new ResourceWallet(1000);
            var q = new ProductionQueue(5);
            Assert.AreEqual(EnqueueResult.Ok, q.TryEnqueue(Tank, w));
            Assert.AreEqual(600, w.Credits);
            Assert.AreEqual(1, q.Count);
        }

        [Test]
        public void Enqueue_InsufficientFunds_RejectedWithoutCharge()
        {
            var w = new ResourceWallet(300);
            var q = new ProductionQueue(5);
            Assert.AreEqual(EnqueueResult.InsufficientFunds, q.TryEnqueue(Tank, w));
            Assert.AreEqual(300, w.Credits);
            Assert.AreEqual(0, q.Count);
        }

        [Test]
        public void Enqueue_RespectsCapacity_WithoutCharging()
        {
            var w = new ResourceWallet(10000);
            var q = new ProductionQueue(2);
            q.TryEnqueue(Tank, w);
            q.TryEnqueue(Tank, w);
            Assert.AreEqual(EnqueueResult.QueueFull, q.TryEnqueue(Tank, w));
            Assert.AreEqual(10000 - 800, w.Credits);
        }

        [Test]
        public void Tick_CompletesHeadAfterBuildTime_InOrder()
        {
            var w = new ResourceWallet(5000);
            var q = new ProductionQueue(5);
            var done = new List<IProducible>();
            q.Completed += done.Add;
            q.TryEnqueue(Tank, w);
            q.TryEnqueue(Harvester, w);

            q.Tick(5.9f);
            Assert.AreEqual(0, done.Count);
            Assert.AreEqual(5.9f / 6f, q.HeadProgress01, 1e-4f);

            q.Tick(0.2f);
            CollectionAssert.AreEqual(new IProducible[] { Tank }, done);
            Assert.AreEqual(1, q.Count);
            Assert.AreEqual(0f, q.HeadElapsed, "Next item starts from zero");

            for (int i = 0; i < 110; i++) q.Tick(0.1f); // 11 s > 10 s build time (float-sum margin)
            CollectionAssert.AreEqual(new IProducible[] { Tank, Harvester }, done);
            Assert.AreEqual(0, q.Count);
        }

        [Test]
        public void Tick_LowPowerMultiplier_SlowsProduction()
        {
            var w = new ResourceWallet(5000);
            var q = new ProductionQueue(5);
            int completed = 0;
            q.Completed += _ => completed++;
            q.TryEnqueue(Tank, w);

            for (int i = 0; i < 60; i++) q.Tick(0.1f, 0.5f); // 6 s at half speed
            Assert.AreEqual(0, completed);
            Assert.AreEqual(0.5f, q.HeadProgress01, 0.01f);
            for (int i = 0; i < 61; i++) q.Tick(0.1f, 0.5f);
            Assert.AreEqual(1, completed);
        }

        [Test]
        public void Cancel_RefundsFullCost_AndResetsProgressOfHead()
        {
            var w = new ResourceWallet(1000);
            var q = new ProductionQueue(5);
            q.TryEnqueue(Tank, w);
            q.TryEnqueue(Tank, w);
            q.Tick(3f);
            Assert.AreEqual(200, w.Credits);

            Assert.IsTrue(q.Cancel(0, w));
            Assert.AreEqual(600, w.Credits);
            Assert.AreEqual(1, q.Count);
            Assert.AreEqual(0f, q.HeadElapsed);

            Assert.IsFalse(q.Cancel(5, w));
            Assert.AreEqual(600, w.Credits);
        }

        [Test]
        public void Cancel_QueuedItem_KeepsHeadProgress()
        {
            var w = new ResourceWallet(1000);
            var q = new ProductionQueue(5);
            q.TryEnqueue(Tank, w);
            q.TryEnqueue(Tank, w);
            q.Tick(3f);
            q.Cancel(1, w);
            Assert.AreEqual(3f, q.HeadElapsed, 1e-4f);
        }

        [Test]
        public void Changed_FiresOnEnqueueCompleteAndCancel_NotOnProgress()
        {
            var w = new ResourceWallet(5000);
            var q = new ProductionQueue(5);
            int changes = 0;
            q.Changed += () => changes++;
            q.TryEnqueue(Tank, w);
            q.TryEnqueue(Tank, w);
            q.Tick(1f);
            Assert.AreEqual(2, changes);
            q.Tick(10f);
            Assert.AreEqual(3, changes);
            q.Cancel(0, w);
            Assert.AreEqual(4, changes);
        }

        [Test]
        public void Credits_NeverNegative_ThroughQueueOperations()
        {
            var w = new ResourceWallet(900);
            var q = new ProductionQueue(5);
            for (int i = 0; i < 5; i++) q.TryEnqueue(Tank, w);
            Assert.AreEqual(100, w.Credits);
            Assert.AreEqual(2, q.Count);
            Assert.GreaterOrEqual(w.Credits, 0);
        }

        // ---- Spawn locator ----

        [Test]
        public void Spawn_PrefersOriginWhenFree()
        {
            Assert.IsTrue(SpawnLocator.TryFind(Vector3.zero, Vector3.forward, 2f, 3, _ => true, out var spot));
            Assert.AreEqual(Vector3.zero, spot);
        }

        [Test]
        public void Spawn_SkipsOccupiedSpots_AndStaysNearby()
        {
            var occupied = new List<Vector3> { Vector3.zero, new Vector3(0, 0, 2) };
            bool IsFree(Vector3 p) => occupied.All(o => (o - p).sqrMagnitude > 0.01f);

            Assert.IsTrue(SpawnLocator.TryFind(Vector3.zero, Vector3.forward, 2f, 3, IsFree, out var spot));
            Assert.IsTrue(IsFree(spot));
            Assert.LessOrEqual(spot.magnitude, 2f + 1e-3f, "Should use the first ring");
        }

        [Test]
        public void Spawn_CandidatesAreDistinct_AndFirstRingStartsForward()
        {
            var list = SpawnLocator.Candidates(Vector3.zero, Vector3.forward, 2f, 2).ToList();
            Assert.AreEqual(1 + 8 + 16, list.Count);
            Assert.AreEqual(new Vector3(0, 0, 2), list[1]);
            for (int i = 0; i < list.Count; i++)
                for (int j = i + 1; j < list.Count; j++)
                    Assert.Greater((list[i] - list[j]).magnitude, 0.1f);
        }

        [Test]
        public void Spawn_NoFreeSpot_ReturnsFalse()
        {
            Assert.IsFalse(SpawnLocator.TryFind(Vector3.zero, Vector3.forward, 2f, 2, _ => false, out _));
        }
    }
}
