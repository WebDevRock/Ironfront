using System;
using System.Collections.Generic;
using System.IO;
using Ironfront.Buildings;
using Ironfront.Combat;
using Ironfront.Data;
using Ironfront.Economy;
using Ironfront.EditorTools.Art;
using Ironfront.Visuals;
using Ironfront.Match;
using Ironfront.Units;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace Ironfront.EditorTools
{
    /// <summary>
    /// Milestone 002 content: data definitions, structure/unit/deposit prefabs and the starting base.
    /// Milestone 003 adds health, health bars and faction tinting to every unit and structure prefab and
    /// delegates combat content (tanks, weapons, enemy scenario) to <see cref="CombatContentBuilder"/>.
    /// Called by <see cref="RtsSandboxBuilder"/>; all values that matter for balancing live in the generated
    /// ScriptableObject assets under Assets/Ironfront/Data and can be edited there afterwards.
    /// </summary>
    public static class EconomyContentBuilder
    {
        private const string BuildingsDataDir = RtsSandboxBuilder.DataDir + "/Buildings";
        private const string UnitsDataDir = RtsSandboxBuilder.DataDir + "/Units";
        private const string BuildingPrefabsDir = RtsSandboxBuilder.PrefabsDir + "/Buildings";
        private const string UnitPrefabsDir = RtsSandboxBuilder.PrefabsDir + "/Units";

        public const int StartingCredits = 2000;

        // Starting layout (player base in the south-west corner).
        public static readonly Vector3 HeadquartersPosition = new Vector3(-41f, 0f, -41f);
        public static readonly Vector3 RefineryPosition = new Vector3(-28f, 0f, -40f);
        public static readonly Vector3 DepositPosition = new Vector3(-12f, 0f, -36f);
        public static readonly Vector3 HarvesterPosition = new Vector3(-27f, 0f, -31f);

        public static string Summary { get; private set; } = "";

        internal static ArtLibrary Art => s_art ?? (s_art = ArtLibrary.Create());

        private static readonly string HeadquartersPath = $"{BuildingsDataDir}/Headquarters.asset";
        private static readonly string RefineryPath = $"{BuildingsDataDir}/Refinery.asset";
        private static readonly string PowerPlantPath = $"{BuildingsDataDir}/PowerPlant.asset";
        private static readonly string VehicleFactoryPath = $"{BuildingsDataDir}/VehicleFactory.asset";
        private static readonly string HarvesterPath = $"{UnitsDataDir}/Harvester.asset";
        private static readonly string MatchSetupPath = $"{RtsSandboxBuilder.DataDir}/MatchSetup.asset";
        private static readonly string DepositPrefabPath = $"{RtsSandboxBuilder.PrefabsDir}/OreDeposit.prefab";
        private static readonly string GhostValidPath = $"{RtsSandboxBuilder.MaterialsDir}/GhostValid.mat";
        private static readonly string GhostInvalidPath = $"{RtsSandboxBuilder.MaterialsDir}/GhostInvalid.mat";

        public class Content
        {
            public MatchSetup MatchSetup;
            public BuildingDefinition Headquarters, Refinery, PowerPlant, VehicleFactory;
            public HarvesterDefinition Harvester;
            public UnitDefinition LightTank, HeavyTank;
            public BuildingDefinition Outpost;
            public CombatVisuals CombatVisuals;
            public CombatContentBuilder.Defs Combat;
            public GameObject DepositPrefab;
            public Material GhostValid, GhostInvalid;
        }

        public class Palette
        {
            public Material Body, Dark, Faction, Ore, HarvesterPaint, LightTankPaint, HeavyTankPaint, Ring;
        }

        /// <summary>Renderers using this material get tinted with the owner's faction colour.</summary>
        private static Material s_factionMaterial;
        /// <summary>Milestone 006 art: generated textures/materials shared by every model.</summary>
        private static ArtLibrary s_art;

        // ---------- Assets ----------

        public static Content CreateAssets(Material selectionRing)
        {
            RtsSandboxBuilder.EnsureFolder(BuildingsDataDir);
            RtsSandboxBuilder.EnsureFolder(UnitsDataDir);
            RtsSandboxBuilder.EnsureFolder(BuildingPrefabsDir);
            RtsSandboxBuilder.EnsureFolder(UnitPrefabsDir);

            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            var pal = new Palette
            {
                Body = RtsSandboxBuilder.CreateOrUpdateMaterial("Structure", lit, new Color(0.58f, 0.6f, 0.63f)),
                Dark = RtsSandboxBuilder.CreateOrUpdateMaterial("StructureDark", lit, new Color(0.22f, 0.23f, 0.25f)),
                Faction = RtsSandboxBuilder.CreateOrUpdateMaterial("FactionBlue", lit, new Color(0.2f, 0.45f, 0.95f)),
                Ore = RtsSandboxBuilder.CreateOrUpdateMaterial("Ore", lit, new Color(0.95f, 0.75f, 0.15f)),
                HarvesterPaint = RtsSandboxBuilder.CreateOrUpdateMaterial("HarvesterPaint", lit, new Color(0.85f, 0.65f, 0.2f)),
                LightTankPaint = RtsSandboxBuilder.CreateOrUpdateMaterial("LightTankPaint", lit, new Color(0.62f, 0.64f, 0.44f)),
                HeavyTankPaint = RtsSandboxBuilder.CreateOrUpdateMaterial("HeavyTankPaint", lit, new Color(0.24f, 0.28f, 0.22f)),
                Ring = selectionRing,
            };
            s_factionMaterial = pal.Faction;
            s_art = ArtLibrary.Create();

            var c = new Content
            {
                GhostValid = CreateTransparent("GhostValid", unlit, new Color(0.2f, 1f, 0.3f, 0.35f)),
                GhostInvalid = CreateTransparent("GhostInvalid", unlit, new Color(1f, 0.2f, 0.15f, 0.35f)),
            };

            // Definitions first (prefabs reference them, and they reference the prefabs).
            c.Headquarters = LoadOrCreate<BuildingDefinition>(HeadquartersPath);
            c.Refinery = LoadOrCreate<BuildingDefinition>(RefineryPath);
            c.PowerPlant = LoadOrCreate<BuildingDefinition>(PowerPlantPath);
            c.VehicleFactory = LoadOrCreate<BuildingDefinition>(VehicleFactoryPath);
            c.Harvester = LoadOrCreate<HarvesterDefinition>(HarvesterPath);
            c.MatchSetup = LoadOrCreate<MatchSetup>(MatchSetupPath);
            var combat = CombatContentBuilder.CreateDefinitions(unlit);

            SetBuilding(c.Headquarters, "headquarters", "Headquarters", cost: 0, size: new Vector2(8f, 8f), apron: 3f,
                powerOut: 20, powerDrain: 0, constructible: false, prereqs: new BuildingDefinition[0], units: new UnitDefinition[0]);
            SetBuilding(c.Refinery, "refinery", "Refinery", cost: 1000, size: new Vector2(7f, 6f), apron: 4f,
                powerOut: 0, powerDrain: 15, constructible: true, prereqs: new[] { c.Headquarters }, units: new UnitDefinition[0]);
            SetBuilding(c.PowerPlant, "power_plant", "Power Plant", cost: 500, size: new Vector2(5f, 5f), apron: 0f,
                powerOut: 50, powerDrain: 0, constructible: true, prereqs: new[] { c.Headquarters }, units: new UnitDefinition[0]);
            SetBuilding(c.VehicleFactory, "vehicle_factory", "Vehicle Factory", cost: 1300, size: new Vector2(8f, 7f), apron: 5f,
                powerOut: 0, powerDrain: 30, constructible: true, prereqs: new[] { c.Headquarters },
                units: new UnitDefinition[] { c.Harvester, combat.LightTank, combat.HeavyTank });
            SetBuildingCombat(c.Headquarters, maxHealth: 2000f, armour: 0.3f);
            SetBuildingCombat(c.Refinery, maxHealth: 1200f, armour: 0.2f);
            SetBuildingCombat(c.PowerPlant, maxHealth: 800f, armour: 0.2f);
            SetBuildingCombat(c.VehicleFactory, maxHealth: 1500f, armour: 0.25f);

            SetUnit(c.Harvester, "harvester", "Harvester", cost: 800, buildTime: 10f);
            Set(c.Harvester, so =>
            {
                so.FindProperty("cargoCapacity").intValue = 200;
                so.FindProperty("harvestRate").floatValue = 50f;
                so.FindProperty("unloadRate").floatValue = 100f;
            });
            SetUnitCombat(c.Harvester, maxHealth: 600f, armour: 0.3f, moveSpeed: 6f, weapon: null);

            Set(c.MatchSetup, so =>
            {
                var factions = so.FindProperty("factions");
                factions.arraySize = 2;
                SetFaction(factions.GetArrayElementAtIndex(0), MatchController.DefaultPlayerFactionId, "Player",
                    new Color(0.2f, 0.45f, 0.95f), human: true, credits: StartingCredits);
                SetFaction(factions.GetArrayElementAtIndex(1), CombatContentBuilder.EnemyFactionId, "Enemy",
                    new Color(0.85f, 0.15f, 0.12f), human: false, credits: StartingCredits); // equal start for the AI match
                so.FindProperty("combatVisuals").objectReferenceValue = combat.Visuals;
                SetList(so.FindProperty("constructionOptions"), c.Refinery, c.PowerPlant, c.VehicleFactory);
                so.FindProperty("creditsPerOre").intValue = 1;
                so.FindProperty("lowPowerProductionMultiplier").floatValue = 0.5f;
                so.FindProperty("productionQueueCapacity").intValue = 5;
            });

            // Prefabs.
            SetPrefab(c.Headquarters, SavePrefab(BuildHeadquarters(c.Headquarters, pal), $"{BuildingPrefabsDir}/Headquarters.prefab"));
            SetPrefab(c.Refinery, SavePrefab(BuildRefinery(c.Refinery, pal), $"{BuildingPrefabsDir}/Refinery.prefab"));
            SetPrefab(c.PowerPlant, SavePrefab(BuildPowerPlant(c.PowerPlant, pal), $"{BuildingPrefabsDir}/PowerPlant.prefab"));
            SetPrefab(c.VehicleFactory, SavePrefab(BuildFactory(c.VehicleFactory, pal), $"{BuildingPrefabsDir}/VehicleFactory.prefab"));
            SetPrefab(c.Harvester, SavePrefab(BuildHarvester(c.Harvester, pal), $"{UnitPrefabsDir}/Harvester.prefab"));
            CombatContentBuilder.CreatePrefabs(combat, pal);
            c.DepositPrefab = SavePrefab(BuildDeposit(pal), DepositPrefabPath);

            AssetDatabase.SaveAssets();
            return Load();
        }

        /// <summary>
        /// Loads the generated content from disk. Call again after opening a new scene: NewScene unloads
        /// unreferenced assets, which leaves previously loaded ScriptableObject references dead.
        /// </summary>
        public static Content Load()
        {
            var c = new Content
            {
                Headquarters = Require<BuildingDefinition>(HeadquartersPath),
                Refinery = Require<BuildingDefinition>(RefineryPath),
                PowerPlant = Require<BuildingDefinition>(PowerPlantPath),
                VehicleFactory = Require<BuildingDefinition>(VehicleFactoryPath),
                Harvester = Require<HarvesterDefinition>(HarvesterPath),
                MatchSetup = Require<MatchSetup>(MatchSetupPath),
                DepositPrefab = Require<GameObject>(DepositPrefabPath),
                GhostValid = Require<Material>(GhostValidPath),
                GhostInvalid = Require<Material>(GhostInvalidPath),
            };
            c.Combat = CombatContentBuilder.Load();
            c.LightTank = c.Combat.LightTank;
            c.HeavyTank = c.Combat.HeavyTank;
            c.Outpost = c.Combat.Outpost;
            c.CombatVisuals = c.Combat.Visuals;
            return c;
        }

        internal static T Require<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) throw new InvalidOperationException("Missing generated asset " + path);
            return asset;
        }

        // ---------- Scene ----------

        /// <summary>Places the acceptance-scenario start: HQ, refinery, one harvester and one deposit.</summary>
        public static void PopulateScene(Content c)
        {
            var baseRoot = new GameObject("Base").transform;
            Place(c.Headquarters.Prefab, baseRoot, HeadquartersPosition, "Headquarters");
            Place(c.Refinery.Prefab, baseRoot, RefineryPosition, "Refinery");

            var resources = new GameObject("Resources").transform;
            Place(c.DepositPrefab, resources, DepositPosition, "OreDeposit_01");

            var units = GameObject.Find("Units");
            var harvester = Place(c.Harvester.Prefab, units != null ? units.transform : null, HarvesterPosition, "Harvester_01");
            harvester.transform.rotation = Quaternion.Euler(0f, 90f, 0f);

            Summary = "HQ + refinery + 1 harvester + 1 ore deposit";
        }

        private static GameObject Place(GameObject prefab, Transform parent, Vector3 position, string name)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.name = name;
            go.transform.position = position;
            return go;
        }

        // ---------- Structure prefabs ----------

        internal static GameObject NewStructure(string name, BuildingDefinition def, float height, Palette pal,
            out Building building)
        {
            var root = new GameObject(name) { layer = RtsSandboxBuilder.StructuresLayerIndex };
            var size = def.FootprintSize;

            var col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, height * 0.5f, 0f);
            col.size = new Vector3(size.x, height, size.y);

            var obstacle = root.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = col.center;
            obstacle.size = col.size;
            obstacle.carving = true;
            obstacle.carveOnlyStationary = true;

            root.AddComponent<FactionMember>();
            building = root.AddComponent<Building>();

            var frame = Part(root.transform, PrimitiveType.Cube, "SelectionIndicator", new Vector3(0f, 0.04f, 0f),
                new Vector3(size.x + 0.8f, 0.06f, size.y + 0.8f), pal.Ring, castShadows: false);
            frame.SetActive(false);

            RtsSandboxBuilder.SetRef(building, "definition", def);
            RtsSandboxBuilder.SetRef(building, "selectionIndicator", frame);

            var health = root.AddComponent<Health>();
            Set(health, so =>
            {
                so.FindProperty("aimHeight").floatValue = height * 0.5f;
                so.FindProperty("explosionSize").floatValue = Mathf.Max(size.x, size.y);
            });
            var bar = root.AddComponent<HealthBar>();
            Set(bar, so =>
            {
                so.FindProperty("height").floatValue = height + 1.2f;
                so.FindProperty("width").floatValue = Mathf.Min(4f, size.x * 0.6f);
                so.FindProperty("thickness").floatValue = 0.3f;
            });

            if (def.ApronDepth > 0f) StructureModels.Apron(root.transform, s_art, size, def.ApronDepth);
            root.AddComponent<DamageFx>();
            root.AddComponent<DestructionFx>();
            return root;
        }

        private static GameObject BuildHeadquarters(BuildingDefinition def, Palette pal)
        {
            var root = NewStructure("Headquarters", def, 4.6f, pal, out _);
            FinishStructure(root, StructureModels.Headquarters(root.transform, s_art), new[] { new Vector3(2f, 2.9f, 1.5f), new Vector3(-0.5f, 4.3f, -1f) });
            return root;
        }

        private static GameObject BuildRefinery(BuildingDefinition def, Palette pal)
        {
            var root = NewStructure("Refinery", def, 4f, pal, out _);
            root.AddComponent<Refinery>();
            FinishStructure(root, StructureModels.Refinery(root.transform, s_art), new[] { new Vector3(-1f, 3f, -0.5f), new Vector3(2.3f, 3.9f, 0f) });
            return root;
        }

        private static GameObject BuildPowerPlant(BuildingDefinition def, Palette pal)
        {
            var root = NewStructure("PowerPlant", def, 3.75f, pal, out _);
            FinishStructure(root, StructureModels.PowerPlant(root.transform, s_art), new[] { new Vector3(0f, 2.2f, 1.5f), new Vector3(1.2f, 3.3f, -0.5f) }, steam: true);
            return root;
        }

        private static GameObject BuildFactory(BuildingDefinition def, Palette pal)
        {
            var root = NewStructure("VehicleFactory", def, 3.1f, pal, out _);
            var t = root.transform;
            FinishStructure(root, StructureModels.VehicleFactory(t, s_art), new[] { new Vector3(-1.5f, 3.2f, 0f), new Vector3(2f, 3f, -1.5f) });

            var marker = new GameObject("RallyMarker");
            marker.transform.SetParent(t, false);
            Part(marker.transform, PrimitiveType.Cylinder, "Pole", new Vector3(0f, 0.9f, 0f), new Vector3(0.1f, 0.9f, 0.1f), pal.Dark);
            Part(marker.transform, PrimitiveType.Cube, "Flag", new Vector3(0.3f, 1.55f, 0f), new Vector3(0.6f, 0.4f, 0.05f), pal.Ring);
            marker.SetActive(false);

            var factory = root.AddComponent<VehicleFactory>();
            RtsSandboxBuilder.SetRef(factory, "rallyMarker", marker);
            RtsSandboxBuilder.SetInt(factory, "unitLayers", 1 << RtsSandboxBuilder.UnitsLayerIndex);
            return root;
        }

        /// <summary>Adds the Milestone 006 presentation components for a structure model.</summary>
        internal static void FinishStructure(GameObject root, StructureModels.Parts parts, Vector3[] damagePoints, bool steam = false)
        {
            var anim = root.AddComponent<StructureAnimator>();
            Set(anim, so =>
            {
                SetList(so.FindProperty("spinners"), parts.Spinners.ToArray());
                SetList(so.FindProperty("blinkers"), parts.Blinkers.ToArray());
                SetList(so.FindProperty(steam ? "steamPoints" : "smokePoints"), parts.SmokePoints.ToArray());
            });
            var construction = root.AddComponent<ConstructionFx>();
            RtsSandboxBuilder.SetRef(construction, "model", parts.Model);
            var damage = root.GetComponent<DamageFx>();
            Set(damage, so =>
            {
                so.FindProperty("smokeSize").floatValue = 1.6f;
                var list = so.FindProperty("localPoints");
                list.arraySize = damagePoints.Length;
                for (int i = 0; i < damagePoints.Length; i++) list.GetArrayElementAtIndex(i).vector3Value = damagePoints[i];
            });
        }

        // ---------- Unit and deposit prefabs ----------

        internal static GameObject NewVehicle(string name, UnitDefinition def, Vector3 colliderSize, float agentRadius, float speed,
            Palette pal, out SelectableUnit selectable)
        {
            var root = new GameObject(name) { layer = RtsSandboxBuilder.UnitsLayerIndex };
            var col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, colliderSize.y * 0.5f, 0f);
            col.size = colliderSize;
            RtsSandboxBuilder.AddAgent(root, agentRadius, colliderSize.y, baseOffset: 0f, speed: speed);
            root.AddComponent<UnitMover>();
            root.AddComponent<FactionMember>();
            selectable = root.AddComponent<SelectableUnit>();
            var ring = RtsSandboxBuilder.AddSelectionRing(root.transform, pal.Ring, 0.03f, Mathf.Max(colliderSize.x, colliderSize.z) + 0.6f);
            RtsSandboxBuilder.SetRef(selectable, "selectionIndicator", ring);

            var identity = root.AddComponent<UnitIdentity>();
            RtsSandboxBuilder.SetRef(identity, "definition", def);
            var health = root.AddComponent<Health>();
            Set(health, so =>
            {
                so.FindProperty("aimHeight").floatValue = colliderSize.y * 0.6f;
                so.FindProperty("explosionSize").floatValue = 3f;
            });
            root.AddComponent<HealthBar>();
            root.AddComponent<DamageFx>();
            root.AddComponent<DestructionFx>();
            return root;
        }

        private static GameObject BuildHarvester(HarvesterDefinition def, Palette pal)
        {
            var root = NewVehicle("Harvester", def, new Vector3(1.6f, 1.6f, 2.6f), 0.8f, 6f, pal, out _);
            var parts = VehicleModels.Harvester(root.transform, s_art);

            var harvester = root.AddComponent<Harvester>();
            RtsSandboxBuilder.SetRef(harvester, "definition", def);
            RtsSandboxBuilder.SetRef(harvester, "cargoVisual", parts.Cargo);

            var fx = root.AddComponent<HarvesterFx>();
            RtsSandboxBuilder.SetRef(fx, "drum", parts.Drum);
            RtsSandboxBuilder.SetRef(fx, "beacon", parts.Beacon);
            RtsSandboxBuilder.SetRef(fx, "scoopPoint", parts.ScoopPoint);
            RtsSandboxBuilder.SetRef(fx, "cargo", parts.Cargo);
            var motion = root.AddComponent<VehicleMotionFx>();
            Set(motion, so =>
            {
                so.FindProperty("hull").objectReferenceValue = parts.Body;
                SetList(so.FindProperty("dustPoints"), parts.DustPoints);
                so.FindProperty("dustSize").floatValue = 1.1f;
                so.FindProperty("maxPitch").floatValue = 2f;
            });
            SetWreck(root, parts.Body);
            return root;
        }

        /// <summary>Wreck = the vehicle visible meshes (excluding glow, glass and cargo), copied on death.</summary>
        internal static void SetWreck(GameObject root, params Transform[] from)
        {
            var filters = new List<MeshFilter>();
            foreach (var t in from)
                foreach (var f in t.GetComponentsInChildren<MeshFilter>(true))
                {
                    var r = f.GetComponent<MeshRenderer>();
                    if (r == null || r.sharedMaterial == s_art.GlowWarm_ || r.sharedMaterial == s_art.GlowCool_ || r.sharedMaterial == s_art.Glass
                        || r.sharedMaterial == s_art.OreCrystal || f.name == "Ore" || f.name == "Lamp") continue;
                    filters.Add(f);
                }
            Set(root.GetComponent<DestructionFx>(), so => SetList(so.FindProperty("wreckParts"), filters.ToArray()));
        }

        private static GameObject BuildDeposit(Palette pal)
        {
            var root = new GameObject("OreDeposit") { layer = RtsSandboxBuilder.ResourcesLayerIndex };
            var col = root.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0f, 1f, 0f);
            col.radius = 2.2f;
            col.height = 2f;

            var obstacle = root.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Capsule;
            obstacle.center = new Vector3(0f, 1f, 0f);
            obstacle.radius = 1.6f;
            obstacle.height = 2f;
            obstacle.carving = true;
            obstacle.carveOnlyStationary = true;

            // Ore field: a ring of amber crystal spires on dark mineral rock, over a stained patch of ground.
            var stain = new MeshKit();
            stain.Lathe(s_art.OreRock, new[] { new Vector2(3.4f, 0.01f), new Vector2(2.4f, 0.05f), new Vector2(0f, 0.06f) }, 14, Vector3.zero, capTop: false);
            var stainRenderer = ArtLibrary.Attach(root.transform, "Stain", "Ore_Stain", stain, shadows: false);
            stainRenderer.receiveShadows = true;

            var crystals = new MeshKit();
            var rng = new System.Random(1234);
            for (int i = 0; i < 6; i++)
            {
                float a = i * 60f + (float)rng.NextDouble() * 30f;
                var pos = Quaternion.Euler(0f, a, 0f) * Vector3.forward * (0.6f + (float)rng.NextDouble() * 1.0f);
                crystals.Block(s_art.OreRock, pos + Vector3.down * 0.05f, new Vector2(0.9f, 0.8f), new Vector2(0.5f, 0.4f), 0.35f + (float)rng.NextDouble() * 0.2f);
            }
            for (int i = 0; i < 14; i++)
            {
                float a = i * 26f + (float)rng.NextDouble() * 20f;
                float r = i < 3 ? (float)rng.NextDouble() * 0.5f : 0.5f + (float)rng.NextDouble() * 1.4f;
                var pos = Quaternion.Euler(0f, a, 0f) * Vector3.forward * r;
                float h = (i < 3 ? 1.5f : 0.6f) + (float)rng.NextDouble() * 0.7f;
                var tilt = Quaternion.Euler((float)rng.NextDouble() * 30f - 15f, a, (float)rng.NextDouble() * 30f - 15f + (r > 0.8f ? 12f : 0f));
                VehicleModels.Crystal(crystals, s_art.OreCrystal, pos, 0.16f + h * 0.08f, h, tilt);
            }
            var visual = new GameObject("Crystals");
            visual.transform.SetParent(root.transform, false);
            visual.layer = root.layer;
            ArtLibrary.Attach(visual.transform, "Spires", "Ore_Crystals", crystals);

            var deposit = root.AddComponent<OreDeposit>();
            root.AddComponent<OreDepositFx>(); // after OreDeposit: its RequireComponent would otherwise add a second one
            Set(deposit, so =>
            {
                so.FindProperty("initialAmount").intValue = 5000;
                so.FindProperty("harvestRadius").floatValue = 2.5f;
                so.FindProperty("visual").objectReferenceValue = visual.transform;
            });
            return root;
        }

        // ---------- Helpers ----------

        internal static GameObject Part(Transform parent, PrimitiveType type, string name, Vector3 localPos, Vector3 localScale,
            Material mat, bool castShadows = true)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            go.layer = parent.gameObject.layer;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            if (!castShadows) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        internal static GameObject SavePrefab(GameObject root, string path)
        {
            AddFactionColorizer(root);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path, out bool ok);
            Object.DestroyImmediate(root);
            if (!ok || prefab == null) throw new IOException("Failed to save prefab " + path);
            return prefab;
        }

        private static void AddFactionColorizer(GameObject root)
        {
            if (root.GetComponent<FactionMember>() == null || s_factionMaterial == null) return;
            var tinted = new List<Renderer>();
            var glowing = new List<Renderer>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r.sharedMaterial == s_factionMaterial || (s_art != null && r.sharedMaterial == s_art.Faction)) tinted.Add(r);
                else if (s_art != null && r.sharedMaterial == s_art.FactionGlow) glowing.Add(r);
            }
            if (tinted.Count == 0 && glowing.Count == 0) return;
            var colorizer = root.AddComponent<FactionColorizer>();
            Set(colorizer, so =>
            {
                SetList(so.FindProperty("renderers"), tinted.ToArray());
                SetList(so.FindProperty("emissiveRenderers"), glowing.ToArray());
            });
        }

        private static void SetFaction(SerializedProperty f, int id, string displayName, Color color, bool human, int credits)
        {
            f.FindPropertyRelative("id").intValue = id;
            f.FindPropertyRelative("displayName").stringValue = displayName;
            f.FindPropertyRelative("color").colorValue = color;
            f.FindPropertyRelative("humanControlled").boolValue = human;
            f.FindPropertyRelative("startingCredits").intValue = credits;
        }

        internal static void SetUnitCombat(UnitDefinition def, float maxHealth, float armour, float moveSpeed, WeaponDefinition weapon)
        {
            Set(def, so =>
            {
                so.FindProperty("maxHealth").floatValue = maxHealth;
                so.FindProperty("armour").floatValue = armour;
                so.FindProperty("moveSpeed").floatValue = moveSpeed;
                so.FindProperty("weapon").objectReferenceValue = weapon;
            });
        }

        internal static void SetBuildingCombat(BuildingDefinition def, float maxHealth, float armour)
        {
            Set(def, so =>
            {
                so.FindProperty("maxHealth").floatValue = maxHealth;
                so.FindProperty("armour").floatValue = armour;
            });
        }

        internal static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        internal static void Set(Object target, Action<SerializedObject> edit)
        {
            var so = new SerializedObject(target);
            edit(so);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        internal static void SetList(SerializedProperty list, params Object[] items)
        {
            list.arraySize = items.Length;
            for (int i = 0; i < items.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
        }

        internal static void SetBuilding(BuildingDefinition def, string id, string displayName, int cost, Vector2 size, float apron,
            int powerOut, int powerDrain, bool constructible, BuildingDefinition[] prereqs, UnitDefinition[] units)
        {
            Set(def, so =>
            {
                so.FindProperty("id").stringValue = id;
                so.FindProperty("displayName").stringValue = displayName;
                so.FindProperty("cost").intValue = cost;
                so.FindProperty("footprintSize").vector2Value = size;
                so.FindProperty("apronDepth").floatValue = apron;
                so.FindProperty("powerOutput").intValue = powerOut;
                so.FindProperty("powerDrain").intValue = powerDrain;
                so.FindProperty("constructible").boolValue = constructible;
                SetList(so.FindProperty("prerequisites"), prereqs);
                SetList(so.FindProperty("producibleUnits"), units);
            });
        }

        internal static void SetUnit(UnitDefinition def, string id, string displayName, int cost, float buildTime)
        {
            Set(def, so =>
            {
                so.FindProperty("id").stringValue = id;
                so.FindProperty("displayName").stringValue = displayName;
                so.FindProperty("cost").intValue = cost;
                so.FindProperty("buildTime").floatValue = buildTime;
            });
        }

        internal static void SetPrefab(Object def, GameObject prefab) =>
            Set(def, so => so.FindProperty("prefab").objectReferenceValue = prefab);

        private static Material CreateTransparent(string name, Shader unlit, Color color)
        {
            var mat = RtsSandboxBuilder.CreateOrUpdateMaterial(name, unlit, color);
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
