using System;
using Ironfront.Combat;
using Ironfront.Core.Repair;
using Ironfront.Data;
using Ironfront.Match;
using UnityEngine;

namespace Ironfront.Buildings
{
    /// <summary>
    /// In-place repair of one structure (Milestone 009). Switched on and off by its owner (selection HUD, or the
    /// computer commander through the same method). While on, the building regains health gradually — also under
    /// attack — and its faction pays in proportion to the health restored (<see cref="RepairMeter"/>). Without credits
    /// it pauses and resumes by itself; at full health it switches off. A destroyed building cannot be repaired.
    /// Visual and audio feedback is handled by <see cref="Visuals.RepairFx"/>, which only reads this component.
    /// </summary>
    [DisallowMultipleComponent]
    public class BuildingRepair : MonoBehaviour
    {
        public enum Status { Off, Repairing, PausedNoCredits, Complete }

        private Building _building;
        private Health _health;
        private RepairMeter _meter;

        public bool IsOn { get; private set; }
        public Status State { get; private set; } = Status.Off;
        /// <summary>Credits spent on this building's repairs this match.</summary>
        public int TotalCharged => _meter != null ? _meter.TotalCharged : 0;
        public float TotalRestored => _meter != null ? _meter.TotalRestored : 0f;
        public Building Building => _building;

        /// <summary>Raised when the switch or the status changes.</summary>
        public event Action<BuildingRepair> Changed;
        /// <summary>Raised for every building's change (HUD, statistics, tests).</summary>
        public static event Action<BuildingRepair> AnyChanged;

        private void Awake()
        {
            _building = GetComponent<Building>();
            _health = GetComponent<Health>();
        }

        /// <summary>Price used for repair costs: the construction cost, or a notional value for unbuyable structures (HQ).</summary>
        public static int RepairValue(BuildingDefinition def, RepairSettings settings) =>
            def == null ? 0 : def.Cost > 0 ? def.Cost : settings.UnbuyableBuildingValue;

        /// <summary>Credits per second while repairing at full speed (for the HUD).</summary>
        public float CreditsPerSecond
        {
            get
            {
                var s = RepairSettings.Current;
                if (_building == null || _building.Definition == null || _health == null) return 0f;
                float cost = RepairMeter.CostPerHealthFor(RepairValue(_building.Definition, s), _health.Max, s.BuildingCostFraction, _building.Definition.RepairCostMultiplier);
                return cost * s.BuildingRatePerSecond * _health.Max;
            }
        }

        public float HealthPerSecond => _health != null ? RepairSettings.Current.BuildingRatePerSecond * _health.Max : 0f;

        /// <summary>Switches repairs on or off. Refused (false) for destroyed or undamaged buildings when switching on.</summary>
        public bool SetRepairing(bool on)
        {
            if (on && (_health == null || !_health.IsAlive || _health.Current >= _health.Max)) return false;
            if (on && _building != null && _building.Sale != null && _building.Sale.IsDismantling) return false; // Milestone 010
            if (IsOn == on) return true;
            IsOn = on;
            if (on && _meter == null) _meter = CreateMeter();
            SetState(on ? Status.Repairing : Status.Off);
            return true;
        }

        public bool Toggle() => SetRepairing(!IsOn);

        private RepairMeter CreateMeter()
        {
            var s = RepairSettings.Current;
            var def = _building.Definition;
            float costPerHp = RepairMeter.CostPerHealthFor(RepairValue(def, s), _health.Max, s.BuildingCostFraction, def != null ? def.RepairCostMultiplier : 1f);
            return new RepairMeter(costPerHp, s.BuildingRatePerSecond * _health.Max);
        }

        private void Update()
        {
            if (!IsOn) return;
            if (_health == null || !_health.IsAlive) { IsOn = false; SetState(Status.Off); return; }
            var flow = MatchFlowController.Current;
            if (flow != null && flow.IsOver) return;
            float missing = _health.Max - _health.Current;
            if (missing <= 0.001f)
            {
                IsOn = false;
                SetState(Status.Complete);
                return;
            }
            var match = MatchController.Current;
            if (match == null) return;
            var wallet = match.GetFaction(_building.FactionId).Wallet;
            int before = _meter.TotalCharged;
            float hp = _meter.Step(Time.deltaTime, missing, wallet.Credits, wallet.TrySpend);
            RepairLedger.AddBuilding(_building.FactionId, _meter.TotalCharged - before);
            if (hp > 0f) _health.Restore(hp);
            SetState(_meter.PausedForCredits ? Status.PausedNoCredits : Status.Repairing);
        }

        private void SetState(Status s)
        {
            if (State == s) return;
            State = s;
            Changed?.Invoke(this);
            AnyChanged?.Invoke(this);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => AnyChanged = null;
    }
}
