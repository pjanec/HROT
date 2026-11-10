using System;
using System.IO;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐ <c>CE-4365</c> (L-3, docs/DESIGN_Terrain_Height.md §9) — the samples of a <see cref="TerrainHeightGrid"/>, held in square TILES of
    /// <see cref="TileSize"/>² posts behind a tile index. One store for every grid (an <c>.asc</c>'s few thousand posts are one tile; a
    /// 5.5 km terrain at 1 m is 22 × 22 tiles, ≈ 121 MB): the index is what a sparse inset (CE-4012) later fills partly. Samples are
    /// float — measured against 16-bit + per-tile offset / scale (§9.2). Allocation-free reads; immutable after construction.
    /// </summary>
    public sealed class TerrainHeightTiles
    {
        /// <summary>Posts along a tile's side (a power of two: the lookup is shifts and masks).</summary>
        public const int TileShift = 8;
        public const int TileSize = 1 << TileShift;
        private const int TileMask = TileSize - 1;
        private const int TileArea = TileSize * TileSize;

        private readonly float[] _data;    // tile-major: the tile in slot s holds [s·TileArea, (s+1)·TileArea), row 0 = south
        private readonly int[] _slot;      // tile index (ty·TilesX + tx) → its slot in _data

        public int Cols { get; }
        public int Rows { get; }
        public int TilesX { get; }
        public int TilesY { get; }

        /// <summary>The bytes the samples take (the memory budget's number).</summary>
        public long SampleBytes => (long)_data.Length * sizeof(float);

        private TerrainHeightTiles(int cols, int rows, float[] data, int[] slot)
        {
            Cols = cols; Rows = rows;
            TilesX = (cols + TileMask) >> TileShift;
            TilesY = (rows + TileMask) >> TileShift;
            _data = data; _slot = slot;
        }

        /// <summary>The sample at column <paramref name="c"/>, row <paramref name="r"/> (row 0 = south). No bounds check beyond the arrays'.</summary>
        public float this[int c, int r]
            => _data[(_slot[(r >> TileShift) * TilesX + (c >> TileShift)] << (2 * TileShift)) | ((r & TileMask) << TileShift) | (c & TileMask)];

        /// <summary>The tiles of a row-major grid (row 0 = south). A tile past the grid's east / north edge repeats the edge post.</summary>
        public static TerrainHeightTiles FromRows(float[] z, int cols, int rows)
        {
            if (z.Length != (long)cols * rows) throw new ArgumentException($"A {cols}×{rows} grid needs {(long)cols * rows} samples (got {z.Length}).");
            int tx = (cols + TileMask) >> TileShift, ty = (rows + TileMask) >> TileShift;
            var data = new float[(long)tx * ty * TileArea];
            var slot = new int[tx * ty];
            for (int t = 0; t < slot.Length; t++) slot[t] = t;
            for (int j = 0; j < ty; j++)
                for (int i = 0; i < tx; i++)
                {
                    int baseIdx = (j * tx + i) * TileArea;
                    for (int y = 0; y < TileSize; y++)
                    {
                        int r = Math.Min(j * TileSize + y, rows - 1);
                        for (int x = 0; x < TileSize; x++)
                            data[baseIdx + (y << TileShift) + x] = z[r * cols + Math.Min(i * TileSize + x, cols - 1)];
                    }
                }
            return new TerrainHeightTiles(cols, rows, data, slot);
        }

        // ── the on-disk store: <name>.heights ──────────────────────────────────────────────────────────────────────────────────────
        //   "HRTH" · u32 version (1) · u32 cols · u32 rows · f64 originX · f64 originY · f64 cellSize · u32 tileSize · u32 tilesX ·
        //   u32 tilesY · then tilesX·tilesY tiles (south row of tiles first, west first), each tileSize² little-endian f32, row 0 = south.

        public const uint Magic = 0x48545248;   // "HRTH" little-endian
        public const int Version = 1;

        /// <summary>Writes the grid's samples (and its placement) as a height store.</summary>
        public void Write(Stream s, System.Numerics.Vector2 origin, float cellSize)
        {
            using var w = new BinaryWriter(s, System.Text.Encoding.ASCII, leaveOpen: true);
            w.Write(Magic); w.Write(Version); w.Write(Cols); w.Write(Rows);
            w.Write((double)origin.X); w.Write((double)origin.Y); w.Write((double)cellSize);
            w.Write(TileSize); w.Write(TilesX); w.Write(TilesY);
            var bytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(_data.AsSpan());
            if (!BitConverter.IsLittleEndian) throw new PlatformNotSupportedException("The height store is little-endian.");
            for (int t = 0; t < _slot.Length; t++)
                w.Write(bytes.Slice(_slot[t] * TileArea * sizeof(float), TileArea * sizeof(float)));
        }

        /// <summary>Reads a height store: its tiles and its placement (origin of the south-west post, cell size).</summary>
        public static TerrainHeightTiles Read(Stream s, string where, out System.Numerics.Vector2 origin, out float cellSize)
        {
            using var r = new BinaryReader(s, System.Text.Encoding.ASCII, leaveOpen: true);
            if (r.ReadUInt32() != Magic) throw new InvalidDataException($"Height store {where}: not a height store (no HRTH magic).");
            int version = r.ReadInt32();
            if (version != Version) throw new InvalidDataException($"Height store {where}: version {version} is not supported (understands {Version}).");
            int cols = r.ReadInt32(), rows = r.ReadInt32();
            origin = new System.Numerics.Vector2((float)r.ReadDouble(), (float)r.ReadDouble());
            cellSize = (float)r.ReadDouble();
            int tile = r.ReadInt32(), tx = r.ReadInt32(), ty = r.ReadInt32();
            if (tile != TileSize) throw new InvalidDataException($"Height store {where}: tile size {tile} (this build reads {TileSize}).");
            if (cols < 2 || rows < 2 || tx != (cols + TileMask) >> TileShift || ty != (rows + TileMask) >> TileShift)
                throw new InvalidDataException($"Height store {where}: {cols}×{rows} posts do not match {tx}×{ty} tiles.");
            var data = new float[(long)tx * ty * TileArea];
            var bytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(data.AsSpan());
            int read = 0;
            while (read < bytes.Length)
            {
                int n = s.Read(bytes.Slice(read));
                if (n <= 0) throw new InvalidDataException($"Height store {where}: truncated ({read} of {bytes.Length} sample bytes).");
                read += n;
            }
            var slot = new int[tx * ty];
            for (int t = 0; t < slot.Length; t++) slot[t] = t;
            return new TerrainHeightTiles(cols, rows, data, slot);
        }
    }
}
