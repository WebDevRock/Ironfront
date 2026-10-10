using System;
using UnityEngine;

namespace Ironfront.Core.Combat
{
    /// <summary>Something that can be attacked.</summary>
    public interface ICombatTarget
    {
        bool IsAlive { get; }
        Vector3 Position { get; }
        string DisplayName { get; }
    }

    /// <summary>The unit's physical side: sensing, movement and the weapon trigger.</summary>
    public interface ICombatBody
    {
        Vector3 Position { get; }
        /// <summary>Distance to the target's nearest surface (so large buildings are in range at their edge).</summary>
        float DistanceTo(ICombatTarget target);
        bool HasLineOfSight(ICombatTarget target);
        /// <summary>Best hostile target within <paramref name="radius"/>, excluding <paramref name="ignore"/>; null if none.</summary>
        ICombatTarget FindTarget(float radius, ICombatTarget ignore);
        void MoveTo(Vector3 destination);
        /// <summary>Path towards the target's reachable surface.</summary>
        void ChaseTo(ICombatTarget target);
        void Halt();
        /// <summary>True when the last <see cref="MoveTo"/> destination has been reached (or there is no path).</summary>
        bool HasArrived { get; }
        /// <summary>Fires if the weapon cooldown allows; returns whether a shot was fired.</summary>
        bool TryFire(ICombatTarget target);
    }

    public enum CombatOrderType
    {
        /// <summary>No order: engages enemies already in range without moving.</summary>
        Idle,
        /// <summary>Travel only; enemies are ignored until arrival.</summary>
        Move,
        /// <summary>Pursue a designated target and engage it in range.</summary>
        Attack,
        /// <summary>Travel, engaging enemies met on the way, then continue.</summary>
        AttackMove,
        /// <summary>Never move; engage enemies in range.</summary>
        Hold,
    }

    public enum CombatActivity
    {
        Idle,
        Moving,
        /// <summary>Moving to get the target into range / line of sight.</summary>
        Chasing,
        /// <summary>Stopped, target in range: firing as the cooldown allows.</summary>
        Engaging,
    }

    [Serializable]
    public struct CombatSettings
    {
        public float Range;
        public float AcquisitionRange;
        public bool RequiresLineOfSight;
        /// <summary>When closing in, stop this far inside max range, so small target movements don't cause start/stop jitter.</summary>
        public float ChaseStopMargin;
        /// <summary>Re-path when the target has moved further than this from the last chase point...</summary>
        public float RepathDistance;
        /// <summary>...but not more often than this.</summary>
        public float RepathInterval;
        /// <summary>Re-path at least this often while chasing.</summary>
        public float MaxRepathInterval;
        /// <summary>Give up if the unit itself has not moved for this long while chasing.</summary>
        public float StuckTimeout;
        public float ProgressEpsilon;
        public float ScanInterval;
        /// <summary>Auto-acquired targets are dropped beyond AcquisitionRange × LeashFactor.</summary>
        public float LeashFactor;
        /// <summary>An unreachable target is not re-acquired automatically for this long.</summary>
        public float IgnoreUnreachableFor;

        public static CombatSettings ForWeapon(float range, float acquisitionRange, bool requiresLineOfSight) => new CombatSettings
        {
            Range = range,
            AcquisitionRange = Math.Max(range, acquisitionRange),
            RequiresLineOfSight = requiresLineOfSight,
            ChaseStopMargin = Math.Min(1f, range * 0.25f),
            RepathDistance = 2f,
            RepathInterval = 0.5f,
            MaxRepathInterval = 3f,
            StuckTimeout = 4f,
            ProgressEpsilon = 0.5f,
            ScanInterval = 0.25f,
            LeashFactor = 1.5f,
            IgnoreUnreachableFor = 6f,
        };
    }

    /// <summary>
    /// Per-unit combat order logic. Pure: all world interaction goes through <see cref="ICombatBody"/>.
    /// </summary>
    public sealed class CombatOrderMachine
    {
        private readonly CombatSettings _s;

        private float _dt;
        private float _scanTimer;
        private float _timeSinceRepath;
        private bool _hasChasePoint;
        private Vector3 _lastChasePoint;
        private Vector3 _progressPosition;
        private float _stuckTimer;
        private ICombatTarget _ignored;
        private float _ignoreTimer;

