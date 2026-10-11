using System.Collections.Generic;
using Ironfront.Combat;
using Ironfront.Core.Research;
using Ironfront.Data;
using Ironfront.Match;
using UnityEngine;

namespace Ironfront.Buildings
{
    /// <summary>
    /// Research facility (Milestone 007). A faction with at least one Research Centre can research one topic at a
    /// time from <see cref="MatchSetup.Research"/>; progress runs at half speed on low power and pauses if every
    /// centre is lost. Research state lives in the faction's per-match <see cref="FactionState"/>, so nothing
    /// carries over between matches. Completed armour research is applied to existing vehicles here; damage and
    /// harvesting bonuses are read live by the systems that use them.
    /// </summary>
    [RequireComponent(typeof(Building))]
    public class ResearchCentre : MonoBehaviour
    {
        public const string StructureId = "research_centre";

        private static readonly List<ResearchCentre> s_All = new List<ResearchCentre>();
        private static readonly Dictionary<int, int> s_tickedFrame = new Dictionary<int, int>();

        private Building _building;

        public static IReadOnlyList<ResearchCentre> All => s_All;
        public int FactionId => _building != null ? _building.FactionId : FactionMember.Of(this);

        private void Awake() => _building = GetComponent<Building>();
        private void OnEnable() { if (!s_All.Contains(this)) s_All.Add(this); }
        private void OnDisable() => s_All.Remove(this);

        private void Update()
        {
            var match = MatchController.Current;
            if (match == null) return;
            int faction = FactionId;
            // One centre per faction advances the shared research each frame.
            if (s_tickedFrame.TryGetValue(faction, out int frame) && frame == Time.frameCount) return;
            s_tickedFrame[faction] = Time.frameCount;
            var state = match.GetFaction(faction);
            float speed = state.Power.IsLowPower && match.Setup != null ? match.Setup.LowPowerProductionMultiplier : 1f;
            state.Research.Tick(Time.deltaTime, speed);
        }

        /// <summary>True if <paramref name="faction"/> owns a working research facility.</summary>
        public static bool HasFacility(int faction)
        {
            var match = MatchController.Current;
            return match != null && match.GetFaction(faction).Structures.Has(StructureId);
        }

        /// <summary>Starts research for a faction through the same rules the player's buttons use.</summary>
        public static ResearchCheck TryStart(int faction, ResearchDefinition topic)
        {
            var match = MatchController.Current;
            if (match == null || topic == null) return ResearchCheck.MissingFacility;
            var state = match.GetFaction(faction);
            return state.Research.TryStart(topic, HasFacility(faction), state.Wallet);
        }

        public static ResearchCheck Check(int faction, ResearchDefinition topic)
        {
            var match = MatchController.Current;
            if (match == null || topic == null) return ResearchCheck.MissingFacility;
            var state = match.GetFaction(faction);
            return state.Research.Check(topic, HasFacility(faction), state.Wallet);
        }

        /// <summary>
        /// Applies completed armour research to the faction's existing vehicles (new ones apply it on spawn).
        /// Called once per faction state by <see cref="MatchController"/>.
        /// </summary>
        public static void HookUpgrades(ResearchState research, int faction)
        {
            research.Completed += _ =>
            {
                foreach (var h in Health.All)
                    if (h != null && h.IsAlive && !h.IsStructure && h.FactionId == faction) h.ApplyArmourBonus(research.ArmourBonus);
            };
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_All.Clear();
            s_tickedFrame.Clear();
        }
    }
}
