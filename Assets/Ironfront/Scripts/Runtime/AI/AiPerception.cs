using System.Collections.Generic;
using Ironfront.Buildings;
using Ironfront.Combat;
using Ironfront.Core.Combat;
using Ironfront.Units;
using UnityEngine;

namespace Ironfront.AI
{
    /// <summary>
    /// What a commander knows about its opponents: enemies currently within vision range of its own units and
    /// buildings, plus remembered structures (until seen to be gone) and recently seen units.
    /// It never reads enemy positions outside vision.
    ///
    /// Milestone 007: with fog of war the commander sees exactly what its faction sees in
    /// <see cref="Ironfront.Vision.VisionSystem"/> (the same rules as the player, including per-unit sight
    /// ranges and attackers revealing themselves). Remembered structures stay "known" until the commander sees
    /// the spot again, so it cannot learn of a loss it did not witness.
    /// </summary>
    public sealed class AiPerception
    {
        public sealed class KnownStructure
        {
            public Health Health;
            public Vector3 Position;
            public string DefinitionId;
            public float LastSeen;
            public bool Alive => Health != null && Health.IsAlive;
        }

        private readonly int _faction;
        private readonly float _vision;
        private readonly float _memory;
        private readonly List<Vector3> _eyes = new List<Vector3>();
        private readonly Dictionary<Health, (float lastSeen, string kind)> _units = new Dictionary<Health, (float, string)>();

        public List<Health> Visible { get; } = new List<Health>();
        public List<KnownStructure> Structures { get; } = new List<KnownStructure>();
        public int KnownEnemyLight { get; private set; }
        public int KnownEnemyHeavy { get; private set; }
        public int KnownEnemyArtillery { get; private set; }
        /// <summary>Known (remembered) enemy defensive structures.</summary>
        public int KnownEnemyDefences
        {
            get
            {
                int n = 0;
                foreach (var s in Structures) if (s.DefinitionId == "guard_tower" || s.DefinitionId == "at_turret") n++;
                return n;
            }
        }

        /// <summary>True while the structure is still in the commander's memory (seen, and not yet seen gone).</summary>
        public bool Remembers(Health structure)
        {
            foreach (var s in Structures) if (s.Health == structure) return true;
            return false;
        }

        private static bool FogActive => Ironfront.Vision.VisionSystem.Current != null && Ironfront.Vision.VisionSystem.Current.FogEnabled;

        public AiPerception(int faction, float visionRange, float memorySeconds)
        {
            _faction = faction;
            _vision = visionRange;
            _memory = memorySeconds;
        }

        public void Update(float time)
        {
            _eyes.Clear();
            foreach (var h in Health.All)
                if (h.IsAlive && h.FactionId == _faction) _eyes.Add(h.transform.position);

            Visible.Clear();
            float vision2 = _vision * _vision;
            foreach (var h in Health.All)
            {
                if (!h.IsAlive || !DamageRules.AreHostile(_faction, h.FactionId)) continue;
                if (FogActive ? !Ironfront.Vision.VisionSystem.CanSee(_faction, h) : !Seen(h.transform.position, vision2)) continue;
                Visible.Add(h);

                var building = h.GetComponent<Building>();
                if (building != null && building.Definition != null)
                {
                    var known = Structures.Find(s => s.Health == h);
                    if (known == null)
                    {
                        known = new KnownStructure { Health = h, Position = h.transform.position, DefinitionId = building.Definition.Id };
                        Structures.Add(known);
                    }
                    known.LastSeen = time;
                }
                else if (h.IsArmed)
                {
                    var identity = h.GetComponent<UnitIdentity>();
                    _units[h] = (time, identity != null && identity.Definition != null ? identity.Definition.Id : "light_tank");
                }
            }

            // Forget structures that are gone, but only once the spot is in view again.
            Structures.RemoveAll(s => !s.Alive && (FogActive ? Ironfront.Vision.VisionSystem.CanSeePoint(_faction, s.Position) : Seen(s.Position, vision2)));

            KnownEnemyLight = KnownEnemyHeavy = KnownEnemyArtillery = 0;
            var stale = new List<Health>();
            foreach (var kv in _units)
            {
                if (kv.Key == null || !kv.Key.IsAlive || time - kv.Value.lastSeen > _memory) { stale.Add(kv.Key); continue; }
                switch (kv.Value.kind)
                {
                    case "heavy_tank": KnownEnemyHeavy++; break;
                    case "rocket_artillery": KnownEnemyArtillery++; break;
                    case "recon": break; // scouts are not an army
                    default: KnownEnemyLight++; break;
                }
            }
            foreach (var h in stale) _units.Remove(h);
        }

        /// <summary>Visible armed enemies within <paramref name="radius"/> of any of the given points.</summary>
        public List<Health> ThreatsNear(IReadOnlyList<Vector3> points, float radius)
        {
            var result = new List<Health>();
            float r2 = radius * radius;
            foreach (var h in Visible)
            {
                if (!h.IsArmed) continue;
                foreach (var p in points)
                {
                    if ((h.transform.position - p).sqrMagnitude <= r2)
                    {
                        result.Add(h);
                        break;
                    }
                }
            }
            return result;
        }

        private bool Seen(Vector3 position, float vision2)
        {
            foreach (var e in _eyes)
                if ((e - position).sqrMagnitude <= vision2) return true;
            return false;
        }
    }
}
