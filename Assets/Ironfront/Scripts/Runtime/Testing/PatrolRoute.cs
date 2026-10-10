using Ironfront.Combat;
using Ironfront.Core.Combat;
using UnityEngine;

namespace Ironfront.Testing
{
    /// <summary>
    /// Test-scenario helper (not AI): drives a unit back and forth between waypoints with plain move orders,
    /// providing a moving target for combat testing. Stops patrolling if the unit is given any other order.
    /// </summary>
    [RequireComponent(typeof(CombatUnit))]
    public class PatrolRoute : MonoBehaviour
    {
        [SerializeField] private Vector3[] waypoints = new Vector3[0];
        [SerializeField] private float startDelay = 0.5f;

        private CombatUnit _unit;
        private int _index;
        private bool _started;

        public Vector3[] Waypoints => waypoints;

        private void Awake() => _unit = GetComponent<CombatUnit>();

        private void Update()
        {
            if (waypoints.Length < 2 || _unit.Machine == null) return;

            if (!_started)
            {
                startDelay -= Time.deltaTime;
                if (startDelay > 0f) return;
                _started = true;
                _unit.OrderMove(waypoints[_index]);
                return;
            }

            if (_unit.Machine.Order == CombatOrderType.Idle)
            {
                _index = (_index + 1) % waypoints.Length;
                _unit.OrderMove(waypoints[_index]);
            }
            else if (_unit.Machine.Order != CombatOrderType.Move)
            {
                enabled = false; // someone gave a real order; stop patrolling
            }
        }
    }
}
