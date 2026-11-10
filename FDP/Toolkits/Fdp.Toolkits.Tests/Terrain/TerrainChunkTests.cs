using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.World;
using Xunit;

namespace Fdp.Toolkit.Terrain.Tests
{
    /// <summary>
    /// ⭐ CE-3423 (docs/DESIGN_Map_3D_Mode.md §6ah) — the terrain BY CHUNK: level 0 of every chunk, plus what stands in each chunk, plus the outside, is EXACTLY the
    /// whole tagged build (<see cref="TerrainWorldQuery.BuildTagged"/>) — the same triangles, kinds and materials — so near the camera the 3-D view is unchanged (L-1, L-5).
    /// The skirts (L-4) are the only extra triangles, and they hang on interior edges only. A coarse level cuts ground only under water (L-3).
    /// 🔴 Red-proof (compiles): <c>AddGroundCell</c>'s cut dropped from level 0 ⇒ the equality reds on every terrain with a building.
    /// </summary>
    public class TerrainChunkTests
    {
        private static TerrainWorld Load(string name)
        {
            var folder = Path.Combine(ContentLayout.TerrainAssets(ContentLayout.TryFindSourceTree()!), name);
            var world = Directory.GetFiles(folder, "*.world.geojson").Single();
            return TerrainWorldParser.Parse(File.ReadAllText(world), name, TerrainAssets.ForFolder(folder));
        }

        private static string Key(Vector3 a, Vector3 b, Vector3 c, TerrainSurfaceKind k, string? m)
            => string.Create(CultureInfo.InvariantCulture, $"{a.X:F3},{a.Y:F3},{a.Z:F3}|{b.X:F3},{b.Y:F3},{b.Z:F3}|{c.X:F3},{c.Y:F3},{c.Z:F3}|{k}|{m}");

        private static bool Vertical(Vector3 a, Vector3 b, Vector3 c)
        {
            var n = Vector3.Cross(b - a, c - a);
            return n.Length() > 1e-9f && System.MathF.Abs(n.Z) / n.Length() < 1e-4f;
        }

        [Theory]
        [InlineData("demo-city")]
        [InlineData("test-town")]
        [InlineData("highway-rest")]
        [InlineData("bt-range")]
        public void EveryChunksLevelZero_PlusItsObjects_PlusTheOutside_IsTheWholeTaggedBuild(string terrain)
        {
            var q = new TerrainWorldQuery(Load(terrain));
            q.BuildTagged(out var v, out var idx, out var kinds, out var mats);
            var whole = new List<string>(kinds.Length);
            for (int t = 0; t < kinds.Length; t++) whole.Add(Key(v[idx[3 * t]], v[idx[3 * t + 1]], v[idx[3 * t + 2]], kinds[t], mats[t]));

            var chunked = new List<string>(kinds.Length);
            var layout = q.Chunks;
            int skirts = 0, painted = 0;
            for (int cy = 0; cy < layout.Ny; cy++)
                for (int cx = 0; cx < layout.Nx; cx++)
                {
                    q.BuildChunk(cx, cy, 0, TerrainChunkPart.ChunkGround, out var gv, out var gi, out var gk, out var gm);
                    for (int t = 0; t < gk.Length; t++)
                    {
                        Vector3 a = gv[gi[3 * t]], b = gv[gi[3 * t + 1]], c = gv[gi[3 * t + 2]];
                        if (Vertical(a, b, c)) { skirts++; Assert.Equal(TerrainSurfaceKind.Ground, gk[t]); continue; }   // a skirt: ground, never a wall
                        chunked.Add(Key(a, b, c, gk[t], gm[t]));
                    }
                    q.BuildChunk(cx, cy, 0, TerrainChunkPart.ChunkObjects, out var ov, out var oi, out var ok, out var om);
                    for (int t = 0; t < ok.Length; t++)
                    {
                        if (om[t] == TerrainWorldMesh.PaintedWindowMaterial) { painted++; continue; }   // §6ai R-5: painted windows are the DRAWING's only (never in the world's soup — the navmesh bakes from it)
                        chunked.Add(Key(ov[oi[3 * t]], ov[oi[3 * t + 1]], ov[oi[3 * t + 2]], ok[t], om[t]));
                    }
                }
            q.BuildOutside(out var av, out var ai, out var ak, out var am);
            for (int t = 0; t < ak.Length; t++) chunked.Add(Key(av[ai[3 * t]], av[ai[3 * t + 1]], av[ai[3 * t + 2]], ak[t], am[t]));

            whole.Sort(System.StringComparer.Ordinal); chunked.Sort(System.StringComparer.Ordinal);
            Assert.Equal(whole.Count, chunked.Count);
            Assert.Equal(whole, chunked);
            if (layout.Nx * layout.Ny > 1) Assert.True(skirts > 0, "interior chunk edges carry skirts");
        }

