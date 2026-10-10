using System.Collections.Generic;
using Ironfront.Core.Combat;
using NUnit.Framework;

namespace Ironfront.Tests
{
    public class CombatRulesTests
    {
        // ---- Damage & armour ----

        [Test]
        public void Armour_ReducesDamageProportionally()
        {
            Assert.AreEqual(100f, DamageRules.Mitigate(100f, 0f), 1e-4f);
            Assert.AreEqual(65f, DamageRules.Mitigate(100f, 0.35f), 1e-4f);
            Assert.AreEqual(22.5f, DamageRules.Mitigate(25f, 0.1f), 1e-4f);
        }

        [Test]
        public void Armour_IsClamped_AndEveryHitDealsMinimumDamage()
        {
            Assert.AreEqual(DamageRules.MaxArmour, DamageRules.ClampArmour(5f));
            Assert.AreEqual(0f, DamageRules.ClampArmour(-1f));
            Assert.AreEqual(10f, DamageRules.Mitigate(100f, 2f), 1e-4f, "Armour above the cap behaves like the cap");
            Assert.AreEqual(DamageRules.MinimumDamage, DamageRules.Mitigate(0.5f, 0.9f));
            Assert.AreEqual(0f, DamageRules.Mitigate(0f, 0f));
            Assert.AreEqual(0f, DamageRules.Mitigate(-10f, 0f));
        }

        [Test]
        public void FriendlyFire_BlockedUnlessExplicitlyAllowed()
        {
            Assert.IsTrue(DamageRules.AreHostile(1, 2));
            Assert.IsFalse(DamageRules.AreHostile(1, 1));
            Assert.IsTrue(DamageRules.CanDamage(1, 2, false));
            Assert.IsFalse(DamageRules.CanDamage(1, 1, false));
            Assert.IsTrue(DamageRules.CanDamage(1, 1, true));
        }

        // ---- Health ----

        [Test]
        public void Health_AppliesArmour_AndReportsDealtDamage()
        {
            var h = new HealthModel(300f, 0.1f);
            float dealt = h.ApplyDamage(25f);
            Assert.AreEqual(22.5f, dealt, 1e-4f);
            Assert.AreEqual(277.5f, h.Current, 1e-4f);
            Assert.IsTrue(h.IsDamaged);
        }

        [Test]
        public void Health_DiesExactlyOnce_AndIgnoresFurtherDamage()
        {
            var h = new HealthModel(50f, 0f);
            int deaths = 0;
            h.Died += () => deaths++;
            Assert.AreEqual(40f, h.ApplyDamage(40f));
            Assert.AreEqual(10f, h.ApplyDamage(40f), "Overkill only removes what is left");
            Assert.IsTrue(h.IsDead);
            Assert.AreEqual(0f, h.Current);
            Assert.AreEqual(0f, h.ApplyDamage(40f));
            Assert.AreEqual(1, deaths);
        }

        [Test]
        public void Health_InvalidMax_Throws()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new HealthModel(0f, 0f));
        }

        [Test]
        public void Health_DamagedEvent_CarriesMitigatedAmount()
        {
            var h = new HealthModel(100f, 0.5f);
            var seen = new List<float>();
            h.Damaged += seen.Add;
            h.ApplyDamage(20f);
            h.ApplyDamage(0f);
            CollectionAssert.AreEqual(new[] { 10f }, seen);
        }

        // ---- Cooldown ----

        [Test]
        public void Cooldown_PreventsFiringFasterThanInterval()
        {
            var c = new WeaponCooldown(1f);
            Assert.IsTrue(c.TryFire());
            Assert.IsFalse(c.TryFire());
            c.Tick(0.6f);
            Assert.IsFalse(c.TryFire());
            c.Tick(0.4f);
            Assert.IsTrue(c.TryFire());
        }

        [Test]
        public void Cooldown_ShotCountOverTime_NeverExceedsRate()
        {
            var c = new WeaponCooldown(2.5f);
            int shots = 0;
            for (int i = 0; i < 1000; i++) // 10 s at 100 Hz, trying every tick
            {
                if (c.TryFire()) shots++;
                c.Tick(0.01f);
            }
            Assert.AreEqual(4, shots); // t = 0, 2.5, 5, 7.5
        }

        // ---- Target selection ----

        [Test]
        public void TargetSelector_PrefersArmedUnits_ThenUnits_ThenStructures_ThenNearest()
        {
            var candidates = new[]
            {
                new TargetCandidate<string>("building", 2f, false, true),
                new TargetCandidate<string>("harvester", 3f, false, false),
                new TargetCandidate<string>("farTank", 9f, true, false),
                new TargetCandidate<string>("nearTank", 6f, true, false),
            };
            Assert.IsTrue(TargetSelector.TryChoose(candidates, 10f, out var best));
            Assert.AreEqual("nearTank", best);

            Assert.IsTrue(TargetSelector.TryChoose(candidates, 5f, out best));
            Assert.AreEqual("harvester", best, "Armed units out of range are ignored");

            Assert.IsTrue(TargetSelector.TryChoose(candidates, 2.5f, out best));
            Assert.AreEqual("building", best);

            Assert.IsFalse(TargetSelector.TryChoose(candidates, 1f, out _));
        }
    }
}
