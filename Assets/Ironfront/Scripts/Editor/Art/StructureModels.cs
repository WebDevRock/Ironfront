using System.Collections.Generic;
using UnityEngine;

namespace Ironfront.EditorTools.Art
{
    /// <summary>
    /// Procedural visual models for Ironfront's structures (Milestone 006). Every model stays inside its
    /// building's footprint (so it never overlaps neighbours or the apron) and is added under a "Model" child;
    /// gameplay colliders, NavMesh obstacles and the footprint itself are unchanged. Fronts face +z (the apron).
    /// </summary>
    public static class StructureModels
    {
        public class Parts
        {
            public Transform Model;
            public readonly List<Renderer> Faction = new List<Renderer>();
            public readonly List<Renderer> FactionGlow = new List<Renderer>();
            public readonly List<Transform> Spinners = new List<Transform>();   // rotate about local y
            public readonly List<Transform> SmokePoints = new List<Transform>(); // chimney / steam
            public readonly List<Transform> Blinkers = new List<Transform>();   // beacon lights
        }

        internal static Parts Begin(Transform root)
        {
            var p = new Parts { Model = VehicleModels.Node(root, "Model", Vector3.zero) };
            return p;
        }

        private static void Plinth(MeshKit k, ArtLibrary a, float sx, float sz, float h = 0.3f)
        {
            k.Block(a.ConcreteDark_, Vector3.zero, new Vector2(sx, sz), new Vector2(sx - 0.25f, sz - 0.25f), h);
        }

        // ---------------- Headquarters (8 x 8) ----------------

