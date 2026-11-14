using System.Globalization;
using Hrot.Tools.TerrainSynth;
using Xunit;

namespace Hrot.Tools.TerrainSynth.Tests;

public class TerraceTests
{
    private sealed record Setup(SynthProfile P, RoadNetwork R, IReadOnlyList<Building> B, IReadOnlyList<Opening> U);

    private static Setup Make(string preset, double spacing, ulong seed, bool terraces)
    {
        var p = SynthOptions.Parse(new[] { "--preset", preset, "--height-spacing", spacing.ToString(CultureInfo.InvariantCulture) }).Resolve();
        var h = new HeightField(p, new Rng(seed));
        var r = RoadNetwork.Generate(p, new Rng(seed));
        var (b, u) = BuildingGenerator.Generate(p, new Rng(seed), h, r, new BuildingOptions { Terraces = terraces });
        return new Setup(p, r, b, u);
    }

    private static readonly Lazy<Setup> Full = new(() => Make("full", 8, 1, true));

    private static List<(double X, double Y)> Ring(Building b) => b.Footprint.Take(b.Footprint.Count - 1).ToList();

    private static ((double, double), (double, double)) Canon((double X, double Y) a, (double X, double Y) b) =>
        a.X < b.X || (a.X == b.X && a.Y <= b.Y) ? ((a.X, a.Y), (b.X, b.Y)) : ((b.X, b.Y), (a.X, a.Y));

