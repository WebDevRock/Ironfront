using System.Collections.Generic;

namespace Ironfront.Core.Economy
{
    /// <summary>Something a faction can pay for and build, described independently of Unity assets.</summary>
    public interface IConstructable
    {
        string Id { get; }
        string DisplayName { get; }
        int Cost { get; }
        IReadOnlyList<string> PrerequisiteIds { get; }
    }

    public enum BuildCheck
    {
        Ok,
        MissingPrerequisite,
        InsufficientFunds,
    }

    public static class ConstructionRules
    {
        /// <summary>Whether a faction may start building <paramref name="item"/> (prerequisites first, then funds).</summary>
        public static BuildCheck Check(IConstructable item, FactionState faction)
        {
            var prereqs = item.PrerequisiteIds;
            if (prereqs != null)
            {
                for (int i = 0; i < prereqs.Count; i++)
                    if (!faction.Structures.Has(prereqs[i])) return BuildCheck.MissingPrerequisite;
            }
            return faction.Wallet.CanAfford(item.Cost) ? BuildCheck.Ok : BuildCheck.InsufficientFunds;
        }

        /// <summary>Checks and, if allowed, charges the faction. Credits are only spent on success.</summary>
        public static BuildCheck TryPurchase(IConstructable item, FactionState faction)
        {
            var check = Check(item, faction);
            if (check != BuildCheck.Ok) return check;
            return faction.Wallet.TrySpend(item.Cost) ? BuildCheck.Ok : BuildCheck.InsufficientFunds;
        }
    }
}
