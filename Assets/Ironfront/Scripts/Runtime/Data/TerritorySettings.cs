using UnityEngine;

namespace Ironfront.Data
{
    /// <summary>
    /// Milestone 010 balance and presentation data: building sales, MCV deployment and the territory overlay. Territory
    /// radii live on the command structures' <see cref="BuildingDefinition"/>s.
    /// </summary>
    [CreateAssetMenu(menuName = "Ironfront/Territory Settings", fileName = "TerritorySettings")]
    public class TerritorySettings : ScriptableObject
    {
        [Header("Building sales")]
        [Tooltip("Seconds of dismantling before the refund is paid.")]
        [SerializeField, Min(0.5f)] private float sellSeconds = 8f;
        [Tooltip("Share of the structure's value refunded at full health (scaled by remaining health).")]
        [SerializeField, Range(0f, 1f)] private float refundFraction = 0.5f;
        [Tooltip("Seconds a 'confirm sale' click stays armed for critical structures.")]
        [SerializeField, Min(0.5f)] private float confirmSeconds = 3f;

        [Header("Mobile Command Vehicle")]
        [SerializeField, Min(1f)] private float deploySeconds = 15f;
        [Tooltip("Lowest health fraction the Forward Command Centre starts with (it keeps the vehicle's health fraction).")]
        [SerializeField, Range(0.01f, 1f)] private float minConvertedHealth = 0.1f;
        [Tooltip("Largest ground height difference under the footprint corners for a deployment (slope check).")]
        [SerializeField, Min(0.1f)] private float deployHeightTolerance = 0.6f;

        [Header("Overlay")]
        [SerializeField] private Material fillMaterial;
        [SerializeField] private Material edgeMaterial;
        [SerializeField] private Material enemyEdgeMaterial;
        [SerializeField, Min(0.5f)] private float overlayCell = 2f;

        public float SellSeconds => sellSeconds;
        public float RefundFraction => refundFraction;
        public float ConfirmSeconds => confirmSeconds;
        public float DeploySeconds => deploySeconds;
        public float MinConvertedHealth => minConvertedHealth;
        public float DeployHeightTolerance => deployHeightTolerance;
        public Material FillMaterial => fillMaterial;
        public Material EdgeMaterial => edgeMaterial;
        public Material EnemyEdgeMaterial => enemyEdgeMaterial;
        public float OverlayCell => overlayCell;

        private static TerritorySettings s_defaults;

        /// <summary>The match's settings, or built-in defaults (scenes or tests without the asset).</summary>
        public static TerritorySettings Current
        {
            get
            {
                var setup = Match.MatchController.Current != null ? Match.MatchController.Current.Setup : null;
                if (setup != null && setup.Territory != null) return setup.Territory;
                if (s_defaults == null) s_defaults = CreateInstance<TerritorySettings>();
                return s_defaults;
            }
        }
    }
}
