using System;

namespace Ironfront.Core.Combat
{
    /// <summary>Enforces a minimum interval between shots.</summary>
    public sealed class WeaponCooldown
    {
        public float Interval { get; }
        public float Remaining { get; private set; }
        public bool IsReady => Remaining <= 0f;

        public WeaponCooldown(float interval)
        {
            if (interval <= 0f) throw new ArgumentOutOfRangeException(nameof(interval));
            Interval = interval;
        }

        public void Tick(float deltaTime)
        {
            if (Remaining > 0f) Remaining = Math.Max(0f, Remaining - deltaTime);
        }

        /// <summary>Consumes the cooldown if ready. Returns false (and changes nothing) otherwise.</summary>
        public bool TryFire()
        {
            if (!IsReady) return false;
            Remaining = Interval;
            return true;
        }
    }
}
