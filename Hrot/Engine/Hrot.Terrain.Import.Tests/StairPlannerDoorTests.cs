using Clipper2Lib;
using Hrot.Terrain.Import;
using Xunit;

namespace Hrot.Terrain.Import.Tests;

/// <summary>
/// ⭐ <c>CE-5056</c> (behaviors' note: village-street's doors impassable) — the generated stair keeps clear of a ground-floor door. 📐 Measured on village-street: 54 of the
/// ground doors with no path through opened straight onto the stair's ramp (the gun house's east door, `f666eab30807`). A 12 × 8 m house plans its stair on its longest
/// (south) wall, from the start; a door there pushes the stair to the other end of that wall or another wall, its apron never overlapped.
/// 🔒 Red-proof: seed no aprons in <c>TryPlan</c> ⇒ the stair sits where it did, over the door.
/// </summary>
public class StairPlannerDoorTests
{
    private static readonly (double X, double Y)[] House = { (0, 0), (12, 0), (12, 8), (0, 8) };
    private static readonly double[] TwoStoreys = { 2.8, 2.8 };

    private static PathD Hull(StairPlanner.Stair s)
    {
        var all = new PathD();
        foreach (var r in s.Runs) { all.Add(new PointD(r.From.X, r.From.Y)); all.Add(new PointD(r.To.X, r.To.Y)); }
        return all;
    }

    private static double DistanceToRuns(StairPlanner.Stair s, (double X, double Y) p)
    {
        double best = double.MaxValue;
        foreach (var r in s.Runs)
        {
            double dx = r.To.X - r.From.X, dy = r.To.Y - r.From.Y, l2 = dx * dx + dy * dy;
            double t = l2 > 0 ? Math.Clamp(((p.X - r.From.X) * dx + (p.Y - r.From.Y) * dy) / l2, 0, 1) : 0;
            double x = r.From.X + t * dx - p.X, y = r.From.Y + t * dy - p.Y;
            best = Math.Min(best, Math.Sqrt(x * x + y * y));
        }
        return best;
    }

    [Fact]
    public void AStair_KeepsClearOfAGroundDoor_WhereItWouldOtherwiseStand()
    {
        var free = StairPlanner.TryPlan(House, TwoStoreys)!;
        var stairFree = Assert.Single(free.Stairs);
        var door = (X: (stairFree.From.X + stairFree.To.X) / 2, Y: 0.0);                     // a door in the middle of where the stair stands, on the south wall
        Assert.True(DistanceToRuns(stairFree, door) < 1.0, "the fixture: the free plan's stair runs past the door");

        var withDoor = StairPlanner.TryPlan(House, TwoStoreys, new[] { door })!;
        var stair = Assert.Single(withDoor.Stairs);
        // the apron: the door's width + 0.3 m each side, 1.5 m deep — the stair's centreline (0.65 m off its wall) stays outside it
        double half = OpeningPlacer.DoorWidth / 2 + StairPlanner.DoorApronSide;
        foreach (var r in stair.Runs)
            foreach (var p in new[] { r.From, r.To, ((r.From.X + r.To.X) / 2, (r.From.Y + r.To.Y) / 2) })
                Assert.False(p.Item1 > door.X - half && p.Item1 < door.X + half && p.Item2 < StairPlanner.DoorApronDepth,
                    $"the stair point {p} is in the door's apron");
        Assert.True(DistanceToRuns(stair, door) > 1.0, $"the stair stays {DistanceToRuns(stair, door):F2} m from the door");
    }
}
