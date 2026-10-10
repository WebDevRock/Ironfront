using System.Collections.Generic;
using UnityEngine;

namespace Ironfront.Core.Construction
{
    /// <summary>
    /// A structure's ground footprint on the XZ plane (x → Rect.x, z → Rect.y). The apron is a reserved strip
    /// in front of the building (refinery dock, factory exit) that must stay clear for units. Buildings face
    /// either +Z (north, the default) or −Z (south, rotated 180°).
    /// </summary>
    public readonly struct Footprint
    {
        public readonly Rect Body;
        public readonly Rect Apron;

        public bool HasApron => Apron.width > 0f && Apron.height > 0f;

        public Footprint(Rect body, Rect apron)
        {
            Body = body;
            Apron = apron;
        }

        /// <summary>Footprint centred on (x, z) with the apron on the +Z side, or the −Z side when facing south.</summary>
        public static Footprint FromCenter(Vector2 centerXZ, Vector2 size, float apronDepth, bool facesSouth = false)
        {
            var body = new Rect(centerXZ - size * 0.5f, size);
            var apron = apronDepth <= 0f
                ? new Rect(0f, 0f, 0f, 0f)
                : facesSouth
                    ? new Rect(body.xMin, body.yMin - apronDepth, size.x, apronDepth)
                    : new Rect(body.xMin, body.yMax, size.x, apronDepth);
            return new Footprint(body, apron);
        }
    }

    public enum PlacementResult
    {
        Valid,
        OutOfBounds,
        OverlapsStructure,
        BlocksAccess,
        OverlapsObstacle,
        OverlapsResource,
        OverlapsUnit,
    }

    /// <summary>
    /// Pure placement validation. Buildings keep a clearance gap from everything so a corridor wider than a
    /// unit always remains, and nothing may be built on another structure's apron or a resource's access ring.
    /// </summary>
    public static class PlacementRules
    {
        public static PlacementResult Evaluate(
            Footprint candidate,
            Rect mapBounds,
            IEnumerable<Footprint> structures,
            IEnumerable<Rect> obstacles,
            IEnumerable<Rect> resources,
            IEnumerable<Rect> units,
            float clearance,
            float resourceClearance)
        {
            if (!Contains(mapBounds, candidate.Body) || (candidate.HasApron && !Contains(mapBounds, candidate.Apron)))
                return PlacementResult.OutOfBounds;

            Rect padded = Expand(candidate.Body, clearance);

            foreach (var s in structures)
            {
                if (padded.Overlaps(s.Body)) return PlacementResult.OverlapsStructure;
                if (s.HasApron && padded.Overlaps(s.Apron)) return PlacementResult.BlocksAccess;
                if (candidate.HasApron && candidate.Apron.Overlaps(Expand(s.Body, clearance)))
                    return PlacementResult.BlocksAccess;
            }

            foreach (var o in obstacles)
            {
                if (padded.Overlaps(o)) return PlacementResult.OverlapsObstacle;
                if (candidate.HasApron && candidate.Apron.Overlaps(o)) return PlacementResult.BlocksAccess;
            }

            Rect resourcePadded = Expand(candidate.Body, resourceClearance);
            foreach (var r in resources)
            {
                if (resourcePadded.Overlaps(r)) return PlacementResult.OverlapsResource;
                if (candidate.HasApron && candidate.Apron.Overlaps(r)) return PlacementResult.BlocksAccess;
            }

            foreach (var u in units)
            {
                if (candidate.Body.Overlaps(u)) return PlacementResult.OverlapsUnit;
            }

            return PlacementResult.Valid;
        }

        public static Rect Expand(Rect r, float amount) =>
            new Rect(r.xMin - amount, r.yMin - amount, r.width + amount * 2f, r.height + amount * 2f);

        public static bool Contains(Rect outer, Rect inner) =>
            inner.xMin >= outer.xMin && inner.yMin >= outer.yMin && inner.xMax <= outer.xMax && inner.yMax <= outer.yMax;

        /// <summary>Snaps an XZ position to a grid so buildings line up.</summary>
        public static Vector2 Snap(Vector2 xz, float grid) =>
            grid <= 0f ? xz : new Vector2(Mathf.Round(xz.x / grid) * grid, Mathf.Round(xz.y / grid) * grid);

        public static string Describe(PlacementResult result)
        {
            switch (result)
            {
                case PlacementResult.Valid: return "Valid placement";
                case PlacementResult.OutOfBounds: return "Outside the map";
                case PlacementResult.OverlapsStructure: return "Too close to another structure";
                case PlacementResult.BlocksAccess: return "Would block a dock or factory exit";
                case PlacementResult.OverlapsObstacle: return "Blocked by terrain";
                case PlacementResult.OverlapsResource: return "Too close to an ore deposit";
                case PlacementResult.OverlapsUnit: return "Units in the way";
                default: return result.ToString();
            }
        }
    }
}
