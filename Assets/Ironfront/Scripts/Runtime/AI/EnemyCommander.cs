using System;
using System.Collections.Generic;
using System.Linq;
using Ironfront.Buildings;
using Ironfront.Combat;
using Ironfront.Construction;
using Ironfront.Core;
using Ironfront.Core.AI;
using Ironfront.Core.Combat;
using Ironfront.Core.Construction;
using Ironfront.Core.Production;
using Ironfront.Data;
using Ironfront.Economy;
using Ironfront.Match;
using Ironfront.Units;
using UnityEngine;
using UnityEngine.AI;

namespace Ironfront.AI
{
    /// <summary>
    /// Rule-based computer commander for one faction. Every <see cref="AiDifficulty.DecisionInterval"/> it:
    /// updates its perception (vision-limited), picks a strategic state (<see cref="StrategyRules"/>), takes
    /// one economic step (<see cref="EconomyPlanner"/>) and manages its army. It acts only through the systems
    /// the player uses: building placement validation/purchase, factory queues and unit combat orders.
    /// </summary>
    public class EnemyCommander : MonoBehaviour
    {
        /// <summary>A deposit within this distance of a refinery dock counts as that refinery's local ore.</summary>
        public const float LocalOreRadius = 20f;

        private static readonly List<EnemyCommander> s_All = new List<EnemyCommander>();
        public static IReadOnlyList<EnemyCommander> All => s_All;

        [SerializeField] private int factionId = 2;
        [SerializeField] private AiDifficulty difficulty;
        [SerializeField] private BuildingPlacementController placement;
        [Tooltip("0 = fixed timing (default). Any other value varies decision timing and rally points slightly, " +
                 "so repeated AI-vs-AI matches differ (balance studies).")]
        [SerializeField] private int seed;
        [SerializeField, Range(0f, 0.5f)] private float decisionJitter = 0.2f;
        [SerializeField] private float rallyJitter = 6f;

        private CommanderSettings _cfg;
        private CostTable _cost;
        private BuildingDefinition _refineryDef, _powerPlantDef, _factoryDef;
        private UnitDefinition _harvesterDef, _lightDef, _heavyDef;
        private AiPerception _perception;
        private float _timer;
        private System.Random _rng;
        private float _stateSince;
        private bool _halted;
        private bool _initialised;

        private readonly HashSet<CombatUnit> _attackers = new HashSet<CombatUnit>();
        private float _attackStartStrength;
        private Vector3? _objective;
        private Health _objectiveTarget;
        private readonly Dictionary<CombatUnit, (Vector3 position, int stuck)> _progress = new Dictionary<CombatUnit, (Vector3, int)>();
        private readonly Dictionary<AiActionType, float> _cooldownUntil = new Dictionary<AiActionType, float>();
        private readonly HashSet<VehicleFactory> _knownFactories = new HashSet<VehicleFactory>();
        private string _lastLoggedAction;
        private readonly Dictionary<CombatUnit, int> _unitKeys = new Dictionary<CombatUnit, int>();
        private int _nextUnitKey;

        public int FactionId => factionId;
        public AiDifficulty Difficulty => difficulty;
        public StrategicState State { get; private set; } = StrategicState.Develop;
        public float TimeInState => Time.time - _stateSince;
        public string Objective { get; private set; } = "-";
        public BoundedLog Log { get; private set; } = new BoundedLog(20);
        public CommanderSnapshot LastSnapshot { get; private set; }
        public AiAction LastAction { get; private set; }
        public int Decisions { get; private set; }
        public int OrdersIssued { get; private set; }
        public int UnitsProduced { get; private set; }
        public int BuildingsPlaced { get; private set; }
        public int AttacksLaunched { get; private set; }
        public int AttackersCount => _attackers.Count;
        public bool IsHalted => _halted;
        public Vector3 RallyPoint { get; private set; }
        public int Seed => seed;
        private Vector3 _rallyOffset;

        /// <summary>Raised for each decision-log entry.</summary>
        public event Action<string> Logged;

        /// <summary>Sets up a commander created at runtime (AI-vs-AI simulation, tests). Call before Start.</summary>
        public void Configure(int faction, AiDifficulty settings, BuildingPlacementController placementController)
        {
            factionId = faction;
            difficulty = settings;
            placement = placementController;
        }

