using UnityEngine;

namespace Ironfront.EditorTools.Art
{
    /// <summary>
    /// Milestone 010 models in the Milestone 006 style: the Mobile Command Vehicle (a long eight-wheeled carrier with a
    /// folded command module, a mast and four outriggers) and the Forward Command Centre it deploys into (a low armoured
    /// command post on a concrete pad with a radar mast, comms dish and landing lights). Local +Z is forward/front.
    /// Faction-coloured parts are separate renderers tinted at runtime. Models have no colliders.
    /// </summary>
    public static class CommandModels
    {
        public sealed class McvParts
        {
            public Transform Body;          // tilted by VehicleMotionFx
            public Transform Module;        // rises while deploying
            public Transform Mast;          // extends while deploying
            public Transform[] Outriggers;  // swing out while deploying
            public Transform Beacon;
            public Renderer[] FactionRenderers;
            public Transform[] DustPoints;
        }

        public const float McvWidth = 2.8f, McvLength = 6.0f, McvHeight = 2.4f;

        public static McvParts Mcv(Transform root, ArtLibrary a)
        {
            var p = new McvParts();
            p.Body = VehicleModels.Node(root, "Body", Vector3.zero);

            var chassis = new MeshKit();
            foreach (float s in new[] { -1f, 1f })
                foreach (float z in new[] { 2.05f, 0.85f, -0.75f, -1.95f })
                    chassis.Wheel(a.Rubber, new Vector3(s * 1.16f, 0.48f, z), 0.48f, 0.42f, 12, a.SteelDark_);
            chassis.Box(a.SteelDark_, new Vector3(0f, 0.62f, 0f), new Vector3(1.8f, 0.36f, 5.6f), 0.04f);              // frame
            chassis.Block(a.PaintHeavy, new Vector3(0f, 0.8f, -0.2f), new Vector2(2.6f, 5.2f), new Vector2(2.5f, 5.0f), 0.32f); // deck
            // Armoured cab at the front with a raked glacis.
            chassis.Hexahedron(a.PaintHeavy, new[]
            {
                new Vector3(-1.25f, 1.1f, 1.7f), new Vector3(-1.25f, 1.1f, 2.95f), new Vector3(1.25f, 1.1f, 2.95f), new Vector3(1.25f, 1.1f, 1.7f),
                new Vector3(-1.15f, 2.05f, 1.7f), new Vector3(-1.1f, 1.75f, 2.55f), new Vector3(1.1f, 1.75f, 2.55f), new Vector3(1.15f, 2.05f, 1.7f),
            });
            chassis.Box(a.SteelDark_, new Vector3(0f, 1.0f, 3.0f), new Vector3(2.3f, 0.3f, 0.2f), 0.03f);              // bumper
            // Side skirts and stowage.
            foreach (float s in new[] { -1f, 1f })
            {
                chassis.Box(a.PaintHeavy, new Vector3(s * 1.33f, 0.98f, 0.05f), new Vector3(0.08f, 0.42f, 4.3f), 0.02f);
                chassis.Box(a.SteelDark_, new Vector3(s * 1.0f, 1.3f, 1.35f), new Vector3(0.5f, 0.35f, 0.5f), 0.04f);
            }
            chassis.Box(a.Hazard, new Vector3(0f, 1.13f, -2.75f), new Vector3(2.5f, 0.12f, 0.06f));
            ArtLibrary.Attach(p.Body, "Chassis", "Mcv_Chassis", chassis);

            var glass = new MeshKit();
            glass.Polygon(a.Glass, new Vector3(-1.0f, 1.25f, 2.86f), new Vector3(1.0f, 1.25f, 2.86f), new Vector3(1.0f, 1.68f, 2.6f), new Vector3(-1.0f, 1.68f, 2.6f));
            ArtLibrary.Attach(p.Body, "Windows", "Mcv_Windows", glass, shadows: false);

            // Folded command module on the deck (rises and unfolds while deploying).
            p.Module = VehicleModels.Node(p.Body, "Module", new Vector3(0f, 1.12f, -0.55f));
            var module = new MeshKit();
            module.Block(a.Concrete_, Vector3.zero, new Vector2(2.3f, 3.2f), new Vector2(2.1f, 3.0f), 0.9f);
            module.Block(a.SteelLight_, new Vector3(0f, 0.9f, 0f), new Vector2(2.1f, 3.0f), new Vector2(1.6f, 2.4f), 0.3f);
            module.Box(a.SteelDark_, new Vector3(0f, 0.45f, 1.62f), new Vector3(1.2f, 0.6f, 0.06f));
            module.Cylinder(a.SteelLight_, new Vector3(0.6f, 1.2f, -0.8f), 0.3f, 0.2f, 8);
            ArtLibrary.Attach(p.Module, "Module", "Mcv_Module", module);
            var glow = new MeshKit();
            glow.Box(a.GlowWarm_, new Vector3(-1.16f, 0.55f, 0f), new Vector3(0.04f, 0.14f, 2.2f));
            glow.Box(a.GlowWarm_, new Vector3(1.16f, 0.55f, 0f), new Vector3(0.04f, 0.14f, 2.2f));
            ArtLibrary.Attach(p.Module, "Lights", "Mcv_ModuleLights", glow, shadows: false);

            var livery = new MeshKit();
            livery.Box(a.Faction, new Vector3(0f, 1.21f, 0f), new Vector3(1.55f, 0.04f, 2.3f), 0.01f);
            livery.Box(a.Faction, new Vector3(-1.17f, 0.25f, 0.6f), new Vector3(0.04f, 0.3f, 1.2f));
            livery.Box(a.Faction, new Vector3(1.17f, 0.25f, 0.6f), new Vector3(0.04f, 0.3f, 1.2f));
            var moduleLivery = ArtLibrary.Attach(p.Module, "Livery", "Mcv_ModuleLivery", livery);
            var cabLivery = new MeshKit();
            cabLivery.Box(a.Faction, new Vector3(0f, 2.07f, 2.1f), new Vector3(2.0f, 0.05f, 0.7f), 0.01f);
            cabLivery.Box(a.Faction, new Vector3(-1.34f, 1.02f, -1.1f), new Vector3(0.03f, 0.2f, 1.8f));
            cabLivery.Box(a.Faction, new Vector3(1.34f, 1.02f, -1.1f), new Vector3(0.03f, 0.2f, 1.8f));
            var bodyLivery = ArtLibrary.Attach(p.Body, "Livery", "Mcv_Livery", cabLivery);

            // Telescopic mast (scaled up in y while deploying).
            p.Mast = VehicleModels.Node(p.Module, "Mast", new Vector3(-0.7f, 1.2f, -1.1f));
            var mast = new MeshKit();
            mast.Cylinder(a.SteelDark_, Vector3.zero, 0.07f, 1.4f, 6);
            mast.Box(a.SteelDark_, new Vector3(0f, 1.35f, 0f), new Vector3(0.6f, 0.04f, 0.04f));
            ArtLibrary.Attach(p.Mast, "Mast", "Mcv_Mast", mast);
            p.Beacon = VehicleModels.Node(p.Mast, "Beacon", new Vector3(0f, 1.45f, 0f));
            var lamp = new MeshKit();
            lamp.Box(a.FactionGlow, Vector3.zero, new Vector3(0.14f, 0.14f, 0.14f));
            ArtLibrary.Attach(p.Beacon, "Lamp", "Mcv_Beacon", lamp, shadows: false);

            // Outriggers: folded along the hull sides, pivot at the deck edge.
            p.Outriggers = new Transform[4];
            int n = 0;
            foreach (float s in new[] { -1f, 1f })
                foreach (float z in new[] { 1.3f, -2.2f })
                {
                    var leg = VehicleModels.Node(p.Body, s < 0 ? (z > 0 ? "OutriggerFL" : "OutriggerRL") : (z > 0 ? "OutriggerFR" : "OutriggerRR"),
                        new Vector3(s * 1.3f, 1.05f, z));
                    var k = new MeshKit();
                    k.Box(a.SteelDark_, new Vector3(0f, -0.35f, 0f), new Vector3(0.16f, 0.7f, 0.2f), 0.02f);
                    k.Box(a.Hazard, new Vector3(0f, -0.72f, 0f), new Vector3(0.36f, 0.06f, 0.36f));
                    ArtLibrary.Attach(leg, "Leg", "Mcv_Outrigger", k);
                    p.Outriggers[n++] = leg;
                }

            p.FactionRenderers = new Renderer[] { moduleLivery, bodyLivery };
            p.DustPoints = new[]
            {
                VehicleModels.Node(p.Body, "DustL", new Vector3(-1.16f, 0.05f, -2.3f)),
                VehicleModels.Node(p.Body, "DustR", new Vector3(1.16f, 0.05f, -2.3f)),
            };
            return p;
        }

