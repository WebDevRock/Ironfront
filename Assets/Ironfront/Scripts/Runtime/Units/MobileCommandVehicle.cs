using System;
using System.Collections.Generic;
using Ironfront.Buildings;
using Ironfront.Combat;
using Ironfront.Construction;
using Ironfront.Core.Construction;
using Ironfront.Data;
using Ironfront.Match;
using Ironfront.Selection;
using UnityEngine;

namespace Ironfront.Units
{
    /// <summary>
    /// Mobile Command Vehicle (Milestone 010): an unarmed, slow, heavily armoured vehicle that deploys where it stands
    /// into a Forward Command Centre (<see cref="deploysInto"/>), which grants new construction territory.
    /// <para>Deploy validates the centre's footprint at the vehicle's position (terrain, ground level, structures, ore,
    /// other units, known enemy exclusion zones — own territory is not required). While deploying the vehicle cannot
    /// move (its <see cref="UnitMover"/> is locked), stays a normal target and can cancel. At the end the site is checked
    /// again and the vehicle is replaced by the centre exactly once through
    /// <see cref="BuildingPlacementController.PlaceDeployed"/>; the centre keeps the vehicle's health fraction. A vehicle
    /// destroyed while deploying leaves nothing behind. Centres cannot be packed up again.</para>
    /// </summary>
    [RequireComponent(typeof(UnitMover))]
    [DisallowMultipleComponent]
    public class MobileCommandVehicle : MonoBehaviour
    {
        private static readonly List<MobileCommandVehicle> s_All = new List<MobileCommandVehicle>();
        public static IReadOnlyList<MobileCommandVehicle> All => s_All;

        /// <summary>A vehicle started deploying, finished (with the new centre), or failed (with the reason).</summary>
        public static event Action<MobileCommandVehicle> AnyDeployStarted;
        public static event Action<MobileCommandVehicle, Building> AnyDeployed;
        public static event Action<MobileCommandVehicle, DeployCheck> AnyDeployFailed;

        [Tooltip("The structure this vehicle deploys into (Forward Command Centre).")]
        [SerializeField] private BuildingDefinition deploysInto;

        private DeployProcess _process;
        private UnitMover _mover;
        private Health _health;
        private Vector3 _site;
        private BuildingPlacementController _placement;

        public BuildingDefinition DeploysInto => deploysInto;
        public DeployState State => _process != null ? _process.State : DeployState.Mobile;
        public bool IsDeploying => State == DeployState.Deploying;
        public float Progress01 => _process != null ? _process.Progress01 : 0f;
        public float DeploySeconds => _process != null ? _process.Duration : TerritorySettings.Current.DeploySeconds;
        public DeployCheck LastFailure => _process != null ? _process.LastFailure : DeployCheck.Valid;
        public int FactionId => FactionMember.Of(this);
        public Health Health => _health;
        /// <summary>The centre this vehicle became (null until converted).</summary>
        public Building Result { get; private set; }
        /// <summary>Where the centre will stand: the vehicle's position on the 1 m construction grid.</summary>
        public Vector3 SiteCentre => IsDeploying ? _site : Snap(transform.position);

        private void Awake()
        {
            _mover = GetComponent<UnitMover>();
            _health = GetComponent<Health>();
            _process = new DeployProcess(TerritorySettings.Current.DeploySeconds);
        }

        private void OnEnable() { if (!s_All.Contains(this)) s_All.Add(this); }
        private void OnDisable() => s_All.Remove(this);

        private BuildingPlacementController Placement => _placement != null ? _placement : (_placement = FindAnyObjectByType<BuildingPlacementController>());

        private static Vector3 Snap(Vector3 p)
        {
            var s = PlacementRules.Snap(new Vector2(p.x, p.z), 1f);
            return new Vector3(s.x, 0f, s.y);
        }

        /// <summary>Checks the deployment site at the vehicle's current position (for the HUD preview and the AI).</summary>
        public DeployCheck CheckSite() => CheckSite(SiteCentre, includeUnits: true);

        public DeployCheck CheckSite(Vector3 centre, bool includeUnits)
        {
            if (_health != null && !_health.IsAlive) return DeployCheck.Destroyed;
            if (deploysInto == null || Placement == null) return DeployCheck.Busy;
            return Placement.ValidateDeployment(deploysInto, centre, FactionId, gameObject, includeUnits);
        }

        /// <summary>Starts deploying here. Returns Valid when started, otherwise why not.</summary>
        public DeployCheck Deploy()
        {
            bool alive = _health == null || _health.IsAlive;
            var flow = MatchFlowController.Current;
            if (flow != null && flow.IsOver) return DeployCheck.Busy;
            var site = Snap(transform.position);
            var check = State == DeployState.Mobile && alive ? CheckSite(site, includeUnits: true) : DeployCheck.Valid;
            var result = _process.Begin(alive, check);
            if (result != DeployCheck.Valid)
            {
                AnyDeployFailed?.Invoke(this, result);
                return result;
            }
            _site = site;
            RepairClient.NotifyExternalOrder(this); // a repair order would fight the lock
            _mover.SetLocked(true);
            AnyDeployStarted?.Invoke(this);
            return DeployCheck.Valid;
        }

        public bool CancelDeploy()
        {
            if (!_process.Cancel()) return false;
            _mover.SetLocked(false);
            return true;
        }

        private void Update()
        {
            if (!IsDeploying) return;
            bool alive = _health == null || _health.IsAlive;
            bool converted = _process.Tick(Time.deltaTime, alive, () => CheckSite(_site, includeUnits: false), Convert);
            if (converted || IsDeploying) return;
            // Back to mobile: the site became invalid at the end (or the vehicle died).
            _mover.SetLocked(false);
            if (alive) AnyDeployFailed?.Invoke(this, _process.LastFailure);
        }

        private bool Convert()
        {
            if (Placement == null || !Placement.PlaceDeployed(deploysInto, _site, FactionId, gameObject, out var centre, out _)) return false;
            var hp = centre.GetComponent<Health>();
            if (hp != null && _health != null)
                hp.SetInitialFraction(Mathf.Max(TerritorySettings.Current.MinConvertedHealth, _health.Current / Mathf.Max(1f, _health.Max)));
            Result = centre;

            var selection = FindAnyObjectByType<SelectionManager>();
            var selectable = GetComponent<SelectableUnit>();
            if (selection != null && selectable != null && selection.Selection.Contains(selectable))
            {
                selection.Selection.Remove(selectable);
                if (selection.Selection.Count == 0) selection.SelectStructure(centre);
            }

            AnyDeployed?.Invoke(this, centre);
            Destroy(gameObject); // replaced, not killed: no death event, no wreck
            return true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_All.Clear();
            AnyDeployStarted = null;
            AnyDeployed = null;
            AnyDeployFailed = null;
        }
    }
}
