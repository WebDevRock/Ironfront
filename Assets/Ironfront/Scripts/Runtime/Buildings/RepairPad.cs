using System;
using System.Collections.Generic;
using Ironfront.Combat;
using Ironfront.Core.Construction;
using Ironfront.Core.Repair;
using Ironfront.Data;
using Ironfront.Match;
using Ironfront.Units;
using UnityEngine;
using UnityEngine.AI;

namespace Ironfront.Buildings
{
    /// <summary>
    /// Vehicle Repair Pad (Milestone 009). Its bay is the apron in front of the building (kept clear by placement rules
    /// and not carved out of the NavMesh), with waiting slots to the left of the bay and exit points to the right, so the
    /// waiting line never blocks the way in or out. Exactly one vehicle is in the bay at a time (<see cref="RepairPadQueue"/>);
    /// the next one is granted the bay only once it is physically clear. Repairs pause while the pad is assembling, while
    /// its faction is on low power and while credits run short; the queue is kept.
    /// </summary>
    [RequireComponent(typeof(Building))]
    public class RepairPad : MonoBehaviour, IRepairPadSite
    {
        public enum Status { Assembling, Ready, Repairing, NoPower, NoCredits }

        private static readonly List<RepairPad> s_All = new List<RepairPad>();
        public static IReadOnlyList<RepairPad> All => s_All;

        /// <summary>Raised when a vehicle finishes a repair here (presentation, statistics).</summary>
        public static event Action<RepairPad, RepairClient> AnyRepairCompleted;

        private readonly RepairPadQueue _queue = new RepairPadQueue();
        private readonly List<Vector3> _slots = new List<Vector3>();
        private readonly List<Vector3> _exits = new List<Vector3>();
        private readonly Dictionary<int, RepairMeter> _meters = new Dictionary<int, RepairMeter>();
        private Building _building;
        private Health _health;
        private float _assembly;
        private float _slotTimer;
        private float _blockerTime;
        private RepairSettings _settings;

        public RepairPadQueue Queue => _queue;
        public Building Building => _building;
        public int FactionId => _building != null ? _building.FactionId : 0;
        public bool IsAlive => this != null && _health != null && _health.IsAlive;
        public Status State { get; private set; } = Status.Assembling;
        public float Assembly01 => _settings == null || _settings.PadAssemblyTime <= 0f ? 1f : Mathf.Clamp01(_assembly / _settings.PadAssemblyTime);
        public bool IsAssembled => Assembly01 >= 1f;
        public float AssemblyRemaining => _settings == null ? 0f : Mathf.Max(0f, _settings.PadAssemblyTime - _assembly);
        /// <summary>Vehicles repaired here this match.</summary>
        public int Completed { get; private set; }
        public int CreditsSpent { get; private set; }

        /// <summary>The vehicle in the bay (entering or being repaired), or null.</summary>
        public RepairClient ActiveClient => RepairClient.Find(_queue.Active);
        public bool IsRepairingNow => State == Status.Repairing;
        /// <summary>Set by the HUD while selected vehicles are queued here (the pad's markings pulse).</summary>
        public bool Highlighted { get; set; }

        public Vector3 BayPoint
        {
            get
            {
                var p = _building != null ? _building.FrontPoint : transform.position;
                p.y = 0f;
                return p;
            }
        }

        public float BayRadius => _settings != null ? _settings.BayRadius : 3f;
        private Vector3 Front => _building != null ? _building.FrontDirection : Vector3.forward;
        private Vector3 Right => new Vector3(Front.z, 0f, -Front.x);

        private void Awake()
        {
            _building = GetComponent<Building>();
            _health = GetComponent<Health>();
            _settings = RepairSettings.Current;
        }

        private void OnEnable() { if (!s_All.Contains(this)) s_All.Add(this); }

        private void OnDisable() => s_All.Remove(this);

        private void OnDestroy()
        {
            // Every client sees the pad gone on its next tick and cancels; drop the tickets now so nothing stays reserved.
            _queue.Clear();
            _meters.Clear();
        }

        private void Start() => RefreshSlots();

        /// <summary>Fraction of the active vehicle's health (repair progress for the HUD), or -1.</summary>
        public float ActiveProgress
        {
            get
            {
                var c = ActiveClient;
                return c != null && c.Health != null ? c.Health.Current / c.Health.Max : -1f;
            }
        }

