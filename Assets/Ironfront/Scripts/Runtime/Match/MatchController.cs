using System.Collections.Generic;
using Ironfront.Core.Economy;
using Ironfront.Data;
using UnityEngine;

namespace Ironfront.Match
{
    /// <summary>Owns per-faction economic state for the current match. One instance per scene.</summary>
    [DefaultExecutionOrder(-200)]
    public class MatchController : MonoBehaviour
    {
        public const int DefaultPlayerFactionId = 1;

        [SerializeField] private MatchSetup setup;

        private readonly Dictionary<int, FactionState> _factions = new Dictionary<int, FactionState>();
        private int _startingCreditsOverride = -1;

        public static MatchController Current { get; private set; }

        public MatchSetup Setup => setup;
        public IEnumerable<FactionState> Factions => _factions.Values;
        public int PlayerFactionId { get; private set; } = DefaultPlayerFactionId;
        public FactionState PlayerFaction => GetFaction(PlayerFactionId);

        /// <summary>
        /// Skirmish setup (Milestone 008): every faction starts with this many credits instead of the MatchSetup value.
        /// Must be called before Awake (the <c>SkirmishInitializer</c> runs first).
        /// </summary>
        public void SetStartingCredits(int credits)
        {
            if (_factions.Count > 0) Debug.LogWarning("[Ironfront] SetStartingCredits after the match was initialised has no effect.", this);
            _startingCreditsOverride = Mathf.Max(0, credits);
        }

        private void Awake()
        {
            if (Current != null && Current != this)
                Debug.LogWarning("[Ironfront] Multiple MatchControllers in scene; the latest one wins.", this);
            Current = this;

            if (setup == null)
            {
                Debug.LogError("[Ironfront] MatchController has no MatchSetup.", this);
                return;
            }

            bool playerChosen = false;
            foreach (var f in setup.Factions)
            {
                int credits = _startingCreditsOverride >= 0 ? _startingCreditsOverride : f.startingCredits;
                _factions[f.id] = new FactionState(f.id, f.displayName, f.humanControlled, credits);
                Buildings.ResearchCentre.HookUpgrades(_factions[f.id].Research, f.id);
                if (f.humanControlled && !playerChosen)
                {
                    PlayerFactionId = f.id;
                    playerChosen = true;
                }
            }
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
        }

        /// <summary>Returns the faction's state, creating an empty one for ids not in the setup.</summary>
        public FactionState GetFaction(int id)
        {
            if (!_factions.TryGetValue(id, out var state))
            {
                state = new FactionState(id, "Faction " + id, false, 0);
                Buildings.ResearchCentre.HookUpgrades(state.Research, id);
                _factions[id] = state;
            }
            return state;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Current = null;
    }
}
