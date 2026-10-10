using System;
using System.Collections.Generic;

namespace Ironfront.Core
{
    /// <summary>
    /// Numbered unit groups (keys 1–9 and 0). Pure: destroyed or invalid members are pruned through the
    /// supplied predicate when a group is read.
    /// </summary>
    public sealed class ControlGroups<T> where T : class
    {
        public const int GroupCount = 10;

        private readonly List<T>[] _groups = new List<T>[GroupCount];

        public ControlGroups()
        {
            for (int i = 0; i < GroupCount; i++) _groups[i] = new List<T>();
        }

        /// <summary>Replaces group <paramref name="group"/> with the given items (duplicates and nulls ignored).</summary>
        public void Assign(int group, IEnumerable<T> items)
        {
            var g = Group(group);
            g.Clear();
            AddTo(group, items);
        }

        public void AddTo(int group, IEnumerable<T> items)
        {
            var g = Group(group);
            foreach (var item in items)
                if (item != null && !g.Contains(item)) g.Add(item);
        }

        /// <summary>Living members of the group (invalid members are removed permanently).</summary>
        public IReadOnlyList<T> Get(int group, Func<T, bool> isValid)
        {
            var g = Group(group);
            if (isValid != null) g.RemoveAll(item => !isValid(item));
            return g;
        }

        public int Count(int group) => Group(group).Count;

        private List<T> Group(int group)
        {
            if (group < 0 || group >= GroupCount) throw new ArgumentOutOfRangeException(nameof(group));
            return _groups[group];
        }
    }

    /// <summary>Detects a second press of the same key within a time window (double-tap / double-click).</summary>
    public sealed class DoubleTapDetector
    {
        private readonly float _window;
        private int _lastKey = int.MinValue;
        private float _lastTime = float.NegativeInfinity;

        public DoubleTapDetector(float windowSeconds)
        {
            _window = windowSeconds;
        }

        /// <summary>Registers a press; returns true if it completes a double-tap of the same key.</summary>
        public bool Press(int key, float time)
        {
            bool isDouble = key == _lastKey && time - _lastTime <= _window;
            _lastKey = isDouble ? int.MinValue : key; // a third press starts a new pair
            _lastTime = isDouble ? float.NegativeInfinity : time;
            return isDouble;
        }
    }
}
