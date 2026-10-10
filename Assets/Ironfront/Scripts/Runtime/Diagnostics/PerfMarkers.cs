using Unity.Profiling;

namespace Ironfront.Diagnostics
{
    /// <summary>
    /// Profiler markers on gameplay hot paths. They show up in the Unity Profiler and are read by
    /// <see cref="BenchmarkRunner"/> through ProfilerRecorder (Editor and development builds; no-ops in release).
    /// </summary>
    public static class PerfMarkers
    {
        public const string CombatTickName = "Ironfront.Combat.Tick";
        public const string FindTargetName = "Ironfront.Combat.FindTarget";
        public const string LineOfSightName = "Ironfront.Combat.LineOfSight";
        public const string ProjectilesName = "Ironfront.Combat.Projectile";
        public const string FxSpawnName = "Ironfront.Fx.Spawn";
        public const string HealthBarsName = "Ironfront.UI.HealthBar";
        public const string HudName = "Ironfront.UI.Hud";
        public const string CommanderName = "Ironfront.AI.Decide";
        public const string PathRequestName = "Ironfront.Nav.PathRequest";

        public static readonly ProfilerMarker CombatTick = new ProfilerMarker(ProfilerCategory.Scripts, CombatTickName);
        public static readonly ProfilerMarker FindTarget = new ProfilerMarker(ProfilerCategory.Scripts, FindTargetName);
        public static readonly ProfilerMarker LineOfSight = new ProfilerMarker(ProfilerCategory.Scripts, LineOfSightName);
        public static readonly ProfilerMarker Projectiles = new ProfilerMarker(ProfilerCategory.Scripts, ProjectilesName);
        public static readonly ProfilerMarker FxSpawn = new ProfilerMarker(ProfilerCategory.Scripts, FxSpawnName);
        public static readonly ProfilerMarker HealthBars = new ProfilerMarker(ProfilerCategory.Scripts, HealthBarsName);
        public static readonly ProfilerMarker Hud = new ProfilerMarker(ProfilerCategory.Scripts, HudName);
        public static readonly ProfilerMarker Commander = new ProfilerMarker(ProfilerCategory.Scripts, CommanderName);
        public static readonly ProfilerMarker PathRequest = new ProfilerMarker(ProfilerCategory.Scripts, PathRequestName);

        public static readonly string[] All =
        {
            CombatTickName, FindTargetName, LineOfSightName, ProjectilesName, FxSpawnName, HealthBarsName, HudName,
            CommanderName, PathRequestName,
        };
    }
}
