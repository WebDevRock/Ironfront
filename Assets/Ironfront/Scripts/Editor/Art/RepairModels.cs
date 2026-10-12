using UnityEngine;

namespace Ironfront.EditorTools.Art
{
    /// <summary>
    /// Milestone 009 Repair Pad model in the Milestone 006 style: a concrete workshop (the collider footprint, 7 × 5 m)
    /// and, on the apron in front of it, the marked repair bay under a cantilever gantry carrying a trolley with two
    /// welding arms. The entrance lane (left) and exit lane (right) are painted; a status lamp sits on the gantry.
    /// Local +Z is the building front. Nothing on the apron has a collider (vehicles drive onto it).
    /// </summary>
    public static class RepairModels
    {
        public sealed class PadParts
        {
            public StructureModels.Parts Parts;
            public Transform Gantry, Trolley;
            public Transform[] Arms;
            public Transform[] SparkPoints;
            public Renderer Lamp, BayRing;
        }

        public const float BodyDepth = 5f, BodyWidth = 7f, Apron = 8f;
        public static float BayZ => BodyDepth * 0.5f + Apron * 0.5f;

        public static PadParts RepairPad(Transform root, ArtLibrary a)
        {
            var p = StructureModels.Begin(root);
            var r = new PadParts { Parts = p };

            // Workshop.
            var k = new MeshKit();
            k.Block(a.ConcreteDark_, Vector3.zero, new Vector2(BodyWidth, BodyDepth), new Vector2(BodyWidth - 0.3f, BodyDepth - 0.3f), 0.3f);
            k.Box(a.Concrete_, new Vector3(0f, 1.35f, -0.2f), new Vector3(BodyWidth - 0.8f, 2.1f, BodyDepth - 0.9f), 0.06f);
            k.Block(a.SteelLight_, new Vector3(0f, 2.4f, -0.2f), new Vector2(BodyWidth - 0.6f, BodyDepth - 0.7f), new Vector2(BodyWidth - 1.4f, BodyDepth - 1.8f), 0.45f);
            k.Box(a.SteelDark_, new Vector3(0f, 1.15f, BodyDepth * 0.5f - 0.62f), new Vector3(3.4f, 1.7f, 0.06f)); // roller door
            for (int i = 0; i < 6; i++)
                k.Box(a.SteelLight_, new Vector3(0f, 0.45f + i * 0.27f, BodyDepth * 0.5f - 0.58f), new Vector3(3.3f, 0.03f, 0.03f));
            k.Box(a.Hazard, new Vector3(-1.85f, 1.15f, BodyDepth * 0.5f - 0.6f), new Vector3(0.12f, 1.8f, 0.08f));
            k.Box(a.Hazard, new Vector3(1.85f, 1.15f, BodyDepth * 0.5f - 0.6f), new Vector3(0.12f, 1.8f, 0.08f));
            // Tool racks and gas bottles at the sides.
            foreach (float x in new[] { -3.05f, 3.05f })
            {
                k.Box(a.SteelDark_, new Vector3(x, 0.8f, -0.6f), new Vector3(0.3f, 1.3f, 2.2f));
                for (int i = 0; i < 3; i++) k.Cylinder(i % 2 == 0 ? a.PaintIndustrial : a.SteelLight_, new Vector3(x, 0.3f, 1.0f + i * 0.35f), 0.13f, 0.95f, 6);
            }
            // Gantry: two masts at the back corners of the bay carrying a beam out over it.
            float bayZ = BayZ;
            foreach (float x in new[] { -3.3f, 3.3f })
            {
                k.Box(a.SteelDark_, new Vector3(x, 2.0f, BodyDepth * 0.5f - 0.1f), new Vector3(0.35f, 4.0f, 0.35f));
                k.Box(a.SteelDark_, new Vector3(x, 3.85f, bayZ - 0.4f), new Vector3(0.3f, 0.3f, Apron - 1.6f));
            }
            k.Box(a.SteelDark_, new Vector3(0f, 3.85f, BodyDepth * 0.5f - 0.1f), new Vector3(6.9f, 0.3f, 0.3f));
            ArtLibrary.Attach(p.Model, "Structure", "RepairPad_Structure", k);

            // Apron: slab, hazard edges, bay circle, lane chevrons (visual only, slightly above the ground).
            var apron = new MeshKit();
            apron.Box(a.Concrete_, new Vector3(0f, 0.02f, bayZ), new Vector3(BodyWidth, 0.04f, Apron), 0f);
            apron.Box(a.Hazard, new Vector3(-BodyWidth * 0.5f + 0.15f, 0.05f, bayZ), new Vector3(0.3f, 0.02f, Apron));
            apron.Box(a.Hazard, new Vector3(BodyWidth * 0.5f - 0.15f, 0.05f, bayZ), new Vector3(0.3f, 0.02f, Apron));
            apron.Box(a.Hazard, new Vector3(0f, 0.05f, BodyDepth * 0.5f + Apron - 0.15f), new Vector3(BodyWidth, 0.02f, 0.3f));
            for (int i = 0; i < 3; i++) // entrance chevrons (left, pointing in) and exit chevrons (right, pointing out)
            {
                apron.Box(a.GlowCool_, new Vector3(-2.6f + i * 0.45f, 0.06f, bayZ + 3.2f), new Vector3(0.25f, 0.02f, 0.5f));
                apron.Box(a.PaintIndustrial, new Vector3(1.7f + i * 0.45f, 0.06f, bayZ + 3.2f), new Vector3(0.25f, 0.02f, 0.5f));
            }
            ArtLibrary.Attach(p.Model, "Apron", "RepairPad_Apron", apron, shadows: false);

            var ring = new MeshKit();
            ring.Lathe(a.GlowCool_, new[] { new Vector2(2.75f, 0f), new Vector2(2.95f, 0f) }, 28, new Vector3(0f, 0.07f, bayZ), capTop: false);
            ring.Lathe(a.GlowCool_, new[] { new Vector2(2.95f, 0f), new Vector2(2.75f, 0.001f) }, 28, new Vector3(0f, 0.07f, bayZ), capTop: false);
            r.BayRing = ArtLibrary.Attach(p.Model, "BayRing", "RepairPad_BayRing", ring, shadows: false);

            // Moving gantry parts: trolley on the beam with two welding arms.
            r.Gantry = VehicleModels.Node(p.Model, "Gantry", new Vector3(0f, 3.85f, bayZ));
            var beam = new MeshKit();
            beam.Box(a.PaintIndustrial, Vector3.zero, new Vector3(6.4f, 0.32f, 0.5f), 0.03f);
            beam.Box(a.Hazard, new Vector3(0f, -0.17f, 0f), new Vector3(6.4f, 0.04f, 0.52f));
            ArtLibrary.Attach(r.Gantry, "Beam", "RepairPad_Beam", beam);
            r.Trolley = VehicleModels.Node(r.Gantry, "Trolley", Vector3.zero);
            var trolley = new MeshKit();
            trolley.Box(a.SteelDark_, new Vector3(0f, -0.35f, 0f), new Vector3(0.9f, 0.4f, 0.8f), 0.04f);
            ArtLibrary.Attach(r.Trolley, "Carriage", "RepairPad_Trolley", trolley);
            r.Arms = new Transform[2];
            r.SparkPoints = new Transform[2];
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                var arm = VehicleModels.Node(r.Trolley, i == 0 ? "ArmL" : "ArmR", new Vector3(0.3f * side, -0.55f, 0f));
                var m = new MeshKit();
                m.Box(a.SteelLight_, new Vector3(0f, -0.6f, 0f), new Vector3(0.12f, 1.2f, 0.12f));
                m.Box(a.SteelDark_, new Vector3(0.2f * side, -1.25f, 0f), new Vector3(0.45f, 0.1f, 0.1f));
                m.Cylinder(a.Hazard, new Vector3(0.42f * side, -1.42f, 0f), 0.05f, 0.2f, 6);
                ArtLibrary.Attach(arm, "Arm", i == 0 ? "RepairPad_ArmL" : "RepairPad_ArmR", m);
                r.Arms[i] = arm;
                r.SparkPoints[i] = VehicleModels.Node(arm, "Tip", new Vector3(0.42f * side, -1.5f, 0f));
            }

