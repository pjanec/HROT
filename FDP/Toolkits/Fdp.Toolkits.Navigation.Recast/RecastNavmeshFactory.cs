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
public sealed class RecastNavmeshFactory : IIncrementalNavmeshFactory
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

    /// <summary>⭐ O-4 (CE-4415) — a PREFILL bake (<see cref="NavTileCacheMode.Prefill"/>) also reads the world's seed folders, so a tile a seed already has is a hit and is NOT written: the overlay compile writes only the tiles the base lacks.
    /// ⛔ Off for the importer's prefill of a terrain's own folder (it is its own seed).</summary>
    public bool SeedPrefill { get; init; }

    /// <summary>⭐ CE-4393 (N-3) — tiles done / total as the bake goes (the importer's "navmesh" stage); called from worker threads.</summary>
    public Action<int, int>? Progress { get; init; }

    /// <summary>⭐ CE-4393 (N-3) — cancels the bake between tiles.</summary>
    public System.Threading.CancellationToken Cancel { get; init; }

    /// <summary>What the last bake did (tiles, fine tiles, baked, from the cache, ms).</summary>
    public RecastNavmeshBaker.BakeStats LastStats { get; private set; }

    public INavmeshProvider? Build(TerrainWorld world) => Build(world, null);

    /// <summary>
    /// ⭐ O-7 A (§18e) — as <see cref="Build(TerrainWorld)"/>, told WHERE the world changed since this factory's previous bake (engine XY rectangles): the tiles outside them are taken by their previous key,
    /// with no gather and no hash. <c>null</c> = unknown = the full bake. Ignored (a full bake) when the previous bake's world bounds, layers or tile size differ.
    /// </summary>
    public INavmeshProvider? Build(TerrainWorld world, IReadOnlyList<(Vector2 Min, Vector2 Max)>? dirty)
    {
        var meshes = BakeMeshes(world, out var doorways, dirty);
        return meshes == null || meshes.Count == 0 ? null
            : new DotRecastNavmeshProvider(meshes, world, doorways) { TileMetres = TileMetres, TileKeys = _lastTileKeys };
    }

    /// <summary>
    /// ⭐ R-218 P2 — the touched-tile rebuild: re-bakes <paramref name="world"/> through the tile cache (only the tiles whose inputs
    /// changed are baked) and swaps the result into <paramref name="provider"/> as ONE new snapshot; queries in flight finish on the
    /// old one (P1). Off the main thread, like the load bake. Returns false (provider untouched) when nothing is walkable.
    /// </summary>
    public bool Rebake(DotRecastNavmeshProvider provider, TerrainWorld world) => Rebake(provider, world, null);

    /// <summary>⭐ O-7 A — <see cref="Rebake(DotRecastNavmeshProvider, TerrainWorld)"/> told where the world changed (see <see cref="Build(TerrainWorld, IReadOnlyList{ValueTuple{Vector2, Vector2}})"/>).</summary>
    public bool Rebake(DotRecastNavmeshProvider provider, TerrainWorld world, IReadOnlyList<(Vector2 Min, Vector2 Max)>? dirty)
    {
        var meshes = BakeMeshes(world, out var doorways, dirty);
        if (meshes == null || meshes.Count == 0) return false;
        provider.Rebake(meshes, world, doorways);
        return true;
    }

    private IReadOnlyDictionary<long, ulong> _lastTileKeys = new Dictionary<long, ulong>();

    // ⭐ O-7 A — the previous bake's key map and what it was baked for: a dirty bake only trusts it for the same world bounds, layers and tile size
    private IReadOnlyDictionary<(NavLayerMask Layer, int Ax, int Az), NavTileKey>? _previousKeys;
    private (Vector2 Min, Vector2 Max, NavLayerMask Layers, float Tile) _previousFor;

    /// <summary>⭐ O-7 A — counts this factory's bakes: a caller that keeps "the world this factory's last bake was of" compares serials to know the key map still describes it (an abandoned staged bake moves it).</summary>
    public long BakeSerial { get; private set; }

    /// <summary>⭐ O-7 A — the last bake's full tile-key map <c>(layer, ax, az) → key</c> (what a rail compares between a dirty and a full bake).</summary>
    public IReadOnlyDictionary<(NavLayerMask Layer, int Ax, int Az), NavTileKey> LastKeys => _previousKeys ?? new Dictionary<(NavLayerMask, int, int), NavTileKey>();

    private Dictionary<NavLayerMask, DotRecast.Detour.DtNavMesh>? BakeMeshes(TerrainWorld world, out IReadOnlyList<NavDoorways.Volume> doorways, IReadOnlyList<(Vector2 Min, Vector2 Max)>? dirty)
    {
        doorways = Array.Empty<NavDoorways.Volume>();
        // ⭐ CE-4366 (L-4) — per TILE from the terrain (TerrainTileTriangles): the whole soup is never built here (scale-1 at 1 m: 61 M
        //   triangles, 7 GB peak); each tile gets exactly the triangles the whole-soup bake gave it
        var source = new TerrainTileTriangles(world);
        // ⭐ Stage 5c — the doorways become their own polygons, judged at query time by the door's live state (§3j "5c").
        Fdp.Core.LoadProgress.Report();   // ⭐ CE-2417 — between the bake's stages (docs/DESIGN_Cluster_Load_Phase.md §12)
        doorways = NavDoorways.For(world);
        Fdp.Core.LoadProgress.Report();
        // ⭐ CE-4393 (UTM §12.1 N-1) — the tiles the importer baked into the terrain folder, read only, before this machine's own folder
        NavTileCache? seed = null;
        if (TileCache != null)
        {
            // ⭐ O-2 (CE-4416) — every seed folder of the world: the terrain's, then an overlay's (TerrainWorld.SeedFolders)
            List<string>? seedFolders = null;
            foreach (var folder in world.SeedFolders)
            {
                var navtiles = System.IO.Path.Combine(folder, NavTileCache.TerrainFolderName);
                if (System.IO.Directory.Exists(navtiles) && !ReferenceEquals(TileCache.Folder, navtiles) && (TileCache.Mode != NavTileCacheMode.Prefill || SeedPrefill))
                    (seedFolders ??= new List<string>()).Add(navtiles);
            }
            if (seedFolders != null) seed = NavTileCache.ForSeeds(seedFolders);
        }
        bool same = _previousKeys != null && _previousFor == (world.BoundsMin, world.BoundsMax, Layers, TileMetres);
        var baker = new RecastNavmeshBaker
        {
            TileMetres = TileMetres, Cache = TileCache, Seed = seed, Progress = Progress, Cancel = Cancel,
            Previous = same ? _previousKeys : null, Dirty = same ? dirty : null,
        };
        // ⭐ CE-4369 (L-7) — the woods first, the doorways after (a doorway's area wins where both overlap); the provider gets the doorways only
        var woods = NavDoorways.Woods(world);
        IReadOnlyList<NavDoorways.Volume> volumes = doorways;
        if (woods.Count > 0)
        {
            var all = new List<NavDoorways.Volume>(woods.Count + doorways.Count);
            all.AddRange(woods); all.AddRange(doorways);
            volumes = all;
        }
        var meshes = baker.Bake(source, Layers, volumes, FineAreas(world));
        LastStats = baker.LastStats;
        _lastTileKeys = baker.LastTileKeys;   // ⭐ CE-4391 S-3 (P3-a)
        _previousKeys = baker.LastKeys; _previousFor = (world.BoundsMin, world.BoundsMax, Layers, TileMetres); BakeSerial++;
        var st = LastStats;
        Fdp.Core.Logging.FdpLog<RecastNavmeshFactory>.Info(
            $"[Navmesh] '{world.Name}': {st.Tiles} tiles ({st.FineTiles} fine) — {st.Baked} baked, {st.FromCache} from the cache, {st.Milliseconds} ms "
          + $"(parallel {st.ParallelMs} ms wall; thread-ms: gather {st.GatherMs}, hash {st.HashMs}, cache {st.CacheMs}, bake {st.BakeMs}; assemble {st.AssembleMs} ms wall; {st.Reused} reused).");
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
