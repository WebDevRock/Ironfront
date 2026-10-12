using System;
using System.Collections.Generic;
using Ironfront.Buildings;
using Ironfront.Combat;
using Ironfront.Core.Repair;
using Ironfront.Data;
using Ironfront.Economy;
using Ironfront.Match;
using UnityEngine;
using UnityEngine.AI;

namespace Ironfront.Units
{
    /// <summary>
    /// A vehicle's repair order (Milestone 009): wraps a <see cref="RepairOrderMachine"/> and drives the vehicle only
    /// through its normal controls — <see cref="CombatUnit"/> move/hold/stop orders for armed vehicles, the harvester's
    /// own stop plus <see cref="UnitMover"/> for harvesters. Added on demand to the vehicle that receives a repair order.
    /// Any other order given to the vehicle (by the player, the computer or a script) cancels the repair.
    /// </summary>
    [DisallowMultipleComponent]
    public class RepairClient : MonoBehaviour, IRepairVehicle
    {
        private static readonly Dictionary<int, RepairClient> s_byId = new Dictionary<int, RepairClient>();
        private static int s_nextId;
        /// <summary>True while a repair client issues an order itself (so the order does not cancel the repair).</summary>
        private static bool s_issuing;

        private Health _health;
        private CombatUnit _combat;
        private Harvester _harvester;
        private UnitMover _mover;
        private RepairOrderMachine _machine;
        private int _id;

        public int Id => _id;
        public RepairOrderMachine Machine => _machine;
        public RepairState State => _machine != null ? _machine.State : RepairState.None;
        public bool IsBusy => _machine != null && _machine.IsActive;
        public RepairPad Pad => _machine != null ? _machine.Pad as RepairPad : null;
        public Health Health => _health;
        /// <summary>0-based place in the waiting line, -1 when not waiting.</summary>
        public int QueuePosition => IsBusy && Pad != null ? Pad.Queue.PositionOf(_id) : -1;

        /// <summary>Raised when a repair order ends (completed or cancelled), with the reason.</summary>
        public static event Action<RepairClient, RepairState, string> AnyFinished;

        public static RepairClient Find(int id) => s_byId.TryGetValue(id, out var c) ? c : null;

        public static RepairClient Of(Component unit) => unit != null ? unit.GetComponent<RepairClient>() : null;

        private void Awake()
        {
            _health = GetComponent<Health>();
            _combat = GetComponent<CombatUnit>();
            _harvester = GetComponent<Harvester>();
            _mover = GetComponent<UnitMover>();
            _id = ++s_nextId;
            s_byId[_id] = this;
            _machine = new RepairOrderMachine(_id, this, RepairSettings.Current.Timeouts);
            _machine.StateChanged += OnStateChanged;
        }

        private void OnDestroy()
        {
            // Destroyed (killed or unloaded): never leave a ticket or a bay reservation behind.
            if (_machine != null && _machine.IsActive && _machine.Pad != null) _machine.Pad.Queue.Remove(_id, gone: true);
            s_byId.Remove(_id);
        }

        /// <summary>Can this object be sent to a Repair Pad by <paramref name="faction"/>? (friendly, alive ground vehicle)</summary>
        public static bool IsRepairableVehicle(Component unit, int faction)
        {
            if (unit == null) return false;
            var h = unit.GetComponent<Health>();
            if (h == null || !h.IsAlive || h.IsStructure || h.FactionId != faction) return false;
            var mcv = unit.GetComponent<MobileCommandVehicle>(); // Milestone 010: a deploying MCV cannot move
            if (mcv != null && mcv.IsDeploying) return false;
            return unit.GetComponent<UnitMover>() != null && unit.GetComponent<UnitIdentity>() != null;
        }

        public static bool IsDamaged(Component unit)
        {
            var h = unit != null ? unit.GetComponent<Health>() : null;
            return h != null && h.IsAlive && h.Current < h.Max - 0.01f;
        }

        /// <summary>
        /// Sends the vehicle to <paramref name="pad"/>. A vehicle already going to another pad is transferred (its old
        /// ticket is removed first); one already queued at this pad keeps its place. Returns false when refused.
        /// </summary>
        public static bool Request(Component unit, RepairPad pad, out RepairClient client)
        {
            client = null;
            if (unit == null || pad == null || !pad.IsAlive || !IsRepairableVehicle(unit, pad.FactionId) || !IsDamaged(unit)) return false;
            client = unit.GetComponent<RepairClient>();
            if (client == null) client = unit.gameObject.AddComponent<RepairClient>();
            if (client.IsBusy)
            {
                if (client.Pad == pad) return true; // already queued here
                client._machine.Cancel("transferred to another Repair Pad");
            }
            return client._machine.Start(pad);
        }

        /// <summary>Cancels an outstanding repair order (cancel button, AI, tests).</summary>
        public void Cancel(string reason)
        {
            if (IsBusy) _machine.Cancel(reason);
        }

        /// <summary>Called by vehicle order entry points: a new order from anyone but the repair client cancels the repair.</summary>
        public static void NotifyExternalOrder(Component unit)
        {
            if (s_issuing || unit == null) return;
            var c = unit.GetComponent<RepairClient>();
            if (c != null && c.IsBusy) c._machine.Cancel("given another order");
        }

        private void Update()
        {
            if (_machine == null || !_machine.IsActive) return;
            var flow = MatchFlowController.Current;
            if (flow != null && flow.IsOver) return;
            _machine.Tick(Time.deltaTime);
        }

        private void OnStateChanged(RepairState state)
        {
            if (state != RepairState.Completed && state != RepairState.Cancelled) return;
            // Back to normal duty: armed vehicles idle (and engage as usual), harvesters return to work after a repair.
            if (state == RepairState.Completed && _harvester != null && _health != null && _health.IsAlive)
            {
                var source = _harvester.FindNearestSource() as OreDeposit;
                if (source != null) Issue(() => _harvester.OrderHarvest(source));
            }
            AnyFinished?.Invoke(this, state, _machine.Reason);
        }

        private static void Issue(Action order)
        {
            bool previous = s_issuing;
            s_issuing = true;
            try { order(); }
            finally { s_issuing = previous; }
        }

        // ---------- IRepairVehicle ----------

        public Vector3 Position => transform.position;
        public bool IsAlive => this != null && _health != null && _health.IsAlive;
        public bool IsFullHealth => _health != null && _health.Current >= _health.Max - 0.01f;

        public bool MoveTo(Vector3 point)
        {
            bool ok = false;
            Issue(() =>
            {
                if (_combat != null && _combat.Machine != null)
                {
                    _combat.OrderMove(point);
                    ok = true;
                }
                else
                {
                    if (_harvester != null) _harvester.StopHarvesting();
                    ok = _mover != null && _mover.MoveTo(point);
                }
            });
            return ok;
        }

        public void HoldStill()
        {
            Issue(() =>
            {
                if (_combat != null && _combat.Machine != null) _combat.OrderHold();
                else
                {
                    if (_harvester != null) _harvester.StopHarvesting();
                    if (_mover != null) _mover.Stop();
                }
            });
        }

        public bool HasArrived => _mover == null || _mover.HasArrived;

        public bool RouteBlocked
        {
            get
            {
                var agent = _mover != null ? _mover.Agent : null;
                return agent != null && agent.isOnNavMesh && !agent.pathPending && agent.hasPath && agent.pathStatus != NavMeshPathStatus.PathComplete;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_byId.Clear();
            s_nextId = 0;
            s_issuing = false;
            AnyFinished = null;
        }
    }
}
