using Unity.Profiling;

namespace Ironfront.Diagnostics
{
    /// <summary>
    /// Development helper: averages "GC Allocated In Frame" over the last frames (Editor / development builds),
    /// for attributing allocations by toggling systems on and off.
    /// </summary>
    public static class AllocProbe
    {
        private static ProfilerRecorder s_recorder;

        public static void Begin(int frames = 300)
        {
            s_recorder.Dispose();
            s_recorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", frames);
        }

        /// <summary>Average bytes allocated per frame over the recorded samples, or -1 when unavailable.</summary>
        public static double AverageBytesPerFrame()
        {
            if (!s_recorder.Valid || s_recorder.Count == 0) return -1;
            double sum = 0;
            int n = s_recorder.Count;
            for (int i = 0; i < n; i++) sum += s_recorder.GetSample(i).Value;
            return sum / n;
        }

        public static void End() => s_recorder.Dispose();
    }
}
