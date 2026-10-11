using UnityEngine;

namespace Ironfront.EditorTools.Art
{
    /// <summary>
    /// Milestone 007 models in the Milestone 006 style (faceted hard surface, livery parts in team colour):
    /// Rocket Artillery, Recon Vehicle, Guard Tower, Anti-Armour Turret and Research Centre.
    /// </summary>
    public static class ExpansionModels
    {
        // ---------------- Rocket Artillery ----------------

        /// <summary>Six-wheeled truck carrying a tilted rocket pod on a turntable (long, boxy silhouette).</summary>
        public static VehicleModels.TankParts RocketArtillery(Transform root, ArtLibrary a)
        {
            var p = new VehicleModels.TankParts();
            p.Hull = VehicleModels.Node(root, "Hull", Vector3.zero);
            var body = new MeshKit();
            foreach (float s in new[] { -1f, 1f })
                foreach (float z in new[] { 1.0f, 0.05f, -0.9f })
                    body.Wheel(a.Rubber, new Vector3(s * 0.78f, 0.34f, z), 0.34f, 0.26f, 10, a.SteelDark_);
            body.Box(a.SteelDark_, new Vector3(0f, 0.45f, 0f), new Vector3(1.1f, 0.26f, 2.9f), 0.03f);              // frame
            body.Block(a.PaintHeavy, new Vector3(0f, 0.58f, -0.1f), new Vector2(1.72f, 2.7f), new Vector2(1.66f, 2.6f), 0.14f); // deck
            // Armoured cab with a sloped windscreen.
            body.Hexahedron(a.PaintHeavy, new[]
            {
                new Vector3(-0.8f, 0.72f, 0.75f), new Vector3(-0.8f, 0.72f, 1.42f), new Vector3(0.8f, 0.72f, 1.42f), new Vector3(0.8f, 0.72f, 0.75f),
                new Vector3(-0.74f, 1.38f, 0.75f), new Vector3(-0.7f, 1.3f, 1.08f), new Vector3(0.7f, 1.3f, 1.08f), new Vector3(0.74f, 1.38f, 0.75f),
            });
            body.Box(a.SteelDark_, new Vector3(0f, 0.84f, 0.25f), new Vector3(1.4f, 0.24f, 0.5f), 0.03f); // stabiliser beam
            foreach (float s in new[] { -1f, 1f })
                body.Box(a.SteelDark_, new Vector3(s * 0.86f, 0.5f, -1.25f), new Vector3(0.12f, 0.5f, 0.12f)); // outriggers
            ArtLibrary.Attach(p.Hull, "HullBody", "Artillery_Hull", body);
            var glass = new MeshKit();
            glass.Polygon(a.Glass, new Vector3(-0.66f, 0.92f, 1.3f), new Vector3(0.66f, 0.92f, 1.3f), new Vector3(0.62f, 1.24f, 1.12f), new Vector3(-0.62f, 1.24f, 1.12f));
            ArtLibrary.Attach(p.Hull, "Windows", "Artillery_Windows", glass, shadows: false);
            var trim = new MeshKit();
            trim.Box(a.Faction, new Vector3(0f, 1.36f, 0.95f), new Vector3(1.3f, 0.05f, 0.34f), 0.01f);
            trim.Box(a.Faction, new Vector3(-0.855f, 0.66f, -0.1f), new Vector3(0.03f, 0.08f, 2.4f));
            trim.Box(a.Faction, new Vector3(0.855f, 0.66f, -0.1f), new Vector3(0.03f, 0.08f, 2.4f));
            var hullTrim = ArtLibrary.Attach(p.Hull, "HullTrim", "Artillery_Trim", trim);

            // Launcher: turntable plus a pod of rocket tubes elevated towards the front.
            p.TurretPivot = VehicleModels.Node(root, "TurretPivot", new Vector3(0f, 0.72f, -0.55f));
            var mount = new MeshKit();
            mount.Cylinder(a.SteelDark_, Vector3.zero, 0.55f, 0.18f, 10);
            mount.Box(a.SteelDark_, new Vector3(0f, 0.36f, 0.05f), new Vector3(0.3f, 0.36f, 0.5f), 0.03f);
            ArtLibrary.Attach(p.TurretPivot, "TurretDetail", "Artillery_Mount", mount);
            p.Barrel = VehicleModels.Node(p.TurretPivot, "Barrel", new Vector3(0f, 0.55f, 0f));
            p.Barrel.localRotation = Quaternion.Euler(-28f, 0f, 0f);
            var pod = new MeshKit();
            pod.Box(a.Faction, new Vector3(0f, 0.18f, 0.25f), new Vector3(1.3f, 0.52f, 1.75f), 0.04f);
            p.Turret = ArtLibrary.Attach(p.Barrel, "Turret", "Artillery_Pod", pod);
            var tubes = new MeshKit();
            for (int row = 0; row < 2; row++)
                for (int col = 0; col < 4; col++)
                    tubes.Box(a.SteelDark_, new Vector3(-0.45f + col * 0.3f, 0.05f + row * 0.26f, 1.13f), new Vector3(0.22f, 0.2f, 0.04f));
            ArtLibrary.Attach(p.Barrel, "Tubes", "Artillery_Tubes", tubes, shadows: false);
            p.Muzzle = VehicleModels.Node(p.Barrel, "Muzzle", new Vector3(0f, 0.18f, 1.25f));

            p.FactionRenderers = new Renderer[] { p.Turret, hullTrim };
            p.GlowRenderers = new Renderer[0];
            p.ExhaustPoints = new[] { VehicleModels.Node(p.Hull, "Exhaust", new Vector3(0.6f, 1.2f, 0.7f)) };
            p.TrackDustPoints = new[] { VehicleModels.Node(p.Hull, "DustL", new Vector3(-0.78f, 0.05f, -1.2f)), VehicleModels.Node(p.Hull, "DustR", new Vector3(0.78f, 0.05f, -1.2f)) };
            return p;
        }

