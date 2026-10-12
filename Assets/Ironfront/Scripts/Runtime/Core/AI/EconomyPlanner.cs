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
        // Milestone 007
        ProduceRecon,
        ProduceArtillery,
        BuildResearchCentre,
        BuildGuardTower,
        BuildAtTurret,
        StartResearch,
        // Milestone 009
        BuildRepairPad,
        // Milestone 010
        ProduceMcv,
    }

    public readonly struct AiAction
    {
        public readonly AiActionType Type;
        /// <summary>For <see cref="AiActionType.SaveFor"/>: what is being saved for.</summary>
        public readonly AiActionType Goal;
        public readonly string Reason;
        /// <summary>For <see cref="AiActionType.StartResearch"/>: the topic id.</summary>
        public readonly string Topic;

        public AiAction(AiActionType type, string reason, AiActionType goal = AiActionType.None, string topic = null)
        {
            Type = type;
            Reason = reason;
            Goal = goal;
            Topic = topic;
        }

        public bool IsConstruction => Type == AiActionType.BuildRefinery || Type == AiActionType.BuildPowerPlant || Type == AiActionType.BuildFactory
                                      || Type == AiActionType.BuildResearchCentre || Type == AiActionType.BuildGuardTower || Type == AiActionType.BuildAtTurret
                                      || Type == AiActionType.BuildRepairPad;
        public bool IsProduction => Type == AiActionType.ProduceHarvester || Type == AiActionType.ProduceLightTank || Type == AiActionType.ProduceHeavyTank
                                    || Type == AiActionType.ProduceRecon || Type == AiActionType.ProduceArtillery || Type == AiActionType.ProduceMcv;

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
            if (s.Refineries == 0 && s.OreAvailable && !s.RefineryBlockedByTerritory)
                return BuyOrSave(s.Credits, cost.Refinery, AiActionType.BuildRefinery, "no refinery");

            // 2. Power for what we have and the next building.
            if (s.SparePower < cfg.PowerMargin)
                return BuyOrSave(s.Credits, cost.PowerPlant, AiActionType.BuildPowerPlant, $"spare power {s.SparePower}");

            // While defending, a factory with no army gets units before more harvesters.
            if (defending && s.Factories > 0 && s.ArmyIncludingQueued < s.ArmyCap(cfg) && QueueHasRoom(s, cfg))
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

            // 4b. Milestone 010: a forward base when the next ore field lies outside construction territory. It comes
            //     before defences and technology: the home field runs dry within minutes on maps without a second field
            //     in reach (M010 study), and without income the vehicle can never be afforded.
            var forward = ForwardBaseRules.Next(s, state, cfg, cost);
            if (forward.Type != AiActionType.None) return forward;

            // 4c-4f. Milestone 007: scouting, defences, technology (each only when the content exists).
            var expansion = ExpansionRules.Next(s, state, cfg, cost);
            if (expansion.Type != AiActionType.None) return expansion;

            // 5. Expansion: a refinery near ore that is still available when the local fields are spent,
            //    or a second refinery once harvesters are saturated and credits allow.
            // (Milestone 010: only while an unclaimed field has a refinery site inside construction territory.)
            if (s.Refineries < cfg.MaxRefineries && s.OreAvailable && !s.OreNearRefinery && !s.RefineryBlockedByTerritory)
                return BuyOrSave(s.Credits, cost.Refinery, AiActionType.BuildRefinery, "local ore exhausted");
            if (s.Refineries < cfg.MaxRefineries && s.Credits >= cost.Refinery + cfg.ExpansionCredits && s.OreAvailable && !s.RefineryBlockedByTerritory)
                return new AiAction(AiActionType.BuildRefinery, "expand income");
            if (s.Factories < cfg.MaxFactories && s.Credits >= cost.Factory + cfg.ExpansionCredits)
                return new AiAction(AiActionType.BuildFactory, "second factory");
            // Milestone 008 (large maps): a strong economy saves for its second factory instead of waiting for a surplus
            // that army spending never leaves.
            if (cfg.SecondFactoryHarvesters > 0 && s.Factories < cfg.MaxFactories && s.Factories > 0 && s.Harvesters >= cfg.SecondFactoryHarvesters
                && state != StrategicState.Defend)
                return BuyOrSave(s.Credits, cost.Factory, AiActionType.BuildFactory, $"second factory ({s.Harvesters} harvesters)");

            // 6. Army, within the cap and keeping a reserve.
            if (s.ArmyIncludingQueued < s.ArmyCap(cfg) && QueueHasRoom(s, cfg))
            {
                var unit = CompositionRules.Choose(s, cfg, cost, ignoreReserve: defending);
                if (unit == AiActionType.SaveFor)
                {
                    var goal = CompositionRules.WantArtillery(s, cfg, cost) ? AiActionType.ProduceArtillery : AiActionType.ProduceHeavyTank;
                    return new AiAction(AiActionType.SaveFor, "army composition", goal);
                }
                if (unit != AiActionType.None)
                    return new AiAction(unit, $"army {s.ArmyIncludingQueued + 1}/{s.ArmyCap(cfg)}");
                return new AiAction(AiActionType.SaveFor, "army unit", AiActionType.ProduceLightTank);
            }

            return new AiAction(AiActionType.None, s.ArmyIncludingQueued >= s.ArmyCap(cfg) ? "army at cap" : "factory queue full");
        }

        public static int DesiredHarvesters(in CommanderSnapshot s, in CommanderSettings cfg)
        {
            int want = s.Refineries * cfg.HarvestersPerRefinery;
            if (want > cfg.MaxHarvesters) want = cfg.MaxHarvesters;
            return want < 1 ? 1 : want;
        }

        public static bool QueueHasRoom(in CommanderSnapshot s, in CommanderSettings cfg) =>
            s.QueuedHarvesters + s.QueuedLight + s.QueuedHeavy + s.QueuedRecon + s.QueuedArtillery < cfg.MaxQueuedUnits * (s.Factories < 1 ? 1 : s.Factories);

        public static bool Affordable(int credits, int cost, int reserve = 0) => cost >= 0 && credits - reserve >= cost;

        internal static AiAction BuyOrSave(int credits, int price, AiActionType type, string reason) =>
            Affordable(credits, price) ? new AiAction(type, reason) : new AiAction(AiActionType.SaveFor, $"{reason} ({credits}/{price})", type);
    }

    /// <summary>Light/Heavy mix: more Heavies against armoured opponents, Lights when money is short.</summary>
    public static class CompositionRules
    {
        public const float BaseHeavyShare = 0.4f;
        public const float CounterHeavyShare = 0.65f;

        /// <summary>Fast light tanks are the answer to enemy artillery, so fewer heavies are wanted while it is around.</summary>
        public const float AntiArtilleryHeavyShare = 0.25f;

        /// <summary>Easy (no counter-building): a fixed, light-heavy mix.</summary>
        public const float FixedHeavyShare = 0.25f;
        /// <summary>Hard (advanced counters): heavier answer to armour, lighter answer to artillery.</summary>
        public const float AdvancedCounterHeavyShare = 0.75f;
        public const float AdvancedAntiArtilleryHeavyShare = 0.15f;

        public static float DesiredHeavyShare(in CommanderSnapshot s) =>
            s.KnownEnemyHeavy > s.KnownEnemyLight ? CounterHeavyShare
            : s.KnownEnemyArtillery > 0 ? AntiArtilleryHeavyShare
            : BaseHeavyShare;

        public static float DesiredHeavyShare(in CommanderSnapshot s, in CommanderSettings cfg)
        {
            if (cfg.NoCounterBuilding) return FixedHeavyShare;
            if (!cfg.AdvancedCounters) return DesiredHeavyShare(s);
            return s.KnownEnemyHeavy > s.KnownEnemyLight ? AdvancedCounterHeavyShare
                : s.KnownEnemyArtillery > 0 ? AdvancedAntiArtilleryHeavyShare
                : BaseHeavyShare;
        }

        /// <summary>Artillery wanted: available (cost known, Research Centre built) and worth it (enemy defences known or a big army).</summary>
        public static bool WantArtillery(in CommanderSnapshot s, in CommanderSettings cfg, in CostTable cost)
        {
            if (cost.Artillery <= 0 || s.ResearchCentres == 0) return false;
            if (!cfg.NoCounterBuilding && s.KnownEnemyDefences == 0 && s.ArmyCount < 6) return false;
            if (cfg.NoCounterBuilding && s.ArmyCount < 6) return false;
            int have = s.Artillery + s.QueuedArtillery;
            int want = System.Math.Max(1, (int)System.Math.Round(s.ArmyIncludingQueued * cfg.ArtilleryShare));
            return have < want;
        }

        /// <summary>
        /// Returns ProduceArtillery, ProduceHeavyTank, ProduceLightTank, None (nothing affordable) or — when not in
        /// an emergency and the wanted unit is not affordable yet — SaveFor (Milestone 007: saving for heavies and
        /// artillery fixes the Light-Tank-only armies the M007 baseline study found).
        /// </summary>
        public static AiActionType Choose(in CommanderSnapshot s, in CommanderSettings cfg, in CostTable cost, bool ignoreReserve)
        {
            // No ore left anywhere: income has ended, so saving and the reserve only strand credits.
            if (!s.OreAvailable) ignoreReserve = true;
            int reserve = ignoreReserve ? 0 : cfg.CreditReserve;
            if (!ignoreReserve && WantArtillery(s, cfg, cost))
            {
                if (EconomyPlanner.Affordable(s.Credits, cost.Artillery, reserve)) return AiActionType.ProduceArtillery;
                if (!cfg.NoCounterBuilding) return AiActionType.SaveFor;
            }
            int heavies = s.HeavyTanks + s.QueuedHeavy;
            int total = s.ArmyIncludingQueued;
            // Small tolerance: an exact match of the target share counts as met regardless of float rounding.
            float share = DesiredHeavyShare(s, cfg);
            bool wantHeavy = total == 0 ? share >= 0.5f : (float)heavies / total < share - 1e-4f;

            if (wantHeavy && EconomyPlanner.Affordable(s.Credits, cost.HeavyTank, reserve)) return AiActionType.ProduceHeavyTank;
            // Not an emergency: save for the heavy instead of always buying the cheaper tank (only with M007 content).
            if (wantHeavy && !ignoreReserve && cost.Artillery > 0 && !cfg.NoCounterBuilding) return AiActionType.SaveFor;
            if (EconomyPlanner.Affordable(s.Credits, cost.LightTank, reserve)) return AiActionType.ProduceLightTank;
            // End game: the last credits buy whatever armed unit they can (a recon), rather than sitting unspent.
            if (!s.OreAvailable && cost.Recon > 0 && EconomyPlanner.Affordable(s.Credits, cost.Recon)) return AiActionType.ProduceRecon;
            return AiActionType.None;
        }
    }
}
