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

        public static MatchController Current { get; private set; }

        public MatchSetup Setup => setup;
        public IEnumerable<FactionState> Factions => _factions.Values;
        public int PlayerFactionId { get; private set; } = DefaultPlayerFactionId;
        public FactionState PlayerFaction => GetFaction(PlayerFactionId);

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
                _factions[f.id] = new FactionState(f.id, f.displayName, f.humanControlled, f.startingCredits);
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
