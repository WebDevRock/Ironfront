using Ironfront.Core.Combat;
using Ironfront.Data;
using Ironfront.EditorTools.Art;
using Ironfront.Units;
using Ironfront.Visuals;
using UnityEditor;
using UnityEngine;

namespace Ironfront.EditorTools
{
    /// <summary>
    /// Milestone 010 content: the Mobile Command Vehicle and Forward Command Centre definitions and prefabs, the
    /// <see cref="TerritorySettings"/> asset with the overlay materials, and the territory / sale fields of the existing
    /// structures. Called from <see cref="EconomyContentBuilder.CreateAssets"/> like the Milestone 009 content.
    /// </summary>
    public static class CommandContentBuilder
    {
        public const string McvPath = RtsSandboxBuilder.DataDir + "/Units/MobileCommandVehicle.asset";
        public const string CentrePath = RtsSandboxBuilder.DataDir + "/Buildings/ForwardCommandCentre.asset";
        public const string SettingsPath = RtsSandboxBuilder.DataDir + "/TerritorySettings.asset";

        public const float HqTerritory = 55f, HqExclusion = 70f;
        public const float CentreTerritory = 40f, CentreExclusion = 50f;
        public const int McvCost = 5000;

        public sealed class Defs
        {
            public UnitDefinition Mcv;
            public BuildingDefinition ForwardCommand;
            public TerritorySettings Settings;
        }

        public static Defs Load() => new Defs
        {
            Mcv = EconomyContentBuilder.Require<UnitDefinition>(McvPath),
            ForwardCommand = EconomyContentBuilder.Require<BuildingDefinition>(CentrePath),
            Settings = EconomyContentBuilder.Require<TerritorySettings>(SettingsPath),
        };

        public static Defs CreateDefinitions(EconomyContentBuilder.Content c, Shader unlit)
        {
            var d = new Defs
            {
                Mcv = EconomyContentBuilder.LoadOrCreate<UnitDefinition>(McvPath),
                ForwardCommand = EconomyContentBuilder.LoadOrCreate<BuildingDefinition>(CentrePath),
                Settings = EconomyContentBuilder.LoadOrCreate<TerritorySettings>(SettingsPath), // values are its defaults
            };

            EconomyContentBuilder.SetBuilding(d.ForwardCommand, "forward_command", "Forward Command Centre", cost: 0,
                size: new Vector2(CommandModels.CentreSize, CommandModels.CentreSize), apron: 0f, powerOut: 10, powerDrain: 0,
                constructible: false, prereqs: new BuildingDefinition[0], units: new UnitDefinition[0]);
            EconomyContentBuilder.SetBuildingCombat(d.ForwardCommand, maxHealth: 2500f, armour: 0.3f);
            EconomyContentBuilder.Set(d.ForwardCommand, so =>
            {
                so.FindProperty("sightRange").floatValue = 30f;
                so.FindProperty("territoryRadius").floatValue = CentreTerritory;
                so.FindProperty("exclusionRadius").floatValue = CentreExclusion;
                so.FindProperty("sellable").boolValue = true;
                so.FindProperty("sellConfirmation").boolValue = true;
                so.FindProperty("saleValueOverride").intValue = McvCost;
                so.FindProperty("description").stringValue =
                    "Deployed from a Mobile Command Vehicle. Grants construction territory around it so a forward base can be built. " +
                    "Does not replace the Headquarters: in Headquarters mode losing the HQ still loses the match.";
            });

            EconomyContentBuilder.SetUnit(d.Mcv, "mcv", "Mobile Command Vehicle", cost: McvCost, buildTime: 45f);
            EconomyContentBuilder.SetUnitCombat(d.Mcv, maxHealth: 2000f, armour: 0.35f, moveSpeed: 3.5f, weapon: null);
            EconomyContentBuilder.Set(d.Mcv, so =>
            {
                so.FindProperty("sightRange").floatValue = 16f;
                so.FindProperty("armourClass").enumValueIndex = (int)ArmourClass.Heavy;
                so.FindProperty("description").stringValue =
                    "Slow, unarmed and heavily armoured. Drive it to a new site and press Deploy [T]: after 15 s it becomes a Forward " +
                    "Command Centre with its own construction territory. Can deploy outside your territory but not inside known enemy territory.";
            });

            // Territory and sale rules of the existing structures.
            EconomyContentBuilder.Set(c.Headquarters, so =>
            {
                so.FindProperty("territoryRadius").floatValue = HqTerritory;
                so.FindProperty("exclusionRadius").floatValue = HqExclusion;
                so.FindProperty("sellable").boolValue = false; // the victory condition in Headquarters mode
            });
            foreach (var critical in new[] { c.VehicleFactory, c.Refinery })
                EconomyContentBuilder.Set(critical, so => so.FindProperty("sellConfirmation").boolValue = true);

            EconomyContentBuilder.Set(d.Settings, so =>
            {
                so.FindProperty("fillMaterial").objectReferenceValue = EconomyContentBuilder.CreateTransparent("TerritoryFill", unlit, new Color(0.25f, 0.85f, 1f, 0.18f));
                so.FindProperty("edgeMaterial").objectReferenceValue = EconomyContentBuilder.CreateTransparent("TerritoryEdge", unlit, new Color(0.45f, 0.95f, 1f, 0.95f));
                so.FindProperty("enemyEdgeMaterial").objectReferenceValue = EconomyContentBuilder.CreateTransparent("TerritoryEnemyEdge", unlit, new Color(1f, 0.3f, 0.2f, 0.9f));
            });
            return d;
        }

