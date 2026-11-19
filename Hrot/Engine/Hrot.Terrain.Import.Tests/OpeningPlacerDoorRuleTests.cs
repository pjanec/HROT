using System.Collections.Generic;
using System.Linq;
using Hrot.Terrain.InputSet;
using Xunit;

namespace Hrot.Terrain.Import.Tests;

/// <summary>
/// ⭐⭐ <c>CE-5029</c> BE-3 (📄 docs/DESIGN_Terrain_Base_Edits.md D-6, D-7) — the ≥ 1 DOOR rule: an enterable house whose data gives it no ground-floor door gets one in
/// the middle of a FREE wall — facing the nearest road when the caller knows it, else the longest — taking the place of a window there; a hint ("add door") puts it on
/// the hinted wall; a footprint with every wall shared is reported, never silent. One rule for the importer and the editor. 🔴 Red-proof: drop the <c>AddDoor</c> call ⇒
/// no door ⇒ red.
/// </summary>
public sealed class OpeningPlacerDoorRuleTests
{
    // a 10 × 6 m house, counter-clockwise from (0,0): edge 0 south (10 m), 1 east (6), 2 north (10), 3 west (6)
    private static readonly List<(double X, double Y)> Ring = new() { (0, 0), (10, 0), (10, 6), (0, 6) };
    private static PartyWalls Alone() => new(new[] { ("h", (IReadOnlyList<(double X, double Y)>)Ring) });

    [Fact]
    public void NoDoorInTheData_OneIsAdded_OnTheLongestFreeWall_InItsMiddle()
    {
        var (placed, counts) = OpeningPlacer.Place(Ring, "h", new List<Opening>(), 2, Alone());
        var door = Assert.Single(placed, p => p.Kind == "door");
        Assert.Equal(0, door.Storey);
        Assert.True(door.Edge is 0 or 2, $"a 10 m wall, edge {door.Edge}");
        Assert.Equal(5.0 - OpeningPlacer.DoorWidth / 2, door.At, 3);
        Assert.True(counts.AddedDoor);
        Assert.False(counts.Doorless);
    }

    [Fact]
    public void ADoorInTheData_NothingIsAdded()
    {
        var (placed, counts) = OpeningPlacer.Place(Ring, "h", new List<Opening> { new("door", 3, 6, 0, 0, 0) }, 1, Alone());
        Assert.Single(placed, p => p.Kind == "door");
        Assert.False(counts.AddedDoor);
    }

    [Fact]
    public void TheWallFacingTheNearestRoad_GetsTheDoor_TakingTheWindowsPlace()
    {
        var windows = new List<Opening> { new("window", 10, 3, 0, 0, 0) };   // a ground-floor window in the middle of the east wall
        var (placed, _) = OpeningPlacer.Place(Ring, "h", windows, 1, Alone(), nearestRoad: _ => (30.0, 3.0));   // the road passes 20 m east of the house
        var door = Assert.Single(placed, p => p.Kind == "door");
        Assert.Equal(1, door.Edge);
        Assert.DoesNotContain(placed, p => p.Kind == "window" && p.Edge == 1 && p.Storey == 0);   // the window gave way
    }

    [Fact]
    public void AHint_PutsTheDoorThere()
    {
        var (placed, _) = OpeningPlacer.Place(Ring, "h", new List<Opening>(), 1, Alone(), doorHint: (2.0, 6.0));   // on the north wall, near its east end (it runs 10,6 → 0,6)
        var door = Assert.Single(placed, p => p.Kind == "door");
        Assert.Equal(2, door.Edge);
        Assert.Equal(8.0 - OpeningPlacer.DoorWidth / 2, door.At, 3);           // 8 m along the north wall from (10, 6)
    }

    [Fact]
    public void EveryWallShared_IsReported_NotSilent()
    {
        // four neighbours, one on each side: every wall of h is a party wall
        var n = new List<(string, IReadOnlyList<(double X, double Y)>)>
        {
            ("h", Ring),
            ("s", new List<(double, double)> { (0, -5), (10, -5), (10, 0), (0, 0) }),
            ("e", new List<(double, double)> { (10, 0), (15, 0), (15, 6), (10, 6) }),
            ("n", new List<(double, double)> { (0, 6), (10, 6), (10, 11), (0, 11) }),
            ("w", new List<(double, double)> { (-5, 0), (0, 0), (0, 6), (-5, 6) }),
        };
        var (placed, counts) = OpeningPlacer.Place(Ring, "h", new List<Opening>(), 1, new PartyWalls(n));
        Assert.DoesNotContain(placed, p => p.Kind == "door");
        Assert.True(counts.Doorless);
    }
}