            // Status lamp on the gantry mast (colour driven at runtime: available / occupied / unpowered / assembling).
            var lampNode = VehicleModels.Node(p.Model, "StatusLamp", new Vector3(3.3f, 4.25f, BodyDepth * 0.5f - 0.1f));
            var lamp = new MeshKit();
            lamp.Lathe(a.GlowCool_, new[] { new Vector2(0.2f, 0f), new Vector2(0.2f, 0.25f), new Vector2(0f, 0.32f) }, 10, Vector3.zero, capTop: false);
            r.Lamp = ArtLibrary.Attach(lampNode, "Lamp", "RepairPad_Lamp", lamp, shadows: false);

            // Faction livery on the workshop roof edge.
            var faction = new MeshKit();
            faction.Box(a.Faction, new Vector3(0f, 2.42f, BodyDepth * 0.5f - 0.6f), new Vector3(BodyWidth - 0.6f, 0.1f, 0.2f));
            faction.Box(a.Faction, new Vector3(0f, 2.42f, -BodyDepth * 0.5f + 0.25f), new Vector3(BodyWidth - 0.6f, 0.1f, 0.2f));
            p.Faction.Add(ArtLibrary.Attach(p.Model, "Livery", "RepairPad_Livery", faction));
            return r;
        }
    }
}