        public static Parts Headquarters(Transform root, ArtLibrary a)
        {
            var p = Begin(root);
            var k = new MeshKit();
            Plinth(k, a, 8f, 8f, 0.45f);
            // Two-stage bastion: vertical lower walls, then sloped armour.
            k.Block(a.Concrete_, new Vector3(0f, 0.45f, 0f), new Vector2(7.2f, 7.2f), new Vector2(7.2f, 7.2f), 1.45f);
            k.Block(a.Concrete_, new Vector3(0f, 1.9f, 0f), new Vector2(7.2f, 7.2f), new Vector2(6.0f, 6.0f), 0.8f);
            // Command tower, set back.
            k.Box(a.Concrete_, new Vector3(0f, 3.15f, -0.7f), new Vector3(3.4f, 0.9f, 3.4f), 0.08f);
            k.Block(a.Concrete_, new Vector3(0f, 3.6f, -0.7f), new Vector2(3.4f, 3.4f), new Vector2(3.0f, 3.0f), 0.55f);
            // Corner buttresses.
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                    k.Block(a.ConcreteDark_, new Vector3(sx * 3.35f, 0.45f, sz * 3.35f), new Vector2(1.1f, 1.1f), new Vector2(0.8f, 0.8f), 1.7f);
            // Entrance: recessed door with a hazard-striped frame, under a canopy.
            k.Box(a.SteelDark_, new Vector3(0f, 1.1f, 3.58f), new Vector3(2.0f, 1.3f, 0.1f));
            k.Box(a.Hazard, new Vector3(0f, 1.82f, 3.62f), new Vector3(2.4f, 0.14f, 0.06f));
            k.Block(a.SteelDark_, new Vector3(0f, 1.95f, 3.8f), new Vector2(2.8f, 0.8f), new Vector2(2.8f, 0.7f), 0.08f);
            // Helipad on the front-left roof corner.
            k.Box(a.ConcreteDark_, new Vector3(-1.75f, 2.74f, 1.75f), new Vector3(2.0f, 0.08f, 2.0f));
            k.Box(a.Hazard, new Vector3(-2.15f, 2.79f, 1.75f), new Vector3(0.16f, 0.02f, 1.0f));
            k.Box(a.Hazard, new Vector3(-1.35f, 2.79f, 1.75f), new Vector3(0.16f, 0.02f, 1.0f));
            k.Box(a.Hazard, new Vector3(-1.75f, 2.79f, 1.75f), new Vector3(0.8f, 0.02f, 0.16f));
            // Roof clutter: vents and a comms box.
            k.Box(a.SteelDark_, new Vector3(2.2f, 2.85f, 1.9f), new Vector3(0.9f, 0.3f, 0.6f), 0.04f);
            k.Cylinder(a.SteelLight_, new Vector3(2.3f, 2.7f, -2.4f), 0.35f, 0.3f, 8);
            k.Cylinder(a.SteelLight_, new Vector3(1.5f, 2.7f, -2.4f), 0.35f, 0.3f, 8);
            // Antenna mast.
            k.Cylinder(a.SteelDark_, new Vector3(-1.2f, 4.15f, -1.9f), 0.06f, 1.6f, 6);
            k.Box(a.SteelDark_, new Vector3(-1.2f, 5.2f, -1.9f), new Vector3(0.5f, 0.04f, 0.04f));
            ArtLibrary.Attach(p.Model, "Structure", "HQ_Structure", k);

            var glass = new MeshKit();
            glass.Box(a.Glass, new Vector3(0f, 3.25f, -0.7f), new Vector3(3.44f, 0.36f, 3.44f));
            ArtLibrary.Attach(p.Model, "Glass", "HQ_Glass", glass, shadows: false);

            var glow = new MeshKit();
            foreach (var (pos, size) in new[]
            {
                (new Vector3(0f, 1.45f, -3.61f), new Vector3(4.6f, 0.18f, 0.04f)),
                (new Vector3(3.61f, 1.45f, 0f), new Vector3(0.04f, 0.18f, 4.6f)),
                (new Vector3(-3.61f, 1.45f, 0f), new Vector3(0.04f, 0.18f, 4.6f)),
                (new Vector3(-2.3f, 1.45f, 3.61f), new Vector3(1.4f, 0.18f, 0.04f)),
                (new Vector3(2.3f, 1.45f, 3.61f), new Vector3(1.4f, 0.18f, 0.04f)),
            })
                glow.Box(a.GlowWarm_, pos, size);
            ArtLibrary.Attach(p.Model, "Windows", "HQ_Windows", glow, shadows: false);

            var faction = new MeshKit();
            faction.Box(a.Faction, new Vector3(0f, 4.2f, -0.7f), new Vector3(3.1f, 0.1f, 3.1f), 0.03f);  // tower roof
            faction.Box(a.Faction, new Vector3(-1.75f, 2.0f, 3.61f), new Vector3(0.5f, 1.4f, 0.04f));     // banners
            faction.Box(a.Faction, new Vector3(1.75f, 2.0f, 3.61f), new Vector3(0.5f, 1.4f, 0.04f));
            foreach (float s in new[] { -1f, 1f })                                                        // roof edge band
            {
                faction.Box(a.Faction, new Vector3(0f, 2.72f, s * 2.85f), new Vector3(5.9f, 0.05f, 0.3f));
                faction.Box(a.Faction, new Vector3(s * 2.85f, 2.72f, 0f), new Vector3(0.3f, 0.05f, 5.4f));
            }
            p.Faction.Add(ArtLibrary.Attach(p.Model, "Livery", "HQ_Livery", faction));

            // Rotating radar on the tower roof.
            var radar = VehicleModels.Node(p.Model, "Radar", new Vector3(0.7f, 4.25f, -1.3f));
            var dish = new MeshKit();
            dish.Cylinder(a.SteelDark_, Vector3.zero, 0.08f, 0.5f, 6);
            dish.Push(new Vector3(0f, 0.62f, 0f), Quaternion.Euler(-70f, 0f, 0f));
            dish.Lathe(a.SteelLight_, new[] { new Vector2(0.06f, 0f), new Vector2(0.45f, 0.12f), new Vector2(0.7f, 0.3f) }, 10, Vector3.zero, capTop: false);
            dish.Lathe(a.SteelLight_, new[] { new Vector2(0.7f, 0.29f), new Vector2(0.45f, 0.11f), new Vector2(0.06f, -0.01f) }, 10, Vector3.zero, capTop: false);
            dish.Pop();
            ArtLibrary.Attach(radar, "Dish", "HQ_Radar", dish);
            p.Spinners.Add(radar);

            var beacon = VehicleModels.Node(p.Model, "Beacon", new Vector3(-1.2f, 5.78f, -1.9f));
            var lamp = new MeshKit();
            lamp.Box(a.FactionGlow, Vector3.zero, new Vector3(0.14f, 0.14f, 0.14f));
            p.FactionGlow.Add(ArtLibrary.Attach(beacon, "Lamp", "HQ_Beacon", lamp, shadows: false));
            p.Blinkers.Add(beacon);
            return p;
        }

        // ---------------- Refinery (7 x 6) ----------------

