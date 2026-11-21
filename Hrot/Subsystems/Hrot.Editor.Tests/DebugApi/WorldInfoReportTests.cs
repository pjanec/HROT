using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Fake;
using Fdp.Toolkit.Terrain;
using Hrot.Editor.DebugApi;
using Xunit;

namespace Hrot.Editor.Tests.DebugApi;

/// <summary>
/// ⭐ <c>CE-3028</c> — <c>GET /world/info</c> reports what is LIVE on the world: the resident terrain, the navmesh, and the
/// grids as CE-3018 placed them. 🔴 It used to hard-code <c>terrain</c>/<c>navmesh</c> to null and report the grids'
/// composition constants, so on a node with a terrain loaded and baked it said there was none.
/// </summary>
public sealed class WorldInfoReportTests
{
    private static readonly (double, double, double) Berlin = (52.52, 13.405, 0);

    [Fact]
    public void NoTerrain_NoSolver_ReportsNullsAndTheDefaultGrid()
    {
        using var world = new EntityRepository();

        var r = WorldInfoReport.Build(world, Berlin);

        Assert.Null(r["terrain"]);
        Assert.Null(r["navmesh"]);
        var expected = TerrainGridCoverage.PerceptionGrid(null);
        Assert.Equal(expected.OriginX, r["spatialGrid"]!["originX"]!.GetValue<float>());
        Assert.Equal(expected.CellSize, r["spatialGrid"]!["cellSize"]!.GetValue<float>());
        Assert.Equal(52.52, r["geo"]!["origin"]!["lat"]!.GetValue<double>(), precision: 2);
    }

    [Fact]
    public void ResidentTerrain_IsReported_AndTheGridsAreTheRebasedOnes()
    {
        using var world = new EntityRepository();
        var terrain = new TerrainWorld
        {
            Name = "big-town", BoundsMin = new Vector2(-2000f, -500f), BoundsMax = new Vector2(1500f, 900f), GroundZ = 2f,
        };
        world.SetSingletonManaged(terrain);

        var r = WorldInfoReport.Build(world, Berlin);

        var t = r["terrain"]!;
        Assert.Equal("big-town", t["name"]!.GetValue<string>());
        Assert.Equal(-2000f, t["bounds"]!["minX"]!.GetValue<float>());
        Assert.Equal(900f, t["bounds"]!["maxY"]!.GetValue<float>());
        Assert.Equal(2f, t["groundZ"]!.GetValue<float>());
        Assert.Equal(0, t["prisms"]!.GetValue<int>());

        foreach (var (key, expected) in new[]
                 {
                     ("spatialGrid",  TerrainGridCoverage.PerceptionGrid(terrain)),
                     ("colliderGrid", TerrainGridCoverage.ColliderGrid(terrain)),
                 })
        {
            var g = r[key]!;
            Assert.Equal(expected.OriginX, g["originX"]!.GetValue<float>());
            Assert.Equal(expected.CellSize, g["cellSize"]!.GetValue<float>());
            Assert.True(g["extent"]!["minX"]!.GetValue<float>() <= -2000f, $"{key} must cover the terrain");
        }
    }

    [Fact]
    public void Navmesh_ReportsBakedOnlyOnceATerrainMeshIsPublished()
    {
        using var world = new EntityRepository();
        var nav = new SwitchableNavmeshProvider();
        world.SetSingletonManaged<INavmeshProvider>(nav);

        Assert.False(WorldInfoReport.Build(world, Berlin)["navmesh"]!["baked"]!.GetValue<bool>());

        nav.Publish(new FakeNavmeshProvider());
        var after = WorldInfoReport.Build(world, Berlin)["navmesh"]!;
        Assert.True(after["baked"]!.GetValue<bool>());
        Assert.Equal(nameof(FakeNavmeshProvider), after["provider"]!.GetValue<string>());
    }
}
