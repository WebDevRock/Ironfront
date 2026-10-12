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
    public partial class EnemyCommander : MonoBehaviour
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
        // Milestone 007 content (optional: null in scenes without it).
        private BuildingDefinition _researchDef, _towerDef, _atDef;
        private BuildingDefinition _repairPadDef; // Milestone 009 (null in scenes without it)
        private UnitDefinition _reconDef, _artilleryDef;
        private float _lastThreatTime = -999f;
        private readonly Dictionary<CombatUnit, Vector3> _scoutTargets = new Dictionary<CombatUnit, Vector3>();
        private readonly HashSet<CombatUnit> _hunters = new HashSet<CombatUnit>();
        public int ResearchStarted { get; private set; }
        public int ScoutMissions { get; private set; }
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

        /// <summary>Selects the difficulty (skirmish setup). Must be called before Start.</summary>
        public void SetDifficulty(AiDifficulty settings)
        {
            if (_initialised) Debug.LogWarning("[Ironfront] EnemyCommander.SetDifficulty after Start has no effect.", this);
            difficulty = settings;
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

            // Milestone 008: caps scaled to the battlefield size (identical for every difficulty; see ScaledForMap).
            var vision = Ironfront.Vision.VisionSystem.Current;
            _cfg = difficulty.Strategy.ScaledForMap(vision != null ? Mathf.Max(vision.MapSize.x, vision.MapSize.y) : 100f);
            Log = new BoundedLog(difficulty.DecisionLogCapacity);
            _perception = new AiPerception(factionId, difficulty.VisionRange, difficulty.ThreatMemorySeconds);
            _intel = new EnemyIntel(CandidateSites());
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

            var hq = Home();
            if (hq == null)
            {
                Halt("headquarters destroyed (no production left)");
                return;
            }

            _perception.Update(Time.time);
            UpdateIntel();
            RallyPoint = ComputeRallyPoint(hq.transform.position);
            SetupFactories();

            // Milestone 009: vehicles on a repair order are unavailable (any order would cancel their repair).
            // Milestone 010: an MCV escort is busy too.
            var all = OwnArmy().Where(u => !InRepair(u) && !_escorts.Contains(u)).ToList();
            var scouts = all.Where(IsRecon).ToList();
            var army = all.Where(u => !IsRecon(u)).ToList();
            foreach (var dead in _unitKeys.Keys.Where(k => k == null).ToList()) _unitKeys.Remove(dead);
            _attackers.RemoveWhere(u => u == null || !u.GetComponent<Health>().IsAlive);

            var basePoints = OwnStructures().Select(b => b.transform.position).ToList();
            var threats = _perception.ThreatsNear(basePoints, difficulty.DefendRadius);
            if (threats.Count > 0) _lastThreatTime = Time.time;
            var snapshot = BuildSnapshot(army, threats, scouts);
            LastSnapshot = snapshot;

            var next = StrategyRules.Next(State, TimeInState, snapshot, _cfg);
            if (next != State) EnterState(next, army);

            var action = EconomyPlanner.Next(snapshot, State, _cfg, _cost);
            LastAction = action;
            Execute(action);

            ManageArmy(army, threats);
            ManageScouts(scouts);
            CounterArtillery(army, basePoints);
            ManageRepairs(army, scouts);
            ManageForwardBases(army);
            ManageSales(army, threats);
        }

        // ---------- Milestone 009: repairs (same pads, queues and prices as the player) ----------

        public int RepairsOrdered { get; private set; }
        public int BuildingRepairsStarted { get; private set; }

        private static bool InRepair(Component u)
        {
            var c = RepairClient.Of(u);
            return c != null && c.IsBusy;
        }

        private void ManageRepairs(List<CombatUnit> army, List<CombatUnit> scouts)
        {
            var match = MatchController.Current;
            int credits = match.GetFaction(factionId).Wallet.Credits;
            // Milestone 010: credits being saved for a forward base are not spent on repairs.
            if (LastAction.Type == AiActionType.SaveFor && LastAction.Goal == AiActionType.ProduceMcv) credits -= _cost.Mcv;
            bool defending = State == StrategicState.Defend;

            // Important structures: repair in place while affordable, stop before production is starved.
            if (_cfg.BuildingRepairThreshold > 0f)
                foreach (var b in OwnStructures().ToList())
                {
                    if (b.Repair == null || !RepairPlanner.IsImportantStructure(b.Definition.Id)) continue;
                    var h = b.GetComponent<Health>();
                    bool want = RepairPlanner.ShouldRepairBuilding(h.Current / h.Max, _cfg.BuildingRepairThreshold, b.Repair.IsOn, credits, _cfg.CreditReserve);
                    if (want == b.Repair.IsOn) continue;
                    if (b.Repair.SetRepairing(want) && want)
                    {
                        BuildingRepairsStarted++;
                        Record($"Repairing {b.Definition.DisplayName} ({h.Current / h.Max * 100f:0}%)");
                    }
                }

            // Vehicles: only when a pad exists, the vehicle is damaged enough and the situation allows it.
            if (_cfg.VehicleRepairThreshold <= 0f || RepairPad.CountFor(factionId) == 0) return;
            int away = 0;
            foreach (var c in CombatUnit.All)
                if (c != null && FactionMember.Of(c) == factionId && InRepair(c)) away++;
            int armySize = army.Count + scouts.Count + away;
            var candidates = new List<Component>();
            candidates.AddRange(army);
            candidates.AddRange(scouts);
            foreach (var h in Health.All)
                if (h.IsAlive && h.FactionId == factionId && h.GetComponent<Harvester>() != null && !InRepair(h)) candidates.Add(h);
            foreach (var u in candidates.OrderBy(c => c.GetComponent<Health>().Current / c.GetComponent<Health>().Max))
            {
                var h = u.GetComponent<Health>();
                var combat = u.GetComponent<CombatUnit>();
                bool engaged = combat != null && combat.Machine != null && combat.Machine.Activity == Core.Combat.CombatActivity.Engaging;
                bool attacking = combat != null && _attackers.Contains(combat);
                var pad = RepairPad.Best(factionId, u.transform.position);
                if (pad == null) return;
                int cost = pad.EstimateCost(h);
                bool isHarvester = combat == null;
                if (!RepairPlanner.ShouldSendVehicle(h.Current / h.Max, _cfg.VehicleRepairThreshold, engaged || attacking, defending && !isHarvester,
                        isHarvester ? 0 : away, isHarvester ? 0 : armySize, credits, _cfg.CreditReserve, cost))
                    continue;
                if (!RepairClient.Request(u, pad, out _)) continue;
                if (combat != null) _attackers.Remove(combat);
                if (!isHarvester) away++;
                credits -= cost;
                RepairsOrdered++;
                Record($"{h.DisplayName} ({h.Current / h.Max * 100f:0}%) sent for repair");
            }
        }

        private void EnterState(StrategicState next, List<CombatUnit> army)
        {
            var previous = State;
            State = next;
            _stateSince = Time.time;
            Record($"State {previous} -> {next}");

            if (previous == StrategicState.Attack && next != StrategicState.Attack)
            {
                // Milestone 008 pacing: a failed attack (most of the group lost) makes the next one bigger.
                float left = _attackers.Where(u => u != null).Sum(Strength);
                // Only a real attack counts: early probes with a handful of units must not inflate every later attack.
                if (_attackStartStrength >= _cfg.AttackStrengthThreshold && left < _attackStartStrength * 0.5f && _escalation < _cfg.MaxEscalation)
                {
                    _escalation = Mathf.Min(_cfg.MaxEscalation, _escalation + _cfg.EscalationStep);
                    Record($"Attack failed ({left:0.0}/{_attackStartStrength:0.0} strength left): next attack group +{_escalation}");
                }
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

        private CommanderSnapshot BuildSnapshot(List<CombatUnit> army, List<Health> threats, List<CombatUnit> scouts)
        {
            var faction = MatchController.Current.GetFaction(factionId);
            var s = new CommanderSnapshot
            {
                Credits = faction.Wallet.Credits,
                PowerProduced = faction.Power.Produced,
                PowerConsumed = faction.Power.Consumed,
                HasHeadquarters = Home() != null,
                KnownEnemyLight = _perception.KnownEnemyLight,
                KnownEnemyHeavy = _perception.KnownEnemyHeavy,
                KnownEnemyArtillery = _perception.KnownEnemyArtillery,
                KnownEnemyDefences = _perception.KnownEnemyDefences,
                KnownEnemyStructures = _perception.Structures.Count,
                RecentlyAttacked = Time.time - _lastThreatTime < 60f,
                Recon = scouts.Count,
                ResearchBusy = faction.Research.Current != null,
                KnownEnemyStrength = _perception.KnownEnemyLight * ArmyMath.LightValue + _perception.KnownEnemyHeavy * ArmyMath.HeavyValue
                                     + _perception.KnownEnemyArtillery * ArmyMath.ArtilleryValue,
                MatchTime = Time.timeSinceLevelLoad,
                EnemyBaseKnown = _intel != null && _intel.EnemyLocated,
                AttackEscalation = _escalation,
                ReconOnCooldown = _intel != null && _intel.EnemyLocated && _cfg.ReconReplaceSeconds > 0f
                                  && Time.time - _lastReconQueued < _cfg.ReconReplaceSeconds,
            };

            var refineries = new List<Vector3>();
            foreach (var b in OwnStructures())
            {
                switch (b.Definition.Id)
                {
                    case "refinery": s.Refineries++; refineries.Add(b.FrontPoint); break;
                    case "power_plant": s.PowerPlants++; break;
                    case "vehicle_factory": s.Factories++; break;
                    case "research_centre": s.ResearchCentres++; break;
                    case "guard_tower": s.GuardTowers++; break;
                    case "at_turret": s.AtTurrets++; break;
                    case "repair_pad": s.RepairPads++; break;
                }
            }

            foreach (var h in Health.All)
                if (h.IsAlive && h.FactionId == factionId && h.GetComponent<Harvester>() != null) s.Harvesters++;

            foreach (var u in army)
            {
                if (IsArtillery(u)) s.Artillery++;
                else if (IsHeavy(u)) s.HeavyTanks++;
                else s.LightTanks++;
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
                    else if (_reconDef != null && item.Id == _reconDef.Id) s.QueuedRecon++;
                    else if (_artilleryDef != null && item.Id == _artilleryDef.Id) s.QueuedArtillery++;
                }
            }

            foreach (var t in threats)
            {
                s.ThreatStrength += ArmyMath.Strength(UnitValue(t.GetComponent<UnitIdentity>()), t.Model.Fraction);
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
            ChooseResearch(ref s, faction); // after the counts it depends on (harvesters)
            var homeBuilding = Home();
            if (homeBuilding != null) FillTerritorySnapshot(ref s, homeBuilding.transform.position);
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

            if (action.Type == AiActionType.StartResearch) StartResearch(action);
            else if (action.IsConstruction) Construct(action);
            else if (action.IsProduction) Produce(action);
        }

        private void Construct(AiAction action)
        {
            var def = action.Type == AiActionType.BuildRefinery ? _refineryDef
                : action.Type == AiActionType.BuildPowerPlant || action.Type == AiActionType.ExtendBuildRange ? _powerPlantDef
                : action.Type == AiActionType.BuildResearchCentre ? _researchDef
                : action.Type == AiActionType.BuildGuardTower ? _towerDef
                : action.Type == AiActionType.BuildAtTurret ? _atDef
                : action.Type == AiActionType.BuildRepairPad ? _repairPadDef : _factoryDef;
            if (def == null) return;

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
                : action.Type == AiActionType.ProduceHeavyTank ? _heavyDef
                : action.Type == AiActionType.ProduceRecon ? _reconDef
                : action.Type == AiActionType.ProduceArtillery ? _artilleryDef
                : action.Type == AiActionType.ProduceMcv ? _mcvDef : _lightDef;
            if (def == null) return;

            VehicleFactory best = null;
            foreach (var f in VehicleFactory.All)
                if (f.FactionId == factionId && f.CanProduce(def) && (best == null || f.Queue.Count < best.Queue.Count))
                    best = f;
            if (best == null) return;

            var result = best.Enqueue(def);
            if (result == EnqueueResult.Ok)
            {
                if (def == _reconDef) _lastReconQueued = Time.time;
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
            var hq = Home();
            if (hq == null || placement == null) return false;
            Vector3 home = hq.transform.position;
            // Milestone 008: "towards the enemy" follows the ground route, not the straight line (ridges, passes).
            Vector3 enemyDir = RouteDirection(home);

            List<Vector3> candidates;
            switch (type)
            {
                case AiActionType.ExtendBuildRange:
                    candidates = CreepCandidates(home);
                    break;
                case AiActionType.BuildPowerPlant:
                    candidates = SiteSearch.Ranked(home, home - enemyDir * 10f, 11f, 24f, 2f);
                    break;
                case AiActionType.BuildFactory:
                    candidates = SiteSearch.Ranked(home, home + enemyDir * 10f, 14f, 26f, 2f);
                    break;
                case AiActionType.BuildResearchCentre:
                    candidates = SiteSearch.Ranked(home, home - enemyDir * 8f, 10f, 24f, 2f);
                    break;
                case AiActionType.BuildRepairPad:
                    // Beside the base, between it and the rally point: damaged units do not drive through the enemy.
                    candidates = SiteSearch.Ranked(home, home + enemyDir * 6f + new Vector3(enemyDir.z, 0f, -enemyDir.x) * 14f, 12f, 28f, 2f);
                    break;
                case AiActionType.BuildGuardTower when UndefendedForwardBase() is Building fcc:
                    // Milestone 010: guard the forward base, on its side facing home (inside its territory).
                    candidates = SiteSearch.Ranked(fcc.transform.position, home, 10f, 22f, 2f);
                    break;
                case AiActionType.BuildGuardTower:
                case AiActionType.BuildAtTurret:
                    // On the approach route from the enemy, a little in front of the base (at a pass if there is one).
                    candidates = SiteSearch.Ranked(home, PointAlongRoute(home, 18f), 12f, 26f, 2f);
                    break;
                default:
                    var deposit = DepositForNewRefinery(home);
                    if (deposit == null) return false;
                    // Towards the anchor whose territory holds the field (home, or a forward base).
                    var anchor = NearestAnchor(deposit.transform.position, home);
                    candidates = SiteSearch.Ranked(deposit.transform.position, anchor, 10f, 18f, 2f);
                    break;
            }

            bool facesSouth = FacesSouth();
            int checks = 0;
            foreach (var c in candidates)
            {
                if (++checks > 500) break;
                // Milestone 010: the faction's construction territory applies exactly as for the player.
                if (placement.Validate(def, c, facesSouth, factionId) == PlacementResult.Valid)
                {
                    site = c;
                    return true;
                }
            }
            return false;
        }

        private void StartResearch(AiAction action)
        {
            var topic = MatchController.Current.Setup.FindResearch(action.Topic);
            if (topic == null) return;
            var result = ResearchCentre.TryStart(factionId, topic);
            if (result == Ironfront.Core.Research.ResearchCheck.Ok)
            {
                ResearchStarted++;
                _lastLoggedAction = null;
                Record($"Research {topic.DisplayName} started");
            }
            else
            {
                Record($"Research {topic.DisplayName} refused: {result}");
                _cooldownUntil[AiActionType.StartResearch] = Time.time + 10f;
            }
        }

        /// <summary>Picks the next research topic with the same checks the player's buttons use.</summary>
        private void ChooseResearch(ref CommanderSnapshot s, Ironfront.Core.Economy.FactionState faction)
        {
            var setup = MatchController.Current.Setup;
            if (setup.Research.Count == 0) return;
            var options = new List<ExpansionRules.TopicOption>();
            foreach (var r in setup.Research)
            {
                if (r == null) continue;
                bool prereq = true;
                foreach (var need in r.RequiredResearch) if (!faction.Research.IsResearched(need)) prereq = false;
                options.Add(new ExpansionRules.TopicOption
                {
                    Id = r.Id, Cost = r.Cost, Done = faction.Research.IsResearched(r.Id) || faction.Research.IsInProgress(r.Id), PrerequisitesMet = prereq,
                });
            }
            s.WantedResearch = ExpansionRules.ChooseResearch(s, options);
            var wanted = s.WantedResearch != null ? setup.FindResearch(s.WantedResearch) : null;
            s.WantedResearchCost = wanted != null ? wanted.Cost : 0;
        }

        private OreDeposit DepositForNewRefinery(Vector3 home)
        {
            var refineries = OwnStructures().Where(b => b.Definition.Id == "refinery").Select(b => b.transform.position).ToList();
            // Milestone 008: ranked by ground travel distance; deposits the commander's units cannot reach are skipped.
            // Addendum: only ore fields this faction has actually seen.
            var reachable = OreDeposit.All.Where(d => !d.IsDepleted && Ironfront.Vision.VisionSystem.IsExplored(factionId, d.transform.position)
                                                      && GroundDistance(home, d) < float.MaxValue
                                                      && RefinerySiteInTerritory(d.transform.position)).ToList(); // Milestone 010
            return reachable
                .Where(d => refineries.Count == 0 || refineries.All(r => (r - d.transform.position).sqrMagnitude > 18f * 18f))
                .OrderBy(d => GroundDistance(home, d))
                .FirstOrDefault()
                ?? reachable.OrderBy(d => GroundDistance(home, d)).FirstOrDefault();
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
            var hq = Home();
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
            var hq = Home();
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

            // Only what the commander has seen: an objective is "gone" once it is no longer remembered.
            bool objectiveGone = _objectiveTarget != null && !_perception.Remembers(_objectiveTarget);
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
                    if (_objectiveTarget != null && _perception.Visible.Contains(_objectiveTarget))
                        Order(u, c => c.OrderAttack(_objectiveTarget));
                    else if (IsArtillery(u) && (pos - objective).sqrMagnitude > 24f * 24f)
                        Order(u, c => c.OrderAttackMove(StandOff(objective, pos))); // artillery stays at range
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
                if (s.Health == exclude) continue;
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
            // Milestone 008: no structure known. Go where the enemy was last seen, else search the nearest candidate
            // start site nobody has looked at yet; the army explores as it goes (sites it sees are crossed off).
            var focus = _intel.KnownEnemyFocus();
            var site = _intel.NextSiteToInvestigate(from.x, from.z, Time.time);
            if (focus != null && (from - new Vector3(focus.Value.x, 0f, focus.Value.z)).sqrMagnitude > 15f * 15f)
            {
                _objective = new Vector3(focus.Value.x, 0f, focus.Value.z);
                Objective = $"Advance on last known enemy position ({_objective.Value.x:0},{_objective.Value.z:0})";
            }
            else if (site != null)
            {
                _objective = new Vector3(site.X, 0f, site.Z);
                Objective = $"Search start site {site.Id} ({site.X:0},{site.Z:0}) for the enemy";
            }
            else
            {
                _objective = Vector3.zero; // every site checked: sweep towards the map centre
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
                // Artillery attack-moves to a stand-off point behind the main group.
                if (IsArtillery(list[i])) slot = StandOff(_objective.Value, list[i].transform.position);
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

        /// <summary>
        /// The building the commander works from: its headquarters, or (Annihilation mode, Milestone 008) any surviving
        /// vehicle factory once the headquarters has fallen, since the faction is still in the match.
        /// </summary>
        private Building Home()
        {
            var hq = HeadquartersBuilding();
            if (hq != null) return hq;
            var flow = MatchFlowController.Current;
            if (flow == null || flow.Victory != Ironfront.Core.Skirmish.VictoryMode.Annihilation) return null;
            foreach (var b in OwnStructures())
                if (b.Definition.Id == "vehicle_factory") return b;
            foreach (var b in OwnStructures())
                if (b.Definition.Id == MatchFlowController.ForwardCommandId) return b;
            return null;
        }

        private Building HeadquartersBuilding()
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

        private bool IsRecon(CombatUnit u)
        {
            var identity = u != null ? u.GetComponent<UnitIdentity>() : null;
            return _reconDef != null && identity != null && identity.Definition == _reconDef;
        }

        private bool IsArtillery(CombatUnit u)
        {
            var identity = u != null ? u.GetComponent<UnitIdentity>() : null;
            return _artilleryDef != null && identity != null && identity.Definition == _artilleryDef;
        }

        private static float UnitValue(UnitIdentity identity)
        {
            string id = identity != null && identity.Definition != null ? identity.Definition.Id : "light_tank";
            switch (id)
            {
                case "heavy_tank": return ArmyMath.HeavyValue;
                case "rocket_artillery": return ArmyMath.ArtilleryValue;
                case "recon": return ArmyMath.ReconValue;
                default: return ArmyMath.LightValue;
            }
        }

        private float Strength(CombatUnit u)
        {
            if (u == null) return 0f;
            var h = u.GetComponent<Health>();
            return h == null ? 0f : ArmyMath.Strength(UnitValue(u.GetComponent<UnitIdentity>()), h.Model.Fraction);
        }

        /// <summary>A point 22 m short of the objective on the approach line (inside rocket range, outside defences).</summary>
        private static Vector3 StandOff(Vector3 objective, Vector3 from)
        {
            Vector3 d = from - objective;
            d.y = 0f;
            if (d.sqrMagnitude < 1e-3f) d = Vector3.back;
            Vector3 p = objective + d.normalized * 22f;
            return NavMesh.SamplePosition(p, out var hit, 6f, NavMesh.AllAreas) ? hit.position : p;
        }

        // ---------- Milestone 007: scouting and counter-artillery ----------

        /// <summary>
        /// Recon units explore the nearest unexplored area (biased towards the enemy side) using only this
        /// faction's own exploration record; once the map is explored they watch the approach to the base.
        /// Damaged scouts withdraw.
        /// </summary>
        private void ManageScouts(List<CombatUnit> scouts)
        {
            var vision = Ironfront.Vision.VisionSystem.Current;
            foreach (var key in _scoutTargets.Keys.Where(k => k == null).ToList()) _scoutTargets.Remove(key);
            // End game: no ore left anywhere, so information no longer pays; scouts join the final attack
            // (otherwise an idle scout at home keeps a spent faction "able to act" and the match never ends).
            if (!LastSnapshot.OreAvailable)
            {
                foreach (var u in scouts)
                {
                    if (u.Machine.Order == CombatOrderType.AttackMove || u.Machine.Order == CombatOrderType.Attack) continue;
                    Vector3 goal = _objective ?? EnemyDirectionTarget(u.transform.position);
                    _scoutTargets.Remove(u);
                    Order(u, c => c.OrderAttackMove(goal));
                    Record($"Scout {u.name} joins the final attack");
                }
                return;
            }
            foreach (var u in scouts)
            {
                var h = u.GetComponent<Health>();
                if (h.Model.Fraction < 0.4f)
                {
                    if (u.Machine.Order != CombatOrderType.Move) Order(u, c => c.OrderMove(RallyPoint));
                    _scoutTargets.Remove(u);
                    continue;
                }
                bool arrived = !_scoutTargets.TryGetValue(u, out var target) || (u.transform.position - target).sqrMagnitude < 6f * 6f
                               || u.Machine.Order == CombatOrderType.Idle;
                if (!arrived) continue;
                Vector3 interest = EnemyDirectionTarget(u.transform.position);
                Vector3 next;
                // Nowhere known to expand to: finding ore near home comes before finding the enemy.
                var ore = NeedsExpansionIntel() ? UnexploredDeposit(Home().transform.position) : null;
                var site = ore != null ? null : _intel.NextSiteToInvestigate(u.transform.position.x, u.transform.position.z, Time.time,
                    exclude: s => IsRejectedScoutPoint(s.X, s.Z) || _scoutTargets.Any(t => t.Key != u && (t.Value - new Vector3(s.X, 0f, s.Z)).sqrMagnitude < 30f * 30f));
                if (ore == null && site == null) ore = UnexploredDeposit(u.transform.position);
                if (ore != null)
                    next = ore.transform.position; // resource discovery for expansion
                else if (site != null)
                    next = new Vector3(site.X, 0f, site.Z); // where might the enemy be?
                else if (vision != null && vision.FogEnabled && ScoutPlanner.NextWaypoint(vision.Grid, factionId, u.transform.position.x, u.transform.position.z,
                        interest.x, interest.z, 12f, 0.35f, out float x, out float z, skip: IsRejectedScoutPoint))
                    next = new Vector3(x, 0f, z);
                else
                    next = PointAlongRoute(RallyPoint, 0.45f * Vector3.Distance(RallyPoint, interest)); // explored: watch the approach
                var wanted = next;
                if (NavMesh.SamplePosition(next, out var hit, 8f, NavMesh.AllAreas) && PathComplete(u.transform.position, hit.position))
                    next = hit.position;
                else
                {
                    // Unreachable (enclosed by terrain): remember it and never order it again.
                    RejectScoutPoint(wanted);
                    Record($"Scout point ({wanted.x:0},{wanted.z:0}) unreachable; skipped");
                    continue;
                }
                // Already standing at the chosen point (nothing left to explore: watching the approach): stay put
                // instead of re-issuing the same order every decision.
                if ((u.transform.position - next).sqrMagnitude < 8f * 8f)
                {
                    _scoutTargets[u] = next;
                    continue;
                }
                _scoutTargets[u] = next;
                ScoutMissions++;
                Order(u, c => c.OrderMove(next));
                Record($"Scout {u.name} -> ({next.x:0},{next.z:0})");
            }
        }

        /// <summary>
        /// Enemy artillery shelling the base (visible: attackers reveal themselves when they fire) is hunted by
        /// up to three fast units, since artillery cannot fire at close targets.
        /// </summary>
        private void CounterArtillery(List<CombatUnit> army, List<Vector3> basePoints)
        {
            _hunters.RemoveWhere(u => u == null || u.Machine == null || u.Machine.Order != CombatOrderType.Attack);
            Health target = null;
            float best = float.MaxValue;
            foreach (var h in _perception.Visible)
            {
                var identity = h.GetComponent<UnitIdentity>();
                if (identity == null || identity.Definition == null || identity.Definition.Id != "rocket_artillery") continue;
                foreach (var p in basePoints)
                {
                    float d = (h.transform.position - p).sqrMagnitude;
                    if (d < (difficulty.DefendRadius + 16f) * (difficulty.DefendRadius + 16f) && d < best) { best = d; target = h; }
                }
            }
            if (target == null) return;
            var fast = army.Where(u => !IsArtillery(u) && !IsHeavy(u) && !_hunters.Contains(u) && !_attackers.Contains(u))
                .OrderBy(u => (u.transform.position - target.transform.position).sqrMagnitude).Take(Mathf.Max(0, 3 - _hunters.Count)).ToList();
            foreach (var u in fast)
            {
                _hunters.Add(u);
                Order(u, c => c.OrderAttack(target));
            }
            if (fast.Count > 0) Record($"Counter-artillery: {fast.Count} units hunt {target.DisplayName}");
        }

        // ---------- Milestone 008: intelligence (no hidden knowledge of the enemy start) ----------

        private EnemyIntel _intel;
        private bool _announcedDiscovery;
        private int _escalation;
        private float _lastReconQueued = -999f;

        /// <summary>Extra attack-group size earned by failed attacks (diagnostics / tests).</summary>
        public int AttackEscalation => _escalation;

        /// <summary>What the commander believes about the enemy's whereabouts (diagnostics / tests).</summary>
        public EnemyIntel Intel => _intel;

        /// <summary>
        /// Candidate enemy start sites: the map's start slots other than its own (public map knowledge — the setup
        /// screen shows them to the player as well). Older fixed-layout scenes without slots list their start markers.
        /// Which site the enemy actually took is never read.
        /// </summary>
        private IEnumerable<(int, float, float)> CandidateSites()
        {
            var home = Home() != null ? Home().transform.position : transform.position;
            if (Ironfront.Skirmish.StartSlot.All.Count > 0)
            {
                foreach (var s in Ironfront.Skirmish.StartSlot.All)
                    if ((s.Headquarters - home).sqrMagnitude > 20f * 20f) yield return (s.Slot, s.Headquarters.x, s.Headquarters.z);
                yield break;
            }
            int i = 0;
            foreach (var s in FactionStartLocation.All)
            {
                if (s.FactionId != factionId && (s.transform.position - home).sqrMagnitude > 20f * 20f)
                    yield return (i, s.transform.position.x, s.transform.position.z);
                i++;
            }
        }

        private readonly List<EnemyIntel.Sighting> _sightings = new List<EnemyIntel.Sighting>();

        private void UpdateIntel()
        {
            _sightings.Clear();
            foreach (var s in _perception.Structures)
                _sightings.Add(new EnemyIntel.Sighting(s.Position.x, s.Position.z, s.DefinitionId == "headquarters", s.LastSeen));
            var vision = Ironfront.Vision.VisionSystem.Current;
            bool fog = vision != null && vision.FogEnabled;
            float range2 = difficulty.VisionRange * difficulty.VisionRange;
            _intel.Observe(Time.time, (x, z) =>
            {
                var p = new Vector3(x, 0f, z);
                if (fog) return Ironfront.Vision.VisionSystem.CanSeePoint(factionId, p);
                foreach (var h in Health.All)
                    if (h.IsAlive && h.FactionId == factionId && (h.transform.position - p).sqrMagnitude <= range2) return true;
                return false;
            }, _sightings);
            if (_intel.EnemyLocated && !_announcedDiscovery)
            {
                _announcedDiscovery = true;
                var f = _intel.KnownEnemyFocus().Value;
                Record($"Enemy located at ({f.x:0},{f.z:0}) after {Time.timeSinceLevelLoad:0}s");
            }
        }

        /// <summary>The commander builds with docks and exits facing the map centre (north or south).</summary>
        private bool FacesSouth()
        {
            var hq = Home();
            return hq != null && hq.transform.position.z > 0f;
        }

        /// <summary>Where "towards the enemy" is: the known or most likely enemy position, otherwise the map centre.</summary>
        private Vector3 EnemyDirectionTarget(Vector3 from)
        {
            var g = _intel?.BestGuess(from.x, from.z, Time.time);
            return g != null ? new Vector3(g.Value.x, 0f, g.Value.z) : Vector3.zero;
        }

        private Vector3 ComputeRallyPoint(Vector3 hq)
        {
            // Milestone 008: along the ground route, so the rally point never snaps to the far side of a ridge.
            Vector3 p = PointAlongRoute(hq, difficulty.RallyDistance) + _rallyOffset;
            return NavMesh.SamplePosition(p, out var hit, 8f, NavMesh.AllAreas) ? hit.position : p;
        }

        // ---------- Milestone 008: terrain awareness (ground routes, reachability) ----------

        private NavMeshPath _path;
        private readonly List<Vector3> _route = new List<Vector3>();
        private Vector3 _routeFrom = new Vector3(float.NaN, 0f, 0f), _routeTo;
        private readonly Dictionary<OreDeposit, float> _depositDistance = new Dictionary<OreDeposit, float>();
        private Vector3 _depositFrom = new Vector3(float.NaN, 0f, 0f);
        private readonly List<Vector3> _rejectedScoutPoints = new List<Vector3>();

        /// <summary>Scout destinations found unreachable and never ordered again (diagnostics / tests).</summary>
        public int RejectedScoutPoints => _rejectedScoutPoints.Count;
        /// <summary>The current ground route towards the enemy (diagnostics / tests).</summary>
        public IReadOnlyList<Vector3> CurrentRoute => _route;

        private NavMeshPath ScratchPath => _path ??= new NavMeshPath();

        private static bool OnNavMesh(Vector3 p, float radius, out Vector3 snapped)
        {
            bool ok = NavMesh.SamplePosition(p, out var hit, radius, NavMesh.AllAreas);
            snapped = ok ? hit.position : p;
            return ok;
        }

        private bool PathComplete(Vector3 from, Vector3 to)
        {
            if (!OnNavMesh(from, 4f, out var a) || !OnNavMesh(to, 4f, out var b)) return false;
            return NavMesh.CalculatePath(a, b, NavMesh.AllAreas, ScratchPath) && ScratchPath.status == NavMeshPathStatus.PathComplete;
        }

        /// <summary>Ground route from home to where the enemy is expected (cached; recomputed when either end moves).</summary>
        private List<Vector3> Route(Vector3 home)
        {
            Vector3 target = EnemyDirectionTarget(home);
            if ((home - _routeFrom).sqrMagnitude < 1f && (target - _routeTo).sqrMagnitude < 1f) return _route;
            _routeFrom = home;
            _routeTo = target;
            _route.Clear();
            // Structures carve the NavMesh, so both ends are snapped to the nearest walkable ground.
            if (OnNavMesh(home, 14f, out var a) && OnNavMesh(target, 14f, out var b)
                && NavMesh.CalculatePath(a, b, NavMesh.AllAreas, ScratchPath) && ScratchPath.status != NavMeshPathStatus.PathInvalid)
            {
                _route.Add(home);
                _route.AddRange(ScratchPath.corners);
            }
            return _route;
        }

        /// <summary>The point <paramref name="distance"/> metres along the ground route towards the enemy.</summary>
        private Vector3 PointAlongRoute(Vector3 home, float distance)
        {
            var route = Route(home);
            if (route.Count < 2)
            {
                Vector3 d = EnemyDirectionTarget(home) - home;
                d.y = 0f;
                return home + (d.sqrMagnitude > 1e-3f ? d.normalized : Vector3.forward) * distance;
            }
            float left = distance;
            for (int i = 1; i < route.Count; i++)
            {
                float seg = Vector3.Distance(route[i - 1], route[i]);
                if (seg >= left) return Vector3.Lerp(route[i - 1], route[i], seg <= 1e-4f ? 0f : left / seg);
                left -= seg;
            }
            return route[route.Count - 1];
        }

        /// <summary>Initial direction of the ground route towards the enemy (falls back to the straight line).</summary>
        private Vector3 RouteDirection(Vector3 home)
        {
            Vector3 d = PointAlongRoute(home, 14f) - home;
            d.y = 0f;
            return d.sqrMagnitude > 1e-3f ? d.normalized : Vector3.forward;
        }

        /// <summary>Path length from home to a deposit's edge (float.MaxValue = unreachable). Cached per home position.</summary>
        private float GroundDistance(Vector3 home, OreDeposit deposit)
        {
            if ((home - _depositFrom).sqrMagnitude > 1f)
            {
                _depositFrom = home;
                _depositDistance.Clear();
            }
            if (_depositDistance.TryGetValue(deposit, out float d)) return d;
            d = float.MaxValue;
            Vector3 target = deposit.transform.position;
            if (OnNavMesh(home, 14f, out var a) && OnNavMesh(target, deposit.HarvestRadius + 4f, out var b)
                && NavMesh.CalculatePath(a, b, NavMesh.AllAreas, ScratchPath) && ScratchPath.status == NavMeshPathStatus.PathComplete)
            {
                d = 0f;
                var c = ScratchPath.corners;
                for (int i = 1; i < c.Length; i++) d += Vector3.Distance(c[i - 1], c[i]);
            }
            _depositDistance[deposit] = d;
            return d;
        }

        private void RejectScoutPoint(Vector3 p) => _rejectedScoutPoints.Add(p);

        /// <summary>
        /// True when every ore field the commander has seen is either spent or already served by one of its refineries:
        /// its next expansion depends on discovering more ore.
        /// </summary>
        private bool NeedsExpansionIntel()
        {
            var refineries = OwnStructures().Where(b => b.Definition.Id == "refinery").Select(b => b.transform.position).ToList();
            foreach (var d in OreDeposit.All)
            {
                if (d.IsDepleted || !Ironfront.Vision.VisionSystem.IsExplored(factionId, d.transform.position)) continue;
                if (refineries.All(r => (r - d.transform.position).sqrMagnitude > LocalOreRadius * LocalOreRadius)) return false;
            }
            return true;
        }

        /// <summary>Nearest ore field this faction has never seen (for scouts), skipping ones another scout is visiting.</summary>
        private OreDeposit UnexploredDeposit(Vector3 from)
        {
            OreDeposit best = null;
            float bestD = float.MaxValue;
            foreach (var d in OreDeposit.All)
            {
                var p = d.transform.position;
                if (d.IsDepleted || Ironfront.Vision.VisionSystem.IsExplored(factionId, p) || IsRejectedScoutPoint(p.x, p.z)) continue;
                if (_scoutTargets.Values.Any(t => (t - p).sqrMagnitude < 20f * 20f)) continue;
                float dist = (p - from).sqrMagnitude;
                if (dist < bestD) { bestD = dist; best = d; }
            }
            return best;
        }

        private bool IsRejectedScoutPoint(float x, float z)
        {
            foreach (var r in _rejectedScoutPoints)
                if ((r.x - x) * (r.x - x) + (r.z - z) * (r.z - z) < 36f) return true;
            return false;
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

            var setup2 = MatchController.Current.Setup;
            _researchDef = setup2.ConstructionOptions.FirstOrDefault(d => d != null && d.Id == "research_centre");
            _towerDef = setup2.ConstructionOptions.FirstOrDefault(d => d != null && d.Id == "guard_tower");
            _atDef = setup2.ConstructionOptions.FirstOrDefault(d => d != null && d.Id == "at_turret");
            _reconDef = _factoryDef.ProducibleUnits.FirstOrDefault(u => u != null && u.Id == "recon");
            _repairPadDef = setup2.ConstructionOptions.FirstOrDefault(d => d != null && d.Id == "repair_pad");
            _artilleryDef = _factoryDef.ProducibleUnits.FirstOrDefault(u => u != null && u.Id == "rocket_artillery");
            ResolveForwardDefinitions(); // Milestone 010

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
                Recon = _reconDef != null ? _reconDef.Cost : 0,
                Artillery = _artilleryDef != null ? _artilleryDef.Cost : 0,
                ResearchCentre = _researchDef != null ? _researchDef.Cost : 0,
                GuardTower = _towerDef != null ? _towerDef.Cost : 0,
                AtTurret = _atDef != null ? _atDef.Cost : 0,
                RepairPad = _repairPadDef != null ? _repairPadDef.Cost : 0,
                Mcv = _mcvDef != null ? _mcvDef.Cost : 0,
            };
            return true;
        }

        private Building UndefendedForwardBase()
        {
            foreach (var b in OwnStructures())
            {
                if (!IsForwardBase(b)) continue;
                var p = b.transform.position;
                if (!OwnStructures().Any(o => (o.Definition.Id == "guard_tower" || o.Definition.Id == "at_turret") && (o.transform.position - p).sqrMagnitude < 30f * 30f))
                    return b;
            }
            return null;
        }

        private Vector3 NearestAnchor(Vector3 p, Vector3 fallback)
        {
            float best = float.MaxValue;
            Vector3 result = fallback;
            foreach (var a in TerritorySystem.Map.AnchorsOf(factionId))
            {
                var c = new Vector3(a.Centre.x, 0f, a.Centre.y);
                float d = (c - p).sqrMagnitude;
                if (d < best) { best = d; result = c; }
            }
            return result;
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