        private void Update()
        {
            if (!IsAlive) return;
            var flow = MatchFlowController.Current;
            if (flow != null && flow.IsOver) return;
            var match = MatchController.Current;
            bool lowPower = match != null && match.GetFaction(FactionId).Power.IsLowPower;
            float dt = Time.deltaTime;

            if (!IsAssembled)
            {
                if (!lowPower) _assembly += dt;
                State = lowPower ? Status.NoPower : Status.Assembling;
            }

            _slotTimer -= dt;
            if (_slotTimer <= 0f)
            {
                _slotTimer = 1f;
                RefreshSlots();
            }

            UpdateVacating();
            if (_queue.BayFree)
            {
                bool clear = BayPhysicallyClear(out var blocker);
                if (clear) _queue.TryGrant(true);
                else HandleBlocker(blocker, dt);
            }
            else _blockerTime = 0f;

            if (!IsAssembled) return;
            var client = ActiveClient;
            if (client == null || client.State != RepairState.Repairing || !InBay(client.transform.position))
            {
                State = lowPower ? Status.NoPower : Status.Ready;
                return;
            }
            if (lowPower)
            {
                State = Status.NoPower;
                return;
            }
            var meter = MeterFor(client);
            var h = client.Health;
            var wallet = match.GetFaction(FactionId).Wallet;
            int before = meter.TotalCharged;
            float hp = meter.Step(dt, h.Max - h.Current, wallet.Credits, wallet.TrySpend);
            if (hp > 0f) h.Restore(hp);
            CreditsSpent += meter.TotalCharged - before;
            RepairLedger.AddVehicle(FactionId, meter.TotalCharged - before);
            State = meter.PausedForCredits ? Status.NoCredits : Status.Repairing;
            if (hp > 0f && h.Current >= h.Max - 0.01f) // counted once: the step that completed the repair
            {
                Completed++;
                RepairLedger.CountVehicleRepaired(FactionId);
                _meters.Remove(client.Id);
                AnyRepairCompleted?.Invoke(this, client);
            }
        }

        private RepairMeter MeterFor(RepairClient client)
        {
            if (_meters.TryGetValue(client.Id, out var m)) return m;
            var identity = client.GetComponent<UnitIdentity>();
            var def = identity != null ? identity.Definition : null;
            int price = def != null ? def.Cost : 500;
            float mult = def != null ? def.RepairCostMultiplier : 1f;
            float max = client.Health.Max;
            m = new RepairMeter(RepairMeter.CostPerHealthFor(price, max, _settings.VehicleCostFraction, mult), _settings.PadRatePerSecond * max);
            _meters[client.Id] = m;
            return m;
        }

        /// <summary>Estimated credits to fully repair a vehicle here (AI, HUD).</summary>
        public int EstimateCost(Health vehicle)
        {
            if (vehicle == null) return 0;
            var identity = vehicle.GetComponent<UnitIdentity>();
            var def = identity != null ? identity.Definition : null;
            float perHp = RepairMeter.CostPerHealthFor(def != null ? def.Cost : 500, vehicle.Max, _settings.VehicleCostFraction, def != null ? def.RepairCostMultiplier : 1f);
            return RepairMeter.CostFor(vehicle.Max - vehicle.Current, perHp);
        }

        /// <summary>Seconds a full repair takes at this pad's rate.</summary>
        public float FullRepairSeconds => 1f / Mathf.Max(0.001f, _settings.PadRatePerSecond);

        private bool InBay(Vector3 p)
        {
            var b = BayPoint;
            float dx = p.x - b.x, dz = p.z - b.z;
            return dx * dx + dz * dz <= BayRadius * BayRadius;
        }

        /// <summary>The leaving vehicle is out of the bay (or gone): the next one may be granted.</summary>
        private void UpdateVacating()
        {
            int v = _queue.Vacating;
            if (v == RepairPadQueue.None) return;
            var c = RepairClient.Find(v);
            if (c == null || !c.IsAlive || !InBay(c.transform.position)) _queue.MarkVacated(v);
        }

        private bool BayPhysicallyClear(out Health blocker)
        {
            blocker = null;
            var all = Health.All;
            for (int i = 0; i < all.Count; i++)
            {
                var h = all[i];
                if (h == null || !h.IsAlive || h.IsStructure) continue;
                if (InBay(h.transform.position)) { blocker = h; return false; }
            }
            return true;
        }

        /// <summary>
        /// A friendly vehicle left idle in the bay for a while (e.g. stopped there by its owner) is asked to drive to an exit
        /// point, so the pad cannot stay blocked forever. Enemy or moving vehicles are just waited for.
        /// </summary>
        private void HandleBlocker(Health blocker, float dt)
        {
            if (blocker == null || blocker.FactionId != FactionId) { _blockerTime = 0f; return; }
            var mover = blocker.GetComponent<UnitMover>();
            bool idle = mover == null || mover.HasArrived;
            var client = blocker.GetComponent<RepairClient>();
            if (!idle || (client != null && client.IsBusy)) { _blockerTime = 0f; return; }
            _blockerTime += dt;
            if (_blockerTime < _settings.BayBlockerSeconds) return;
            _blockerTime = 0f;
            var exit = ExitPoint(0);
            var combat = blocker.GetComponent<CombatUnit>();
            if (combat != null && combat.Machine != null) combat.OrderMove(exit);
            else if (mover != null) mover.MoveTo(exit);
        }

