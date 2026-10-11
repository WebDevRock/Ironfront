using Ironfront.Audio;
using Ironfront.Buildings;
using Ironfront.Combat;
using Ironfront.Core.Combat;
using Ironfront.Core.Research;
using Ironfront.Data;
using Ironfront.EditorTools.Art;
using Ironfront.Visuals;
using UnityEditor;
using UnityEngine;

namespace Ironfront.EditorTools
{
    /// <summary>
    /// Milestone 007 content: Rocket Artillery, Recon Vehicle, Guard Tower, Anti-Armour Turret, Research Centre,
    /// their weapons, three research topics, plus sight ranges, armour classes and player-facing descriptions
    /// for the existing units and structures. All balance values are set here and live in the generated assets
    /// under Assets/Ironfront/Data (see Docs/Milestone-007-Design.md for the reasoning).
    /// </summary>
    public static class ExpansionContentBuilder
    {
        private const string Data = RtsSandboxBuilder.DataDir;
        private const string ResearchDir = Data + "/Research";

        public class Defs
        {
            public WeaponDefinition RocketSalvo, ReconGun, TowerGun, AtCannon;
            public UnitDefinition Recon, Artillery;
            public BuildingDefinition ResearchCentre, GuardTower, AtTurret;
            public ResearchDefinition Armour, Weapons, Harvesting;
            public ResearchDefinition[] Research => new[] { Armour, Weapons, Harvesting };
        }

        private static string P(string sub) => $"{Data}/{sub}.asset";

        public static Defs Load() => new Defs
        {
            RocketSalvo = EconomyContentBuilder.Require<WeaponDefinition>(P("Weapons/RocketSalvo")),
            ReconGun = EconomyContentBuilder.Require<WeaponDefinition>(P("Weapons/ReconGun")),
            TowerGun = EconomyContentBuilder.Require<WeaponDefinition>(P("Weapons/TowerGun")),
            AtCannon = EconomyContentBuilder.Require<WeaponDefinition>(P("Weapons/AtCannon")),
            Recon = EconomyContentBuilder.Require<UnitDefinition>(P("Units/Recon")),
            Artillery = EconomyContentBuilder.Require<UnitDefinition>(P("Units/RocketArtillery")),
            ResearchCentre = EconomyContentBuilder.Require<BuildingDefinition>(P("Buildings/ResearchCentre")),
            GuardTower = EconomyContentBuilder.Require<BuildingDefinition>(P("Buildings/GuardTower")),
            AtTurret = EconomyContentBuilder.Require<BuildingDefinition>(P("Buildings/AntiArmourTurret")),
            Armour = EconomyContentBuilder.Require<ResearchDefinition>(P("Research/CompositeArmour")),
            Weapons = EconomyContentBuilder.Require<ResearchDefinition>(P("Research/EnhancedWeapons")),
            Harvesting = EconomyContentBuilder.Require<ResearchDefinition>(P("Research/AdvancedHarvesting")),
        };

        // ---------- Definitions ----------

