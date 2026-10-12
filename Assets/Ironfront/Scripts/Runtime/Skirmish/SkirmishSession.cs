using Ironfront.Core.Skirmish;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ironfront.Skirmish
{
    /// <summary>
    /// Carries the chosen skirmish configuration from the setup menu into the map scene (Milestone 008). Holds plain
    /// settings only, never scene objects or match state: every match builds fresh managers from these settings.
    /// <list type="bullet">
    /// <item><see cref="Active"/>: what the next map scene load uses (also on Restart).</item>
    /// <item><see cref="LastChosen"/>: what the menu pre-selects next time.</item>
    /// </list>
    /// </summary>
    public static class SkirmishSession
    {
        private static SkirmishSettings s_active;
        private static SkirmishSettings s_lastChosen;
        private static int s_launches;

        /// <summary>Settings for the map scene being loaded or played (null: none chosen, scenes use their defaults).</summary>
        public static SkirmishSettings Active => s_active?.Clone();
        public static SkirmishSettings LastChosen => (s_lastChosen ?? SkirmishSettings.Default).Clone();
        public static int Launches => s_launches;

        /// <summary>Stores the settings (as a copy) and loads the map scene.</summary>
        public static void Launch(SkirmishSettings settings, string sceneName)
        {
            Select(settings);
            Time.timeScale = 1f;
            InputBindings.RtsInputMode.ResetAll();
            SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
        }

        /// <summary>Stores the settings without loading (tests and editor tools).</summary>
        public static void Select(SkirmishSettings settings)
        {
            s_active = (settings ?? SkirmishSettings.Default).Clone();
            s_lastChosen = s_active.Clone();
        }

        /// <summary>
        /// The settings a map scene should use: the active ones if they were chosen for this map, otherwise the defaults
        /// for this map (scene opened directly in the Editor or by a test).
        /// </summary>
        public static SkirmishSettings SettingsFor(string mapId)
        {
            if (s_active != null && s_active.MapId == mapId) return s_active.Clone();
            if (s_active != null)
                Debug.LogWarning($"[Ironfront] Skirmish settings were chosen for '{s_active.MapId}' but '{mapId}' was loaded; using defaults.");
            var d = SkirmishSettings.Default;
            d.MapId = mapId;
            return d;
        }

        /// <summary>A new seed for every match start (Random start location), deterministic per session.</summary>
        public static int NextSeed() => unchecked(++s_launches * 7919 + System.Environment.TickCount);

        /// <summary>Forgets the active settings (back to the scene defaults); the menu keeps its last choice.</summary>
        public static void ClearActive() => s_active = null;

        /// <summary>Forgets everything (tests).</summary>
        public static void Reset()
        {
            s_active = null;
            s_lastChosen = null;
            s_launches = 0;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Reset();
    }
}