        // ---------------- Forward Command Centre (9 x 9) ----------------

        public const float CentreSize = 9f;

        public static StructureModels.Parts ForwardCommand(Transform root, ArtLibrary a)
        {
            var p = StructureModels.Begin(root);
            float h = CentreSize * 0.5f;
            var k = new MeshKit();
            // Concrete pad with hazard edges and an octagonal armoured post.
            k.Block(a.ConcreteDark_, Vector3.zero, new Vector2(CentreSize, CentreSize), new Vector2(CentreSize - 0.3f, CentreSize - 0.3f), 0.3f);
            var oct = new Vector2[8];
            for (int i = 0; i < 8; i++)
            {
                float ang = (i + 0.5f) * Mathf.PI * 2f / 8f;
                oct[i] = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * 3.6f;
            }
            k.ExtrudeY(a.Concrete_, oct, 0.3f, 1.7f, a.ConcreteDark_);
            k.Block(a.Concrete_, new Vector3(0f, 1.7f, 0f), new Vector2(6.0f, 6.0f), new Vector2(4.6f, 4.6f), 0.6f);
            // Command cabin, set back, with a slit window band.
            k.Box(a.Concrete_, new Vector3(0f, 2.75f, -0.6f), new Vector3(3.0f, 0.9f, 2.6f), 0.08f);
            k.Block(a.SteelLight_, new Vector3(0f, 3.2f, -0.6f), new Vector2(3.0f, 2.6f), new Vector2(2.6f, 2.2f), 0.3f);
            // Entrance with hazard frame.
            k.Box(a.SteelDark_, new Vector3(0f, 1.0f, 3.3f), new Vector3(1.8f, 1.2f, 0.1f));
            k.Box(a.Hazard, new Vector3(0f, 1.66f, 3.34f), new Vector3(2.2f, 0.12f, 0.06f));
            // Outrigger feet (the deployed vehicle's legs) at the four corners.
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                {
                    k.Box(a.SteelDark_, new Vector3(sx * (h - 0.7f), 0.55f, sz * (h - 0.7f)), new Vector3(0.5f, 0.5f, 0.5f), 0.04f);
                    k.Box(a.Hazard, new Vector3(sx * (h - 0.7f), 0.32f, sz * (h - 0.7f)), new Vector3(0.8f, 0.05f, 0.8f));
                }
            // Hazard edging on the pad.
            k.Box(a.Hazard, new Vector3(0f, 0.32f, h - 0.2f), new Vector3(CentreSize - 2.4f, 0.03f, 0.2f));
            k.Box(a.Hazard, new Vector3(0f, 0.32f, -h + 0.2f), new Vector3(CentreSize - 2.4f, 0.03f, 0.2f));
            // Radar mast.
            k.Cylinder(a.SteelDark_, new Vector3(1.6f, 2.3f, -2.0f), 0.08f, 3.2f, 6);
            k.Box(a.SteelDark_, new Vector3(1.6f, 4.2f, -2.0f), new Vector3(0.7f, 0.05f, 0.05f));
            // Generator box and fuel drums.
            k.Box(a.SteelDark_, new Vector3(-2.1f, 2.0f, 1.4f), new Vector3(1.0f, 0.5f, 0.8f), 0.04f);
            for (int i = 0; i < 3; i++) k.Cylinder(a.PaintIndustrial, new Vector3(-3.5f + i * 0.0f, 0.3f, -2.4f + i * 0.6f), 0.25f, 0.8f, 8);
            ArtLibrary.Attach(p.Model, "Structure", "FCC_Structure", k);

            var glass = new MeshKit();
            glass.Box(a.Glass, new Vector3(0f, 2.85f, -0.6f), new Vector3(3.04f, 0.3f, 2.64f));
            ArtLibrary.Attach(p.Model, "Glass", "FCC_Glass", glass, shadows: false);

            var glow = new MeshKit();
            for (int i = 0; i < 8; i++)
            {
                float ang = i * Mathf.PI * 2f / 8f;
                var dir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                glow.Box(a.GlowCool_, dir * (h - 0.25f) + Vector3.up * 0.33f, new Vector3(0.18f, 0.06f, 0.18f)); // landing lights
            }
            glow.Box(a.GlowWarm_, new Vector3(0f, 1.2f, 3.56f), new Vector3(2.4f, 0.12f, 0.04f));
            ArtLibrary.Attach(p.Model, "Lights", "FCC_Lights", glow, shadows: false);

            var faction = new MeshKit();
            faction.Box(a.Faction, new Vector3(0f, 3.52f, -0.6f), new Vector3(2.5f, 0.06f, 2.1f), 0.02f);
            faction.Box(a.Faction, new Vector3(-1.4f, 1.2f, 3.32f), new Vector3(0.5f, 1.0f, 0.04f));
            faction.Box(a.Faction, new Vector3(1.4f, 1.2f, 3.32f), new Vector3(0.5f, 1.0f, 0.04f));
            p.Faction.Add(ArtLibrary.Attach(p.Model, "Livery", "FCC_Livery", faction));

            var radar = VehicleModels.Node(p.Model, "Radar", new Vector3(-0.6f, 3.5f, -1.2f));
            var dish = new MeshKit();
            dish.Cylinder(a.SteelDark_, Vector3.zero, 0.07f, 0.45f, 6);
            dish.Push(new Vector3(0f, 0.55f, 0f), Quaternion.Euler(-70f, 0f, 0f));
            dish.Lathe(a.SteelLight_, new[] { new Vector2(0.05f, 0f), new Vector2(0.4f, 0.1f), new Vector2(0.6f, 0.26f) }, 10, Vector3.zero, capTop: false);
            dish.Lathe(a.SteelLight_, new[] { new Vector2(0.6f, 0.25f), new Vector2(0.4f, 0.09f), new Vector2(0.05f, -0.01f) }, 10, Vector3.zero, capTop: false);
            dish.Pop();
            ArtLibrary.Attach(radar, "Dish", "FCC_Radar", dish);
            p.Spinners.Add(radar);

            var beacon = VehicleModels.Node(p.Model, "Beacon", new Vector3(1.6f, 4.35f, -2.0f));
            var lamp = new MeshKit();
            lamp.Box(a.FactionGlow, Vector3.zero, new Vector3(0.16f, 0.16f, 0.16f));
            p.FactionGlow.Add(ArtLibrary.Attach(beacon, "Lamp", "FCC_Beacon", lamp, shadows: false));
            p.Blinkers.Add(beacon);
            p.SmokePoints.Add(VehicleModels.Node(p.Model, "Exhaust", new Vector3(-2.1f, 2.4f, 1.4f)));
            return p;
        }
    }
}