        public static Parts Refinery(Transform root, ArtLibrary a)
        {
            var p = Begin(root);
            var k = new MeshKit();
            Plinth(k, a, 7f, 6f);
            // Processing hall with a sawtooth roof.
            float hx0 = -3.25f, hx1 = 0.9f, hz0 = -2.75f, hz1 = 1.6f, hy = 2.5f;
            k.Block(a.Concrete_, new Vector3((hx0 + hx1) * 0.5f, 0.3f, (hz0 + hz1) * 0.5f), new Vector2(hx1 - hx0, hz1 - hz0), new Vector2(hx1 - hx0, hz1 - hz0), hy - 0.3f);
            int teeth = 3;
            float tw = (hz1 - hz0) / teeth;
            for (int i = 0; i < teeth; i++)
            {
                float z0 = hz0 + i * tw, z1 = z0 + tw;
                k.Push(new Vector3((hx0 + hx1) * 0.5f, 0f, 0f));
                k.ExtrudeX(a.SteelLight_, new[] { new Vector2(z0, hy), new Vector2(z1, hy), new Vector2(z1, hy + 0.7f) }, hx1 - hx0);
                k.Pop();
            }
            // Intake hopper on the dock side, with a conveyor to the hall roof.
            k.Block(a.SteelDark_, new Vector3(-1.2f, 0.3f, 2.35f), new Vector2(1.2f, 0.9f), new Vector2(2.0f, 1.3f), 1.0f);
            k.Box(a.Hazard, new Vector3(-1.2f, 1.33f, 2.35f), new Vector3(2.06f, 0.08f, 1.36f));
            k.Push(new Vector3(-1.2f, 1.6f, 1.75f), Quaternion.Euler(-28f, 0f, 0f));
            k.Box(a.SteelDark_, new Vector3(0f, 0f, -0.4f), new Vector3(0.5f, 0.18f, 1.6f));
            k.Pop();
            // Silos with ring bands and domed caps.
            foreach (float z in new[] { -1.55f, 0.85f })
            {
                var c = new Vector3(2.3f, 0.3f, z);
                k.Lathe(a.SteelLight_, new[] { new Vector2(0.95f, 0f), new Vector2(0.95f, 3.0f), new Vector2(0.75f, 3.45f), new Vector2(0.25f, 3.6f), new Vector2(0f, 3.6f) },
                    12, c, capTop: false);
                k.Lathe(a.SteelDark_, new[] { new Vector2(0.99f, 0.6f), new Vector2(0.99f, 0.75f) }, 12, c, capTop: false);
                k.Lathe(a.SteelDark_, new[] { new Vector2(0.99f, 2.2f), new Vector2(0.99f, 2.35f) }, 12, c, capTop: false);
                k.Tube(a.SteelDark_, new Vector3(1.3f, 2.2f, z), 0.12f, 0.1f, 6);
                k.Push(new Vector3(0.9f, 2.0f, z), Quaternion.Euler(0f, 90f, 0f));
                k.Tube(a.SteelDark_, Vector3.zero, 0.12f, 0.5f, 6);
                k.Pop();
            }
            // Chimney.
            k.Cylinder(a.ConcreteDark_, new Vector3(-2.6f, 2.5f, -2.2f), 0.32f, 1.7f, 8, 0.26f);
            k.Lathe(a.SteelDark_, new[] { new Vector2(0.3f, 4.1f), new Vector2(0.3f, 4.25f) }, 8, new Vector3(-2.6f, 0f, -2.2f));
            ArtLibrary.Attach(p.Model, "Structure", "Refinery_Structure", k);

            var ore = new MeshKit();
            ore.Box(a.OreCrystal, new Vector3(-1.2f, 1.25f, 2.35f), new Vector3(1.8f, 0.06f, 1.15f));
            for (int i = 0; i < 4; i++)
                VehicleModels.Crystal(ore, a.OreCrystal, new Vector3(-1.75f + i * 0.36f, 1.25f, 2.35f + ((i % 2) - 0.5f) * 0.4f), 0.12f, 0.28f,
                    Quaternion.Euler(0f, i * 40f, 15f));
            ArtLibrary.Attach(p.Model, "OreBin", "Refinery_OreBin", ore, shadows: false);

            var glow = new MeshKit();
            glow.Box(a.GlowWarm_, new Vector3(-1.2f, 1.6f, hz1 + 0.02f), new Vector3(3.0f, 0.15f, 0.04f));
            glow.Box(a.GlowWarm_, new Vector3(hx0 - 0.02f, 1.7f, -0.6f), new Vector3(0.04f, 0.15f, 3.0f));
            ArtLibrary.Attach(p.Model, "Windows", "Refinery_Windows", glow, shadows: false);

            var faction = new MeshKit();
            foreach (float z in new[] { -1.55f, 0.85f })
                faction.Lathe(a.Faction, new[] { new Vector2(0.97f, 1.3f), new Vector2(0.97f, 1.75f) }, 12, new Vector3(2.3f, 0.3f, z), capTop: false);
            faction.Box(a.Faction, new Vector3((hx0 + hx1) * 0.5f, hy + 0.03f, hz1 - 0.08f), new Vector3(hx1 - hx0 + 0.04f, 0.1f, 0.18f));
            faction.Box(a.Faction, new Vector3((hx0 + hx1) * 0.5f, hy + 0.03f, hz0 + 0.08f), new Vector3(hx1 - hx0 + 0.04f, 0.1f, 0.18f));
            p.Faction.Add(ArtLibrary.Attach(p.Model, "Livery", "Refinery_Livery", faction));

            p.SmokePoints.Add(VehicleModels.Node(p.Model, "Chimney", new Vector3(-2.6f, 4.3f, -2.2f)));
            return p;
        }

