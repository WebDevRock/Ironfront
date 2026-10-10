using Ironfront.Data;
using UnityEngine;
using UnityEngine.AI;

namespace Ironfront.Units
{
    /// <summary>Links a unit instance to its <see cref="UnitDefinition"/> and applies the definition's movement speed.</summary>
    [DisallowMultipleComponent]
    public class UnitIdentity : MonoBehaviour
    {
        [SerializeField] private UnitDefinition definition;

        public UnitDefinition Definition => definition;
        public string DisplayName => definition != null ? definition.DisplayName : name;

        private void Awake()
        {
            if (definition == null || definition.MoveSpeed <= 0f) return;
            var agent = GetComponent<NavMeshAgent>();
            if (agent != null) agent.speed = definition.MoveSpeed;
        }
    }
}
