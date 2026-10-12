using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Ironfront.Diagnostics
{
    /// <summary>
    /// Headless AI-vs-AI study for a built player (no Editor, no rendering). Usage:
    /// <code>Ironfront.exe -batchmode -nographics -ironfrontSkirmishStudy [-plan "map:seed:F1:F2,..."] [-out &lt;dir&gt;]
    ///     [-studyStep &lt;seconds&gt;] -logFile &lt;file&gt;</code>
    /// Plays the plan through <see cref="SkirmishStudy.RunPlan"/> — the same code, matches, setup, seeding, commanders,
    /// 10× time scale and 2700 s limit as the Editor PlayMode study — and writes the same files to <c>-out</c> (default:
    /// <c>StudyOut</c> next to the executable), then <c>skirmish_done.txt</c>, and quits: exit code 0 when every match
    /// was played (timeouts are results, not errors), 1 on an error.
    /// <para><b>Why it is faster with no loss of quality:</b> game time normally advances by wall-clock frame time ×
    /// time scale, so without rendering it would still run at 10× real time. Here every frame advances by a fixed
    /// <c>-studyStep</c> (default 1/60 s, i.e. one 60 fps frame) × 10 via <see cref="Time.captureDeltaTime"/>, and frames
    /// run back to back as fast as the simulation allows. Each frame is a 1/6 s game step, the same as the Editor study at
    /// 60 fps and finer than its slower frames (the Editor's variable frame time can reach 0.33 s × 10).</para>
    /// </summary>
    public class SkirmishStudyRunner : MonoBehaviour
    {
        public const string CommandLineSwitch = "-ironfrontSkirmishStudy";
        public const string DoneFile = "skirmish_done.txt";
        public const float DefaultStep = 1f / 60f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void MaybeStart()
        {
            if (Application.isEditor || !Environment.GetCommandLineArgs().Contains(CommandLineSwitch)) return;
            if (FindAnyObjectByType<SkirmishStudyRunner>() != null) return;
            var go = new GameObject("SkirmishStudy");
            DontDestroyOnLoad(go);
            go.AddComponent<SkirmishStudyRunner>();
        }

        private static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        private void Start() => StartCoroutine(Run());

        private IEnumerator Run()
        {
            string outDir = Arg("-out") ?? Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "StudyOut");
            string done = Path.Combine(outDir, DoneFile);
            SkirmishStudy.Results results = null;
            IEnumerator study = null;
            try
            {
                Directory.CreateDirectory(outDir);
                if (File.Exists(done)) File.Delete(done);
                var plan = SkirmishStudy.ParsePlan(Arg("-plan"));
                float step = float.TryParse(Arg("-studyStep"), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float s) && s > 0f ? s : DefaultStep;
                Application.runInBackground = true;
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = -1;
                Time.captureDeltaTime = step; // fixed game step per frame (× the study's time scale), frames as fast as possible
                Debug.Log($"[Skirmish] headless study: {plan.Count} matches, step {step:0.#####} s x {SkirmishStudy.TimeScale:0}, out {outDir}");
                results = new SkirmishStudy.Results();
                study = SkirmishStudy.RunPlan(plan, outDir, results, "headless");
            }
            catch (Exception e)
            {
                Fail(done, e);
                yield break;
            }

            // Drive the study by hand so an exception inside a match ends the run with an error instead of hanging.
            var stack = new System.Collections.Generic.Stack<IEnumerator>();
            stack.Push(study);
            float realStart = Time.realtimeSinceStartup;
            while (stack.Count > 0)
            {
                object current;
                try
                {
                    var top = stack.Peek();
                    if (!top.MoveNext()) { stack.Pop(); continue; }
                    current = top.Current;
                }
                catch (Exception e)
                {
                    Fail(done, e);
                    yield break;
                }
                if (current is IEnumerator nested) { stack.Push(nested); continue; }
                yield return current;
            }

            File.WriteAllText(done, $"ok {results.Lines.Count}/{results.Planned} matches, {results.Timeouts} timeouts, " +
                                    $"{Time.realtimeSinceStartup - realStart:0} s real\n");
            Debug.Log($"[Skirmish] headless study finished in {Time.realtimeSinceStartup - realStart:0} s");
            Application.Quit(results.Lines.Count == results.Planned ? 0 : 1);
        }

        private static void Fail(string done, Exception e)
        {
            Debug.LogError("[Skirmish] headless study failed: " + e);
            try { File.WriteAllText(done, "error " + e.Message + "\n"); } catch { /* best effort */ }
            Application.Quit(1);
        }
    }
}
