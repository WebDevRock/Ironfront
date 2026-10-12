namespace Ironfront.Core.AI
{
    /// <summary>
    /// Milestone 010: when the computer commander should buy a Mobile Command Vehicle, where a forward base may go, and
    /// how often it may retry. Pure and deterministic; the commander executes the result with the same production,
    /// movement and deployment the player uses.
    /// </summary>
    public static class ForwardBaseRules
    {
        /// <summary>Known enemy structures must be at least this far (metres) from a forward base site.</summary>
        public const float MinEnemyDistance = 90f;
        /// <summary>Deploy positions tried per site, and sites tried per vehicle, before giving up.</summary>
        public const int PositionsPerSite = 3;
        public const int SitesPerVehicle = 2;
        /// <summary>Seconds a vehicle may travel towards one site.</summary>
        public const float TravelTimeout = 240f;
        /// <summary>Seconds after losing an MCV before another is bought.</summary>
        public const float LossCooldown = 300f;
        /// <summary>Escort size while the vehicle travels and deploys.</summary>
        public const int EscortSize = 3;
        /// <summary>
        /// Harvesters gather from any field and haul to the nearest refinery, so a forward base only pays off when the next
        /// field is a long haul away (M010 study: shorter expansions were bought and sold again within minutes).
        /// </summary>
        public const float LongHaul = 70f;
        /// <summary>Spare credits beyond the vehicle, the reserve and this margin allow an opportunistic expansion.</summary>
        public const int SurplusMargin = 1000;
        /// <summary>Refineries and forward bases are kept while any ore remains within this distance (metres).</summary>
        public const float ObsoleteRadius = 120f;
        /// <summary>
        /// Build range comes from every structure, so a field this close (metres from the nearest own structure) is reached
        /// by building towards it — a power plant or two — rather than with a 5000-credit vehicle.
        /// </summary>
        public const float CreepMax = 45f;

        /// <summary>
        /// Extend the build range towards the nearest unclaimed ore field when it is within <see cref="CreepMax"/> of the
        /// base and more refineries are wanted. Bought only when affordable with the reserve (cheap; never saved for).
        /// </summary>
        public static AiAction Creep(in CommanderSnapshot s, StrategicState state, in CommanderSettings cfg, in CostTable cost)
        {
            if (cost.PowerPlant <= 0 || state == StrategicState.Defend || s.Factories == 0) return None("");
            if (!s.RefineryBlockedByTerritory || s.Refineries >= cfg.MaxRefineries) return None("");
            if (s.CreepDistance <= 0f || s.CreepDistance > CreepMax) return None("");
            return EconomyPlanner.Affordable(s.Credits, cost.PowerPlant, cfg.CreditReserve)
                ? new AiAction(AiActionType.ExtendBuildRange, $"build towards ore {s.CreepDistance:0} m away")
                : None("");
        }

        public static AiAction Next(in CommanderSnapshot s, StrategicState state, in CommanderSettings cfg, in CostTable cost)
        {
            if (cost.Mcv <= 0 || cfg.MaxForwardBases <= 0) return None("no forward bases");
            if (state == StrategicState.Defend) return None("defending");
            if (s.Mcvs + s.QueuedMcv > 0) return None("vehicle already on its way");
            if (s.ForwardBases >= cfg.MaxForwardBases) return None("forward bases at cap");
            if (s.McvLosses >= cfg.MaxMcvLosses) return None("too many vehicles lost");
            if (s.McvOnCooldown) return None("recently lost a vehicle");
            if (s.MatchTime < cfg.EarliestForwardBase) return None("too early");
            if (!s.RefineryBlockedByTerritory || s.Refineries >= cfg.MaxRefineries) return None("no expansion needed");
            if (!s.ExpansionTargetKnown) return None("no explored ore field outside territory");
            if (s.CreepDistance > 0f && s.CreepDistance <= CreepMax) return None("a field can be reached by building towards it");
            if (s.Factories == 0 || s.ArmyCount < cfg.ForwardBaseArmy) return None("army too small to escort");

            // Local ore gone and the next field a long haul away: the economy depends on a forward base, so save for it
            // (M010 study, first run: buying it only from surplus meant it never happened and both economies stalled).
            if (!s.OreNearRefinery && s.ExpansionHaulDistance >= LongHaul)
                return EconomyPlanner.BuyOrSave(s.Credits, cost.Mcv, AiActionType.ProduceMcv, "local ore exhausted: forward base");
            // Otherwise only from a clear surplus.
            return EconomyPlanner.Affordable(s.Credits, cost.Mcv, cfg.CreditReserve + SurplusMargin)
                ? new AiAction(AiActionType.ProduceMcv, "expand income")
                : None("harvesters can still haul; saving nothing for a forward base");
        }

        /// <summary>Is a candidate forward-base site acceptable? Distances in metres; float.MaxValue = unknown.</summary>
        public static bool AcceptableSite(float distanceToNearestKnownEnemy, float groundDistanceFromHome, float groundDistanceToKnownEnemyBase)
        {
            if (groundDistanceFromHome == float.MaxValue) return false; // unreachable
            if (distanceToNearestKnownEnemy < MinEnemyDistance) return false;
            // Closer to home than to the enemy (when the enemy base is known): defensible and on our side of the map.
            return groundDistanceToKnownEnemyBase == float.MaxValue || groundDistanceFromHome < groundDistanceToKnownEnemyBase;
        }

        private static AiAction None(string reason) => new AiAction(AiActionType.None, reason);
    }
}