        public CombatOrderType Order { get; private set; } = CombatOrderType.Idle;
        public CombatActivity Activity { get; private set; } = CombatActivity.Idle;
        public ICombatTarget Target { get; private set; }
        public bool IsAutoTarget { get; private set; }
        public Vector3 Destination { get; private set; }
        /// <summary>Why the last order ended early, e.g. "Target unreachable". Cleared by new orders.</summary>
        public string LastResult { get; private set; }
        public CombatSettings Settings => _s;

        /// <summary>Raised when the order type changes.</summary>
        public event Action<CombatOrderType> OrderChanged;

        public CombatOrderMachine(CombatSettings settings)
        {
            if (settings.Range <= 0f) throw new ArgumentOutOfRangeException(nameof(settings), "Range must be positive.");
            _s = settings;
        }

        // ---------- Orders ----------

        public void OrderMove(ICombatBody body, Vector3 destination)
        {
            SetOrder(CombatOrderType.Move);
            ClearTarget();
            Destination = destination;
            body.MoveTo(destination);
            Activity = CombatActivity.Moving;
        }

        public void OrderAttack(ICombatBody body, ICombatTarget target)
        {
            if (!IsValid(target)) return;
            SetOrder(CombatOrderType.Attack);
            SetTarget(target, auto: false, body);
            Activity = CombatActivity.Chasing; // resolved on the next tick
        }

        public void OrderAttackMove(ICombatBody body, Vector3 destination)
        {
            SetOrder(CombatOrderType.AttackMove);
            ClearTarget();
            Destination = destination;
            body.MoveTo(destination);
            Activity = CombatActivity.Moving;
            _scanTimer = 0f;
        }

        public void OrderStop(ICombatBody body)
        {
            SetOrder(CombatOrderType.Idle);
            LastResult = null;
            ClearTarget();
            body.Halt();
            Activity = CombatActivity.Idle;
        }

        public void OrderHold(ICombatBody body)
        {
            SetOrder(CombatOrderType.Hold);
            ClearTarget();
            body.Halt();
            Activity = CombatActivity.Idle;
            _scanTimer = 0f;
        }

        // ---------- Tick ----------

        public void Tick(float deltaTime, ICombatBody body)
        {
            _dt = deltaTime;
            _scanTimer -= deltaTime;
            _timeSinceRepath += deltaTime;
            if (_ignoreTimer > 0f)
            {
                _ignoreTimer -= deltaTime;
                if (_ignoreTimer <= 0f) _ignored = null;
            }

            switch (Order)
            {
                case CombatOrderType.Move:
                    if (body.HasArrived)
                    {
                        SetOrder(CombatOrderType.Idle);
                        Activity = CombatActivity.Idle;
                    }
                    break;

                case CombatOrderType.Idle:
                case CombatOrderType.Hold:
                    TickStationary(body);
                    break;

                case CombatOrderType.Attack:
                    if (!IsValid(Target))
                    {
                        ClearTarget();
                        SetOrder(CombatOrderType.Idle);
                        Activity = CombatActivity.Idle;
                        LastResult = "target destroyed";
                        body.Halt();
                        return;
                    }
                    Engage(body, Target);
                    break;

                case CombatOrderType.AttackMove:
                    TickAttackMove(body);
                    break;
            }
        }

        /// <summary>Idle and Hold: fire at whatever is in range, never move.</summary>
        private void TickStationary(ICombatBody body)
        {
            if (Target != null && (!IsValid(Target) || body.DistanceTo(Target) > _s.Range || !HasSight(body, Target)))
                ClearTarget();

            if (Target == null && ScanDue())
            {
                var t = body.FindTarget(_s.Range, _ignored);
                if (t != null && HasSight(body, t)) SetTarget(t, auto: true, body);
            }

            if (Target != null)
            {
                Activity = CombatActivity.Engaging;
                body.TryFire(Target);
            }
            else
            {
                Activity = CombatActivity.Idle;
            }
        }

        private void TickAttackMove(ICombatBody body)
        {
            if (Target != null)
            {
                // Validity first: a destroyed target must not be queried for its distance.
                if (!IsValid(Target) || (IsAutoTarget && body.DistanceTo(Target) > _s.AcquisitionRange * _s.LeashFactor))
                {
                    ClearTarget();
                    ResumeAttackMove(body);
                }
                else
                {
                    Engage(body, Target);
                    return;
                }
            }

            if (ScanDue())
            {
                var t = body.FindTarget(_s.AcquisitionRange, _ignored);
                if (t != null)
                {
                    SetTarget(t, auto: true, body);
                    Engage(body, t);
                    return;
                }
            }

            if (body.HasArrived)
            {
                SetOrder(CombatOrderType.Idle);
                Activity = CombatActivity.Idle;
            }
        }