        /// <summary>Sets the variation seed (0 = none). Takes effect immediately, also on a running commander.</summary>
        public void SetSeed(int value)
        {
            seed = value;
            if (_initialised) ApplySeed();
        }

        private void ApplySeed()
        {
            _rng = null;
            _rallyOffset = Vector3.zero;
            if (seed == 0) return;
            _rng = new System.Random(seed);
            _timer = (float)_rng.NextDouble() * difficulty.DecisionInterval;
            float angle = (float)(_rng.NextDouble() * Math.PI * 2.0);
            _rallyOffset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * rallyJitter * (float)_rng.NextDouble();
        }

        private float Jitter(float baseValue) =>
            _rng == null ? baseValue : baseValue * (1f + decisionJitter * (float)(_rng.NextDouble() * 2.0 - 1.0));

        private void OnEnable() { if (!s_All.Contains(this)) s_All.Add(this); }
        private void OnDisable() => s_All.Remove(this);

        private void Start()
        {
            if (difficulty == null || MatchController.Current == null || MatchController.Current.Setup == null)
            {
                Debug.LogError("[Ironfront] EnemyCommander needs an AiDifficulty and a MatchController.", this);
                enabled = false;
                return;
            }
            if (placement == null) placement = FindAnyObjectByType<BuildingPlacementController>();

            _cfg = difficulty.Strategy;
            Log = new BoundedLog(difficulty.DecisionLogCapacity);
            _perception = new AiPerception(factionId, difficulty.VisionRange, difficulty.ThreatMemorySeconds);
            if (!ResolveDefinitions())
            {
                enabled = false;
                return;
            }
            _stateSince = Time.time;
            _timer = difficulty.DecisionInterval * 0.5f; // stagger from other per-interval work
            ApplySeed();
            _initialised = true;
            Record($"Commander online ({difficulty.DisplayName}), faction {factionId}" + (seed != 0 ? $", seed {seed}" : ""));
        }

        private void Update()
        {
            if (!_initialised || _halted) return;
            if (MatchFlowController.Current != null && MatchFlowController.Current.IsOver)
            {
                Halt("match over");
                return;
            }
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = Jitter(difficulty.DecisionInterval);
            Decide();
        }

        /// <summary>Stops all strategic activity (match over or HQ lost).</summary>
        public void Halt(string reason)
        {
            if (_halted) return;
            _halted = true;
            Record($"Halted: {reason}");
        }

        // ---------- Decision cycle ----------

        /// <summary>Runs one decision immediately (normally called on the decision interval).</summary>
        public void Decide()
        {
            using var perfScope = Ironfront.Diagnostics.PerfMarkers.Commander.Auto();
            if (!_initialised || _halted) return;
            Decisions++;

            var hq = Headquarters();
            if (hq == null)
            {
                Halt("headquarters destroyed");
                return;
            }

            _perception.Update(Time.time);
            RallyPoint = ComputeRallyPoint(hq.transform.position);
            SetupFactories();

            var army = OwnArmy();
            foreach (var dead in _unitKeys.Keys.Where(k => k == null).ToList()) _unitKeys.Remove(dead);
            _attackers.RemoveWhere(u => u == null || !u.GetComponent<Health>().IsAlive);

            var basePoints = OwnStructures().Select(b => b.transform.position).ToList();
            var threats = _perception.ThreatsNear(basePoints, difficulty.DefendRadius);
            var snapshot = BuildSnapshot(army, threats);
            LastSnapshot = snapshot;

            var next = StrategyRules.Next(State, TimeInState, snapshot, _cfg);
            if (next != State) EnterState(next, army);

            var action = EconomyPlanner.Next(snapshot, State, _cfg, _cost);
            LastAction = action;
            Execute(action);

            ManageArmy(army, threats);
        }

        private void EnterState(StrategicState next, List<CombatUnit> army)
        {
            var previous = State;
            State = next;
            _stateSince = Time.time;
            Record($"State {previous} -> {next}");

            if (previous == StrategicState.Attack && next != StrategicState.Attack)
            {
                if (next == StrategicState.Recover)
                {
                    foreach (var u in _attackers) Order(u, c => c.OrderMove(RallyPoint));
                    Record($"Retreat: {_attackers.Count} attackers fall back to rally");
                }
                _attackers.Clear();
                _objective = null;
                _objectiveTarget = null;
            }

            if (next == StrategicState.Attack) LaunchAttack(army);
        }

