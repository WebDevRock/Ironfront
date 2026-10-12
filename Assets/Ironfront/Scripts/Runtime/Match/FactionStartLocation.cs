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

        /// <summary>Creates a marker at runtime (skirmish maps spawn their bases; Milestone 008).</summary>
        public static FactionStartLocation Create(int factionId, Vector3 position, Transform parent = null)
        {
            var go = new GameObject($"StartLocation_F{factionId}");
            go.SetActive(false);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            var marker = go.AddComponent<FactionStartLocation>();
            marker.factionId = factionId;
            go.SetActive(true);
            return marker;
        }

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
