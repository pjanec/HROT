using Hrot.Terrain.InputSet;
using Xunit;

namespace Hrot.Terrain.Osm.Tests;

public class HeightTests
{
    private static (string, string) T(string k, string v) => (k, v);
    private static HeightBuilder Town(FuncDem? dem = null) => new(dem ?? FuncDem.EastSlope(), Fx.Bounds, Fx.Model.Value);

    // ---- F-2: levelling across the road width — ONLY on the ground
    [Fact]
    public void GroundRoad_IsLevelledAcrossItsWidth_KeepingTheLengthwiseProfile()
    {
        var hb = Town();
        double centre = hb.DemAt(100, 100);
        Assert.Equal(centre, hb.GroundAt(102, 100), 3);      // inside the road: the centre line's height
        Assert.Equal(centre, hb.GroundAt(98, 100), 3);
        Assert.True(Math.Abs(hb.DemAt(102, 100) - centre) > 0.2, "the slope is there without levelling");
        // blended back to the DEM within 3 m past the edge
        Assert.Equal(hb.DemAt(110, 100), hb.GroundAt(110, 100), 3);
        double near = hb.GroundAt(104.5, 100);
        Assert.InRange(near, Math.Min(centre, hb.DemAt(104.5, 100)) - 1e-6, Math.Max(centre, hb.DemAt(104.5, 100)) + 1e-6);
    }

    [Fact]
    public void Bridge_IsNotLevelled_TheValleyUnderItIsUntouched()
    {
        var hb = Town();
        Assert.Equal(hb.DemAt(102, -150), hb.GroundAt(102, -150), 6);
        Assert.NotEqual(hb.GroundAt(100, -150), hb.GroundAt(102, -150));
    }

    [Theory]
    [InlineData("tunnel", "yes")]
    [InlineData("bridge", "viaduct")]
    [InlineData("layer", "-1")]
    [InlineData("layer", "2")]
    public void BridgeTunnelOrOtherLayer_IsNeverLevelled(string key, string value)
    {
        var mb = new ModelBuilder();
        mb.Road(0, -50, 0, 50, T(key, value));
        var hb = new HeightBuilder(FuncDem.EastSlope(), Fx.Bounds, mb.Model);
        Assert.Equal(0, hb.LevelledSegments);
        Assert.Equal(hb.DemAt(2, 0), hb.GroundAt(2, 0), 6);
    }

    [Fact]
    public void Footpaths_AndGroundRoadsAtLayer0_AreTreatedAsTheyShouldBe()
    {
        var mb = new ModelBuilder();
        mb.Line(new double[] { 0, -50, 0, 50 }, T("highway", "footway"));
        mb.Road(40, -50, 40, 50, T("layer", "0"));
        var hb = new HeightBuilder(FuncDem.EastSlope(), Fx.Bounds, mb.Model);
        Assert.Equal(1, hb.LevelledSegments);                 // the road, not the footpath
    }

    // ---- F-1: nothing flattened under buildings
    [Fact]
    public void NothingIsFlattenedUnderABuilding()
    {
        var hb = Town();
        var d = Fx.B(Fx.Church);
        foreach (var (x, y) in new[] { (112.0, 25.0), (126.0, 35.0), (119.0, 30.0) })
            Assert.Equal(hb.DemAt(x, y), hb.GroundAt(x, y), 6);
    }

    [Fact]
    public void TheLatticeInterpolation_MatchesTheExactProjection()
    {
        var hb = Town(new FuncDem((lat, lon) => lat * 10 + lon * 10));
        var (lat, lon) = AreaFrame.ToGeo(Fx.Bounds.Frame, 123.4, -78.9);
        Assert.Equal(lat * 10 + lon * 10, hb.DemAt(123.4, -78.9), 3);
    }

    // ---- the grid file
    [Fact]
    public void Write_StreamsA1mGrid_NorthRowFirst_WithTheTrueSourceResolution()
    {
        using var tmp = new TempDir();
        var dem = FuncDem.EastSlope();
        var hb = Town(dem);
        hb.Write(tmp.Combine("heights"), null, CancellationToken.None);
        var info = HeightsGrid.ReadInfo(tmp.Path);   // the folder with heights/ inside
        Assert.Equal(1.0, info.CellSize);
        Assert.Equal((int)Fx.Bounds.Width + 1, info.Cols); Assert.Equal((int)Fx.Bounds.Height + 1, info.Rows);
        Assert.Equal(30.0, info.SourceResolution);
        Assert.Equal(Fx.Bounds.MinX, info.OriginX); Assert.Equal(Fx.Bounds.MinY, info.OriginY);
        var (_, z) = HeightsGrid.ReadAll(tmp.Path);
        // the SW post is row 0 of ReadAll; the grid agrees with GroundAt
        Assert.Equal((float)hb.GroundAt(Fx.Bounds.MinX, Fx.Bounds.MinY), z[0], 3);
        Assert.Equal((float)hb.GroundAt(Fx.Bounds.MaxX, Fx.Bounds.MaxY), z[^1], 3);
    }
}