        // ---------------- Power Plant (5 x 5) ----------------

        public static Parts PowerPlant(Transform root, ArtLibrary a)
        {
            var p = Begin(root);
            var k = new MeshKit();
            Plinth(k, a, 5f, 5f);
            // Hyperboloid cooling towers (hollow tops).
            var tower = new[]
            {
                new Vector2(1.05f, 0f), new Vector2(0.92f, 0.8f), new Vector2(0.78f, 1.7f), new Vector2(0.74f, 2.2f),
                new Vector2(0.78f, 2.7f), new Vector2(0.86f, 3.1f), new Vector2(0.72f, 3.1f), new Vector2(0.64f, 2.7f), new Vector2(0.6f, 2.3f),
            };
            foreach (float x in new[] { -1.2f, 1.2f })
            {
                k.Lathe(a.Concrete_, tower, 14, new Vector3(x, 0.3f, -0.55f), capTop: false);
                k.Lathe(a.ConcreteDark_, new[] { new Vector2(0.6f, 2.3f), new Vector2(0f, 2.3f) }, 14, new Vector3(x, 0.3f, -0.55f), capTop: false);
                k.Lathe(a.ConcreteDark_, new[] { new Vector2(1.07f, 0f), new Vector2(1.07f, 0.25f), new Vector2(1.0f, 0.3f) }, 14, new Vector3(x, 0.3f, -0.55f), capTop: false);
            }
            // Generator hall along the front.
            k.Box(a.SteelLight_, new Vector3(0f, 0.95f, 1.55f), new Vector3(4.4f, 1.3f, 1.4f), 0.06f);
            k.Block(a.SteelDark_, new Vector3(0f, 1.6f, 1.55f), new Vector2(4.2f, 1.2f), new Vector2(3.6f, 0.6f), 0.35f);
            // Insulators / pylons on the hall roof.
            foreach (float x in new[] { -1.6f, -0.55f, 0.55f, 1.6f })
            {
                k.Cylinder(a.SteelDark_, new Vector3(x, 1.95f, 1.55f), 0.07f, 0.45f, 6);
                k.Lathe(a.SteelLight_, new[] { new Vector2(0.13f, 0f), new Vector2(0.13f, 0.06f) }, 6, new Vector3(x, 2.15f, 1.55f));
            }
            ArtLibrary.Attach(p.Model, "Structure", "PowerPlant_Structure", k);

            // Energy coils glow in the owner's colour (power readability + faction identity).
            var coils = new MeshKit();
            foreach (float x in new[] { -1.5f, 0f, 1.5f })
                coils.Box(a.FactionGlow, new Vector3(x, 0.95f, 2.26f), new Vector3(0.7f, 0.8f, 0.04f));
            p.FactionGlow.Add(ArtLibrary.Attach(p.Model, "Coils", "PowerPlant_Coils", coils, shadows: false));

            var faction = new MeshKit();
            foreach (float x in new[] { -1.2f, 1.2f })
                faction.Lathe(a.Faction, new[] { new Vector2(0.79f, 2.55f), new Vector2(0.83f, 2.8f) }, 14, new Vector3(x, 0.3f, -0.55f), capTop: false);
            p.Faction.Add(ArtLibrary.Attach(p.Model, "Livery", "PowerPlant_Livery", faction));

            p.SmokePoints.Add(VehicleModels.Node(p.Model, "SteamL", new Vector3(-1.2f, 3.3f, -0.55f)));
            p.SmokePoints.Add(VehicleModels.Node(p.Model, "SteamR", new Vector3(1.2f, 3.3f, -0.55f)));
            return p;
        }