        // ---------------- Recon Vehicle ----------------

        /// <summary>Small four-wheeled buggy: roll cage, light gun and a tall sensor mast (reads as "eyes").</summary>
        public static VehicleModels.TankParts Recon(Transform root, ArtLibrary a)
        {
            var p = new VehicleModels.TankParts();
            p.Hull = VehicleModels.Node(root, "Hull", Vector3.zero);
            var body = new MeshKit();
            foreach (float s in new[] { -1f, 1f })
                foreach (float z in new[] { 0.62f, -0.62f })
                    body.Wheel(a.Rubber, new Vector3(s * 0.6f, 0.27f, z), 0.27f, 0.22f, 10, a.SteelDark_);
            body.ExtrudeX(a.PaintLight, new[] { new Vector2(-0.95f, 0.25f), new Vector2(0.85f, 0.25f), new Vector2(1.0f, 0.4f),
                new Vector2(0.55f, 0.6f), new Vector2(-0.85f, 0.6f), new Vector2(-0.98f, 0.45f) }, 0.96f);
            foreach (float s in new[] { -1f, 1f })
            {
                body.Box(a.SteelDark_, new Vector3(s * 0.42f, 0.9f, -0.15f), new Vector3(0.06f, 0.6f, 0.06f));
                body.Box(a.SteelDark_, new Vector3(s * 0.42f, 0.9f, -0.75f), new Vector3(0.06f, 0.6f, 0.06f));
            }
            body.Box(a.SteelDark_, new Vector3(0f, 1.2f, -0.45f), new Vector3(0.9f, 0.06f, 0.66f));
            // Sensor mast with a radar disc.
            body.Cylinder(a.SteelDark_, new Vector3(0.3f, 1.2f, -0.75f), 0.04f, 0.75f, 6);
            body.Lathe(a.SteelLight_, new[] { new Vector2(0.28f, 0f), new Vector2(0.28f, 0.05f) }, 10, new Vector3(0.3f, 1.95f, -0.75f));
            ArtLibrary.Attach(p.Hull, "HullBody", "Recon_Hull", body);
            var trim = new MeshKit();
            trim.Box(a.Faction, new Vector3(0f, 0.605f, 0.2f), new Vector3(0.9f, 0.02f, 0.6f));
            var hullTrim = ArtLibrary.Attach(p.Hull, "HullTrim", "Recon_Trim", trim);
            var glow = new MeshKit();
            glow.Box(a.GlowCool_, new Vector3(0.3f, 2.02f, -0.75f), new Vector3(0.1f, 0.06f, 0.1f));
            glow.Box(a.GlowWarm_, new Vector3(-0.3f, 0.45f, 0.98f), new Vector3(0.12f, 0.06f, 0.03f));
            glow.Box(a.GlowWarm_, new Vector3(0.3f, 0.45f, 0.98f), new Vector3(0.12f, 0.06f, 0.03f));
            var lights = ArtLibrary.Attach(p.Hull, "Lights", "Recon_Lights", glow, shadows: false);

            p.TurretPivot = VehicleModels.Node(root, "TurretPivot", new Vector3(0f, 1.23f, -0.35f));
            var turret = new MeshKit();
            turret.Lathe(a.Faction, new[] { new Vector2(0.22f, 0f), new Vector2(0.2f, 0.16f), new Vector2(0f, 0.18f) }, 8, Vector3.zero);
            p.Turret = ArtLibrary.Attach(p.TurretPivot, "Turret", "Recon_Turret", turret);
            p.Barrel = VehicleModels.Node(p.TurretPivot, "Barrel", new Vector3(0f, 0.09f, 0.15f));
            var gun = new MeshKit();
            gun.Tube(a.SteelDark_, Vector3.zero, 0.035f, 0.55f, 6);
            ArtLibrary.Attach(p.Barrel, "Gun", "Recon_Gun", gun);
            p.Muzzle = VehicleModels.Node(p.TurretPivot, "Muzzle", new Vector3(0f, 0.09f, 0.72f));

            p.FactionRenderers = new Renderer[] { p.Turret, hullTrim };
            p.GlowRenderers = new Renderer[] { lights };
            p.ExhaustPoints = new Transform[0];
            p.TrackDustPoints = new[] { VehicleModels.Node(p.Hull, "DustL", new Vector3(-0.6f, 0.05f, -0.85f)), VehicleModels.Node(p.Hull, "DustR", new Vector3(0.6f, 0.05f, -0.85f)) };
            return p;
        }

