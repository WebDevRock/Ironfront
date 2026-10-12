namespace Ironfront.Core.AI
{
    /// <summary>
    /// Milestone 009: when the computer commander repairs. It uses exactly the player's repair system and prices; these
    /// rules only decide <i>whether</i> a repair is worth it. Pure and deterministic.
    /// </summary>
    public static class RepairPlanner
    {
        /// <summary>Health below which a vehicle is badly damaged and goes for repair even while the base is defended.</summary>
        public const float CriticalHealth = 0.25f;
        /// <summary>Credits kept above the reserve before a building repair is started (repairs never starve production).</summary>
        public const int BuildingRepairMargin = 300;

        /// <summary>
        /// Send a vehicle to a pad? Only when it is damaged enough, not in the middle of a fight it is needed for, the share
        /// of the army away for repair stays at most half, and the credits cover the repair on top of the reserve.
        /// </summary>
        public static bool ShouldSendVehicle(float healthFraction, float threshold, bool engaged, bool defending, int alreadyAway, int armySize,
            int credits, int reserve, int estimatedCost)
        {
            if (threshold <= 0f || healthFraction >= threshold) return false;
            bool critical = healthFraction < CriticalHealth;
            if (engaged && !critical) return false;
            if (defending && !critical) return false;
            if (armySize > 0 && alreadyAway + 1 > System.Math.Max(1, armySize / 2)) return false;
            return credits >= reserve + estimatedCost;
        }

        /// <summary>
        /// Building repair switch with hysteresis: start below the threshold when credits exceed the reserve plus a margin;
        /// keep going while credits stay above the reserve; otherwise stop (production comes first).
        /// </summary>
        public static bool ShouldRepairBuilding(float healthFraction, float threshold, bool currentlyOn, int credits, int reserve)
        {
            if (threshold <= 0f || healthFraction >= 1f) return false;
            if (currentlyOn) return credits > reserve;
            return healthFraction < threshold && credits >= reserve + BuildingRepairMargin;
        }

        /// <summary>Is this structure worth repairing (production, economy, power, technology, defences)?</summary>
        public static bool IsImportantStructure(string definitionId)
        {
            switch (definitionId)
            {
                case "headquarters":
                case "vehicle_factory":
                case "refinery":
                case "power_plant":
                case "research_centre":
                case "repair_pad":
                case "guard_tower":
                case "at_turret":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Estimated seconds until a vehicle sent to a pad starts its own repair: the drive there plus everyone ahead of it
        /// (and the remaining assembly time). Lower is better; unusable pads score +infinity.
        /// </summary>
        public static float PadWaitEstimate(bool usable, float travelSeconds, int vehiclesAhead, float averageRepairSeconds, float assemblyRemaining)
        {
            if (!usable) return float.PositiveInfinity;
            float queue = vehiclesAhead * averageRepairSeconds;
            return System.Math.Max(travelSeconds, assemblyRemaining) + queue;
        }
    }
}
