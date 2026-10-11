using System.Collections.Generic;

namespace Ironfront.Core.Audio
{
    /// <summary>
    /// Prevents sound pile-ups: each sound id may have at most N voices playing at once and must leave a minimum
    /// interval between starts. When the voice cap is reached, a new start is refused (the oldest voice keeps
    /// playing; in a big battle a few clear shots read better than a wall of noise). Pure logic, time supplied by
    /// the caller.
    /// </summary>
    public sealed class VoiceLimiter
    {
        private readonly Dictionary<int, List<float>> _endTimes = new Dictionary<int, List<float>>();
        private readonly Dictionary<int, float> _lastStart = new Dictionary<int, float>();

        /// <summary>Voices currently counted for <paramref name="id"/> at time <paramref name="now"/>.</summary>
        public int Active(int id, float now)
        {
            if (!_endTimes.TryGetValue(id, out var list)) return 0;
            list.RemoveAll(t => t <= now);
            return list.Count;
        }

        /// <summary>
        /// Returns true (and records the voice) if a sound of <paramref name="id"/> lasting <paramref name="duration"/>
        /// seconds may start at <paramref name="now"/>.
        /// </summary>
        public bool TryStart(int id, float now, float duration, int maxVoices, float minInterval)
        {
            if (maxVoices <= 0) return false;
            if (_lastStart.TryGetValue(id, out float last) && now - last < minInterval) return false;
            if (Active(id, now) >= maxVoices) return false;
            if (!_endTimes.TryGetValue(id, out var list)) _endTimes[id] = list = new List<float>(maxVoices);
            list.Add(now + duration);
            _lastStart[id] = now;
            return true;
        }

        public void Clear()
        {
            _endTimes.Clear();
            _lastStart.Clear();
        }
    }
}
