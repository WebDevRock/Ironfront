#if UNITY_EDITOR
using System.Collections;
using System.IO;
using Ironfront.Diagnostics;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ironfront.Tests
{
    /// <summary>
    /// AI-vs-AI skirmish study in the Editor (Milestone 008, extended in 009–010). The matches themselves are played by
    /// <see cref="SkirmishStudy"/>, shared with the headless player runner (<see cref="SkirmishStudyRunner"/>), which is the
    /// faster way to run a study: <c>Tools/SkirmishStudy/run_study.ps1</c>. This Editor version remains for debugging
    /// with rendering. Writes Temp/skirmish_runs.jsonl, Temp/skirmish_trace.txt and Temp/skirmish_study.txt. Asserts only
    /// that every match reaches an outcome (no strategic deadlock). Explicit: about 1–4 minutes per match.
    /// Plan override: EditorPrefs "Ironfront.SkirmishRuns" = "map:seed:F1difficulty:F2difficulty,...".
    /// </summary>
    [Explicit("Long-running AI-vs-AI study; run on demand (prefer the headless runner, Tools/SkirmishStudy/run_study.ps1).")]
    public class SkirmishStudyPlayModeTests
    {
        [UnityTest, Timeout(14400000)]
        public IEnumerator AiVsAi_AllMaps_SeededStarts()
        {
            LogAssert.ignoreFailingMessages = true; // outcome study; unrelated editor noise must not abort it
            var plan = SkirmishStudy.ParsePlan(EditorPrefs.GetString("Ironfront.SkirmishRuns", ""));
            var results = new SkirmishStudy.Results();
            yield return SkirmishStudy.RunPlan(plan, Path.Combine(Application.dataPath, "..", "Temp"), results, "editor");
            Assert.AreEqual(plan.Count, results.Lines.Count);
            Assert.AreEqual(0, results.Timeouts, "every match reached an outcome");
        }
    }
}
#endif
