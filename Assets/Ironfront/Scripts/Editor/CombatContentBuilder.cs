using Ironfront.Combat;
using Ironfront.Audio;
using Ironfront.Data;
using Ironfront.EditorTools.Art;
using Ironfront.Match;
using Ironfront.Visuals;
using Ironfront.Testing;
using UnityEditor;
using UnityEngine;

namespace Ironfront.EditorTools
{
    /// <summary>
    /// Milestone 003 content: weapons, Light/Heavy Tank definitions and prefabs, the enemy outpost, combat
    /// visuals and the enemy test scenario. Balancing values live in the generated assets under
    /// Assets/Ironfront/Data and can be edited afterwards.
    /// </summary>
    public static class CombatContentBuilder
    {
        public const int EnemyFactionId = 2;

        private const string WeaponsDir = RtsSandboxBuilder.DataDir + "/Weapons";
        private static readonly string LightCannonPath = WeaponsDir + "/LightCannon.asset";
        private static readonly string HeavyCannonPath = WeaponsDir + "/HeavyCannon.asset";
        private static readonly string LightTankPath = RtsSandboxBuilder.DataDir + "/Units/LightTank.asset";
        private static readonly string HeavyTankPath = RtsSandboxBuilder.DataDir + "/Units/HeavyTank.asset";
        private static readonly string OutpostPath = RtsSandboxBuilder.DataDir + "/Buildings/EnemyOutpost.asset";
        private static readonly string VisualsPath = RtsSandboxBuilder.DataDir + "/CombatVisuals.asset";

        // Enemy test scenario layout (north of the central wall, away from the player's base and ore route).
        public static readonly Vector3 OutpostPosition = new Vector3(32f, 0f, 36f);
        public static readonly Vector3 WestSentryPosition = new Vector3(-40f, 0f, 16f);
        public static readonly Vector3 OutpostLightPosition = new Vector3(25f, 0f, 30f);
        public static readonly Vector3 OutpostHeavyPosition = new Vector3(38f, 0f, 28f);
        public static readonly Vector3[] WestPatrol = { new Vector3(-32f, 0f, 22f), new Vector3(-14f, 0f, 20f) };
        public static readonly Vector3[] EastPatrol = { new Vector3(22f, 0f, 10f), new Vector3(42f, 0f, 10f) };

        public static string Summary { get; private set; } = "";

        public class Defs
        {
            public WeaponDefinition LightCannon, HeavyCannon;
            public UnitDefinition LightTank, HeavyTank;
            public BuildingDefinition Outpost;
            public CombatVisuals Visuals;
        }

        // ---------- Assets ----------

