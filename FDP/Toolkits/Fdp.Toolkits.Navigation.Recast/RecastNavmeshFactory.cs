using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Terrain;

namespace Fdp.Toolkit.Navigation.Recast;

/// <summary>
/// ⭐ <b>The terrain world as Recast input</b> — <see cref="TerrainWorldMesh"/> (engine, Z-up) swizzled to Recast's
/// Y-up and re-wound so walkable faces point +Y (docs/DESIGN_Terrain_World.md §3, W7: the conversion lives only
/// INSIDE the Recast implementation).
/// <para>⚠ The swizzle <c>(x, y, z) → (x, z, y)</c> is a reflection, so it flips handedness: an engine triangle
/// wound counter-clockwise seen from above would face DOWN in Recast. Every triangle is therefore re-wound.</para>
/// </summary>
public sealed class TerrainWorldGeometrySource : ISceneGeometrySource
{
    private readonly TerrainWorld _world;
    private readonly float _cellSize;

    /// <param name="cellSize">Ground grid cell size; 0 ⇒ <see cref="TerrainWorldMesh.DefaultCellSize"/>.</param>
    public TerrainWorldGeometrySource(TerrainWorld world, float cellSize = 0f)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _cellSize = cellSize;
    }

    public bool TryGetTriangles(out float[] verts, out int[] indices)
    {
        TerrainWorldMesh.Build(_world, out Vector3[] v, out int[] i, _cellSize);
        verts = new float[v.Length * 3];
        for (int k = 0; k < v.Length; k++)
        {
            verts[(k * 3) + 0] = v[k].X;   // east
            verts[(k * 3) + 1] = v[k].Z;   // up
            verts[(k * 3) + 2] = v[k].Y;   // north
        }
        indices = new int[i.Length];
        for (int t = 0; t + 2 < i.Length; t += 3)
        {
            indices[t]     = i[t];
            indices[t + 1] = i[t + 2];      // re-wind: the swizzle mirrored the triangle
            indices[t + 2] = i[t + 1];
        }
        return indices.Length > 0;
    }
}

/// <summary>
/// ⭐⭐ <b>W6 — the DotRecast navmesh for a terrain world</b> (docs/DESIGN_Terrain_World.md §4.1). Called OFF the main
/// thread from <c>TerrainResidency.Prepare</c>; the returned provider is published into the node's
/// <see cref="SwitchableNavmeshProvider"/> at commit.
/// <para>⭐⭐ TILED (CE-3111 · CE-1029 · R-218 P2 — 📄 docs/designs/navig-2/Navigation_Design_v2_0.md §14 "P2 as built"): infantry
/// tiles over a building bake at 0.15 m (real 0.8–0.9 m doors pass), everything else at 0.3 m; tiles bake in parallel and come
/// from <see cref="TileCache"/> when their inputs are unchanged — a second load bakes nothing, and <see cref="Rebake"/> after a
/// geometry change bakes only the tiles it touched.</para>
/// </summary>
public sealed class RecastNavmeshFactory : INavmeshFactory
{
    /// <summary>The movement layers baked (default: infantry + vehicles).</summary>
    public NavLayerMask Layers { get; init; } = NavLayerMask.Infantry | NavLayerMask.Vehicle;

    /// <summary>The tile edge (m) — <see cref="RecastNavmeshBaker.DefaultTileMetres"/>.</summary>
    public float TileMetres { get; init; } = RecastNavmeshBaker.DefaultTileMetres;

    /// <summary>
    /// The tile cache (default: the process-wide <see cref="NavTileCache.Default"/> — memory + this machine's folder). Null ⇒ every
    /// tile bakes on every load.
    /// </summary>
    public NavTileCache? TileCache { get; init; } = NavTileCache.Default;

    /// <summary>What the last bake did (tiles, fine tiles, baked, from the cache, ms).</summary>
    public RecastNavmeshBaker.BakeStats LastStats { get; private set; }

    public INavmeshProvider? Build(TerrainWorld world)
    {
        var meshes = BakeMeshes(world, out var doorways);
        return meshes == null || meshes.Count == 0 ? null : new DotRecastNavmeshProvider(meshes, world, doorways);
    }

    /// <summary>
    /// ⭐ R-218 P2 — the touched-tile rebuild: re-bakes <paramref name="world"/> through the tile cache (only the tiles whose inputs
    /// changed are baked) and swaps the result into <paramref name="provider"/> as ONE new snapshot; queries in flight finish on the
    /// old one (P1). Off the main thread, like the load bake. Returns false (provider untouched) when nothing is walkable.
    /// </summary>
    public bool Rebake(DotRecastNavmeshProvider provider, TerrainWorld world)
    {
        var meshes = BakeMeshes(world, out var doorways);
        if (meshes == null || meshes.Count == 0) return false;
        provider.Rebake(meshes, world, doorways);
        return true;
    }

    private Dictionary<NavLayerMask, DotRecast.Detour.DtNavMesh>? BakeMeshes(TerrainWorld world, out IReadOnlyList<NavDoorways.Volume> doorways)
    {
        doorways = Array.Empty<NavDoorways.Volume>();
        if (!new TerrainWorldGeometrySource(world).TryGetTriangles(out var verts, out var indices)) return null;
        // ⭐ Stage 5c — the doorways become their own polygons, judged at query time by the door's live state (§3j "5c").
        doorways = NavDoorways.For(world);
        var baker = new RecastNavmeshBaker { TileMetres = TileMetres, Cache = TileCache };
        var meshes = baker.Bake(verts, indices, Layers, doorways, FineAreas(world));
        LastStats = baker.LastStats;
        var st = LastStats;
        Fdp.Core.Logging.FdpLog<RecastNavmeshFactory>.Info(
            $"[Navmesh] '{world.Name}': {st.Tiles} tiles ({st.FineTiles} fine) — {st.Baked} baked, {st.FromCache} from the cache, {st.Milliseconds} ms.");
        return meshes;
    }

    /// <summary>
    /// ⭐ CE-3111 — where infantry bakes fine: every building's footprint box (doors, interior walls, stairs — the places a 0.3 m
    /// cell cannot resolve) and every doorway (a door may stand in a wall outside any building). RECAST x/z = engine x/y.
    /// </summary>
    public static IReadOnlyList<RecastNavmeshBaker.Area> FineAreas(TerrainWorld world)
    {
        var areas = new List<RecastNavmeshBaker.Area>(world.Buildings.Count + world.Doors.Count);
        foreach (var b in world.Buildings)
        {
            if (b.Footprint.Length == 0) continue;
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            foreach (var pt in b.Footprint) { x0 = MathF.Min(x0, pt.X); x1 = MathF.Max(x1, pt.X); y0 = MathF.Min(y0, pt.Y); y1 = MathF.Max(y1, pt.Y); }
            areas.Add(new RecastNavmeshBaker.Area(x0, y0, x1, y1));
        }
        foreach (var d in world.Doors)
            areas.Add(new RecastNavmeshBaker.Area(d.Center.X - 1f, d.Center.Y - 1f, d.Center.X + 1f, d.Center.Y + 1f));
        return areas;
    }
}
