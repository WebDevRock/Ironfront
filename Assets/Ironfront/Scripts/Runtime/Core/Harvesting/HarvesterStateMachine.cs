using System;

namespace Ironfront.Core.Harvesting
{
    /// <summary>A finite supply of Ore.</summary>
    public interface IOreSource
    {
        bool IsDepleted { get; }
        int Remaining { get; }
        /// <summary>Removes up to <paramref name="max"/> Ore and returns how much was actually taken.</summary>
        int Extract(int max);
    }

    /// <summary>Accepts Ore deliveries (a refinery).</summary>
    public interface IOreSink
    {
        void Deliver(int ore);
    }

    /// <summary>The harvester's physical side: movement, arrival checks and world queries.</summary>
    public interface IHarvesterBody
    {
        IOreSource FindNearestSource();
        IOreSink FindNearestSink();
        void MoveTo(IOreSource source);
        void MoveTo(IOreSink sink);
        bool IsAt(IOreSource source);
        bool IsAt(IOreSink sink);
    }

    public enum HarvesterState
    {
        /// <summary>Not harvesting (new, or interrupted by a player order).</summary>
        Idle,
        MovingToSource,
        Harvesting,
        MovingToSink,
        Unloading,
        /// <summary>Every known deposit is depleted. Retries periodically.</summary>
        NoSourceAvailable,
        /// <summary>No refinery exists to deliver to. Retries periodically.</summary>
        NoSinkAvailable,
    }

    /// <summary>
    /// Harvest loop: go to a deposit, fill up, return to the nearest refinery, unload, repeat.
    /// When a deposit runs out the harvester delivers any partial load, then moves to the next nearest
    /// deposit, or waits in <see cref="HarvesterState.NoSourceAvailable"/> if none remain.
    /// </summary>
    public sealed class HarvesterStateMachine
    {
        public const float RetryInterval = 1f;

        private readonly int _capacity;
        private readonly float _harvestRate;
        private readonly float _unloadRate;
        private float _accumulator;
        private float _retryTimer;

        public HarvesterState State { get; private set; } = HarvesterState.Idle;
        public int Cargo { get; private set; }
        public int Capacity => _capacity;
        /// <summary>Speeds up mining and unloading (research upgrades); 1 = definition rates.</summary>
        public float RateMultiplier { get; set; } = 1f;
        public IOreSource Source { get; private set; }
        public IOreSink Sink { get; private set; }

        public event Action<HarvesterState> StateChanged;

        /// <summary>Total Ore delivered by this harvester.</summary>
        public int TotalDelivered { get; private set; }

        public HarvesterStateMachine(int capacity, float harvestRatePerSecond, float unloadRatePerSecond)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            if (harvestRatePerSecond <= 0f) throw new ArgumentOutOfRangeException(nameof(harvestRatePerSecond));
            if (unloadRatePerSecond <= 0f) throw new ArgumentOutOfRangeException(nameof(unloadRatePerSecond));
            _capacity = capacity;
            _harvestRate = harvestRatePerSecond;
            _unloadRate = unloadRatePerSecond;
        }

        /// <summary>Starts (or restarts) the harvest loop, optionally at a specific deposit.</summary>
        public void StartHarvesting(IHarvesterBody body, IOreSource preferred = null)
        {
            if (preferred != null && !preferred.IsDepleted) Source = preferred;
            if (Cargo >= _capacity) GoToSink(body);
            else PickSource(body);
        }

        /// <summary>Stops automation (e.g. the player gave a move order). Cargo is kept.</summary>
        public void Stop()
        {
            SetState(HarvesterState.Idle);
        }

        public void Tick(float deltaTime, IHarvesterBody body)
        {
            switch (State)
            {
                case HarvesterState.Idle:
                    break;

                case HarvesterState.MovingToSource:
                    if (Source == null || Source.IsDepleted) OnSourceLost(body);
                    else if (body.IsAt(Source)) SetState(HarvesterState.Harvesting);
                    break;

                case HarvesterState.Harvesting:
                    TickHarvest(deltaTime, body);
                    break;

                case HarvesterState.MovingToSink:
                    if (Sink == null) GoToSink(body);
                    else if (body.IsAt(Sink)) SetState(HarvesterState.Unloading);
                    break;

                case HarvesterState.Unloading:
                    TickUnload(deltaTime, body);
                    break;

                case HarvesterState.NoSourceAvailable:
                case HarvesterState.NoSinkAvailable:
                    _retryTimer -= deltaTime;
                    if (_retryTimer <= 0f)
                    {
                        if (State == HarvesterState.NoSinkAvailable) GoToSink(body);
                        else PickSource(body);
                    }
                    break;
            }
        }

        private void TickHarvest(float deltaTime, IHarvesterBody body)
        {
            if (Source == null || Source.IsDepleted)
            {
                OnSourceLost(body);
                return;
            }

            _accumulator += _harvestRate * RateMultiplier * deltaTime;
            int whole = (int)_accumulator;
            if (whole > 0)
            {
                _accumulator -= whole;
                int taken = Source.Extract(Math.Min(whole, _capacity - Cargo));
                Cargo += taken;
            }

            if (Cargo >= _capacity) GoToSink(body);
            else if (Source.IsDepleted) OnSourceLost(body);
        }

        private void TickUnload(float deltaTime, IHarvesterBody body)
        {
            if (Sink == null)
            {
                GoToSink(body);
                return;
            }

            _accumulator += _unloadRate * RateMultiplier * deltaTime;
            int whole = Math.Min((int)_accumulator, Cargo);
            if (whole > 0)
            {
                _accumulator -= whole;
                Cargo -= whole;
                TotalDelivered += whole;
                Sink.Deliver(whole);
            }

            if (Cargo == 0) PickSource(body);
        }

        private void OnSourceLost(IHarvesterBody body)
        {
            // Deliver what we have before looking elsewhere.
            if (Cargo > 0) GoToSink(body);
            else PickSource(body);
        }

        private void PickSource(IHarvesterBody body)
        {
            if (Source == null || Source.IsDepleted) Source = body.FindNearestSource();

            if (Source == null)
            {
                if (Cargo > 0)
                {
                    GoToSink(body);
                    return;
                }
                _retryTimer = RetryInterval;
                SetState(HarvesterState.NoSourceAvailable);
                return;
            }

            body.MoveTo(Source);
            SetState(HarvesterState.MovingToSource);
        }

        /// <summary>
        /// The current refinery was destroyed (the runtime detects this; a destroyed engine object does not
        /// compare to null here). Forgets it and, if heading to or unloading at it, looks for another.
        /// </summary>
        public void SinkLost(IHarvesterBody body)
        {
            if (Sink == null) return;
            Sink = null;
            if (State == HarvesterState.MovingToSink || State == HarvesterState.Unloading) GoToSink(body);
        }

        private void GoToSink(IHarvesterBody body)
        {
            Sink = body.FindNearestSink();
            if (Sink == null)
            {
                _retryTimer = RetryInterval;
                SetState(HarvesterState.NoSinkAvailable);
                return;
            }

            body.MoveTo(Sink);
            SetState(HarvesterState.MovingToSink);
        }

        private void SetState(HarvesterState next)
        {
            _accumulator = 0f;
            if (State == next) return;
            State = next;
            StateChanged?.Invoke(next);
        }
    }
}
