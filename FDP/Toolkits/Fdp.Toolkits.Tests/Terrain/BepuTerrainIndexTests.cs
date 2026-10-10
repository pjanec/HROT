using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using Fdp.Toolkit.Spatial.Bepu;
using Fdp.Toolkit.Terrain;
using Xunit;
using Xunit.Abstractions;

namespace Fdp.Toolkit.Terrain.Tests
{
    /// <summary>
    /// ⭐ CE-1035 Q0 — the Bepu spike (<c>docs/DESIGN_World_Query_Seam.md</c> §5 Q0, WQ-D). Three questions:
    /// ① does a Bepu tree under <see cref="TerrainWorld"/> give IDENTICAL sight / fire answers (R-216)?
    /// ② is it faster on a large terrain? ③ what does a large height grid cost as a Bepu mesh?
    /// Timings are printed, never asserted (the cloud's machine load varies); correctness is asserted.
    /// </summary>
    public sealed class BepuTerrainIndexTests
    {
        private readonly ITestOutputHelper _out;
        public BepuTerrainIndexTests(ITestOutputHelper output) => _out = output;

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Hrot", "Subsystems"))) dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
        }

        private static TerrainWorld Shipped(string name)
        {
            var folder = Path.Combine(RepoRoot(), "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Recipes", "Terrain", name);
            return TerrainWorldParser.Parse(File.ReadAllText(Path.Combine(folder, name + ".world.geojson")), name,
                TerrainAssets.ForFolder(folder));
        }

        private static float MaxTop(TerrainWorld w)
            => Math.Max(w.Prisms.Count == 0 ? 0f : w.Prisms.Max(p => p.TopZ), w.Walkables.Count == 0 ? 0f : w.Walkables.Max(x => x.MaxZ));

        /// <summary>Random segments over the world: a mix of short (1–60 m) and long (corner to corner) ones.</summary>
        private static List<(Vector3 A, Vector3 B)> Segments(TerrainWorld w, int count, int seed, float? fixedLength = null)
        {
            var rng = new Random(seed);
            float top = MaxTop(w) + 5f;
            var min = w.BoundsMin; var max = w.BoundsMax;
            Vector3 P() => new(min.X + (float)rng.NextDouble() * (max.X - min.X), min.Y + (float)rng.NextDouble() * (max.Y - min.Y), (float)rng.NextDouble() * top);
            var list = new List<(Vector3, Vector3)>(count);
            for (int i = 0; i < count; i++)
            {
                var a = P();
                Vector3 b;
                if (fixedLength is float len)
                {
                    float ang = (float)(rng.NextDouble() * Math.PI * 2);
                    b = new Vector3(a.X + MathF.Cos(ang) * len, a.Y + MathF.Sin(ang) * len, (float)rng.NextDouble() * top);
                }
                else if (i % 3 == 0) b = P();
                else
                {
                    float len2 = 1f + (float)rng.NextDouble() * 59f, ang = (float)(rng.NextDouble() * Math.PI * 2);
                    b = new Vector3(a.X + MathF.Cos(ang) * len2, a.Y + MathF.Sin(ang) * len2, (float)rng.NextDouble() * top);
                }
                list.Add((a, b));
            }
            return list;
        }

        /// <summary>Runs every segment with and without the index and asserts the answers are identical.</summary>
        private (int Blocked, int WithCrossings) AssertIdentical(TerrainWorld w, IReadOnlyList<(Vector3 A, Vector3 B)> segs, DoorStates? doors)
        {
            var fireA = new List<TerrainWorld.FireCrossing>();
            var fireB = new List<TerrainWorld.FireCrossing>();
            int blocked = 0, withCrossings = 0;
            var index = w.SpatialIndex;
            foreach (var (a, b) in segs)
            {
                w.SpatialIndex = null;
                bool sa = w.SegmentBlocked(a, b, doors);
                var qa = w.QuerySight(a, b, doors);
                w.QueryFire(a, b, fireA, doors);

                w.SpatialIndex = index;
                bool sb = w.SegmentBlocked(a, b, doors);
                var qb = w.QuerySight(a, b, doors);
                w.QueryFire(a, b, fireB, doors);

                Assert.Equal(sa, sb);
                Assert.Equal(qa.Transmittance, qb.Transmittance);
                Assert.Equal(qa.Crossed, qb.Crossed);
                Assert.Equal(fireA, fireB);
                if (sa) blocked++;
                if (qa.Crossed.Count > 0 || fireA.Count > 0) withCrossings++;
            }
            return (blocked, withCrossings);
        }

        // ── ① identical answers ───────────────────────────────────────────────────────────────────────────────

