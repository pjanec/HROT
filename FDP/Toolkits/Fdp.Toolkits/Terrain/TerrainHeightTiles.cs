using System;
using System.IO;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐ <c>CE-4365</c> (L-3, docs/DESIGN_Terrain_Height.md §9) — the samples of a <see cref="TerrainHeightGrid"/>, held in square TILES of
    /// <see cref="TileSize"/>² posts behind a tile index. One store for every grid (an <c>.asc</c>'s few thousand posts are one tile; a
    /// 5.5 km terrain at 1 m is 22 × 22 tiles, ≈ 121 MB): the index is what a sparse inset (CE-4012) later fills partly. Samples are
    /// float — measured against 16-bit + per-tile offset / scale (§9.2). Allocation-free reads; immutable after construction.
    /// <para>⭐ <c>CE-4596</c> (P-0; 📄 docs/DESIGN_Ground_Patches_And_Road_Blockage.md §9.1 V-5) — each tile is its OWN array, so a patched
    /// store (<see cref="Patched"/>) copies only the tiles it touches and SHARES every other one with the store it came from: a crater or a pad
    /// costs one 256 KB tile, not the whole ~121 MB store. Still immutable: a patch is a new store.</para>
    /// </summary>
    public sealed class TerrainHeightTiles
    {
        /// <summary>Posts along a tile's side (a power of two: the lookup is shifts and masks).</summary>
        public const int TileShift = 8;
        public const int TileSize = 1 << TileShift;
        private const int TileMask = TileSize - 1;
        private const int TileArea = TileSize * TileSize;

        private readonly float[][] _tiles;   // ⭐ CE-4596 — tile index (ty·TilesX + tx) → its own TileArea samples, row 0 = south

        public int Cols { get; }
        public int Rows { get; }
        public int TilesX { get; }
        public int TilesY { get; }

        /// <summary>The bytes the samples take (the memory budget's number) — this store's tiles, shared ones included.</summary>
        public long SampleBytes => (long)_tiles.Length * TileArea * sizeof(float);

        private TerrainHeightTiles(int cols, int rows, float[][] tiles)
        {
            Cols = cols; Rows = rows;
            TilesX = (cols + TileMask) >> TileShift;
            TilesY = (rows + TileMask) >> TileShift;
            _tiles = tiles;
        }

        /// <summary>The sample at column <paramref name="c"/>, row <paramref name="r"/> (row 0 = south). No bounds check beyond the arrays'.</summary>
        public float this[int c, int r]
            => _tiles[(r >> TileShift) * TilesX + (c >> TileShift)][((r & TileMask) << TileShift) | (c & TileMask)];

        /// <summary>⭐ <c>CE-4596</c> — does this store hold the very same tile array (tile <paramref name="tx"/>, <paramref name="ty"/>) as
        /// <paramref name="other"/> — a patch shared it rather than copying it (rails, diagnostics).</summary>
        public bool SharesTile(TerrainHeightTiles other, int tx, int ty)
            => ReferenceEquals(_tiles[ty * TilesX + tx], other._tiles[ty * other.TilesX + tx]);

        /// <summary>The tiles of a row-major grid (row 0 = south). A tile past the grid's east / north edge repeats the edge post.</summary>
        public static TerrainHeightTiles FromRows(float[] z, int cols, int rows)
        {
            if (z.Length != (long)cols * rows) throw new ArgumentException($"A {cols}×{rows} grid needs {(long)cols * rows} samples (got {z.Length}).");
            int tx = (cols + TileMask) >> TileShift, ty = (rows + TileMask) >> TileShift;
            var tiles = new float[tx * ty][];
            for (int j = 0; j < ty; j++)
                for (int i = 0; i < tx; i++)
                {
                    var tile = tiles[j * tx + i] = new float[TileArea];
                    for (int y = 0; y < TileSize; y++)
                    {
                        int r = Math.Min(j * TileSize + y, rows - 1);
                        for (int x = 0; x < TileSize; x++)
                            tile[(y << TileShift) + x] = z[r * cols + Math.Min(i * TileSize + x, cols - 1)];
                    }
                }
            return new TerrainHeightTiles(cols, rows, tiles);
        }

        /// <summary>
        /// ⭐ <c>CE-4596</c> (P-0, V-5) — a NEW store whose posts in columns [<paramref name="c0"/>, <paramref name="c1"/>] × rows
        /// [<paramref name="r0"/>, <paramref name="r1"/>] (inclusive, clamped to the grid) are <paramref name="patch"/>(c, r, z) of this store's;
        /// every tile the rectangle does not touch is SHARED (the same array), every tile it touches is copied once. The padding a tile holds past
        /// the grid's east / north edge keeps repeating the (patched) edge post. Allocates the touched tiles and the index only.
        /// </summary>
        public TerrainHeightTiles Patched(int c0, int r0, int c1, int r1, Func<int, int, float, float> patch)
        {
            if (patch == null) throw new ArgumentNullException(nameof(patch));
            c0 = Math.Max(c0, 0); r0 = Math.Max(r0, 0); c1 = Math.Min(c1, Cols - 1); r1 = Math.Min(r1, Rows - 1);
            var tiles = (float[][])_tiles.Clone();
            if (c0 > c1 || r0 > r1) return new TerrainHeightTiles(Cols, Rows, tiles);
            // a patch on the last column / row also rewrites the padding that repeats it ⇒ the rectangle in TILE posts reaches the tile's edge there
            int pc1 = c1 == Cols - 1 ? TilesX * TileSize - 1 : c1, pr1 = r1 == Rows - 1 ? TilesY * TileSize - 1 : r1;
            for (int ty = r0 >> TileShift; ty <= pr1 >> TileShift; ty++)
                for (int tx = c0 >> TileShift; tx <= pc1 >> TileShift; tx++)
                {
                    int t = ty * TilesX + tx;
                    var tile = tiles[t] = (float[])_tiles[t].Clone();
                    int ya = Math.Max(r0 - (ty << TileShift), 0), yb = Math.Min(pr1 - (ty << TileShift), TileMask);
                    int xa = Math.Max(c0 - (tx << TileShift), 0), xb = Math.Min(pc1 - (tx << TileShift), TileMask);
                    for (int y = ya; y <= yb; y++)
                    {
                        int r = Math.Min((ty << TileShift) + y, Rows - 1);
                        for (int x = xa; x <= xb; x++)
                        {
                            int c = Math.Min((tx << TileShift) + x, Cols - 1);
                            tile[(y << TileShift) | x] = patch(c, r, this[c, r]);
                        }
                    }
                }
            return new TerrainHeightTiles(Cols, Rows, tiles);
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
            if (!BitConverter.IsLittleEndian) throw new PlatformNotSupportedException("The height store is little-endian.");
            for (int t = 0; t < _tiles.Length; t++)
                w.Write(System.Runtime.InteropServices.MemoryMarshal.AsBytes(_tiles[t].AsSpan()));
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
            var tiles = new float[tx * ty][];
            long total = (long)tiles.Length * TileArea * sizeof(float), read = 0;
            for (int t = 0; t < tiles.Length; t++)
            {
                var bytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes((tiles[t] = new float[TileArea]).AsSpan());
                int got = 0;
                while (got < bytes.Length)
                {
                    int n = s.Read(bytes.Slice(got));
                    if (n <= 0) throw new InvalidDataException($"Height store {where}: truncated ({read + got} of {total} sample bytes).");
                    got += n;
                }
                read += got;
            }
            return new TerrainHeightTiles(cols, rows, tiles);
        }
    }
}