        // ---------- Slots ----------

        /// <summary>
        /// Recomputes valid waiting slots (left of the bay, rows of four going outwards) and exit points (right of the bay).
        /// A slot is valid when it is on walkable ground and clear of structures, obstacles and the bay's lanes.
        /// </summary>
        public void RefreshSlots()
        {
            _slots.Clear();
            var bay = BayPoint;
            Vector3 f = Front, r = Right;
            float half = (_building != null && _building.Definition != null ? _building.Definition.FootprintSize.x : 6f) * 0.5f;
            for (int col = 0; col < 8 && _slots.Count < 24; col++)
                for (int row = 0; row < 4 && _slots.Count < 24; row++)
                {
                    var p = bay - r * (half + 5f + col * 4.5f) + f * (row * 4.5f - 2f);
                    if (ValidSpot(p, out var snapped)) _slots.Add(snapped);
                }
            if (_slots.Count == 0) _slots.Add(bay + f * 10f - r * 8f); // nothing valid: still give a direction to wait in

            _exits.Clear();
            foreach (var p in new[] { bay + r * (half + 8f) + f * 2f, bay + r * (half + 6f) + f * 9f, bay + f * 12f + r * 3f })
                if (ValidSpot(p, out var snapped)) _exits.Add(snapped);
            if (_exits.Count == 0) _exits.Add(bay + f * 10f);
        }

        private bool ValidSpot(Vector3 p, out Vector3 snapped)
        {
            snapped = p;
            if (!NavMesh.SamplePosition(p, out var hit, 1.5f, NavMesh.AllAreas)) return false;
            var q = hit.position;
            if ((new Vector2(q.x - p.x, q.z - p.z)).sqrMagnitude > 1.2f * 1.2f) return false;
            if (InBay(q) || (new Vector2(q.x - BayPoint.x, q.z - BayPoint.z)).sqrMagnitude < (BayRadius + 2f) * (BayRadius + 2f)) return false;
            var xz = new Vector2(q.x, q.z);
            foreach (var b in Building.All)
            {
                if (b == null || b.Definition == null) continue;
                var fp = b.Footprint;
                if (PlacementRules.Expand(fp.Body, 1.5f).Contains(xz)) return false;
                if (fp.HasApron && b != _building && fp.Apron.Contains(xz)) return false;
            }
            if (Physics.CheckSphere(q + Vector3.up * 1f, 1.2f, 1, QueryTriggerInteraction.Ignore)) return false; // obstacles (Default layer)
            snapped = q;
            return true;
        }

        public Vector3 WaitPoint(int position)
        {
            if (_slots.Count == 0) RefreshSlots();
            if (position < _slots.Count) return _slots[Mathf.Max(0, position)];
            // Longer queue than slots: extend the last row outwards (still left of the bay).
            var last = _slots[_slots.Count - 1];
            return last - Right * 4.5f * (position - _slots.Count + 1);
        }

        public int ExitPointCount => Mathf.Max(1, _exits.Count);

        public Vector3 ExitPoint(int attempt)
        {
            if (_exits.Count == 0) RefreshSlots();
            return _exits[Mathf.Clamp(attempt, 0, _exits.Count - 1)];
        }

        public IReadOnlyList<Vector3> WaitSlots => _slots;

        /// <summary>Owner-faction pad with the shortest expected wait from <paramref name="from"/> (see RepairPlanner).</summary>
        public static RepairPad Best(int faction, Vector3 from)
        {
            RepairPad best = null;
            float bestScore = float.PositiveInfinity;
            foreach (var pad in s_All)
            {
                if (pad == null || !pad.IsAlive || pad.FactionId != faction) continue;
                float travel = Vector3.Distance(from, pad.BayPoint) / 7f;
                float score = Core.AI.RepairPlanner.PadWaitEstimate(true, travel, pad.Queue.Count, pad.FullRepairSeconds * 0.5f, pad.AssemblyRemaining);
                if (score < bestScore) { bestScore = score; best = pad; }
            }
            return best;
        }

        public static int CountFor(int faction)
        {
            int n = 0;
            foreach (var p in s_All) if (p != null && p.IsAlive && p.FactionId == faction) n++;
            return n;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_All.Clear();
            AnyRepairCompleted = null;
        }
    }
}