        [Theory]
        [InlineData("test-town")]
        [InlineData("bt-range")]
        [InlineData("basic-desert")]
        public void Q0_1_OnShippedTerrains_TheIndexGivesIdenticalAnswers(string name)
        {
            var w = Shipped(name);
            using var index = BepuTerrainIndex.AttachTo(w);
            var segs = Segments(w, 6000, seed: 1035);
            var (blockedNull, crossNull) = AssertIdentical(w, segs, doors: null);
            var (blockedDoor, _) = AssertIdentical(w, segs, DoorStates.Authored(w));
            _out.WriteLine($"[Q0_1] {name}: {w.Prisms.Count} prisms, {w.DoorLeaves.Count} door leaves, {w.Walkables.Count} walkables; "
                + $"{segs.Count} segments, {crossNull} cross something, {blockedNull} blocked (doors as authored: {blockedDoor}) — all identical");
            Assert.True(crossNull > segs.Count / 20, "the segments must actually cross terrain, or the comparison proves nothing");
        }

        // ── ② speed on a large terrain ────────────────────────────────────────────────────────────────────────

        /// <summary>A 2 km square town: 80 × 80 blocks of a building and a garden wall each (12 800 prisms), and a few decks.</summary>
        private static TerrainWorld LargeTown(int blocks = 80, float spacing = 25f)
        {
            var rng = new Random(7);
            var prisms = new List<TerrainPrism>();
            for (int i = 0; i < blocks; i++)
                for (int j = 0; j < blocks; j++)
                {
                    float x = i * spacing + 2f, y = j * spacing + 2f;
                    float sx = 8f + (float)rng.NextDouble() * 10f, sy = 8f + (float)rng.NextDouble() * 10f;
                    prisms.Add(Prism(x, y, x + sx, y + sy, 3f + (float)rng.NextDouble() * 20f, TerrainPrismKind.Building));
                    prisms.Add(Prism(x, y + sy + 2f, x + spacing - 4f, y + sy + 2.3f, 2f, TerrainPrismKind.Wall));
                }
            var walkables = new List<TerrainWalkable>();
            for (int k = 0; k < 40; k++)
            {
                float x = (float)rng.NextDouble() * blocks * spacing, y = (float)rng.NextDouble() * blocks * spacing, z = 3f + k % 4;
                var v = new[] { new Vector3(x, y, z), new Vector3(x + 20, y, z), new Vector3(x + 20, y + 20, z), new Vector3(x, y + 20, z) };
                walkables.Add(new TerrainWalkable
                {
                    Kind = TerrainWalkableKind.Slab, Vertices = v, Triangles = new[] { 0, 1, 2, 0, 2, 3 },
                    Min = new Vector2(x, y), Max = new Vector2(x + 20, y + 20), MinZ = z, MaxZ = z,
                });
            }
            return new TerrainWorld
            {
                Name = "large-town", BoundsMin = Vector2.Zero, BoundsMax = new Vector2(blocks * spacing),
                Prisms = prisms, Walkables = walkables,
            };
        }

