using System.Globalization;
using Hrot.Tools.TerrainSynth;
using Xunit;

namespace Hrot.Tools.TerrainSynth.Tests;

public class OpeningNoiseTests
{
    private sealed record Pair(IReadOnlyList<Building> Clean, IReadOnlyList<Building> Noisy);

    private static Pair Generate(string preset, double spacing, ulong seed, bool noise)
    {
        var p = SynthOptions.Parse(new[] { "--preset", preset, "--height-spacing", spacing.ToString(CultureInfo.InvariantCulture) }).Resolve();
        var h = new HeightField(p, new Rng(seed));
        var r = RoadNetwork.Generate(p, new Rng(seed));
        var clean = BuildingGenerator.Generate(p, new Rng(seed), h, r).Buildings;
        var noisy = BuildingGenerator.Generate(p, new Rng(seed), h, r, new BuildingOptions { Noise = noise }).Buildings;
        return new Pair(clean, noisy);
    }

    private static readonly Lazy<Pair> Full = new(() => Generate("full", 8, 1, true));

    private static double Quantile(List<double> sorted, double q) => sorted[(int)Math.Min(sorted.Count - 1, q * sorted.Count)];

    private static double Dist(Opening a, Opening b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static bool Inside(IReadOnlyList<(double X, double Y)> v, double x, double y)
    {
        bool inside = false;
        for (int i = 0, j = v.Count - 1; i < v.Count; j = i++)
            if ((v[i].Y > y) != (v[j].Y > y) && x < (v[j].X - v[i].X) * (y - v[i].Y) / (v[j].Y - v[i].Y) + v[i].X)
                inside = !inside;
        return inside;
    }

    private static IEnumerable<(Building B, Opening Clean, Opening Noisy)> Pairs(Pair p)
    {
        for (int i = 0; i < p.Clean.Count; i++)
        {
            Assert.Equal(p.Clean[i].Id, p.Noisy[i].Id);
            Assert.Equal(p.Clean[i].Openings.Count, p.Noisy[i].Openings.Count);
            for (int k = 0; k < p.Clean[i].Openings.Count; k++)
                yield return (p.Clean[i], p.Clean[i].Openings[k], p.Noisy[i].Openings[k]);
        }
    }

    [Fact]
    public void NoiseOff_EqualsTheCleanSet()
    {
        var p = Generate("tiny", 4, 1, false);
        Assert.Equal(p.Clean.Select(b => b.Openings.ToList()), p.Noisy.Select(b => b.Openings.ToList()));
    }

    [Fact]
    public void Noise_LeavesFootprintsAndFloorsAlone()
    {
        var p = Full.Value;
        Assert.Equal(p.Clean.Count, p.Noisy.Count);
        for (int i = 0; i < p.Clean.Count; i++)
        {
            Assert.Equal(p.Clean[i].Footprint, p.Noisy[i].Footprint);
            Assert.Equal(p.Clean[i].StoreyZ, p.Noisy[i].StoreyZ);
            Assert.Equal(p.Clean[i].BaseZ, p.Noisy[i].BaseZ);
        }
    }

    [Fact]
    public void Noise_DoorHeights_FollowTheMeasuredSpread()
    {
        var p = Full.Value;
        var offsets = new List<double>();
        foreach (var (b, c, n) in Pairs(p))
            if (c.Type == "door") offsets.Add(n.Z - b.StoreyZ[c.Storey]);
        offsets.Sort();
        Assert.True(offsets.Count > 3000);
        Assert.InRange(Quantile(offsets, 0.5), 0.05, 0.15);
        Assert.InRange(Quantile(offsets, 0.1), -0.4, -0.2);
        Assert.InRange(Quantile(offsets, 0.9), 0.85, 1.15);
        Assert.InRange(offsets[0], -1.2 - 0.011, 3.6);
        Assert.InRange(offsets[^1], -1.2, 3.6 + 0.011);
        Console.WriteLine($"NOISE door z offset p10={Quantile(offsets, 0.1):F2} p50={Quantile(offsets, 0.5):F2} p90={Quantile(offsets, 0.9):F2} min={offsets[0]:F2} max={offsets[^1]:F2}");
    }

    [Fact]
    public void Noise_WindowHeights_AreFloorPlusOneWithinTwentyCentimetres()
    {
        var p = Full.Value;
        double lo = 0, hi = 0;
        int n = 0;
        foreach (var (b, c, nz) in Pairs(p))
        {
            if (c.Type != "window") continue;
            double d = nz.Z - (b.StoreyZ[c.Storey] + 1.0);
            Assert.InRange(d, -0.2 - 0.011, 0.2 + 0.011);
            lo = Math.Min(lo, d); hi = Math.Max(hi, d);
            n++;
        }
        Assert.True(n > 10000);
        Assert.True(lo < -0.15 && hi > 0.15, "the jitter should use the whole range");
    }

    [Fact]
    public void Noise_Positions_HalfOnTheWall_RestWithinThirtyCentimetres_AFewDoorsLoose()
    {
        var p = Full.Value;
        int total = 0, exact = 0, loose = 0, looseDoors = 0, doors = 0;
        foreach (var (b, c, n) in Pairs(p))
        {
            total++;
            if (c.Type == "door") doors++;
            double d = Dist(c, n);
            if (d >= 0.9)
            {
                Assert.Equal("door", c.Type);
                Assert.InRange(d, 0.9, 3.1);
                Assert.False(Inside(b.Footprint, n.X, n.Y), "a loose door must be outside its footprint");
                loose++;
                looseDoors++;
                continue;
            }
            Assert.True(d <= 0.3 + 0.015, $"offset {d}");
            if (d < 0.005) exact++;
        }
        double exactShare = exact / (double)(total - loose);
        Assert.InRange(exactShare, 0.44, 0.56);
        Assert.InRange(looseDoors / (double)doors, 0.004, 0.02);
        Console.WriteLine($"NOISE exactOnWall={exactShare:F3} looseDoors={looseDoors / (double)doors:F4} ({looseDoors} of {doors})");
    }

    [Fact]
    public void Noise_FlipsAboutThreePercentOfHeadings()
    {
        var p = Full.Value;
        int total = 0, flipped = 0;
        foreach (var (_, c, n) in Pairs(p))
        {
            total++;
            double diff = Math.Abs(((n.Heading - c.Heading) % 360 + 360) % 360);
            if (Math.Abs(diff - 180.0) < 0.15) flipped++;
            else Assert.True(diff < 0.15 || diff > 359.85, "a heading changed other than by a flip");
        }
        double share = flipped / (double)total;
        Assert.InRange(share, 0.02, 0.04);
        Console.WriteLine($"NOISE flipped headings={share:F4}");
    }

    [Fact]
    public void Noise_IsDeterministic()
    {
        var a = Generate("tiny", 4, 3, true);
        var b = Generate("tiny", 4, 3, true);
        Assert.Equal(a.Noisy.SelectMany(x => x.Openings), b.Noisy.SelectMany(x => x.Openings));
        Assert.NotEqual(a.Clean.SelectMany(x => x.Openings), a.Noisy.SelectMany(x => x.Openings));
    }

    [Fact]
    public void Options_NoiseDefaultsByPreset_AndIsValidated()
    {
        Assert.True(SynthOptions.Parse(new[] { "--preset", "full" }).NoiseEnabled);
        Assert.False(SynthOptions.Parse(new[] { "--preset", "tiny" }).NoiseEnabled);
        Assert.False(SynthOptions.Parse(new[] { "--preset", "full", "--noise", "none" }).NoiseEnabled);
        Assert.True(SynthOptions.Parse(new[] { "--preset", "tiny", "--noise", "real" }).NoiseEnabled);
        Assert.Throws<ArgumentException>(() => SynthOptions.Parse(new[] { "--noise", "lots" }));
        Assert.Throws<ArgumentException>(() => SynthOptions.Parse(new[] { "--noise" }));
    }
}