        // ---------- Snapshot ----------

        private CommanderSnapshot BuildSnapshot(List<CombatUnit> army, List<Health> threats)
        {
            var faction = MatchController.Current.GetFaction(factionId);
            var s = new CommanderSnapshot
            {
                Credits = faction.Wallet.Credits,
                PowerProduced = faction.Power.Produced,
                PowerConsumed = faction.Power.Consumed,
                HasHeadquarters = Headquarters() != null,
                KnownEnemyLight = _perception.KnownEnemyLight,
                KnownEnemyHeavy = _perception.KnownEnemyHeavy,
            };

            var refineries = new List<Vector3>();
            foreach (var b in OwnStructures())
            {
                switch (b.Definition.Id)
                {
                    case "refinery": s.Refineries++; refineries.Add(b.FrontPoint); break;
                    case "power_plant": s.PowerPlants++; break;
                    case "vehicle_factory": s.Factories++; break;
                }
            }

            foreach (var h in Health.All)
                if (h.IsAlive && h.FactionId == factionId && h.GetComponent<Harvester>() != null) s.Harvesters++;

            foreach (var u in army)
            {
                bool heavy = IsHeavy(u);
                if (heavy) s.HeavyTanks++; else s.LightTanks++;
                s.ArmyStrength += Strength(u);
            }

            foreach (var f in VehicleFactory.All)
            {
                if (f.FactionId != factionId) continue;
                foreach (var item in f.Queue.Items)
                {
                    if (item.Id == _harvesterDef.Id) s.QueuedHarvesters++;
                    else if (item.Id == _heavyDef.Id) s.QueuedHeavy++;
                    else if (item.Id == _lightDef.Id) s.QueuedLight++;
                }
            }

            foreach (var t in threats)
            {
                var identity = t.GetComponent<UnitIdentity>();
                bool heavy = identity != null && identity.Definition != null && identity.Definition.Id == _heavyDef.Id;
                s.ThreatStrength += ArmyMath.Strength(heavy, t.Model.Fraction);
            }

            foreach (var d in OreDeposit.All)
            {
                if (d.IsDepleted) continue;
                s.OreAvailable = true;
                foreach (var r in refineries)
                    if ((d.transform.position - r).sqrMagnitude <= LocalOreRadius * LocalOreRadius) s.OreNearRefinery = true;
            }

            s.AttackersAlive = _attackers.Count;
            s.AttackStrength = _attackers.Sum(Strength);
            s.AttackStartStrength = _attackStartStrength;
            return s;
        }

        // ---------- Economy execution ----------

        private void Execute(AiAction action)
        {
            if (action.Type == AiActionType.None || action.Type == AiActionType.SaveFor)
            {
                // De-duplicate on what is being waited for, not the changing credit figures in the text.
                string key = $"{action.Type}/{action.Goal}";
                if (key != _lastLoggedAction)
                {
                    _lastLoggedAction = key;
                    Record(action.ToString());
                }
                return;
            }

            if (_cooldownUntil.TryGetValue(action.Type, out float until) && Time.time < until) return;

            if (action.IsConstruction) Construct(action);
            else if (action.IsProduction) Produce(action);
        }

        private void Construct(AiAction action)
        {
            var def = action.Type == AiActionType.BuildRefinery ? _refineryDef
                : action.Type == AiActionType.BuildPowerPlant ? _powerPlantDef : _factoryDef;

            if (!FindSite(def, action.Type, out var site))
            {
                Record($"No valid site for {def.DisplayName}; retry later");
                _cooldownUntil[action.Type] = Time.time + 20f;
                return;
            }

            if (placement.TryPlace(def, site, factionId, out var built, out var reason, FacesSouth()))
            {
                BuildingsPlaced++;
                _lastLoggedAction = null;
                Record($"Built {def.DisplayName} at ({site.x:0},{site.z:0}) - {action.Reason}");
            }
            else
            {
                Record($"Placing {def.DisplayName} failed: {reason}");
                _cooldownUntil[action.Type] = Time.time + 5f;
            }
        }