        private static TerrainPrism Prism(float x0, float y0, float x1, float y1, float height, TerrainPrismKind kind) => new()
        {
            Kind = kind, Footprint = new[] { new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x1, y1), new Vector2(x0, y1) },
            BaseZ = 0f, TopZ = height, Min = new Vector2(x0, y0), Max = new Vector2(x1, y1), Triangles = new[] { 0, 1, 2, 0, 2, 3 },
        };

        private static double NsPer(int n, Action body)
        {
            body();                                   // warm up
            var sw = Stopwatch.StartNew();
            body();
            return sw.Elapsed.TotalMilliseconds * 1e6 / n;
        }

        [Fact]
        public void Q0_2_OnALargeTown_TheIndexIsIdentical_AndTheTimingsArePrinted()
        {
            var w = LargeTown();
            var sw = Stopwatch.StartNew();
            using var index = BepuTerrainIndex.AttachTo(w);
            double buildMs = sw.Elapsed.TotalMilliseconds;

            var mixed = Segments(w, 1500, seed: 11);
            var (blocked, crossing) = AssertIdentical(w, mixed, doors: null);
            Assert.True(crossing > mixed.Count / 4);

            var fire = new List<TerrainWorld.FireCrossing>();
            foreach (var (label, segs) in new[] { ("100 m sight lines", Segments(w, 2000, seed: 12, fixedLength: 100f)),
                                                  ("400 m sight lines", Segments(w, 2000, seed: 13, fixedLength: 400f)) })
            {
                double scanLos, idxLos, scanFire, idxFire;
                w.SpatialIndex = null;
                scanLos = NsPer(segs.Count, () => { foreach (var (a, b) in segs) w.SegmentBlocked(a, b); });
                scanFire = NsPer(segs.Count, () => { foreach (var (a, b) in segs) w.QueryFire(a, b, fire); });
                w.SpatialIndex = index;
                idxLos = NsPer(segs.Count, () => { foreach (var (a, b) in segs) w.SegmentBlocked(a, b); });
                idxFire = NsPer(segs.Count, () => { foreach (var (a, b) in segs) w.QueryFire(a, b, fire); });
                _out.WriteLine($"[Q0_2] {label}: SegmentBlocked scan {scanLos / 1000:F1} µs → index {idxLos / 1000:F1} µs (×{scanLos / idxLos:F0}); "
                    + $"QueryFire scan {scanFire / 1000:F1} µs → index {idxFire / 1000:F1} µs (×{scanFire / idxFire:F0})");
            }

            // R-220 — once warm, an indexed sight query allocates nothing
            var probe = mixed.Take(200).ToList();
            foreach (var (a, b) in probe) w.SegmentBlocked(a, b);
            long before = GC.GetAllocatedBytesForCurrentThread();
            foreach (var (a, b) in probe) w.SegmentBlocked(a, b);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            _out.WriteLine($"[Q0_2] large town: {w.Prisms.Count} prisms + {w.Walkables.Count} slabs; index build {buildMs:F0} ms; "
                + $"{blocked}/{mixed.Count} mixed segments blocked, all identical; {allocated} bytes allocated by 200 warm indexed SegmentBlocked calls");
            Assert.Equal(0, allocated);
        }

        // ── ③ a large height grid as a Bepu mesh ──────────────────────────────────────────────────────────────

        [Fact]
        public void Q0_3_ALargeHeightGrid_AsABepuMesh_MatchesTheArithmetic_AndItsCostIsPrinted()
        {
            const int n = 1001;            // 2 km at 2 m cells
            const float cell = 2f;
            var z = new float[n * n];
            for (int r = 0; r < n; r++)
                for (int c = 0; c < n; c++)
                {
                    float x = c * cell, y = r * cell;
                    z[r * n + c] = 30f * MathF.Sin(x / 180f) * MathF.Cos(y / 220f) + 6f * MathF.Sin(x / 37f + y / 53f);   // hills and ripples
                }

            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            long privBefore = Process.GetCurrentProcess().PrivateMemorySize64;
            var sw = Stopwatch.StartNew();
            using var grid = new BepuHeightGridProbe(Vector2.Zero, cell, n, n, z);
            double buildMs = sw.Elapsed.TotalMilliseconds;
            GC.Collect();
            long privAfter = Process.GetCurrentProcess().PrivateMemorySize64;

            var rng = new Random(5);
            float span = (n - 1) * cell;
            var pts = Enumerable.Range(0, 20_000).Select(_ => new Vector2((float)rng.NextDouble() * span, (float)rng.NextDouble() * span)).ToArray();
            float worst = 0f;
            foreach (var p in pts) worst = MathF.Max(worst, MathF.Abs(grid.HeightByRay(p.X, p.Y) - grid.HeightAt(p.X, p.Y)));
            Assert.True(worst < 1e-2f, $"the mesh and the arithmetic must describe one surface; worst difference {worst} m");

            double arith = NsPer(pts.Length, () => { foreach (var p in pts) grid.HeightAt(p.X, p.Y); });
            double byRay = NsPer(pts.Length, () => { foreach (var p in pts) grid.HeightByRay(p.X, p.Y); });

            // the ground trace: sight lines 1.8 m above the surface at both ends, 300 m long
            var segs = Enumerable.Range(0, 5000).Select(_ =>
            {
                var a = new Vector2((float)rng.NextDouble() * (span - 600) + 300, (float)rng.NextDouble() * (span - 600) + 300);
                float ang = (float)(rng.NextDouble() * Math.PI * 2);
                var b = a + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * 300f;
                return (A: new Vector3(a, grid.HeightAt(a.X, a.Y) + 1.8f), B: new Vector3(b, grid.HeightAt(b.X, b.Y) + 1.8f));
            }).ToArray();
            int disagree = 0, hidden = 0;
            foreach (var (a, b) in segs)
            {
                bool m = grid.SegmentHitsGround(a, b), s = grid.SegmentHitsGroundByScan(a, b);
                if (m != s) disagree++;
                if (m) hidden++;
            }
            double meshTrace = NsPer(segs.Length, () => { foreach (var (a, b) in segs) grid.SegmentHitsGround(a, b); });
            double scanTrace = NsPer(segs.Length, () => { foreach (var (a, b) in segs) grid.SegmentHitsGroundByScan(a, b); });

            _out.WriteLine($"[Q0_3] {n}×{n} grid ({grid.TriangleCount:N0} triangles, {span / 1000:F1} km): build {buildMs:F0} ms, "
                + $"process private memory +{(privAfter - privBefore) / (1024 * 1024)} MB; height worst diff {worst * 1000:F2} mm");
            _out.WriteLine($"[Q0_3] height: arithmetic {arith:F0} ns, Bepu ray down {byRay:F0} ns");
            _out.WriteLine($"[Q0_3] 300 m ground trace: Bepu mesh {meshTrace / 1000:F1} µs, triangle scan of the box {scanTrace / 1000:F1} µs; "
                + $"{hidden}/{segs.Length} hidden by a hill; {disagree} disagreements");
            Assert.True(hidden > 0 && hidden < segs.Length, "the trace must see both hidden and clear lines");
            Assert.True(disagree <= segs.Length / 1000, $"mesh trace and scan must agree (grazing hits aside): {disagree}");
        }
    }
}
