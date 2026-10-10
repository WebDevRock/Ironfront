using System.Collections.Generic;
using Ironfront.Core;
using NUnit.Framework;
using UnityEngine;

namespace Ironfront.Tests
{
    /// <summary>Pure helpers added for Milestone 005: control groups, double-tap, notification throttle, statistics, minimap maths.</summary>
    public class FeedbackTests
    {
        // ---- Control groups ----

        [Test]
        public void ControlGroups_AssignReplaces_AddToAppends_WithoutDuplicates()
        {
            var g = new ControlGroups<string>();
            g.Assign(1, new[] { "a", "b", "a", null });
            Assert.AreEqual(2, g.Count(1));
            g.AddTo(1, new[] { "b", "c" });
            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, g.Get(1, null));
            g.Assign(1, new[] { "z" });
            CollectionAssert.AreEqual(new[] { "z" }, g.Get(1, null));
            Assert.AreEqual(0, g.Count(2), "groups are independent");
        }

        [Test]
        public void ControlGroups_Get_PrunesInvalidMembersPermanently()
        {
            var g = new ControlGroups<string>();
            var dead = new HashSet<string> { "b" };
            g.Assign(3, new[] { "a", "b", "c" });
            CollectionAssert.AreEqual(new[] { "a", "c" }, g.Get(3, s => !dead.Contains(s)));
            Assert.AreEqual(2, g.Count(3));
        }