        private void Produce(AiAction action)
        {
            var def = action.Type == AiActionType.ProduceHarvester ? _harvesterDef
                : action.Type == AiActionType.ProduceHeavyTank ? _heavyDef : _lightDef;

            VehicleFactory best = null;
            foreach (var f in VehicleFactory.All)
                if (f.FactionId == factionId && f.CanProduce(def) && (best == null || f.Queue.Count < best.Queue.Count))
                    best = f;
            if (best == null) return;

            var result = best.Enqueue(def);
            if (result == EnqueueResult.Ok)
            {
                _lastLoggedAction = null;
                Record($"Queued {def.DisplayName} - {action.Reason}");
            }
            else
            {
                Record($"Queue {def.DisplayName} refused: {result}");
                _cooldownUntil[action.Type] = Time.time + 3f;
            }
        }

        /// <summary>First site, best first, that passes the same placement validation the player gets.</summary>
        private bool FindSite(BuildingDefinition def, AiActionType type, out Vector3 site)
        {
            site = default;
            var hq = Headquarters();
            if (hq == null || placement == null) return false;
            Vector3 home = hq.transform.position;
            Vector3 enemyDir = (EnemyDirectionTarget(home) - home);
            enemyDir.y = 0f;
            enemyDir = enemyDir.sqrMagnitude > 1e-3f ? enemyDir.normalized : Vector3.forward;

            List<Vector3> candidates;
            switch (type)
            {
                case AiActionType.BuildPowerPlant:
                    candidates = SiteSearch.Ranked(home, home - enemyDir * 10f, 11f, 24f, 2f);
                    break;
                case AiActionType.BuildFactory:
                    candidates = SiteSearch.Ranked(home, home + enemyDir * 10f, 14f, 26f, 2f);
                    break;
                default:
                    var deposit = DepositForNewRefinery(home);
                    if (deposit == null) return false;
                    candidates = SiteSearch.Ranked(deposit.transform.position, home, 10f, 18f, 2f);
                    break;
            }

            bool facesSouth = FacesSouth();
            int checks = 0;
            foreach (var c in candidates)
            {
                if (++checks > 500) break;
                if (placement.Validate(def, c, facesSouth) == PlacementResult.Valid)
                {
                    site = c;
                    return true;
                }
            }
            return false;
        }

        private OreDeposit DepositForNewRefinery(Vector3 home)
        {
            var refineries = OwnStructures().Where(b => b.Definition.Id == "refinery").Select(b => b.transform.position).ToList();
            return OreDeposit.All
                .Where(d => !d.IsDepleted)
                .Where(d => refineries.Count == 0 || refineries.All(r => (r - d.transform.position).sqrMagnitude > 18f * 18f))
                .OrderBy(d => (d.transform.position - home).sqrMagnitude)
                .FirstOrDefault()
                ?? OreDeposit.All.Where(d => !d.IsDepleted).OrderBy(d => (d.transform.position - home).sqrMagnitude).FirstOrDefault();
        }

        // ---------- Army ----------

        private void ManageArmy(List<CombatUnit> army, List<Health> threats)
        {
            if (State == StrategicState.Defend && threats.Count > 0)
            {
                Defend(army, threats);
                return;
            }

            if (State == StrategicState.Attack) UpdateAttack();

            var home = army.Where(u => !_attackers.Contains(u)).OrderBy(u => UnitKey(u)).ToList();
            GatherAtRally(home);
        }

        private void Defend(List<CombatUnit> army, List<Health> threats)
        {
            var hq = Headquarters();
            var threat = threats.OrderBy(t => (t.transform.position - hq.transform.position).sqrMagnitude).First();
            Vector3 point = threat.transform.position;
            Objective = $"Defend base: {threats.Count} enemies near ({point.x:0},{point.z:0})";

            foreach (var u in army)
            {
                var m = u.Machine;
                bool busy = (m.Order == CombatOrderType.Attack && m.Target != null && m.Target.IsAlive)
                            || (m.Order == CombatOrderType.AttackMove && (m.Destination - point).sqrMagnitude < 8f * 8f)
                            || m.Activity == CombatActivity.Engaging;
                if (!busy) Order(u, c => c.OrderAttackMove(point));
            }
        }