        public static Defs CreateDefinitions(Shader unlit)
        {
            RtsSandboxBuilder.EnsureFolder(WeaponsDir);

            var d = new Defs
            {
                LightCannon = EconomyContentBuilder.LoadOrCreate<WeaponDefinition>(LightCannonPath),
                HeavyCannon = EconomyContentBuilder.LoadOrCreate<WeaponDefinition>(HeavyCannonPath),
                LightTank = EconomyContentBuilder.LoadOrCreate<UnitDefinition>(LightTankPath),
                HeavyTank = EconomyContentBuilder.LoadOrCreate<UnitDefinition>(HeavyTankPath),
                Outpost = EconomyContentBuilder.LoadOrCreate<BuildingDefinition>(OutpostPath),
                Visuals = EconomyContentBuilder.LoadOrCreate<CombatVisuals>(VisualsPath),
            };

            SetWeapon(d.LightCannon, "Light Cannon", damage: 25f, range: 11f, acquisition: 15f, cooldown: 1f, speed: 45f, size: 0.25f);
            SetWeapon(d.HeavyCannon, "Heavy Cannon", damage: 70f, range: 13f, acquisition: 16f, cooldown: 2.5f, speed: 35f, size: 0.4f);

            EconomyContentBuilder.SetUnit(d.LightTank, "light_tank", "Light Tank", cost: 500, buildTime: 6f);
            EconomyContentBuilder.SetUnitCombat(d.LightTank, maxHealth: 300f, armour: 0.1f, moveSpeed: 8f, weapon: d.LightCannon);
            EconomyContentBuilder.SetUnit(d.HeavyTank, "heavy_tank", "Heavy Tank", cost: 900, buildTime: 10f);
            EconomyContentBuilder.SetUnitCombat(d.HeavyTank, maxHealth: 700f, armour: 0.35f, moveSpeed: 5f, weapon: d.HeavyCannon);

            EconomyContentBuilder.SetBuilding(d.Outpost, "enemy_outpost", "Enemy Outpost", cost: 0, size: new Vector2(6f, 6f), apron: 0f,
                powerOut: 0, powerDrain: 0, constructible: false, prereqs: new BuildingDefinition[0], units: new UnitDefinition[0]);
            EconomyContentBuilder.SetBuildingCombat(d.Outpost, maxHealth: 1000f, armour: 0.1f);

            Material M(string name, Color c) => RtsSandboxBuilder.CreateOrUpdateMaterial(name, unlit, c);
            EconomyContentBuilder.Set(d.Visuals, so =>
            {
                so.FindProperty("projectile").objectReferenceValue = M("FxProjectile", new Color(1f, 0.85f, 0.3f));
                so.FindProperty("muzzleFlash").objectReferenceValue = M("FxMuzzleFlash", new Color(1f, 0.95f, 0.6f));
                so.FindProperty("impact").objectReferenceValue = M("FxImpact", new Color(1f, 0.55f, 0.15f));
                so.FindProperty("explosion").objectReferenceValue = M("FxExplosion", new Color(1f, 0.35f, 0.1f));
                so.FindProperty("healthBarBackground").objectReferenceValue = M("HealthBarBack", new Color(0.08f, 0.08f, 0.08f));
                so.FindProperty("healthBarFill").objectReferenceValue = M("HealthBarFill", Color.white);
                so.FindProperty("moveMarker").objectReferenceValue = M("MarkerMove", new Color(0.3f, 1f, 0.35f));
                so.FindProperty("attackMoveMarker").objectReferenceValue = M("MarkerAttackMove", new Color(1f, 0.6f, 0.1f));
                so.FindProperty("attackMarker").objectReferenceValue = M("MarkerAttack", new Color(1f, 0.15f, 0.1f));
                var art = EconomyContentBuilder.Art;
                so.FindProperty("fxFlash").objectReferenceValue = art.FxFlash;
                so.FindProperty("fxFire").objectReferenceValue = art.FxFire;
                so.FindProperty("fxSpark").objectReferenceValue = art.FxSpark;
                so.FindProperty("fxSmoke").objectReferenceValue = art.FxSmoke;
                so.FindProperty("fxDust").objectReferenceValue = art.FxDust;
                so.FindProperty("fxScorch").objectReferenceValue = art.FxScorch;
                so.FindProperty("fxRing").objectReferenceValue = art.FxRing;
                so.FindProperty("fxTracer").objectReferenceValue = art.FxTracer;
                so.FindProperty("fxGlint").objectReferenceValue = art.FxGlint;
                so.FindProperty("debris").objectReferenceValue = art.Wreck;
            });
            return d;
        }

        public static Defs Load() => new Defs
        {
            LightCannon = EconomyContentBuilder.Require<WeaponDefinition>(LightCannonPath),
            HeavyCannon = EconomyContentBuilder.Require<WeaponDefinition>(HeavyCannonPath),
            LightTank = EconomyContentBuilder.Require<UnitDefinition>(LightTankPath),
            HeavyTank = EconomyContentBuilder.Require<UnitDefinition>(HeavyTankPath),
            Outpost = EconomyContentBuilder.Require<BuildingDefinition>(OutpostPath),
            Visuals = EconomyContentBuilder.Require<CombatVisuals>(VisualsPath),
        };

