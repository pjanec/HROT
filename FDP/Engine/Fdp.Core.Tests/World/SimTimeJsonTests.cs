using System.Text.Json;
using Fdp.Core;
using Xunit;

namespace Fdp.Core.Tests.World;

/// <summary>
/// ⭐ CE-3441 — a <see cref="SimInstant"/> / <see cref="SimDuration"/> of ±∞ or NaN survives JSON: written as a marker (<c>"-inf"</c> / <c>"+inf"</c> /
/// <c>"nan"</c>, JSON has no infinity) and read back to the same value; a finite time stays a plain number. 📐 Before: <c>SquadUnitSense.AskedAt = -∞</c>
/// ("never asked") made every scenario golden snapshot throw on its first frame. 🔴 Red-proof: the marker branch dropped ⇒ the infinity cases throw.
/// </summary>
public sealed class SimTimeJsonTests
{
    private sealed record Holder(SimInstant At, SimDuration For);

    [Theory]
    [InlineData(double.NegativeInfinity, "\"-inf\"")]
    [InlineData(double.PositiveInfinity, "\"+inf\"")]
    [InlineData(double.NaN, "\"nan\"")]
    [InlineData(12.5, "12.5")]
    [InlineData(0.0, "0")]
    public void CE3441_ASimInstant_RoundTrips_NonFiniteAsAMarker_FiniteAsANumber(double seconds, string expectedJson)
    {
        var at = (SimInstant)seconds;
        string json = JsonSerializer.Serialize(at);
        Assert.Equal(expectedJson, json);
        var back = JsonSerializer.Deserialize<SimInstant>(json);
        Assert.Equal(seconds, back.Seconds);   // NaN equals NaN under double.Equals
    }

    [Fact]
    public void CE3441_ASimDuration_AndAWholeRecord_RoundTrip_WithInfinities()
    {
        var h = new Holder((SimInstant)double.NegativeInfinity, (SimDuration)float.PositiveInfinity);
        string json = JsonSerializer.Serialize(h);
        Assert.Equal("{\"At\":\"-inf\",\"For\":\"+inf\"}", json);
        var back = JsonSerializer.Deserialize<Holder>(json)!;
        Assert.True(double.IsNegativeInfinity(back.At.Seconds));
        Assert.True(float.IsPositiveInfinity(back.For.Seconds));
    }

    [Fact]
    public void CE3441_AMarkerNumberString_IsReadInvariant_AndGarbageThrows()
    {
        Assert.Equal(1.5, JsonSerializer.Deserialize<SimInstant>("\"1.5\"").Seconds);
        Assert.ThrowsAny<System.Exception>(() => JsonSerializer.Deserialize<SimInstant>("\"forever\""));
    }
}
