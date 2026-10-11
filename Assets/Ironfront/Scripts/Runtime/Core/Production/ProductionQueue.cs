using System;
using System.Collections.Generic;
using Ironfront.Core.Economy;

namespace Ironfront.Core.Production
{
    /// <summary>Something a factory can manufacture.</summary>
    public interface IProducible
    {
        string Id { get; }
        string DisplayName { get; }
        int Cost { get; }
        /// <summary>Seconds of production at full power.</summary>
        float BuildTime { get; }
    }

    public enum EnqueueResult
    {
        Ok,
        QueueFull,
        InsufficientFunds,
        /// <summary>A required structure is missing (e.g. Research Centre for artillery).</summary>
        Locked,
    }

    /// <summary>
    /// First-in-first-out production queue. Items are paid in full when queued (so credits can never go
    /// negative mid-production) and refunded in full when cancelled. Only the head item makes progress.
    /// </summary>
    public sealed class ProductionQueue
    {
        private readonly List<IProducible> _items = new List<IProducible>();

        public int Capacity { get; }
        public IReadOnlyList<IProducible> Items => _items;
        public int Count => _items.Count;

        /// <summary>Seconds of progress on the head item.</summary>
        public float HeadElapsed { get; private set; }

        public float HeadProgress01 =>
            _items.Count == 0 ? 0f : _items[0].BuildTime <= 0f ? 1f : Math.Min(1f, HeadElapsed / _items[0].BuildTime);

        /// <summary>Raised when items are added, removed or completed.</summary>
        public event Action Changed;

        /// <summary>Raised when the head item finishes. The item has already been removed from the queue.</summary>
        public event Action<IProducible> Completed;

        public ProductionQueue(int capacity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            Capacity = capacity;
        }

        public EnqueueResult TryEnqueue(IProducible item, ResourceWallet wallet)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (_items.Count >= Capacity) return EnqueueResult.QueueFull;
            if (!wallet.TrySpend(item.Cost)) return EnqueueResult.InsufficientFunds;

            _items.Add(item);
            Changed?.Invoke();
            return EnqueueResult.Ok;
        }

        /// <summary>Removes the item at <paramref name="index"/> and refunds its full cost.</summary>
        public bool Cancel(int index, ResourceWallet wallet)
        {
            if (index < 0 || index >= _items.Count) return false;
            var item = _items[index];
            _items.RemoveAt(index);
            if (index == 0) HeadElapsed = 0f;
            wallet.Add(item.Cost);
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// Advances the head item by <paramref name="deltaTime"/> × <paramref name="speedMultiplier"/>.
        /// At most one item completes per tick; leftover time is not carried over.
        /// </summary>
        public void Tick(float deltaTime, float speedMultiplier = 1f)
        {
            if (_items.Count == 0 || deltaTime <= 0f || speedMultiplier <= 0f) return;

            HeadElapsed += deltaTime * speedMultiplier;
            var head = _items[0];
            if (HeadElapsed < head.BuildTime) return;

            _items.RemoveAt(0);
            HeadElapsed = 0f;
            Changed?.Invoke();
            Completed?.Invoke(head);
        }
    }
}
