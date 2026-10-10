using System.Collections.Generic;
using UnityEngine;

namespace Ironfront.Core
{
    /// <summary>Limits how often a notification of the same kind can appear (e.g. "Base under attack").</summary>
    public sealed class NotificationThrottle
    {
        private readonly Dictionary<string, float> _last = new Dictionary<string, float>();

        /// <summary>Whether <paramref name="key"/> could fire now (does not record). Lets callers skip building the message.</summary>
        public bool IsReady(string key, float time, float cooldown) =>
            !_last.TryGetValue(key, out float last) || time - last >= cooldown;

        /// <summary>True (and records the time) if <paramref name="key"/> has not fired within <paramref name="cooldown"/>.</summary>
        public bool TryFire(string key, float time, float cooldown)
        {
            if (_last.TryGetValue(key, out float last) && time - last < cooldown) return false;
            _last[key] = time;
            return true;
        }
    }

    /// <summary>Per-faction match statistics shown on the result screen.</summary>
    public sealed class MatchStatistics
    {
        public sealed class FactionStats
        {
            public int UnitsProduced;
            public int UnitsLost;
            public int StructuresBuilt;
            public int StructuresLost;
            public int EnemyUnitsDestroyed;
            public int EnemyStructuresDestroyed;
            public int OreHarvested;
        }

        private readonly Dictionary<int, FactionStats> _stats = new Dictionary<int, FactionStats>();

        public FactionStats For(int faction)
        {
            if (!_stats.TryGetValue(faction, out var s)) _stats[faction] = s = new FactionStats();
            return s;
        }

        public void UnitProduced(int faction) => For(faction).UnitsProduced++;
        public void StructureBuilt(int faction) => For(faction).StructuresBuilt++;
        public void OreDelivered(int faction, int ore) => For(faction).OreHarvested += Mathf.Max(0, ore);

        /// <summary>Records a destroyed unit or structure; the kill is credited to <paramref name="killerFaction"/> when it differs.</summary>
        public void Destroyed(int victimFaction, bool structure, int? killerFaction)
        {
            var v = For(victimFaction);
            if (structure) v.StructuresLost++; else v.UnitsLost++;
            if (killerFaction.HasValue && killerFaction.Value != victimFaction)
            {
                var k = For(killerFaction.Value);
                if (structure) k.EnemyStructuresDestroyed++; else k.EnemyUnitsDestroyed++;
            }
        }
    }

    /// <summary>Maps between the world's XZ rectangle and minimap pixels/UV.</summary>
    public static class MinimapMath
    {
        /// <summary>World XZ → normalised [0,1]² (x right, y up = +Z).</summary>
        public static Vector2 WorldToUv(Vector3 world, Rect worldRect) =>
            new Vector2((world.x - worldRect.xMin) / worldRect.width, (world.z - worldRect.yMin) / worldRect.height);

        public static Vector3 UvToWorld(Vector2 uv, Rect worldRect) =>
            new Vector3(worldRect.xMin + Mathf.Clamp01(uv.x) * worldRect.width, 0f, worldRect.yMin + Mathf.Clamp01(uv.y) * worldRect.height);

        public static Vector2Int WorldToPixel(Vector3 world, Rect worldRect, int size)
        {
            var uv = WorldToUv(world, worldRect);
            return new Vector2Int(Mathf.Clamp(Mathf.FloorToInt(uv.x * size), 0, size - 1), Mathf.Clamp(Mathf.FloorToInt(uv.y * size), 0, size - 1));
        }

        /// <summary>Where a camera ray hits the ground plane (y = 0); false if it points away from it.</summary>
        public static bool RayToGround(Ray ray, out Vector3 point)
        {
            point = default;
            if (Mathf.Abs(ray.direction.y) < 1e-5f) return false;
            float t = -ray.origin.y / ray.direction.y;
            if (t < 0f) return false;
            point = ray.origin + ray.direction * t;
            return true;
        }
    }
}
