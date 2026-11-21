using System.Globalization;
using System.Text.Json;
using Hrot.Tools.TerrainSynth;
using Xunit;

namespace Hrot.Tools.TerrainSynth.Tests;

public class BuildingTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "terrain-synth-tests", "b" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private sealed record Setup(SynthProfile P, HeightField H, RoadNetwork R, IReadOnlyList<Building> B, IReadOnlyList<Opening> U);

    private static Setup Make(string preset, double spacing, ulong seed)
    {
        var p = SynthOptions.Parse(new[] { "--preset", preset, "--height-spacing", spacing.ToString(CultureInfo.InvariantCulture) }).Resolve();
        var h = new HeightField(p, new Rng(seed));
        var r = RoadNetwork.Generate(p, new Rng(seed));
        var (b, u) = BuildingGenerator.Generate(p, new Rng(seed), h, r);
        return new Setup(p, h, r, b, u);
    }

    private static readonly Lazy<Setup> Tiny1 = new(() => Make("tiny", 4, 1));
    private static readonly Lazy<Setup> Full1 = new(() => Make("full", 8, 1));

    // ---- independent geometry helpers ----

    private static List<(double X, double Y)> Ring(Building b) => b.Footprint.Take(b.Footprint.Count - 1).ToList();

    private static double SignedArea(List<(double X, double Y)> v)
    {
        double a = 0;
        for (int i = 0; i < v.Count; i++)
        {
            var p = v[i]; var q = v[(i + 1) % v.Count];
            a += p.X * q.Y - q.X * p.Y;
        }
        return a / 2;
    }

    private static bool Inside(List<(double X, double Y)> v, double x, double y)
    {
        bool inside = false;
        for (int i = 0, j = v.Count - 1; i < v.Count; j = i++)
            if ((v[i].Y > y) != (v[j].Y > y) && x < (v[j].X - v[i].X) * (y - v[i].Y) / (v[j].Y - v[i].Y) + v[i].X)
                inside = !inside;
        return inside;
    }

    private static double PointSeg(double px, double py, (double X, double Y) a, (double X, double Y) b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y, l2 = dx * dx + dy * dy;
        double t = l2 == 0 ? 0 : Math.Clamp(((px - a.X) * dx + (py - a.Y) * dy) / l2, 0, 1);
        return Math.Sqrt(Math.Pow(a.X + t * dx - px, 2) + Math.Pow(a.Y + t * dy - py, 2));
    }

    private static bool SegCross((double X, double Y) a, (double X, double Y) b, (double X, double Y) c, (double X, double Y) d)
    {
        double O((double X, double Y) p, (double X, double Y) q, (double X, double Y) s) => (q.X - p.X) * (s.Y - p.Y) - (q.Y - p.Y) * (s.X - p.X);
        return O(a, b, c) * O(a, b, d) < 0 && O(c, d, a) * O(c, d, b) < 0;
    }

    // distance between two polygons (0 when they overlap)
    private static double Gap(List<(double X, double Y)> a, List<(double X, double Y)> b)
    {
        double best = double.PositiveInfinity;
        for (int i = 0; i < a.Count; i++)
        {
            var a0 = a[i]; var a1 = a[(i + 1) % a.Count];
            for (int j = 0; j < b.Count; j++)
            {
                var b0 = b[j]; var b1 = b[(j + 1) % b.Count];
                if (SegCross(a0, a1, b0, b1)) return 0;
                best = Math.Min(best, Math.Min(Math.Min(PointSeg(a0.X, a0.Y, b0, b1), PointSeg(a1.X, a1.Y, b0, b1)),
                                               Math.Min(PointSeg(b0.X, b0.Y, a0, a1), PointSeg(b1.X, b1.Y, a0, a1))));
            }
        }
        if (Inside(a, b[0].X, b[0].Y) || Inside(b, a[0].X, a[0].Y)) return 0;
        return best;
    }

    // ---- tests ----

    [Fact]
    public void Footprints_ClosedCcwNoOverlapInsideBoundsOffRoads()
    {
        var s = Tiny1.Value;
        Assert.NotEmpty(s.B);
        foreach (var b in s.B)
        {
            Assert.True(b.Footprint.Count >= 5);
            Assert.Equal(b.Footprint[0], b.Footprint[^1]);
            var v = Ring(b);
            Assert.True(SignedArea(v) > 0, $"{b.Id} not counter-clockwise");
            foreach (var (x, y) in v)
            {
                Assert.InRange(x, s.P.MinX, s.P.MaxX);
                Assert.InRange(y, s.P.MinY, s.P.MaxY);
                Assert.True(s.R.DistanceToRoad(x, y) >= 3 - 1e-6, $"{b.Id} corner too close to a road");
            }
            for (int i = 0; i < v.Count; i++)
            {
                var q = v[(i + 1) % v.Count];
                Assert.True(s.R.DistanceToRoad((v[i].X + q.X) / 2, (v[i].Y + q.Y) / 2) >= 3 - 1e-6, $"{b.Id} edge too close to a road");
            }
        }
        for (int i = 0; i < s.B.Count; i++)
            for (int j = i + 1; j < s.B.Count; j++)
                Assert.True(Gap(Ring(s.B[i]), Ring(s.B[j])) >= 2 - 1e-6, $"{s.B[i].Id} and {s.B[j].Id} closer than 2 m");
    }

    [Fact]
    public void Ids_Unique()
    {
        foreach (var s in new[] { Tiny1.Value, Full1.Value })
        {
            Assert.Equal(s.B.Count, s.B.Select(b => b.Id).Distinct().Count());
            Assert.All(s.B, b => Assert.Matches("^[0-9a-f]{12}$", b.Id));
        }
    }

    [Fact]
    public void StoreyZ_AscendingFromBase()
    {
        foreach (var b in Tiny1.Value.B.Concat(Full1.Value.B))
        {
            Assert.InRange(b.StoreyZ.Count, 1, 6);
            Assert.Equal(b.BaseZ, b.StoreyZ[0], 6);
            for (int i = 1; i < b.StoreyZ.Count; i++) Assert.True(b.StoreyZ[i] > b.StoreyZ[i - 1]);
            Assert.True(b.Height > b.StoreyZ[^1] - b.BaseZ);
        }
    }

    [Fact]
    public void Doors_OnTheirFloor_Windows_FloorPlusOne()
    {
        foreach (var b in Tiny1.Value.B.Concat(Full1.Value.B))
        {
            Assert.Contains(b.Openings, o => o.Type == "door" && o.Storey == 0);
            foreach (var o in b.Openings)
            {
                double floor = b.StoreyZ[o.Storey];
                if (o.Type == "door") Assert.InRange(o.Z, floor - 0.01, floor + 0.01);
                else
                {
                    Assert.Equal("window", o.Type);
                    Assert.InRange(o.Z, floor + 1.0 - 0.01, floor + 1.0 + 0.01);
                }
            }
        }
    }

    [Fact]
    public void Headings_PointOutward()
    {
        int total = 0;
        foreach (var b in Tiny1.Value.B.Concat(Full1.Value.B))
        {
            var v = Ring(b);
            foreach (var o in b.Openings)
            {
                double rad = o.Heading * Math.PI / 180;
                Assert.False(Inside(v, o.X + Math.Sin(rad), o.Y + Math.Cos(rad)), $"{b.Id} {o.Type} heading {o.Heading} points inward");
                Assert.InRange(o.Heading, 0.0, 360.0);
                total++;
            }
        }
        Assert.True(total > 0);
    }

    [Fact]
    public void Roofs_PitchedHaveRidgeAboveEaves()
    {
        bool anyPitched = false, anyFlat = false;
        foreach (var b in Tiny1.Value.B.Concat(Full1.Value.B))
        {
            if (b.Roof == "pitched")
            {
                anyPitched = true;
                Assert.NotNull(b.RidgeZ);
                Assert.True(b.RidgeZ > b.BaseZ + b.Height);
            }
            else
            {
                anyFlat = true;
                Assert.Equal("flat", b.Roof);
                Assert.Null(b.RidgeZ);
            }
        }
        Assert.True(anyPitched && anyFlat);
    }

    [Fact]
    public void NoNaN_NearNoData()
    {
        foreach (var s in new[] { Tiny1.Value, Full1.Value })
        {
            Assert.NotEmpty(s.B);
            foreach (var b in s.B)
            {
                Assert.True(double.IsFinite(b.BaseZ));
                Assert.True(b.BaseZ >= 450);
                foreach (var (x, y) in Ring(b)) Assert.False(s.H.IsNoData(x, y));
            }
        }
        // on tiny the nodata patch is 20 x 20 m at (100,100): a building placed within 30 m of it exercises the nodata checks
        var t = Tiny1.Value;
        var rect = t.P.NodataRects[0];
        Assert.Contains(t.B, b => Ring(b).Any(v =>
            Math.Max(Math.Max(rect.MinX - v.X, v.X - rect.MaxX), 0) < 30 && Math.Max(Math.Max(rect.MinY - v.Y, v.Y - rect.MaxY), 0) < 30));
    }

    [Fact]
    public void Full_Stats()
    {
        var s = Full1.Value;
        int n = s.B.Count;
        Assert.InRange(n, 1620, 1980);

        int rects = s.B.Count(b => b.Footprint.Count == 5);
        Assert.InRange((double)rects / n, 0.88, 0.98);

        var want = new[] { 0.17, 0.40, 0.32, 0.06, 0.05, 0.003 };
        for (int k = 1; k <= 4; k++)
            Assert.InRange(s.B.Count(b => b.StoreyZ.Count == k) * 100.0 / n, want[k - 1] * 100 - 5, want[k - 1] * 100 + 5);
        Assert.InRange(s.B.Count(b => b.StoreyZ.Count >= 5) * 100.0 / n, 0, 5.3 + 5);

        double doors = s.B.Sum(b => b.Openings.Count(o => o.Type == "door")) / (double)n;
        double windows = s.B.Sum(b => b.Openings.Count(o => o.Type == "window")) / (double)n;
        Assert.InRange(doors, 2.2, 3.0);
        Assert.InRange(windows, 30, 45);
        Assert.InRange(s.B.Count(b => b.Roof == "pitched") / (double)n, 0.68, 0.78);
        Assert.InRange(s.U.Count / (double)n, 0.8, 1.2);

        Console.WriteLine($"STATS n={n} rect={(double)rects / n:F3} doors={doors:F2} windows={windows:F2} pitched={s.B.Count(b => b.Roof == "pitched") / (double)n:F3} unassigned={s.U.Count / (double)n:F2} storeys%={string.Join("/", Enumerable.Range(1, 6).Select(k => (s.B.Count(b => b.StoreyZ.Count == k) * 100.0 / n).ToString("F1", CultureInfo.InvariantCulture)))} tinyCount={Tiny1.Value.B.Count}");
    }

    [Fact]
    public void Generate_IsDeterministic()
    {
        var a = Make("tiny", 4, 5);
        var b = Make("tiny", 4, 5);
        Assert.Equal(a.B.Select(x => x.Id), b.B.Select(x => x.Id));
        Assert.Equal(a.B.SelectMany(x => x.Openings), b.B.SelectMany(x => x.Openings));
        Assert.Equal(a.U, b.U);
    }

    [Fact]
    public void Unassigned_AreOutsideTheirFootprints()
    {
        var s = Tiny1.Value;
        Assert.NotEmpty(s.U);
        foreach (var o in s.U)
        {
            Assert.InRange(o.X, s.P.MinX, s.P.MaxX);
            Assert.InRange(o.Y, s.P.MinY, s.P.MaxY);
            Assert.DoesNotContain(s.B, b => Inside(Ring(b), o.X, o.Y));
        }
    }

    [Fact]
    public void GeoJsonWriter_Format()
    {
        var old = CultureInfo.CurrentCulture;
        string bPath = Path.Combine(_dir, "buildings.geojson"), uPath = Path.Combine(_dir, "unassigned.geojson"), wPath = Path.Combine(_dir, "walls.geojson");
        Directory.CreateDirectory(_dir);
        var s = Tiny1.Value;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("cs-CZ");
            GeoJsonWriter.WriteBuildings(bPath, s.B);
            GeoJsonWriter.WriteUnassigned(uPath, s.U);
            GeoJsonWriter.WriteEmpty(wPath, "none");
        }
        finally { CultureInfo.CurrentCulture = old; }

        string text = File.ReadAllText(bPath);
        Assert.DoesNotContain("\n", text);
        var root = JsonDocument.Parse(text).RootElement;
        Assert.Equal("FeatureCollection", root.GetProperty("type").GetString());
        var inp = root.GetProperty("hrotInput");
        Assert.Equal("absolute", inp.GetProperty("z").GetString());
        Assert.Contains("compass degrees", inp.GetProperty("heading").GetString());
        Assert.Contains("local metres", inp.GetProperty("frame").GetString());
        var feats = root.GetProperty("features").EnumerateArray().ToList();
        Assert.Equal(s.B.Count, feats.Count);
        for (int i = 0; i < feats.Count; i++)
        {
            var f = feats[i];
            var b = s.B[i];
            Assert.Equal("Polygon", f.GetProperty("geometry").GetProperty("type").GetString());
            var ring = f.GetProperty("geometry").GetProperty("coordinates")[0].EnumerateArray().ToList();
            Assert.Equal(b.Footprint.Count, ring.Count);
            Assert.Equal(ring[0][0].GetDouble(), ring[^1][0].GetDouble());
            var props = f.GetProperty("properties").EnumerateObject().Select(x => x.Name).ToList();
            var expect = new List<string> { "id", "baseZ", "height", "roof" };
            if (b.Roof == "pitched") expect.Add("ridgeZ");
            expect.Add("storeyZ");
            expect.Add("openings");
            Assert.Equal(expect, props);
            var o0 = f.GetProperty("properties").GetProperty("openings")[0];
            Assert.Equal(new[] { "type", "x", "y", "z", "heading", "storey" }, o0.EnumerateObject().Select(x => x.Name));
        }

        var un = JsonDocument.Parse(File.ReadAllText(uPath)).RootElement;
        var uf = un.GetProperty("features").EnumerateArray().ToList();
        Assert.Equal(s.U.Count, uf.Count);
        Assert.Equal("Point", uf[0].GetProperty("geometry").GetProperty("type").GetString());
        Assert.Equal(3, uf[0].GetProperty("geometry").GetProperty("coordinates").GetArrayLength());
        Assert.Equal(new[] { "type", "heading" }, uf[0].GetProperty("properties").EnumerateObject().Select(x => x.Name));

        var w = JsonDocument.Parse(File.ReadAllText(wPath)).RootElement;
        Assert.Equal(0, w.GetProperty("features").GetArrayLength());
        Assert.Contains("hrotInput", File.ReadAllText(wPath));
    }
}
