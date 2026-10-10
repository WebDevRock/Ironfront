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

        private const float FallbackSearchFactor = 4f;

        /// <summary>Total path requests issued by all units since load (diagnostics for path-spam checks).</summary>
        public static long PathRequests { get; private set; }

        private NavMeshAgent _agent;
        private bool _started;
        private Vector3? _pendingDestination;

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

            _started = true;
            if (!EnsureOnNavMesh())
                Debug.LogWarning($"[Ironfront] {name} could not be placed on the NavMesh at {transform.position}.", this);

            if (_pendingDestination.HasValue)
            {
                var destination = _pendingDestination.Value;
                _pendingDestination = null;
                MoveTo(destination);
            }
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
        /// <remarks>
        /// Orders given before Start (e.g. to a freshly spawned unit) are applied once the agent is enabled.
        /// </remarks>
        public bool MoveTo(Vector3 destination)
        {
            using var perfScope = Ironfront.Diagnostics.PerfMarkers.PathRequest.Auto();
            if (!_started)
            {
                _pendingDestination = destination;
                return true;
            }

            var agent = Agent;
            if (agent == null || !EnsureOnNavMesh()) return false;

            // A point deep inside a structure's carved footprint (e.g. attack-move onto a factory) is further than the
            // normal radius from any walkable surface: widen the search once so the unit goes to the nearest edge
            // instead of silently ignoring the order.
            if (!NavMesh.SamplePosition(destination, out var hit, destinationSearchRadius, agent.areaMask) &&
                !NavMesh.SamplePosition(destination, out hit, destinationSearchRadius * FallbackSearchFactor, agent.areaMask))
                return false;

            agent.isStopped = false;
            PathRequests++;
            return agent.SetDestination(hit.position);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => PathRequests = 0;

        public void Stop()
        {
            _pendingDestination = null;
            var agent = Agent;
            if (agent != null && agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.ResetPath();
            }
        }
    }
}
