using UnityEngine;

namespace Ironfront.EditorTools.Art
{
    /// <summary>
    /// Procedural visual models for Ironfront's vehicles (Milestone 006). Each builder adds renderer children to an
    /// existing gameplay prefab root and returns the transforms the gameplay/visual components need. Gameplay
    /// components (colliders, NavMeshAgent, Health, CombatUnit...) are untouched; models never have colliders.
    ///
    /// Faction colour: parts that carry the owner's livery are separate single-material renderers using
    /// <see cref="ArtLibrary.Faction"/> and are tinted at runtime by FactionColorizer (one model for every faction).
    /// Coordinates: metres, +z forward, root pivot on the ground.
    /// </summary>
    public static class VehicleModels
    {
        public class TankParts
        {
            public Transform Hull;          // tilted by VehicleMotionFx
            public Transform TurretPivot;   // yawed by CombatUnit
            public Transform Barrel;        // recoils (WeaponFx)
            public Transform Muzzle;
            public Renderer Turret;         // faction-tinted turret shell ("TurretPivot/Turret")
            public Renderer[] FactionRenderers;
            public Renderer[] GlowRenderers;
            public Renderer Tracks;
            public Transform[] ExhaustPoints;
            public Transform[] TrackDustPoints;
        }

        // ---------------- Light Tank ----------------

