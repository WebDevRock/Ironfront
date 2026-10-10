using UnityEngine;

namespace Ironfront.Data
{
    /// <summary>Unit definition with the harvesting parameters used by harvesters.</summary>
    [CreateAssetMenu(menuName = "Ironfront/Harvester Definition", fileName = "HarvesterDefinition")]
    public class HarvesterDefinition : UnitDefinition
    {
        [Header("Harvesting")]
        [SerializeField, Min(1)] private int cargoCapacity = 200;
        [SerializeField, Min(0.1f)] private float harvestRate = 50f;
        [SerializeField, Min(0.1f)] private float unloadRate = 100f;

        public int CargoCapacity => cargoCapacity;
        /// <summary>Ore per second while harvesting.</summary>
        public float HarvestRate => harvestRate;
        /// <summary>Ore per second while unloading.</summary>
        public float UnloadRate => unloadRate;
    }
}