    // edges used by two or more buildings: identical vertex coordinates
    private static Dictionary<((double, double), (double, double)), List<int>> SharedEdges(IReadOnlyList<Building> bs)
    {
        var map = new Dictionary<((double, double), (double, double)), List<int>>();
        for (int i = 0; i < bs.Count; i++)
        {
            var v = Ring(bs[i]);
            for (int k = 0; k < v.Count; k++)
            {
                var key = Canon(v[k], v[(k + 1) % v.Count]);
                if (!map.TryGetValue(key, out var l)) map[key] = l = new List<int>();
                l.Add(i);
            }
        }
        return map.Where(kv => kv.Value.Count > 1).ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    private static double PointSeg(double px, double py, (double X, double Y) a, (double X, double Y) b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y, l2 = dx * dx + dy * dy;
        double t = l2 == 0 ? 0 : Math.Clamp(((px - a.X) * dx + (py - a.Y) * dy) / l2, 0, 1);
        return Math.Sqrt(Math.Pow(a.X + t * dx - px, 2) + Math.Pow(a.Y + t * dy - py, 2));
    }

    private static bool Inside(List<(double X, double Y)> v, double x, double y)
    {
        bool inside = false;
        for (int i = 0, j = v.Count - 1; i < v.Count; j = i++)
            if ((v[i].Y > y) != (v[j].Y > y) && x < (v[j].X - v[i].X) * (y - v[i].Y) / (v[j].Y - v[i].Y) + v[i].X)
                inside = !inside;
        return inside;
    }

    private static bool SegCross((double X, double Y) a, (double X, double Y) b, (double X, double Y) c, (double X, double Y) d)
    {
        double O((double X, double Y) p, (double X, double Y) q, (double X, double Y) s) => (q.X - p.X) * (s.Y - p.Y) - (q.Y - p.Y) * (s.X - p.X);
        return O(a, b, c) * O(a, b, d) < -1e-9 && O(c, d, a) * O(c, d, b) < -1e-9;
    }

    private static double Gap(List<(double X, double Y)> a, List<(double X, double Y)> b)
    {
        double best = double.PositiveInfinity;
        for (int i = 0; i < a.Count; i++)
            for (int j = 0; j < b.Count; j++)
            {
                var a0 = a[i]; var a1 = a[(i + 1) % a.Count]; var b0 = b[j]; var b1 = b[(j + 1) % b.Count];
                if (SegCross(a0, a1, b0, b1)) return 0;
                best = Math.Min(best, Math.Min(Math.Min(PointSeg(a0.X, a0.Y, b0, b1), PointSeg(a1.X, a1.Y, b0, b1)),
                                               Math.Min(PointSeg(b0.X, b0.Y, a0, a1), PointSeg(b1.X, b1.Y, a0, a1))));
            }
        return best;
    }

    private static IEnumerable<(int A, int B)> NearPairs(IReadOnlyList<Building> bs)
    {
        var cells = new Dictionary<(int, int), List<int>>();
        for (int i = 0; i < bs.Count; i++)
        {
            var v = Ring(bs[i]);
            for (int cx = (int)Math.Floor((v.Min(q => q.X) - 3) / 50); cx <= (int)Math.Floor((v.Max(q => q.X) + 3) / 50); cx++)
                for (int cy = (int)Math.Floor((v.Min(q => q.Y) - 3) / 50); cy <= (int)Math.Floor((v.Max(q => q.Y) + 3) / 50); cy++)
                {
                    if (!cells.TryGetValue((cx, cy), out var l)) cells[(cx, cy)] = l = new List<int>();
                    l.Add(i);
                }
        }
        var seen = new HashSet<(int, int)>();
        foreach (var l in cells.Values)
            for (int i = 0; i < l.Count; i++)
                for (int j = i + 1; j < l.Count; j++)
                {
                    var key = (Math.Min(l[i], l[j]), Math.Max(l[i], l[j]));
                    if (seen.Add(key)) yield return key;
                }
    }

    [Fact]
    public void Rows_ExistAboutTenPercentOfTheHouses()
    {
        var s = Full.Value;
        var shared = SharedEdges(s.B);
        var inRow = new HashSet<int>(shared.Values.SelectMany(x => x));
        double share = inRow.Count / (double)s.B.Count;
        Assert.InRange(share, 0.08, 0.12);
        Assert.InRange(s.B.Count, 1620, 1980);

        // every shared edge belongs to exactly two houses; the houses of one row form chains of 3 to 6
        Assert.All(shared.Values, l => Assert.Equal(2, l.Count));
        var parent = Enumerable.Range(0, s.B.Count).ToArray();
        int Find(int a) { while (parent[a] != a) { parent[a] = parent[parent[a]]; a = parent[a]; } return a; }
        foreach (var l in shared.Values) parent[Find(l[0])] = Find(l[1]);
        var sizes = inRow.GroupBy(Find).Select(g => g.Count()).ToList();
        Assert.All(sizes, n => Assert.InRange(n, 3, 6));
        Assert.True(sizes.Count >= 20, $"{sizes.Count} rows");
        Console.WriteLine($"TERRACE houses={s.B.Count} inRows={inRow.Count} share={share:F3} rows={sizes.Count} sizes={string.Join(",", sizes.GroupBy(x => x).OrderBy(g => g.Key).Select(g => g.Key + "x" + g.Count()))}");
    }

    [Fact]
    public void NoOpeningOnOrNextToASharedEdge()
    {
        var s = Full.Value;
        foreach (var kv in SharedEdges(s.B))
        {
            var (a, b) = kv.Key;
            foreach (int i in kv.Value)
                foreach (var o in s.B[i].Openings)
                    Assert.True(PointSeg(o.X, o.Y, a, b) > 0.3, $"{s.B[i].Id} has a {o.Type} on a party wall");
        }
    }

    [Fact]
    public void NoFootprintOverlap_AndTheGapRuleHoldsExceptAlongSharedEdges()
    {
        var s = Full.Value;
        var shared = SharedEdges(s.B);
        var sharing = new HashSet<(int, int)>(shared.Values.Select(l => (Math.Min(l[0], l[1]), Math.Max(l[0], l[1]))));
        foreach (var (a, b) in NearPairs(s.B))
        {
            var ra = Ring(s.B[a]); var rb = Ring(s.B[b]);
            if (sharing.Contains((a, b)))
            {
                // neighbours in a row: no crossing and no vertex strictly inside the other
                Assert.Equal(0, Gap(ra, rb), 6);
                foreach (var v in ra)
                {
                    double toBoundary = rb.Select((w, k) => PointSeg(v.X, v.Y, w, rb[(k + 1) % rb.Count])).Min();
                    Assert.False(Inside(rb, v.X, v.Y) && toBoundary > 1e-3, "overlapping row neighbours");
                }
                for (int p = 0; p < ra.Count; p++)
                    for (int q = 0; q < rb.Count; q++)
                        Assert.False(SegCross(ra[p], ra[(p + 1) % ra.Count], rb[q], rb[(q + 1) % rb.Count]), "row neighbours cross");
                var ca = (ra.Average(q => q.X), ra.Average(q => q.Y));
                Assert.False(Inside(rb, ca.Item1, ca.Item2));
            }
            else
                Assert.True(Gap(ra, rb) >= 2 - 1e-6, $"{s.B[a].Id} and {s.B[b].Id} closer than 2 m");
        }
    }

    [Fact]
    public void RowsRunParallelToTheirStreet()
    {
        var s = Full.Value;
        var nodes = s.R.Nodes.ToDictionary(n => n.Id);
        var streets = s.R.Edges.Where(e => e.Class == RoadClass.Residential).Select(e => (A: nodes[e.From], B: nodes[e.To])).ToList();
        int rows = 0, parallel = 0;
        foreach (var kv in SharedEdges(s.B))
        {
            var (a, b) = kv.Key;
            // the party wall is perpendicular to the street: find the street nearest its middle
            double mx = (a.Item1 + b.Item1) / 2, my = (a.Item2 + b.Item2) / 2;
            double best = double.PositiveInfinity; (double X, double Y) dir = (1, 0);
            foreach (var (n0, n1) in streets)
            {
                double d = PointSeg(mx, my, (n0.X, n0.Y), (n1.X, n1.Y));
                if (d < best) { best = d; dir = (n1.X - n0.X, n1.Y - n0.Y); }
            }
            double wx = b.Item1 - a.Item1, wy = b.Item2 - a.Item2;
            double cos = Math.Abs(wx * dir.X + wy * dir.Y) / (Math.Sqrt(wx * wx + wy * wy) * Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y));
            rows++;
            if (cos < Math.Sin(8 * Math.PI / 180)) parallel++;
        }
        Assert.True(rows > 0);
        Assert.True(parallel / (double)rows > 0.9, $"{parallel} of {rows} party walls are perpendicular to the street");
    }

