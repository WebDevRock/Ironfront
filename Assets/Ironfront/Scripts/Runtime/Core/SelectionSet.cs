using System;
using System.Collections.Generic;

namespace Ironfront.Core
{
    /// <summary>
    /// Ordered set of selected items. Pure C# so it can be unit tested without a scene.
    /// Raises <see cref="Changed"/> once per mutating call that actually changed membership.
    /// </summary>
    public sealed class SelectionSet<T> where T : class
    {
        private readonly List<T> _items = new List<T>();

        public event Action Changed;

        public int Count => _items.Count;
        public IReadOnlyList<T> Items => _items;

        public bool Contains(T item) => item != null && _items.Contains(item);

        public bool Add(T item)
        {
            if (item == null || _items.Contains(item)) return false;
            _items.Add(item);
            Changed?.Invoke();
            return true;
        }

        public bool Remove(T item)
        {
            if (item == null || !_items.Remove(item)) return false;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Adds the item if absent, removes it if present. Returns the new selected state.</summary>
        public bool Toggle(T item)
        {
            if (item == null) return false;
            if (_items.Remove(item))
            {
                Changed?.Invoke();
                return false;
            }
            _items.Add(item);
            Changed?.Invoke();
            return true;
        }

        public void Clear()
        {
            if (_items.Count == 0) return;
            _items.Clear();
            Changed?.Invoke();
        }

        /// <summary>Adds every non-null item not already selected.</summary>
        public void AddRange(IEnumerable<T> items)
        {
            bool changed = false;
            foreach (var item in items)
            {
                if (item == null || _items.Contains(item)) continue;
                _items.Add(item);
                changed = true;
            }
            if (changed) Changed?.Invoke();
        }

        /// <summary>Replaces the whole selection with the given items.</summary>
        public void Replace(IEnumerable<T> items)
        {
            var next = new List<T>();
            foreach (var item in items)
            {
                if (item != null && !next.Contains(item)) next.Add(item);
            }

            bool changed = next.Count != _items.Count;
            if (!changed)
            {
                for (int i = 0; i < next.Count; i++)
                {
                    if (!_items.Contains(next[i])) { changed = true; break; }
                }
            }
            if (!changed) return;

            _items.Clear();
            _items.AddRange(next);
            Changed?.Invoke();
        }

        /// <summary>Removes items matching the predicate (e.g. destroyed units).</summary>
        public void RemoveWhere(Predicate<T> predicate)
        {
            if (_items.RemoveAll(predicate) > 0) Changed?.Invoke();
        }
    }
}