        [Test]
        public void ControlGroups_RejectsOutOfRangeGroup()
        {
            var g = new ControlGroups<string>();
            Assert.Throws<System.ArgumentOutOfRangeException>(() => g.Assign(10, new[] { "a" }));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => g.Count(-1));
        }

        [Test]
        public void DoubleTap_DetectsSameKeyWithinWindow_Only()
        {
            var d = new DoubleTapDetector(0.4f);
            Assert.IsFalse(d.Press(1, 0f));
            Assert.IsTrue(d.Press(1, 0.3f));
            Assert.IsFalse(d.Press(1, 0.5f), "third press starts a new pair");
            Assert.IsFalse(d.Press(2, 0.6f), "different key");
            Assert.IsFalse(d.Press(2, 1.2f), "outside the window");
            Assert.IsTrue(d.Press(2, 1.3f));
        }

        // ---- Notifications ----

        [Test]
        public void Throttle_SuppressesSameKeyWithinCooldown()
        {
            var t = new NotificationThrottle();
            Assert.IsTrue(t.TryFire("base", 0f, 10f));
            Assert.IsFalse(t.TryFire("base", 9.9f, 10f));
            Assert.IsTrue(t.TryFire("units", 9.9f, 10f), "keys are independent");
            Assert.IsTrue(t.TryFire("base", 10f, 10f));
            Assert.IsTrue(t.TryFire("x", 0f, 0f));
            Assert.IsTrue(t.TryFire("x", 0f, 0f), "zero cooldown never throttles");
        }

        [Test]
        public void Throttle_IsReady_MatchesTryFire_WithoutRecording()
        {
            var t = new NotificationThrottle();
            Assert.IsTrue(t.IsReady("a", 0f, 5f));
            Assert.IsTrue(t.IsReady("a", 0f, 5f), "checking does not consume");
            Assert.IsTrue(t.TryFire("a", 0f, 5f));
            Assert.IsFalse(t.IsReady("a", 4f, 5f));
            Assert.IsTrue(t.IsReady("a", 5f, 5f));
        }

        // ---- Statistics ----

        [Test]
        public void Statistics_CreditKillsToKiller_AndLossesToVictim()
        {
            var s = new MatchStatistics();
            s.UnitProduced(1);
            s.UnitProduced(1);
            s.StructureBuilt(2);
            s.OreDelivered(1, 100);
            s.OreDelivered(1, -5); // ignored
            s.Destroyed(2, structure: false, killerFaction: 1);
            s.Destroyed(2, structure: true, killerFaction: 1);
            s.Destroyed(1, structure: false, killerFaction: null);
            s.Destroyed(1, structure: false, killerFaction: 1); // friendly fire: a loss, not a kill

            var p = s.For(1);
            var e = s.For(2);
            Assert.AreEqual(2, p.UnitsProduced);
            Assert.AreEqual(100, p.OreHarvested);
            Assert.AreEqual(1, p.EnemyUnitsDestroyed);
            Assert.AreEqual(1, p.EnemyStructuresDestroyed);
            Assert.AreEqual(2, p.UnitsLost);
            Assert.AreEqual(1, e.StructuresBuilt);
            Assert.AreEqual(1, e.UnitsLost);
            Assert.AreEqual(1, e.StructuresLost);
            Assert.AreEqual(0, e.EnemyUnitsDestroyed);
        }

        // ---- Minimap ----

        private static readonly Rect World = Rect.MinMaxRect(-60f, -40f, 60f, 80f);

        [Test]
        public void Minimap_WorldUvRoundTrip_AndCorners()
        {
            Assert.AreEqual(Vector2.zero, MinimapMath.WorldToUv(new Vector3(-60f, 5f, -40f), World));
            Assert.AreEqual(Vector2.one, MinimapMath.WorldToUv(new Vector3(60f, 0f, 80f), World));
            var w = new Vector3(12.5f, 0f, -7.25f);
            var back = MinimapMath.UvToWorld(MinimapMath.WorldToUv(w, World), World);
            Assert.AreEqual(w.x, back.x, 1e-4f);
            Assert.AreEqual(w.z, back.z, 1e-4f);
            Assert.AreEqual(0f, back.y);
        }

        [Test]
        public void Minimap_UvToWorld_ClampsOutsideClicks()
        {
            var p = MinimapMath.UvToWorld(new Vector2(-0.5f, 1.7f), World);
            Assert.AreEqual(-60f, p.x, 1e-4f);
            Assert.AreEqual(80f, p.z, 1e-4f);
        }

        [Test]
        public void Minimap_WorldToPixel_StaysInsideTexture()
        {
            Assert.AreEqual(new Vector2Int(0, 0), MinimapMath.WorldToPixel(new Vector3(-500f, 0f, -500f), World, 160));
            Assert.AreEqual(new Vector2Int(159, 159), MinimapMath.WorldToPixel(new Vector3(60f, 0f, 80f), World, 160));
            Assert.AreEqual(new Vector2Int(80, 80), MinimapMath.WorldToPixel(new Vector3(0f, 0f, 20f), World, 160));
        }

        [Test]
        public void Minimap_RayToGround_HitsPlane_OrReportsMiss()
        {
            Assert.IsTrue(MinimapMath.RayToGround(new Ray(new Vector3(3f, 10f, 4f), new Vector3(0f, -1f, 1f)), out var hit));
            Assert.AreEqual(3f, hit.x, 1e-4f);
            Assert.AreEqual(0f, hit.y, 1e-4f);
            Assert.AreEqual(14f, hit.z, 1e-4f);
            Assert.IsFalse(MinimapMath.RayToGround(new Ray(new Vector3(0f, 10f, 0f), Vector3.up), out _), "points away");
            Assert.IsFalse(MinimapMath.RayToGround(new Ray(new Vector3(0f, 10f, 0f), Vector3.forward), out _), "parallel");
        }

        // ---- Formation spacing ----

        [Test]
        public void SpacingFor_GrowsWithUnitSize_ButNeverBelowMinimum()
        {
            Assert.AreEqual(2.5f, FormationCalculator.SpacingFor(0.5f, 2.5f), 1e-4f);
            Assert.AreEqual(3.6f, FormationCalculator.SpacingFor(1.5f, 2.5f), 1e-4f);
            Assert.AreEqual(4f, FormationCalculator.SpacingFor(1.5f, 2.5f, gap: 1f), 1e-4f);
        }
    }
}