        public static Defs CreateDefinitions(EconomyContentBuilder.Content c, CombatContentBuilder.Defs combat)
        {
            RtsSandboxBuilder.EnsureFolder(ResearchDir);
            var d = new Defs
            {
                RocketSalvo = EconomyContentBuilder.LoadOrCreate<WeaponDefinition>(P("Weapons/RocketSalvo")),
                ReconGun = EconomyContentBuilder.LoadOrCreate<WeaponDefinition>(P("Weapons/ReconGun")),
                TowerGun = EconomyContentBuilder.LoadOrCreate<WeaponDefinition>(P("Weapons/TowerGun")),
                AtCannon = EconomyContentBuilder.LoadOrCreate<WeaponDefinition>(P("Weapons/AtCannon")),
                Recon = EconomyContentBuilder.LoadOrCreate<UnitDefinition>(P("Units/Recon")),
                Artillery = EconomyContentBuilder.LoadOrCreate<UnitDefinition>(P("Units/RocketArtillery")),
                ResearchCentre = EconomyContentBuilder.LoadOrCreate<BuildingDefinition>(P("Buildings/ResearchCentre")),
                GuardTower = EconomyContentBuilder.LoadOrCreate<BuildingDefinition>(P("Buildings/GuardTower")),
                AtTurret = EconomyContentBuilder.LoadOrCreate<BuildingDefinition>(P("Buildings/AntiArmourTurret")),
                Armour = EconomyContentBuilder.LoadOrCreate<ResearchDefinition>(P("Research/CompositeArmour")),
                Weapons = EconomyContentBuilder.LoadOrCreate<ResearchDefinition>(P("Research/EnhancedWeapons")),
                Harvesting = EconomyContentBuilder.LoadOrCreate<ResearchDefinition>(P("Research/AdvancedHarvesting")),
            };

            // Existing weapons keep their M003 values; they gain explicit (neutral) effectiveness and a role line.
            Weapon(combat.LightCannon, role: "All-round cannon", minRange: 0f, splash: 0f, salvo: 1, ballistic: false, l: 1f, h: 1f, s: 1f, keepStats: true);
            Weapon(combat.HeavyCannon, role: "Twin heavy cannon", minRange: 0f, splash: 0f, salvo: 1, ballistic: false, l: 1f, h: 1f, s: 1f, keepStats: true);

            Weapon(d.RocketSalvo, "Rocket Salvo", damage: 120f, range: 28f, acquisition: 28f, cooldown: 6f, speed: 20f, size: 0.3f,
                role: "Long range, splash; devastating vs structures. Cannot fire closer than 9 m.",
                minRange: 9f, splash: 3f, salvo: 3, ballistic: true, l: 0.7f, h: 1.0f, s: 2.0f, los: false);
            Weapon(d.ReconGun, "Recon Machine Gun", damage: 9f, range: 9f, acquisition: 12f, cooldown: 0.5f, speed: 60f, size: 0.12f,
                role: "Light gun: harasses light vehicles, weak vs armour", minRange: 0f, splash: 0f, salvo: 1, ballistic: false, l: 1f, h: 0.35f, s: 0.25f);
            Weapon(d.TowerGun, "Twin Machine Guns", damage: 14f, range: 12f, acquisition: 12f, cooldown: 0.45f, speed: 60f, size: 0.12f,
                role: "Shreds light vehicles; weak vs heavy armour", minRange: 0f, splash: 0f, salvo: 2, ballistic: false, l: 1f, h: 0.35f, s: 0.3f);
            Weapon(d.AtCannon, "Anti-Armour Cannon", damage: 95f, range: 15f, acquisition: 15f, cooldown: 2.4f, speed: 55f, size: 0.35f,
                role: "Strong vs heavy armour", minRange: 0f, splash: 0f, salvo: 1, ballistic: false, l: 0.6f, h: 1.5f, s: 0.5f);

            // Units.
            EconomyContentBuilder.SetUnit(d.Recon, "recon", "Recon Vehicle", cost: 450, buildTime: 5f);
            EconomyContentBuilder.SetUnitCombat(d.Recon, maxHealth: 180f, armour: 0f, moveSpeed: 12f, weapon: d.ReconGun);
            Unit(d.Recon, sight: 28f, ArmourClass.Light, null,
                "Fast, lightly armoured scout. Sees twice as far as a tank: use it to explore, spot for artillery and warn of attacks. Weak in a fight.");
            EconomyContentBuilder.SetUnit(d.Artillery, "rocket_artillery", "Rocket Artillery", cost: 1100, buildTime: 12f);
            EconomyContentBuilder.SetUnitCombat(d.Artillery, maxHealth: 260f, armour: 0.05f, moveSpeed: 5.5f, weapon: d.RocketSalvo);
            Unit(d.Artillery, sight: 12f, ArmourClass.Light, d.ResearchCentre,
                "Long-range rocket launcher (28 m) that wrecks structures and defences. Needs others to spot distant targets, " +
                "cannot fire at anything closer than 9 m and is easy prey for fast attackers. Moving targets can dodge its rockets.");
            Unit(combat.LightTank, sight: 15f, ArmourClass.Light, null, "Fast, inexpensive tank. Good all-rounder and the counter to artillery.");
            Unit(combat.HeavyTank, sight: 14f, ArmourClass.Heavy, null, "Slow, heavily armoured twin-gun tank. Wins head-on fights; vulnerable to anti-armour turrets and artillery.");
            Unit(c.Harvester, sight: 10f, ArmourClass.Heavy, null, "Collects ore and delivers it to a refinery for credits.");

            // Structures.
            EconomyContentBuilder.SetBuilding(d.ResearchCentre, "research_centre", "Research Centre", cost: 1200, size: new Vector2(5f, 5f), apron: 0f,
                powerOut: 0, powerDrain: 30, constructible: true, prereqs: new[] { c.VehicleFactory }, units: new UnitDefinition[0]);
            EconomyContentBuilder.SetBuildingCombat(d.ResearchCentre, maxHealth: 1000f, armour: 0.2f);
            Building(d.ResearchCentre, sight: 10f, null, false,
                "Unlocks research upgrades and Rocket Artillery. Research one topic at a time; progress halves on low power.");
            EconomyContentBuilder.SetBuilding(d.GuardTower, "guard_tower", "Guard Tower", cost: 400, size: new Vector2(3f, 3f), apron: 0f,
                powerOut: 0, powerDrain: 0, constructible: true, prereqs: new[] { c.Headquarters }, units: new UnitDefinition[0]);
            EconomyContentBuilder.SetBuildingCombat(d.GuardTower, maxHealth: 800f, armour: 0.25f);
            Building(d.GuardTower, sight: 16f, d.TowerGun, false,
                "Cheap automated machine-gun tower (12 m). Stops scouts and light raids; weak against heavy tanks. Needs no power.");
            EconomyContentBuilder.SetBuilding(d.AtTurret, "at_turret", "Anti-Armour Turret", cost: 900, size: new Vector2(3f, 3f), apron: 0f,
                powerOut: 0, powerDrain: 25, constructible: true, prereqs: new[] { c.VehicleFactory }, units: new UnitDefinition[0]);
            EconomyContentBuilder.SetBuildingCombat(d.AtTurret, maxHealth: 1000f, armour: 0.4f);
            Building(d.AtTurret, sight: 16f, d.AtCannon, true,
                "Heavy cannon emplacement (15 m) that stops tanks. Goes offline on low power and is outranged by Rocket Artillery.");
            Building(c.Headquarters, sight: 18f, null, false, "Command centre. Lose it and you lose the match.");
            Building(c.Refinery, sight: 10f, null, false, "Turns ore delivered by harvesters into credits. Comes with a harvester dock.");
            Building(c.PowerPlant, sight: 9f, null, false, "Generates 50 power. Low power halves production and research and shuts down anti-armour turrets.");
            Building(c.VehicleFactory, sight: 11f, null, false, "Builds harvesters and combat vehicles.");
            Building(combat.Outpost, sight: 14f, null, false, "Fortified enemy outpost.");

            // Research.
            Research(d.Armour, "composite_armour", "Composite Armour", 1000, 45f, UpgradeEffect.VehicleArmour, 0.1f, null,
                "+10 armour (damage reduction) for all your vehicles, including those already in the field.");
            Research(d.Weapons, "enhanced_weapons", "Enhanced Weapon Systems", 1200, 50f, UpgradeEffect.WeaponDamage, 0.2f, d.Armour,
                "+20% damage for all your vehicles and defences. Requires Composite Armour.");
            Research(d.Harvesting, "advanced_harvesting", "Advanced Harvesting", 800, 35f, UpgradeEffect.HarvestRate, 0.4f, null,
                "Harvesters mine and unload 40% faster.");
            return d;
        }

