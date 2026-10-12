using System.Collections.Generic;
using Ironfront.Construction;
using Ironfront.Core.Construction;
using Ironfront.Core.Maps;
using Ironfront.Data;
using UnityEngine;
using UnityEngine.AI;

namespace Ironfront.Skirmish
{
    /// <summary>
    /// <see cref="IMapProbe"/> over the loaded scene (Milestone 008): the baked NavMesh for walkability and paths, and
    /// the real <see cref="BuildingPlacementController"/> rules for placement. Works in Edit Mode and Play Mode.
    /// </summary>
    public sealed class NavMeshMapProbe : IMapProbe
    {
        private readonly BuildingPlacementController _placement;
        private readonly Dictionary<string, BuildingDefinition> _definitions;
        private readonly NavMeshPath _path = new NavMeshPath();

        public NavMeshMapProbe(BuildingPlacementController placement, Dictionary<string, BuildingDefinition> definitions)
        {
            _placement = placement;
            _definitions = definitions;
        }

        private static Vector3 P(Vector2 p) => new Vector3(p.x, 0f, p.y);

        public bool IsWalkable(Vector2 p, float tolerance)
        {
            if (!NavMesh.SamplePosition(P(p), out var hit, Mathf.Max(0.1f, tolerance) + 0.5f, NavMesh.AllAreas)) return false;
            var d = new Vector2(hit.position.x - p.x, hit.position.z - p.y);
            return d.magnitude <= tolerance;
        }

        public float PathLength(Vector2 from, Vector2 to, float snapFrom, float snapTo)
        {
            if (!NavMesh.SamplePosition(P(from), out var a, snapFrom, NavMesh.AllAreas)) return float.PositiveInfinity;
            if (!NavMesh.SamplePosition(P(to), out var b, snapTo, NavMesh.AllAreas)) return float.PositiveInfinity;
            if (!NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, _path) || _path.status != NavMeshPathStatus.PathComplete)
                return float.PositiveInfinity;
            float length = 0f;
            var c = _path.corners;
            for (int i = 1; i < c.Length; i++) length += Vector3.Distance(c[i - 1], c[i]);
            return length;
        }

        public bool CanPlace(string structureId, Vector2 center, bool facesSouth, IReadOnlyList<PlannedStructure> placed)
        {
            if (_placement == null || !_definitions.TryGetValue(structureId, out var def) || def == null) return false;
            var footprints = new List<Footprint>(placed.Count);
            foreach (var s in placed)
                if (_definitions.TryGetValue(s.Id, out var d) && d != null) footprints.Add(d.FootprintAt(P(s.Center), s.FacesSouth));
            return _placement.ValidateTerrain(def, P(center), facesSouth, footprints) == PlacementResult.Valid;
        }
    }
}
