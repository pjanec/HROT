using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Fdp.Toolkit.Orchestration;
using Xunit;

namespace Fdp.Toolkit.Terrain.Tests
{
    /// <summary>
    /// ⭐ <c>CE-4365</c> (L-3, docs/DESIGN_Terrain_Height.md §9) — the large height store: the tiles answer every post exactly, the binary
    /// store round-trips bit for bit, a terrain read from a <c>.heights</c> answers <c>GroundHeightAt</c> exactly as the same terrain from its
    /// <c>.asc</c>, and the resampler interpolates and fills NODATA.
    /// </summary>
    public class HeightStoreTests
    {
        private static float F(int c, int r) => 450f + 0.37f * c - 0.21f * r + 3f * MathF.Sin(c * 0.1f) * MathF.Cos(r * 0.07f);

        [Fact]
        public void CE4365_TheTiles_AnswerEveryPost_AcrossTileEdges()
        {
            const int cols = 300, rows = 700;   // neither a multiple of the tile: partial tiles east and north
            var z = new float[cols * rows];
            for (int r = 0; r < rows; r++) for (int c = 0; c < cols; c++) z[r * cols + c] = F(c, r);
            var g = new TerrainHeightGrid(new Vector2(-10f, 5f), 2f, cols, rows, z);
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    Assert.Equal(z[r * cols + c], g[c, r]);
            Assert.Equal(z.Min(), g.MinZ);
            Assert.Equal(z.Max(), g.MaxZ);
            Assert.Equal(2, g.Tiles.TilesX);
            Assert.Equal(3, g.Tiles.TilesY);
        }

        [Fact]
        public void CE4365_TheStore_RoundTripsBitForBit()
        {
            const int cols = 513, rows = 260;
            var z = new float[cols * rows];
            for (int r = 0; r < rows; r++) for (int c = 0; c < cols; c++) z[r * cols + c] = F(c, r);
            var g = new TerrainHeightGrid(new Vector2(12.5f, -7f), 1f, cols, rows, z);
            using var ms = new MemoryStream();
            g.WriteStore(ms);
            ms.Position = 0;
            var back = TerrainHeightGrid.ReadStore(ms, "mem");
            Assert.Equal(g.Origin, back.Origin);
            Assert.Equal(g.CellSize, back.CellSize);
            Assert.Equal((g.Cols, g.Rows), (back.Cols, back.Rows));
            for (int r = 0; r < rows; r++) for (int c = 0; c < cols; c++) Assert.Equal(g[c, r], back[c, r]);
            Assert.Equal(g.MinZ, back.MinZ);
            Assert.Equal(g.MaxZ, back.MaxZ);
        }

        [Fact]
        public void CE4365_ATruncatedOrForeignStore_IsRefusedByName()
        {
            var g = new TerrainHeightGrid(Vector2.Zero, 1f, 3, 3, new float[9]);
            using var ms = new MemoryStream();
            g.WriteStore(ms);
            var cut = new MemoryStream(ms.ToArray(), 0, (int)ms.Length - 10);
            Assert.Contains("truncated", Assert.Throws<InvalidDataException>(() => TerrainHeightGrid.ReadStore(cut, "cut.heights")).Message);
            var junk = new MemoryStream(new byte[64]);
            Assert.Contains("junk.heights", Assert.Throws<InvalidDataException>(() => TerrainHeightGrid.ReadStore(junk, "junk.heights")).Message);
        }

