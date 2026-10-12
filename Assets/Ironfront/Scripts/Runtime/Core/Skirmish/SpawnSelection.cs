using System;
using System.Collections.Generic;

namespace Ironfront.Core.Skirmish
{
    /// <summary>A map's start slots and, for each, the slots an opponent may start from (validated pairs).</summary>
    public sealed class SpawnTable
    {
        private readonly SortedDictionary<int, List<int>> _opponents = new SortedDictionary<int, List<int>>();

        public IEnumerable<int> Slots => _opponents.Keys;
        public int Count => _opponents.Count;

        public void Add(int slot, IEnumerable<int> validOpponents)
        {
            var list = new List<int>();
            if (validOpponents != null)
                foreach (int o in validOpponents)
                    if (o != slot && !list.Contains(o)) list.Add(o);
            list.Sort();
            _opponents[slot] = list;
        }

        public bool Contains(int slot) => _opponents.ContainsKey(slot);

        public IReadOnlyList<int> OpponentsOf(int slot) => _opponents.TryGetValue(slot, out var l) ? l : (IReadOnlyList<int>)Array.Empty<int>();

        /// <summary>Every ordered (player, opponent) pair allowed by the table, in a stable order.</summary>
        public List<(int player, int enemy)> OrderedPairs()
        {
            var pairs = new List<(int, int)>();
            foreach (var kv in _opponents)
                foreach (int o in kv.Value)
                    if (_opponents.ContainsKey(o)) pairs.Add((kv.Key, o));
            return pairs;
        }
    }

    /// <summary>Thresholds a pair of start slots must meet to be offered (Milestone 008 addendum).</summary>
    public sealed class SpawnPairRules
    {
        /// <summary>Minimum ground (path) distance between the two headquarters.</summary>
        public float MinSeparation = 200f;
        /// <summary>Maximum difference of the two slots' economic opportunity scores (fraction of the larger).</summary>
        public float MaxOpportunityImbalance = 0.12f;
    }

    /// <summary>
    /// Chooses the two starting positions for a match. Pure and deterministic: the same table, request and seed
    /// always give the same pair. Random starts draw uniformly over every valid ordered pair, so no slot is favoured;
    /// a requested player slot keeps that slot and draws the opponent among its valid partners.
    /// </summary>
    public static class SpawnSelection
    {
        public static bool Choose(SpawnTable table, int requestedPlayerSlot, int seed, out int player, out int enemy)
        {
            player = enemy = -1;
            if (table == null || table.Count == 0) return false;
            var rng = new Random(seed);
            if (requestedPlayerSlot >= 0 && table.Contains(requestedPlayerSlot))
            {
                var partners = new List<int>();
                foreach (int o in table.OpponentsOf(requestedPlayerSlot))
                    if (table.Contains(o)) partners.Add(o);
                if (partners.Count > 0)
                {
                    player = requestedPlayerSlot;
                    enemy = partners[rng.Next(partners.Count)];
                    return true;
                }
            }
            var pairs = table.OrderedPairs();
            if (pairs.Count == 0) return false;
            (player, enemy) = pairs[rng.Next(pairs.Count)];
            return true;
        }

        /// <summary>
        /// Which slot pairs are valid: both slots individually usable, ground-connected, at least
        /// <see cref="SpawnPairRules.MinSeparation"/> apart and economically equivalent.
        /// </summary>
        /// <param name="usable">Per slot: base placement, expansion room and home field checks passed.</param>
        /// <param name="pathDistance">Ground distance between two slots (+∞ if unreachable).</param>
        /// <param name="opportunity">Per slot economic opportunity score.</param>
        public static SpawnTable ValidPairs(IReadOnlyList<int> slots, Func<int, bool> usable, Func<int, int, float> pathDistance,
            Func<int, float> opportunity, SpawnPairRules rules)
        {
            var table = new SpawnTable();
            foreach (int a in slots)
            {
                var partners = new List<int>();
                if (usable(a))
                    foreach (int b in slots)
                        if (b != a && usable(b) && IsValidPair(pathDistance(a, b), opportunity(a), opportunity(b), rules)) partners.Add(b);
                table.Add(a, partners);
            }
            return table;
        }

        public static bool IsValidPair(float pathDistance, float opportunityA, float opportunityB, SpawnPairRules rules)
        {
            if (float.IsInfinity(pathDistance) || float.IsNaN(pathDistance) || pathDistance < rules.MinSeparation) return false;
            return Imbalance(opportunityA, opportunityB) <= rules.MaxOpportunityImbalance;
        }

        public static float Imbalance(float a, float b)
        {
            float max = Math.Max(a, b);
            return max <= 0f ? 0f : Math.Abs(a - b) / max;
        }
    }
}
