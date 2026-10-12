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
        /// <summary>Milestone 007: scouts kept alive.</summary>
        public int ReconCount;
        public int MaxGuardTowers;
        public int MaxAtTurrets;
        /// <summary>Army size before a Research Centre is worth its cost.</summary>
        public int ResearchArmyThreshold;
        /// <summary>Target fraction of the army that is artillery once it is available and useful.</summary>
        public float ArtilleryShare;

        // ---- Milestone 008 difficulty switches. Zero/false reproduces the Milestone 007 (Normal) behaviour. ----
        /// <summary>Easy: a fixed unit mix that ignores what the enemy fields, never saves for expensive units.</summary>
        public bool NoCounterBuilding;
        /// <summary>Hard: stronger counter shares and anti-armour defences after the first enemy heavy.</summary>
        public bool AdvancedCounters;
        /// <summary>Hard: attack only with this multiple of the known enemy army strength (0 = off). Ignored at the army cap.</summary>
        public float AttackStrengthEdge;
        /// <summary>Easy: no attack before this many seconds of match time (0 = off). Defence is unaffected.</summary>
        public float EarliestAttack;
        /// <summary>
        /// Milestone 008 pacing: units added to the attack group size and the army cap after each failed attack, up to
        /// <see cref="MaxEscalation"/>, so repeated equal trades escalate into a decisive push instead of a stalemate.
        /// </summary>
        public int EscalationStep;
        public int MaxEscalation;
        /// <summary>
        /// Harvesters at which a second factory becomes a priority it saves for (0 = only from surplus credits, the
        /// Milestone 007 rule). Set by <see cref="ScaledForMap"/> on large maps.
        /// </summary>
        public int SecondFactoryHarvesters;
        // ---- Milestone 009 repairs (0 = never: the Milestone 008 behaviour) ----
        public int MaxRepairPads;
        /// <summary>Vehicles below this health fraction are sent to a Repair Pad (when the situation allows).</summary>
        public float VehicleRepairThreshold;
        /// <summary>Important structures below this health fraction are repaired in place.</summary>
        public float BuildingRepairThreshold;
        /// <summary>Seconds between replacement scouts once the enemy has been located (0 = no limit).</summary>
        public float ReconReplaceSeconds;

        /// <summary>Maps at least this wide (metres) count as large for <see cref="ScaledForMap"/>.</summary>
        public const float LargeMapSize = 250f;

        /// <summary>
        /// Milestone 008 map adaptation: the Milestone 007 caps were tuned for a 100 m map. On large maps the same rules
        /// produce economies and armies too small to ever force a decision (AI-vs-AI study: one factory, four harvesters
        /// and ~1 unit a minute on a 600 m map). Large maps raise the harvester, refinery and army caps and lower the
        /// credit level for expanding. Applies identically to every difficulty and changes no game rule.
        /// </summary>
        public CommanderSettings ScaledForMap(float mapSize)
        {
            var c = this;
            if (mapSize < LargeMapSize) return c;
            c.MaxHarvesters += 2;
            c.MaxRefineries += 1;
            c.MaxArmy += 4;
            c.ExpansionCredits = (int)Math.Round(ExpansionCredits * 0.6);
            c.SecondFactoryHarvesters = 5;
            return c;
        }

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
            ReconCount = 1,
            MaxGuardTowers = 2,
            MaxAtTurrets = 2,
            ResearchArmyThreshold = 5,
            ArtilleryShare = 0.25f,
            EscalationStep = 1,
            MaxEscalation = 4,
            ReconReplaceSeconds = 150f,
            MaxRepairPads = 1,
            VehicleRepairThreshold = 0.45f,
            BuildingRepairThreshold = 0.7f,
        };

        /// <summary>
        /// Milestone 008 Easy: slower and less aggressive, a smaller economy and army, no scouting and no reaction to
        /// the enemy's unit mix. Same rules and costs as everyone else.
        /// </summary>
        public static CommanderSettings Easy
        {
            get
            {
                var c = Normal;
                c.MinStateDuration = 15f;
                c.Aggressiveness = 0.2f;
                c.DefenceAllocation = 0.35f;
                c.RetreatFraction = 0.5f;
                c.MaxHarvesters = 3;
                c.MaxFactories = 1;
                c.MaxArmy = 8;
                c.MaxQueuedUnits = 1;
                c.CreditReserve = 600;
                c.ExpansionCredits = 6000;
                c.ReconCount = 0;
                c.MaxGuardTowers = 1;
                c.MaxAtTurrets = 0;
                c.ResearchArmyThreshold = 8;
                c.ArtilleryShare = 0.1f;
                c.NoCounterBuilding = true;
                c.EarliestAttack = 300f;
                c.EscalationStep = 1;
                c.MaxEscalation = 2;
                c.VehicleRepairThreshold = 0.35f;
                c.BuildingRepairThreshold = 0.5f;
                return c;
            }
        }

        /// <summary>
        /// Milestone 008 Hard: a larger, faster-growing economy, two scouts, stronger counter-building, attacks only
        /// with a strength edge and retreats earlier to preserve the army. No bonuses of any kind.
        /// </summary>
        public static CommanderSettings Hard
        {
            get
            {
                var c = Normal;
                c.MinStateDuration = 6f;
                c.Aggressiveness = 0.5f;
                c.DefenceAllocation = 0.2f;
                c.RetreatFraction = 0.45f;
                c.HarvestersPerRefinery = 3;
                c.MaxHarvesters = 6;
                c.MaxRefineries = 3;
                c.MaxArmy = 16;
                c.CreditReserve = 150;
                c.ExpansionCredits = 2000;
                c.ReconCount = 2;
                c.MaxGuardTowers = 2;
                c.MaxAtTurrets = 2;
                c.ResearchArmyThreshold = 4;
                c.ArtilleryShare = 0.3f;
                c.AdvancedCounters = true;
                c.AttackStrengthEdge = 1.15f;
                c.MaxRepairPads = 2;
                c.VehicleRepairThreshold = 0.5f;
                c.BuildingRepairThreshold = 0.75f;
                return c;
            }
        }
    }

    /// <summary>Costs and power figures the planner needs, read from the same definitions the player uses.</summary>
    [Serializable]
    public struct CostTable
    {
        public int Refinery, PowerPlant, Factory, Harvester, LightTank, HeavyTank;
        public int RefineryDrain, FactoryDrain, PowerPlantOutput;
        /// <summary>Milestone 007 content (0 = not available, which disables the related decisions).</summary>
        public int Recon, Artillery, ResearchCentre, GuardTower, AtTurret;
        /// <summary>Milestone 009 (0 = not available).</summary>
        public int RepairPad;
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

        // ---- Milestone 007 ----
        public int Recon, QueuedRecon, Artillery, QueuedArtillery;
        public int ResearchCentres, GuardTowers, AtTurrets;
        /// <summary>Enemy artillery seen recently, and enemy defences / structures known (remembered).</summary>
        public int KnownEnemyArtillery, KnownEnemyDefences, KnownEnemyStructures;
        /// <summary>The base was threatened within the last minute.</summary>
        public bool RecentlyAttacked;
        public bool ResearchBusy;
        /// <summary>Next research topic the commander would like (null = none) and its cost.</summary>
        public string WantedResearch;
        public int WantedResearchCost;

        // ---- Milestone 008 ----
        /// <summary>Estimated strength of the enemy army the commander has seen recently (perception memory).</summary>
        public float KnownEnemyStrength;
        /// <summary>Seconds since the match started.</summary>
        public float MatchTime;
        /// <summary>The commander has located the enemy (seen at least one of its structures).</summary>
        public bool EnemyBaseKnown;
        /// <summary>Extra attack-group size and army cap earned by failed attacks (see CommanderSettings.EscalationStep).</summary>
        public int AttackEscalation;
        /// <summary>A replacement scout was bought recently.</summary>
        public bool ReconOnCooldown;
        /// <summary>Milestone 009: Repair Pads owned.</summary>
        public int RepairPads;
        public int ArmyCap(in CommanderSettings cfg) => cfg.MaxArmy + AttackEscalation;

        public int ArmyCount => LightTanks + HeavyTanks + Artillery;
        public int ArmyIncludingQueued => ArmyCount + QueuedLight + QueuedHeavy + QueuedArtillery;
        public int HarvestersIncludingQueued => Harvesters + QueuedHarvesters;
        public int SparePower => PowerProduced - PowerConsumed;
        public bool EconomyBroken => Refineries == 0 || HarvestersIncludingQueued == 0;
    }

    /// <summary>Unit strength used for attack/retreat decisions: Light 1, Heavy 2, scaled by remaining health.</summary>
    public static class ArmyMath
    {
        public const float LightValue = 1f;
        public const float HeavyValue = 2f;
        public const float ArtilleryValue = 1.5f;
        public const float ReconValue = 0.4f;

        public static float Strength(float value, float healthFraction) => value * Math.Max(0f, Math.Min(1f, healthFraction));

        public static float Strength(bool heavy, float healthFraction) =>
            (heavy ? HeavyValue : LightValue) * Math.Max(0f, Math.Min(1f, healthFraction));
    }
}