        public static void CreatePrefabs(Defs d, EconomyContentBuilder.Palette pal)
        {
            string units = RtsSandboxBuilder.PrefabsDir + "/Units";
            string buildings = RtsSandboxBuilder.PrefabsDir + "/Buildings";
            EconomyContentBuilder.SetPrefab(d.LightTank, EconomyContentBuilder.SavePrefab(BuildLightTank(d.LightTank, pal), units + "/LightTank.prefab"));
            EconomyContentBuilder.SetPrefab(d.HeavyTank, EconomyContentBuilder.SavePrefab(BuildHeavyTank(d.HeavyTank, pal), units + "/HeavyTank.prefab"));
            EconomyContentBuilder.SetPrefab(d.Outpost, EconomyContentBuilder.SavePrefab(BuildOutpost(d.Outpost, pal), buildings + "/EnemyOutpost.prefab"));
        }

        // ---------- Prefabs ----------

        private static GameObject BuildLightTank(UnitDefinition def, EconomyContentBuilder.Palette pal)
        {
            var root = EconomyContentBuilder.NewVehicle("LightTank", def, new Vector3(1.6f, 1.1f, 2.2f), 0.75f, 8f, pal, out _);
            var parts = VehicleModels.LightTank(root.transform, EconomyContentBuilder.Art);
            AddCombat(root, parts.TurretPivot, parts.Muzzle);
            AddTankFx(root, parts, recoil: 0.16f, flash: 1f, SoundId.LightCannon);
            return root;
        }

        private static GameObject BuildHeavyTank(UnitDefinition def, EconomyContentBuilder.Palette pal)
        {
            var root = EconomyContentBuilder.NewVehicle("HeavyTank", def, new Vector3(2f, 1.4f, 2.8f), 0.9f, 5f, pal, out _);
            var parts = VehicleModels.HeavyTank(root.transform, EconomyContentBuilder.Art);
            AddCombat(root, parts.TurretPivot, parts.Muzzle);
            AddTankFx(root, parts, recoil: 0.24f, flash: 1.5f, SoundId.HeavyCannon);
            EconomyContentBuilder.Set(root.GetComponent<HealthBar>(), so =>
            {
                so.FindProperty("height").floatValue = 2.6f;
                so.FindProperty("width").floatValue = 2.2f;
            });
            return root;
        }

        internal static void AddTankFx(GameObject root, VehicleModels.TankParts parts, float recoil, float flash, SoundId sound)
        {
            var weapon = root.AddComponent<WeaponFx>();
            EconomyContentBuilder.Set(weapon, so =>
            {
                so.FindProperty("barrel").objectReferenceValue = parts.Barrel;
                so.FindProperty("muzzle").objectReferenceValue = parts.Muzzle;
                so.FindProperty("recoilDistance").floatValue = recoil;
                so.FindProperty("flashScale").floatValue = flash;
                so.FindProperty("fireSound").enumValueIndex = (int)sound;
            });
            var motion = root.AddComponent<VehicleMotionFx>();
            EconomyContentBuilder.Set(motion, so =>
            {
                so.FindProperty("hull").objectReferenceValue = parts.Hull;
                so.FindProperty("tracks").objectReferenceValue = parts.Tracks;
                EconomyContentBuilder.SetList(so.FindProperty("dustPoints"), parts.TrackDustPoints);
                EconomyContentBuilder.SetList(so.FindProperty("exhaustPoints"), parts.ExhaustPoints);
            });
            EconomyContentBuilder.Set(root.GetComponent<DamageFx>(), so => so.FindProperty("smokeSize").floatValue = flash * 0.7f);
            EconomyContentBuilder.SetWreck(root, parts.Hull, parts.TurretPivot);
            if (root.GetComponent<RangeIndicator>() == null) root.AddComponent<RangeIndicator>();
        }

