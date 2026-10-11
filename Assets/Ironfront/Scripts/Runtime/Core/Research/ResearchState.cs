using System;
using System.Collections.Generic;
using Ironfront.Core.Economy;

namespace Ironfront.Core.Research
{
    /// <summary>What research gives a faction (multipliers and bonuses applied by the runtime).</summary>
    public enum UpgradeEffect
    {
        None,
        /// <summary>Adds Value to the armour of the faction's vehicles.</summary>
        VehicleArmour,
        /// <summary>Multiplies damage of the faction's vehicles and defences by (1 + Value).</summary>
        WeaponDamage,
        /// <summary>Multiplies harvesting and unloading speed by (1 + Value).</summary>
        HarvestRate,
    }

    /// <summary>One research topic as the rules need it (data comes from ResearchDefinition assets).</summary>
    public interface IResearchTopic
    {
        string Id { get; }
        string DisplayName { get; }
        int Cost { get; }
        float ResearchTime { get; }
        UpgradeEffect Effect { get; }
        float Value { get; }
        /// <summary>Research ids that must be completed first (may be empty).</summary>
        IReadOnlyList<string> RequiredResearch { get; }
    }

    public enum ResearchCheck
    {
        Ok,
        AlreadyResearched,
        InProgress,
        Busy,
        MissingFacility,
        MissingPrerequisite,
        InsufficientFunds,
    }

    /// <summary>
    /// Per-faction, per-match research progress (lives in <see cref="FactionState"/>, so nothing carries over
    /// between matches). One topic at a time; paid in full when started, refunded if cancelled; a topic can
    /// never be researched twice. Progress pauses while the faction has no research facility.
    /// </summary>
    public sealed class ResearchState
    {
        private readonly HashSet<string> _completed = new HashSet<string>();

        public IResearchTopic Current { get; private set; }
        public float Progress { get; private set; }
        public float Progress01 => Current == null ? 0f : Math.Min(1f, Progress / Math.Max(0.01f, Current.ResearchTime));
        public IReadOnlyCollection<string> CompletedTopics => _completed;

        public event Action<string> Started;
        /// <summary>Raised with the topic id when a topic finishes.</summary>
        public event Action<string> Completed;
        public event Action Changed;

        // Effects accumulated from completed topics.
        public float ArmourBonus { get; private set; }
        public float DamageMultiplier { get; private set; } = 1f;
        public float HarvestMultiplier { get; private set; } = 1f;

        public bool IsResearched(string id) => id != null && _completed.Contains(id);
        public bool IsInProgress(string id) => Current != null && Current.Id == id;

        public ResearchCheck Check(IResearchTopic topic, bool hasFacility, ResourceWallet wallet)
        {
            if (topic == null) throw new ArgumentNullException(nameof(topic));
            if (IsResearched(topic.Id)) return ResearchCheck.AlreadyResearched;
            if (IsInProgress(topic.Id)) return ResearchCheck.InProgress;
            if (Current != null) return ResearchCheck.Busy;
            if (!hasFacility) return ResearchCheck.MissingFacility;
            if (topic.RequiredResearch != null)
                foreach (var r in topic.RequiredResearch)
                    if (!string.IsNullOrEmpty(r) && !IsResearched(r)) return ResearchCheck.MissingPrerequisite;
            if (wallet != null && !wallet.CanAfford(topic.Cost)) return ResearchCheck.InsufficientFunds;
            return ResearchCheck.Ok;
        }

        /// <summary>Starts a topic, charging the wallet. Returns the check result (Ok when started).</summary>
        public ResearchCheck TryStart(IResearchTopic topic, bool hasFacility, ResourceWallet wallet)
        {
            var check = Check(topic, hasFacility, wallet);
            if (check != ResearchCheck.Ok) return check;
            if (!wallet.TrySpend(topic.Cost)) return ResearchCheck.InsufficientFunds;
            Current = topic;
            Progress = 0f;
            Started?.Invoke(topic.Id);
            Changed?.Invoke();
            return ResearchCheck.Ok;
        }

        /// <summary>Cancels the topic in progress with a full refund.</summary>
        public bool Cancel(ResourceWallet wallet)
        {
            if (Current == null) return false;
            wallet?.Add(Current.Cost);
            Current = null;
            Progress = 0f;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Advances research; <paramref name="speed"/> 1 = normal, 0.5 when low on power, 0 without a facility.</summary>
        public void Tick(float deltaTime, float speed)
        {
            if (Current == null || deltaTime <= 0f || speed <= 0f) return;
            Progress += deltaTime * speed;
            if (Progress < Current.ResearchTime) return;
            var done = Current;
            Current = null;
            Progress = 0f;
            Complete(done);
        }

        /// <summary>Marks a topic complete and applies its effect (also used by tests and tools).</summary>
        public void Complete(IResearchTopic topic)
        {
            if (topic == null || !_completed.Add(topic.Id)) return;
            switch (topic.Effect)
            {
                case UpgradeEffect.VehicleArmour: ArmourBonus += topic.Value; break;
                case UpgradeEffect.WeaponDamage: DamageMultiplier *= 1f + topic.Value; break;
                case UpgradeEffect.HarvestRate: HarvestMultiplier *= 1f + topic.Value; break;
            }
            Completed?.Invoke(topic.Id);
            Changed?.Invoke();
        }
    }
}