        /// <summary>Fast, low wedge-hulled tank with a compact faceted turret and a single long gun.</summary>
        public static TankParts LightTank(Transform root, ArtLibrary a)
        {
            var p = new TankParts();
            p.Hull = Node(root, "Hull", Vector3.zero);

            // Tracks (run faces scroll with the tread texture) and road wheels.
            var tracks = new MeshKit { UvScale = 1f };
            var trackProfile = new[] { new Vector2(-0.95f, 0.02f), new Vector2(0.95f, 0.02f), new Vector2(1.12f, 0.24f),
                new Vector2(1.0f, 0.42f), new Vector2(-1.0f, 0.42f), new Vector2(-1.12f, 0.24f) };
            tracks.ExtrudeX(a.Track, trackProfile, 0.32f, -0.63f, a.Rubber);
            tracks.ExtrudeX(a.Track, trackProfile, 0.32f, 0.63f, a.Rubber);
            p.Tracks = ArtLibrary.Attach(p.Hull, "Tracks", "LightTank_Tracks", tracks);

            var hull = new MeshKit();
            foreach (float side in new[] { -1f, 1f })
                foreach (float z in new[] { -0.72f, -0.24f, 0.24f, 0.72f })
                    hull.Wheel(a.SteelDark_, new Vector3(side * 0.8f, 0.2f, z), 0.17f, 0.06f, 8);
            // Lower hull with a sharp glacis.
            hull.ExtrudeX(a.PaintLight, new[] { new Vector2(-1.0f, 0.18f), new Vector2(0.92f, 0.18f), new Vector2(1.14f, 0.40f),
                new Vector2(0.62f, 0.62f), new Vector2(-0.95f, 0.62f), new Vector2(-1.06f, 0.44f) }, 0.98f);
            // Fenders over the tracks, slightly narrower at the nose.
            hull.Block(a.PaintLight, new Vector3(0f, 0.42f, -0.04f), new Vector2(1.58f, 1.98f), new Vector2(1.54f, 1.9f), 0.07f);
            // Engine deck grille, exhausts, headlights, stowage.
            hull.Box(a.SteelDark_, new Vector3(0f, 0.645f, -0.62f), new Vector3(0.72f, 0.05f, 0.46f), 0.015f);
            hull.Tube(a.SteelDark_, new Vector3(-0.3f, 0.5f, -1.08f), 0.045f, 0.1f, 6);
            hull.Tube(a.SteelDark_, new Vector3(0.3f, 0.5f, -1.08f), 0.045f, 0.1f, 6);
            hull.Box(a.SteelDark_, new Vector3(0.55f, 0.54f, -0.75f), new Vector3(0.22f, 0.12f, 0.36f), 0.02f);
            ArtLibrary.Attach(p.Hull, "HullBody", "LightTank_Hull", hull);

            var glow = new MeshKit();
            glow.Box(a.GlowWarm_, new Vector3(-0.42f, 0.47f, 1.02f), new Vector3(0.1f, 0.06f, 0.04f));
            glow.Box(a.GlowWarm_, new Vector3(0.42f, 0.47f, 1.02f), new Vector3(0.1f, 0.06f, 0.04f));
            var hullGlow = ArtLibrary.Attach(p.Hull, "Headlights", "LightTank_Lights", glow, shadows: false);

            // Faction stripe along both fenders.
            var trim = new MeshKit();
            trim.Box(a.Faction, new Vector3(-0.79f, 0.465f, -0.05f), new Vector3(0.04f, 0.06f, 1.7f));
            trim.Box(a.Faction, new Vector3(0.79f, 0.465f, -0.05f), new Vector3(0.04f, 0.06f, 1.7f));
            trim.Box(a.Faction, new Vector3(0f, 0.655f, -0.98f), new Vector3(0.7f, 0.03f, 0.1f));
            var hullTrim = ArtLibrary.Attach(p.Hull, "HullTrim", "LightTank_HullTrim", trim);

            // Turret: faceted, slightly forward, faction coloured.
            p.TurretPivot = Node(root, "TurretPivot", new Vector3(0f, 0.62f, -0.05f));
            var turret = new MeshKit();
            turret.Lathe(a.Faction, new[] { new Vector2(0.47f, 0f), new Vector2(0.5f, 0.1f), new Vector2(0.4f, 0.27f), new Vector2(0f, 0.27f) },
                8, new Vector3(0f, 0f, 0f), startAngle: Mathf.PI / 8f);
            turret.Block(a.Faction, new Vector3(0f, 0.03f, -0.52f), new Vector2(0.56f, 0.3f), new Vector2(0.48f, 0.24f), 0.2f);
            p.Turret = ArtLibrary.Attach(p.TurretPivot, "Turret", "LightTank_Turret", turret);

            var detail = new MeshKit();
            detail.Box(a.SteelDark_, new Vector3(0f, 0.13f, 0.44f), new Vector3(0.3f, 0.18f, 0.16f), 0.03f); // mantlet
            detail.Cylinder(a.SteelLight_, new Vector3(-0.14f, 0.27f, -0.28f), 0.11f, 0.03f, 8);             // hatch
            detail.Cylinder(a.SteelDark_, new Vector3(-0.26f, 0.2f, -0.55f), 0.012f, 0.65f, 4);              // antenna
            detail.Box(a.SteelDark_, new Vector3(0.34f, 0.2f, 0.26f), new Vector3(0.1f, 0.09f, 0.16f), 0.01f); // sight
            ArtLibrary.Attach(p.TurretPivot, "TurretDetail", "LightTank_TurretDetail", detail);
            var optic = new MeshKit();
            optic.Box(a.GlowCool_, new Vector3(0.34f, 0.2f, 0.345f), new Vector3(0.06f, 0.03f, 0.01f));
            var turretGlow = ArtLibrary.Attach(p.TurretPivot, "Optic", "LightTank_Optic", optic, shadows: false);

            p.Barrel = Node(p.TurretPivot, "Barrel", new Vector3(0f, 0.13f, 0.5f));
            var gun = new MeshKit();
            gun.Tube(a.SteelDark_, Vector3.zero, 0.065f, 0.25f, 8, 0.055f);
            gun.Tube(a.SteelDark_, new Vector3(0f, 0f, 0.25f), 0.05f, 0.58f, 8);
            gun.Box(a.SteelDark_, new Vector3(0f, 0f, 0.88f), new Vector3(0.13f, 0.11f, 0.12f), 0.02f); // muzzle brake
            ArtLibrary.Attach(p.Barrel, "Gun", "LightTank_Gun", gun);
            p.Muzzle = Node(p.TurretPivot, "Muzzle", new Vector3(0f, 0.13f, 1.45f));

            p.FactionRenderers = new Renderer[] { p.Turret, hullTrim };
            p.GlowRenderers = new Renderer[] { hullGlow, turretGlow };
            p.ExhaustPoints = new[] { Node(p.Hull, "Exhaust", new Vector3(0f, 0.55f, -1.15f)) };
            p.TrackDustPoints = new[] { Node(p.Hull, "DustL", new Vector3(-0.63f, 0.05f, -1.05f)), Node(p.Hull, "DustR", new Vector3(0.63f, 0.05f, -1.05f)) };
            return p;
        }