        private void GatherAtRally(List<CombatUnit> units)
        {
            if (units.Count == 0) return;
            var slots = FormationCalculator.GetGridSlots(RallyPoint, EnemyDirectionTarget(RallyPoint) - RallyPoint, units.Count, 3f);
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                var m = u.Machine;
                if (m.Activity == CombatActivity.Engaging || m.Order == CombatOrderType.Attack) continue;
                if (m.Order == CombatOrderType.AttackMove || m.Order == CombatOrderType.Move) continue; // already travelling
                if ((u.transform.position - slots[i]).sqrMagnitude <= 5f * 5f) continue;
                Order(u, c => c.OrderAttackMove(slots[i]));
            }
            if (State != StrategicState.Attack)
                Objective = $"{State}: {units.Count} units at rally, {LastSnapshot.ArmyStrength:0.0} strength";
        }

        private void LaunchAttack(List<CombatUnit> army)
        {
            var ordered = army.OrderBy(u => (u.transform.position - RallyPoint).sqrMagnitude).ToList();
            // All-in (no ore left anywhere): nothing to protect for later, so nobody stays home.
            int keep = StrategyRules.AllIn(LastSnapshot) ? 0 : Mathf.FloorToInt(ordered.Count * _cfg.DefenceAllocation);
            // Keep the units nearest home as defenders; the rest attack.
            var hq = Headquarters();
            var byHome = ordered.OrderBy(u => (u.transform.position - hq.transform.position).sqrMagnitude).ToList();
            var defenders = new HashSet<CombatUnit>(byHome.Take(keep));

            _attackers.Clear();
            foreach (var u in ordered) if (!defenders.Contains(u)) _attackers.Add(u);
            _attackStartStrength = _attackers.Sum(Strength);
            _progress.Clear();
            AttacksLaunched++;

            ChooseObjective();
            IssueAttackMoves();
            Record($"Attack launched: {_attackers.Count} units (strength {_attackStartStrength:0.0}), {defenders.Count} stay home -> {Objective}");
        }

        private void UpdateAttack()
        {
            if (_attackers.Count == 0) return;

            bool objectiveGone = _objectiveTarget != null && !_objectiveTarget.IsAlive;
            if (_objective == null || objectiveGone)
            {
                if (objectiveGone) Record("Objective destroyed");
                ChooseObjective();
                IssueAttackMoves();
            }
            if (_objective == null) return;
            Vector3 objective = _objective.Value;

            foreach (var u in _attackers.ToList())
            {
                var m = u.Machine;
                Vector3 pos = u.transform.position;
                _progress.TryGetValue(u, out var p);
                bool moved = (pos - p.position).sqrMagnitude > 1f;
                bool engaging = m.Activity == CombatActivity.Engaging;

                if (m.Order == CombatOrderType.Idle || m.Order == CombatOrderType.Hold)
                {
                    // Idle again at every decision without moving: its orders are not taking effect. Give up rather
                    // than re-issuing them forever (this kept an attack "running" indefinitely in a balance study).
                    int idleStuck = moved ? 0 : p.stuck + 1;
                    if (idleStuck >= difficulty.StuckDecisions * 2)
                    {
                        _attackers.Remove(u);
                        _progress.Remove(u);
                        Order(u, c => c.OrderMove(RallyPoint));
                        Record($"{u.name} makes no progress towards the objective; sent home");
                        continue;
                    }

                    // Finished its leg: attack the objective structure if visible, otherwise continue to it.
                    if (_objectiveTarget != null && _objectiveTarget.IsAlive && _perception.Visible.Contains(_objectiveTarget))
                        Order(u, c => c.OrderAttack(_objectiveTarget));
                    else if ((pos - objective).sqrMagnitude > 10f * 10f)
                        Order(u, c => c.OrderAttackMove(objective));
                    else
                    {
                        // At the objective with nothing in sight: pick the next target.
                        ChooseObjective(exclude: _objectiveTarget);
                        if (_objective != null) Order(u, c => c.OrderAttackMove(_objective.Value));
                    }
                    _progress[u] = (pos, idleStuck);
                    continue;
                }

                int stuck = moved || engaging ? 0 : p.stuck + 1;
                _progress[u] = (pos, stuck);
                if (stuck == difficulty.StuckDecisions)
                {
                    // Blocked route: re-issue towards a nearby point so the path is recomputed.
                    Vector3 offset = Quaternion.Euler(0f, (UnitKey(u) % 8) * 45f, 0f) * Vector3.forward * 5f;
                    Order(u, c => c.OrderAttackMove(objective + offset));
                    Record($"{u.name} not progressing; re-routing");
                }
                else if (stuck >= difficulty.StuckDecisions * 2)
                {
                    _attackers.Remove(u);
                    _progress.Remove(u);
                    Order(u, c => c.OrderMove(RallyPoint));
                    Record($"{u.name} cannot reach the objective; sent home");
                }
            }
        }

