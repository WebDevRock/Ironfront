using System.Collections.Generic;
using UnityEngine;

namespace Ironfront.Match
{
    /// <summary>Marks a faction's starting position on the map.</summary>
    public class FactionStartLocation : MonoBehaviour
    {
        private static readonly List<FactionStartLocation> s_All = new List<FactionStartLocation>();
        public static IReadOnlyList<FactionStartLocation> All => s_All;

        [SerializeField] private int factionId = MatchController.DefaultPlayerFactionId;

        public int FactionId => factionId;

        private void OnEnable() { if (!s_All.Contains(this)) s_All.Add(this); }
        private void OnDisable() => s_All.Remove(this);

        public static FactionStartLocation Of(int factionId)
        {
            foreach (var s in s_All)
                if (s.factionId == factionId) return s;
            return null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => s_All.Clear();
    }
}
