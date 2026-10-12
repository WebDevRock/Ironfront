using System.Collections.Generic;

namespace Ironfront.Core.AI
{
    /// <summary>
    /// Milestone 007 economic decisions for the computer commander: keep a scout, build defences when they are
    /// warranted, build a Research Centre once the army is established, and research upgrades when affordable.
    /// Each rule is inactive when its content has no cost in the <see cref="CostTable"/> (older scenes, tests).
    /// Pure and deterministic; the commander executes the result through the same systems the player uses.
    /// </summary>
    public static class ExpansionRules
    {
        public static AiAction Next(in CommanderSnapshot s, StrategicState state, in CommanderSettings cfg, in CostTable cost)
        {
            bool defending = state == StrategicState.Defend;
            // End game (no ore anywhere): no new investments, every credit goes to combat units.
            if (!s.OreAvailable) return new AiAction(AiActionType.None, "no ore left");
            bool roomInQueue = EconomyPlanner.QueueHasRoom(s, cfg);

            // Scout: a cheap recon once the first tanks exist (and replace it when lost). Milestone 008: while the enemy
            // has not been found, the first scout comes before any tank.
            if (cost.Recon > 0 && !defending && !s.ReconOnCooldown && s.Recon + s.QueuedRecon < cfg.ReconCount && s.ArmyCount >= (s.EnemyBaseKnown ? 2 : 0) && roomInQueue
                && EconomyPlanner.Affordable(s.Credits, cost.Recon))
                return new AiAction(AiActionType.ProduceRecon, "scouting");

            // Anti-armour turret when heavy armour has been seen (counter), up to the cap.
            if (cost.AtTurret > 0 && s.KnownEnemyHeavy >= (cfg.AdvancedCounters ? 1 : 2) && s.AtTurrets < cfg.MaxAtTurrets)
                return EconomyPlanner.BuyOrSave(s.Credits, cost.AtTurret, AiActionType.BuildAtTurret, $"enemy heavies seen ({s.KnownEnemyHeavy})");

            // Guard tower after the base has been attacked, or one as early insurance once an army exists.
            if (cost.GuardTower > 0 && s.GuardTowers < cfg.MaxGuardTowers)
            {
                if (s.RecentlyAttacked)
                    return EconomyPlanner.BuyOrSave(s.Credits, cost.GuardTower, AiActionType.BuildGuardTower, "base was attacked");
                if (s.GuardTowers == 0 && s.ArmyCount >= 6 && EconomyPlanner.Affordable(s.Credits, cost.GuardTower, cfg.CreditReserve))
                    return new AiAction(AiActionType.BuildGuardTower, "early warning defence");
            }

            if (defending) return new AiAction(AiActionType.None, "defending");

            // Technology: a Research Centre once the army can protect the investment.
            if (cost.ResearchCentre > 0 && s.ResearchCentres == 0 && s.ArmyCount >= cfg.ResearchArmyThreshold && s.Harvesters >= 2)
                return EconomyPlanner.BuyOrSave(s.Credits, cost.ResearchCentre, AiActionType.BuildResearchCentre, "technology");

            // Research when idle and affordable without touching the reserve (does not block army production).
            if (s.ResearchCentres > 0 && !s.ResearchBusy && s.WantedResearch != null
                && EconomyPlanner.Affordable(s.Credits, s.WantedResearchCost, cfg.CreditReserve))
                return new AiAction(AiActionType.StartResearch, "upgrade", topic: s.WantedResearch);

            return new AiAction(AiActionType.None, "");
        }

        /// <summary>A research option as the commander sees it.</summary>
        public struct TopicOption
        {
            public string Id;
            public int Cost;
            public bool Done;
            public bool PrerequisitesMet;
        }

        /// <summary>
        /// Research priorities: harvesting first once two or more harvesters work (it pays for itself), then armour, then
        /// weapons. Returns null when nothing is left.
        /// </summary>
        public static string ChooseResearch(in CommanderSnapshot s, IReadOnlyList<TopicOption> options)
        {
            string[] order = s.Harvesters >= 2
                ? new[] { "advanced_harvesting", "composite_armour", "enhanced_weapons" }
                : new[] { "composite_armour", "enhanced_weapons", "advanced_harvesting" };
            foreach (var id in order)
                foreach (var o in options)
                    if (o.Id == id && !o.Done && o.PrerequisitesMet) return id;
            foreach (var o in options)
                if (!o.Done && o.PrerequisitesMet) return o.Id;
            return null;
        }
    }
}