        private void ChooseObjective(Health exclude = null)
        {
            Vector3 from = _attackers.Count > 0
                ? FormationCalculator.Centroid(_attackers.Select(a => a.transform.position).ToList())
                : RallyPoint;

            AiPerception.KnownStructure best = null;
            float bestScore = float.MinValue;
            foreach (var s in _perception.Structures)
            {
                if (!s.Alive || s.Health == exclude) continue;
                float score = TargetPriorities.Score(s.DefinitionId, Vector3.Distance(from, s.Position));
                if (score > bestScore) { bestScore = score; best = s; }
            }

            if (best != null)
            {
                _objectiveTarget = best.Health;
                _objective = best.Position;
                Objective = $"Attack {best.Health.DisplayName} at ({best.Position.x:0},{best.Position.z:0})";
                return;
            }

            _objectiveTarget = null;
            var enemyStart = difficulty.KnowsEnemyStartLocation ? HostileStart(from) : null;
            if (enemyStart != null && (exclude == null || (from - enemyStart.transform.position).sqrMagnitude > 15f * 15f))
            {
                _objective = enemyStart.transform.position;
                Objective = $"Advance on enemy start ({_objective.Value.x:0},{_objective.Value.z:0})";
            }
            else
            {
                _objective = Vector3.zero; // no knowledge: sweep towards the map centre
                Objective = "Search map centre";
            }
        }

        private void IssueAttackMoves()
        {
            if (_objective == null || _attackers.Count == 0) return;
            var list = _attackers.OrderBy(u => UnitKey(u)).ToList();
            var positions = list.Select(u => u.transform.position).ToList();
            var slots = FormationCalculator.GetGridSlots(_objective.Value, _objective.Value - FormationCalculator.Centroid(positions), list.Count, 3f);
            var assignment = FormationCalculator.AssignSlots(positions, slots);
            for (int i = 0; i < list.Count; i++)
            {
                Vector3 slot = slots[assignment[i]];
                Order(list[i], c => c.OrderAttackMove(slot));
            }
        }

        // ---------- Helpers ----------

        /// <summary>Stable per-unit ordering key (first time the commander saw the unit).</summary>
        private int UnitKey(CombatUnit u)
        {
            if (!_unitKeys.TryGetValue(u, out int key))
            {
                key = _nextUnitKey++;
                _unitKeys[u] = key;
            }
            return key;
        }

        private void Order(CombatUnit unit, Action<CombatUnit> order)
        {
            if (unit == null || unit.Machine == null) return;
            order(unit);
            OrdersIssued++;
        }

        private Building Headquarters()
        {
            foreach (var b in Building.All)
                if (b.FactionId == factionId && b.Definition != null && b.Definition.Id == "headquarters")
                {
                    var h = b.GetComponent<Health>();
                    if (h == null || h.IsAlive) return b;
                }
            return null;
        }

        private IEnumerable<Building> OwnStructures()
        {
            foreach (var b in Building.All)
            {
                if (b.FactionId != factionId || b.Definition == null) continue;
                var h = b.GetComponent<Health>();
                if (h != null && !h.IsAlive) continue;
                yield return b;
            }
        }

        private List<CombatUnit> OwnArmy()
        {
            var list = new List<CombatUnit>();
            foreach (var c in CombatUnit.All)
                if (c != null && c.Machine != null && FactionMember.Of(c) == factionId && c.GetComponent<Health>().IsAlive) list.Add(c);
            return list;
        }

        private bool IsHeavy(CombatUnit u)
        {
            var identity = u.GetComponent<UnitIdentity>();
            return identity != null && identity.Definition == _heavyDef;
        }

