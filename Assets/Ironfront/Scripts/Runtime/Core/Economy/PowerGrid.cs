using System;

namespace Ironfront.Core.Economy
{
    /// <summary>
    /// Tracks a faction's power production and consumption. Low power (drain above output) slows production.
    /// </summary>
    public sealed class PowerGrid
    {
        public int Produced { get; private set; }
        public int Consumed { get; private set; }
        public bool IsLowPower => Consumed > Produced;

        public event Action Changed;

        public void Add(int output, int drain)
        {
            if (output < 0 || drain < 0) throw new ArgumentOutOfRangeException();
            if (output == 0 && drain == 0) return;
            Produced += output;
            Consumed += drain;
            Changed?.Invoke();
        }

        public void Remove(int output, int drain)
        {
            if (output < 0 || drain < 0) throw new ArgumentOutOfRangeException();
            if (output == 0 && drain == 0) return;
            Produced = Math.Max(0, Produced - output);
            Consumed = Math.Max(0, Consumed - drain);
            Changed?.Invoke();
        }
    }
}
