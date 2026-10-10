using System.Collections.Generic;

namespace Ironfront.Core.Victory
{
    public enum MatchResult
    {
        Ongoing,
        /// <summary>Exactly one faction still has its headquarters.</summary>
        Victory,
        /// <summary>The last headquarters fell together (same evaluation): nobody wins.</summary>
        Draw,
    }

    public enum EndReason
    {
        None,
        HeadquartersDestroyed,
        SimultaneousHeadquarters,
        /// <summary>No faction can affect the game any more (no army, no money, no ore to earn more).</summary>
        Exhaustion,
    }

    public readonly struct MatchOutcome
    {
        public readonly MatchResult Result;
        public readonly int WinnerFactionId;
        public readonly EndReason Reason;

        public MatchOutcome(MatchResult result, int winner, EndReason reason = EndReason.None)
        {
            Result = result;
            WinnerFactionId = winner;
            Reason = reason;
        }

        public bool IsOver => Result != MatchResult.Ongoing;
    }

    /// <summary>
    /// Victory rules: a faction is defeated when its headquarters is destroyed. Evaluated once per frame after
    /// all deaths are processed, so headquarters destroyed in the same frame are treated as simultaneous.
    /// </summary>
    public static class MatchOutcomeRules
    {
        /// <param name="headquartersAlive">Each competing faction and whether its headquarters still stands.</param>
        public static MatchOutcome Evaluate(IReadOnlyList<(int factionId, bool headquartersAlive)> headquartersAlive)
        {
            int alive = 0, lastAlive = 0;
            foreach (var (factionId, hqAlive) in headquartersAlive)
            {
                if (!hqAlive) continue;
                alive++;
                lastAlive = factionId;
            }

            if (headquartersAlive.Count < 2) return new MatchOutcome(MatchResult.Ongoing, 0);
            if (alive == 1) return new MatchOutcome(MatchResult.Victory, lastAlive, EndReason.HeadquartersDestroyed);
            if (alive == 0) return new MatchOutcome(MatchResult.Draw, 0, EndReason.SimultaneousHeadquarters);
            return new MatchOutcome(MatchResult.Ongoing, 0);
        }

        /// <summary>Stalemate rule: if no competing faction can still affect the game, the match is a draw.</summary>
        /// <param name="canStillAct">For each competing faction, whether it can still fight or rebuild.</param>
        public static MatchOutcome EvaluateExhaustion(IReadOnlyList<bool> canStillAct)
        {
            if (canStillAct.Count < 2) return new MatchOutcome(MatchResult.Ongoing, 0);
            foreach (bool can in canStillAct)
                if (can) return new MatchOutcome(MatchResult.Ongoing, 0);
            return new MatchOutcome(MatchResult.Draw, 0, EndReason.Exhaustion);
        }

        /// <summary>
        /// Whether a faction can still influence the match: it has armed units or units in production, can afford
        /// a combat unit at a factory, holds unbanked ore, or can still earn (ore remains, and it has or can buy
        /// the refinery and harvester it needs).
        /// </summary>
        public static bool CanStillAct(int armedUnits, int queuedUnits, bool hasFactory, int credits, int cheapestCombatUnit,
            bool oreRemains, bool hasRefinery, int harvesters, int harvesterCargo, int refineryCost, int harvesterCost)
        {
            if (armedUnits > 0 || queuedUnits > 0) return true;
            if (hasFactory && credits >= cheapestCombatUnit) return true;
            if (harvesterCargo > 0 && hasRefinery) return true;
            if (!oreRemains) return false;
            bool canRefine = hasRefinery || credits >= refineryCost;
            bool canHarvest = harvesters > 0 || (hasFactory && credits >= harvesterCost + (hasRefinery ? 0 : refineryCost));
            return canRefine && canHarvest;
        }
    }
}
