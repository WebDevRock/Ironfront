using System;
using System.Collections.Generic;
using Ironfront.Core.Harvesting;
using Ironfront.Match;
using UnityEngine;

namespace Ironfront.Buildings
{
    /// <summary>Accepts Ore from harvesters at its dock and credits the owning faction.</summary>
    [RequireComponent(typeof(Building))]
    public class Refinery : MonoBehaviour, IOreSink
    {
        private static readonly List<Refinery> s_All = new List<Refinery>();
        public static IReadOnlyList<Refinery> All => s_All;

        private Building _building;

        /// <summary>Raised with the Ore amount after each delivery.</summary>
        public event Action<int> Delivered;

        /// <summary>Raised for every delivery at any refinery (statistics).</summary>
        public static event Action<Refinery, int> AnyDelivered;

        public Building Building => _building != null ? _building : (_building = GetComponent<Building>());
        public int FactionId => Building.FactionId;

        /// <summary>Where harvesters park to unload.</summary>
        public Vector3 DockPoint => Building.FrontPoint;

        private void OnEnable() { if (!s_All.Contains(this)) s_All.Add(this); }
        private void OnDisable() => s_All.Remove(this);

        public void Deliver(int ore)
        {
            if (ore <= 0) return;
            var match = MatchController.Current;
            if (match == null) return;
            int perOre = match.Setup != null ? match.Setup.CreditsPerOre : 1;
            match.GetFaction(FactionId).Wallet.Add(ore * perOre);
            Delivered?.Invoke(ore);
            AnyDelivered?.Invoke(this, ore);
        }

        public static Refinery FindNearest(Vector3 position, int factionId)
        {
            Refinery best = null;
            float bestSqr = float.MaxValue;
            foreach (var r in s_All)
            {
                if (r.FactionId != factionId) continue;
                float d = (r.DockPoint - position).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; best = r; }
            }
            return best;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_All.Clear();
            AnyDelivered = null;
        }
    }
}