        // ---------------- Defences ----------------

        public class TurretParts
        {
            public StructureModels.Parts Structure;
            public Transform Head;
            public Transform Muzzle;
            public Transform Barrel;
        }

        /// <summary>Guard Tower (3 x 3): concrete tower with a rotating twin machine-gun head and a searchlight.</summary>
        public static TurretParts GuardTower(Transform root, ArtLibrary a)
        {
            var t = new TurretParts { Structure = StructureModels.Begin(root) };
            var model = t.Structure.Model;
            var k = new MeshKit();
            k.Block(a.ConcreteDark_, Vector3.zero, new Vector2(3f, 3f), new Vector2(2.7f, 2.7f), 0.3f);
            k.Block(a.Concrete_, new Vector3(0f, 0.3f, 0f), new Vector2(1.9f, 1.9f), new Vector2(1.4f, 1.4f), 2.6f);
            k.Box(a.ConcreteDark_, new Vector3(0f, 3.0f, 0f), new Vector3(1.9f, 0.25f, 1.9f), 0.05f);
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                    k.Box(a.SteelDark_, new Vector3(sx * 0.85f, 3.32f, sz * 0.85f), new Vector3(0.1f, 0.4f, 0.1f));
            ArtLibrary.Attach(model, "Structure", "GuardTower_Structure", k);
            var band = new MeshKit();
            band.Box(a.Faction, new Vector3(0f, 2.75f, 0f), new Vector3(1.52f, 0.18f, 1.52f));
            t.Structure.Faction.Add(ArtLibrary.Attach(model, "Livery", "GuardTower_Livery", band));

            t.Head = VehicleModels.Node(model, "TurretPivot", new Vector3(0f, 3.15f, 0f));
            var head = new MeshKit();
            head.Box(a.Faction, new Vector3(0f, 0.25f, -0.05f), new Vector3(0.8f, 0.5f, 0.8f), 0.06f);
            t.Structure.Faction.Add(ArtLibrary.Attach(t.Head, "Turret", "GuardTower_Head", head));
            t.Barrel = VehicleModels.Node(t.Head, "Barrel", new Vector3(0f, 0.28f, 0.35f));
            var guns = new MeshKit();
            foreach (float s in new[] { -1f, 1f }) guns.Tube(a.SteelDark_, new Vector3(s * 0.16f, 0f, 0f), 0.045f, 0.75f, 6);
            ArtLibrary.Attach(t.Barrel, "Guns", "GuardTower_Guns", guns);
            var light = new MeshKit();
            light.Box(a.GlowWarm_, new Vector3(0.33f, 0.52f, 0.36f), new Vector3(0.14f, 0.1f, 0.04f));
            ArtLibrary.Attach(t.Head, "Searchlight", "GuardTower_Light", light, shadows: false);
            t.Muzzle = VehicleModels.Node(t.Head, "Muzzle", new Vector3(0f, 0.28f, 1.15f));
            return t;
        }

