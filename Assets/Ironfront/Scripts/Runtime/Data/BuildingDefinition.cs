using System.Collections.Generic;
using System.Linq;
using Ironfront.Core.Construction;
using Ironfront.Core.Economy;
using UnityEngine;

namespace Ironfront.Data
{
    /// <summary>Data for a structure type: cost, footprint, power and what it unlocks or produces.</summary>
    [CreateAssetMenu(menuName = "Ironfront/Building Definition", fileName = "BuildingDefinition")]
    public class BuildingDefinition : ScriptableObject, IConstructable
    {
        [SerializeField] private string id = "building";
        [SerializeField] private string displayName = "Building";
        [SerializeField, Min(0)] private int cost = 500;
        [Tooltip("Width (X) and depth (Z) of the structure in metres.")]
        [SerializeField] private Vector2 footprintSize = new Vector2(6f, 6f);
        [Tooltip("Depth of the reserved strip in front (+Z) of the structure, e.g. a dock or exit. 0 = none.")]
        [SerializeField, Min(0f)] private float apronDepth;
        [SerializeField, Min(0)] private int powerOutput;
        [SerializeField, Min(0)] private int powerDrain;
        [SerializeField] private List<BuildingDefinition> prerequisites = new List<BuildingDefinition>();
        [Tooltip("Whether the player can place this from the construction menu.")]
        [SerializeField] private bool constructible = true;
        [SerializeField] private GameObject prefab;
        [Tooltip("Units this structure can manufacture (factories only).")]
        [SerializeField] private List<UnitDefinition> producibleUnits = new List<UnitDefinition>();
        [Header("Combat")]
        [SerializeField, Min(1f)] private float maxHealth = 1000f;
        [SerializeField, Range(0f, 0.9f)] private float armour = 0.2f;
        [Header("Presentation")]
        [Tooltip("Sidebar / selection portrait (generated from the model).")]
        [SerializeField] private Sprite icon;
        [TextArea] [SerializeField] private string description = "";
        [Header("Milestone 007")]
        [Tooltip("How far the structure reveals the fog of war (metres).")]
        [SerializeField, Min(1f)] private float sightRange = 10f;
        [Tooltip("Defences: the weapon, for range indicators and tooltips.")]
        [SerializeField] private WeaponDefinition weapon;
        [Tooltip("Defences: stops firing while the owner is low on power.")]
        [SerializeField] private bool needsPower;
        [Tooltip("Scales this structure's repair cost (Milestone 009; 1 = the RepairSettings cost).")]
        [SerializeField, Min(0f)] private float repairCostMultiplier = 1f;

        public Sprite Icon => icon;
        public string Description => description;
        public float SightRange => sightRange;
        public WeaponDefinition Weapon => weapon;
        public bool NeedsPower => needsPower;
        public float RepairCostMultiplier => repairCostMultiplier;

        public string Id => id;
        public string DisplayName => displayName;
        public int Cost => cost;
        public Vector2 FootprintSize => footprintSize;
        public float ApronDepth => apronDepth;
        public int PowerOutput => powerOutput;
        public int PowerDrain => powerDrain;
        public bool Constructible => constructible;
        public GameObject Prefab => prefab;
        public IReadOnlyList<UnitDefinition> ProducibleUnits => producibleUnits;
        public IReadOnlyList<BuildingDefinition> Prerequisites => prerequisites;
        public float MaxHealth => maxHealth;
        public float Armour => armour;

        public IReadOnlyList<string> PrerequisiteIds =>
            prerequisites.Where(p => p != null).Select(p => p.Id).ToList();

        public Footprint FootprintAt(Vector3 worldCenter, bool facesSouth = false) =>
            Footprint.FromCenter(new Vector2(worldCenter.x, worldCenter.z), footprintSize, apronDepth, facesSouth);

        /// <summary>World rotation for a building facing north (+Z, default) or south (−Z).</summary>
        public static Quaternion Facing(bool facesSouth) => facesSouth ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
    }
}