        /// <summary>Marks the Research Centre (Milestone 007 content) as critical for selling.</summary>
        public static void MarkCritical(BuildingDefinition researchCentre)
        {
            if (researchCentre != null) EconomyContentBuilder.Set(researchCentre, so => so.FindProperty("sellConfirmation").boolValue = true);
        }

        public static void CreatePrefabs(Defs d, EconomyContentBuilder.Palette pal)
        {
            var a = EconomyContentBuilder.Art;

            // Forward Command Centre.
            var centre = EconomyContentBuilder.NewStructure("ForwardCommandCentre", d.ForwardCommand, 3.6f, pal, out _);
            var parts = CommandModels.ForwardCommand(centre.transform, a);
            EconomyContentBuilder.FinishStructure(centre, parts, new[] { new Vector3(1.5f, 2.2f, 1.5f), new Vector3(-1.2f, 3.2f, -0.6f) });
            EconomyContentBuilder.SetPrefab(d.ForwardCommand,
                EconomyContentBuilder.SavePrefab(centre, RtsSandboxBuilder.PrefabsDir + "/Buildings/ForwardCommandCentre.prefab"));

            // Mobile Command Vehicle.
            var root = EconomyContentBuilder.NewVehicle("MobileCommandVehicle", d.Mcv,
                new Vector3(CommandModels.McvWidth, CommandModels.McvHeight, CommandModels.McvLength), 1.7f, 3.5f, pal, out _);
            var m = CommandModels.Mcv(root.transform, a);
            var mcv = root.AddComponent<MobileCommandVehicle>();
            RtsSandboxBuilder.SetRef(mcv, "deploysInto", d.ForwardCommand);
            var fx = root.AddComponent<McvFx>();
            EconomyContentBuilder.Set(fx, so =>
            {
                so.FindProperty("vehicle").objectReferenceValue = mcv;
                so.FindProperty("module").objectReferenceValue = m.Module;
                so.FindProperty("mast").objectReferenceValue = m.Mast;
                so.FindProperty("beacon").objectReferenceValue = m.Beacon;
                EconomyContentBuilder.SetList(so.FindProperty("outriggers"), m.Outriggers);
            });
            var motion = root.AddComponent<VehicleMotionFx>();
            EconomyContentBuilder.Set(motion, so =>
            {
                so.FindProperty("hull").objectReferenceValue = m.Body;
                EconomyContentBuilder.SetList(so.FindProperty("dustPoints"), m.DustPoints);
                so.FindProperty("dustSize").floatValue = 1.6f;
                so.FindProperty("maxPitch").floatValue = 1.5f;
            });
            EconomyContentBuilder.SetWreck(root, m.Body);
            EconomyContentBuilder.SetPrefab(d.Mcv,
                EconomyContentBuilder.SavePrefab(root, RtsSandboxBuilder.PrefabsDir + "/Units/MobileCommandVehicle.prefab"));
        }
    }
}
