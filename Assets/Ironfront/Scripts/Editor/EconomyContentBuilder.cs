using System;
using System.Collections.Generic;
using System.IO;
using Ironfront.Buildings;
using Ironfront.Data;
using Ironfront.Economy;
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

        private static readonly string HeadquartersPath = $"{BuildingsDataDir}/Headquarters.asset";
        private static readonly string RefineryPath = $"{BuildingsDataDir}/Refinery.asset";
        private static readonly string PowerPlantPath = $"{BuildingsDataDir}/PowerPlant.asset";
        private static readonly string VehicleFactoryPath = $"{BuildingsDataDir}/VehicleFactory.asset";
        private static readonly string HarvesterPath = $"{UnitsDataDir}/Harvester.asset";
        private static readonly string TankPath = $"{UnitsDataDir}/Tank.asset";
        private static readonly string MatchSetupPath = $"{RtsSandboxBuilder.DataDir}/MatchSetup.asset";
        private static readonly string DepositPrefabPath = $"{RtsSandboxBuilder.PrefabsDir}/OreDeposit.prefab";
        private static readonly string GhostValidPath = $"{RtsSandboxBuilder.MaterialsDir}/GhostValid.mat";
        private static readonly string GhostInvalidPath = $"{RtsSandboxBuilder.MaterialsDir}/GhostInvalid.mat";

        public class Content
        {
            public MatchSetup MatchSetup;
            public BuildingDefinition Headquarters, Refinery, PowerPlant, VehicleFactory;
            public HarvesterDefinition Harvester;
            public UnitDefinition Tank;
            public GameObject DepositPrefab;
            public Material GhostValid, GhostInvalid;
        }

        private class Palette
        {
            public Material Body, Dark, Faction, Ore, HarvesterPaint, TankPaint, Ring;
        }

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
                TankPaint = RtsSandboxBuilder.CreateOrUpdateMaterial("TankPaint", lit, new Color(0.35f, 0.42f, 0.28f)),
                Ring = selectionRing,
            };

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
            c.Tank = LoadOrCreate<UnitDefinition>(TankPath);
            c.MatchSetup = LoadOrCreate<MatchSetup>(MatchSetupPath);

            SetBuilding(c.Headquarters, "headquarters", "Headquarters", cost: 0, size: new Vector2(8f, 8f), apron: 3f,
                powerOut: 20, powerDrain: 0, constructible: false, prereqs: new BuildingDefinition[0], units: new UnitDefinition[0]);
            SetBuilding(c.Refinery, "refinery", "Refinery", cost: 1000, size: new Vector2(7f, 6f), apron: 4f,
                powerOut: 0, powerDrain: 15, constructible: true, prereqs: new[] { c.Headquarters }, units: new UnitDefinition[0]);
            SetBuilding(c.PowerPlant, "power_plant", "Power Plant", cost: 500, size: new Vector2(5f, 5f), apron: 0f,
                powerOut: 50, powerDrain: 0, constructible: true, prereqs: new[] { c.Headquarters }, units: new UnitDefinition[0]);
            SetBuilding(c.VehicleFactory, "vehicle_factory", "Vehicle Factory", cost: 1300, size: new Vector2(8f, 7f), apron: 5f,
                powerOut: 0, powerDrain: 30, constructible: true, prereqs: new[] { c.Headquarters },
                units: new UnitDefinition[] { c.Harvester, c.Tank });

            SetUnit(c.Harvester, "harvester", "Harvester", cost: 800, buildTime: 10f);
            Set(c.Harvester, so =>
            {
                so.FindProperty("cargoCapacity").intValue = 200;
                so.FindProperty("harvestRate").floatValue = 50f;
                so.FindProperty("unloadRate").floatValue = 100f;
            });
            SetUnit(c.Tank, "tank", "Tank", cost: 400, buildTime: 6f);

            Set(c.MatchSetup, so =>
            {
                var factions = so.FindProperty("factions");
                factions.arraySize = 1;
                var f = factions.GetArrayElementAtIndex(0);
                f.FindPropertyRelative("id").intValue = MatchController.DefaultPlayerFactionId;
                f.FindPropertyRelative("displayName").stringValue = "Player";
                f.FindPropertyRelative("color").colorValue = new Color(0.2f, 0.45f, 0.95f);
                f.FindPropertyRelative("humanControlled").boolValue = true;
                f.FindPropertyRelative("startingCredits").intValue = StartingCredits;
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
            SetPrefab(c.Tank, SavePrefab(BuildTank(pal), $"{UnitPrefabsDir}/Tank.prefab"));
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
                Tank = Require<UnitDefinition>(TankPath),
                MatchSetup = Require<MatchSetup>(MatchSetupPath),
                DepositPrefab = Require<GameObject>(DepositPrefabPath),
                GhostValid = Require<Material>(GhostValidPath),
                GhostInvalid = Require<Material>(GhostInvalidPath),
            };
            return c;
        }

        private static T Require<T>(string path) where T : Object
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

        private static GameObject NewStructure(string name, BuildingDefinition def, float height, Palette pal,
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

            if (def.ApronDepth > 0f)
            {
                Part(root.transform, PrimitiveType.Cube, "ApronPad",
                    new Vector3(0f, 0.02f, size.y * 0.5f + def.ApronDepth * 0.5f),
                    new Vector3(size.x, 0.04f, def.ApronDepth), pal.Dark, castShadows: false);
            }
            return root;
        }

        private static GameObject BuildHeadquarters(BuildingDefinition def, Palette pal)
        {
            var root = NewStructure("Headquarters", def, 4.6f, pal, out _);
            var t = root.transform;
            Part(t, PrimitiveType.Cube, "Base", new Vector3(0f, 1.25f, 0f), new Vector3(8f, 2.5f, 8f), pal.Body);
            Part(t, PrimitiveType.Cube, "Tower", new Vector3(0f, 3.5f, 0f), new Vector3(4f, 2f, 4f), pal.Body);
            Part(t, PrimitiveType.Cube, "Roof", new Vector3(0f, 4.55f, 0f), new Vector3(4.2f, 0.1f, 4.2f), pal.Faction);
            Part(t, PrimitiveType.Cylinder, "Antenna", new Vector3(1.2f, 5.5f, 1.2f), new Vector3(0.15f, 1f, 0.15f), pal.Dark);
            return root;
        }

        private static GameObject BuildRefinery(BuildingDefinition def, Palette pal)
        {
            var root = NewStructure("Refinery", def, 4f, pal, out _);
            root.AddComponent<Refinery>();
            var t = root.transform;
            Part(t, PrimitiveType.Cube, "Hall", new Vector3(-1f, 1.25f, 0f), new Vector3(5f, 2.5f, 6f), pal.Body);
            Part(t, PrimitiveType.Cube, "Stripe", new Vector3(-1f, 2.55f, 0f), new Vector3(5.1f, 0.1f, 6.1f), pal.Faction);
            Part(t, PrimitiveType.Cylinder, "SiloA", new Vector3(2.5f, 2f, -1.5f), new Vector3(1.6f, 2f, 1.6f), pal.Body);
            Part(t, PrimitiveType.Cylinder, "SiloB", new Vector3(2.5f, 2f, 1.5f), new Vector3(1.6f, 2f, 1.6f), pal.Body);
            Part(t, PrimitiveType.Cube, "OreBin", new Vector3(-1f, 2.7f, 0f), new Vector3(2f, 0.3f, 2f), pal.Ore);
            return root;
        }

        private static GameObject BuildPowerPlant(BuildingDefinition def, Palette pal)
        {
            var root = NewStructure("PowerPlant", def, 3.75f, pal, out _);
            var t = root.transform;
            Part(t, PrimitiveType.Cube, "Base", new Vector3(0f, 0.75f, 0f), new Vector3(5f, 1.5f, 5f), pal.Body);
            Part(t, PrimitiveType.Cube, "Stripe", new Vector3(0f, 1.55f, 0f), new Vector3(5.1f, 0.1f, 5.1f), pal.Faction);
            Part(t, PrimitiveType.Cylinder, "CoolingTowerA", new Vector3(-1.2f, 2.25f, 0f), new Vector3(1.8f, 1.5f, 1.8f), pal.Body);
            Part(t, PrimitiveType.Cylinder, "CoolingTowerB", new Vector3(1.2f, 2.25f, 0f), new Vector3(1.8f, 1.5f, 1.8f), pal.Body);
            return root;
        }

        private static GameObject BuildFactory(BuildingDefinition def, Palette pal)
        {
            var root = NewStructure("VehicleFactory", def, 3.1f, pal, out _);
            var t = root.transform;
            Part(t, PrimitiveType.Cube, "Hall", new Vector3(0f, 1.5f, 0f), new Vector3(8f, 3f, 7f), pal.Body);
            Part(t, PrimitiveType.Cube, "Stripe", new Vector3(0f, 3.05f, 0f), new Vector3(8.1f, 0.1f, 7.1f), pal.Faction);
            Part(t, PrimitiveType.Cube, "Door", new Vector3(0f, 1.2f, 3.51f), new Vector3(4f, 2.4f, 0.05f), pal.Dark);

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

        // ---------- Unit and deposit prefabs ----------

        private static GameObject NewVehicle(string name, Vector3 colliderSize, float agentRadius, float speed, Palette pal,
            out SelectableUnit selectable)
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
            return root;
        }

        private static GameObject BuildHarvester(HarvesterDefinition def, Palette pal)
        {
            var root = NewVehicle("Harvester", new Vector3(1.6f, 1.6f, 2.6f), 0.8f, 6f, pal, out _);
            var t = root.transform;
            Part(t, PrimitiveType.Cube, "Chassis", new Vector3(0f, 0.55f, 0f), new Vector3(1.6f, 0.7f, 2.6f), pal.HarvesterPaint);
            Part(t, PrimitiveType.Cube, "Cab", new Vector3(0f, 1.2f, 0.85f), new Vector3(1.4f, 0.7f, 0.8f), pal.Faction);
            Part(t, PrimitiveType.Cube, "Hopper", new Vector3(0f, 1.0f, -0.45f), new Vector3(1.5f, 0.2f, 1.5f), pal.Dark);
            var cargo = Part(t, PrimitiveType.Cube, "Cargo", new Vector3(0f, 1.35f, -0.45f), new Vector3(1.3f, 0.5f, 1.3f), pal.Ore);

            var harvester = root.AddComponent<Harvester>();
            RtsSandboxBuilder.SetRef(harvester, "definition", def);
            RtsSandboxBuilder.SetRef(harvester, "cargoVisual", cargo.transform);
            return root;
        }

        private static GameObject BuildTank(Palette pal)
        {
            var root = NewVehicle("Tank", new Vector3(1.8f, 1.2f, 2.4f), 0.8f, 6.5f, pal, out _);
            var t = root.transform;
            Part(t, PrimitiveType.Cube, "Hull", new Vector3(0f, 0.5f, 0f), new Vector3(1.6f, 0.6f, 2.3f), pal.TankPaint);
            Part(t, PrimitiveType.Cube, "TrackL", new Vector3(-0.8f, 0.3f, 0f), new Vector3(0.3f, 0.6f, 2.4f), pal.Dark);
            Part(t, PrimitiveType.Cube, "TrackR", new Vector3(0.8f, 0.3f, 0f), new Vector3(0.3f, 0.6f, 2.4f), pal.Dark);
            Part(t, PrimitiveType.Cylinder, "Turret", new Vector3(0f, 0.95f, -0.1f), new Vector3(1.1f, 0.2f, 1.1f), pal.Faction);
            Part(t, PrimitiveType.Cube, "Barrel", new Vector3(0f, 0.97f, 0.95f), new Vector3(0.18f, 0.18f, 1.4f), pal.Dark);
            return root;
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

            var visual = new GameObject("Crystals");
            visual.transform.SetParent(root.transform, false);
            visual.layer = root.layer;
            var rng = new System.Random(1234);
            for (int i = 0; i < 9; i++)
            {
                float angle = i * 40f + (float)rng.NextDouble() * 20f;
                float radius = i == 0 ? 0f : 0.7f + (float)rng.NextDouble() * 1.1f;
                var pos = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * radius;
                float h = 0.6f + (float)rng.NextDouble() * 1.0f;
                var crystal = Part(visual.transform, PrimitiveType.Cube, "Crystal_" + i,
                    new Vector3(pos.x, h * 0.5f, pos.z), new Vector3(0.6f, h, 0.6f), pal.Ore);
                crystal.transform.localRotation = Quaternion.Euler((float)rng.NextDouble() * 20f, angle, (float)rng.NextDouble() * 20f);
            }

            var deposit = root.AddComponent<OreDeposit>();
            Set(deposit, so =>
            {
                so.FindProperty("initialAmount").intValue = 5000;
                so.FindProperty("harvestRadius").floatValue = 2.5f;
                so.FindProperty("visual").objectReferenceValue = visual.transform;
            });
            return root;
        }

        // ---------- Helpers ----------

        private static GameObject Part(Transform parent, PrimitiveType type, string name, Vector3 localPos, Vector3 localScale,
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

        private static GameObject SavePrefab(GameObject root, string path)
        {
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path, out bool ok);
            Object.DestroyImmediate(root);
            if (!ok || prefab == null) throw new IOException("Failed to save prefab " + path);
            return prefab;
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static void Set(Object target, Action<SerializedObject> edit)
        {
            var so = new SerializedObject(target);
            edit(so);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        private static void SetList(SerializedProperty list, params Object[] items)
        {
            list.arraySize = items.Length;
            for (int i = 0; i < items.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
        }

        private static void SetBuilding(BuildingDefinition def, string id, string displayName, int cost, Vector2 size, float apron,
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

        private static void SetUnit(UnitDefinition def, string id, string displayName, int cost, float buildTime)
        {
            Set(def, so =>
            {
                so.FindProperty("id").stringValue = id;
                so.FindProperty("displayName").stringValue = displayName;
                so.FindProperty("cost").intValue = cost;
                so.FindProperty("buildTime").floatValue = buildTime;
            });
        }

        private static void SetPrefab(Object def, GameObject prefab) =>
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