    [Fact]
    public void TheCleanRulesStillHoldWithRows()
    {
        var s = Full.Value;
        Assert.Equal(s.B.Count, s.B.Select(b => b.Id).Distinct().Count());
        foreach (var b in s.B)
        {
            var v = Ring(b);
            double area = 0;
            for (int i = 0; i < v.Count; i++) area += v[i].X * v[(i + 1) % v.Count].Y - v[(i + 1) % v.Count].X * v[i].Y;
            Assert.True(area > 0, $"{b.Id} not counter-clockwise");
            Assert.Equal(b.Footprint[0], b.Footprint[^1]);
            foreach (var q in v)
            {
                Assert.InRange(q.X, s.P.MinX, s.P.MaxX);
                Assert.True(s.R.DistanceToRoad(q.X, q.Y) >= 3 - 1e-6);
            }
            Assert.Contains(b.Openings, o => o.Type == "door" && o.Storey == 0);
            for (int i = 1; i < b.StoreyZ.Count; i++) Assert.True(b.StoreyZ[i] > b.StoreyZ[i - 1]);
            foreach (var o in b.Openings)
            {
                double rad = o.Heading * Math.PI / 180;
                Assert.False(Inside(v, o.X + Math.Sin(rad), o.Y + Math.Cos(rad)), $"{b.Id} heading points inward");
                double floor = b.StoreyZ[o.Storey];
                Assert.InRange(o.Z, floor + (o.Type == "door" ? 0 : 1.0) - 0.01, floor + (o.Type == "door" ? 0 : 1.0) + 0.01);
            }
        }
        foreach (var o in s.U)
            foreach (var b in s.B)
                Assert.False(Inside(Ring(b), o.X, o.Y), "an unassigned opening lies inside a footprint");
    }

    [Fact]
    public void TerracesOff_HasNoSharedEdges_AndOnIsDeterministic()
    {
        var off = Make("tiny", 4, 1, false);
        Assert.Empty(SharedEdges(off.B));
        var a = Make("full", 8, 2, true);
        var b = Make("full", 8, 2, true);
        Assert.Equal(a.B.Select(x => x.Id), b.B.Select(x => x.Id));
        Assert.Equal(a.U, b.U);
        Assert.NotEqual(a.B.Select(x => x.Id), Make("full", 8, 2, false).B.Select(x => x.Id));
    }

    [Fact]
    public void Options_TerracesDefaultByPreset_AndAreValidated()
    {
        Assert.True(SynthOptions.Parse(new[] { "--preset", "full" }).TerracesEnabled);
        Assert.False(SynthOptions.Parse(new[] { "--preset", "tiny" }).TerracesEnabled);
        Assert.False(SynthOptions.Parse(new[] { "--preset", "full", "--terraces", "off" }).TerracesEnabled);
        Assert.True(SynthOptions.Parse(new[] { "--preset", "tiny", "--terraces", "on" }).TerracesEnabled);
        Assert.Throws<ArgumentException>(() => SynthOptions.Parse(new[] { "--terraces", "maybe" }));
    }
}