        // ---------------- Heavy Tank ----------------

        /// <summary>Broad, slab-armoured tank with skirt plates, a big angular turret and twin guns.</summary>
        public static TankParts HeavyTank(Transform root, ArtLibrary a)
        {
            var p = new TankParts();
            p.Hull = Node(root, "Hull", Vector3.zero);

            var tracks = new MeshKit { UvScale = 1f };
            var trackProfile = new[] { new Vector2(-1.18f, 0.02f), new Vector2(1.18f, 0.02f), new Vector2(1.42f, 0.3f),
                new Vector2(1.28f, 0.56f), new Vector2(-1.28f, 0.56f), new Vector2(-1.42f, 0.3f) };
            tracks.ExtrudeX(a.Track, trackProfile, 0.42f, -0.78f, a.Rubber);
            tracks.ExtrudeX(a.Track, trackProfile, 0.42f, 0.78f, a.Rubber);
            p.Tracks = ArtLibrary.Attach(p.Hull, "Tracks", "HeavyTank_Tracks", tracks);

            var hull = new MeshKit();
            hull.ExtrudeX(a.PaintHeavy, new[] { new Vector2(-1.3f, 0.24f), new Vector2(1.2f, 0.24f), new Vector2(1.44f, 0.5f),
                new Vector2(0.92f, 0.84f), new Vector2(-1.22f, 0.84f), new Vector2(-1.36f, 0.6f) }, 1.16f);
            hull.Block(a.PaintHeavy, new Vector3(0f, 0.56f, -0.06f), new Vector2(2.0f, 2.62f), new Vector2(1.96f, 2.54f), 0.08f);
            // Skirt armour, angled outwards, hiding most of the running gear.
            foreach (float s in new[] { -1f, 1f })
            {
                float xi = s * 0.98f, xo = s * 1.03f;
                hull.Hexahedron(a.PaintHeavy, Corners(new Vector3(Mathf.Min(xi, xo) - 0.0f, 0.2f, -1.32f), new Vector3(Mathf.Max(xi, xo), 0.62f, 1.22f),
                    bottomInset: s * 0.05f));
                foreach (float z in new[] { -0.9f, 0f, 0.9f })
                    hull.Box(a.SteelDark_, new Vector3(s * 1.04f, 0.42f, z), new Vector3(0.03f, 0.08f, 0.08f)); // bolts
            }
            // Glacis applique, engine block, exhausts, tow hooks.
            hull.Block(a.SteelDark_, new Vector3(0f, 0.56f, 1.14f), new Vector2(1.0f, 0.24f), new Vector2(0.9f, 0.06f), 0.22f, new Vector2(0f, -0.06f));
            hull.Box(a.PaintHeavy, new Vector3(0f, 0.93f, -0.86f), new Vector3(1.2f, 0.18f, 0.72f), 0.04f);
            hull.Box(a.SteelDark_, new Vector3(0f, 1.025f, -0.86f), new Vector3(0.9f, 0.02f, 0.5f));
            foreach (float s in new[] { -1f, 1f })
            {
                hull.Tube(a.SteelDark_, new Vector3(s * 0.45f, 0.75f, -1.36f), 0.06f, 0.14f, 6);
                hull.Box(a.SteelDark_, new Vector3(s * 0.5f, 0.3f, 1.36f), new Vector3(0.12f, 0.1f, 0.12f), 0.02f);
            }
            ArtLibrary.Attach(p.Hull, "HullBody", "HeavyTank_Hull", hull);

            var glow = new MeshKit();
            glow.Box(a.GlowWarm_, new Vector3(-0.62f, 0.63f, 1.27f), new Vector3(0.12f, 0.07f, 0.04f));
            glow.Box(a.GlowWarm_, new Vector3(0.62f, 0.63f, 1.27f), new Vector3(0.12f, 0.07f, 0.04f));
            var hullGlow = ArtLibrary.Attach(p.Hull, "Headlights", "HeavyTank_Lights", glow, shadows: false);

            var trim = new MeshKit();
            trim.Box(a.Faction, new Vector3(-1.045f, 0.56f, -0.05f), new Vector3(0.03f, 0.08f, 2.2f));
            trim.Box(a.Faction, new Vector3(1.045f, 0.56f, -0.05f), new Vector3(0.03f, 0.08f, 2.2f));
            var hullTrim = ArtLibrary.Attach(p.Hull, "HullTrim", "HeavyTank_HullTrim", trim);

            p.TurretPivot = Node(root, "TurretPivot", new Vector3(0f, 0.84f, -0.12f));
            var turret = new MeshKit();
            turret.Hexahedron(a.Faction, new[]
            {
                new Vector3(-0.76f, 0f, -0.7f), new Vector3(-0.8f, 0f, 0.55f), new Vector3(0.8f, 0f, 0.55f), new Vector3(0.76f, 0f, -0.7f),
                new Vector3(-0.62f, 0.44f, -0.7f), new Vector3(-0.56f, 0.44f, 0.28f), new Vector3(0.56f, 0.44f, 0.28f), new Vector3(0.62f, 0.44f, -0.7f),
            });
            turret.Block(a.Faction, new Vector3(0f, 0.04f, -0.95f), new Vector2(1.2f, 0.5f), new Vector2(1.1f, 0.44f), 0.34f); // bustle
            p.Turret = ArtLibrary.Attach(p.TurretPivot, "Turret", "HeavyTank_Turret", turret);

            var detail = new MeshKit();
            detail.Box(a.SteelDark_, new Vector3(0f, 0.22f, 0.6f), new Vector3(0.66f, 0.3f, 0.18f), 0.04f);  // mantlet
            detail.Cylinder(a.SteelLight_, new Vector3(-0.3f, 0.44f, -0.5f), 0.15f, 0.04f, 8);              // hatch
            detail.Box(a.SteelDark_, new Vector3(0.55f, 0.36f, 0.2f), new Vector3(0.16f, 0.12f, 0.22f), 0.02f); // sight
            foreach (float s in new[] { -1f, 1f })                                                             // smoke launchers
                for (int i = 0; i < 3; i++)
                    detail.Tube(a.SteelDark_, new Vector3(s * (0.64f + i * 0.04f), 0.28f + i * 0.04f, 0.18f), 0.04f, 0.12f, 6);
            detail.Cylinder(a.SteelDark_, new Vector3(0.4f, 0.44f, -1.0f), 0.014f, 0.8f, 4);                 // antenna
            ArtLibrary.Attach(p.TurretPivot, "TurretDetail", "HeavyTank_TurretDetail", detail);
            var optic = new MeshKit();
            optic.Box(a.GlowCool_, new Vector3(0.55f, 0.36f, 0.315f), new Vector3(0.09f, 0.04f, 0.01f));
            var turretGlow = ArtLibrary.Attach(p.TurretPivot, "Optic", "HeavyTank_Optic", optic, shadows: false);

            p.Barrel = Node(p.TurretPivot, "Barrel", new Vector3(0f, 0.22f, 0.66f));
            var gun = new MeshKit();
            foreach (float s in new[] { -1f, 1f })
            {
                gun.Tube(a.SteelDark_, new Vector3(s * 0.17f, 0f, 0f), 0.085f, 0.3f, 8, 0.07f);
                gun.Tube(a.SteelDark_, new Vector3(s * 0.17f, 0f, 0.3f), 0.065f, 0.72f, 8);
                gun.Box(a.SteelDark_, new Vector3(s * 0.17f, 0f, 1.08f), new Vector3(0.17f, 0.15f, 0.16f), 0.025f);
            }
            ArtLibrary.Attach(p.Barrel, "Guns", "HeavyTank_Guns", gun);
            p.Muzzle = Node(p.TurretPivot, "Muzzle", new Vector3(0f, 0.22f, 1.84f));

            p.FactionRenderers = new Renderer[] { p.Turret, hullTrim };
            p.GlowRenderers = new Renderer[] { hullGlow, turretGlow };
            p.ExhaustPoints = new[] { Node(p.Hull, "ExhaustL", new Vector3(-0.45f, 0.78f, -1.46f)), Node(p.Hull, "ExhaustR", new Vector3(0.45f, 0.78f, -1.46f)) };
            p.TrackDustPoints = new[] { Node(p.Hull, "DustL", new Vector3(-0.78f, 0.05f, -1.3f)), Node(p.Hull, "DustR", new Vector3(0.78f, 0.05f, -1.3f)) };
            return p;
        }

