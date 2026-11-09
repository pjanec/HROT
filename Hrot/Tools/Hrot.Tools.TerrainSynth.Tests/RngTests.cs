using Hrot.Tools.TerrainSynth;
using Xunit;

namespace Hrot.Tools.TerrainSynth.Tests;

public class RngTests
{
    [Fact]
    public void Rng_SameSeed_SameSequence()
    {
        var a = new Rng(42);
        var b = new Rng(42);
        for (int i = 0; i < 100; i++) Assert.Equal(a.NextU64(), b.NextU64());
        Assert.NotEqual(new Rng(1).NextU64(), new Rng(2).NextU64());
    }

    [Fact]
    public void Rng_Fork_IsIndependentAndStable()
    {
        Assert.Equal(14973439391416995469UL, new Rng(7).Fork("trees").NextU64());
        // the fork does not depend on how much the parent has been drawn
        var parent = new Rng(7);
        parent.NextU64();
        Assert.Equal(new Rng(7).Fork("trees").NextU64(), parent.Fork("trees").NextU64());
        Assert.NotEqual(new Rng(7).Fork("trees").NextU64(), new Rng(7).Fork("roads").NextU64());
    }

    [Fact]
    public void Rng_Ranges_StayInside()
    {
        var r = new Rng(3);
        for (int i = 0; i < 1000; i++)
        {
            double d = r.NextDouble();
            Assert.InRange(d, 0.0, 0.9999999999999999);
            Assert.InRange(r.Range(2.6, 3.6), 2.6, 3.6);
            Assert.InRange(r.Int(5, 9), 5, 8);
        }
    }
}
