using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DotRecast.Detour;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Terrain;
using Xunit;

namespace Fdp.Toolkit.Navigation.Recast.Tests;

/// <summary>
/// ⭐ <c>CE-4366</c> (L-4, docs/DESIGN_Scale_Rig.md §5) — the bake from the terrain's TILE source (no whole soup) is the bake from the whole
/// soup: every tile's inputs are the same (its cache key — a bake into a cache, then the other bake from it, bakes nothing) and every tile's
/// baked data is the same, byte for byte, on the shipped terrains (buildings, doors, heights, bridges, water, woods).
/// </summary>
public class TileSourceEqualityTests
{
    private static TerrainWorld Load(string name)
    {
        var folder = Path.Combine(ContentLayout.TerrainAssets(ContentLayout.TryFindSourceTree()!), name);
        var world = Directory.GetFiles(folder, "*.world.geojson").Single();
        return TerrainWorldParser.Parse(File.ReadAllText(world), name, TerrainAssets.ForFolder(folder));
    }

    private static Dictionary<NavLayerMask, DtNavMesh> WholeSoup(TerrainWorld w, NavTileCache? cache, out RecastNavmeshBaker.BakeStats stats)
    {
        Assert.True(new TerrainWorldGeometrySource(w).TryGetTriangles(out var v, out var i));
        var baker = new RecastNavmeshBaker { Cache = cache };
        var m = baker.Bake(v, i, NavLayerMask.Infantry | NavLayerMask.Vehicle, NavDoorways.For(w), RecastNavmeshFactory.FineAreas(w));
        stats = baker.LastStats;
        return m;
    }

    private static Dictionary<NavLayerMask, DtNavMesh> PerTile(TerrainWorld w, NavTileCache? cache, out RecastNavmeshBaker.BakeStats stats)
    {
        var baker = new RecastNavmeshBaker { Cache = cache };
        var m = baker.Bake(new TerrainTileTriangles(w), NavLayerMask.Infantry | NavLayerMask.Vehicle, NavDoorways.For(w), RecastNavmeshFactory.FineAreas(w));
        stats = baker.LastStats;
        return m;
    }

    [Theory]
    [InlineData("demo-city")]
    [InlineData("test-town")]
    [InlineData("bt-range")]
    [InlineData("highway-rest")]
    [InlineData("basic-desert")]
    public void CE4366_ThePerTileBake_IsTheWholeSoupBake(string terrain)
    {
        var w = Load(terrain);

        // ① the same inputs: the whole-soup bake fills a cache, the per-tile bake then finds every tile in it
        var dir = Path.Combine(Path.GetTempPath(), "ce4366-" + Guid.NewGuid().ToString("N"));
        try
        {
            WholeSoup(w, new NavTileCache(dir), out var a);
            PerTile(w, new NavTileCache(dir), out var b);
            Assert.Equal(a.Tiles, b.Tiles);
            Assert.Equal(a.FineTiles, b.FineTiles);
            Assert.Equal(0, b.Baked);
            Assert.Equal(b.Tiles, b.FromCache);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }

        // ② the same output: both bakes fresh (no cache), tile by tile
        var whole = WholeSoup(w, null, out _);
        var tiled = PerTile(w, null, out _);
        Assert.Equal(whole.Keys.OrderBy(k => k), tiled.Keys.OrderBy(k => k));
        foreach (var layer in whole.Keys)
        {
            var x = Tiles(whole[layer]); var y = Tiles(tiled[layer]);
            Assert.Equal(x.Keys.OrderBy(k => k), y.Keys.OrderBy(k => k));
            foreach (var (k, a) in x)
            {
                var b = y[k];
                Assert.Equal(a.header.polyCount, b.header.polyCount);
                Assert.Equal(a.verts, b.verts);
                Assert.Equal(a.detailVerts, b.detailVerts);
                Assert.Equal(a.polys.Select(p => (p.GetArea(), string.Join(",", p.verts))), b.polys.Select(p => (p.GetArea(), string.Join(",", p.verts))));
            }
        }
    }

    private static Dictionary<(int, int), DtMeshData> Tiles(DtNavMesh mesh)
    {
        var d = new Dictionary<(int, int), DtMeshData>();
        for (int i = 0; i < mesh.GetMaxTiles(); i++)
        {
            var t = mesh.GetTile(i);
            if (t?.data?.header != null) d[(t.data.header.x, t.data.header.y)] = t.data;
        }
        return d;
    }
}
