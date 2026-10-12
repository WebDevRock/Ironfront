using Ironfront.Core.Combat;
using Ironfront.Core.Production;
using UnityEngine;

namespace Ironfront.Data
{
    /// <summary>Data for a producible unit type: cost, build time and the prefab to spawn.</summary>
    [CreateAssetMenu(menuName = "Ironfront/Unit Definition", fileName = "UnitDefinition")]
    public class UnitDefinition : ScriptableObject, IProducible
    {
        [SerializeField] private string id = "unit";
        [SerializeField] private string displayName = "Unit";
        [SerializeField, Min(0)] private int cost = 100;
        [SerializeField, Min(0.1f)] private float buildTime = 5f;
        [SerializeField] private GameObject prefab;

        [Header("Combat")]
        [SerializeField, Min(1f)] private float maxHealth = 100f;
        [Tooltip("Fraction of incoming damage absorbed (0 = none, max 0.9).")]
        [SerializeField, Range(0f, 0.9f)] private float armour;
        [Tooltip("NavMeshAgent speed in m/s. 0 keeps the prefab's agent speed.")]
        [SerializeField, Min(0f)] private float moveSpeed;
        [Tooltip("Leave empty for unarmed units.")]
        [SerializeField] private WeaponDefinition weapon;

        [Header("Presentation")]
        [Tooltip("Sidebar / selection portrait (generated from the model).")]
        [SerializeField] private Sprite icon;
        [TextArea] [SerializeField] private string description = "";

        [Header("Milestone 007")]
        [Tooltip("How far the unit reveals the fog of war (metres).")]
        [SerializeField, Min(1f)] private float sightRange = 14f;
        [SerializeField] private ArmourClass armourClass = ArmourClass.Light;
        [Tooltip("A structure the faction must own before this unit can be queued (e.g. a Research Centre).")]
        [SerializeField] private BuildingDefinition requiredStructure;
        [Tooltip("Scales this vehicle's repair cost (Milestone 009; 1 = the RepairSettings cost).")]
        [SerializeField, Min(0f)] private float repairCostMultiplier = 1f;

        public string Id => id;
        public string DisplayName => displayName;
        public int Cost => cost;
        public float BuildTime => buildTime;
        public GameObject Prefab => prefab;
        public float MaxHealth => maxHealth;
        public float Armour => armour;
        public float MoveSpeed => moveSpeed;
        public WeaponDefinition Weapon => weapon;
        public Sprite Icon => icon;
        public string Description => description;
        public float SightRange => sightRange;
        public ArmourClass ArmourClass => armourClass;
        public BuildingDefinition RequiredStructure => requiredStructure;
        public float RepairCostMultiplier => repairCostMultiplier;
    }
}
