using System;
using System.Collections.Generic;
using Ironfront.Core.Economy;
using Ironfront.Core.Production;
using Ironfront.Data;
using Ironfront.Match;
using Ironfront.Units;
using UnityEngine;
using UnityEngine.AI;

namespace Ironfront.Buildings
{
    /// <summary>
    /// Produces units from its definition's producible list. Owns a production queue, spawns finished
    /// units at a free spot by its exit and sends them to the rally point.
    /// </summary>
    [RequireComponent(typeof(Building))]
    public class VehicleFactory : MonoBehaviour
    {
        private static readonly List<VehicleFactory> s_All = new List<VehicleFactory>();
        public static IReadOnlyList<VehicleFactory> All => s_All;

        /// <summary>Raised when any factory is enabled or disabled.</summary>
        public static event Action FactoriesChanged;

        [SerializeField] private GameObject rallyMarker;
        [SerializeField] private LayerMask unitLayers = 0;
        [SerializeField] private float spawnSpacing = 2.5f;
        [SerializeField] private float defaultRallyDistance = 5f;

        private Building _building;
        private Vector3? _rallyPoint;
        private int _spawnCounter;

        public ProductionQueue Queue { get; private set; }
        public Building Building => _building != null ? _building : (_building = GetComponent<Building>());
        public int FactionId => Building.FactionId;
        public Vector3 ExitPoint => Building.FrontPoint;
        public Vector3 RallyPoint => _rallyPoint ?? ExitPoint + Vector3.forward * defaultRallyDistance;

        /// <summary>Raised with each newly spawned unit.</summary>
        public event Action<GameObject> UnitProduced;

        private FactionState Faction => MatchController.Current != null ? MatchController.Current.GetFaction(FactionId) : null;

        private void Awake()
        {
            var setup = MatchController.Current != null ? MatchController.Current.Setup : null;
            Queue = new ProductionQueue(setup != null ? setup.ProductionQueueCapacity : 5);
            Queue.Completed += OnCompleted;
            if (rallyMarker != null) rallyMarker.SetActive(false);
        }

        private void OnEnable()
        {
            if (!s_All.Contains(this)) s_All.Add(this);
            FactoriesChanged?.Invoke();
        }

        private void OnDisable()
        {
            s_All.Remove(this);
            FactoriesChanged?.Invoke();
        }

        private void Update()
        {
            var faction = Faction;
            if (faction == null) return;
            var setup = MatchController.Current.Setup;
            float speed = faction.Power.IsLowPower && setup != null ? setup.LowPowerProductionMultiplier : 1f;
            Queue.Tick(Time.deltaTime, speed);

            if (rallyMarker != null)
            {
                bool show = Building.IsSelected;
                if (rallyMarker.activeSelf != show) rallyMarker.SetActive(show);
                if (show) rallyMarker.transform.position = RallyPoint;
            }
        }

        public bool CanProduce(UnitDefinition unit)
        {
            if (unit == null || Building.Definition == null) return false;
            var list = Building.Definition.ProducibleUnits;
            for (int i = 0; i < list.Count; i++)
                if (list[i] == unit) return true;
            return false;
        }

        /// <summary>Queues a unit, charging the owning faction.</summary>
        public EnqueueResult Enqueue(UnitDefinition unit)
        {
            if (!CanProduce(unit)) throw new ArgumentException($"{name} cannot produce {unit?.DisplayName}.");
            var faction = Faction;
            if (faction == null) return EnqueueResult.InsufficientFunds;
            return Queue.TryEnqueue(unit, faction.Wallet);
        }

        public bool Cancel(int index)
        {
            var faction = Faction;
            return faction != null && Queue.Cancel(index, faction.Wallet);
        }

        public void SetRallyPoint(Vector3 point)
        {
            _rallyPoint = NavMesh.SamplePosition(point, out var hit, 4f, NavMesh.AllAreas) ? hit.position : point;
        }

        private void OnCompleted(IProducible item)
        {
            var unit = item as UnitDefinition;
            if (unit == null || unit.Prefab == null)
            {
                Debug.LogError($"[Ironfront] {name} finished '{item?.DisplayName}' but it has no prefab.", this);
                return;
            }
            Spawn(unit);
        }

        /// <summary>Spawns a unit at the nearest free spot by the exit and orders it to the rally point.</summary>
        public GameObject Spawn(UnitDefinition unit)
        {
            SpawnLocator.TryFind(ExitPoint, Vector3.forward, spawnSpacing, 3, IsFree, out var spot);
            if (NavMesh.SamplePosition(spot, out var hit, spawnSpacing, NavMesh.AllAreas)) spot = hit.position;

            var go = Instantiate(unit.Prefab, spot, Quaternion.LookRotation(Vector3.forward));
            go.name = $"{unit.DisplayName}_{++_spawnCounter:00}";
            var member = go.GetComponent<FactionMember>();
            if (member != null) member.FactionId = FactionId;

            var mover = go.GetComponent<UnitMover>();
            if (mover != null) mover.MoveTo(RallyPoint);

            UnitProduced?.Invoke(go);
            return go;
        }

        private bool IsFree(Vector3 p)
        {
            if (!NavMesh.SamplePosition(p, out var hit, 1f, NavMesh.AllAreas)) return false;
            return !Physics.CheckSphere(hit.position + Vector3.up, 1.2f, unitLayers, QueryTriggerInteraction.Ignore);
        }

        public static VehicleFactory FirstOwnedBy(int factionId)
        {
            foreach (var f in s_All)
                if (f.FactionId == factionId) return f;
            return null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_All.Clear();
            FactoriesChanged = null;
        }
    }
}
