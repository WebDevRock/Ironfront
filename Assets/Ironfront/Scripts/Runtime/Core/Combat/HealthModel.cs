using System;

namespace Ironfront.Core.Combat
{
    /// <summary>
    /// Hit points with armour. Damage is reduced by armour (see <see cref="DamageRules"/>); death happens
    /// exactly once, when health first reaches zero.
    /// </summary>
    public sealed class HealthModel
    {
        public float Max { get; }
        public float Current { get; private set; }
        /// <summary>Fraction of incoming damage absorbed, 0..<see cref="DamageRules.MaxArmour"/>.</summary>
        public float Armour { get; private set; }
        public bool IsDead { get; private set; }
        public float Fraction => Max > 0f ? Current / Max : 0f;
        public bool IsDamaged => Current < Max;

        /// <summary>Raised with the damage actually dealt (after armour).</summary>
        public event Action<float> Damaged;
        public event Action Died;

        public HealthModel(float max, float armour)
        {
            if (max <= 0f) throw new ArgumentOutOfRangeException(nameof(max));
            Max = max;
            Current = max;
            Armour = DamageRules.ClampArmour(armour);
        }

        /// <summary>Changes armour (research upgrades); clamped like the constructor value.</summary>
        public void SetArmour(float armour) => Armour = DamageRules.ClampArmour(armour);

        /// <summary>Applies raw damage (armour is applied here). Returns the damage dealt.</summary>
        public float ApplyDamage(float rawDamage)
        {
            if (IsDead || rawDamage <= 0f) return 0f;
            float dealt = Math.Min(Current, DamageRules.Mitigate(rawDamage, Armour));
            Current -= dealt;
            Damaged?.Invoke(dealt);
            if (Current <= 0f)
            {
                Current = 0f;
                IsDead = true;
                Died?.Invoke();
            }
            return dealt;
        }
    }
}
