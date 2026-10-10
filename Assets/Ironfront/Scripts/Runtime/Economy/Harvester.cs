using System;
using Ironfront.Buildings;
using Ironfront.Core.Harvesting;
using Ironfront.Data;
using Ironfront.Match;
using Ironfront.Units;
using UnityEngine;

namespace Ironfront.Economy
{
    /// <summary>
    /// Connects a <see cref="HarvesterStateMachine"/> to the scene: movement via <see cref="UnitMover"/>,
    /// deposits and refineries via their registries. Harvests automatically from Start.
    /// </summary>
    [RequireComponent(typeof(UnitMover))]
    public class Harvester : MonoBehaviour, IHarvesterBody
    {
        [SerializeField] private HarvesterDefinition definition;
        [SerializeField] private bool harvestOnStart = true;
        [Tooltip("Scaled vertically with the cargo load.")]
        [SerializeField] private Transform cargoVisual;
        [Tooltip("Extra distance beyond a deposit's harvest radius / from a dock that still counts as arrived.")]
        [SerializeField] private float arrivalTolerance = 1.5f;

        private UnitMover _mover;
        private Vector3 _cargoBaseScale = Vector3.one;

        public HarvesterStateMachine Machine { get; private set; }
        public HarvesterDefinition Definition => definition;
        public HarvesterState State => Machine.State;

        /// <summary>Raised when the harvest state changes.</summary>
        public event Action<HarvesterState> StateChanged;

        public string StatusText
        {
            get
            {
                string load = $"{Machine.Cargo}/{Machine.Capacity}";
                switch (Machine.State)
                {
                    case HarvesterState.Idle: return $"Idle (cargo {load})";
                    case HarvesterState.MovingToSource: return $"Moving to ore (cargo {load})";
                    case HarvesterState.Harvesting: return $"Harvesting {load}";
                    case HarvesterState.MovingToSink: return $"Returning to refinery ({load})";
                    case HarvesterState.Unloading: return $"Unloading ({load})";
                    case HarvesterState.NoSourceAvailable: return "Waiting: no ore left";
                    case HarvesterState.NoSinkAvailable: return $"Waiting: no refinery ({load})";
                    default: return Machine.State.ToString();
                }
            }
        }

        private void Awake()
        {
            _mover = GetComponent<UnitMover>();
            if (definition == null)
            {
                Debug.LogError($"[Ironfront] {name} has no HarvesterDefinition.", this);
                enabled = false;
                return;
            }
            Machine = new HarvesterStateMachine(definition.CargoCapacity, definition.HarvestRate, definition.UnloadRate);
            Machine.StateChanged += s => StateChanged?.Invoke(s);
            if (cargoVisual != null) _cargoBaseScale = cargoVisual.localScale;
            UpdateCargoVisual();
        }

        private void Start()
        {
            if (harvestOnStart) Machine.StartHarvesting(this);
        }

        private void Update()
        {
            Machine.Tick(Time.deltaTime, this);
            UpdateCargoVisual();
        }

        /// <summary>Player order: harvest from this deposit (and keep cycling).</summary>
        public void OrderHarvest(OreDeposit deposit) => Machine.StartHarvesting(this, deposit);

        /// <summary>Player order overrides automation (e.g. a move order). Cargo is kept.</summary>
        public void StopHarvesting() => Machine.Stop();

        private void UpdateCargoVisual()
        {
            if (cargoVisual == null || Machine == null) return;
            float t = Machine.Capacity > 0 ? (float)Machine.Cargo / Machine.Capacity : 0f;
            cargoVisual.gameObject.SetActive(t > 0f);
            var s = _cargoBaseScale;
            s.y *= Mathf.Max(0.05f, t);
            cargoVisual.localScale = s;
        }

        // ---- IHarvesterBody ----

        public IOreSource FindNearestSource() => OreDeposit.FindNearest(transform.position);

        public IOreSink FindNearestSink() => Refinery.FindNearest(transform.position, FactionMember.Of(this));

        public void MoveTo(IOreSource source)
        {
            if (source is OreDeposit deposit) _mover.MoveTo(ApproachPoint(deposit));
        }

        public void MoveTo(IOreSink sink)
        {
            if (sink is Refinery refinery) _mover.MoveTo(refinery.DockPoint);
        }

        public bool IsAt(IOreSource source)
        {
            if (!(source is OreDeposit deposit)) return false;
            return FlatDistance(transform.position, deposit.transform.position) <= deposit.HarvestRadius + arrivalTolerance;
        }

        public bool IsAt(IOreSink sink)
        {
            if (!(sink is Refinery refinery)) return false;
            return FlatDistance(transform.position, refinery.DockPoint) <= arrivalTolerance;
        }

        private Vector3 ApproachPoint(OreDeposit deposit)
        {
            Vector3 center = deposit.transform.position;
            Vector3 dir = transform.position - center;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) dir = Vector3.back;
            return center + dir.normalized * (deposit.HarvestRadius + 0.5f);
        }

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
