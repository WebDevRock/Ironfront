using System;
using System.Collections.Generic;
using Ironfront.Buildings;
using Ironfront.Combat;
using Ironfront.Construction;
using Ironfront.Economy;
using Ironfront.Core.Skirmish;
using Ironfront.Core.Victory;
using Ironfront.InputBindings;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ironfront.Match
{
    /// <summary>
    /// Runs the match to its end. Default (Headquarters mode): a faction is defeated when its headquarters is
    /// destroyed. Annihilation mode (Milestone 008): a faction is eliminated when it has no headquarters or vehicle
    /// factory and no armed unit left (see <see cref="MatchOutcomeRules.EvaluateAnnihilation"/>). Outcome is
    /// evaluated once per frame in LateUpdate, so headquarters destroyed in the same frame produce a draw.
    /// On the end the game is paused, player input modes are cleared and the result is shown; commanders stop.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public class MatchFlowController : MonoBehaviour
    {
        public const string HeadquartersId = "headquarters";
        public const string FactoryId = "vehicle_factory";

        [SerializeField] private MatchResultView resultView;
        [Tooltip("Pause the simulation when the match ends.")]
        [SerializeField] private bool pauseOnEnd = true;
        [Tooltip("Seconds between stalemate (exhaustion) checks; two consecutive positive checks end the match.")]
        [SerializeField, Min(0.5f)] private float exhaustionCheckInterval = 2f;
        [SerializeField] private VictoryMode victoryMode = VictoryMode.Headquarters;

        private float _exhaustionTimer;
        private int _exhaustedChecks;

        private readonly List<int> _competitors = new List<int>();

        public static MatchFlowController Current { get; private set; }

        public bool IsOver { get; private set; }
        public MatchOutcome Outcome { get; private set; }
        public float EndTime { get; private set; }
        public IReadOnlyList<int> Competitors => _competitors;
        public VictoryMode Victory => victoryMode;

        /// <summary>Sets the victory rules for this match (skirmish setup). Call before the match starts.</summary>
        public void Configure(VictoryMode mode) => victoryMode = mode;

        public event Action<MatchOutcome> MatchEnded;

        private void Awake()
        {
            Current = this;
        }

        private void Start()
        {
            // Every faction that starts with a headquarters competes.
            foreach (var b in Building.All)
                if (b.Definition != null && b.Definition.Id == HeadquartersId && !_competitors.Contains(b.FactionId))
                    _competitors.Add(b.FactionId);
            _competitors.Sort();
            if (_competitors.Count < 2)
                Debug.LogWarning("[Ironfront] MatchFlowController: fewer than two factions have a headquarters; no victory checks.", this);
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
        }

        private void LateUpdate()
        {
            if (IsOver || _competitors.Count < 2) return;
            var outcome = victoryMode == VictoryMode.Annihilation
                ? MatchOutcomeRules.EvaluateAnnihilation(ForcesStatus())
                : MatchOutcomeRules.Evaluate(HeadquartersStatus());
            if (outcome.IsOver)
            {
                End(outcome);
                return;
            }

            _exhaustionTimer -= Time.deltaTime;
            if (_exhaustionTimer > 0f) return;
            _exhaustionTimer = exhaustionCheckInterval;
            var exhaustion = MatchOutcomeRules.EvaluateExhaustion(CapabilityStatus());
            _exhaustedChecks = exhaustion.IsOver ? _exhaustedChecks + 1 : 0;
            if (_exhaustedChecks >= 2) End(exhaustion);
        }

        /// <summary>For each competitor: can it still fight or rebuild? (See MatchOutcomeRules.CanStillAct.)</summary>
        public List<bool> CapabilityStatus()
        {
            var setup = MatchController.Current != null ? MatchController.Current.Setup : null;
            int cheapestUnit = int.MaxValue, refineryCost = int.MaxValue, harvesterCost = int.MaxValue;
            if (setup != null)
            {
                foreach (var b in setup.ConstructionOptions)
                {
                    if (b == null) continue;
                    if (b.Id == "refinery") refineryCost = b.Cost;
                    foreach (var u in b.ProducibleUnits)
                    {
                        if (u == null) continue;
                        if (u.Weapon != null) cheapestUnit = Math.Min(cheapestUnit, u.Cost);
                        else if (u.Id == "harvester") harvesterCost = u.Cost;
                    }
                }
            }

            bool oreRemains = false;
            foreach (var d in OreDeposit.All)
                if (!d.IsDepleted) { oreRemains = true; break; }

            var result = new List<bool>();
            foreach (int f in _competitors)
            {
                int armed = 0, harvesters = 0, cargo = 0, queued = 0;
                foreach (var c in CombatUnit.All)
                    if (FactionMember.Of(c) == f && c.GetComponent<Health>().IsAlive) armed++;
                foreach (var h in Health.All)
                {
                    if (!h.IsAlive || h.FactionId != f) continue;
                    var harvester = h.GetComponent<Harvester>();
                    if (harvester == null) continue;
                    harvesters++;
                    if (harvester.Machine != null) cargo += harvester.Machine.Cargo;
                }
                bool hasFactory = false, hasRefinery = false;
                foreach (var factory in VehicleFactory.All)
                    if (factory.FactionId == f) { hasFactory = true; queued += factory.Queue.Count; }
                foreach (var r in Refinery.All)
                    if (r.FactionId == f) hasRefinery = true;
                int credits = MatchController.Current != null ? MatchController.Current.GetFaction(f).Wallet.Credits : 0;

                result.Add(MatchOutcomeRules.CanStillAct(armed, queued, hasFactory, credits, cheapestUnit, oreRemains,
                    hasRefinery, harvesters, cargo, refineryCost, harvesterCost));
            }
            return result;
        }

        public List<(int, bool)> HeadquartersStatus()
        {
            var status = new List<(int, bool)>();
            foreach (int f in _competitors) status.Add((f, HasHeadquarters(f)));
            return status;
        }

        /// <summary>For each competitor: living production structures (HQ, factories) and armed mobile units.</summary>
        public List<(int, int, int)> ForcesStatus()
        {
            var status = new List<(int, int, int)>();
            foreach (int f in _competitors) status.Add((f, ProductionStructures(f), ArmedUnits(f)));
            return status;
        }

        public static int ProductionStructures(int factionId)
        {
            int n = 0;
            foreach (var b in Building.All)
            {
                if (b.FactionId != factionId || b.Definition == null) continue;
                if (b.Definition.Id != HeadquartersId && b.Definition.Id != FactoryId) continue;
                var h = b.GetComponent<Health>();
                if (h == null || h.IsAlive) n++;
            }
            return n;
        }

        public static int ArmedUnits(int factionId)
        {
            int n = 0;
            foreach (var c in CombatUnit.All)
            {
                if (c == null || FactionMember.Of(c) != factionId) continue;
                var h = c.GetComponent<Health>();
                if (h != null && h.IsAlive && !h.IsStructure) n++;
            }
            return n;
        }

        /// <summary>
        /// Annihilation "last stand": the faction can no longer produce, so its remaining forces are revealed to every
        /// faction (symmetric rule; guarantees the match can be finished). Always false in Headquarters mode.
        /// </summary>
        public bool IsLastStand(int factionId) =>
            victoryMode == VictoryMode.Annihilation && !IsOver && _competitors.Contains(factionId)
            && MatchOutcomeRules.IsLastStand(ProductionStructures(factionId));

        public static bool HasHeadquarters(int factionId)
        {
            foreach (var b in Building.All)
            {
                if (b.FactionId != factionId || b.Definition == null || b.Definition.Id != HeadquartersId) continue;
                var h = b.GetComponent<Health>();
                if (h == null || h.IsAlive) return true;
            }
            return false;
        }

        private void End(MatchOutcome outcome)
        {
            IsOver = true;
            Outcome = outcome;
            EndTime = Time.timeSinceLevelLoad;

            // Leave any player input mode so nothing is stuck after a restart.
            var placement = FindAnyObjectByType<BuildingPlacementController>();
            if (placement != null) placement.CancelPlacement();
            RtsInputMode.ResetAll();

            int player = MatchController.Current != null ? MatchController.Current.PlayerFactionId : MatchController.DefaultPlayerFactionId;
            string text = outcome.Reason == EndReason.Exhaustion ? "Draw: neither side can continue"
                : outcome.Reason == EndReason.SimultaneousAnnihilation ? "Draw: both forces eliminated together"
                : outcome.Result == MatchResult.Draw ? "Draw: both headquarters fell together"
                : outcome.WinnerFactionId == player ? "Victory" : "Defeat";
            Debug.Log($"[Ironfront] Match over after {EndTime:0}s: {text} (winner faction {outcome.WinnerFactionId})");

            MatchEnded?.Invoke(outcome);
            if (resultView != null) resultView.Show(outcome, player, EndTime);
            if (pauseOnEnd) Time.timeScale = 0f;
        }

        /// <summary>Reloads the current scene with fresh state.</summary>
        public void Restart()
        {
            Time.timeScale = 1f;
            RtsInputMode.ResetAll();
            SceneManager.LoadScene(SceneManager.GetActiveScene().name, LoadSceneMode.Single);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Current = null;
    }
}
