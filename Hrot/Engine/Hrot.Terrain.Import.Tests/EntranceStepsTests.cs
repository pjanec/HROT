using Hrot.Terrain.Import;
using Xunit;

namespace Hrot.Terrain.Import.Tests;

/// <summary>
/// ⭐ <c>CE-5056</c> ④ (user: "the importer generates entrance steps - ok approved") — a raised ground-floor door gets an entrance ramp out and down to the ground; a level
/// one, or one the ground rises above, gets none. 📐 village-street: 17 ground doors stood more than the infantry climb above the ground outside, up to 2.9 m.
/// 🔒 Red-proof: drop the <c>rise &lt;= MinRise</c> skip ⇒ the level door gets a ramp.
/// </summary>
public class EntranceStepsTests
{
    // a 10 × 8 m house, counter-clockwise; its south wall is edge 0 (y = 0), its north wall edge 2 (y = 8); floor (sill) at z 10
    private static readonly List<(double X, double Y)> Ring = new() { (0, 0), (10, 0), (10, 8), (0, 8) };

    private static BuiltBuilding House(params (int Edge, double At, double Width)[] doors) => new()
    {
        Id = "h", TemplateName = "h", Ring = Ring, BaseZ = 10, GroundDoors = doors,
    };

    [Fact]
    public void ADownhillDoor_GetsARampDownToTheGround_AnUphillOrLevelDoorNone()
    {
        // the ground falls 0.5 m per metre to the south: at the south wall it is 1 m below the floor, at the north wall 3 m ABOVE it (a house dug into the slope)
        double Ground(double x, double y) => 9 + 0.5 * y;
        var rep = new ImportReport();
        var ramps = EntranceSteps.For(new[] { House((0, 4.4, 1.2), (2, 4.4, 1.2)) }, Ground, rep);
        var r = Assert.Single(ramps);                                       // the south door only: the north one is buried
        Assert.Equal(1, rep.EntranceSteps);
        Assert.Equal(0, r.TopA.Y, 3); Assert.Equal(0, r.TopB.Y, 3);         // the top on the wall's centreline (the slab's edge)
        Assert.Equal(1.2, Math.Abs(r.TopB.X - r.TopA.X), 3);                // as wide as the door
        Assert.True(r.FootA.Y < -0.8 && r.FootB.Y < -0.8, $"the foot goes OUT (south): {r.FootA}");
        Assert.Equal(10 - Ground(r.TopA.X, 0), r.TopAboveA, 3);             // the top stands at the sill
        double rise = 10 - Ground(r.FootA.X, r.FootA.Y), run = -r.FootA.Y;
        Assert.InRange(Math.Atan2(rise, run) * 180 / Math.PI, 28, 38);     // a flight's pitch (33°), walkable for infantry (60°)

        var level = EntranceSteps.For(new[] { House((0, 4.4, 1.2)) }, (x, y) => 9.9, new ImportReport());
        Assert.Empty(level);                                                // 0.1 m below the sill: no step needed
    }
}
