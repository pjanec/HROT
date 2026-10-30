using Hrot.Core.Tkb;
using Xunit;

namespace Hrot.Traffic.Tests;

/// <summary>⭐ CE-1457 (§6ad-3, TC4) — a SUMO car's paint is a pure function of its id: the same on every node and replay, and across a fleet it follows the palette.</summary>
public sealed class TrafficPaintTests
{
    [Fact]
    public void CE1457_TheSameId_GivesTheSamePaint_AndAThousandIdsFollowThePalette()
    {
        Assert.Equal(TrafficPaint.Of("veh42", UrbanCombatTkbCatalog.TkbCivilianCar), TrafficPaint.Of("veh42", UrbanCombatTkbCatalog.TkbCivilianCar));
        var counts = new Dictionary<uint, int>();
        for (int i = 0; i < 1000; i++)
        {
            uint c = TrafficPaint.Of("flow" + i, UrbanCombatTkbCatalog.TkbTrafficSedan);
            Assert.NotEqual(0u, c);   // 0 means "no colour"
            counts[c] = counts.GetValueOrDefault(c) + 1;
        }
        int total = TrafficPaint.CarPalette.Sum(p => p.Weight);
        foreach (var (rgb, weight) in TrafficPaint.CarPalette)
        {
            double expected = 1000.0 * weight / total;
            Assert.InRange(counts.GetValueOrDefault(rgb), expected - 50.0, expected + 50.0);   // ±5 % of the fleet
        }
        Assert.Equal(TrafficPaint.CarPalette.Length, counts.Count);   // every colour shows up
    }

    [Fact]
    public void CE1457_ABusAndATruck_TakeFleetColours_NotStreetOnes()
    {
        var bus = Enumerable.Range(0, 200).Select(i => TrafficPaint.Of("b" + i, UrbanCombatTkbCatalog.TkbTrafficBus)).Distinct().ToList();
        Assert.All(bus, c => Assert.Contains(TrafficPaint.BusPalette, p => p.Rgb == c));
        var truck = Enumerable.Range(0, 200).Select(i => TrafficPaint.Of("t" + i, UrbanCombatTkbCatalog.TkbTrafficTruck)).Distinct().ToList();
        Assert.All(truck, c => Assert.Contains(TrafficPaint.TruckPalette, p => p.Rgb == c));
        Assert.True(bus.Count >= 3 && truck.Count >= 3, "a fleet is more than one colour");
    }
}
