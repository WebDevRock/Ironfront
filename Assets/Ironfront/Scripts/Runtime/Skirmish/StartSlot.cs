using System.Collections.Generic;
using Ironfront.Core.Skirmish;
using UnityEngine;

namespace Ironfront.Skirmish
{
    /// <summary>
    /// One of a skirmish map's predefined starting positions (Milestone 008: six to eight per map): where a faction's
    /// headquarters, refinery and first harvester are placed, plus the slots an opponent may start from (validated
    /// pairs, written by the map builder and re-checked by the map validator). Which slots are used is decided per
    /// match by <see cref="SkirmishInitializer"/> with <see cref="SpawnSelection"/>.
    /// </summary>
    public class StartSlot : MonoBehaviour
    {
        private static readonly List<StartSlot> s_All = new List<StartSlot>();
        public static IReadOnlyList<StartSlot> All => s_All;

        [SerializeField, Min(0)] private int slot;
        [SerializeField] private string displayName = "Start";
        [SerializeField] private Vector3 refinery;
        [SerializeField] private Vector3 harvester;
        [Tooltip("Harvester yaw in degrees.")]
        [SerializeField] private float harvesterYaw;
        [Tooltip("Slots an opponent may start from when this slot is used (validated pairs).")]
        [SerializeField] private List<int> validOpponents = new List<int>();

        public int Slot => slot;
        public string DisplayName => displayName;
        /// <summary>Headquarters position (the marker's own position).</summary>
        public Vector3 Headquarters => transform.position;
        public Vector3 Refinery => refinery;
        public Vector3 Harvester => harvester;
        public float HarvesterYaw => harvesterYaw;
        public IReadOnlyList<int> ValidOpponents => validOpponents;
        /// <summary>Buildings face the map centre: starts in the northern half build with their fronts to the south.</summary>
        public bool FacesSouth => transform.position.z > 0f;

        private void OnEnable() { if (!s_All.Contains(this)) s_All.Add(this); }
        private void OnDisable() => s_All.Remove(this);

        public static StartSlot Find(int slot) => Find(s_All, slot);

        public static StartSlot Find(IEnumerable<StartSlot> slots, int slot)
        {
            foreach (var s in slots)
                if (s != null && s.slot == slot) return s;
            return null;
        }

        /// <summary>The spawn table of the given slots (scene objects; works in Edit Mode on an unplayed scene).</summary>
        public static SpawnTable TableOf(IEnumerable<StartSlot> slots)
        {
            var t = new SpawnTable();
            foreach (var s in slots)
                if (s != null) t.Add(s.slot, s.validOpponents);
            return t;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => s_All.Clear();
    }
}
