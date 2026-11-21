using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Terrain;
using Hrot.Map.Common;
using Hrot.UI.Common.AddEntity;
using Xunit;

namespace Hrot.Presentation.Tests.AddEntity;

/// <summary>
/// ⭐⭐ <c>CE-5030</c> BE-4 (📄 docs/DESIGN_Terrain_Base_Edits.md D-1, D-5) — a building EDIT is born naming the BAKED building under the click: a record's footprint or a solid block's;
/// a click on open ground, or on a placed overlay house (not baked — its label carries a <c>/</c>), places NOTHING. 🔴 Red-proof: return true for no building ⇒ the open-ground row is red.
/// </summary>
public sealed class BuildingEditBirthTests
{
    private static TerrainWorld World() => TerrainWorldParser.Parse(
        "{\"type\":\"FeatureCollection\",\"hrot\":{\"schemaVersion\":1,\"bounds\":[0,0,120,60],\"groundZ\":0},\"features\":[" +
        "{\"type\":\"Feature\",\"properties\":{\"kind\":\"building\",\"label\":\"S\",\"height\":3},\"geometry\":{\"type\":\"Polygon\",\"coordinates\":[[[70,10],[75,10],[75,14],[70,14],[70,10]]]}}," +
        "{\"type\":\"Feature\",\"properties\":{\"kind\":\"building\",\"label\":\"B\",\"building\":{\"footprint\":[[0,0],[10,0],[10,8],[0,8]],\"storeys\":[{\"height\":3,\"walls\":[{\"from\":[0,0],\"to\":[10,0]},{\"from\":[10,0],\"to\":[10,8]},{\"from\":[10,8],\"to\":[0,8]},{\"from\":[0,8],\"to\":[0,0]}]}]}},\"geometry\":{\"type\":\"Point\",\"coordinates\":[20,20]}}," +
        "{\"type\":\"Feature\",\"properties\":{\"kind\":\"building\",\"label\":\"g/0\",\"building\":{\"footprint\":[[0,0],[6,0],[6,6],[0,6]],\"storeys\":[{\"height\":3,\"walls\":[{\"from\":[0,0],\"to\":[6,0]},{\"from\":[6,0],\"to\":[6,6]},{\"from\":[6,6],\"to\":[0,6]},{\"from\":[0,6],\"to\":[0,0]}]}]}},\"geometry\":{\"type\":\"Point\",\"coordinates\":[40,40]}}]}",
        "range");

    private static List<object> Born(string action)
    {
        var c = new List<object>();
        TerrainBirth.BornWith(TkbEntityTypes.TerrainFeature, $"feature:{TerrainFeature.Edit}:{action}:Point", Quaternion.Identity, c);
        return c;
    }

    [Theory]
    [InlineData(72f, 12f, "S")]    // a solid block
    [InlineData(25f, 24f, "B")]    // a building record
    public void AnEdit_IsBornNamingTheBakedBuildingUnderTheClick(float x, float y, string label)
    {
        var c = Born("remove");
        Assert.True(TerrainBirth.ResolveEdit(World(), new Vector2(x, y), c));
        var f = Assert.Single(c.OfType<TerrainFeature>());
        Assert.Equal(TerrainFeature.Edit, f.Kind);
        Assert.Equal("remove", f.Template.ToString());
        Assert.Equal(label, f.Target.ToString());
        Assert.Contains(label, c.OfType<EntityInfo>().Single().Name.ToString());
    }

    [Theory]
    [InlineData(55f, 50f)]   // open ground
    [InlineData(43f, 43f)]   // a placed overlay house: not baked
    public void AClickOnNoBakedBuilding_PlacesNothing(float x, float y)
        => Assert.False(TerrainBirth.ResolveEdit(World(), new Vector2(x, y), Born("enterable")));

    [Fact]
    public void AnyOtherEntity_IsLeftAlone()
        => Assert.True(TerrainBirth.ResolveEdit(World(), new Vector2(55f, 50f), new List<object> { new TerrainFeature { Kind = TerrainFeature.Tree } }));
}