        // ---------------- Vehicle Factory (8 x 7) ----------------

        public static Parts VehicleFactory(Transform root, ArtLibrary a)
        {
            var p = Begin(root);
            var k = new MeshKit();
            Plinth(k, a, 8f, 7f);
            float x0 = -3.8f, x1 = 3.8f, z0 = -3.3f, z1 = 2.9f, wall = 2.2f;
            k.Block(a.Concrete_, new Vector3(0f, 0.3f, (z0 + z1) * 0.5f), new Vector2(x1 - x0, z1 - z0), new Vector2(x1 - x0, z1 - z0), wall - 0.3f);
            // Arched roof: profile in (x, y), extruded along z.
            var arch = new List<Vector2>();
            int seg = 8;
            for (int i = 0; i <= seg; i++)
            {
                float t = Mathf.PI * i / seg;
                arch.Add(new Vector2(Mathf.Cos(t) * (x1 + 0.05f), wall + Mathf.Sin(t) * 1.05f));
            }
            k.Push(new Vector3(0f, 0f, (z0 + z1) * 0.5f), Quaternion.Euler(0f, -90f, 0f));
            k.ExtrudeX(a.SteelLight_, arch.ToArray(), z1 - z0 + 0.1f);
            k.Pop();
            // Ribs over the roof.
            for (int i = 0; i < 5; i++)
            {
                float z = Mathf.Lerp(z0 + 0.2f, z1 - 0.2f, i / 4f);
                var rib = new List<Vector2>();
                for (int s = 0; s <= seg; s++)
                {
                    float t = Mathf.PI * s / seg;
                    rib.Add(new Vector2(Mathf.Cos(t) * (x1 + 0.1f), wall + Mathf.Sin(t) * 1.1f));
                }
                k.Push(new Vector3(0f, 0f, z), Quaternion.Euler(0f, -90f, 0f));
                k.ExtrudeX(a.SteelDark_, rib.ToArray(), 0.12f);
                k.Pop();
            }
            // Big door: dark opening with hazard frame and a header beam.
            k.Box(a.SteelDark_, new Vector3(0f, 1.25f, z1 + 0.02f), new Vector3(4.4f, 1.9f, 0.06f));
            k.Box(a.Hazard, new Vector3(-2.35f, 1.25f, z1 + 0.06f), new Vector3(0.22f, 1.95f, 0.08f));
            k.Box(a.Hazard, new Vector3(2.35f, 1.25f, z1 + 0.06f), new Vector3(0.22f, 1.95f, 0.08f));
            k.Box(a.ConcreteDark_, new Vector3(0f, 2.35f, z1 + 0.12f), new Vector3(5.2f, 0.3f, 0.3f));
            // Rear office annex and roof vents.
            k.Box(a.ConcreteDark_, new Vector3(-2.6f, 1.1f, z0 - 0.0f), new Vector3(2.2f, 1.6f, 0.6f), 0.05f);
            foreach (float x in new[] { -1.6f, 1.6f })
                k.Cylinder(a.SteelDark_, new Vector3(x, wall + 0.85f, -1.8f), 0.32f, 0.35f, 8);
            // Gantry crane over the apron edge.
            foreach (float x in new[] { -3.5f, 3.5f })
                k.Box(a.PaintIndustrial, new Vector3(x, 1.5f, z1 + 0.35f), new Vector3(0.18f, 2.4f, 0.18f));
            k.Box(a.PaintIndustrial, new Vector3(0f, 2.75f, z1 + 0.35f), new Vector3(7.2f, 0.22f, 0.22f));
            ArtLibrary.Attach(p.Model, "Structure", "Factory_Structure", k);

            var glow = new MeshKit();
            glow.Box(a.GlowWarm_, new Vector3(0f, 2.12f, z1 + 0.08f), new Vector3(4.2f, 0.08f, 0.04f));
            glow.Box(a.GlowWarm_, new Vector3(-2.6f, 1.3f, z0 - 0.31f), new Vector3(1.8f, 0.2f, 0.04f));
            ArtLibrary.Attach(p.Model, "Lights", "Factory_Lights", glow, shadows: false);

            var faction = new MeshKit();
            faction.Box(a.Faction, new Vector3(0f, wall + 1.12f, (z0 + z1) * 0.5f), new Vector3(1.4f, 0.06f, z1 - z0 - 0.4f));
            faction.Box(a.Faction, new Vector3(-3.83f, 1.5f, (z0 + z1) * 0.5f), new Vector3(0.04f, 0.5f, z1 - z0 - 1.0f));
            faction.Box(a.Faction, new Vector3(3.83f, 1.5f, (z0 + z1) * 0.5f), new Vector3(0.04f, 0.5f, z1 - z0 - 1.0f));
            p.Faction.Add(ArtLibrary.Attach(p.Model, "Livery", "Factory_Livery", faction));

            p.SmokePoints.Add(VehicleModels.Node(p.Model, "VentL", new Vector3(-1.6f, wall + 1.25f, -1.8f)));
            return p;
        }

