using System.Collections.Generic;
using System.Linq;
using Ironfront.Core.Procedural;
using Ironfront.EditorTools.Art;
using NUnit.Framework;
using UnityEngine;

namespace Ironfront.Tests
{
    /// <summary>
    /// Procedural art contracts: noise is deterministic and tileable, the mesh kit emits outward-facing closed
    /// geometry, and every visual model keeps the shape its gameplay prefab relies on (named nodes, faction parts,
    /// staying inside the structure footprint, sensible polygon budgets).
    /// </summary>
    public class ProceduralArtTests
    {
        private ArtLibrary _art;
        private readonly List<Object> _created = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            ArtLibrary.PersistMeshes = false;
            _art = ArtLibrary.InMemory();
        }

        [TearDown]
        public void TearDown()
        {
            ArtLibrary.PersistMeshes = true;
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private Transform Root(string name)
        {
            var go = new GameObject(name);
            _created.Add(go);
            return go.transform;
        }

        // ---------- Noise ----------

        [Test]
        public void Noise_IsDeterministicInRangeAndTileable()
        {
            for (int i = 0; i < 200; i++)
            {
                float u = i * 0.0137f % 1f, v = i * 0.0291f % 1f;
                float a = ProceduralNoise.Fbm(u, v, 8, 4, 3);
                Assert.AreEqual(a, ProceduralNoise.Fbm(u, v, 8, 4, 3));
                Assert.That(a, Is.InRange(0f, 1f));
            }
            // Tileable: the value at u = 0 equals the value at u = 1 (same lattice after wrapping).
            for (int i = 0; i < 20; i++)
            {
                float v = i / 20f;
                Assert.AreEqual(ProceduralNoise.Fbm(0f, v, 6, 3, 9), ProceduralNoise.Fbm(1f, v, 6, 3, 9), 1e-4f);
            }
        }

        [Test]
        public void Step_IsAMaskBetweenTheEdges()
        {
            Assert.AreEqual(0f, ProceduralNoise.Step(2f, 4f, 1f));
            Assert.AreEqual(1f, ProceduralNoise.Step(2f, 4f, 5f));
            Assert.AreEqual(0.5f, ProceduralNoise.Step(2f, 4f, 3f), 1e-5f);
            Assert.Less(ProceduralNoise.Step(2f, 4f, 2.5f), ProceduralNoise.Step(2f, 4f, 3.5f));
        }

        // ---------- Mesh kit ----------

        private static void AssertOutward(Mesh mesh, Vector3 center, string what)
        {
            var v = mesh.vertices;
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var t = mesh.GetTriangles(s);
                for (int i = 0; i < t.Length; i += 3)
                {
                    Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                    Vector3 n = Vector3.Cross(b - a, c - a);
                    Vector3 mid = (a + b + c) / 3f;
                    Assert.Greater(Vector3.Dot(n, mid - center), -1e-5f, $"{what}: triangle {i / 3} faces inwards");
                }
            }
        }

        [Test]
        public void MeshKit_BoxesAndBlocksFaceOutwards()
        {
            var m = _art.Concrete_;
            var k = new MeshKit();
            k.Box(m, new Vector3(1f, 2f, 3f), new Vector3(2f, 1f, 3f), bevel: 0.2f, bottomFace: true);
            AssertOutward(k.Build("box"), new Vector3(1f, 2f, 3f), "chamfered box");

            k = new MeshKit();
            k.Block(m, Vector3.zero, new Vector2(2f, 2f), new Vector2(1f, 1.2f), 1.5f, new Vector2(0.2f, 0f), bottomFace: true);
            AssertOutward(k.Build("block"), new Vector3(0f, 0.75f, 0f), "tapered block");

            k = new MeshKit();
            k.ExtrudeX(m, new[] { new Vector2(-1f, 0f), new Vector2(1f, 0f), new Vector2(1.2f, 0.4f), new Vector2(-1f, 0.6f) }, 1f);
            AssertOutward(k.Build("prism"), new Vector3(0f, 0.25f, 0f), "extruded hull");
        }

        [Test]
        public void MeshKit_LatheSidesFaceAwayFromAxis_AndCapsOutwards()
        {
            var k = new MeshKit();
            k.Lathe(_art.Concrete_, new[] { new Vector2(1f, 0f), new Vector2(0.7f, 1f), new Vector2(0.8f, 2f) }, 12, Vector3.zero, capTop: true, capBottom: true);
            AssertOutward(k.Build("lathe"), new Vector3(0f, 1f, 0f), "lathe");
        }

        [Test]
        public void MeshKit_GroupsTrianglesIntoOneSubmeshPerMaterial()
        {
            var k = new MeshKit();
            k.Box(_art.Concrete_, Vector3.zero, Vector3.one);
            k.Box(_art.SteelDark_, Vector3.right * 2f, Vector3.one);
            k.Box(_art.Concrete_, Vector3.right * 4f, Vector3.one);
            var mesh = k.Build("grouped");
            Assert.AreEqual(2, mesh.subMeshCount);
            Assert.AreEqual(2, k.Materials.Count);
        }

