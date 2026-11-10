using Clipper2Lib;
using Hrot.Terrain.InputSet;
using Xunit;

namespace Hrot.Terrain.Osm.Tests;

public class AreaRulesTests
{
    private static (string, string) T(string k, string v) => (k, v);
    private static readonly AreaBounds B = Fx.Bounds;

    // ---- F-3 woods are POLYGONS
    [Fact]
    public void Woods_ArePolygonsWithADensity_HolesSplit_NotInventedTreePoints()
    {
        var v = VegetationRules.Apply(Fx.Model.Value, B);
        Assert.Equal(2, v.Woods.Count);                       // the clearing splits the wood into two hole-free parts
        Assert.All(v.Woods, w => { Assert.Equal(115.0, w.Density); Assert.Null(w.Kind); });
        Assert.InRange(v.WoodsAreaM2, 15190, 15210);          // 120 × 140 − 40 × 40
        Assert.DoesNotContain(v.Woods, w => Polygons.Contains(w.Ring, -190, 120));   // the clearing stays clear
        Assert.Contains(v.Woods, w => Polygons.Contains(w.Ring, -240, 70));
        // trees.csv holds ONLY the lone tree and the tree row — nothing inside the wood
        Assert.Equal(6, v.Trees.Count);
        Assert.DoesNotContain(v.Trees, t => t.X < -130 && t.Y > 60);
        Assert.InRange(v.EstimatedWoodsTrees, 174, 176);
    }

    [Fact]
    public void LoneTree_AndATreeRowEvery10m_MakeTheTrees()
    {
        var v = VegetationRules.Apply(Fx.Model.Value, B);
        Assert.Equal(1, v.LoneTrees);
        Assert.Equal(5, v.RowTrees);                          // a 40 m row: 0, 10, 20, 30, 40
        var row = v.Trees.Where(t => Math.Abs(t.X - (-150)) < 0.5).OrderBy(t => t.Y).ToList();
        Assert.Equal(5, row.Count);
        for (int i = 1; i < row.Count; i++) Assert.Equal(10.0, row[i].Y - row[i - 1].Y, 1);
    }

    [Fact]
    public void Scrub_IsAScrubPolygon()
    {
        var v = VegetationRules.Apply(Fx.Model.Value, B);
        var s = Assert.Single(v.Scrub);
        Assert.Equal("scrub", s.Kind); Assert.Equal(2000.0, s.Area, 0);
    }

    // ---- walls
    [Fact]
    public void Barriers_GetTheirHeightByType()
    {
        var w = BarrierRules.Apply(Fx.Model.Value, B);
        Assert.Equal(5, w.Count);
        double H(string b) => w.Single(x => x.Barrier == b).Height;
        Assert.Equal(2.0, H("wall")); Assert.Equal(1.5, H("fence")); Assert.Equal(1.5, H("hedge")); Assert.Equal(2.0, H("retaining_wall")); Assert.Equal(6.0, H("city_wall"));
        Assert.All(w, x => Assert.True(x.Thickness > 0));
    }

    [Fact]
    public void ABarrierLeavingTheArea_IsClippedToTheBounds()
    {
        var mb = new ModelBuilder();
        mb.Line(new double[] { 250, 0, 400, 0 }, T("barrier", "wall"));
        var w = Assert.Single(BarrierRules.Apply(mb.Model, ModelBuilder.Bounds));
        Assert.Equal(300, w.Line.Max(p => p.x), 6);
    }

    // ---- F-5 water
    [Fact]
    public void RiverLine_IsBufferedBy10m_StreamIsNoWater_LakeIsAPolygon()
    {
        int filled = 0;
        var water = WaterRules.Apply(Fx.Model.Value, B, ref filled);
        Assert.Equal(2, water.Count);                         // the river and the lake — not the stream
        var river = water.Single(w => w.Kind == "river");
        Assert.InRange(river.Area, B.Width * 10.0 - 10, B.Width * 10.0 + 10);         // 10 m wide, clipped to the bounds
        Assert.True(Polygons.Contains(river.Ring, 0, -154)); Assert.False(Polygons.Contains(river.Ring, 0, -156));
        var lake = water.Single(w => w.Kind == "lake");
        Assert.Equal(1500.0, lake.Area, 0);
        Assert.DoesNotContain(water, w => Polygons.Contains(w.Ring, 200, 125));   // the stream (150,100)→(250,150)
    }

    [Fact]
    public void RiverWidthTag_AndCanalDefault()
    {
        var mb = new ModelBuilder();
        mb.Line(new double[] { -100, 0, 100, 0 }, T("waterway", "river"), T("width", "30"));
        mb.Line(new double[] { -100, 100, 100, 100 }, T("waterway", "canal"));
        int f = 0;
        var water = WaterRules.Apply(mb.Model, ModelBuilder.Bounds, ref f);
        Assert.Equal(2, water.Count);
        Assert.True(water.Any(w => Polygons.Contains(w.Ring, 0, 14)) && !water.Any(w => Polygons.Contains(w.Ring, 0, 16)));      // 30 m wide
        Assert.True(water.Any(w => Polygons.Contains(w.Ring, 0, 103.9)) && !water.Any(w => Polygons.Contains(w.Ring, 0, 104.5)));  // a canal is 8 m
    }

    [Fact]
    public void Wood_OuterRingFromTwoOpenWays_IsJoinedAndAHoleSplitsOnlyWhenItIsInside()
    {
        var asm = new GeometryAssembler(Fx.Model.Value);
        var shape = asm.Areas(t => OsmTags.Is(t, "landuse", "forest")).Single();
        Assert.Equal("r9002", shape.Id);
        var region = Assert.Single(shape.Regions);
        Assert.Single(region.Holes);
        Assert.Equal(16800.0, Polygons.Area(region.Outer), 0);
    }

    [Fact]
    public void SplitHoles_CutsAcrossTheShorterAxis_ThroughTheHolesCentre()
    {
        var outer = Polygons.Rect(0, 0, 100, 50);
        var region = new PolyRegion(outer);
        region.Holes.Add(Polygons.Cw(Polygons.Rect(40, 20, 60, 30)));   // 20 wide × 10 tall: the shorter axis is y ⇒ a cut parallel to y
        int filled = 0;
        var parts = Polygons.SplitHoles(region, 12, ref filled);
        Assert.Equal(2, parts.Count);
        Assert.Equal(0, filled);
        Assert.Equal(5000.0 - 200.0, parts.Sum(p => Polygons.Area(p)), 1);
        Assert.All(parts, p => Assert.False(Polygons.Contains(p, 50, 25)));
        Assert.All(parts, p => Assert.True(Polygons.SignedArea(p) > 0));   // counter-clockwise
    }
}
