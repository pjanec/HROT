#nullable enable
using System;
using System.Numerics;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Recast;
using Fdp.Toolkit.Terrain;
using Xunit;

namespace Fdp.Toolkit.Navigation.Recast.Tests;

/// <summary>
/// ⭐ W6 — the navmesh baked from a TERRAIN WORLD (docs/DESIGN_Terrain_World.md §4.1): engine-space (Z-up) queries
/// against a mesh built from the world file, through the swappable node navmesh.
/// </summary>
public sealed class RecastNavmeshFactoryTests
{
    // A 60×60 m world with a 20×20 m, 10 m-high building in the middle.
    private const string BlockWorld = """
        {"type":"FeatureCollection","hrot":{"schemaVersion":1,"bounds":[0,0,60,60],"groundZ":0},"features":[
          {"type":"Feature","properties":{"kind":"building","height":10},
           "geometry":{"type":"Polygon","coordinates":[[[20,20],[40,20],[40,40],[20,40],[20,20]]]}}]}
        """;

    private static INavmeshProvider Bake(string json)
        => new RecastNavmeshFactory { Layers = NavLayerMask.Infantry }.Build(TerrainWorldParser.Parse(json))
           ?? throw new InvalidOperationException("bake produced no mesh");

    [Fact]
    public void GeometrySource_EmitsRecastYUp_WithWalkableFacesPointingUp()
    {
        Assert.True(new TerrainWorldGeometrySource(TerrainWorldParser.Parse(BlockWorld))
            .TryGetTriangles(out var v, out var idx));
        for (int t = 0; t < idx.Length; t += 3)
        {
            Vector3 P(int i) => new(v[idx[i] * 3], v[(idx[i] * 3) + 1], v[(idx[i] * 3) + 2]);
            var n = Vector3.Cross(P(t + 1) - P(t), P(t + 2) - P(t));
            Assert.True(n.Y >= -1e-4f, $"triangle {t / 3} faces down in Recast space: {n}");
        }
    }

    [Fact]
    public void Bake_TheBuildingIsNotWalkable_AndThePathGoesAroundIt()
    {
        var nav = Bake(BlockWorld);
        Assert.True(nav.IsWalkable(new Vector3(5, 30, 0)));
        Assert.False(nav.IsWalkable(new Vector3(30, 30, 0)), "the building footprint must not be ground");

        // West to east straight through the building — the plan must go AROUND it.
        Span<NavWaypoint> wps = stackalloc NavWaypoint[64];
        int n = nav.PlanPath(new Vector3(5, 30, 0), new Vector3(55, 30, 0), wps, (uint)NavLayerMask.Infantry);
        Assert.True(n >= 3, $"expected a detour, got {n} waypoint(s)");
        for (int i = 0; i < n; i++)
        {
            var p = wps[i].Position;
            Assert.False(p.X > 20.5f && p.X < 39.5f && p.Y > 20.5f && p.Y < 39.5f,
                $"waypoint {i} {p} is inside the building");
            Assert.InRange(p.Z, -0.5f, 0.5f);   // Z-up: the ground is at Z = 0
        }
        Assert.Equal(55f, wps[n - 1].Position.X, 0.5f);
    }

    [Fact]
    public void SwitchableProvider_AnswersStraightLinesUntilABakeIsPublished_ThenTheBake()
    {
        var node = new SwitchableNavmeshProvider();
        Assert.False(node.HasBakedMesh);
        Assert.True(node.IsWalkable(new Vector3(30, 30, 0)));      // fallback: everywhere walkable
        uint before = node.QueryVersion();

        node.Publish(Bake(BlockWorld));
        Assert.True(node.HasBakedMesh);
        Assert.False(node.IsWalkable(new Vector3(30, 30, 0)));
        Assert.NotEqual(before, node.QueryVersion());

        node.Publish(null);                                         // terrain unload
        Assert.False(node.HasBakedMesh);
    }
}
