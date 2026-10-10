namespace Ironfront.Core.AI
{
    public enum AiActionType
    {
        None,
        /// <summary>The top priority is not affordable yet: save credits instead of spending on lower priorities.</summary>
        SaveFor,
        BuildRefinery,
        BuildPowerPlant,
        BuildFactory,
        ProduceHarvester,
        ProduceLightTank,
        ProduceHeavyTank,
    }

    public readonly struct AiAction
    {
        public readonly AiActionType Type;
        /// <summary>For <see cref="AiActionType.SaveFor"/>: what is being saved for.</summary>
        public readonly AiActionType Goal;
        public readonly string Reason;

        public AiAction(AiActionType type, string reason, AiActionType goal = AiActionType.None)
        {
            Type = type;
            Reason = reason;
            Goal = goal;
        }

        public bool IsConstruction => Type == AiActionType.BuildRefinery || Type == AiActionType.BuildPowerPlant || Type == AiActionType.BuildFactory;
        public bool IsProduction => Type == AiActionType.ProduceHarvester || Type == AiActionType.ProduceLightTank || Type == AiActionType.ProduceHeavyTank;

        public override string ToString() => Type == AiActionType.SaveFor ? $"Save for {Goal}: {Reason}" : $"{Type}: {Reason}";
    }

    /// <summary>
    /// Chooses the commander's next economic or production step by fixed priorities:
    /// refinery → power → harvesters → factory → expansion → army. If the most important step is not
    /// affordable it returns <see cref="AiActionType.SaveFor"/>, so lower priorities cannot starve it.
    /// Never returns an action the faction cannot pay for.
    /// </summary>
    public static class EconomyPlanner
    {
        public static AiAction Next(in CommanderSnapshot s, StrategicState state, in CommanderSettings cfg, in CostTable cost)
        {
            if (!s.HasHeadquarters) return new AiAction(AiActionType.None, "no headquarters");
            bool defending = state == StrategicState.Defend;

            // 1. Income first: without a refinery nothing else matters (unless there is no ore left anywhere).
            if (s.Refineries == 0 && s.OreAvailable)
                return BuyOrSave(s.Credits, cost.Refinery, AiActionType.BuildRefinery, "no refinery");

            // 2. Power for what we have and the next building.
            if (s.SparePower < cfg.PowerMargin)
                return BuyOrSave(s.Credits, cost.PowerPlant, AiActionType.BuildPowerPlant, $"spare power {s.SparePower}");

            // While defending, a factory with no army gets units before more harvesters.
            if (defending && s.Factories > 0 && s.ArmyIncludingQueued < cfg.MaxArmy && QueueHasRoom(s, cfg))
            {
                var unit = CompositionRules.Choose(s, cfg, cost, ignoreReserve: true);
                if (unit != AiActionType.None) return new AiAction(unit, "base under attack");
            }

            // 3. Harvesters (replace losses, then grow to the target).
            int wantHarvesters = DesiredHarvesters(s, cfg);
            if (s.Factories > 0 && s.HarvestersIncludingQueued < wantHarvesters && QueueHasRoom(s, cfg) && s.OreAvailable)
            {
                if (s.HarvestersIncludingQueued == 0 || !defending)
                    return BuyOrSave(s.Credits, cost.Harvester, AiActionType.ProduceHarvester,
                        $"harvesters {s.HarvestersIncludingQueued}/{wantHarvesters}");
            }

            // 4. A factory to produce anything at all.
            if (s.Factories == 0)
                return BuyOrSave(s.Credits, cost.Factory, AiActionType.BuildFactory, "no factory");

            // 5. Expansion: a refinery near ore that is still available when the local fields are spent,
            //    or a second refinery once harvesters are saturated and credits allow.
            if (s.Refineries < cfg.MaxRefineries && s.OreAvailable && !s.OreNearRefinery)
                return BuyOrSave(s.Credits, cost.Refinery, AiActionType.BuildRefinery, "local ore exhausted");
            if (s.Refineries < cfg.MaxRefineries && s.Credits >= cost.Refinery + cfg.ExpansionCredits && s.OreAvailable)
                return new AiAction(AiActionType.BuildRefinery, "expand income");
            if (s.Factories < cfg.MaxFactories && s.Credits >= cost.Factory + cfg.ExpansionCredits)
                return new AiAction(AiActionType.BuildFactory, "second factory");

            // 6. Army, within the cap and keeping a reserve.
            if (s.ArmyIncludingQueued < cfg.MaxArmy && QueueHasRoom(s, cfg))
            {
                var unit = CompositionRules.Choose(s, cfg, cost, ignoreReserve: defending);
                if (unit != AiActionType.None)
                    return new AiAction(unit, $"army {s.ArmyIncludingQueued + 1}/{cfg.MaxArmy}");
                return new AiAction(AiActionType.SaveFor, "army unit", AiActionType.ProduceLightTank);
            }

            return new AiAction(AiActionType.None, s.ArmyIncludingQueued >= cfg.MaxArmy ? "army at cap" : "factory queue full");
        }

        public static int DesiredHarvesters(in CommanderSnapshot s, in CommanderSettings cfg)
        {
            int want = s.Refineries * cfg.HarvestersPerRefinery;
            if (want > cfg.MaxHarvesters) want = cfg.MaxHarvesters;
            return want < 1 ? 1 : want;
        }

        public static bool QueueHasRoom(in CommanderSnapshot s, in CommanderSettings cfg) =>
            s.QueuedHarvesters + s.QueuedLight + s.QueuedHeavy < cfg.MaxQueuedUnits * (s.Factories < 1 ? 1 : s.Factories);

        public static bool Affordable(int credits, int cost, int reserve = 0) => cost >= 0 && credits - reserve >= cost;

        private static AiAction BuyOrSave(int credits, int price, AiActionType type, string reason) =>
            Affordable(credits, price) ? new AiAction(type, reason) : new AiAction(AiActionType.SaveFor, $"{reason} ({credits}/{price})", type);
    }

    /// <summary>Light/Heavy mix: more Heavies against armoured opponents, Lights when money is short.</summary>
    public static class CompositionRules
    {
        public const float BaseHeavyShare = 0.4f;
        public const float CounterHeavyShare = 0.65f;

        public static float DesiredHeavyShare(in CommanderSnapshot s) =>
            s.KnownEnemyHeavy > s.KnownEnemyLight ? CounterHeavyShare : BaseHeavyShare;

        /// <summary>Returns ProduceHeavyTank, ProduceLightTank or None (nothing affordable).</summary>
        public static AiActionType Choose(in CommanderSnapshot s, in CommanderSettings cfg, in CostTable cost, bool ignoreReserve)
        {
            int reserve = ignoreReserve ? 0 : cfg.CreditReserve;
            int heavies = s.HeavyTanks + s.QueuedHeavy;
            int total = s.ArmyIncludingQueued;
            // Small tolerance: an exact match of the target share counts as met regardless of float rounding.
            bool wantHeavy = total == 0 ? DesiredHeavyShare(s) >= 0.5f : (float)heavies / total < DesiredHeavyShare(s) - 1e-4f;

            if (wantHeavy && EconomyPlanner.Affordable(s.Credits, cost.HeavyTank, reserve)) return AiActionType.ProduceHeavyTank;
            if (EconomyPlanner.Affordable(s.Credits, cost.LightTank, reserve)) return AiActionType.ProduceLightTank;
            return AiActionType.None;
        }
    }
}