        /// <summary>Anti-Armour Turret (3 x 3): armoured bunker with a heavy long-barrelled gun and a power coil.</summary>
        public static TurretParts AntiArmourTurret(Transform root, ArtLibrary a)
        {
            var t = new TurretParts { Structure = StructureModels.Begin(root) };
            var model = t.Structure.Model;
            var k = new MeshKit();
            k.Block(a.ConcreteDark_, Vector3.zero, new Vector2(3f, 3f), new Vector2(2.6f, 2.6f), 0.35f);
            k.Block(a.Concrete_, new Vector3(0f, 0.35f, 0f), new Vector2(2.6f, 2.6f), new Vector2(1.9f, 1.9f), 0.9f);
            k.Box(a.SteelDark_, new Vector3(-1.05f, 0.55f, -1.05f), new Vector3(0.45f, 0.4f, 0.45f), 0.04f); // power box
            ArtLibrary.Attach(model, "Structure", "AtTurret_Structure", k);
            var coil = new MeshKit();
            coil.Box(a.FactionGlow, new Vector3(-1.05f, 0.8f, -0.82f), new Vector3(0.3f, 0.16f, 0.02f));
            t.Structure.FactionGlow.Add(ArtLibrary.Attach(model, "Coil", "AtTurret_Coil", coil, shadows: false));

            t.Head = VehicleModels.Node(model, "TurretPivot", new Vector3(0f, 1.25f, 0f));
            var head = new MeshKit();
            head.Hexahedron(a.Faction, new[]
            {
                new Vector3(-0.7f, 0f, -0.75f), new Vector3(-0.72f, 0f, 0.6f), new Vector3(0.72f, 0f, 0.6f), new Vector3(0.7f, 0f, -0.75f),
                new Vector3(-0.55f, 0.55f, -0.75f), new Vector3(-0.5f, 0.55f, 0.3f), new Vector3(0.5f, 0.55f, 0.3f), new Vector3(0.55f, 0.55f, -0.75f),
            });
            t.Structure.Faction.Add(ArtLibrary.Attach(t.Head, "Turret", "AtTurret_Head", head));
            t.Barrel = VehicleModels.Node(t.Head, "Barrel", new Vector3(0f, 0.28f, 0.6f));
            var gun = new MeshKit();
            gun.Tube(a.SteelDark_, Vector3.zero, 0.11f, 0.35f, 8, 0.09f);
            gun.Tube(a.SteelDark_, new Vector3(0f, 0f, 0.35f), 0.075f, 1.15f, 8);
            gun.Box(a.SteelDark_, new Vector3(0f, 0f, 1.55f), new Vector3(0.2f, 0.18f, 0.2f), 0.03f);
            ArtLibrary.Attach(t.Barrel, "Gun", "AtTurret_Gun", gun);
            t.Muzzle = VehicleModels.Node(t.Head, "Muzzle", new Vector3(0f, 0.28f, 2.2f));
            return t;
        }

