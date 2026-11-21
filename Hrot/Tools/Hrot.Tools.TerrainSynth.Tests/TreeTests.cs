using System.Globalization;
using Hrot.Tools.TerrainSynth;
using Xunit;

namespace Hrot.Tools.TerrainSynth.Tests;

public class TreeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "terrain-synth-tests", "t" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private sealed record Setup(SynthProfile P, HeightField H, RoadNetwork R, IReadOnlyList<Building> B, IReadOnlyList<(double X, double Y, double Z)> T);

    private static Setup Make(string preset, double spacing, ulong seed)
    {
        var p = SynthOptions.Parse(new[] { "--preset", preset, "--height-spacing", spacing.ToString(CultureInfo.InvariantCulture) }).Resolve();
        var h = new HeightField(p, new Rng(seed));
        var r = RoadNetwork.Generate(p, new Rng(seed));
        var (b, _) = BuildingGenerator.Generate(p, new Rng(seed), h, r);
        var t = TreeGenerator.Generate(p, new Rng(seed), h, r, b);
        return new Setup(p, h, r, b, t);
    }

    private static readonly Lazy<Setup> Tiny1 = new(() => Make("tiny", 4, 1));
    private static readonly Lazy<Setup> Full1 = new(() => Make("full", 8, 1));

    private static double PointSeg(double px, double py, (double X, double Y) a, (double X, double Y) b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y, l2 = dx * dx + dy * dy;
        double t = l2 == 0 ? 0 : Math.Clamp(((px - a.X) * dx + (py - a.Y) * dy) / l2, 0, 1);
        return Math.Sqrt(Math.Pow(a.X + t * dx - px, 2) + Math.Pow(a.Y + t * dy - py, 2));
    }

    private static bool Inside(IReadOnlyList<(double X, double Y)> v, double x, double y)
    {
        bool inside = false;
        for (int i = 0, j = v.Count - 1; i < v.Count; j = i++)
            if ((v[i].Y > y) != (v[j].Y > y) && x < (v[j].X - v[i].X) * (y - v[i].Y) / (v[j].Y - v[i].Y) + v[i].X)
                inside = !inside;
        return inside;
    }

    [Fact]
    public void NoTreeOnRoadOrInBuilding()
    {
        foreach (var s in new[] { Tiny1.Value, Full1.Value })
        {
            Assert.NotEmpty(s.T);
            // buildings in 50 m cells
            var cells = new Dictionary<(int, int), List<Building>>();
            foreach (var b in s.B)
            {
                var xs = b.Footprint.Select(q => q.X); var ys = b.Footprint.Select(q => q.Y);
                for (int cx = (int)Math.Floor((xs.Min() - 3) / 50); cx <= (int)Math.Floor((xs.Max() + 3) / 50); cx++)
                    for (int cy = (int)Math.Floor((ys.Min() - 3) / 50); cy <= (int)Math.Floor((ys.Max() + 3) / 50); cy++)
                    {
                        if (!cells.TryGetValue((cx, cy), out var l)) cells[(cx, cy)] = l = new List<Building>();
                        l.Add(b);
                    }
            }
            foreach (var (x, y, z) in s.T)
            {
                Assert.InRange(x, s.P.MinX, s.P.MaxX);
                Assert.InRange(y, s.P.MinY, s.P.MaxY);
                Assert.False(s.H.IsNoData(x, y));
                Assert.InRange(z, 450.0, 980.0);
                Assert.True(s.R.DistanceToRoad(x, y) >= 2 - 0.02, $"tree at {x},{y} on a road");
                if (!cells.TryGetValue(((int)Math.Floor(x / 50), (int)Math.Floor(y / 50)), out var near)) continue;
                foreach (var b in near)
                {
                    Assert.False(Inside(b.Footprint, x, y), $"tree at {x},{y} inside {b.Id}");
                    for (int i = 0; i + 1 < b.Footprint.Count; i++)
                        Assert.True(PointSeg(x, y, b.Footprint[i], b.Footprint[i + 1]) >= 2 - 0.02, $"tree at {x},{y} within 2 m of {b.Id}");
                }
            }
        }
    }

    [Fact]
    public void Tiny_CountNearTarget()
    {
        var s = Tiny1.Value;
        Assert.InRange(s.T.Count, (int)(s.P.TreeCount * 0.9), (int)(s.P.TreeCount * 1.1));
    }

    [Fact]
    public void Full_Stats()
    {
        var s = Full1.Value;
        int n = s.T.Count;
        Assert.InRange(n, 158400, 193600);

        // woods share: 25 m cells holding at least 45 trees/ha (3 trees in 0.0625 ha)
        int cols = (int)Math.Ceiling(s.P.Width / 25), rows = (int)Math.Ceiling(s.P.Height / 25);
        var count = new int[cols, rows];
        foreach (var (x, y, _) in s.T)
            count[Math.Min(cols - 1, (int)((x - s.P.MinX) / 25)), Math.Min(rows - 1, (int)((y - s.P.MinY) / 25))]++;
        int woodCells = 0;
        long inWoods = 0;
        for (int i = 0; i < cols; i++)
            for (int j = 0; j < rows; j++)
                if (count[i, j] >= 3) { woodCells++; inWoods += count[i, j]; }
        double share = woodCells / (double)(cols * rows);
        Assert.InRange(share, 0.40, 0.50);

        // nearest-neighbour distance of the trees in woods (every 4th tree), 10 m hash
        var hash = new Dictionary<(int, int), List<int>>();
        for (int i = 0; i < n; i++)
        {
            var key = ((int)Math.Floor(s.T[i].X / 10), (int)Math.Floor(s.T[i].Y / 10));
            if (!hash.TryGetValue(key, out var l)) hash[key] = l = new List<int>();
            l.Add(i);
        }
        var nn = new List<double>();
        for (int i = 0; i < n; i += 4)
        {
            var (x, y, _) = s.T[i];
            if (count[Math.Min(cols - 1, (int)((x - s.P.MinX) / 25)), Math.Min(rows - 1, (int)((y - s.P.MinY) / 25))] < 3) continue;
            double best = double.PositiveInfinity;
            int cx = (int)Math.Floor(x / 10), cy = (int)Math.Floor(y / 10);
            for (int dx = -2; dx <= 2; dx++)
                for (int dy = -2; dy <= 2; dy++)
                    if (hash.TryGetValue((cx + dx, cy + dy), out var l))
                        foreach (int k in l)
                            if (k != i) best = Math.Min(best, Math.Sqrt(Math.Pow(s.T[k].X - x, 2) + Math.Pow(s.T[k].Y - y, 2)));
            if (double.IsFinite(best)) nn.Add(best);
        }
        nn.Sort();
        double median = nn[nn.Count / 2];
        Assert.InRange(median, 4.0, 8.0);

        double loneShare = 1.0 - inWoods / (double)n;
        Assert.InRange(loneShare, 0.02, 0.15);
        Console.WriteLine($"STATS trees={n} woodsShare={share:F3} nnMedian={median:F2} loneShare={loneShare:F3} perHaInWoods={inWoods / (woodCells * 0.0625):F1}");
    }

    [Fact]
    public void Generate_IsDeterministic()
    {
        var a = Make("tiny", 4, 5);
        var b = Make("tiny", 4, 5);
        Assert.Equal(a.T, b.T);
        Assert.NotEqual(a.T, Make("tiny", 4, 6).T);
    }

    [Fact]
    public void WriteCsv_Format()
    {
        var s = Tiny1.Value;
        string path = Path.Combine(_dir, "trees.csv");
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("cs-CZ");
            TreeGenerator.WriteCsv(path, s.T);
        }
        finally { CultureInfo.CurrentCulture = old; }
        var lines = File.ReadAllLines(path);
        Assert.Equal("x,y,z_ground", lines[0]);
        Assert.Equal(s.T.Count + 1, lines.Length);
        Assert.Matches(@"^-?\d+\.\d\d,-?\d+\.\d\d,\d+\.\d\d$", lines[1]);
        var f = lines[1].Split(',');
        Assert.Equal(s.T[0].X, double.Parse(f[0], CultureInfo.InvariantCulture), 2);
        Assert.Equal(s.T[0].Z, double.Parse(f[2], CultureInfo.InvariantCulture), 2);
    }
}
