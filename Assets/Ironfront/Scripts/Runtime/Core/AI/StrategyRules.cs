namespace Ironfront.Core.AI
{
    /// <summary>
    /// Strategic state transitions. Defence pre-empts everything immediately; every other change waits for
    /// <see cref="CommanderSettings.MinStateDuration"/> so the commander does not flip between states.
    /// End game: once no ore is left anywhere the economy cannot recover, so the commander stops waiting
    /// for a full attack group and commits whatever army it has.
    /// </summary>
    public static class StrategyRules
    {
        public static StrategicState Next(StrategicState current, float timeInState, in CommanderSnapshot s, in CommanderSettings cfg)
        {
            if (!s.HasHeadquarters) return current;

            bool threatened = s.ThreatStrength > 0f;
            if (threatened) return StrategicState.Defend;

            if (timeInState < cfg.MinStateDuration) return current;

            switch (current)
            {
                case StrategicState.Defend:
                    if (AllIn(s)) return StrategicState.Attack;
                    return Recoverable(s) ? StrategicState.Recover : BaseReady(s) ? StrategicState.Assemble : StrategicState.Develop;

                case StrategicState.Develop:
                    if (AllIn(s)) return StrategicState.Attack;
                    if (Recoverable(s)) return StrategicState.Recover;
                    return BaseReady(s) ? StrategicState.Assemble : StrategicState.Develop;

                case StrategicState.Assemble:
                    if (ReadyToAttack(s, cfg)) return StrategicState.Attack;
                    if (Recoverable(s) || (s.Factories == 0 && s.OreAvailable)) return StrategicState.Recover;
                    return StrategicState.Assemble;

                case StrategicState.Attack:
                    return ShouldRetreat(s, cfg) ? StrategicState.Recover : StrategicState.Attack;

                case StrategicState.Recover:
                    if (AllIn(s)) return StrategicState.Attack;
                    if (Recoverable(s) || (s.Factories == 0 && s.OreAvailable)) return StrategicState.Recover;
                    return BaseReady(s) ? StrategicState.Assemble : StrategicState.Develop;

                default:
                    return current;
            }
        }

        /// <summary>The base can sustain an army: income, power and a factory.</summary>
        public static bool BaseReady(in CommanderSnapshot s) =>
            s.Refineries > 0 && s.Harvesters > 0 && s.Factories > 0 && s.PowerPlants > 0;

        public static bool ReadyToAttack(in CommanderSnapshot s, in CommanderSettings cfg)
        {
            if (AllIn(s)) return true;
            if (s.MatchTime < cfg.EarliestAttack) return false;
            if (s.ArmyCount < cfg.EffectiveAttackSize + s.AttackEscalation || s.ArmyStrength < cfg.AttackStrengthThreshold) return false;
            return HasStrengthEdge(s, cfg);
        }

        /// <summary>
        /// Hard: attack only when stronger than the enemy army it has seen recently (or when its own army is at the cap,
        /// so a cautious commander cannot wait forever).
        /// </summary>
        public static bool HasStrengthEdge(in CommanderSnapshot s, in CommanderSettings cfg) =>
            cfg.AttackStrengthEdge <= 0f || s.ArmyCount >= s.ArmyCap(cfg) || s.ArmyStrength >= s.KnownEnemyStrength * cfg.AttackStrengthEdge;

        /// <summary>Retreat to rebuild only makes sense while there is an economy to rebuild.</summary>
        public static bool ShouldRetreat(in CommanderSnapshot s, in CommanderSettings cfg) =>
            s.AttackersAlive == 0 || (s.OreAvailable && s.AttackStrength < s.AttackStartStrength * cfg.RetreatFraction);

        /// <summary>No ore left anywhere: commit the remaining army rather than wait for income that will never come.</summary>
        public static bool AllIn(in CommanderSnapshot s) => !s.OreAvailable && s.ArmyCount > 0;

        /// <summary>The economy is broken and can still be repaired (ore remains on the map).</summary>
        public static bool Recoverable(in CommanderSnapshot s) => s.EconomyBroken && s.OreAvailable;
    }
}
