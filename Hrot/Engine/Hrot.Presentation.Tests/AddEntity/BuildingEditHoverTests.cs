using System.Numerics;
using Fdp.Toolkit.Terrain;
using Hrot.UI.Common.AddEntity;
using Xunit;

namespace Hrot.Presentation.Tests.AddEntity;

/// <summary>
/// ⭐⭐ <c>CE-5030</c> BE-4 part 2 (📄 docs/DESIGN_Terrain_Base_Edits.md §5, mockup <c>docs/UX/terrain-base-edits.svg</c>) — the ARMED edit's cursor: the baked building the click would act
/// on is outlined (its footprint) and named — <c>Baked building · S</c> · <c>SOLID — no interior · 1 storey · 20 m²</c> · the verdict; <i>Make enterable</i> on an enterable house says
/// why not; a doorless house says <c>NO DOOR</c>; open ground says so. Reached through the placement's own ghost (<see cref="TerrainPlacement.Preview"/>), the path the gizmo draws.
/// 🔴 Red-proof: make every building SOLID in the hover ⇒ the enterable rows are red.
/// </summary>
public sealed class BuildingEditHoverTests
{
    private const string Walls = "\"walls\":[{\"from\":[0,0],\"to\":[10,0]},{\"from\":[10,0],\"to\":[10,8]},{\"from\":[10,8],\"to\":[0,8]},{\"from\":[0,8],\"to\":[0,0]}";
    private static TerrainWorld World() => TerrainWorldParser.Parse(
        "{\"type\":\"FeatureCollection\",\"hrot\":{\"schemaVersion\":1,\"bounds\":[0,0,120,60],\"groundZ\":0},\"features\":[" +
        "{\"type\":\"Feature\",\"properties\":{\"kind\":\"building\",\"label\":\"S\",\"height\":3},\"geometry\":{\"type\":\"Polygon\",\"coordinates\":[[[70,10],[75,10],[75,14],[70,14],[70,10]]]}}," +
        "{\"type\":\"Feature\",\"properties\":{\"kind\":\"building\",\"label\":\"B\",\"building\":{\"footprint\":[[0,0],[10,0],[10,8],[0,8]],\"storeys\":[{\"height\":3," + Walls + "]}]}},\"geometry\":{\"type\":\"Point\",\"coordinates\":[20,20]}}," +
        "{\"type\":\"Feature\",\"properties\":{\"kind\":\"building\",\"label\":\"D\",\"building\":{\"footprint\":[[0,0],[10,0],[10,8],[0,8]],\"storeys\":[{\"height\":3,\"walls\":[{\"from\":[0,0],\"to\":[10,0],\"openings\":[{\"kind\":\"door\",\"at\":5,\"width\":1,\"doorId\":\"front\"}]},{\"from\":[10,0],\"to\":[10,8]},{\"from\":[10,8],\"to\":[0,8]},{\"from\":[0,8],\"to\":[0,0]}]}]}},\"geometry\":{\"type\":\"Point\",\"coordinates\":[60,40]}}]}",
        "range");

    [Fact]
    public void ASolidBlock_UnderMakeEnterable_IsOutlinedAndNamed_AndTheClickWouldMakeItEnterable()
    {
        var h = BuildingEditHover.Compute(World(), "enterable", new Vector2(72f, 12f));
        Assert.True(h.Highlight);
        Assert.Equal(4, h.Outline.Length);
        Assert.Equal("Baked building · S", h.Line1);
        Assert.Equal("SOLID — no interior · 1 storey · 20 m²", h.Line2);
        Assert.Equal("click: make it enterable", h.Line3);
        Assert.True(h.Fits);
    }

    [Fact]
    public void AnEnterableHouse_UnderMakeEnterable_SaysWhyNot_AndADoorlessOneSaysNoDoor()
    {
        var h = BuildingEditHover.Compute(World(), "enterable", new Vector2(25f, 24f));
        Assert.Equal("Baked building · B · NO DOOR", h.Line1);
        Assert.StartsWith("enterable · 1 storey · 80 m²", h.Line2);
        Assert.False(h.Fits);
        Assert.StartsWith("already enterable", h.Line3);

        var d = BuildingEditHover.Compute(World(), "enterable", new Vector2(65f, 44f));
        Assert.Equal("Baked building · D", d.Line1);   // it has a door
        Assert.Equal("already enterable", d.Line3);
    }

    [Fact]
    public void OpenGround_SaysThereIsNoBakedBuilding()
    {
        var h = BuildingEditHover.Compute(World(), "remove", new Vector2(100f, 50f));
        Assert.Empty(h.Outline);
        Assert.Equal(BuildingEditHover.NoBuilding, h.Line1);
        Assert.False(h.Fits);
    }

    [Fact]
    public void TheArmedPlacement_ShowsTheHover_NotABlockGhost()
    {
        var world = World();
        var ctx = new TerrainGhostContext(world, System.IO.Path.GetTempPath());
        var placement = new TerrainPlacement(TerrainPickKind.Feature, $"{TerrainFeature.Edit}:remove:Point", () => ctx);
        var shape = placement.Preview(new Vector2(72f, 12f))!;
        Assert.True(shape.Highlight);
        Assert.Equal("Baked building · S", shape.Line1);
        Assert.StartsWith("click: remove it", shape.Line3);
    }
}
