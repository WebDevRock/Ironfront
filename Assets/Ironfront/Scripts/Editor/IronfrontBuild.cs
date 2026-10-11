using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Ironfront.EditorTools
{
    /// <summary>Player build helpers. Output goes to Builds/ (outside Assets, git-ignored).</summary>
    public static class IronfrontBuild
    {
        public const string WindowsOutput = "Builds/Windows/Ironfront.exe";

        [MenuItem("Ironfront/Build Windows Player")]
        public static string BuildWindows() => BuildWindows(WindowsOutput);

        /// <summary>Release build to <paramref name="output"/> (e.g. Builds/Before/Ironfront.exe for comparisons).</summary>
        public static string BuildWindows(string output)
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            });
            var s = report.summary;
            string result = $"{s.result}: {s.totalErrors} errors, {s.totalWarnings} warnings, {s.totalSize / (1024 * 1024)} MB -> {s.outputPath}";
            if (s.result == BuildResult.Succeeded) Debug.Log("[Ironfront] Build " + result);
            else Debug.LogError("[Ironfront] Build " + result);
            return result;
        }
    }
}
