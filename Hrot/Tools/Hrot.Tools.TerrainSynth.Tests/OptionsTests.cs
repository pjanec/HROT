using Hrot.Tools.TerrainSynth;
using Xunit;

namespace Hrot.Tools.TerrainSynth.Tests;

public class OptionsTests
{
    [Fact]
    public void Options_Defaults()
    {
        var o = SynthOptions.Parse(new[] { "--preset", "tiny" });
        Assert.Equal("tiny", o.Preset);
        Assert.Equal(1UL, o.Seed);
        Assert.Equal("artifacts/terrain-synth/tiny-1", o.OutDir);
        Assert.Null(o.Size);
    }

    [Fact]
    public void Options_ParsesOverrides()
    {
        var o = SynthOptions.Parse(new[] { "--preset", "full", "--seed", "9", "--out", "x", "--size", "1000", "--height-spacing", "8", "--buildings", "5", "--trees", "6" });
        Assert.Equal(9UL, o.Seed);
        Assert.Equal("x", o.OutDir);
        Assert.Equal(1000.0, o.Size);
        Assert.Equal(8.0, o.HeightSpacing);
        Assert.Equal(5, o.Buildings);
        Assert.Equal(6, o.Trees);
        var p = o.Resolve();
        Assert.Equal(5, p.BuildingCount);
        Assert.Equal(6, p.TreeCount);
        Assert.Equal(8.0, p.HeightSpacing);
    }

    [Fact]
    public void Options_SizeOverride_ScalesCounts()
    {
        var full = SynthProfile.Full;
        var p = SynthOptions.Parse(new[] { "--preset", "full", "--size", "2750" }).Resolve();
        Assert.InRange(p.BuildingCount, full.BuildingCount / 4 - 1, full.BuildingCount / 4 + 1);
        Assert.InRange(p.TreeCount, full.TreeCount / 4 - 1, full.TreeCount / 4 + 1);
        Assert.Equal(-1375.0, p.MinX);
        Assert.Equal(1375.0, p.MaxY);
    }

    [Fact]
    public void Options_RejectsBadInput()
    {
        Assert.Throws<ArgumentException>(() => SynthOptions.Parse(new[] { "--preset", "huge" }));
        Assert.Throws<ArgumentException>(() => SynthOptions.Parse(new[] { "--bogus" }));
        Assert.Throws<ArgumentException>(() => SynthOptions.Parse(new[] { "--seed" }));
    }

    [Fact]
    public void Profile_HoldsSpecNumbers()
    {
        var f = SynthProfile.Full;
        Assert.Equal(-2750.0, f.MinX);
        Assert.Equal(0.48828125, f.HeightSpacing);
        Assert.Equal(450.0, f.ZMin);
        Assert.Equal(980.0, f.ZMax);
        Assert.Equal(2, f.NodataRects.Count);
        Assert.Equal(1800, f.BuildingCount);
        Assert.Equal(176000, f.TreeCount);
        Assert.Equal(500000.0, f.E0);
        Assert.Equal(5600000.0, f.N0);
        Assert.Equal(1.0, f.StoreyShares.Sum(), 2); // the spec shares are rounded and sum to 1.003
        Assert.Equal(150.2, f.RoadKm.Values.Sum(), 1);
        var t = SynthProfile.Tiny;
        Assert.Equal(40, t.BuildingCount);
        Assert.Equal(1500, t.TreeCount);
        Assert.Single(t.NodataRects);
    }
}