        /// <summary>The shipped basic-desert (an .asc terrain) read twice: its own .asc, and the same grid as a .heights store — GroundHeightAt
        /// and the ground trace agree everywhere (the store changes no caller).</summary>
        [Fact]
        public void CE4365_ATerrainFromItsStore_AnswersAsFromItsAsc()
        {
            var folder = Path.Combine(ContentLayout.TerrainAssets(ContentLayout.TryFindSourceTree()!), "basic-desert");
            string worldText = File.ReadAllText(Path.Combine(folder, "basic-desert.world.geojson"));
            var fromAsc = TerrainWorldParser.Parse(worldText, "basic-desert", TerrainAssets.ForFolder(folder));
            Assert.NotNull(fromAsc.Height);

            using var store = new MemoryStream();
            fromAsc.Height!.WriteStore(store);
            byte[] bytes = store.ToArray();
            var baseAssets = TerrainAssets.ForFolder(folder);
            var storeAssets = new TerrainAssets
            {
                Materials = baseAssets.Materials, Templates = baseAssets.Templates, ReadFile = baseAssets.ReadFile,
                OpenFile = n => n == "basic-desert.heights" ? new MemoryStream(bytes) : null,
            };
            string storeWorld = worldText.Replace("basic-desert.height.asc", "basic-desert.heights");
            Assert.NotEqual(worldText, storeWorld);
            var fromStore = TerrainWorldParser.Parse(storeWorld, "basic-desert", storeAssets);

            var rng = new Random(4365);
            var lo = fromAsc.BoundsMin; var hi = fromAsc.BoundsMax;
            for (int i = 0; i < 5000; i++)
            {
                float x = lo.X + (float)rng.NextDouble() * (hi.X - lo.X), y = lo.Y + (float)rng.NextDouble() * (hi.Y - lo.Y);
                Assert.Equal(fromAsc.GroundHeightAt(x, y), fromStore.GroundHeightAt(x, y));
                var a = new Vector3(x, y, fromAsc.GroundHeightAt(x, y) + 1.7f);
                float x2 = lo.X + (float)rng.NextDouble() * (hi.X - lo.X), y2 = lo.Y + (float)rng.NextDouble() * (hi.Y - lo.Y);
                var b = new Vector3(x2, y2, fromAsc.GroundHeightAt(x2, y2) + 1.7f);
                Assert.Equal(fromAsc.Height.Crosses(a, b), fromStore.Height!.Crosses(a, b));
            }
        }

        [Fact]
        public void CE4365_TheResampler_Interpolates_AndFillsNoData()
        {
            // a 0.5 m source raster (north row first) of z = 100 + x + 2y over [0, 10] × [0, 10]; a NODATA block at the east edge
            const int n = 21; const float noData = -9999f;
            var src = new float[n * n];
            for (int row = 0; row < n; row++)
                for (int c = 0; c < n; c++)
                {
                    double x = c * 0.5, y = 10 - row * 0.5;
                    src[row * n + c] = (float)(100 + x + 2 * y);
                }
            for (int row = 8; row < 13; row++) for (int c = 17; c < n; c++) src[row * n + c] = noData;
            var z = TerrainHeightGrid.Resample(src, n, n, 0, 10, 0.5, 0.5, noData, 0, 0, 11, 11, 1.0, out long filled);
            Assert.Equal(100f + 3 + 2 * 4, z[4 * 11 + 3], 3);          // (3, 4): a plane is reproduced exactly
            Assert.Equal(100f + 5 + 2 * 1.0f, z[1 * 11 + 5], 3);
            Assert.True(filled > 0);
            Assert.DoesNotContain(z, v => v == noData || v == 0f);       // no cliff: every filled post took a real height
            Assert.InRange(z[5 * 11 + 10], 100f, 140f);
        }


        // ── CE-4596 (P-0; docs/DESIGN_Ground_Patches_And_Road_Blockage.md §9.1 V-5) — a patch copies only the tiles it touches ─────────

        private static TerrainHeightGrid Grid(int cols, int rows)
        {
            var z = new float[cols * rows];
            for (int r = 0; r < rows; r++) for (int c = 0; c < cols; c++) z[r * cols + c] = F(c, r);
            return new TerrainHeightGrid(new Vector2(-10f, 5f), 1f, cols, rows, z);
        }

