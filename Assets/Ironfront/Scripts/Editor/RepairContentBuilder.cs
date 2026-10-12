using Ironfront.Buildings;
using Ironfront.Data;
using Ironfront.EditorTools.Art;
using Ironfront.Visuals;
using UnityEditor;
using UnityEngine;

namespace Ironfront.EditorTools
{
    /// <summary>
    /// Milestone 009 content: the Repair Pad definition and prefab, and the <see cref="RepairSettings"/> asset the match
    /// setup references. Called from <see cref="EconomyContentBuilder.CreateAssets"/> like the Milestone 007 content.
    /// </summary>
    public static class RepairContentBuilder
    {
        public const string PadPath = RtsSandboxBuilder.DataDir + "/Buildings/RepairPad.asset";
        public const string SettingsPath = RtsSandboxBuilder.DataDir + "/RepairSettings.asset";

        public sealed class Defs
        {
            public BuildingDefinition RepairPad;
            public RepairSettings Settings;
        }

        /// <summary>
        /// Regenerates all generated content (definitions, prefabs, the match setup, sounds, sprites and icons) without
        /// rebuilding any scene. Menu: Ironfront > Regenerate Content.
        /// </summary>
        [MenuItem("Ironfront/Regenerate Content")]
        public static string RegenerateContent()
        {
            var mats = RtsSandboxBuilder.CreateMaterials();
            RtsSandboxBuilder.CreateUnitPrefab(mats["SelectionRing"]);
            EconomyContentBuilder.CreateAssets(mats["SelectionRing"]);
            RtsSandboxBuilder.GeneratePresentationAssets();
            AssetDatabase.SaveAssets();
            Debug.Log("[Ironfront] Content regenerated");
            return "ok";
        }

        public static Defs Load() => new Defs
        {
            RepairPad = EconomyContentBuilder.Require<BuildingDefinition>(PadPath),
            Settings = EconomyContentBuilder.Require<RepairSettings>(SettingsPath),
        };

        public static Defs CreateDefinitions(EconomyContentBuilder.Content c)
        {
            var d = new Defs
            {
                RepairPad = EconomyContentBuilder.LoadOrCreate<BuildingDefinition>(PadPath),
                Settings = EconomyContentBuilder.LoadOrCreate<RepairSettings>(SettingsPath), // values are its defaults; balance in the asset
            };
            EconomyContentBuilder.SetBuilding(d.RepairPad, "repair_pad", "Repair Pad", cost: 1000,
                size: new Vector2(RepairModels.BodyWidth, RepairModels.BodyDepth), apron: RepairModels.Apron,
                powerOut: 0, powerDrain: 20, constructible: true, prereqs: new[] { c.VehicleFactory }, units: new UnitDefinition[0]);
            EconomyContentBuilder.SetBuildingCombat(d.RepairPad, maxHealth: 900f, armour: 0.2f);
            EconomyContentBuilder.Set(d.RepairPad, so =>
            {
                so.FindProperty("sightRange").floatValue = 10f;
                so.FindProperty("needsPower").boolValue = true;
                so.FindProperty("description").stringValue =
                    "Repairs one friendly vehicle at a time on its bay; others queue beside it. Select damaged vehicles and " +
                    "press Repair [R] or right-click the pad. Costs credits per health restored; pauses on low power.";
            });
            return d;
        }

        public static void CreatePrefab(Defs d, EconomyContentBuilder.Palette pal)
        {
            var a = EconomyContentBuilder.Art;
            var root = EconomyContentBuilder.NewStructure("RepairPad", d.RepairPad, 3.0f, pal, out _);
            var pad = root.AddComponent<RepairPad>();
            var parts = RepairModels.RepairPad(root.transform, a);
            var fx = root.AddComponent<RepairPadFx>();
            EconomyContentBuilder.Set(fx, so =>
            {
                so.FindProperty("pad").objectReferenceValue = pad;
                so.FindProperty("gantry").objectReferenceValue = parts.Gantry;
                so.FindProperty("trolley").objectReferenceValue = parts.Trolley;
                EconomyContentBuilder.SetList(so.FindProperty("arms"), parts.Arms);
                EconomyContentBuilder.SetList(so.FindProperty("sparkPoints"), parts.SparkPoints);
                so.FindProperty("lamp").objectReferenceValue = parts.Lamp;
                so.FindProperty("bayRing").objectReferenceValue = parts.BayRing;
            });
            EconomyContentBuilder.FinishStructure(root, parts.Parts, new[] { new Vector3(-1.5f, 2.2f, 0f), new Vector3(1.8f, 2.4f, -1f) });
            EconomyContentBuilder.SetPrefab(d.RepairPad,
                EconomyContentBuilder.SavePrefab(root, RtsSandboxBuilder.PrefabsDir + "/Buildings/RepairPad.prefab"));
        }
    }
}