        private static void Weapon(WeaponDefinition w, string name = null, float damage = 0f, float range = 0f, float acquisition = 0f, float cooldown = 0f,
            float speed = 0f, float size = 0f, string role = "", float minRange = 0f, float splash = 0f, int salvo = 1, bool ballistic = false,
            float l = 1f, float h = 1f, float s = 1f, bool los = true, bool keepStats = false)
        {
            EconomyContentBuilder.Set(w, so =>
            {
                if (!keepStats)
                {
                    so.FindProperty("displayName").stringValue = name;
                    so.FindProperty("damage").floatValue = damage;
                    so.FindProperty("range").floatValue = range;
                    so.FindProperty("acquisitionRange").floatValue = acquisition;
                    so.FindProperty("cooldown").floatValue = cooldown;
                    so.FindProperty("projectileSpeed").floatValue = speed;
                    so.FindProperty("requiresLineOfSight").boolValue = los;
                    so.FindProperty("allowFriendlyFire").boolValue = false;
                    so.FindProperty("projectileSize").floatValue = size;
                }
                so.FindProperty("minRange").floatValue = minRange;
                so.FindProperty("splashRadius").floatValue = splash;
                so.FindProperty("salvo").intValue = salvo;
                so.FindProperty("ballistic").boolValue = ballistic;
                so.FindProperty("role").stringValue = role;
                var e = so.FindProperty("effectiveness");
                e.FindPropertyRelative("VsLight").floatValue = l;
                e.FindPropertyRelative("VsHeavy").floatValue = h;
                e.FindPropertyRelative("VsStructure").floatValue = s;
            });
        }

