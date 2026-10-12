using System;

namespace Ironfront.Core.Repair
{
    /// <summary>
    /// Repair accounting for one repair job (Milestone 009). Restores health gradually and charges credits in proportion
    /// to the health actually restored. Credits are whole numbers, so the fractional cost is carried between steps and a
    /// credit is charged as soon as it has accrued; when the next credit cannot be paid nothing is restored (paused),
    /// and the job resumes by itself once credits are available. Pure and deterministic.
    /// </summary>
    public sealed class RepairMeter
    {
        /// <summary>Credits per hit point restored.</summary>
        public float CostPerHealth { get; }
        /// <summary>Health restored per second while running.</summary>
        public float HealthPerSecond { get; }

        /// <summary>Accrued cost not yet charged (always in [0, 1) after a step).</summary>
        public float Carry { get; private set; }
        public float TotalRestored { get; private set; }
        public int TotalCharged { get; private set; }
        /// <summary>The last step could not run for lack of credits.</summary>
        public bool PausedForCredits { get; private set; }

        public RepairMeter(float costPerHealth, float healthPerSecond)
        {
            if (costPerHealth < 0f) throw new ArgumentOutOfRangeException(nameof(costPerHealth));
            if (healthPerSecond <= 0f) throw new ArgumentOutOfRangeException(nameof(healthPerSecond));
            CostPerHealth = costPerHealth;
            HealthPerSecond = healthPerSecond;
        }

        /// <summary>Cost per hit point for an item: <paramref name="costFraction"/> of its price restores all its health.</summary>
        public static float CostPerHealthFor(int price, float maxHealth, float costFraction, float multiplier = 1f) =>
            maxHealth <= 0f ? 0f : Math.Max(0f, price * costFraction * multiplier / maxHealth);

        /// <summary>Total cost of restoring <paramref name="health"/> points (rounded the way the meter charges).</summary>
        public static int CostFor(float health, float costPerHealth) => (int)Math.Floor(Math.Max(0f, health) * costPerHealth + 1e-4);

        /// <summary>
        /// Advances by <paramref name="dt"/>. Restores at most <paramref name="missing"/> health and spends at most the
        /// faction's credits through <paramref name="trySpend"/> (returns true when the amount was paid).
        /// Returns the health to restore this step.
        /// </summary>
        public float Step(float dt, float missing, int availableCredits, Func<int, bool> trySpend)
        {
            PausedForCredits = false;
            if (dt <= 0f || missing <= 0f) return 0f;
            float want = Math.Min(missing, HealthPerSecond * dt);
            if (CostPerHealth <= 0f)
            {
                TotalRestored += want;
                return want;
            }

            // Affordable health: the carry already accrued plus whole credits that can still be paid.
            float cost = want * CostPerHealth;
            float owed = Carry + cost;
            int whole = (int)Math.Floor(owed + 1e-6);
            if (whole > availableCredits)
            {
                // Restore only what the available credits cover (possibly nothing: paused). The owed total may approach
                // availableCredits + 1, because only whole credits are charged and the remainder is carried.
                float affordable = Math.Max(0f, (Math.Max(0, availableCredits) + 1f - Carry - 1e-4f) / CostPerHealth);
                want = Math.Min(want, affordable);
                if (want <= 1e-5f)
                {
                    PausedForCredits = true;
                    return 0f;
                }
                owed = Carry + want * CostPerHealth;
                whole = (int)Math.Floor(owed + 1e-6);
            }
            if (whole > 0 && !trySpend(whole))
            {
                PausedForCredits = true;
                return 0f;
            }
            TotalCharged += whole;
            Carry = owed - whole;
            TotalRestored += want;
            return want;
        }
    }
}
