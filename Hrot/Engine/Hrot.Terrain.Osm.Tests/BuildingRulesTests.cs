using Clipper2Lib;
using Hrot.Terrain.InputSet;
using Xunit;

namespace Hrot.Terrain.Osm.Tests;

/// <summary>One rail per §5.5 row, per F-row and per R-4193 rule, on the synthetic town or a tiny hand-built model (E-9: the rules are pure functions of the model).</summary>
public class BuildingRulesTests
{
    private static readonly OsmImportOptions Opt = new() { Name = "t" };
    private static BuildingRulesResult Run(ModelBuilder mb, OsmImportOptions? o = null) => BuildingRules.Apply(mb.Model, o ?? Opt, ModelBuilder.Bounds);
    private static (string, string) T(string k, string v) => (k, v);

    // ---- the census of the town
    [Fact]
    public void TheTownCensus_EnterableAndSolidAreCountedApart()
    {
        var r = Fx.Buildings.Value;
        Assert.Equal(11, r.Enterable);   // 4 terrace + 2 courtyard parts + church + 3-storey house + commercial + plain yes + greenhouse
        Assert.Equal(4, r.Solid);        // garage, shed, 20 m² yes, ruins
        Assert.Equal(1, r.DroppedAtEdge);   // the house crossing the area's edge
        Assert.Equal(1, r.NoWalls);         // the carport
        Assert.Equal(1, r.PartsIgnored);    // the min_level=1 overhang
        Assert.Equal(1, r.SplitCourtyards);
    }

    // ---- R-4193: solid, not enterable
    [Theory]
    [InlineData(Fx.Garage)]
    [InlineData(Fx.Shed)]
    [InlineData(Fx.SmallYes)]   // a plain building=yes of 20 m² — solid by area
    [InlineData(Fx.Ruins)]
    public void Garages_Sheds_SmallFootprints_AreSolid(string id)
    {
        var d = Fx.B(id);
        Assert.False(d.Enterable);
        Assert.Empty(d.Openings);
        Assert.False(Fx.Finished(id).Enterable);
    }

    [Theory]
    [InlineData("garage")] [InlineData("garages")] [InlineData("shed")] [InlineData("hut")] [InlineData("kiosk")] [InlineData("service")] [InlineData("transformer_tower")]
    public void EverySolidType_IsSolid_EvenWhenLarge(string type)
    {
        var mb = new ModelBuilder();
        mb.Rect(0, 0, 10, 10, T("building", type));   // 100 m²
        Assert.False(Run(mb).Buildings.Single().Enterable);
    }

    [Fact]
    public void TwentyFiveSquareMetres_IsTheEnterableThreshold()
    {
        var mb = new ModelBuilder();
        mb.Rect(0, 0, 5, 4.9, T("building", "house"));      // 24.5
        mb.Rect(20, 0, 25, 5.2, T("building", "house"));    // 26
        var r = Run(mb);
        Assert.False(r.Buildings.Single(b => b.Area < 25).Enterable);
        Assert.True(r.Buildings.Single(b => b.Area > 25).Enterable);
    }

    // ---- §5.5 footprint rows
    [Fact]
    public void FootprintsUnder12SquareMetres_AndThinOnes_AreDropped()
    {
        var mb = new ModelBuilder();
        mb.Rect(0, 0, 3, 3, T("building", "yes"));         // 9 m²
        mb.Rect(20, 0, 22, 30, T("building", "yes"));      // 60 m² but 2 m thin
        mb.Rect(50, 0, 56, 6, T("building", "yes"));       // fine
        var r = Run(mb);
        Assert.Single(r.Buildings);
        Assert.Equal(1, r.DroppedSmall);
        Assert.Equal(1, r.DroppedThin);
    }

    [Fact]
    public void RoofAndCarport_AreNoBuilding_RuinsAreALowSolidPrism_GreenhouseIsEnterableOneStorey()
    {
        var r = Fx.Buildings.Value;
        Assert.DoesNotContain(r.Buildings, b => b.Type == "carport");
        var ruins = Fx.B(Fx.Ruins);
        Assert.False(ruins.Enterable); Assert.Equal(1, ruins.Storeys); Assert.Equal(3.0, ruins.Height); Assert.Equal("flat", ruins.Roof);
        var gh = Fx.B(Fx.Greenhouse);
        Assert.True(gh.Enterable); Assert.Equal(1, gh.Storeys);
    }

