using System;

namespace Ironfront.Core.Combat
{
    /// <summary>Armour and faction rules shared by every damage source.</summary>
    public static class DamageRules
    {
        /// <summary>Armour can never make a unit invulnerable.</summary>
        public const float MaxArmour = 0.9f;

        /// <summary>Every hit deals at least this much.</summary>
        public const float MinimumDamage = 1f;

        public static float ClampArmour(float armour) => Math.Max(0f, Math.Min(MaxArmour, armour));

        /// <summary>Damage after armour: raw × (1 − armour), never below <see cref="MinimumDamage"/>.</summary>
        public static float Mitigate(float rawDamage, float armour)
        {
            if (rawDamage <= 0f) return 0f;
            return Math.Max(MinimumDamage, rawDamage * (1f - ClampArmour(armour)));
        }

        /// <summary>Whether two factions are enemies. Different factions are hostile; alliances can extend this later.</summary>
        public static bool AreHostile(int factionA, int factionB) => factionA != factionB;

        /// <summary>Whether an attacker may damage a target. Same-faction damage needs explicit friendly fire.</summary>
        public static bool CanDamage(int attackerFaction, int targetFaction, bool allowFriendlyFire) =>
            AreHostile(attackerFaction, targetFaction) || allowFriendlyFire;
    }
}
