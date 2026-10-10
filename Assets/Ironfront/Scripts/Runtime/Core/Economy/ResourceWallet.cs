using System;

namespace Ironfront.Core.Economy
{
    /// <summary>
    /// A faction's Ore credits. All mutations go through this class, which guarantees the balance never
    /// becomes negative. Raises <see cref="Changed"/> with the new balance after every real change.
    /// </summary>
    public sealed class ResourceWallet
    {
        public int Credits { get; private set; }

        public event Action<int> Changed;

        public ResourceWallet(int startingCredits = 0)
        {
            if (startingCredits < 0) throw new ArgumentOutOfRangeException(nameof(startingCredits));
            Credits = startingCredits;
        }

        public bool CanAfford(int amount) => amount >= 0 && Credits >= amount;

        /// <summary>Spends credits if the balance covers the amount. Returns false (and changes nothing) otherwise.</summary>
        public bool TrySpend(int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount), "Cannot spend a negative amount.");
            if (Credits < amount) return false;
            if (amount == 0) return true;
            Credits -= amount;
            Changed?.Invoke(Credits);
            return true;
        }

        /// <summary>Adds credits (harvest deliveries, refunds). Saturates at int.MaxValue.</summary>
        public void Add(int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount), "Use TrySpend to remove credits.");
            if (amount == 0) return;
            Credits = (int)Math.Min((long)Credits + amount, int.MaxValue);
            Changed?.Invoke(Credits);
        }
    }
}
