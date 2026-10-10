using Ironfront.Combat;
using UnityEngine;

namespace Ironfront.Testing
{
    /// <summary>Test-scenario helper (not AI): gives a pre-placed unit its initial combat order.</summary>
    [RequireComponent(typeof(CombatUnit))]
    public class ScenarioStartOrder : MonoBehaviour
    {
        public enum StartOrder
        {
            Idle,
            Hold,
        }

        [SerializeField] private StartOrder order = StartOrder.Hold;

        private void Start()
        {
            var unit = GetComponent<CombatUnit>();
            if (order == StartOrder.Hold) unit.OrderHold();
        }
    }
}
