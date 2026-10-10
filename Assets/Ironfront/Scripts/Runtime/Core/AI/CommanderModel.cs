using System;

namespace Ironfront.Core.AI
{
    /// <summary>High-level behaviour of a computer commander.</summary>
    public enum StrategicState
    {
        /// <summary>Building up the economy and base.</summary>
        Develop,
        /// <summary>Enemy forces are near the base: all units defend.</summary>
        Defend,
        /// <summary>Base is functional: gather an army at the rally point.</summary>
        Assemble,
        /// <summary>The assembled group is attacking.</summary>
        Attack,
        /// <summary>After a failed attack or economic losses: rebuild before doing anything else.</summary>
        Recover,
    }

    /// <summary>Tuning for a commander (see the AiDifficulty asset). No field grants resources or damage bonuses.</summary>
    [Serializable]
    public struct CommanderSettings
    {
        public float MinStateDuration;
        /// <summary>0..1. Scales the attack group size: 0 waits for 1.5× the base size, 1 attacks with 0.5×.</summary>
        public float Aggressiveness;
        /// <summary>Fraction of the army kept at home during an attack.</summary>
        public float DefenceAllocation;
        public int AttackArmySize;
        public float AttackStrengthThreshold;
        /// <summary>Attackers retreat when their strength falls below this fraction of the strength they set out with.</summary>
        public float RetreatFraction;
        public int HarvestersPerRefinery;
        public int MaxHarvesters;
        public int MaxRefineries;
        public int MaxFactories;
        public int MaxArmy;
        public int MaxQueuedUnits;
        /// <summary>Credits kept back from military spending (not while defending).</summary>
        public int CreditReserve;
        /// <summary>Build a power plant when spare power drops below this.</summary>
        public int PowerMargin;
        /// <summary>Credits above which a second factory is considered.</summary>
        public int ExpansionCredits;

        public int EffectiveAttackSize =>
            Math.Max(2, (int)Math.Round(AttackArmySize * (1.5 - Math.Max(0f, Math.Min(1f, Aggressiveness)))));

        public static CommanderSettings Normal => new CommanderSettings
        {
            MinStateDuration = 8f,
            Aggressiveness = 0.5f,
            DefenceAllocation = 0.25f,
            AttackArmySize = 6,
            AttackStrengthThreshold = 7f,
            RetreatFraction = 0.35f,
            HarvestersPerRefinery = 2,
            MaxHarvesters = 4,
            MaxRefineries = 2,
            MaxFactories = 2,
            MaxArmy = 12,
            MaxQueuedUnits = 2,
            CreditReserve = 300,
            PowerMargin = 10,
            ExpansionCredits = 3500,
        };
    }

    /// <summary>Costs and power figures the planner needs, read from the same definitions the player uses.</summary>
    [Serializable]
    public struct CostTable
    {
        public int Refinery, PowerPlant, Factory, Harvester, LightTank, HeavyTank;
        public int RefineryDrain, FactoryDrain, PowerPlantOutput;
    }

    /// <summary>What the commander knows at a decision point (its own state plus what it has seen).</summary>
    [Serializable]
    public struct CommanderSnapshot
    {
        public int Credits;
        public int PowerProduced, PowerConsumed;
        public bool HasHeadquarters;
        public int Refineries, PowerPlants, Factories;
        public int Harvesters, QueuedHarvesters;
        public int LightTanks, HeavyTanks, QueuedLight, QueuedHeavy;
        public float ArmyStrength;
        /// <summary>Strength of visible hostile armed units near the base.</summary>
        public float ThreatStrength;
        public int KnownEnemyLight, KnownEnemyHeavy;
        /// <summary>Some deposit still has Ore.</summary>
        public bool OreAvailable;
        /// <summary>A non-empty deposit is within reach of an existing refinery.</summary>
        public bool OreNearRefinery;
        /// <summary>Units currently committed to an attack, and their strength now / when they set out.</summary>
        public int AttackersAlive;
        public float AttackStrength, AttackStartStrength;

        public int ArmyCount => LightTanks + HeavyTanks;
        public int ArmyIncludingQueued => ArmyCount + QueuedLight + QueuedHeavy;
        public int HarvestersIncludingQueued => Harvesters + QueuedHarvesters;
        public int SparePower => PowerProduced - PowerConsumed;
        public bool EconomyBroken => Refineries == 0 || HarvestersIncludingQueued == 0;
    }

    /// <summary>Unit strength used for attack/retreat decisions: Light 1, Heavy 2, scaled by remaining health.</summary>
    public static class ArmyMath
    {
        public const float LightValue = 1f;
        public const float HeavyValue = 2f;

        public static float Strength(bool heavy, float healthFraction) =>
            (heavy ? HeavyValue : LightValue) * Math.Max(0f, Math.Min(1f, healthFraction));
    }
}
