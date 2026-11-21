#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using DotRecast.Core;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using DotRecast.Recast;
using DotRecast.Recast.Geom;
using Fdp.Toolkit.Navigation.Recast;
using Fdp.Toolkit.Terrain;
using Xunit;
using Xunit.Abstractions;

namespace Fdp.Toolkit.Navigation.Recast.Tests;

/// <summary>
/// ⭐ CE-3111 SPIKE (📄 docs/designs/navig-2/Navigation_Design_v2_0.md §14 "P2 spike") — can tiles baked at DIFFERENT cell sizes (0.3 m in the open,
/// 0.15 m over buildings) be joined into ONE Detour navmesh, with paths crossing the borders between them? 🔒 User: "Approved, start
/// with the spike". The bake today is one tile per layer (<c>RecastNavmeshBaker.BakeLayer</c>); this bakes the same world as tiles.
/// <para>World: a closed 10 × 8 m room at (20,20) with ONE 0.9 m door in its south wall (a real door width: measured to bake at 0.15 m
/// cells, never at 0.3 m — CE-3111); 12 m tiles (40 cells at 0.3 m, 80 at 0.15 m — the tile's WORLD size must match).</para>
/// </summary>
public sealed class TiledBakeSpikeTests
{
    private readonly ITestOutputHelper _out;
    public TiledBakeSpikeTests(ITestOutputHelper o) => _out = o;

    private const float TileMetres = 12f;
    private const float Coarse = 0.3f, Fine = 0.15f;

    private const string Room = """
        {"type":"FeatureCollection","hrot":{"schemaVersion":1,"bounds":[0,0,60,60],"groundZ":0},"features":[
          {"type":"Feature","properties":{"kind":"building","label":"R","doors":{"front":"open"},"building":{
             "footprint":[[0,0],[10,0],[10,8],[0,8]],
             "storeys":[{"height":3,"walls":[
                  {"from":[0,0],"to":[10,0],"openings":[{"kind":"door","at":4.55,"width":0.9,"doorId":"front"}]},
                  {"from":[10,0],"to":[10,8]},{"from":[10,8],"to":[0,8]},{"from":[0,8],"to":[0,0]}]}]}},
           "geometry":{"type":"Point","coordinates":[20,20]}}]}
        """;

    private sealed record Baked(DtNavMesh Mesh, int FineTiles, int CoarseTiles, long Ms);

    /// <summary>Bakes the world as 12 m tiles; a tile whose square overlaps <paramref name="fineWhere"/> uses 0.15 m cells.</summary>
    private static Baked BakeTiled(TerrainWorld world, Func<float, float, float, float, bool> fineWhere)
    {
        Assert.True(new TerrainWorldGeometrySource(world).TryGetTriangles(out var verts, out var indices));
        var geom = new RcSampleInputGeomProvider(verts, indices);
        RcVec3f rawMin = geom.GetMeshBoundsMin(), rawMax = geom.GetMeshBoundsMax();
        var bmin = new RcVec3f(rawMin.X, rawMin.Y - 0.5f, rawMin.Z);
        var bmax = new RcVec3f(rawMax.X, rawMax.Y + 2.3f, rawMax.Z);
        int tilesX = (int)MathF.Ceiling((bmax.X - bmin.X) / TileMetres), tilesZ = (int)MathF.Ceiling((bmax.Z - bmin.Z) / TileMetres);

        var prm = new DtNavMeshParams { orig = bmin, tileWidth = TileMetres, tileHeight = TileMetres, maxTiles = tilesX * tilesZ, maxPolys = 1 << 14 };
        var nav = new DtNavMesh();
        Assert.True(nav.Init(ref prm, 6).Succeeded());

        var sw = System.Diagnostics.Stopwatch.StartNew();
        int fine = 0, coarse = 0;
        for (int tz = 0; tz < tilesZ; tz++)
        for (int tx = 0; tx < tilesX; tx++)
        {
            float x0 = bmin.X + tx * TileMetres, z0 = bmin.Z + tz * TileMetres;
            bool isFine = fineWhere(x0, z0, x0 + TileMetres, z0 + TileMetres);
            float cs = isFine ? Fine : Coarse;
            int tileCells = (int)MathF.Round(TileMetres / cs);
            var cfg = new RcConfig(true, tileCells, tileCells, RcConfig.CalcBorder(0.3f, cs), RcPartition.WATERSHED, cs, 0.2f,
                60f, 1.8f, 0.3f, 0.4f, 2f, 10f, 12f, 1.3f, 6, 6f, 1f, true, false, true,
                new RcAreaModification(RcAreaModification.RC_AREA_FLAGS_MASK), true);
            var r = new RcBuilder().BuildTile(geom, cfg, bmin, bmax, tx, tz, new RcAtomicInteger(0), 1, false);
            var pm = r.Mesh;
            if (pm == null || pm.npolys == 0) continue;
            var flags = new int[pm.npolys];
            Array.Fill(flags, 1);
            var cp = new DtNavMeshCreateParams
            {
                verts = pm.verts, vertCount = pm.nverts, polys = pm.polys, polyAreas = pm.areas, polyFlags = flags, polyCount = pm.npolys,
                nvp = pm.nvp, walkableHeight = 1.8f, walkableRadius = 0.3f, walkableClimb = 0.4f, bmin = pm.bmin, bmax = pm.bmax,
                cs = cfg.Cs, ch = cfg.Ch, buildBvTree = true, tileX = tx, tileZ = tz,
            };
            if (r.MeshDetail is { } dm)
            {
                cp.detailMeshes = dm.meshes; cp.detailVerts = dm.verts; cp.detailVertsCount = dm.nverts;
                cp.detailTris = dm.tris; cp.detailTriCount = dm.ntris;
            }
            var data = DtNavMeshBuilder.CreateNavMeshData(cp);
            Assert.NotNull(data);
            Assert.True(nav.AddTile(data!, 0, 0, out _).Succeeded());
            if (isFine) fine++; else coarse++;
        }
        return new Baked(nav, fine, coarse, sw.ElapsedMilliseconds);
    }

