using System;
using System.Collections.Generic;
using Ironfront.Core.Economy;
using NUnit.Framework;

namespace Ironfront.Tests
{
    public class EconomyTests
    {
        private class Item : IConstructable
        {
            public string Id { get; set; } = "x";
            public string DisplayName => Id;
            public int Cost { get; set; }
            public IReadOnlyList<string> PrerequisiteIds { get; set; } = new string[0];
        }

        // ---- Wallet ----

        [Test]
        public void Wallet_SpendWithinBalance_Deducts()
        {
            var w = new ResourceWallet(1000);
            Assert.IsTrue(w.TrySpend(400));
            Assert.AreEqual(600, w.Credits);
        }

        [Test]
        public void Wallet_SpendMoreThanBalance_FailsAndKeepsBalance()
        {
            var w = new ResourceWallet(300);
            Assert.IsFalse(w.TrySpend(301));
            Assert.AreEqual(300, w.Credits);
        }

        [Test]
        public void Wallet_SpendExactBalance_ReachesZeroNotNegative()
        {
            var w = new ResourceWallet(250);
            Assert.IsTrue(w.TrySpend(250));
            Assert.AreEqual(0, w.Credits);
            Assert.IsFalse(w.TrySpend(1));
            Assert.AreEqual(0, w.Credits);
        }

        [Test]
        public void Wallet_RejectsNegativeAmounts()
        {
            var w = new ResourceWallet(100);
            Assert.Throws<ArgumentOutOfRangeException>(() => w.TrySpend(-5));
            Assert.Throws<ArgumentOutOfRangeException>(() => w.Add(-5));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ResourceWallet(-1));
            Assert.AreEqual(100, w.Credits);
        }

        [Test]
        public void Wallet_AddSaturatesInsteadOfOverflowing()
        {
            var w = new ResourceWallet(int.MaxValue - 10);
            w.Add(100);
            Assert.AreEqual(int.MaxValue, w.Credits);
        }

        [Test]
        public void Wallet_ChangedEvent_ReportsNewBalance_OnlyOnRealChanges()
        {
            var w = new ResourceWallet(100);
            var seen = new List<int>();
            w.Changed += seen.Add;
            w.Add(50);
            w.TrySpend(30);
            w.TrySpend(0);
            w.Add(0);
            w.TrySpend(1000);
            CollectionAssert.AreEqual(new[] { 150, 120 }, seen);
        }

        // ---- Power ----

        [Test]
        public void Power_LowWhenDrainExceedsOutput()
        {
            var p = new PowerGrid();
            p.Add(20, 15);
            Assert.IsFalse(p.IsLowPower);
            p.Add(0, 30);
            Assert.IsTrue(p.IsLowPower);
            p.Add(50, 0);
            Assert.IsFalse(p.IsLowPower);
            Assert.AreEqual(70, p.Produced);
            Assert.AreEqual(45, p.Consumed);
        }

        [Test]
        public void Power_RemoveRestoresBalance_AndNeverGoesNegative()
        {
            var p = new PowerGrid();
            p.Add(50, 10);
            p.Remove(50, 10);
            p.Remove(5, 5);
            Assert.AreEqual(0, p.Produced);
            Assert.AreEqual(0, p.Consumed);
        }

        // ---- Faction / construction rules ----

        [Test]
        public void Faction_RegisterStructure_UpdatesRosterAndPower()
        {
            var f = new FactionState(1, "P", true, 0);
            f.RegisterStructure("hq", 20, 0);
            f.RegisterStructure("refinery", 0, 15);
            Assert.IsTrue(f.Structures.Has("hq"));
            Assert.AreEqual(1, f.Structures.Count("refinery"));
            Assert.AreEqual(20, f.Power.Produced);
            Assert.AreEqual(15, f.Power.Consumed);

            f.UnregisterStructure("refinery", 0, 15);
            Assert.IsFalse(f.Structures.Has("refinery"));
            Assert.AreEqual(0, f.Power.Consumed);
        }

        [Test]
        public void Construction_RequiresPrerequisite()
        {
            var f = new FactionState(1, "P", true, 5000);
            var item = new Item { Cost = 500, PrerequisiteIds = new[] { "hq" } };
            Assert.AreEqual(BuildCheck.MissingPrerequisite, ConstructionRules.Check(item, f));
            f.RegisterStructure("hq", 0, 0);
            Assert.AreEqual(BuildCheck.Ok, ConstructionRules.Check(item, f));
        }

        [Test]
        public void Construction_InsufficientFunds_DoesNotCharge()
        {
            var f = new FactionState(1, "P", true, 400);
            var item = new Item { Cost = 500 };
            Assert.AreEqual(BuildCheck.InsufficientFunds, ConstructionRules.TryPurchase(item, f));
            Assert.AreEqual(400, f.Wallet.Credits);
        }

        [Test]
        public void Construction_PurchaseCharges_AndMissingPrerequisiteDoesNot()
        {
            var f = new FactionState(1, "P", true, 2000);
            var locked = new Item { Cost = 500, PrerequisiteIds = new[] { "hq" } };
            Assert.AreEqual(BuildCheck.MissingPrerequisite, ConstructionRules.TryPurchase(locked, f));
            Assert.AreEqual(2000, f.Wallet.Credits);

            f.RegisterStructure("hq", 0, 0);
            Assert.AreEqual(BuildCheck.Ok, ConstructionRules.TryPurchase(locked, f));
            Assert.AreEqual(1500, f.Wallet.Credits);
        }

        [Test]
        public void Factions_HaveIndependentWallets()
        {
            var a = new FactionState(1, "A", true, 1000);
            var b = new FactionState(2, "B", false, 1000);
            a.Wallet.TrySpend(600);
            Assert.AreEqual(400, a.Wallet.Credits);
            Assert.AreEqual(1000, b.Wallet.Credits);
        }
    }
}