        // ---------------- Harvester ----------------

        public class HarvesterParts
        {
            public Transform Body;
            public Transform Cargo;       // scaled in y by Harvester (pivot on the bin floor)
            public Transform Drum;        // spins while harvesting
            public Transform Beacon;      // flashes while working
            public Transform ScoopPoint;  // harvest particles
            public Renderer[] FactionRenderers;
            public Transform[] DustPoints;
        }

        /// <summary>Six-wheeled ore truck: front cutter drum, cab, open ore hopper with a visible load.</summary>
        public static HarvesterParts Harvester(Transform root, ArtLibrary a)
        {
            var p = new HarvesterParts();
            p.Body = Node(root, "Body", Vector3.zero);

            var body = new MeshKit();
            foreach (float s in new[] { -1f, 1f })
                foreach (float z in new[] { 0.78f, -0.05f, -0.88f })
                    body.Wheel(a.Rubber, new Vector3(s * 0.66f, 0.31f, z), 0.31f, 0.24f, 10, a.SteelDark_);
            body.Box(a.SteelDark_, new Vector3(0f, 0.42f, -0.05f), new Vector3(1.0f, 0.24f, 2.45f), 0.03f);  // frame
            body.Block(a.PaintIndustrial, new Vector3(0f, 0.54f, -0.08f), new Vector2(1.56f, 2.42f), new Vector2(1.56f, 2.42f), 0.1f); // deck
            // Cab with raked windscreen.
            body.Hexahedron(a.PaintIndustrial, new[]
            {
                new Vector3(-0.66f, 0.64f, 0.62f), new Vector3(-0.66f, 0.64f, 1.2f), new Vector3(0.0f, 0.64f, 1.2f), new Vector3(0.0f, 0.64f, 0.62f),
                new Vector3(-0.66f, 1.28f, 0.62f), new Vector3(-0.62f, 1.28f, 0.98f), new Vector3(-0.04f, 1.28f, 0.98f), new Vector3(0.0f, 1.28f, 0.62f),
            });
            body.Box(a.SteelDark_, new Vector3(0.36f, 0.86f, 0.9f), new Vector3(0.56f, 0.44f, 0.56f), 0.04f); // engine housing
            body.Tube(a.SteelDark_, new Vector3(0.55f, 1.08f, 0.75f), 0.05f, 0.05f, 6);
            body.Cylinder(a.SteelDark_, new Vector3(0.55f, 1.08f, 0.75f), 0.05f, 0.42f, 6);                 // exhaust stack
            // Ore hopper: open-topped bin.
            float bz0 = -1.28f, bz1 = 0.5f, by0 = 0.64f, by1 = 1.3f;
            body.Box(a.SteelDark_, new Vector3(0f, by0 + 0.02f, (bz0 + bz1) * 0.5f), new Vector3(1.4f, 0.04f, bz1 - bz0));
            body.Block(a.PaintIndustrial, new Vector3(-0.68f, by0, (bz0 + bz1) * 0.5f), new Vector2(0.08f, bz1 - bz0), new Vector2(0.06f, bz1 - bz0), by1 - by0, new Vector2(-0.04f, 0f));
            body.Block(a.PaintIndustrial, new Vector3(0.68f, by0, (bz0 + bz1) * 0.5f), new Vector2(0.08f, bz1 - bz0), new Vector2(0.06f, bz1 - bz0), by1 - by0, new Vector2(0.04f, 0f));
            body.Block(a.PaintIndustrial, new Vector3(0f, by0, bz0 + 0.04f), new Vector2(1.44f, 0.08f), new Vector2(1.52f, 0.06f), by1 - by0, new Vector2(0f, -0.04f));
            body.Block(a.PaintIndustrial, new Vector3(0f, by0, bz1 - 0.04f), new Vector2(1.44f, 0.08f), new Vector2(1.44f, 0.06f), by1 - by0 - 0.1f);
            body.Box(a.Hazard, new Vector3(0f, by1 - 0.06f, bz0 + 0.0f), new Vector3(1.5f, 0.1f, 0.02f));
            // Drum arms.
            foreach (float s in new[] { -1f, 1f })
                body.Box(a.SteelDark_, new Vector3(s * 0.62f, 0.4f, 1.22f), new Vector3(0.08f, 0.1f, 0.42f), 0.02f);
            ArtLibrary.Attach(p.Body, "Chassis", "Harvester_Chassis", body);

            var glass = new MeshKit();
            glass.Polygon(a.Glass, new Vector3(-0.6f, 0.86f, 1.165f), new Vector3(-0.06f, 0.86f, 1.165f), new Vector3(-0.06f, 1.2f, 1.01f), new Vector3(-0.6f, 1.2f, 1.01f));
            glass.Polygon(a.Glass, new Vector3(-0.665f, 0.9f, 0.7f), new Vector3(-0.665f, 0.9f, 1.1f), new Vector3(-0.64f, 1.2f, 0.96f), new Vector3(-0.64f, 1.2f, 0.7f));
            ArtLibrary.Attach(p.Body, "Windows", "Harvester_Windows", glass, shadows: false);

            var trim = new MeshKit();
            trim.Box(a.Faction, new Vector3(-0.33f, 1.31f, 0.8f), new Vector3(0.62f, 0.05f, 0.4f), 0.01f);   // cab roof
            trim.Box(a.Faction, new Vector3(-0.765f, 0.59f, -0.08f), new Vector3(0.03f, 0.08f, 2.3f));
            trim.Box(a.Faction, new Vector3(0.765f, 0.59f, -0.08f), new Vector3(0.03f, 0.08f, 2.3f));
            var factionTrim = ArtLibrary.Attach(p.Body, "FactionTrim", "Harvester_Trim", trim);

            // Cutter drum (spins about x).
            p.Drum = Node(p.Body, "Drum", new Vector3(0f, 0.3f, 1.34f));
            var drum = new MeshKit();
            drum.Wheel(a.SteelDark_, Vector3.zero, 0.2f, 1.14f, 8, a.SteelLight_);
            for (int i = 0; i < 8; i++)
            {
                drum.Push(Vector3.zero, Quaternion.Euler(i * 45f, 0f, 0f));
                for (int k = -2; k <= 2; k++)
                    drum.Box(a.SteelLight_, new Vector3(k * 0.24f + (i % 2) * 0.12f, 0.22f, 0f), new Vector3(0.06f, 0.07f, 0.06f));
                drum.Pop();
            }
            ArtLibrary.Attach(p.Drum, "Cutter", "Harvester_Drum", drum);

            // Cargo load: crystal heap with its pivot on the bin floor so scaling y fills the hopper upwards.
            p.Cargo = Node(p.Body, "Cargo", new Vector3(0f, by0 + 0.04f, (bz0 + bz1) * 0.5f));
            var cargo = new MeshKit();
            cargo.Block(a.OreRock, Vector3.zero, new Vector2(1.3f, 1.66f), new Vector2(0.9f, 1.2f), 0.42f);
            var rng = new System.Random(77);
            for (int i = 0; i < 7; i++)
            {
                var pos = new Vector3((float)(rng.NextDouble() - 0.5) * 0.9f, 0.3f, (float)(rng.NextDouble() - 0.5) * 1.2f);
                Crystal(cargo, a.OreCrystal, pos, 0.12f + (float)rng.NextDouble() * 0.06f, 0.35f + (float)rng.NextDouble() * 0.2f,
                    Quaternion.Euler((float)rng.NextDouble() * 40f - 20f, (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 40f - 20f));
            }
            ArtLibrary.Attach(p.Cargo, "Ore", "Harvester_Cargo", cargo);

            p.Beacon = Node(p.Body, "Beacon", new Vector3(-0.33f, 1.34f, 0.66f));
            var beacon = new MeshKit();
            beacon.Cylinder(a.GlowWarm_, Vector3.zero, 0.06f, 0.1f, 6);
            ArtLibrary.Attach(p.Beacon, "Lamp", "Harvester_Beacon", beacon, shadows: false);

            p.ScoopPoint = Node(p.Body, "ScoopPoint", new Vector3(0f, 0.25f, 1.6f));
            p.FactionRenderers = new Renderer[] { factionTrim };
            p.DustPoints = new[] { Node(p.Body, "DustL", new Vector3(-0.66f, 0.05f, -1.15f)), Node(p.Body, "DustR", new Vector3(0.66f, 0.05f, -1.15f)) };
            return p;
        }

        // ---------------- Shared helpers ----------------

        /// <summary>Hexagonal crystal: prism with a pointed tip, rotated by <paramref name="rotation"/>.</summary>
        public static void Crystal(MeshKit kit, Material m, Vector3 basePos, float radius, float height, Quaternion rotation)
        {
            kit.Push(basePos, rotation);
            kit.Lathe(m, new[] { new Vector2(radius * 0.8f, -height * 0.1f), new Vector2(radius, height * 0.6f), new Vector2(0f, height) },
                6, Vector3.zero, capTop: false);
            kit.Pop();
        }

        internal static Transform Node(Transform parent, string name, Vector3 localPosition)
        {
            var go = new GameObject(name);
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            return go.transform;
        }

        /// <summary>Corners of an axis-aligned box from min to max; the bottom edge is pulled in by bottomInset (x).</summary>
        private static Vector3[] Corners(Vector3 min, Vector3 max, float bottomInset)
        {
            float bx0 = min.x - Mathf.Min(0f, bottomInset), bx1 = max.x - Mathf.Max(0f, bottomInset);
            return new[]
            {
                new Vector3(bx0, min.y, min.z), new Vector3(bx0, min.y, max.z), new Vector3(bx1, min.y, max.z), new Vector3(bx1, min.y, min.z),
                new Vector3(min.x, max.y, min.z), new Vector3(min.x, max.y, max.z), new Vector3(max.x, max.y, max.z), new Vector3(max.x, max.y, min.z),
            };
        }
    }
}
