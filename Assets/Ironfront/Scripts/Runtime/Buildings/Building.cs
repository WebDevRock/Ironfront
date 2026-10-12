using System;
using System.Collections.Generic;
using Ironfront.Core.Construction;
using Ironfront.Data;
using Ironfront.Match;
using UnityEngine;

namespace Ironfront.Buildings
{
    /// <summary>
    /// A placed structure. Registers with its faction (structure count and power) while active and
    /// provides its footprint for placement validation.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(FactionMember))]
    public class Building : MonoBehaviour
    {
        private static readonly List<Building> s_All = new List<Building>();
        public static IReadOnlyList<Building> All => s_All;

        /// <summary>Raised when any building registers with or leaves its faction.</summary>
        public static event Action<Building> AnyRegistrationChanged;

        [SerializeField] private BuildingDefinition definition;
        [SerializeField] private GameObject selectionIndicator;

        private FactionMember _member;
        private bool _started;
        private int _registeredFaction = int.MinValue;

        public BuildingDefinition Definition => definition;
        public int FactionId => Member.FactionId;
        public bool IsSelected { get; private set; }
        /// <summary>Registered with its faction (counts for prerequisites, power and territory).</summary>
        public bool IsRegistered => _registeredFaction != int.MinValue;
        public Footprint Footprint => definition != null ? definition.FootprintAt(transform.position, FacesSouth) : default;

        /// <summary>Buildings face north (+Z) or, rotated 180°, south (−Z).</summary>
        public bool FacesSouth => transform.forward.z < 0f;

        /// <summary>Unit vector out of the building's front (+Z or −Z).</summary>
        public Vector3 FrontDirection => FacesSouth ? Vector3.back : Vector3.forward;

        /// <summary>Centre of the apron strip in front of the building, or the front edge if none.</summary>
        public Vector3 FrontPoint
        {
            get
            {
                if (definition == null) return transform.position;
                float offset = definition.FootprintSize.y * 0.5f + definition.ApronDepth * 0.5f;
                var p = transform.position + FrontDirection * offset;
                p.y = transform.position.y;
                return p;
            }
        }

        private FactionMember Member => _member != null ? _member : (_member = GetComponent<FactionMember>());

        /// <summary>In-place repairs (Milestone 009); every structure has one.</summary>
        public BuildingRepair Repair { get; private set; }
        /// <summary>Selling / dismantling (Milestone 010); every structure has one.</summary>
        public BuildingSale Sale { get; private set; }

        private void Awake()
        {
            Repair = GetComponent<BuildingRepair>();
            if (Repair == null) Repair = gameObject.AddComponent<BuildingRepair>();
            if (GetComponent<Visuals.BuildingRepairFx>() == null) gameObject.AddComponent<Visuals.BuildingRepairFx>();
            Sale = GetComponent<BuildingSale>();
            if (Sale == null) Sale = gameObject.AddComponent<BuildingSale>();
            if (GetComponent<Visuals.BuildingSaleFx>() == null) gameObject.AddComponent<Visuals.BuildingSaleFx>();
        }

        private void OnEnable()
        {
            if (!s_All.Contains(this)) s_All.Add(this);
            if (_started) Register();
            ApplyIndicator();
        }

        private void Start()
        {
            _started = true;
            Register();
        }

        private void OnDisable()
        {
            s_All.Remove(this);
            Unregister();
            TerritorySystem.OnDisabled(this); // Milestone 010: also when it never registered
        }

        public void SetSelected(bool selected)
        {
            if (IsSelected == selected) return;
            IsSelected = selected;
            ApplyIndicator();
        }

        private void ApplyIndicator()
        {
            if (selectionIndicator != null) selectionIndicator.SetActive(IsSelected);
        }

        private void Register()
        {
            if (_registeredFaction != int.MinValue || definition == null) return;
            var match = MatchController.Current;
            if (match == null) return;

            _registeredFaction = FactionId;
            match.GetFaction(_registeredFaction).RegisterStructure(definition.Id, definition.PowerOutput, definition.PowerDrain);
            AnyRegistrationChanged?.Invoke(this);
        }

        private void Unregister()
        {
            if (_registeredFaction == int.MinValue) return;
            var match = MatchController.Current;
            if (match != null)
                match.GetFaction(_registeredFaction).UnregisterStructure(definition.Id, definition.PowerOutput, definition.PowerDrain);
            _registeredFaction = int.MinValue;
            AnyRegistrationChanged?.Invoke(this);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_All.Clear();
            AnyRegistrationChanged = null;
        }
    }
}