        private float Strength(CombatUnit u)
        {
            if (u == null) return 0f;
            var h = u.GetComponent<Health>();
            return h == null ? 0f : ArmyMath.Strength(IsHeavy(u), h.Model.Fraction);
        }

        private FactionStartLocation HostileStart(Vector3 from)
        {
            FactionStartLocation best = null;
            foreach (var s in FactionStartLocation.All)
            {
                if (!DamageRules.AreHostile(factionId, s.FactionId)) continue;
                if (best == null || (s.transform.position - from).sqrMagnitude < (best.transform.position - from).sqrMagnitude) best = s;
            }
            return best;
        }

        /// <summary>The commander builds with docks and exits facing its opponent (north or south).</summary>
        private bool FacesSouth()
        {
            var hq = Headquarters();
            if (hq == null) return false;
            return EnemyDirectionTarget(hq.transform.position).z < hq.transform.position.z;
        }

        /// <summary>Where "towards the enemy" is: its start if known by configuration, otherwise the map centre.</summary>
        private Vector3 EnemyDirectionTarget(Vector3 from)
        {
            var s = difficulty.KnowsEnemyStartLocation ? HostileStart(from) : null;
            return s != null ? s.transform.position : Vector3.zero;
        }

        private Vector3 ComputeRallyPoint(Vector3 hq)
        {
            Vector3 dir = EnemyDirectionTarget(hq) - hq;
            dir.y = 0f;
            dir = dir.sqrMagnitude > 1e-3f ? dir.normalized : Vector3.forward;
            Vector3 p = hq + dir * difficulty.RallyDistance + _rallyOffset;
            return NavMesh.SamplePosition(p, out var hit, 8f, NavMesh.AllAreas) ? hit.position : p;
        }

        private void SetupFactories()
        {
            foreach (var f in VehicleFactory.All)
            {
                if (f.FactionId != factionId || _knownFactories.Contains(f)) continue;
                _knownFactories.Add(f);
                f.SetRallyPoint(RallyPoint);
                f.UnitProduced += _ => UnitsProduced++;
            }
            _knownFactories.RemoveWhere(f => f == null);
        }

        private bool ResolveDefinitions()
        {
            var setup = MatchController.Current.Setup;
            _refineryDef = setup.ConstructionOptions.FirstOrDefault(d => d != null && d.Id == "refinery");
            _powerPlantDef = setup.ConstructionOptions.FirstOrDefault(d => d != null && d.Id == "power_plant");
            _factoryDef = setup.ConstructionOptions.FirstOrDefault(d => d != null && d.Id == "vehicle_factory");
            if (_refineryDef == null || _powerPlantDef == null || _factoryDef == null)
            {
                Debug.LogError("[Ironfront] EnemyCommander: construction options missing refinery/power plant/factory.", this);
                return false;
            }
            _harvesterDef = _factoryDef.ProducibleUnits.FirstOrDefault(u => u != null && u.Id == "harvester");
            _lightDef = _factoryDef.ProducibleUnits.FirstOrDefault(u => u != null && u.Id == "light_tank");
            _heavyDef = _factoryDef.ProducibleUnits.FirstOrDefault(u => u != null && u.Id == "heavy_tank");
            if (_harvesterDef == null || _lightDef == null || _heavyDef == null)
            {
                Debug.LogError("[Ironfront] EnemyCommander: factory cannot produce harvester/light/heavy tanks.", this);
                return false;
            }

            _cost = new CostTable
            {
                Refinery = _refineryDef.Cost,
                PowerPlant = _powerPlantDef.Cost,
                Factory = _factoryDef.Cost,
                Harvester = _harvesterDef.Cost,
                LightTank = _lightDef.Cost,
                HeavyTank = _heavyDef.Cost,
                RefineryDrain = _refineryDef.PowerDrain,
                FactoryDrain = _factoryDef.PowerDrain,
                PowerPlantOutput = _powerPlantDef.PowerOutput,
            };
            return true;
        }

        private void Record(string message)
        {
            float t = Time.timeSinceLevelLoad;
            string entry = $"[{(int)(t / 60):00}:{(int)(t % 60):00}] {message}";
            Log.Add(entry);
            Logged?.Invoke(entry);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => s_All.Clear();
    }
}
