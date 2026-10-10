using UnityEngine;
using UnityEngine.AI;

namespace Ironfront.Units
{
    /// <summary>
    /// Moves a unit with a NavMeshAgent. Destinations are snapped to the nearest point on the NavMesh.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class UnitMover : MonoBehaviour
    {
        [Tooltip("How far from the requested point to search for a valid NavMesh position.")]
        [SerializeField] private float destinationSearchRadius = 4f;

        private NavMeshAgent _agent;

        public NavMeshAgent Agent => _agent != null ? _agent : (_agent = GetComponent<NavMeshAgent>());

        public bool HasArrived
        {
            get
            {
                var agent = Agent;
                if (agent == null || !agent.isOnNavMesh) return true;
                return !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.05f;
            }
        }

        private void Start()
        {
            // The unit prefab ships with its agent disabled: an agent enabled during scene load, before the
            // NavMeshSurface has registered its data, fails to bind in player builds. By Start every
            // surface's OnEnable has run, so enable it now.
            var agent = Agent;
            if (agent != null && !agent.enabled) agent.enabled = true;

            if (!EnsureOnNavMesh())
                Debug.LogWarning($"[Ironfront] {name} could not be placed on the NavMesh at {transform.position}.", this);
        }

        /// <summary>Binds the agent to the NavMesh if it is not already. Returns true when on the NavMesh.</summary>
        public bool EnsureOnNavMesh()
        {
            var agent = Agent;
            if (agent == null || !agent.isActiveAndEnabled) return false;
            if (agent.isOnNavMesh) return true;

            if (NavMesh.SamplePosition(transform.position, out var hit, destinationSearchRadius, agent.areaMask))
                agent.Warp(hit.position);
            return agent.isOnNavMesh;
        }

        /// <summary>Orders the unit to move. Returns false if no reachable NavMesh point was found.</summary>
        public bool MoveTo(Vector3 destination)
        {
            var agent = Agent;
            if (agent == null || !EnsureOnNavMesh()) return false;

            if (!NavMesh.SamplePosition(destination, out var hit, destinationSearchRadius, agent.areaMask))
                return false;

            agent.isStopped = false;
            return agent.SetDestination(hit.position);
        }

        public void Stop()
        {
            var agent = Agent;
            if (agent != null && agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.ResetPath();
            }
        }
    }
}
