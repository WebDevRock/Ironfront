using System.Collections.Generic;
using Ironfront.Combat;
using Ironfront.Core.Vision;
using Ironfront.Match;
using UnityEngine;

namespace Ironfront.Vision
{
    /// <summary>
    /// Fog of war (Milestone 007). Ten times a second it rebuilds every faction's visibility from its units' and
    /// structures' sight circles (<see cref="VisibilityGrid"/>), then decides for every <see cref="VisionEntity"/>
    /// which factions can currently see it. Targeting, selection, the minimap, the computer commanders and the
    /// presentation all ask this system, so no code path reads hidden enemy state.
    ///
    /// When the match has fog disabled (or no system exists, e.g. in tools), everything counts as visible.
    /// </summary>
    [DefaultExecutionOrder(-150)]
    public class VisionSystem : MonoBehaviour
    {
        public const float DefaultCellSize = 1f;
        /// <summary>Seconds an attacker stays revealed to the faction it hit (muzzle flashes give positions away).</summary>
        public const float AttackRevealSeconds = 2.5f;

        private static VisionSystem s_current;
        private static readonly List<VisionEntity> s_entities = new List<VisionEntity>();
        /// <summary>A new unit or structure appeared: update vision on the next frame instead of waiting for the timer.</summary>
        private static bool s_dirty;

        [SerializeField] private float tickInterval = 0.1f;
        [SerializeField] private Vector2 mapMin = new Vector2(-50f, -50f);
        [SerializeField] private Vector2 mapSize = new Vector2(100f, 100f);
        [SerializeField] private float cellSize = DefaultCellSize;

        private VisibilityGrid _grid;
        private float _timer;
        private bool _enabled = true;
        private int[] _factions = new int[0];

        public static VisionSystem Current => s_current;
        public VisibilityGrid Grid => _grid;
        public bool FogEnabled => _enabled;
        /// <summary>Vision ticks run so far (diagnostics / tests).</summary>
        public int Ticks { get; private set; }
        public static IReadOnlyList<VisionEntity> Entities => s_entities;

        /// <summary>The faction whose view the screen shows (the human player).</summary>
        public static int LocalFaction => MatchController.Current != null ? MatchController.Current.PlayerFactionId : MatchController.DefaultPlayerFactionId;

        /// <summary>Raised after every vision tick.</summary>
        public event System.Action Updated;

        private void Awake()
        {
            s_current = this;
            var setup = MatchController.Current != null ? MatchController.Current.Setup : null;
            _enabled = (setup == null || setup.FogOfWar) && Visuals.VisualFeatures.Fog;
            var ids = new List<int>();
            if (setup != null) foreach (var f in setup.Factions) ids.Add(f.id);
            if (ids.Count == 0) ids.Add(MatchController.DefaultPlayerFactionId);
            _factions = ids.ToArray();
            _grid = new VisibilityGrid(mapMin.x, mapMin.y, mapSize.x, mapSize.y, cellSize, ids);
        }

        private void Start() => Tick(); // initial view before the first frame renders

        private void OnDestroy()
        {
            if (s_current == this) s_current = null;
        }

        /// <summary>Creates the system for the active match if the scene has none (tests, runtime-built scenes).</summary>
        public static VisionSystem Ensure()
        {
            if (s_current != null) return s_current;
            if (MatchController.Current == null) return null;
            return MatchController.Current.gameObject.AddComponent<VisionSystem>();
        }

        internal static void Register(VisionEntity e)
        {
            if (!s_entities.Contains(e)) s_entities.Add(e);
            s_dirty = true;
        }

        internal static void Unregister(VisionEntity e) => s_entities.Remove(e);

        private void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f && !s_dirty) return;
            _timer = tickInterval;
            s_dirty = false;
            Tick();
        }

        /// <summary>Recomputes visibility now (normally every <see cref="tickInterval"/>; public for tests).</summary>
        public void Tick()
        {
            using var perfScope = Ironfront.Diagnostics.PerfMarkers.Vision.Auto();
            Ticks++;
            float now = Time.time;
            _grid.BeginTick();
            for (int i = 0; i < s_entities.Count; i++)
            {
                var e = s_entities[i];
                if (e == null || !e.IsRevealer) continue;
                Vector3 p = e.transform.position;
                _grid.Reveal(e.OwnerFaction, p.x, p.z, e.SightRange);
            }

            for (int i = 0; i < s_entities.Count; i++)
            {
                var e = s_entities[i];
                if (e == null) continue;
                int mask = 0;
                Vector3 p = e.transform.position;
                for (int f = 0; f < _factions.Length; f++)
                {
                    int faction = _factions[f];
                    bool sees = !_enabled || faction == e.OwnerFaction
                                || _grid.AnyVisible(faction, p.x, p.z, e.HalfExtents.x, e.HalfExtents.y)
                                || e.RevealedTo(faction, now);
                    if (sees) mask |= 1 << f;
                }
                e.SetVisibility(mask, FactionBit(LocalFaction));
            }
            Updated?.Invoke();
        }

        /// <summary>Bit for a faction in <see cref="VisionEntity.VisibleMask"/>, or 0 if unknown.</summary>
        public int FactionBit(int faction)
        {
            for (int i = 0; i < _factions.Length; i++)
                if (_factions[i] == faction) return 1 << i;
            return 0;
        }

        // ---------- Queries ----------

        /// <summary>Can <paramref name="faction"/> currently see <paramref name="target"/>? True without fog.</summary>
        public static bool CanSee(int faction, Health target)
        {
            if (target == null) return false;
            var sys = s_current;
            if (sys == null || !sys._enabled) return true;
            var e = target.Vision;
            if (e == null) return true;
            return (e.VisibleMask & sys.FactionBit(faction)) != 0;
        }

        /// <summary>Is a world point currently visible to <paramref name="faction"/>? True without fog.</summary>
        public static bool CanSeePoint(int faction, Vector3 p)
        {
            var sys = s_current;
            if (sys == null || !sys._enabled) return true;
            return sys._grid.IsVisible(faction, p.x, p.z);
        }

        public static bool IsExplored(int faction, Vector3 p)
        {
            var sys = s_current;
            if (sys == null || !sys._enabled) return true;
            return sys._grid.IsExplored(faction, p.x, p.z);
        }

        /// <summary>Shortcut for presentation code: is the point visible to the human player?</summary>
        public static bool PlayerCanSee(Vector3 p) => CanSeePoint(LocalFaction, p);

        /// <summary>The attacker gave its position away to the victim's faction (until the reveal expires).</summary>
        public static void RevealAttacker(Health attacker, int victimFaction)
        {
            if (attacker == null || attacker.Vision == null || s_current == null) return;
            attacker.Vision.RevealTo(victimFaction, Time.time + AttackRevealSeconds);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_current = null;
            s_entities.Clear();
            s_dirty = false;
        }
    }
}
