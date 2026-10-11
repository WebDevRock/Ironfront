using System;
using UnityEngine;

namespace Ironfront.Combat
{
    /// <summary>Anything that fires shots: mobile units (<see cref="CombatUnit"/>) and defences (<see cref="DefenceTurret"/>).</summary>
    public interface IFiringWeapon
    {
        /// <summary>Raised with the target each time a shot is fired.</summary>
        event Action<Health> Fired;
        Transform Muzzle { get; }
    }
}
