using System.Collections.Generic;
using UnityEngine;

namespace Ironfront.Buildings
{
    /// <summary>
    /// Credits each faction spent on repairs this match (Milestone 009: statistics, balance studies and the HUD).
    /// Reset when a match starts; only the repair systems add to it.
    /// </summary>
    public static class RepairLedger
    {
        private static readonly Dictionary<int, int> s_buildings = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> s_vehicles = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> s_vehiclesRepaired = new Dictionary<int, int>();

        public static void Reset()
        {
            s_buildings.Clear();
            s_vehicles.Clear();
            s_vehiclesRepaired.Clear();
        }

        internal static void AddBuilding(int faction, int credits) { if (credits > 0) s_buildings[faction] = BuildingCredits(faction) + credits; }
        internal static void AddVehicle(int faction, int credits) { if (credits > 0) s_vehicles[faction] = VehicleCredits(faction) + credits; }
        internal static void CountVehicleRepaired(int faction) => s_vehiclesRepaired[faction] = VehiclesRepaired(faction) + 1;

        public static int BuildingCredits(int faction) => s_buildings.TryGetValue(faction, out int v) ? v : 0;
        public static int VehicleCredits(int faction) => s_vehicles.TryGetValue(faction, out int v) ? v : 0;
        public static int VehiclesRepaired(int faction) => s_vehiclesRepaired.TryGetValue(faction, out int v) ? v : 0;
        public static int Total(int faction) => BuildingCredits(faction) + VehicleCredits(faction);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Reset();
    }
}
