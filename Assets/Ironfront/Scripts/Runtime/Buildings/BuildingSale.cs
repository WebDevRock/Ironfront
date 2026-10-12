using System;
using System.Collections.Generic;
using Ironfront.Combat;
using Ironfront.Core.Economy;
using Ironfront.Data;
using Ironfront.Match;
using UnityEngine;

namespace Ironfront.Buildings
{
    /// <summary>
    /// Selling (dismantling) one structure (Milestone 010). The owner starts a sale; after
    /// <see cref="TerritorySettings.SellSeconds"/> of dismantling the faction is refunded
    /// <see cref="SaleRules.Refund"/> (value × 50 % × remaining health, read at completion) exactly once, and the
    /// structure is removed through the normal unregistration path (power, prerequisites, territory, NavMesh obstacle).
    /// <para>Rules: the Headquarters and other unpriced structures cannot be sold; a second request is refused; the sale
    /// can be cancelled at no cost before it completes; a structure destroyed while dismantling refunds nothing. A
    /// factory's queued units are refunded in full (they were paid when queued, as with the cancel button); selling the
    /// faction's last Research Centre cancels the topic in progress with a full refund. Dismantling switches in-place
    /// repair off.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public class BuildingSale : MonoBehaviour
    {
        private static readonly Dictionary<int, int> s_refunded = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> s_sold = new Dictionary<int, int>();

        private Building _building;
        private Health _health;
        private SaleProcess _process;

        public static event Action<BuildingSale> AnyStarted;
        public static event Action<BuildingSale> AnyCancelled;
        /// <summary>A sale completed: the structure (about to be removed) and the credits refunded.</summary>
        public static event Action<Building, int> AnySold;

        public Building Building => _building;
        public SaleState State => _process != null ? _process.State : SaleState.Idle;
        public bool IsDismantling => State == SaleState.Dismantling;
        public float Progress01 => _process != null ? _process.Progress01 : 0f;
        public float Duration => _process != null ? _process.Duration : TerritorySettings.Current.SellSeconds;
        public float SecondsRemaining => IsDismantling ? Mathf.Max(0f, Duration * (1f - Progress01)) : Duration;
        public int RefundPaid => _process != null ? _process.RefundPaid : 0;
        public bool NeedsConfirmation => _building != null && _building.Definition != null && _building.Definition.SellConfirmation;

        /// <summary>Refund if the sale completed now (deterministic: value, health, refund fraction).</summary>
        public int EstimatedRefund
        {
            get
            {
                if (_building == null || _building.Definition == null || _health == null) return 0;
                return SaleRules.Refund(_building.Definition.SaleValue, _health.Current, _health.Max, TerritorySettings.Current.RefundFraction);
            }
        }

        private void Awake()
        {
            _building = GetComponent<Building>();
            _health = GetComponent<Health>();
            _process = new SaleProcess(TerritorySettings.Current.SellSeconds);
        }

        /// <summary>Whether the owner may start selling now, and if not, why.</summary>
        public bool CanSell(out string reason)
        {
            reason = "";
            if (_building == null || _building.Definition == null || !_building.Definition.Sellable) { reason = "This structure cannot be sold"; return false; }
            if (_health != null && !_health.IsAlive) { reason = "Destroyed"; return false; }
            var flow = MatchFlowController.Current;
            if (flow != null && flow.IsOver) { reason = "The match is over"; return false; }
            if (State == SaleState.Dismantling) { reason = "Already being sold"; return false; }
            if (State == SaleState.Sold) { reason = "Already sold"; return false; }
            return true;
        }

        /// <summary>Starts dismantling. Returns false (with the reason) when not allowed.</summary>
        public bool Begin(out string reason)
        {
            if (!CanSell(out reason)) return false;
            if (!_process.Begin(_health == null || _health.IsAlive)) { reason = "Already being sold"; return false; }
            if (_building.Repair != null) _building.Repair.SetRepairing(false);
            reason = $"Selling {_building.Definition.DisplayName}";
            AnyStarted?.Invoke(this);
            return true;
        }

        public bool Cancel()
        {
            if (!_process.Cancel()) return false;
            AnyCancelled?.Invoke(this);
            return true;
        }

        private void Update()
        {
            if (!IsDismantling) return;
            var flow = MatchFlowController.Current;
            if (flow != null && flow.IsOver) return; // frozen with the match
            _process.Tick(Time.deltaTime, _health == null || _health.IsAlive, () => EstimatedRefund, Complete);
        }

        private void Complete(int refund)
        {
            var match = MatchController.Current;
            int faction = _building.FactionId;
            if (match != null)
            {
                var state = match.GetFaction(faction);
                // Queued production was paid in advance: refunded in full, exactly as cancelling it would.
                var factory = GetComponent<VehicleFactory>();
                if (factory != null)
                    for (int i = factory.Queue.Count - 1; i >= 0; i--) factory.Cancel(i);
                // The last Research Centre: the topic in progress is cancelled with its full refund.
                if (GetComponent<ResearchCentre>() != null && state.Structures.Count(ResearchCentre.StructureId) <= 1 && state.Research.Current != null)
                    state.Research.Cancel(state.Wallet);
                if (refund > 0) state.Wallet.Add(refund);
            }
            s_refunded[faction] = RefundedBy(faction) + refund;
            s_sold[faction] = SoldBy(faction) + 1;
            AnySold?.Invoke(_building, refund);
            Destroy(gameObject); // removal, not destruction: unregisters in OnDisable, no death event
        }

        public static int RefundedBy(int faction) => s_refunded.TryGetValue(faction, out int v) ? v : 0;
        public static int SoldBy(int faction) => s_sold.TryGetValue(faction, out int v) ? v : 0;

        /// <summary>Clears the per-match sale statistics (match start).</summary>
        public static void ResetMatch()
        {
            s_refunded.Clear();
            s_sold.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ResetMatch();
            AnyStarted = null;
            AnyCancelled = null;
            AnySold = null;
        }
    }
}
