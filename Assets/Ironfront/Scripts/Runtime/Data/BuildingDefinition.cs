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

        public Footprint FootprintAt(Vector3 worldCenter) =>
            Footprint.FromCenter(new Vector2(worldCenter.x, worldCenter.z), footprintSize, apronDepth);
    }
}