        private void ResumeAttackMove(ICombatBody body)
        {
            body.MoveTo(Destination);
            Activity = CombatActivity.Moving;
        }

        /// <summary>Fire if in range with line of sight, otherwise close in.</summary>
        private void Engage(ICombatBody body, ICombatTarget target)
        {
            float distance = body.DistanceTo(target);
            // Hysteresis: once engaging, keep firing up to max range; when closing in, stop a little inside it.
            float threshold = Activity == CombatActivity.Engaging ? _s.Range : _s.Range - _s.ChaseStopMargin;

            if (distance <= threshold && HasSight(body, target))
            {
                if (Activity != CombatActivity.Engaging)
                {
                    body.Halt();
                    Activity = CombatActivity.Engaging;
                    _hasChasePoint = false; // re-path immediately if the target leaves range
                }
                _stuckTimer = 0f;
                _progressPosition = body.Position;
                body.TryFire(target);
                return;
            }

            Chase(body, target);
        }

        private void Chase(ICombatBody body, ICombatTarget target)
        {
            if (Activity != CombatActivity.Chasing)
            {
                Activity = CombatActivity.Chasing;
                _stuckTimer = 0f;
                _progressPosition = body.Position;
            }

            bool targetMoved = _hasChasePoint && (target.Position - _lastChasePoint).magnitude > _s.RepathDistance;
            bool due = !_hasChasePoint
                       || (targetMoved && _timeSinceRepath >= _s.RepathInterval)
                       || _timeSinceRepath >= _s.MaxRepathInterval;
            if (due)
            {
                body.ChaseTo(target);
                _lastChasePoint = target.Position;
                _hasChasePoint = true;
                _timeSinceRepath = 0f;
            }

            // Progress is measured on our own position: a fleeing target is fine, a blocked unit is not.
            if ((body.Position - _progressPosition).magnitude > _s.ProgressEpsilon)
            {
                _progressPosition = body.Position;
                _stuckTimer = 0f;
            }
            else
            {
                _stuckTimer += _dt;
                if (_stuckTimer >= _s.StuckTimeout) GiveUp(body, target);
            }
        }

        private void GiveUp(ICombatBody body, ICombatTarget target)
        {
            LastResult = "target unreachable";
            _ignored = target;
            _ignoreTimer = _s.IgnoreUnreachableFor;
            ClearTarget();
            body.Halt();

            if (Order == CombatOrderType.AttackMove)
            {
                ResumeAttackMove(body);
            }
            else
            {
                SetOrder(CombatOrderType.Idle);
                Activity = CombatActivity.Idle;
            }
        }

        // ---------- Helpers ----------

        private bool HasSight(ICombatBody body, ICombatTarget t) => !_s.RequiresLineOfSight || body.HasLineOfSight(t);

        private static bool IsValid(ICombatTarget t) => t != null && t.IsAlive;

        private bool ScanDue()
        {
            if (_scanTimer > 0f) return false;
            _scanTimer = _s.ScanInterval;
            return true;
        }

        private void SetTarget(ICombatTarget target, bool auto, ICombatBody body)
        {
            Target = target;
            IsAutoTarget = auto;
            _hasChasePoint = false;
            _stuckTimer = 0f;
            _progressPosition = body.Position;
        }

        private void ClearTarget()
        {
            Target = null;
            IsAutoTarget = false;
            _hasChasePoint = false;
        }

        private void SetOrder(CombatOrderType order)
        {
            if (order != CombatOrderType.Idle) LastResult = null;
            if (Order == order) return;
            Order = order;
            OrderChanged?.Invoke(order);
        }

        /// <summary>Player-facing description of the current order.</summary>
        public string Describe()
        {
            string target = Target != null && Target.IsAlive ? Target.DisplayName : null;
            switch (Order)
            {
                case CombatOrderType.Move:
                    return "Moving";
                case CombatOrderType.Attack:
                    return Activity == CombatActivity.Engaging ? $"Attacking {target} - firing" : $"Attacking {target} - closing in";
                case CombatOrderType.AttackMove:
                    return target != null
                        ? (Activity == CombatActivity.Engaging ? $"Attack-move - firing at {target}" : $"Attack-move - engaging {target}")
                        : "Attack-move - advancing";
                case CombatOrderType.Hold:
                    return target != null ? $"Holding position - firing at {target}" : "Holding position";
                default:
                    string idle = target != null ? $"Idle - firing at {target}" : "Idle";
                    return LastResult != null ? $"{idle} ({LastResult})" : idle;
            }
        }
    }
}