        /// <summary>
        /// ⭐⭐ <c>CE-4596</c> (V-5) — a crater-sized patch (30 × 30 posts) inside ONE tile copies that tile only: every other tile is the very same
        /// array as before; the patched posts answer the patch, the rest the old heights; the old grid is unchanged. 🔴 Red-proof: clone every tile in
        /// <c>TerrainHeightTiles.Patched</c> ⇒ the "shares" assertion fails.
        /// </summary>
        [Fact]
        public void CE4596_APatch_CopiesOnlyTheTilesItTouches_AndLeavesTheOldGridAlone()
        {
            var g = Grid(700, 600);   // 3 × 3 tiles, partial east and north
            var p = g.Patched(300, 300, 329, 329, (c, r, z) => z - 5f);   // inside tile (1, 1)
            for (int ty = 0; ty < g.Tiles.TilesY; ty++)
                for (int tx = 0; tx < g.Tiles.TilesX; tx++)
                    Assert.Equal(tx != 1 || ty != 1, p.Tiles.SharesTile(g.Tiles, tx, ty));
            Assert.Equal(F(310, 310) - 5f, p[310, 310]);
            Assert.Equal(F(310, 310), g[310, 310]);       // the old grid is untouched (immutable)
            Assert.Equal(F(299, 310), p[299, 310]);       // just outside the patch
            Assert.Equal(F(330, 329), p[330, 329]);
        }

        /// <summary>
        /// ⭐⭐ <c>CE-4596</c> (V-5) — after patches across tile and block edges, at the grid's north-east corner, and one that LOWERS the highest
        /// post, every block maximum (the sight skip) and the MinZ / MaxZ equal a fresh grid built from the same posts; a patch on the last column
        /// also rewrites the padding that repeats it. 🔴 Red-proof: skip the block recompute in the patched constructor ⇒ the block assertion fails;
        /// keep the old MaxZ ⇒ the lowered-peak assertion fails.
        /// </summary>
        [Fact]
        public void CE4596_APatchedGrid_HasTheBlocksAndRangeOfAFreshOne()
        {
            var g = Grid(700, 600);
            int peakC = 0, peakR = 0; float peak = float.MinValue;
            for (int r = 0; r < 600; r++) for (int c = 0; c < 700; c++) if (g[c, r] > peak) { peak = g[c, r]; peakC = c; peakR = r; }
            var p = g.Patched(250, 240, 270, 275, (c, r, z) => z + 40f)              // across a tile edge (256) and block edges
                     .Patched(690, 590, 699, 599, (c, r, z) => z - 30f)              // the north-east corner
                     .Patched(peakC - 2, peakR - 2, peakC + 2, peakR + 2, (c, r, z) => -100f)   // lower the highest post (and below the lowest)
                     .Patched(512, 0, 512, 0, (c, r, z) => z);                        // a single post on a tile edge, unchanged
            var z2 = new float[700 * 600];
            for (int r = 0; r < 600; r++) for (int c = 0; c < 700; c++) z2[r * 700 + c] = p[c, r];
            var fresh = new TerrainHeightGrid(g.Origin, g.CellSize, 700, 600, z2);
            Assert.Equal(fresh.MinZ, p.MinZ);
            Assert.Equal(fresh.MaxZ, p.MaxZ);
            Assert.True(p.MaxZ < peak, "the lowered peak no longer counts");
            var (bx, by) = p.BlockCount;
            for (int y = 0; y < by; y++) for (int x = 0; x < bx; x++) Assert.Equal(fresh.BlockMaximum(x, y), p.BlockMaximum(x, y));
            Assert.Equal(p[699, 599], p.Tiles[767, 767]);   // the padding past the north-east edge repeats the patched edge post
            Assert.Equal(p.Sample(-10f + 695.5f, 5f + 595.5f), fresh.Sample(-10f + 695.5f, 5f + 595.5f));
        }

        /// <summary>⭐ <c>CE-4596</c> (R-2084) — reading a patched grid allocates nothing (the per-frame ground height of every mover).</summary>
        [Fact]
        public void CE4596_ReadingAPatchedGrid_AllocatesNothing()
        {
            var p = Grid(700, 600).Patched(100, 100, 400, 400, (c, r, z) => z + 1f);
            float sink = 0f;
            void Run() { for (int i = 0; i < 2000; i++) sink += p.Sample(-10f + (i * 7) % 690, 5f + (i * 13) % 590) + p[(i * 3) % 700, (i * 5) % 600]; }
            Run();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int k = 0; k < 20; k++) Run();
            Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.NotEqual(0f, sink);
        }
    }
}