        // ---------- Models ----------

        [Test]
        public void TankModels_ExposeTurretPivotTurretMuzzleAndFactionParts()
        {
            foreach (var (name, parts) in new[]
            {
                ("Light", VehicleModels.LightTank(Root("Light"), _art)),
                ("Heavy", VehicleModels.HeavyTank(Root("Heavy"), _art)),
            })
            {
                Assert.AreEqual("TurretPivot", parts.TurretPivot.name);
                Assert.AreEqual("Turret", parts.Turret.name, name + ": CombatPlayModeTests read TurretPivot/Turret");
                Assert.AreSame(parts.TurretPivot, parts.Turret.transform.parent);
                Assert.AreSame(_art.Faction, parts.Turret.sharedMaterial, name + " turret carries the livery");
                Assert.IsTrue(parts.FactionRenderers.All(r => r.sharedMaterials.Length == 1 && r.sharedMaterial == _art.Faction),
                    name + ": livery renderers are single-material so one property block tints them");
                Assert.Greater(parts.Muzzle.localPosition.z, 1f, name + " muzzle is at the gun tip");
                Assert.IsTrue(parts.Barrel.IsChildOf(parts.TurretPivot), name + " barrel turns with the turret");
                Assert.IsEmpty(parts.Hull.root.GetComponentsInChildren<Collider>(), name + " model adds no colliders");
            }
        }

        [Test]
        public void HarvesterModel_CargoPivotSitsOnTheHopperFloor()
        {
            var parts = VehicleModels.Harvester(Root("Harvester"), _art);
            Assert.AreEqual("Cargo", parts.Cargo.name);
            var bounds = parts.Cargo.GetComponentInChildren<MeshRenderer>().GetComponent<MeshFilter>().sharedMesh.bounds;
            Assert.GreaterOrEqual(bounds.min.y, -0.06f, "the load grows upwards when Harvester scales it in y");
            Assert.IsEmpty(parts.Body.root.GetComponentsInChildren<Collider>());
        }

        [Test]
        public void StructureModels_StayInsideTheirFootprints()
        {
            var cases = new (string name, System.Func<Transform, StructureModels.Parts> build, Vector2 footprint)[]
            {
                ("Headquarters", t => StructureModels.Headquarters(t, _art), new Vector2(8f, 8f)),
                ("Refinery", t => StructureModels.Refinery(t, _art), new Vector2(7f, 6f)),
                ("PowerPlant", t => StructureModels.PowerPlant(t, _art), new Vector2(5f, 5f)),
                ("VehicleFactory", t => StructureModels.VehicleFactory(t, _art), new Vector2(8f, 7f)),
                ("Outpost", t => StructureModels.Outpost(t, _art), new Vector2(6f, 6f)),
            };
            foreach (var (name, build, footprint) in cases)
            {
                var root = Root(name);
                var parts = build(root);
                var b = new Bounds(Vector3.zero, Vector3.zero);
                foreach (var r in parts.Model.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var mb = r.GetComponent<MeshFilter>().sharedMesh.bounds;
                    var world = r.transform.TransformPoint(mb.center);
                    b.Encapsulate(new Bounds(world, Vector3.Scale(mb.size, r.transform.lossyScale)));
                }
                // The front may reach a little onto the apron (door frames, crane); sides and back must not overhang.
                Assert.LessOrEqual(b.max.x, footprint.x * 0.5f + 0.15f, name + " +x");
                Assert.GreaterOrEqual(b.min.x, -footprint.x * 0.5f - 0.15f, name + " -x");
                Assert.GreaterOrEqual(b.min.z, -footprint.y * 0.5f - 0.15f, name + " back");
                Assert.LessOrEqual(b.max.z, footprint.y * 0.5f + 0.8f, name + " front");
                Assert.IsEmpty(root.GetComponentsInChildren<Collider>(), name + " model adds no colliders");
                Assert.IsTrue(parts.Faction.Count > 0 || parts.FactionGlow.Count > 0, name + " shows its owner");
            }
        }

        [Test]
        public void Models_StayWithinPolygonBudgets()
        {
            int Tris(Transform t) => t.GetComponentsInChildren<MeshFilter>(true).Sum(f => (int)f.sharedMesh.triangles.Length / 3);
            var light = Root("L"); VehicleModels.LightTank(light, _art);
            var heavy = Root("H"); VehicleModels.HeavyTank(heavy, _art);
            var harv = Root("V"); VehicleModels.Harvester(harv, _art);
            var hq = Root("Q"); StructureModels.Headquarters(hq, _art);
            Assert.Less(Tris(light), 2000, "light tank");
            Assert.Less(Tris(heavy), 2500, "heavy tank");
            Assert.Less(Tris(harv), 3000, "harvester");
            Assert.Less(Tris(hq), 4000, "headquarters");
            Assert.Less(light.GetComponentsInChildren<Renderer>().Length, 9, "light tank renderer count (draw calls at 200 units)");
            Assert.Less(heavy.GetComponentsInChildren<Renderer>().Length, 9, "heavy tank renderer count");
        }
    }
}
