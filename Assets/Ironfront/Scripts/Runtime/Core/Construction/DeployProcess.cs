using System;

namespace Ironfront.Core.Construction
{
    public enum DeployState { Mobile, Deploying, Converted }

    public enum DeployCheck
    {
        Valid,
        OutOfBounds,
        OverlapsStructure,
        BlocksAccess,
        OverlapsObstacle,
        OverlapsResource,
        UnitsInTheWay,
        UnevenGround,
        EnemyTerritory,
        Busy,
        Destroyed,
    }

    /// <summary>
    /// Mobile Command Vehicle deployment (Milestone 010): Mobile → Deploying → Converted, or back to Mobile when
    /// cancelled or when the site is no longer valid at the end. Conversion is reported exactly once. Pure;
    /// <see cref="Ironfront.Units.MobileCommandVehicle"/> drives it and performs the actual replacement.
    /// </summary>
    public sealed class DeployProcess
    {
        public float Duration { get; }
        public DeployState State { get; private set; } = DeployState.Mobile;
        public float Elapsed { get; private set; }
        public float Progress01 => Duration <= 0f ? 1f : Math.Min(1f, Elapsed / Duration);
        public DeployCheck LastFailure { get; private set; } = DeployCheck.Valid;

        public event Action<DeployState> StateChanged;

        public DeployProcess(float duration) => Duration = Math.Max(0f, duration);

        /// <summary>Starts deploying when the site check passed. Refused while deploying, after conversion, or when dead.</summary>
        public DeployCheck Begin(bool alive, DeployCheck siteCheck)
        {
            if (!alive) return DeployCheck.Destroyed;
            if (State != DeployState.Mobile) return DeployCheck.Busy;
            if (siteCheck != DeployCheck.Valid) { LastFailure = siteCheck; return siteCheck; }
            Elapsed = 0f;
            Set(DeployState.Deploying);
            return DeployCheck.Valid;
        }

        public bool Cancel()
        {
            if (State != DeployState.Deploying) return false;
            Elapsed = 0f;
            Set(DeployState.Mobile);
            return true;
        }

        /// <summary>
        /// Advances deployment. At the end the site is checked again (<paramref name="finalCheck"/>); if it passes,
        /// <paramref name="convert"/> runs once and must return whether the structure was created. A failed final check
        /// or conversion returns the vehicle to Mobile. Returns true only on the tick that converted.
        /// </summary>
        public bool Tick(float dt, bool alive, Func<DeployCheck> finalCheck, Func<bool> convert)
        {
            if (State != DeployState.Deploying) return false;
            if (!alive) { Cancel(); return false; }
            Elapsed += Math.Max(0f, dt);
            if (Elapsed < Duration) return false;

            var check = finalCheck != null ? finalCheck() : DeployCheck.Valid;
            if (check != DeployCheck.Valid)
            {
                LastFailure = check;
                Cancel();
                return false;
            }
            Set(DeployState.Converted); // first: no second conversion even if convert re-enters
            if (convert != null && convert()) return true;
            LastFailure = DeployCheck.Busy;
            Elapsed = 0f;
            Set(DeployState.Mobile);
            return false;
        }

        private void Set(DeployState s)
        {
            State = s;
            StateChanged?.Invoke(s);
        }

        public static string Describe(DeployCheck c)
        {
            switch (c)
            {
                case DeployCheck.Valid: return "Valid deployment site";
                case DeployCheck.OutOfBounds: return "Outside the map";
                case DeployCheck.OverlapsStructure: return "Too close to another structure";
                case DeployCheck.BlocksAccess: return "Would block a dock or factory exit";
                case DeployCheck.OverlapsObstacle: return "Blocked by terrain";
                case DeployCheck.OverlapsResource: return "Too close to an ore deposit";
                case DeployCheck.UnitsInTheWay: return "Units in the way";
                case DeployCheck.UnevenGround: return "Ground too uneven or blocked";
                case DeployCheck.EnemyTerritory: return "Inside enemy territory";
                case DeployCheck.Busy: return "Already deploying";
                case DeployCheck.Destroyed: return "Vehicle destroyed";
                default: return c.ToString();
            }
        }

        public static DeployCheck FromPlacement(PlacementResult r)
        {
            switch (r)
            {
                case PlacementResult.Valid: return DeployCheck.Valid;
                case PlacementResult.OutOfBounds: return DeployCheck.OutOfBounds;
                case PlacementResult.OverlapsStructure: return DeployCheck.OverlapsStructure;
                case PlacementResult.BlocksAccess: return DeployCheck.BlocksAccess;
                case PlacementResult.OverlapsObstacle: return DeployCheck.OverlapsObstacle;
                case PlacementResult.OverlapsResource: return DeployCheck.OverlapsResource;
                case PlacementResult.OverlapsUnit: return DeployCheck.UnitsInTheWay;
                case PlacementResult.EnemyTerritory: return DeployCheck.EnemyTerritory;
                default: return DeployCheck.UnevenGround;
            }
        }
    }
}
