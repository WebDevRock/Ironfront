using Ironfront.Core.Combat;
using UnityEngine;

namespace Ironfront.Data
{
    /// <summary>Data for a weapon: damage, range, cooldown and projectile behaviour.</summary>
    [CreateAssetMenu(menuName = "Ironfront/Weapon Definition", fileName = "WeaponDefinition")]
    public class WeaponDefinition : ScriptableObject
    {
        [SerializeField] private string displayName = "Cannon";
        [Tooltip("Damage per hit before the target's armour.")]
        [SerializeField, Min(0f)] private float damage = 25f;
        [Tooltip("Maximum firing distance to the target's nearest surface, in metres.")]
        [SerializeField, Min(0.5f)] private float range = 10f;
        [Tooltip("Distance at which attack-moving units notice enemies. Never less than range.")]
        [SerializeField, Min(0.5f)] private float acquisitionRange = 14f;
        [Tooltip("Minimum seconds between shots.")]
        [SerializeField, Min(0.05f)] private float cooldown = 1f;
        [SerializeField, Min(1f)] private float projectileSpeed = 40f;
        [SerializeField] private bool requiresLineOfSight = true;
        [Tooltip("Off by default: shots at same-faction targets deal no damage.")]
        [SerializeField] private bool allowFriendlyFire;
        [SerializeField, Min(0.05f)] private float projectileSize = 0.3f;

        [Header("Milestone 007")]
        [Tooltip("Targets closer than this cannot be engaged (artillery). 0 = none.")]
        [SerializeField, Min(0f)] private float minRange;
        [Tooltip("Damage radius around the impact point; 0 = single target.")]
        [SerializeField, Min(0f)] private float splashRadius;
        [Tooltip("Projectiles per shot; damage is split between them.")]
        [SerializeField, Min(1)] private int salvo = 1;
        [Tooltip("Shells fly an arc to where the target was when fired (moving targets can dodge) instead of homing.")]
        [SerializeField] private bool ballistic;
        [Tooltip("Damage multipliers against light vehicles, heavy vehicles and structures.")]
        [SerializeField] private ClassMultipliers effectiveness = ClassMultipliers.One;
        [Tooltip("Short player-facing summary, e.g. \"Strong vs heavy armour\".")]
        [SerializeField] private string role = "";

        public string DisplayName => displayName;
        public float Damage => damage;
        public float Range => range;
        public float AcquisitionRange => Mathf.Max(range, acquisitionRange);
        public float Cooldown => cooldown;
        public float ProjectileSpeed => projectileSpeed;
        public bool RequiresLineOfSight => requiresLineOfSight;
        public bool AllowFriendlyFire => allowFriendlyFire;
        public float ProjectileSize => projectileSize;
        public float MinRange => minRange;
        public float SplashRadius => splashRadius;
        public int Salvo => Mathf.Max(1, salvo);
        public bool Ballistic => ballistic;
        public ClassMultipliers Effectiveness => effectiveness;
        public string Role => role;
    }
}
