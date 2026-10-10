using Ironfront.Core;
using NUnit.Framework;
using UnityEngine;

namespace Ironfront.Tests
{
    public class CameraMathTests
    {
        private static readonly Vector2 Screen = new Vector2(1920, 1080);

        [Test]
        public void EdgePan_CentreOfScreen_IsZero()
        {
            Assert.AreEqual(Vector2.zero, CameraMath.EdgePanDirection(new Vector2(960, 540), Screen, 12));
        }

        [Test]
        public void EdgePan_EachEdgeAndCorner()
        {
            Assert.AreEqual(new Vector2(-1, 0), CameraMath.EdgePanDirection(new Vector2(5, 540), Screen, 12));
            Assert.AreEqual(new Vector2(1, 0), CameraMath.EdgePanDirection(new Vector2(1915, 540), Screen, 12));
            Assert.AreEqual(new Vector2(0, -1), CameraMath.EdgePanDirection(new Vector2(960, 3), Screen, 12));
            Assert.AreEqual(new Vector2(0, 1), CameraMath.EdgePanDirection(new Vector2(960, 1079), Screen, 12));
            Assert.AreEqual(new Vector2(1, 1), CameraMath.EdgePanDirection(new Vector2(1919, 1079), Screen, 12));
        }

        [Test]
        public void EdgePan_PointerOutsideWindow_IsZero()
        {
            Assert.AreEqual(Vector2.zero, CameraMath.EdgePanDirection(new Vector2(-20, 540), Screen, 12));
            Assert.AreEqual(Vector2.zero, CameraMath.EdgePanDirection(new Vector2(960, 2000), Screen, 12));
        }

        [Test]
        public void Zoom_ScrollUpZoomsIn_ScrollDownZoomsOut()
        {
            Assert.AreEqual(26f, CameraMath.ApplyZoom(30f, 120f, 4f, 12f, 60f));
            Assert.AreEqual(34f, CameraMath.ApplyZoom(30f, -1f, 4f, 12f, 60f));
            Assert.AreEqual(30f, CameraMath.ApplyZoom(30f, 0f, 4f, 12f, 60f));
        }

        [Test]
        public void Zoom_IsClampedToLimits()
        {
            float d = 30f;
            for (int i = 0; i < 50; i++) d = CameraMath.ApplyZoom(d, 1f, 4f, 12f, 60f);
            Assert.AreEqual(12f, d);
            for (int i = 0; i < 50; i++) d = CameraMath.ApplyZoom(d, -1f, 4f, 12f, 60f);
            Assert.AreEqual(60f, d);
        }

        [Test]
        public void ClampToBounds_KeepsRigInsideMap()
        {
            var p = CameraMath.ClampToBounds(new Vector3(80, 3, -70), new Vector2(-50, -50), new Vector2(50, 50));
            Assert.AreEqual(new Vector3(50, 3, -50), p);
        }

        [Test]
        public void OrbitOffset_IsAboveAndBehindFocus()
        {
            var offset = CameraMath.OrbitOffset(55f, 0f, 30f);
            Assert.Greater(offset.y, 0f);
            Assert.Less(offset.z, 0f);
            Assert.AreEqual(30f, offset.magnitude, 1e-3f);
        }

        [Test]
        public void PanToWorld_DiagonalInputIsNormalised()
        {
            var dir = CameraMath.PanToWorld(new Vector2(1, 1), 0f);
            Assert.AreEqual(1f, dir.magnitude, 1e-4f);
            Assert.AreEqual(0f, dir.y);
        }
    }
}
