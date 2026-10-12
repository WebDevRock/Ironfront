using UnityEngine;

namespace Ironfront.Core.Repair
{
    public enum RepairState
    {
        None,
        TravellingToQueue,
        Waiting,
        EnteringPad,
        Repairing,
        ExitingPad,
        Completed,
        Cancelled,
    }

    /// <summary>What the order machine needs from a vehicle (implemented over CombatUnit / Harvester / UnitMover).</summary>
    public interface IRepairVehicle
    {
        Vector3 Position { get; }
        bool IsAlive { get; }
        bool IsFullHealth { get; }
        /// <summary>Drive to a point. False when no route could be requested.</summary>
        bool MoveTo(Vector3 point);
        /// <summary>Stop and stay (armed vehicles still return fire in range).</summary>
        void HoldStill();
        bool HasArrived { get; }
        /// <summary>The current route is known to be incomplete (blocked).</summary>
        bool RouteBlocked { get; }
    }

    /// <summary>What the order machine needs from a Repair Pad.</summary>
    public interface IRepairPadSite
    {
        bool IsAlive { get; }
        RepairPadQueue Queue { get; }
        /// <summary>Waiting position for a 0-based place in the line (always a currently valid slot).</summary>
        Vector3 WaitPoint(int position);
        Vector3 BayPoint { get; }
        float BayRadius { get; }
        int ExitPointCount { get; }
        Vector3 ExitPoint(int attempt);
    }

    /// <summary>Navigation recovery limits (from RepairSettings).</summary>
    public struct RepairTimeouts
    {
        public float StallSeconds;
        public float StallDistance;
        public int MaxReroutes;
        public float TravelSeconds;
        public float EnterSeconds;
        public float ExitSeconds;
        public float ArriveRadius;
        public float BayArriveRadius;

        public static RepairTimeouts Default => new RepairTimeouts
        {
            StallSeconds = 8f, StallDistance = 1f, MaxReroutes = 3, TravelSeconds = 120f, EnterSeconds = 25f, ExitSeconds = 20f,
            ArriveRadius = 2.5f, BayArriveRadius = 1.5f,
        };
    }

    /// <summary>
    /// One vehicle's repair order at one pad (Milestone 009). Explicit states:
    /// TravellingToQueue → Waiting → EnteringPad → Repairing → ExitingPad → Completed, or Cancelled from any state.
    /// Every tick re-checks the facts (vehicle alive, pad alive, ticket still held) so unexpected transitions cannot leave
    /// it in a stale state. Never moves a vehicle except by ordinary movement orders (no teleporting). Pure.
    /// </summary>
    public sealed class RepairOrderMachine
    {
        private readonly IRepairVehicle _vehicle;
        private readonly RepairTimeouts _t;
        private Vector3 _target;
        private bool _hasTarget;
        private float _stateTime;
        private float _watchTime;
        private float _watchDistance;
        private int _reroutes;
        private int _exitAttempt;

        public int ClientId { get; }
        public IRepairPadSite Pad { get; private set; }
        public RepairState State { get; private set; } = RepairState.None;
        public string Reason { get; private set; } = "";
        public int Reroutes => _reroutes;
        public bool IsActive => State != RepairState.None && State != RepairState.Completed && State != RepairState.Cancelled;
        public bool IsFinished => State == RepairState.Completed || State == RepairState.Cancelled;

        /// <summary>Raised on every state change (presentation, HUD, AI bookkeeping).</summary>
        public event System.Action<RepairState> StateChanged;

        public RepairOrderMachine(int clientId, IRepairVehicle vehicle, RepairTimeouts timeouts)
        {
            ClientId = clientId;
            _vehicle = vehicle;
            _t = timeouts;
        }

        /// <summary>Joins the pad's queue. False if the pad is gone or the vehicle is already queued there.</summary>
        public bool Start(IRepairPadSite pad)
        {
            if (pad == null || !pad.IsAlive || IsActive) return false;
            if (!pad.Queue.Enqueue(ClientId)) return false;
            Pad = pad;
            Reason = "";
            _reroutes = 0;
            _exitAttempt = 0;
            GoToSlot(force: true);
            return true;
        }

        /// <summary>Gives up the order (new command, cancel button, transfer). The ticket is always removed.</summary>
        public void Cancel(string reason) => Finish(RepairState.Cancelled, reason, vehicleGone: false);