    /// <summary>A complete path between two engine-space points (x east, y north), and its length; null when none or partial.</summary>
    private static float? PathLength(DtNavMesh nav, float ax, float ay, float bx, float by)
    {
        var q = new DtNavMeshQuery(nav);
        var filter = new DtQueryDefaultFilter();
        var ext = new RcVec3f(2f, 4f, 2f);
        var a = new RcVec3f(ax, 0, ay);
        var b = new RcVec3f(bx, 0, by);
        q.FindNearestPoly(a, ext, filter, out long ra, out var pa, out _);
        q.FindNearestPoly(b, ext, filter, out long rb, out var pb, out _);
        if (ra == 0 || rb == 0) return null;
        var path = new long[512];
        q.FindPath(ra, rb, pa, pb, filter, path.AsSpan(), out int n, path.Length);
        if (n == 0 || path[n - 1] != rb) return null;
        var straight = new DtStraightPath[256];
        q.FindStraightPath(pa, pb, path.AsSpan(0, n), n, straight.AsSpan(), out int sn, straight.Length, 0);
        float len = 0;
        for (int i = 1; i < sn; i++) len += RcVec3f.Distance(straight[i - 1].pos, straight[i].pos);
        return len;
    }

    [Fact]
    public void CE3111_Spike_TilesOfDifferentCellSizes_JoinIntoOneNavmesh_AndPathsCrossTheBorders()
    {
        var world = TerrainWorldParser.Parse(Room, "range");
        bool OverBuilding(float x0, float z0, float x1, float z1)
            => world.Buildings.Any(b => b.Footprint.Any(_ => true)
                                        && b.Footprint.Min(p => p.X) < x1 && b.Footprint.Max(p => p.X) > x0
                                        && b.Footprint.Min(p => p.Y) < z1 && b.Footprint.Max(p => p.Y) > z0);

        var allCoarse = BakeTiled(world, (_, _, _, _) => false);
        var allFine = BakeTiled(world, (_, _, _, _) => true);
        var mixed = BakeTiled(world, OverBuilding);
        _out.WriteLine($"all 0.3 m: {allCoarse.CoarseTiles} tiles, {allCoarse.Ms} ms · all 0.15 m: {allFine.FineTiles} tiles, {allFine.Ms} ms · " +
                       $"mixed: {mixed.FineTiles} fine + {mixed.CoarseTiles} coarse, {mixed.Ms} ms");

        // through the 0.9 m door: outside (25,10) → inside (25,25)
        var doorCoarse = PathLength(allCoarse.Mesh, 25, 10, 25, 25);
        var doorFine = PathLength(allFine.Mesh, 25, 10, 25, 25);
        var doorMixed = PathLength(mixed.Mesh, 25, 10, 25, 25);
        // in the open, across a coarse → fine tile border (x = 12): (5,16) → (17,16), straight-line 12 m
        var openMixed = PathLength(mixed.Mesh, 5, 16, 17, 16);
        _out.WriteLine($"door: coarse={doorCoarse?.ToString("F1") ?? "none"} fine={doorFine?.ToString("F1") ?? "none"} mixed={doorMixed?.ToString("F1") ?? "none"} · open border crossing (12 m): {openMixed?.ToString("F2") ?? "none"}");

        Assert.True(mixed.FineTiles > 0 && mixed.CoarseTiles > 0, "the mixed bake must really mix");
        Assert.Null(doorCoarse);                                                   // control: 0.9 m does not bake at 0.3 m
        Assert.NotNull(doorFine);                                                  // control: it does at 0.15 m
        Assert.NotNull(doorMixed);                                                 // ⭐ the question: through mixed tiles
        Assert.NotNull(openMixed);
        Assert.InRange(openMixed!.Value, 11.9f, 12.3f);                           // straight across the border, no detour
    }
}