        [Fact]
        public void TheChunkGrid_IsAWholeNumberOfGroundCells_AndItsLevelsStayAtMost16m()
        {
            foreach (var name in new[] { "demo-city", "highway-rest", "bt-range" })
            {
                var l = new TerrainWorldQuery(Load(name)).Chunks;
                float per = l.ChunkSize / l.Cell;
                Assert.Equal(System.MathF.Round(per), per, 3);
                Assert.InRange(l.Levels, 1, 4);
                Assert.True(l.Cell * (1 << (l.Levels - 1)) <= TerrainWorldMesh.MaxLevelCell + 1e-3f, $"{name}: the coarsest level is ≤ 16 m");
                Assert.True(l.Nx >= 1 && l.Ny >= 1);
            }
        }

        /// <summary>L-3 / L-4 — a coarse level samples every 2^k cells, keeps ground under buildings (cut only under water), and its skirts hang on interior edges only.</summary>
        [Fact]
        public void ACoarseLevel_HasAQuarterOfTheCells_CutsOnlyUnderWater_AndSkirtsOnlyInteriorEdges()
        {
            var w = Load("demo-city");
            var q = new TerrainWorldQuery(w);
            var l = q.Chunks;
            Assert.True(l.Levels >= 2, "demo-city has a coarser level");
            int fine = 0, coarse = 0, outerSkirts = 0;
            for (int cy = 0; cy < l.Ny; cy++)
                for (int cx = 0; cx < l.Nx; cx++)
                    for (int level = 0; level < 2; level++)
                    {
                        q.BuildChunk(cx, cy, level, TerrainChunkPart.ChunkGround, out var v, out var i, out _, out _);
                        for (int t = 0; t < i.Length / 3; t++)
                        {
                            Vector3 a = v[i[3 * t]], b = v[i[3 * t + 1]], c = v[i[3 * t + 2]];
                            if (Vertical(a, b, c))
                            {
                                // a skirt's x or y is a chunk edge that is NOT the grid's outer edge
                                var lo = l.ChunkMin(0, 0); var hi = l.ChunkMax(l.Nx - 1, l.Ny - 1);
                                bool onOuter = (System.MathF.Abs(a.X - b.X) < 1e-3f && (System.MathF.Abs(a.X - lo.X) < 1e-3f || a.X >= w.BoundsMax.X - 1e-3f))
                                            || (System.MathF.Abs(a.Y - b.Y) < 1e-3f && (System.MathF.Abs(a.Y - lo.Y) < 1e-3f || a.Y >= w.BoundsMax.Y - 1e-3f));
                                if (onOuter) outerSkirts++;
                                continue;
                            }
                            if (level == 0) fine++; else coarse++;
                        }
                    }
            Assert.Equal(0, outerSkirts);
            // level 1 has about a quarter of level 0's cells — MORE than a quarter, as it does not cut under buildings
            Assert.InRange(coarse, fine / 5, fine / 2);

            // under a building: level 0 has no ground there, level 1 does (the house hides it); under water neither does
            var house = w.Prisms.First(p => p.Panel < 0 && (p.Max.X - p.Min.X) > 2 * l.Cell * 2 && (p.Max.Y - p.Min.Y) > 2 * l.Cell * 2);
            var centre = (house.Min + house.Max) * 0.5f;
            Assert.False(HasGroundAt(q, l, centre, 0));
            Assert.True(HasGroundAt(q, l, centre, 1));
        }

        private static bool HasGroundAt(TerrainWorldQuery q, TerrainChunkLayout l, Vector2 p, int level)
        {
            int cx = System.Math.Clamp((int)System.MathF.Floor((p.X - l.Origin.X) / l.ChunkSize), 0, l.Nx - 1);
            int cy = System.Math.Clamp((int)System.MathF.Floor((p.Y - l.Origin.Y) / l.ChunkSize), 0, l.Ny - 1);
            q.BuildChunk(cx, cy, level, TerrainChunkPart.ChunkGround, out var v, out var i, out _, out _);
            for (int t = 0; t < i.Length / 3; t++)
            {
                Vector3 a = v[i[3 * t]], b = v[i[3 * t + 1]], c = v[i[3 * t + 2]];
                if (Vertical(a, b, c)) continue;
                if (PolygonMath.Contains(new[] { new Vector2(a.X, a.Y), new Vector2(b.X, b.Y), new Vector2(c.X, c.Y) }, p)) return true;
            }
            return false;
        }
    }
}
