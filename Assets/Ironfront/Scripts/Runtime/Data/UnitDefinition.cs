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

        public string Id => id;
        public string DisplayName => displayName;
        public int Cost => cost;
        public float BuildTime => buildTime;
        public GameObject Prefab => prefab;
    }
}