    [Fact]
    public void BuildingPart_WithMinLevel_IsIgnored_AndAPartInsideAnOutlineIsMerged()
    {
        var mb = new ModelBuilder();
        mb.Rect(0, 0, 20, 20, T("building", "house"));
        mb.Rect(2, 2, 8, 8, T("building:part", "yes"));                      // inside → merged
        mb.Rect(30, 0, 40, 10, T("building:part", "yes"), T("min_level", "2")); // an overhang → ignored
        mb.Rect(60, 0, 70, 10, T("building:part", "yes"));                   // stands alone → a building
        var r = Run(mb);
        Assert.Equal(2, r.Buildings.Count);
        Assert.Equal(1, r.PartsMerged); Assert.Equal(1, r.PartsIgnored);
    }

    // ---- E-4: holes
    [Fact]
    public void Courtyard_IsSplitIntoHoleFreeParts_ThatCoverTheRing()
    {
        var parts = Fx.Buildings.Value.Buildings.Where(b => b.Id.StartsWith(Fx.Courtyard)).ToList();
        Assert.Equal(2, parts.Count);
        var (hx, hy) = (51.0 + Fx.Bounds.Frame.E0 - 500000, -75.0 + Fx.Bounds.Frame.N0 - 5430000);   // the hole's centre in local metres
        foreach (var p in parts) Assert.False(Polygons.Contains(p.Ring, hx, hy), "a part covers the courtyard");
        Assert.InRange(parts.Sum(p => p.Area), 1055, 1065);   // 40 × 30 − 14 × 10
        Assert.All(parts, p => Assert.Equal(4, p.Storeys));
        Assert.All(parts, p => Assert.True(p.Enterable));
    }

    // ---- storeys
    [Fact]
    public void Storeys_FromLevels_Height_TypeDefault_AndTheDefault()
    {
        var mb = new ModelBuilder();
        mb.Rect(0, 0, 10, 10, T("building", "house"), T("building:levels", "3"));                    // levels
        mb.Rect(20, 0, 30, 10, T("building", "house"), T("building:levels", "2"), T("roof:levels", "1")); // + attic
        mb.Rect(40, 0, 50, 10, T("building", "yes"), T("height", "9 m"));                            // height / 3
        mb.Rect(60, 0, 70, 10, T("building", "apartments"));                                         // type default 4
        mb.Rect(80, 0, 90, 10, T("building", "yes"));                                                // option default 2
        mb.Rect(100, 0, 110, 10, T("building", "yes"));
        var r = Run(mb, new OsmImportOptions { Name = "t", DefaultStoreys = 5 });
        Assert.Equal(new[] { 3, 3, 3, 4, 5, 5 }, r.Buildings.Select(b => b.Storeys).ToArray());
        Assert.Equal(3, r.HeightsInferred);   // the type default and the two untagged 'yes' were inferred; levels and height were tagged
    }

    [Fact]
    public void TallTypes_AreOneStorey_WithTheTaggedHeightOr6m()
    {
        var church = Fx.B(Fx.Church);
        Assert.Equal(1, church.Storeys); Assert.Equal(18.0, church.Height);
        var mb = new ModelBuilder();
        mb.Rect(0, 0, 20, 20, T("building", "industrial"));
        var d = Run(mb).Buildings.Single();
        Assert.Equal(1, d.Storeys); Assert.Equal(6.0, d.Height); Assert.Equal("flat", d.Roof);
    }

    // ---- roofs
    [Fact]
    public void Roofs_FromTheTag_ElseByType_RidgeFromRoofHeightElse035OfTheShorterSide()
    {
        var hipped = Fx.B(Fx.Levels3);
        Assert.Equal("pitched", hipped.Roof); Assert.Equal(3.0, hipped.RidgeRise); Assert.Equal("brick", hipped.Material);
        var b = Fx.Finished(Fx.Levels3);
        Assert.Equal(b.BaseZ + b.Height + 3.0, b.RidgeZ!.Value, 2);
        Assert.Equal("flat", Fx.B(Fx.Commercial).Roof);
        var h1 = Fx.B(Fx.H1);   // gabled, no roof:height, 8 m wide ⇒ 0.35 × 8
        Assert.Equal(0.35 * 8, h1.RidgeRise, 1);
        Assert.Equal("flat", Fx.B(Fx.Courtyard + "-1").Roof);   // apartments untagged
    }

    [Fact]
    public void FlatRoofShape_IsFlat_AnyOtherShapeIsPitched()
    {
        var mb = new ModelBuilder();
        mb.Rect(0, 0, 10, 10, T("building", "house"), T("roof:shape", "flat"));
        mb.Rect(20, 0, 30, 10, T("building", "commercial"), T("roof:shape", "many"));
        var r = Run(mb);
        Assert.Equal(new[] { "flat", "pitched" }, r.Buildings.Select(b => b.Roof).ToArray());
    }

    [Fact]
    public void Material_IsTakenFromBuildingMaterial_Otherwise_Omitted()
    {
        Assert.Equal("brick", Fx.B(Fx.H2).Material);
        Assert.Null(Fx.B(Fx.H1).Material);
    }

