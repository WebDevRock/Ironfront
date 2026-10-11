using System.Collections.Generic;
using Ironfront.Buildings;
using Ironfront.Combat;
using Ironfront.Match;
using Ironfront.Units;
using UnityEngine;

namespace Ironfront.Vision
{
    /// <summary>
    /// A unit or structure taking part in the fog of war: it reveals the map around itself for its owner
    /// (<see cref="SightRange"/> from its definition) and is itself only visible to other factions when inside
    /// their vision.
    ///
    /// Presentation for the human player: enemy units are drawn only while visible. Enemy structures are drawn
    /// from the first time they are seen (static, remembered); if one is destroyed while out of sight a "last
    /// known" ghost copy stays until the player looks again, so its loss is not given away. Remembered structures
    /// sit under the explored-fog shading and cannot be targeted until visible again.
    /// </summary>
    [DisallowMultipleComponent]
    public class VisionEntity : MonoBehaviour
    {
        [Tooltip("0 = use the unit or building definition's sight range.")]
        [SerializeField] private float sightOverride;

        private FactionMember _member;
        private Health _health;
        private bool _structure;
        private Renderer[] _renderers = new Renderer[0];
        private bool _drawn = true;
        private bool _seenByPlayer;
        private readonly Dictionary<int, float> _revealedUntil = new Dictionary<int, float>(2);

        public float SightRange { get; private set; } = 10f;
        public int OwnerFaction => _member != null ? _member.FactionId : MatchController.DefaultPlayerFactionId;
        /// <summary>Bit mask of factions that currently see this entity (see <see cref="VisionSystem.FactionBit"/>).</summary>
        public int VisibleMask { get; private set; } = ~0;
        /// <summary>Half footprint (x, z) for structures, so their edges count as well as their centre.</summary>
        public Vector2 HalfExtents { get; private set; }
        public bool IsStructure => _structure;
        /// <summary>Contributes sight while alive.</summary>
        public bool IsRevealer => _health == null || _health.IsAlive;
        /// <summary>Drawn on the player's screen right now (true for own entities).</summary>
        public bool DrawnForPlayer => _drawn;
        public bool SeenByPlayer => _seenByPlayer;

        private void Awake()
        {
            _member = GetComponent<FactionMember>();
            _health = GetComponent<Health>();
            if (_health != null) _health.Vision = this;
            var building = GetComponent<Building>();
            var identity = GetComponent<UnitIdentity>();
            _structure = building != null;
            float sight = building != null && building.Definition != null ? building.Definition.SightRange
                : identity != null && identity.Definition != null ? identity.Definition.SightRange : 10f;
            SightRange = sightOverride > 0f ? sightOverride : sight;
            if (building != null && building.Definition != null) HalfExtents = building.Definition.FootprintSize * 0.5f;
        }

        private void OnEnable()
        {
            VisionSystem.Ensure();
            VisionSystem.Register(this);
        }

        private void OnDisable() => VisionSystem.Unregister(this);

        private void Start()
        {
            // Every renderer except the selection indicator (that follows selection, which enemies never get).
            var list = new List<Renderer>();
            foreach (var r in GetComponentsInChildren<Renderer>(true))
                if (!r.transform.name.Contains("SelectionIndicator")) list.Add(r);
            _renderers = list.ToArray();
            if (VisionSystem.Current != null) ApplyDrawn(ShouldDraw((VisibleMask & VisionSystem.Current.FactionBit(VisionSystem.LocalFaction)) != 0));
        }

        public void RevealTo(int faction, float until)
        {
            if (!_revealedUntil.TryGetValue(faction, out float t) || t < until) _revealedUntil[faction] = until;
        }

        public bool RevealedTo(int faction, float now) => _revealedUntil.Count > 0 && _revealedUntil.TryGetValue(faction, out float t) && t > now;

        /// <summary>Called by <see cref="VisionSystem"/> after each tick.</summary>
        internal void SetVisibility(int mask, int localBit)
        {
            VisibleMask = mask;
            bool visibleToPlayer = (mask & localBit) != 0;
            if (visibleToPlayer) _seenByPlayer = true;
            ApplyDrawn(ShouldDraw(visibleToPlayer));
        }

        private bool ShouldDraw(bool visibleToPlayer)
        {
            if (OwnerFaction == VisionSystem.LocalFaction) return true;
            if (_structure) return visibleToPlayer || _seenByPlayer;
            return visibleToPlayer;
        }

        private void ApplyDrawn(bool drawn)
        {
            if (drawn == _drawn) return;
            _drawn = drawn;
            foreach (var r in _renderers)
                if (r != null) r.forceRenderingOff = !drawn;
        }

        /// <summary>Is this entity drawn for the player? (Units hidden by fog are not; HealthBar, minimap and FX use this.)</summary>
        public static bool IsDrawn(Component c)
        {
            var h = c != null ? c.GetComponent<Health>() : null;
            return h == null || h.Vision == null || h.Vision.DrawnForPlayer;
        }

        private void OnDestroy()
        {
            // Destroyed while the player was not looking: leave a "last known" ghost so the loss is not revealed.
            // Only real destruction counts (not scene unloads), and only for structures the player had seen.
            if (!_structure || !_seenByPlayer || _health == null || _health.IsAlive || OwnerFaction == VisionSystem.LocalFaction) return;
            var sys = VisionSystem.Current;
            if (sys == null || !sys.FogEnabled) return;
            if ((VisibleMask & sys.FactionBit(VisionSystem.LocalFaction)) != 0) return;
            StructureGhost.Create(this, _renderers);
        }
    }
}
