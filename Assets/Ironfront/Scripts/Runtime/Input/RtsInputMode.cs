using UnityEngine;

namespace Ironfront.InputBindings
{
    /// <summary>
    /// Shared pointer arbitration between world-input systems (selection, commands, building placement).
    /// While placing a building, or on a frame where another system consumed the pointer, selection and
    /// unit commands ignore the mouse.
    /// </summary>
    public static class RtsInputMode
    {
        private static int s_consumedFrame = -1;

        public static bool IsPlacingBuilding { get; set; }

        /// <summary>Marks the current frame's pointer press as handled.</summary>
        public static void ConsumePointerThisFrame() => s_consumedFrame = Time.frameCount;

        public static bool WorldPointerBlocked => IsPlacingBuilding || s_consumedFrame == Time.frameCount;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_consumedFrame = -1;
            IsPlacingBuilding = false;
        }
    }
}