        private static void Unit(UnitDefinition u, float sight, ArmourClass cls, BuildingDefinition required, string description)
        {
            EconomyContentBuilder.Set(u, so =>
            {
                so.FindProperty("sightRange").floatValue = sight;
                so.FindProperty("armourClass").enumValueIndex = (int)cls;
                so.FindProperty("requiredStructure").objectReferenceValue = required;
                so.FindProperty("description").stringValue = description;
            });
        }

        private static void Building(BuildingDefinition b, float sight, WeaponDefinition weapon, bool needsPower, string description)
        {
            EconomyContentBuilder.Set(b, so =>
            {
                so.FindProperty("sightRange").floatValue = sight;
                so.FindProperty("weapon").objectReferenceValue = weapon;
                so.FindProperty("needsPower").boolValue = needsPower;
                so.FindProperty("description").stringValue = description;
            });
        }

        private static void Research(ResearchDefinition r, string id, string name, int cost, float time, UpgradeEffect effect, float value,
            ResearchDefinition requires, string description)
        {
            EconomyContentBuilder.Set(r, so =>
            {
                so.FindProperty("id").stringValue = id;
                so.FindProperty("displayName").stringValue = name;
                so.FindProperty("cost").intValue = cost;
                so.FindProperty("researchTime").floatValue = time;
                so.FindProperty("effect").enumValueIndex = (int)effect;
                so.FindProperty("value").floatValue = value;
                so.FindProperty("description").stringValue = description;
                if (requires != null) EconomyContentBuilder.SetList(so.FindProperty("requires"), requires);
                else so.FindProperty("requires").arraySize = 0;
            });
        }

        // ---------- Prefabs ----------

