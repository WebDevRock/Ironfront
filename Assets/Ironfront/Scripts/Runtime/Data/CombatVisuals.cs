using UnityEngine;

namespace Ironfront.Data
{
    /// <summary>Materials for combat feedback (projectiles, impacts, health bars, order markers).</summary>
    [CreateAssetMenu(menuName = "Ironfront/Combat Visuals", fileName = "CombatVisuals")]
    public class CombatVisuals : ScriptableObject
    {
        [SerializeField] private Material projectile;
        [SerializeField] private Material muzzleFlash;
        [SerializeField] private Material impact;
        [SerializeField] private Material explosion;
        [SerializeField] private Material healthBarBackground;
        [Tooltip("Tinted green → red with health via a property block.")]
        [SerializeField] private Material healthBarFill;
        [SerializeField] private Material moveMarker;
        [SerializeField] private Material attackMoveMarker;
        [SerializeField] private Material attackMarker;

        public Material Projectile => projectile;
        public Material MuzzleFlash => muzzleFlash;
        public Material Impact => impact;
        public Material Explosion => explosion;
        public Material HealthBarBackground => healthBarBackground;
        public Material HealthBarFill => healthBarFill;
        public Material MoveMarker => moveMarker;
        public Material AttackMoveMarker => attackMoveMarker;
        public Material AttackMarker => attackMarker;
    }
}