        // ---------------- Enemy Outpost (sandbox, 6 x 6) ----------------

        public static Parts Outpost(Transform root, ArtLibrary a)
        {
            var p = Begin(root);
            var k = new MeshKit();
            Plinth(k, a, 6f, 6f, 0.35f);
            k.Block(a.ConcreteDark_, new Vector3(0f, 0.35f, 0f), new Vector2(5.4f, 5.4f), new Vector2(4.4f, 4.4f), 1.3f);
            k.Lathe(a.Concrete_, new[] { new Vector2(1.8f, 0f), new Vector2(1.6f, 0.7f), new Vector2(1.0f, 1.25f), new Vector2(0f, 1.4f) }, 8,
                new Vector3(0f, 1.65f, 0f), startAngle: Mathf.PI / 8f);
            k.Box(a.SteelDark_, new Vector3(0f, 2.05f, 1.55f), new Vector3(1.8f, 0.18f, 0.3f));
            k.Cylinder(a.SteelDark_, new Vector3(1.4f, 2.4f, 1.4f), 0.06f, 1.6f, 6);
            foreach (float s in new[] { -1f, 1f })
                k.Box(a.SteelDark_, new Vector3(s * 2.3f, 0.9f, 2.5f), new Vector3(1.0f, 0.6f, 0.5f), 0.05f); // sandbag walls
            ArtLibrary.Attach(p.Model, "Structure", "Outpost_Structure", k);
            var faction = new MeshKit();
            faction.Lathe(a.Faction, new[] { new Vector2(1.72f, 0.3f), new Vector2(1.66f, 0.55f) }, 8, new Vector3(0f, 1.65f, 0f), capTop: false, startAngle: Mathf.PI / 8f);
            p.Faction.Add(ArtLibrary.Attach(p.Model, "Livery", "Outpost_Livery", faction));
            return p;
        }

        /// <summary>Concrete apron slab with hazard edges in front of a structure (replaces the flat dark pad).</summary>
        public static void Apron(Transform root, ArtLibrary a, Vector2 footprint, float depth)
        {
            var k = new MeshKit();
            float zc = footprint.y * 0.5f + depth * 0.5f;
            k.Box(a.Asphalt, new Vector3(0f, 0.02f, zc), new Vector3(footprint.x - 0.1f, 0.04f, depth - 0.1f));
            k.Box(a.Hazard, new Vector3(-footprint.x * 0.5f + 0.2f, 0.045f, zc), new Vector3(0.22f, 0.012f, depth - 0.3f));
            k.Box(a.Hazard, new Vector3(footprint.x * 0.5f - 0.2f, 0.045f, zc), new Vector3(0.22f, 0.012f, depth - 0.3f));
            k.Box(a.Concrete_, new Vector3(0f, 0.045f, zc + depth * 0.5f - 0.25f), new Vector3(footprint.x - 0.6f, 0.012f, 0.14f));
            var r = ArtLibrary.Attach(root, "ApronPad", $"Apron_{footprint.x:0}x{footprint.y:0}_{depth:0}", k, shadows: false);
            r.receiveShadows = true;
        }
    }
}
