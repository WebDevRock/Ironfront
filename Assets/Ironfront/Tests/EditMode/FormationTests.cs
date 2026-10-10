using System.Linq;
using Ironfront.Core;
using NUnit.Framework;
using UnityEngine;

namespace Ironfront.Tests
{
    public class FormationTests
    {
        private const float Spacing = 2f;

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(5)]
        [TestCase(9)]
        [TestCase(12)]
        public void GridSlots_ReturnsOneSlotPerUnit_WithMinimumSpacing(int count)
        {
            var slots = FormationCalculator.GetGridSlots(new Vector3(10, 0, -5), Vector3.forward, count, Spacing);

            Assert.AreEqual(count, slots.Count);
            for (int i = 0; i < slots.Count; i++)
                for (int j = i + 1; j < slots.Count; j++)
                    Assert.GreaterOrEqual(Vector3.Distance(slots[i], slots[j]), Spacing - 1e-4f,
                        $"Slots {i} and {j} overlap");
        }

        [TestCase(1)]
        [TestCase(4)]
        [TestCase(9)]
        public void GridSlots_FullGridIsCentredOnTarget(int count)
        {
            var target = new Vector3(3, 0, 7);
            var slots = FormationCalculator.GetGridSlots(target, new Vector3(1, 0, 1), count, Spacing);
            var centroid = FormationCalculator.Centroid(slots);
            Assert.Less(Vector3.Distance(target, centroid), 1e-4f);
        }

        [Test]
        public void GridSlots_FiveUnits_StayCloseToTarget()
        {
            var target = new Vector3(0, 0, 0);
            var slots = FormationCalculator.GetGridSlots(target, Vector3.forward, 5, Spacing);
            Assert.IsTrue(slots.All(s => Vector3.Distance(s, target) <= Spacing * 1.5f));
            Assert.AreEqual(5, slots.Distinct().Count());
        }

        [Test]
        public void GridSlots_StayOnTargetHeight_AndIgnoreVerticalFacing()
        {
            var slots = FormationCalculator.GetGridSlots(new Vector3(0, 2, 0), new Vector3(0, 5, 1), 4, Spacing);
            Assert.IsTrue(slots.All(s => Mathf.Approximately(s.y, 2f)));
        }

        [Test]
        public void GridSlots_ZeroFacing_FallsBackToWorldForward()
        {
            var slots = FormationCalculator.GetGridSlots(Vector3.zero, Vector3.zero, 4, Spacing);
            Assert.AreEqual(4, slots.Count);
            // Front row should be ahead along +Z.
            Assert.Greater(slots[0].z, slots[3].z);
        }

        [Test]
        public void GridSlots_FrontRowFacesMovementDirection()
        {
            var slots = FormationCalculator.GetGridSlots(Vector3.zero, Vector3.right, 4, Spacing);
            // Facing +X: first row (indices 0,1) is further along +X than the second row.
            Assert.Greater(slots[0].x, slots[2].x);
            Assert.Greater(slots[1].x, slots[3].x);
        }

        [Test]
        public void GridSlots_ZeroOrNegativeCount_ReturnsEmpty()
        {
            Assert.AreEqual(0, FormationCalculator.GetGridSlots(Vector3.zero, Vector3.forward, 0, Spacing).Count);
            Assert.AreEqual(0, FormationCalculator.GetGridSlots(Vector3.zero, Vector3.forward, -3, Spacing).Count);
        }

        [Test]
        public void AssignSlots_GivesEachUnitADistinctSlot()
        {
            var units = new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 0, 1), new Vector3(5, 0, 5), new Vector3(-3, 0, 2) };
            var slots = FormationCalculator.GetGridSlots(new Vector3(20, 0, 20), Vector3.forward, units.Length, Spacing);

            var assignment = FormationCalculator.AssignSlots(units, slots);

            Assert.AreEqual(units.Length, assignment.Length);
            Assert.AreEqual(units.Length, assignment.Distinct().Count());
            Assert.IsTrue(assignment.All(i => i >= 0 && i < slots.Count));
        }

        [Test]
        public void AssignSlots_UnitsAlreadyOnSlots_KeepTheirOwnSlot()
        {
            var slots = FormationCalculator.GetGridSlots(Vector3.zero, Vector3.forward, 4, Spacing);
            var units = new[] { slots[2], slots[0], slots[3], slots[1] };

            var assignment = FormationCalculator.AssignSlots(units, slots);

            CollectionAssert.AreEqual(new[] { 2, 0, 3, 1 }, assignment);
        }

        [Test]
        public void AssignSlots_TooFewSlots_Throws()
        {
            Assert.Throws<System.ArgumentException>(() =>
                FormationCalculator.AssignSlots(new[] { Vector3.zero, Vector3.one }, new[] { Vector3.zero }));
        }
    }
}