        internal static void AddCombatUnit(GameObject root, Transform turret, Transform muzzle) => AddCombat(root, turret, muzzle);

        private static void AddCombat(GameObject root, Transform turret, Transform muzzle)
        {
            var combat = root.AddComponent<CombatUnit>();
            RtsSandboxBuilder.SetRef(combat, "turret", turret);
            RtsSandboxBuilder.SetRef(combat, "muzzle", muzzle);
            RtsSandboxBuilder.SetInt(combat, "lineOfSightBlockers", (1 << 0) | (1 << RtsSandboxBuilder.StructuresLayerIndex));
        }

        private static GameObject BuildOutpost(BuildingDefinition def, EconomyContentBuilder.Palette pal)
        {
            var root = EconomyContentBuilder.NewStructure("EnemyOutpost", def, 3.2f, pal, out _);
            EconomyContentBuilder.FinishStructure(root, StructureModels.Outpost(root.transform, EconomyContentBuilder.Art),
                new[] { new Vector3(0f, 2.5f, 0f) });
            return root;
        }

        // ---------- Scenario ----------

        /// <summary>Enemy test targets: an outpost, stationary (holding) tanks and two patrolling tanks. No AI.</summary>
        public static void PopulateScenario(Defs d)
        {
            var root = new GameObject("EnemyScenario").transform;

            Enemy(d.Outpost.Prefab, root, OutpostPosition, "Enemy_Outpost");

            var sentry = Enemy(d.LightTank.Prefab, root, WestSentryPosition, "Enemy_LightTank_WestSentry");
            Hold(sentry);
            Hold(Enemy(d.LightTank.Prefab, root, OutpostLightPosition, "Enemy_LightTank_OutpostGuard"));
            Hold(Enemy(d.HeavyTank.Prefab, root, OutpostHeavyPosition, "Enemy_HeavyTank_OutpostGuard"));

            Patrol(Enemy(d.LightTank.Prefab, root, WestPatrol[0], "Enemy_LightTank_WestPatrol"), WestPatrol);
            Patrol(Enemy(d.LightTank.Prefab, root, EastPatrol[0], "Enemy_LightTank_EastPatrol"), EastPatrol);

            Summary = "enemy outpost + 3 holding tanks + 2 patrolling tanks";
        }

        private static GameObject Enemy(GameObject prefab, Transform parent, Vector3 position, string name)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.name = name;
            go.transform.position = position;
            go.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            RtsSandboxBuilder.SetInt(go.GetComponent<FactionMember>(), "factionId", EnemyFactionId);
            return go;
        }

        private static void Hold(GameObject unit)
        {
            unit.AddComponent<ScenarioStartOrder>(); // defaults to Hold
        }

        private static void Patrol(GameObject unit, Vector3[] waypoints)
        {
            var patrol = unit.AddComponent<PatrolRoute>();
            EconomyContentBuilder.Set(patrol, so =>
            {
                var list = so.FindProperty("waypoints");
                list.arraySize = waypoints.Length;
                for (int i = 0; i < waypoints.Length; i++) list.GetArrayElementAtIndex(i).vector3Value = waypoints[i];
            });
        }

        private static void SetWeapon(WeaponDefinition w, string name, float damage, float range, float acquisition, float cooldown,
            float speed, float size)
        {
            EconomyContentBuilder.Set(w, so =>
            {
                so.FindProperty("displayName").stringValue = name;
                so.FindProperty("damage").floatValue = damage;
                so.FindProperty("range").floatValue = range;
                so.FindProperty("acquisitionRange").floatValue = acquisition;
                so.FindProperty("cooldown").floatValue = cooldown;
                so.FindProperty("projectileSpeed").floatValue = speed;
                so.FindProperty("requiresLineOfSight").boolValue = true;
                so.FindProperty("allowFriendlyFire").boolValue = false;
                so.FindProperty("projectileSize").floatValue = size;
            });
        }
    }
}