        // ---------------- Research Centre ----------------

        /// <summary>Research Centre (5 x 5): laboratory block with a glazed dome and an antenna array.</summary>
        public static StructureModels.Parts ResearchCentre(Transform root, ArtLibrary a)
        {
            var p = StructureModels.Begin(root);
            var k = new MeshKit();
            k.Block(a.ConcreteDark_, Vector3.zero, new Vector2(5f, 5f), new Vector2(4.75f, 4.75f), 0.3f);
            k.Box(a.Concrete_, new Vector3(-0.3f, 1.3f, 0f), new Vector3(4.1f, 2.0f, 4.4f), 0.08f);
            k.Lathe(a.SteelLight_, new[] { new Vector2(1.3f, 0f), new Vector2(1.3f, 0.2f) }, 14, new Vector3(0.4f, 2.3f, -0.3f));
            foreach (float z in new[] { -1.6f, -0.6f, 0.4f })
            {
                k.Cylinder(a.SteelDark_, new Vector3(1.9f, 2.3f, z), 0.04f, 1.3f, 4);
                k.Box(a.SteelDark_, new Vector3(1.9f, 3.45f, z), new Vector3(0.5f, 0.04f, 0.04f));
            }
            k.Box(a.SteelDark_, new Vector3(-1.4f, 1.0f, 2.21f), new Vector3(1.2f, 1.4f, 0.04f)); // door
            ArtLibrary.Attach(p.Model, "Structure", "Research_Structure", k);
            var dome = new MeshKit();
            dome.Lathe(a.Glass, new[] { new Vector2(1.25f, 0f), new Vector2(1.1f, 0.55f), new Vector2(0.65f, 0.95f), new Vector2(0f, 1.1f) },
                14, new Vector3(0.4f, 2.5f, -0.3f), capTop: false);
            ArtLibrary.Attach(p.Model, "Dome", "Research_Dome", dome);
            var glow = new MeshKit();
            glow.Box(a.GlowCool_, new Vector3(-0.3f, 1.5f, 2.21f), new Vector3(1.6f, 0.2f, 0.04f));
            glow.Box(a.GlowCool_, new Vector3(-2.36f, 1.5f, 0f), new Vector3(0.04f, 0.2f, 3.2f));
            glow.Lathe(a.GlowCool_, new[] { new Vector2(0.2f, 0f), new Vector2(0.2f, 0.3f) }, 8, new Vector3(0.4f, 2.5f, -0.3f));
            ArtLibrary.Attach(p.Model, "Lights", "Research_Lights", glow, shadows: false);
            var faction = new MeshKit();
            faction.Box(a.Faction, new Vector3(-0.3f, 2.33f, 1.9f), new Vector3(4.12f, 0.08f, 0.5f));
            faction.Box(a.Faction, new Vector3(-0.3f, 2.33f, -1.9f), new Vector3(4.12f, 0.08f, 0.5f));
            p.Faction.Add(ArtLibrary.Attach(p.Model, "Livery", "Research_Livery", faction));
            var beacon = VehicleModels.Node(p.Model, "Beacon", new Vector3(1.9f, 3.7f, -0.6f));
            var lamp = new MeshKit();
            lamp.Box(a.FactionGlow, Vector3.zero, new Vector3(0.12f, 0.12f, 0.12f));
            p.FactionGlow.Add(ArtLibrary.Attach(beacon, "Lamp", "Research_Beacon", lamp, shadows: false));
            p.Blinkers.Add(beacon);
            return p;
        }
    }
}
