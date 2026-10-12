using System;

namespace Ironfront.Core.Economy
{
    /// <summary>
    /// Building sales (Milestone 010): deterministic refund rule and the AI's reasons to sell. Pure.
    /// </summary>
    public static class SaleRules
    {
        /// <summary>Default share of a structure's value returned at full health.</summary>
        public const float DefaultRefundFraction = 0.5f;

        /// <summary>
        /// Credits returned for a structure worth <paramref name="saleValue"/> with the given health:
        /// floor(value × fraction × health / max). Zero for a destroyed (0 HP) structure or a zero value.
        /// </summary>
        public static int Refund(int saleValue, float currentHealth, float maxHealth, float fraction = DefaultRefundFraction)
        {
            if (saleValue <= 0 || maxHealth <= 0f || currentHealth <= 0f || fraction <= 0f) return 0;
            float health01 = Math.Min(1f, currentHealth / maxHealth);
            fraction = Math.Min(1f, fraction);
            // Small epsilon: 0.5 × 1000 × 1.0 must give exactly 500 despite float rounding.
            return (int)Math.Floor(saleValue * fraction * health01 + 1e-4);
        }

        /// <summary>
        /// Computer commander: sell a refinery whose ore is gone (obsolete) while another refinery still works, or a
        /// forward command centre that is about to be lost (below <paramref name="criticalHealth"/>, under attack, no own
        /// forces near) to salvage its value.
        /// </summary>
        public static bool AiShouldSellRefinery(bool oreNearby, int refineries) => !oreNearby && refineries > 1;

        public static bool AiShouldSellForwardBase(float health01, bool underAttack, int ownArmyNearby, float criticalHealth = 0.25f) =>
            health01 < criticalHealth && underAttack && ownArmyNearby == 0;

        /// <summary>
        /// Computer commander: a forward command centre whose territory holds no ore any more and no factory is obsolete;
        /// selling it returns half the vehicle's price and frees the slot for a new forward base.
        /// </summary>
        public static bool AiShouldSellObsoleteForwardBase(bool oreInTerritory, int factoriesInTerritory) => !oreInTerritory && factoriesInTerritory == 0;
    }

    public enum SaleState { Idle, Dismantling, Sold }

    /// <summary>
    /// One structure's sale (Milestone 010): Idle → Dismantling → Sold, or back to Idle on cancel. The refund is
    /// computed and reported exactly once, on the transition to Sold. Duplicate requests and requests on a destroyed
    /// structure are refused. Pure; <see cref="Ironfront.Buildings.BuildingSale"/> drives it.
    /// </summary>
    public sealed class SaleProcess
    {
        public float Duration { get; }
        public SaleState State { get; private set; } = SaleState.Idle;
        public float Elapsed { get; private set; }
        public float Progress01 => Duration <= 0f ? 1f : Math.Min(1f, Elapsed / Duration);
        public int RefundPaid { get; private set; }

        public event Action<SaleState> StateChanged;

        public SaleProcess(float duration) => Duration = Math.Max(0f, duration);

        /// <summary>Starts dismantling. False when already dismantling or sold, or when the structure is destroyed.</summary>
        public bool Begin(bool alive)
        {
            if (!alive || State != SaleState.Idle) return false;
            Elapsed = 0f;
            Set(SaleState.Dismantling);
            return true;
        }

        public bool Cancel()
        {
            if (State != SaleState.Dismantling) return false;
            Elapsed = 0f;
            Set(SaleState.Idle);
            return true;
        }

        /// <summary>
        /// Advances dismantling. On completion, calls <paramref name="refund"/> once with the amount and returns true.
        /// A structure that died meanwhile gets nothing (<paramref name="alive"/> false cancels the sale).
        /// </summary>
        public bool Tick(float dt, bool alive, Func<int> computeRefund, Action<int> refund)
        {
            if (State != SaleState.Dismantling) return false;
            if (!alive) { Cancel(); return false; }
            Elapsed += Math.Max(0f, dt);
            if (Elapsed < Duration) return false;
            int amount = computeRefund != null ? Math.Max(0, computeRefund()) : 0;
            RefundPaid = amount;
            Set(SaleState.Sold); // before paying: a re-entrant call cannot pay twice
            refund?.Invoke(amount);
            return true;
        }

        private void Set(SaleState s)
        {
            State = s;
            StateChanged?.Invoke(s);
        }
    }
}
