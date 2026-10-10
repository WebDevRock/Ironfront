using Ironfront.Buildings;
using Ironfront.Combat;
using Ironfront.Construction;
using Ironfront.Core;
using UnityEngine;

namespace Ironfront.Match
{
    /// <summary>Collects <see cref="MatchStatistics"/> from gameplay events for the result screen.</summary>
    public class MatchStatisticsTracker : MonoBehaviour
    {
        public static MatchStatisticsTracker Current { get; private set; }
        public MatchStatistics Stats { get; } = new MatchStatistics();

        private void Awake() => Current = this;

        private void OnEnable()
        {
            VehicleFactory.AnyUnitProduced += OnProduced;
            BuildingPlacementController.AnyBuildingPlaced += OnPlaced;
            Refinery.AnyDelivered += OnDelivered;
            Health.AnyDied += OnDied;
        }

        private void OnDisable()
        {
            VehicleFactory.AnyUnitProduced -= OnProduced;
            BuildingPlacementController.AnyBuildingPlaced -= OnPlaced;
            Refinery.AnyDelivered -= OnDelivered;
            Health.AnyDied -= OnDied;
            if (Current == this) Current = null;
        }

        private void OnProduced(VehicleFactory f, GameObject unit) { if (f != null) Stats.UnitProduced(f.FactionId); }
        private void OnPlaced(Building b) { if (b != null) Stats.StructureBuilt(b.FactionId); }
        private void OnDelivered(Refinery r, int ore) { if (r != null) Stats.OreDelivered(r.FactionId, ore); }

        private void OnDied(Health dead)
        {
            if (dead != null) Stats.Destroyed(dead.FactionId, dead.IsStructure, dead.LastAttackerFaction);
        }
    }
}