    [Fact]
    public void Ids_AreTheOsmWayOrRelationId()
    {
        Assert.All(Fx.Buildings.Value.Buildings, b => Assert.Matches(@"^[wr]\d+(-\d+)?$", b.Id));
        Assert.Equal(Fx.Buildings.Value.Buildings.Count, Fx.Buildings.Value.Buildings.Select(b => b.Id).Distinct().Count());
    }

    // ---- F-4 / E-5 party walls
    [Fact]
    public void Terrace_SharedEdgesAreIdenticalInBothFootprints_AndCarryNoOpenings()
    {
        var f1 = Fx.Finished(Fx.H1); var f2 = Fx.Finished(Fx.H2); var f3 = Fx.Finished(Fx.H3);
        var shared12 = f1.Footprint.Intersect(f2.Footprint).ToList();
        Assert.Equal(2, shared12.Count);                    // the two corner posts of the wall they share, written identically
        Assert.Equal(2, f2.Footprint.Intersect(f3.Footprint).Count());
        double wall12X = shared12[0].X;
        // the middle houses have only north / south walls free: no opening faces east or west
        foreach (var o in Fx.B(Fx.H2).Openings.Concat(Fx.B(Fx.H3).Openings))
            Assert.True(o.Heading == 0 || o.Heading == 180, $"an opening faces {o.Heading} on a party wall");
        Assert.DoesNotContain(Fx.B(Fx.H1).Openings, o => o.Heading == 90);   // h1's east wall is shared with h2
        Assert.DoesNotContain(Fx.B(Fx.H2).Openings, o => Math.Abs(o.X - wall12X) < 0.3);
        Assert.True(Fx.Buildings.Value.SharedWallsById >= 3);
    }

    [Fact]
    public void Houses02mApart_WithoutSharedNodes_AreSnappedToOneSharedEdge_ByDistance()
    {
        var mb = new ModelBuilder();
        mb.Rect(0, 0, 8, 12, T("building", "house"));
        mb.Rect(8.2, 0.1, 16, 11.9, T("building", "house"));   // 0.2 m gap, slightly shorter
        var r = Run(mb);
        Assert.Equal(0, r.SharedWallsById);
        Assert.True(r.SharedWallsByDistance >= 1); Assert.True(r.SnappedVertices >= 2);
        var a = BuildingRules.Finish(r.Buildings[0], null); var b = BuildingRules.Finish(r.Buildings[1], null);
        Assert.Equal(2, a.Footprint.Intersect(b.Footprint).Count());   // ONE shared edge, identical vertices
        Assert.DoesNotContain(r.Buildings[0].Openings, o => o.Heading == 90 && o.Y > 0.5 && o.Y < 11.5 && Math.Abs(o.X - 8) < 0.5);
    }

    // ---- doors
    [Fact]
    public void EntranceNode_IsTheDoor_ThenNoRoadFacingDoor()
    {
        var d = Fx.B(Fx.H3);
        var doors = d.Openings.Where(o => o.Type == "door").ToList();
        Assert.Single(doors);
        var entrance = Fx.Model.Value.NodeTags.Single(kv => kv.Value.ContainsKey("entrance")).Key;
        var (ex, ey) = Fx.Model.Value.Nodes[entrance];
        Assert.Equal(ex, doors[0].X, 2); Assert.Equal(ey, doors[0].Y, 2);
        Assert.Equal(0.0, doors[0].Heading);
    }

    [Fact]
    public void WithoutAnEntrance_OneDoorFacesTheNearestRoad_AtTheWallCentre()
    {
        var d = Fx.B(Fx.H1);
        var door = Assert.Single(d.Openings, o => o.Type == "door");
        Assert.Equal(0.0, door.Heading);                                      // Main Street is to the north
        var f = Fx.Finished(Fx.H1);
        double cx = f.Footprint.Take(4).Average(p => p.X);
        Assert.Equal(cx, door.X, 1);                                          // the middle of the 8 m wall
        Assert.Equal(f.Footprint.Max(p => p.Y), door.Y, 1);                   // on the north wall
    }

    [Fact]
    public void BigBuildingWithoutEntrance_GetsABackDoorOnTheOppositeWall()
    {
        var church = Fx.B(Fx.Church);   // 360 m², West Street… the road is to the west
        var doors = church.Openings.Where(o => o.Type == "door").ToList();
        Assert.Equal(2, doors.Count);
        Assert.Contains(doors, o => o.Heading == 270); Assert.Contains(doors, o => o.Heading == 90);
        var big = Fx.B(Fx.Levels3).Openings.Where(o => o.Type == "door").Select(o => o.Heading).OrderBy(h => h).ToArray();   // 400 m², West Street to the west
        Assert.Equal(new[] { 90.0, 270.0 }, big);
        Assert.Single(Fx.B(Fx.H1).Openings, o => o.Type == "door");   // 96 m²: the front door only
    }

