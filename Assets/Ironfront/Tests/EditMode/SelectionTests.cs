using Ironfront.Core;
using NUnit.Framework;
using UnityEngine;

namespace Ironfront.Tests
{
    public class SelectionTests
    {
        private class Item { public readonly string Name; public Item(string n) { Name = n; } public override string ToString() => Name; }

        private Item _a, _b, _c;
        private SelectionSet<Item> _set;
        private int _changes;

        [SetUp]
        public void SetUp()
        {
            _a = new Item("a"); _b = new Item("b"); _c = new Item("c");
            _set = new SelectionSet<Item>();
            _changes = 0;
            _set.Changed += () => _changes++;
        }

        [Test]
        public void Click_OnUnit_SelectsOnlyThatUnit()
        {
            _set.Replace(new[] { _a, _b });
            SelectionRules.ApplyClick(_set, _c, additive: false);
            CollectionAssert.AreEqual(new[] { _c }, _set.Items);
        }

        [Test]
        public void Click_OnEmptyGround_ClearsSelection()
        {
            _set.Replace(new[] { _a, _b });
            SelectionRules.ApplyClick<Item>(_set, null, additive: false);
            Assert.AreEqual(0, _set.Count);
        }

        [Test]
        public void ShiftClick_OnEmptyGround_KeepsSelection()
        {
            _set.Replace(new[] { _a, _b });
            SelectionRules.ApplyClick<Item>(_set, null, additive: true);
            CollectionAssert.AreEquivalent(new[] { _a, _b }, _set.Items);
        }

        [Test]
        public void ShiftClick_TogglesUnitInAndOut()
        {
            _set.Replace(new[] { _a });
            SelectionRules.ApplyClick(_set, _b, additive: true);
            CollectionAssert.AreEquivalent(new[] { _a, _b }, _set.Items);

            SelectionRules.ApplyClick(_set, _a, additive: true);
            CollectionAssert.AreEquivalent(new[] { _b }, _set.Items);
        }

        [Test]
        public void Box_ReplacesSelection()
        {
            _set.Replace(new[] { _a });
            SelectionRules.ApplyBox(_set, new[] { _b, _c }, additive: false);
            CollectionAssert.AreEquivalent(new[] { _b, _c }, _set.Items);
        }

        [Test]
        public void Box_EmptyRectangle_ClearsSelection()
        {
            _set.Replace(new[] { _a });
            SelectionRules.ApplyBox(_set, new Item[0], additive: false);
            Assert.AreEqual(0, _set.Count);
        }

        [Test]
        public void ShiftBox_AddsWithoutDuplicates()
        {
            _set.Replace(new[] { _a, _b });
            SelectionRules.ApplyBox(_set, new[] { _b, _c }, additive: true);
            CollectionAssert.AreEquivalent(new[] { _a, _b, _c }, _set.Items);
            Assert.AreEqual(3, _set.Count);
        }

        [Test]
        public void ChangedEvent_FiresOnlyOnRealChanges()
        {
            _set.Add(_a);
            Assert.AreEqual(1, _changes);
            _set.Add(_a);
            _set.Replace(new[] { _a });
            _set.Remove(_b);
            Assert.AreEqual(1, _changes, "No-op operations must not raise Changed.");
            _set.Clear();
            Assert.AreEqual(2, _changes);
            _set.Clear();
            Assert.AreEqual(2, _changes);
        }

        [Test]
        public void NullItems_AreIgnored()
        {
            Assert.IsFalse(_set.Add(null));
            _set.Replace(new[] { _a, null, _a });
            CollectionAssert.AreEqual(new[] { _a }, _set.Items);
        }

        [Test]
        public void IsDrag_RespectsThreshold()
        {
            Assert.IsFalse(SelectionRules.IsDrag(new Vector2(100, 100), new Vector2(105, 103), 8f));
            Assert.IsTrue(SelectionRules.IsDrag(new Vector2(100, 100), new Vector2(108, 100), 8f));
        }

        [Test]
        public void RectFromCorners_NormalisesAnyDragDirection()
        {
            var r = SelectionRules.RectFromCorners(new Vector2(200, 50), new Vector2(100, 150));
            Assert.AreEqual(new Rect(100, 50, 100, 100), r);
        }

        [Test]
        public void IsInsideBox_RejectsPointsBehindCamera()
        {
            var box = new Rect(0, 0, 100, 100);
            Assert.IsTrue(SelectionRules.IsInsideBox(box, new Vector3(50, 50, 10)));
            Assert.IsFalse(SelectionRules.IsInsideBox(box, new Vector3(50, 50, -10)));
            Assert.IsFalse(SelectionRules.IsInsideBox(box, new Vector3(150, 50, 10)));
        }
    }
}
