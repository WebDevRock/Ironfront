using System;

namespace Ironfront.Core.Combat
{
    /// <summary>Target categories for weapon effectiveness (Milestone 007 counters).</summary>
    public enum ArmourClass
    {
        Light,
        Heavy,
        Structure,
    }

    /// <summary>Damage multipliers of a weapon against each armour class (1 = listed damage).</summary>
    [Serializable]
    public struct ClassMultipliers
    {
        public float VsLight;
        public float VsHeavy;
        public float VsStructure;

        public static ClassMultipliers One => new ClassMultipliers { VsLight = 1f, VsHeavy = 1f, VsStructure = 1f };

        public float For(ArmourClass c) =>
            c == ArmourClass.Heavy ? VsHeavy : c == ArmourClass.Structure ? VsStructure : VsLight;
    }

    /// <summary>Weapon effectiveness, splash and minimum-range rules. Pure maths.</summary>
    public static class WeaponClassRules
    {
        /// <summary>Raw damage after the armour-class multiplier and an upgrade multiplier (armour is applied later).</summary>
        public static float RawDamage(float baseDamage, in ClassMultipliers m, ArmourClass target, float upgradeMultiplier = 1f) =>
            Math.Max(0f, baseDamage * Math.Max(0f, m.For(target)) * Math.Max(0f, upgradeMultiplier));

        /// <summary>
        /// Splash falloff: full damage within the inner 30 % of the radius, then linear down to 25 % at the edge,
        /// zero outside.
        /// </summary>
        public static float SplashFactor(float distance, float radius)
        {
            if (radius <= 0f) return distance <= 0f ? 1f : 0f;
            if (distance > radius) return 0f;
            float inner = radius * 0.3f;
            if (distance <= inner) return 1f;
            float t = (distance - inner) / (radius - inner);
            return 1f - 0.75f * t;
        }

        /// <summary>True when a target at <paramref name="distance"/> can be fired upon by a weapon with these ranges.</summary>
        public static bool InFiringBand(float distance, float minRange, float maxRange) =>
            distance <= maxRange && distance >= minRange;
    }
}