    // ---- windows
    [Fact]
    public void Windows_HaveOneMetreMargins_NoneWithinAMetreOfADoor_NonePerPartyWall()
    {
        var north = Fx.B(Fx.H1).Openings.Where(o => o.Type == "window" && o.Storey == 0 && o.Heading == 0).ToList();
        var f = Fx.Finished(Fx.H1);
        double west = f.Footprint.Min(p => p.X);
        // 8 m wall, 1 m margins ⇒ posts at 1, 4, 7 m; the one at 4 m is the door's
        Assert.Equal(new[] { west + 1, west + 7 }, north.Select(o => Math.Round(o.X, 1)).OrderBy(x => x).Select(x => Math.Round(x, 1)).ToArray(), new DoubleComparer());
        var door = Fx.B(Fx.H1).Openings.Single(o => o.Type == "door");
        Assert.All(Fx.B(Fx.H1).Openings.Where(o => o.Type == "window"), w => Assert.True(Math.Abs(w.X - door.X) >= 1.0 || Math.Abs(w.Y - door.Y) >= 1.0));
    }

    private sealed class DoubleComparer : IEqualityComparer<double> { public bool Equals(double a, double b) => Math.Abs(a - b) < 0.11; public int GetHashCode(double d) => 0; }

    [Fact]
    public void Windows_AreAboutEvery275Metres_OnEveryStorey()
    {
        var church = Fx.B(Fx.Church);   // 18 m north wall, usable 16 m
        var xs = church.Openings.Where(o => o.Type == "window" && o.Heading == 0 && o.Storey == 0).Select(o => o.X).OrderBy(x => x).ToList();
        Assert.Equal(7, xs.Count);
        for (int i = 1; i < xs.Count; i++) Assert.InRange(xs[i] - xs[i - 1], 2.4, 3.2);
        var f = Fx.Finished(Fx.Levels3);
        Assert.Equal(new[] { 0, 1, 2 }, f.Openings.Where(o => o.Type == "window").Select(o => o.Storey).Distinct().OrderBy(s => s).ToArray());
        // z: the floor of the storey + 1 m for a window, the floor itself for a door
        Assert.All(f.Openings.Where(o => o.Type == "window"), w => Assert.Equal(f.StoreyZ[w.Storey] + 1.0, w.Z, 2));
        Assert.All(f.Openings.Where(o => o.Type == "door"), d => Assert.Equal(f.StoreyZ[0], d.Z, 2));
    }

    [Fact]
    public void GroundFloorOfIndustrialBuildings_HasNoWindows()
    {
        var mb = new ModelBuilder();
        mb.Rect(0, 0, 20, 20, T("building", "industrial"), T("building:levels", "2"));
        var d = Run(mb).Buildings.Single();
        Assert.DoesNotContain(d.Openings, o => o.Type == "window" && o.Storey == 0);
        Assert.Contains(d.Openings, o => o.Type == "window" && o.Storey == 1);
    }

    [Fact]
    public void Headings_AreCompassDegrees_InZeroTo360()
    {
        Assert.All(Fx.Buildings.Value.Buildings.SelectMany(b => b.Openings), o => Assert.InRange(o.Heading, 0.0, 359.99));
        Assert.Equal(90.0, BuildingRules.Heading(1, 0)); Assert.Equal(180.0, BuildingRules.Heading(0, -1)); Assert.Equal(270.0, BuildingRules.Heading(-1, 0));
    }

    // ---- TH-C / F-1: the ground
    [Fact]
    public void BaseZ_IsTheLowestGroundUnderTheFootprint_NotTheMean()
    {
        var dem = FuncDem.EastSlope();
        var hb = new HeightBuilder(dem, Fx.Bounds, Fx.Model.Value);
        var d = Fx.B(Fx.Church);
        var f = BuildingRules.Finish(d, hb.GroundAt);
        double lowest = d.Ring.Min(p => hb.GroundAt(p.x, p.y)), highest = d.Ring.Max(p => hb.GroundAt(p.x, p.y));
        Assert.Equal(lowest, f.BaseZ, 2);
        Assert.True(highest - f.BaseZ > 2.0, "the church stands across a slope");
        Assert.True(f.BaseZ < (lowest + highest) / 2 - 1.0, "a mean would float the downhill side");
        Assert.Equal(f.BaseZ, f.StoreyZ[0], 6);
        Assert.Equal(f.BaseZ, f.Openings.First(o => o.Type == "door").Z, 2);
    }
}
