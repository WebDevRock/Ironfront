using Ironfront.Core.Repair;
using UnityEngine;

namespace Ironfront.Data
{
    /// <summary>
    /// Every repair number in one place (Milestone 009), so repairs can be balanced without touching code.
    /// Cost fractions say what share of an item's price restores all its health; definitions can scale that with their
    /// own <c>repairCostMultiplier</c>.
    /// </summary>
    [CreateAssetMenu(menuName = "Ironfront/Repair Settings", fileName = "RepairSettings")]
    public class RepairSettings : ScriptableObject
    {
        [Header("Buildings (repair in place)")]
        [Tooltip("Share of the construction cost that restores 100 % of a building's health.")]
        [SerializeField, Range(0f, 2f)] private float buildingCostFraction = 0.5f;
        [Tooltip("Share of max health restored per second.")]
        [SerializeField, Range(0.001f, 1f)] private float buildingRatePerSecond = 0.01f; // M009 study: 2.5 % let a besieged HQ out-repair a six-tank attack
        [Tooltip("Price basis for structures that cannot be bought (the headquarters), so their repairs are not free.")]
        [SerializeField, Min(1)] private int unbuyableBuildingValue = 2000;

        [Header("Vehicles (Repair Pad)")]
        [Tooltip("Share of the purchase price that restores 100 % of a vehicle's health.")]
        [SerializeField, Range(0f, 2f)] private float vehicleCostFraction = 0.4f;
        [Tooltip("Share of max health a pad restores per second.")]
        [SerializeField, Range(0.001f, 1f)] private float padRatePerSecond = 0.06f;
        [Tooltip("Seconds a newly placed pad spends assembling before it can repair.")]
        [SerializeField, Min(0f)] private float padAssemblyTime = 15f;
        [Tooltip("Radius of the bay on the pad's apron (m).")]
        [SerializeField, Min(1f)] private float bayRadius = 3f;

        [Header("Navigation recovery")]
        [SerializeField, Min(1f)] private float stallSeconds = 8f;
        [SerializeField, Min(0.1f)] private float stallDistance = 1f;
        [SerializeField, Min(0)] private int maxReroutes = 3;
        [SerializeField, Min(5f)] private float travelTimeout = 120f;
        [SerializeField, Min(5f)] private float enterTimeout = 25f;
        [SerializeField, Min(5f)] private float exitTimeout = 20f;
        [Tooltip("A friendly vehicle left idle in the bay this long is asked to move off it.")]
        [SerializeField, Min(2f)] private float bayBlockerSeconds = 10f;

        public float BuildingCostFraction => buildingCostFraction;
        public float BuildingRatePerSecond => buildingRatePerSecond;
        public int UnbuyableBuildingValue => unbuyableBuildingValue;
        public float VehicleCostFraction => vehicleCostFraction;
        public float PadRatePerSecond => padRatePerSecond;
        public float PadAssemblyTime => padAssemblyTime;
        public float BayRadius => bayRadius;
        public float BayBlockerSeconds => bayBlockerSeconds;

        public RepairTimeouts Timeouts => new RepairTimeouts
        {
            StallSeconds = stallSeconds, StallDistance = stallDistance, MaxReroutes = maxReroutes, TravelSeconds = travelTimeout,
            EnterSeconds = enterTimeout, ExitSeconds = exitTimeout, ArriveRadius = 2.5f, BayArriveRadius = 1.5f,
        };

        private static RepairSettings s_defaults;

        /// <summary>The match's settings, or built-in defaults (scenes or tests without the asset).</summary>
        public static RepairSettings Current
        {
            get
            {
                var setup = Match.MatchController.Current != null ? Match.MatchController.Current.Setup : null;
                if (setup != null && setup.Repair != null) return setup.Repair;
                if (s_defaults == null) s_defaults = CreateInstance<RepairSettings>();
                return s_defaults;
            }
        }
    }
}