        public void Tick(float dt)
        {
            if (!IsActive) return;
            _stateTime += dt;
            _watchTime += dt;

            if (!_vehicle.IsAlive) { Finish(RepairState.Cancelled, "vehicle destroyed", vehicleGone: true); return; }
            if (Pad == null || !Pad.IsAlive) { Finish(RepairState.Cancelled, "Repair Pad destroyed", vehicleGone: false); return; }

            var q = Pad.Queue;
            bool holdsBay = q.Active == ClientId;
            bool queued = holdsBay || q.PositionOf(ClientId) >= 0;
            if (State != RepairState.ExitingPad && !queued)
            {
                Finish(RepairState.Cancelled, "removed from the repair queue", vehicleGone: false);
                return;
            }

            switch (State)
            {
                case RepairState.TravellingToQueue:
                case RepairState.Waiting:
                    if (holdsBay)
                    {
                        Enter(RepairState.EnteringPad);
                        Drive(Pad.BayPoint);
                        return;
                    }
                    GoToSlot(force: false);
                    if (State == RepairState.TravellingToQueue)
                    {
                        if (Dist(_target) <= _t.ArriveRadius || (_vehicle.HasArrived && Dist(_target) <= _t.ArriveRadius * 2f))
                        {
                            Enter(RepairState.Waiting);
                            _vehicle.HoldStill();
                        }
                        else if (_stateTime > _t.TravelSeconds) Finish(RepairState.Cancelled, "could not reach the Repair Pad in time", false);
                        else Watch();
                    }
                    else if (Dist(_target) > _t.ArriveRadius * 2f)
                    {
                        // Pushed out of its slot: drive back.
                        Enter(RepairState.TravellingToQueue);
                        Drive(_target);
                    }
                    break;

                case RepairState.EnteringPad:
                    if (Dist(Pad.BayPoint) <= _t.BayArriveRadius)
                    {
                        Enter(RepairState.Repairing);
                        _vehicle.HoldStill();
                    }
                    else if (_stateTime > _t.EnterSeconds) Finish(RepairState.Cancelled, "could not enter the repair bay", false);
                    else Watch();
                    break;

                case RepairState.Repairing:
                    if (_vehicle.IsFullHealth)
                    {
                        q.Release(ClientId);
                        Enter(RepairState.ExitingPad);
                        _exitAttempt = 0;
                        Drive(Pad.ExitPoint(0));
                    }
                    else if (Dist(Pad.BayPoint) > Pad.BayRadius)
                    {
                        // Pushed off the bay: drive back on (the pad only repairs inside the bay).
                        Enter(RepairState.EnteringPad);
                        Drive(Pad.BayPoint);
                    }
                    break;

                case RepairState.ExitingPad:
                    if (Dist(Pad.BayPoint) > Pad.BayRadius + 1f)
                    {
                        q.MarkVacated(ClientId);
                        Finish(RepairState.Completed, "repaired", false);
                    }
                    else if (_stateTime > _t.ExitSeconds)
                    {
                        _exitAttempt++;
                        if (_exitAttempt < Pad.ExitPointCount)
                        {
                            _stateTime = 0f;
                            Drive(Pad.ExitPoint(_exitAttempt));
                        }
                        else
                        {
                            // Released where it stands; the pad keeps the bay closed until it is physically clear.
                            q.MarkVacated(ClientId);
                            Finish(RepairState.Completed, "repaired, but could not leave the bay cleanly", false);
                        }
                    }
                    break;
            }
        }

        private void GoToSlot(bool force)
        {
            int pos = Pad.Queue.PositionOf(ClientId);
            if (pos < 0) return;
            var slot = Pad.WaitPoint(pos);
            if (!force && _hasTarget && (slot - _target).sqrMagnitude < 1.5f * 1.5f) return;
            Enter(RepairState.TravellingToQueue);
            Drive(slot);
        }

        private void Drive(Vector3 point)
        {
            _target = point;
            _hasTarget = true;
            _watchTime = 0f;
            _watchDistance = Dist(point);
            _vehicle.MoveTo(point);
        }

        /// <summary>Stall watchdog: re-route when no progress; give up after the re-route budget or a blocked route.</summary>
        private void Watch()
        {
            if (_watchTime < _t.StallSeconds) return;
            float d = Dist(_target);
            if (_watchDistance - d >= _t.StallDistance)
            {
                _watchTime = 0f;
                _watchDistance = d;
                return;
            }
            _reroutes++;
            if (_reroutes > _t.MaxReroutes || _vehicle.RouteBlocked)
            {
                Finish(RepairState.Cancelled, "route to the Repair Pad is blocked", false);
                return;
            }
            Drive(_target);
        }

        private void Enter(RepairState s)
        {
            if (State == s) return;
            State = s;
            _stateTime = 0f;
            StateChanged?.Invoke(s);
        }

        private void Finish(RepairState s, string reason, bool vehicleGone)
        {
            if (!IsActive) return;
            if (Pad != null)
            {
                // A vehicle still in the bay keeps it "vacating" until the pad sees it gone.
                Pad.Queue.Remove(ClientId, gone: vehicleGone || !Pad.IsAlive);
                if (s == RepairState.Completed) Pad.Queue.MarkVacated(ClientId);
            }
            Reason = reason;
            if (s == RepairState.Cancelled && _vehicle.IsAlive) _vehicle.HoldStill();
            Enter(s);
        }

        private float Dist(Vector3 p)
        {
            var v = _vehicle.Position;
            float dx = v.x - p.x, dz = v.z - p.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }
}
