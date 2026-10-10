using Ironfront.Core.Construction;
using NUnit.Framework;
using UnityEngine;

namespace Ironfront.Tests
{
    public class PlacementRulesTests
    {
        private static readonly Rect Map = new Rect(-50, -50, 100, 100);
        private static readonly Rect[] None = new Rect[0];
        private static readonly Footprint[] NoStructures = new Footprint[0];
        private const float Clearance = 1.5f;
        private const float ResourceClearance = 3f;

        private static PlacementResult Eval(Footprint c, Footprint[] structures = null, Rect[] obstacles = null,
            Rect[] resources = null, Rect[] units = null) =>
            PlacementRules.Evaluate(c, Map, structures ?? NoStructures, obstacles ?? None, resources ?? None, units ?? None,
                Clearance, ResourceClearance);

        private static Footprint At(float x, float z, float w = 6, float d = 6, float apron = 0) =>
            Footprint.FromCenter(new Vector2(x, z), new Vector2(w, d), apron);

        [Test]
        public void OpenGround_IsValid()
        {
            Assert.AreEqual(PlacementResult.Valid, Eval(At(0, 0)));
        }

        [Test]
        public void Footprint_ApronIsOnPositiveZSide()
        {
            var f = At(0, 0, 6, 6, 4);
            Assert.IsTrue(f.HasApron);
            Assert.AreEqual(3f, f.Apron.yMin);
            Assert.AreEqual(7f, f.Apron.yMax);
            Assert.AreEqual(f.Body.xMin, f.Apron.xMin);
        }

        [Test]
        public void Footprint_FacingSouth_PutsApronOnNegativeZSide()
        {
            var f = Footprint.FromCenter(new Vector2(0, 0), new Vector2(6, 6), 4, facesSouth: true);
            Assert.AreEqual(-7f, f.Apron.yMin);
            Assert.AreEqual(-3f, f.Apron.yMax);
            Assert.AreEqual(PlacementResult.OutOfBounds, Eval(Footprint.FromCenter(new Vector2(0, -45), new Vector2(6, 6), 4, true)));
            Assert.AreEqual(PlacementResult.Valid, Eval(Footprint.FromCenter(new Vector2(0, 45), new Vector2(6, 6), 4, true)),
                "Facing south near the north edge is fine");
        }

        [Test]
        public void OutsideMap_IsRejected_IncludingApron()
        {
            Assert.AreEqual(PlacementResult.OutOfBounds, Eval(At(48, 0)));
            Assert.AreEqual(PlacementResult.OutOfBounds, Eval(At(0, 45, 6, 6, 4)), "Apron would leave the map");
        }

        [Test]
        public void OverlappingStructure_IsRejected()
        {
            Assert.AreEqual(PlacementResult.OverlapsStructure, Eval(At(2, 2), new[] { At(0, 0) }));
        }

        [Test]
        public void TouchingStructureWithoutClearance_IsRejected_ButGapIsAccepted()
        {
            // Bodies 6 wide: centres 6 apart touch; 7 apart leave a 1 m gap (< 1.5 clearance); 8 apart is fine.
            Assert.AreEqual(PlacementResult.OverlapsStructure, Eval(At(6, 0), new[] { At(0, 0) }));
            Assert.AreEqual(PlacementResult.OverlapsStructure, Eval(At(7, 0), new[] { At(0, 0) }));
            Assert.AreEqual(PlacementResult.Valid, Eval(At(8, 0), new[] { At(0, 0) }));
        }

        [Test]
        public void BuildingOnAnotherStructuresApron_BlocksAccess()
        {
            var refinery = At(0, 0, 6, 6, 4); // apron z 3..7
            Assert.AreEqual(PlacementResult.BlocksAccess, Eval(At(0, 9), new[] { refinery }));
        }

        [Test]
        public void NewApronOverExistingStructure_BlocksAccess()
        {
            var existing = At(0, 10);           // body z 7..13
            var factory = At(0, 0, 6, 6, 5);    // apron z 3..8 overlaps padded existing body
            Assert.AreEqual(PlacementResult.BlocksAccess, Eval(factory, new[] { existing }));
        }

        [Test]
        public void Obstacles_RespectClearance()
        {
            var wall = new Rect(4, -10, 2, 20); // x 4..6
            Assert.AreEqual(PlacementResult.OverlapsObstacle, Eval(At(0, 0), obstacles: new[] { wall })); // body x -3..3, gap 1
            Assert.AreEqual(PlacementResult.Valid, Eval(At(-2, 0), obstacles: new[] { wall }));         // gap 3
        }

        [Test]
        public void Resources_KeepWiderAccessRing()
        {
            var ore = new Rect(5, -2, 4, 4); // x 5..9
            Assert.AreEqual(PlacementResult.OverlapsResource, Eval(At(0, 0), resources: new[] { ore })); // gap 2 < 3
            Assert.AreEqual(PlacementResult.Valid, Eval(At(-1.5f, 0), resources: new[] { ore }));       // gap 3.5
        }

        [Test]
        public void UnitsInsideFootprint_AreRejected()
        {
            var unit = new Rect(-0.5f, -0.5f, 1, 1);
            Assert.AreEqual(PlacementResult.OverlapsUnit, Eval(At(0, 0), units: new[] { unit }));
            Assert.AreEqual(PlacementResult.Valid, Eval(At(10, 0), units: new[] { unit }));
        }

        [Test]
        public void Snap_RoundsToGrid()
        {
            Assert.AreEqual(new Vector2(3f, -2f), PlacementRules.Snap(new Vector2(2.6f, -2.4f), 1f));
            Assert.AreEqual(new Vector2(2.6f, -2.4f), PlacementRules.Snap(new Vector2(2.6f, -2.4f), 0f));
        }

        [Test]
        public void EveryResult_HasDescription()
        {
            foreach (PlacementResult r in System.Enum.GetValues(typeof(PlacementResult)))
                Assert.IsFalse(string.IsNullOrEmpty(PlacementRules.Describe(r)));
        }
    }
}