        public static void CreatePrefabs(Defs d, EconomyContentBuilder.Palette pal)
        {
            var a = EconomyContentBuilder.Art;
            string units = RtsSandboxBuilder.PrefabsDir + "/Units";
            string buildings = RtsSandboxBuilder.PrefabsDir + "/Buildings";

            var recon = EconomyContentBuilder.NewVehicle("Recon", d.Recon, new Vector3(1.3f, 1.2f, 2.0f), 0.65f, 12f, pal, out _);
            var rp = ExpansionModels.Recon(recon.transform, a);
            CombatContentBuilder.AddCombatUnit(recon, rp.TurretPivot, rp.Muzzle);
            CombatContentBuilder.AddTankFx(recon, rp, recoil: 0.05f, flash: 0.4f, SoundId.MachineGun);
            EconomyContentBuilder.SetPrefab(d.Recon, EconomyContentBuilder.SavePrefab(recon, units + "/Recon.prefab"));

            var art = EconomyContentBuilder.NewVehicle("RocketArtillery", d.Artillery, new Vector3(1.8f, 1.6f, 3.0f), 0.9f, 5.5f, pal, out _);
            var ap = ExpansionModels.RocketArtillery(art.transform, a);
            CombatContentBuilder.AddCombatUnit(art, ap.TurretPivot, ap.Muzzle);
            CombatContentBuilder.AddTankFx(art, ap, recoil: 0.0f, flash: 1.2f, SoundId.RocketLaunch);
            EconomyContentBuilder.Set(art.GetComponent<HealthBar>(), so => { so.FindProperty("height").floatValue = 2.4f; so.FindProperty("width").floatValue = 2f; });
            EconomyContentBuilder.SetPrefab(d.Artillery, EconomyContentBuilder.SavePrefab(art, units + "/RocketArtillery.prefab"));

            var tower = EconomyContentBuilder.NewStructure("GuardTower", d.GuardTower, 4.2f, pal, out _);
            var tp = ExpansionModels.GuardTower(tower.transform, a);
            AddDefence(tower, tp, SoundId.MachineGun, flash: 0.5f, recoil: 0.06f, damagePoint: new Vector3(0f, 3.2f, 0f));
            EconomyContentBuilder.SetPrefab(d.GuardTower, EconomyContentBuilder.SavePrefab(tower, buildings + "/GuardTower.prefab"));

            var at = EconomyContentBuilder.NewStructure("AntiArmourTurret", d.AtTurret, 2.2f, pal, out _);
            var atp = ExpansionModels.AntiArmourTurret(at.transform, a);
            AddDefence(at, atp, SoundId.HeavyCannon, flash: 1.4f, recoil: 0.3f, damagePoint: new Vector3(0f, 1.6f, 0f));
            EconomyContentBuilder.SetPrefab(d.AtTurret, EconomyContentBuilder.SavePrefab(at, buildings + "/AntiArmourTurret.prefab"));

            var lab = EconomyContentBuilder.NewStructure("ResearchCentre", d.ResearchCentre, 3.4f, pal, out _);
            lab.AddComponent<ResearchCentre>();
            EconomyContentBuilder.FinishStructure(lab, ExpansionModels.ResearchCentre(lab.transform, a), new[] { new Vector3(0f, 2.6f, 0f) });
            EconomyContentBuilder.SetPrefab(d.ResearchCentre, EconomyContentBuilder.SavePrefab(lab, buildings + "/ResearchCentre.prefab"));
        }

        private static void AddDefence(GameObject root, ExpansionModels.TurretParts parts, SoundId sound, float flash, float recoil, Vector3 damagePoint)
        {
            EconomyContentBuilder.FinishStructure(root, parts.Structure, new[] { damagePoint });
            var turret = root.AddComponent<DefenceTurret>();
            RtsSandboxBuilder.SetRef(turret, "turret", parts.Head);
            RtsSandboxBuilder.SetRef(turret, "muzzle", parts.Muzzle);
            RtsSandboxBuilder.SetInt(turret, "lineOfSightBlockers", 1 << 0);
            var fx = root.AddComponent<WeaponFx>();
            EconomyContentBuilder.Set(fx, so =>
            {
                so.FindProperty("barrel").objectReferenceValue = parts.Barrel;
                so.FindProperty("muzzle").objectReferenceValue = parts.Muzzle;
                so.FindProperty("recoilDistance").floatValue = recoil;
                so.FindProperty("flashScale").floatValue = flash;
                so.FindProperty("fireSound").enumValueIndex = (int)sound;
            });
            root.AddComponent<RangeIndicator>();
        }
    }
}
