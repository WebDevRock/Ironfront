using System;
using System.Collections.Generic;

namespace Ironfront.Core.Vision
{
    /// <summary>
    /// Faction visibility on a square grid over the map (Milestone 007 fog of war). Each faction has a
    /// "visible now" layer, rebuilt every vision tick from its revealers' sight circles, and an "explored"
    /// layer that only ever grows. Circles are stamped from cached cell offsets, so a tick costs one write per
    /// covered cell (no per-cell distance maths). Pure logic: no Unity types.
    /// </summary>
    public sealed class VisibilityGrid
    {
        public const byte Unexplored = 0;
        public const byte Explored = 1;
        public const byte Visible = 2;

        private readonly float _minX, _minZ, _cell;
        private readonly int[] _factionIds;
        private readonly byte[][] _visible;
        private readonly byte[][] _explored;
        private readonly Dictionary<int, int[]> _stamps = new Dictionary<int, int[]>();

        public int Width { get; }
        public int Height { get; }
        public float CellSize => _cell;
        public float MinX => _minX;
        public float MinZ => _minZ;
        public IReadOnlyList<int> Factions => _factionIds;

        public VisibilityGrid(float minX, float minZ, float sizeX, float sizeZ, float cellSize, IReadOnlyList<int> factionIds)
        {
            if (cellSize <= 0f) throw new ArgumentOutOfRangeException(nameof(cellSize));
            if (factionIds == null || factionIds.Count == 0) throw new ArgumentException("At least one faction.", nameof(factionIds));
            _minX = minX;
            _minZ = minZ;
            _cell = cellSize;
            Width = Math.Max(1, (int)Math.Ceiling(sizeX / cellSize));
            Height = Math.Max(1, (int)Math.Ceiling(sizeZ / cellSize));
            _factionIds = new int[factionIds.Count];
            _visible = new byte[factionIds.Count][];
            _explored = new byte[factionIds.Count][];
            for (int i = 0; i < factionIds.Count; i++)
            {
                _factionIds[i] = factionIds[i];
                _visible[i] = new byte[Width * Height];
                _explored[i] = new byte[Width * Height];
            }
        }

        public int IndexOf(int faction)
        {
            for (int i = 0; i < _factionIds.Length; i++)
                if (_factionIds[i] == faction) return i;
            return -1;
        }

        /// <summary>Clears every faction's "visible now" layer (explored is kept).</summary>
        public void BeginTick()
        {
            for (int i = 0; i < _visible.Length; i++) Array.Clear(_visible[i], 0, _visible[i].Length);
        }

        public bool ToCell(float x, float z, out int cx, out int cz)
        {
            cx = (int)Math.Floor((x - _minX) / _cell);
            cz = (int)Math.Floor((z - _minZ) / _cell);
            return cx >= 0 && cz >= 0 && cx < Width && cz < Height;
        }

        /// <summary>Marks a sight circle as visible (and explored) for <paramref name="faction"/>.</summary>
        public void Reveal(int faction, float x, float z, float radius)
        {
            int f = IndexOf(faction);
            if (f < 0 || radius <= 0f) return;
            int cx = (int)Math.Floor((x - _minX) / _cell);
            int cz = (int)Math.Floor((z - _minZ) / _cell);
            int r = (int)Math.Ceiling(radius / _cell);
            var stamp = Stamp(r);
            var vis = _visible[f];
            var exp = _explored[f];
            for (int i = 0; i < stamp.Length; i += 2)
            {
                int px = cx + stamp[i], pz = cz + stamp[i + 1];
                if (px < 0 || pz < 0 || px >= Width || pz >= Height) continue;
                int idx = pz * Width + px;
                vis[idx] = 1;
                exp[idx] = 1;
            }
        }

        /// <summary>Cell offsets (dx, dz pairs) inside a circle of <paramref name="r"/> cells, cached per radius.</summary>
        private int[] Stamp(int r)
        {
            if (_stamps.TryGetValue(r, out var s)) return s;
            var list = new List<int>();
            float limit = (r + 0.5f) * (r + 0.5f);
            for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                    if (dx * dx + dz * dz <= limit) { list.Add(dx); list.Add(dz); }
            s = list.ToArray();
            _stamps[r] = s;
            return s;
        }

        public byte StateAt(int faction, float x, float z)
        {
            int f = IndexOf(faction);
            if (f < 0 || !ToCell(x, z, out int cx, out int cz)) return Unexplored;
            int idx = cz * Width + cx;
            return _visible[f][idx] != 0 ? Visible : _explored[f][idx] != 0 ? Explored : Unexplored;
        }

        public bool IsVisible(int faction, float x, float z) => StateAt(faction, x, z) == Visible;
        public bool IsExplored(int faction, float x, float z) => StateAt(faction, x, z) != Unexplored;

        /// <summary>Visible if any of the sample points is in a visible cell (used for large structures).</summary>
        public bool AnyVisible(int faction, float cx, float cz, float halfX, float halfZ)
        {
            return IsVisible(faction, cx, cz)
                   || IsVisible(faction, cx - halfX, cz - halfZ) || IsVisible(faction, cx + halfX, cz - halfZ)
                   || IsVisible(faction, cx - halfX, cz + halfZ) || IsVisible(faction, cx + halfX, cz + halfZ);
        }

        /// <summary>Raw state per cell for drawing: 0 unexplored, 1 explored, 2 visible.</summary>
        public void CopyStates(int faction, byte[] into)
        {
            int f = IndexOf(faction);
            if (f < 0 || into == null || into.Length < Width * Height) return;
            var vis = _visible[f];
            var exp = _explored[f];
            for (int i = 0; i < vis.Length; i++) into[i] = vis[i] != 0 ? Visible : exp[i] != 0 ? Explored : Unexplored;
        }

        /// <summary>Fraction of the map this faction has explored (0..1).</summary>
        public float ExploredFraction(int faction)
        {
            int f = IndexOf(faction);
            if (f < 0) return 0f;
            int n = 0;
            foreach (var b in _explored[f]) if (b != 0) n++;
            return (float)n / _explored[f].Length;
        }

        /// <summary>Centre (x, z) of a cell.</summary>
        public (float x, float z) CellCentre(int cx, int cz) => (_minX + (cx + 0.5f) * _cell, _minZ + (cz + 0.5f) * _cell);

        public bool CellExplored(int faction, int cx, int cz)
        {
            int f = IndexOf(faction);
            return f >= 0 && cx >= 0 && cz >= 0 && cx < Width && cz < Height && _explored[f][cz * Width + cx] != 0;
        }
    }
}
