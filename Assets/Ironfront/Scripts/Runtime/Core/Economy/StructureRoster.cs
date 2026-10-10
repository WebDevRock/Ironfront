using System;
using System.Collections.Generic;

namespace Ironfront.Core.Economy
{
    /// <summary>Counts a faction's completed structures by definition id (used for prerequisites).</summary>
    public sealed class StructureRoster
    {
        private readonly Dictionary<string, int> _counts = new Dictionary<string, int>();

        public event Action Changed;

        public int Count(string id) => id != null && _counts.TryGetValue(id, out var n) ? n : 0;
        public bool Has(string id) => Count(id) > 0;

        public void Add(string id)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("Structure id required.", nameof(id));
            _counts[id] = Count(id) + 1;
            Changed?.Invoke();
        }

        public void Remove(string id)
        {
            int n = Count(id);
            if (n == 0) return;
            if (n == 1) _counts.Remove(id); else _counts[id] = n - 1;
            Changed?.Invoke();
        }
    }
}
