#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using DotRecast.Core;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using DotRecast.Recast;
using DotRecast.Recast.Geom;
using Fdp.Toolkit.Navigation;

namespace Fdp.Toolkit.Navigation.Recast;

/// <summary>
/// Bakes per-<see cref="NavLayerMask"/> DotRecast navmeshes from a triangle soup.
///
/// <para>
/// <b>Coordinate convention (the #1 risk).</b>
/// ⭐ The baker's input is in <b>RECAST space</b> (Y-up): <c>Vector3(x_east, up, z_north)</c> — the same as Stride world
/// space. It is NOT a navigation API: the engine-wide navigation contract is Z-up (R-182 / W7) and the swizzle
/// <c>(x, y, z) → (x, z, y)</c> happens inside <see cref="DotRecastNavmeshProvider"/> (queries) and in whichever
/// <see cref="ISceneGeometrySource"/> produces the triangles (a Stride source gets Y-up for free; an engine-space source
/// must swizzle itself). The synthetic soups used in headless tests are authored directly in Recast space.
/// </para>
///
/// <para>
/// DotRecast itself is Y-up (X=East, Y=Up, Z=North for axis-aligned geometry), which
/// matches navmesh-query space exactly — no additional swizzle is needed inside the baker.
/// </para>
///
/// <para>
/// <b>Per-layer params</b> (design §10.1):
/// <list type="table">
///   <item><term>Infantry</term><description>agent radius 0.3 m, max slope 60°, step 0.4 m, height 1.8 m</description></item>
///   <item><term>Vehicle</term> <description>agent radius 1.5 m, max slope 20°, step 0.1 m, height 2.0 m</description></item>
///   <item><term>Naval</term>   <description>agent radius 1.0 m, max slope 5°,  step 0.05 m, height 1.0 m</description></item>
///   <item><term>Air</term>     <description>agent radius 2.0 m, max slope 90°, step 0.5 m, height 2.0 m</description></item>
/// </list>
/// </para>
/// </summary>
public sealed class RecastNavmeshBaker
{
    // ── Layer parameter table ────────────────────────────────────────────────

    /// <summary>
    /// Per-layer bake parameters passed to DotRecast <see cref="RcConfig"/>.
    /// </summary>
    public readonly struct LayerParams
    {
        /// <summary>Horizontal radius of the agent capsule (metres).</summary>
        public float AgentRadius { get; init; }

        /// <summary>Maximum walkable slope angle (degrees).</summary>
        public float MaxSlope { get; init; }

        /// <summary>Maximum climbable step height (metres).</summary>
        public float MaxStepHeight { get; init; }

        /// <summary>Minimum agent standing height (metres).</summary>
        public float AgentHeight { get; init; }
    }

    // Design §10.1 values.
    private static readonly LayerParams InfantryParams = new()
    {
        AgentRadius   = 0.3f,
        MaxSlope      = 60f,
        MaxStepHeight = 0.4f,
        AgentHeight   = 1.8f,
    };

    // ⭐ CE-3027 (user-approved 2026-10-03): the Vehicle layer is baked at the widest vehicle class's hull half-width
    //   (Bradley, 3.6 m wide ⇒ 1.8 m). ⛔ SUPERSEDED: 1.5 m (§10.1) — narrower than the hull, so planned paths let a
    //   tank's side scrape building corners.
    private static readonly LayerParams VehicleParams = new()
    {
        AgentRadius   = 1.8f,
        MaxSlope      = 20f,
        // ⭐ CE-1048 (user-approved 2026-10-11) — at least ONE voxel (CellHeight 0.2 m): Recast's climb is floor(step / cell), and on
        //   relief neighbouring spans sit one voxel apart, so a step below a voxel filtered EVERY span on a slope as a ledge — the
        //   Vehicle layer was empty on any hill. 0.3 m covers a 20° slope over a 0.3 m cell (0.11 m) and a curb.
        //   ⛔ SUPERSEDED: 0.1 m (§10.1). 📄 docs/DESIGN_Terrain_Height.md §3.4.
        MaxStepHeight = 0.3f,
        AgentHeight   = 2.0f,
    };

    private static readonly LayerParams NavalParams = new()
    {
        AgentRadius   = 1.0f,
        MaxSlope      = 5f,
        MaxStepHeight = 0.05f,
        AgentHeight   = 1.0f,
    };

    private static readonly LayerParams AirParams = new()
    {
        AgentRadius   = 2.0f,
        MaxSlope      = 90f,
        MaxStepHeight = 0.5f,
        AgentHeight   = 2.0f,
    };

    // ── Tiling (CE-3111 / CE-1029 / R-218 P2) ────────────────────────────────

    /// <summary>
    /// ⭐ The tile edge (m). A multiple of BOTH cell sizes (<see cref="CoarseCellSize"/>, <see cref="FineCellSize"/>): every tile
    /// covers the same square of the world whatever its cell, so Detour can join a fine tile to a coarse one (the CE-3111 spike).
    /// </summary>
    public const float DefaultTileMetres = 24f;

    /// <summary>The cell of an ordinary tile (m).</summary>
    public const float CoarseCellSize = 0.3f;

    /// <summary>
    /// The cell of an INFANTRY tile over a building (m): a real 0.8–0.9 m door bakes at 0.15 m and not at 0.3 m (CE-3111 —
    /// 0.9 m: 5/5 grid alignments vs 0/5).
    /// </summary>
    public const float FineCellSize = 0.15f;

    /// <summary>The voxel height (m), every tile.</summary>
    public const float CellHeight = 0.2f;

    /// <summary>
    /// ⛔ BUMP whenever the bake changes what a tile CONTAINS (a setting below, the Recast filters, the area marking): it is part of
    /// every tile's cache key, so a stale disk cache can never serve a tile baked by older code.
    /// </summary>
    public const int BakeFormat = 1;

    /// <summary>How far past a tile a fine area still makes it fine (m) — a door in a wall ON a tile edge is fine on both sides.</summary>
    public const float FineMarginMetres = 1f;

    /// <summary>A rectangle in RECAST space (x east, z north) where infantry tiles bake at <see cref="FineCellSize"/>.</summary>
    public readonly record struct Area(float MinX, float MinZ, float MaxX, float MaxZ);

    /// <summary>What the last <see cref="Bake"/> did (a rail and the load log read it).</summary>
    /// <param name="GatherMs">⭐ O-7 — THREAD time (summed over the worker threads) spent gathering and binning each tile's triangles (<c>TileTriangles</c>).</param>
    /// <param name="HashMs">THREAD time hashing tiles (<c>HashTile</c>; only with a <see cref="Cache"/>).</param>
    /// <param name="CacheMs">THREAD time in <c>Cache.TryGet</c> (memory lookup, seed/disk read, deserialise).</param>
    /// <param name="BakeMs">THREAD time baking the tiles that missed (Recast + <c>Cache.Put</c>).</param>
    /// <param name="ParallelMs">WALL time of the parallel tile section.</param>
    /// <param name="AssembleMs">WALL time of the serial assembly (the <c>AddTile</c> loop) over every layer.</param>
    public readonly record struct BakeStats(int Tiles, int FineTiles, int Baked, int FromCache, long Milliseconds,
        long GatherMs = 0, long HashMs = 0, long CacheMs = 0, long BakeMs = 0, long ParallelMs = 0, long AssembleMs = 0);

    /// <summary>The tile edge (m); must be a multiple of both cell sizes.</summary>
    public float TileMetres { get; init; } = DefaultTileMetres;

    /// <summary>The tile cache; null ⇒ every tile is baked.</summary>
    public NavTileCache? Cache { get; init; }

    /// <summary>⭐ CE-4393 (UTM §12.1 N-1) — the terrain's prefilled tiles (read only), consulted between <see cref="Cache"/>'s memory and its folder.</summary>
    public NavTileCache? Seed { get; init; }

    /// <summary>⭐ CE-4393 (N-3) — called as tiles finish (done, total), from the bake's worker threads (thread-safe callers only).</summary>
    public Action<int, int>? Progress { get; init; }

    /// <summary>⭐ CE-4393 (N-3) — cancels the bake between tiles (throws <see cref="OperationCanceledException"/>).</summary>
    public System.Threading.CancellationToken Cancel { get; init; }

    /// <summary>
    /// ⭐ <c>CE-4391</c> S-3 (P3-a) — each tile's CONTENT key from the last <see cref="Bake"/> (all layers folded together; <c>((long)ax &lt;&lt; 32) | (uint)az</c>): the tile
    /// cache's own hash, so two bakes agree on a tile exactly when its inputs are equal. Empty when <see cref="Cache"/> is null (no key is computed then).
    /// </summary>
    public IReadOnlyDictionary<long, ulong> LastTileKeys { get; private set; } = new Dictionary<long, ulong>();

    /// <summary>The stats of the last <see cref="Bake"/>.</summary>
    public BakeStats LastStats { get; private set; }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Gets the baked <see cref="LayerParams"/> used for the last bake of each layer.
    /// Populated by <see cref="Bake"/>.  Keyed by the layer's single-bit
    /// <see cref="NavLayerMask"/> value.
    /// </summary>
    public IReadOnlyDictionary<NavLayerMask, LayerParams> BakedParams => _bakedParams;

    private readonly Dictionary<NavLayerMask, LayerParams> _bakedParams = new();

    // ── Bake ────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ One flat ground quad (X = East, Y = 0 altitude, Z = North) baked for <paramref name="layers"/> — the shared way a test or harness
    /// gets a walkable world without a terrain asset. ⛔ Throws if a requested layer baked no polygons.
    /// </summary>
    public static Dictionary<NavLayerMask, DtNavMesh> BakeFlatGround(
        float xMin, float xMax, float zMin, float zMax,
        NavLayerMask layers = NavLayerMask.Infantry | NavLayerMask.Vehicle)
    {
        float[] verts =
        {
            xMin, 0f, zMin,
            xMax, 0f, zMin,
            xMax, 0f, zMax,
            xMin, 0f, zMax,
        };
        int[] indices = { 0, 2, 1,  0, 3, 2 };

        var meshes = new RecastNavmeshBaker().Bake(verts, indices, layers);
        foreach (NavLayerMask layer in LayerBits)
            if ((layers & layer) != 0 && !meshes.ContainsKey(layer))
                throw new InvalidOperationException($"{layer} navmesh bake failed for flat ground.");
        return meshes;
    }

    /// <summary>
    /// Bakes DotRecast navmeshes for the requested layers from the provided triangle soup.
    /// <para>⭐⭐ TILED (📄 docs/designs/navig-2/Navigation_Design_v2_0.md §14 "P2 as built"): the world is cut into
    /// <see cref="TileMetres"/> squares on a grid anchored at the world origin; INFANTRY tiles touching a
    /// <paramref name="fineAreas"/> rectangle bake at <see cref="FineCellSize"/>, every other tile at <see cref="CoarseCellSize"/>;
    /// the tiles bake in parallel, and a tile whose inputs are already in <see cref="Cache"/> is not baked at all.</para>
    /// </summary>
    /// <param name="verts">
    /// Flat vertex array in navmesh-query space: <c>[x0, y0, z0, x1, y1, z1, …]</c>.
    /// RECAST space (Y-up): X=East, Y=up, Z=North — not engine (Z-up) space; see the class remarks.
    /// </param>
    /// <param name="indices">
    /// Flat triangle index array: <c>[i0, i1, i2, …]</c> (one triangle per 3 elements).
    /// </param>
    /// <param name="layerMask">Which layers to bake (default: Infantry + Vehicle).</param>
    /// <param name="doorways">Doorway volumes marked with their own area (Stage 5c).</param>
    /// <param name="fineAreas">Where infantry tiles bake fine (building footprints — <see cref="RecastNavmeshFactory"/>).</param>
    /// <returns>
    /// Dictionary mapping each requested layer bit to its baked <see cref="DtNavMesh"/>.
    /// Layers for which baking produced no polygons are omitted.
    /// </returns>
    /// <exception cref="ArgumentException">If <paramref name="verts"/> or <paramref name="indices"/> are invalid.</exception>
    public Dictionary<NavLayerMask, DtNavMesh> Bake(
        float[]    verts,
        int[]      indices,
        NavLayerMask layerMask = NavLayerMask.Infantry | NavLayerMask.Vehicle,
        IReadOnlyList<NavDoorways.Volume>? doorways = null,
        IReadOnlyList<Area>? fineAreas = null)
    {
        if (verts   == null) throw new ArgumentNullException(nameof(verts));
        if (indices == null) throw new ArgumentNullException(nameof(indices));
        if (verts.Length % 3 != 0) throw new ArgumentException("verts length must be a multiple of 3.", nameof(verts));
        if (indices.Length % 3 != 0) throw new ArgumentException("indices length must be a multiple of 3.", nameof(indices));
        CheckTileMetres(TileMetres);

        return Bake(new ArrayTileTriangles(verts, indices, TileMetres), layerMask, doorways, fineAreas);
    }

    /// <summary>
    /// ⭐ <c>CE-4366</c> (L-4, docs/DESIGN_Scale_Rig.md §5) — the bake from a TILE SOURCE: each tile asks <paramref name="source"/> for the
    /// triangles that can reach it and keeps exactly those the whole-mesh binning kept (same test, same order ⇒ the same tile, the same
    /// cache key). No global mesh and no global Recast input exist, so a 5.5 km terrain at 1 m bakes in per-tile memory.
    /// </summary>
    public Dictionary<NavLayerMask, DtNavMesh> Bake(
        ITileTriangles source,
        NavLayerMask layerMask = NavLayerMask.Infantry | NavLayerMask.Vehicle,
        IReadOnlyList<NavDoorways.Volume>? doorways = null,
        IReadOnlyList<Area>? fineAreas = null)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        CheckTileMetres(TileMetres);
        var sw = Stopwatch.StartNew();

        var layers = new List<NavLayerMask>();
        foreach (NavLayerMask layer in LayerBits)
            if ((layerMask & layer) != 0) layers.Add(layer);
        var result = new Dictionary<NavLayerMask, DtNavMesh>();
        if (!source.TryGetBounds(out var rawMin, out var rawMax) || layers.Count == 0) { LastStats = default; return result; }

        // ⭐ The grid: anchored at the world origin, so tile (x, z) is the same square in every bake (Q71 R7 — the cache key).
        int firstX = (int)MathF.Floor(rawMin.X / TileMetres), firstZ = (int)MathF.Floor(rawMin.Z / TileMetres);
        int tilesX = Math.Max(1, (int)MathF.Floor(rawMax.X / TileMetres) - firstX + 1);
        int tilesZ = Math.Max(1, (int)MathF.Floor(rawMax.Z / TileMetres) - firstZ + 1);

        // Each tile's triangles: every triangle reaching into the tile or its widest border (what any layer's bake may rasterise).
        float margin = 0f;
        foreach (var layer in layers)
        {
            var p = GetParams(layer);
            margin = Math.Max(margin, RcConfig.CalcBorder(p.AgentRadius, CoarseCellSize) * CoarseCellSize);
            margin = Math.Max(margin, RcConfig.CalcBorder(p.AgentRadius, FineCellSize) * FineCellSize);
        }
        var volumes = doorways ?? Array.Empty<NavDoorways.Volume>();
        // ⭐ CE-4369 (L-7) — each volume's box once, and the volumes FILED BY TILE (in their own order, so a tile's list — and its hash — is
        //   the one the plain scan gave). 📐 scale-1 with its woods: ≈ 24 000 volumes × 106 260 tiles scanned per load made the WARM load's
        //   navmesh step 80 s (every tile from the cache).
        float pad = margin + 3f;
        var volumeBox = new (float X0, float Z0, float X1, float Z1)[volumes.Count];
        var volumesByTile = new List<int>?[tilesX * tilesZ];
        for (int vi = 0; vi < volumes.Count; vi++)
        {
            var d = volumes[vi];
            float vx0 = float.MaxValue, vz0 = float.MaxValue, vx1 = float.MinValue, vz1 = float.MinValue;
            for (int k = 0; k + 2 < d.Verts.Length; k += 3)
            {
                vx0 = MathF.Min(vx0, d.Verts[k]); vx1 = MathF.Max(vx1, d.Verts[k]);
                vz0 = MathF.Min(vz0, d.Verts[k + 2]); vz1 = MathF.Max(vz1, d.Verts[k + 2]);
            }
            volumeBox[vi] = (vx0, vz0, vx1, vz1);
            int bx0 = Math.Max(0, (int)MathF.Floor((vx0 - pad) / TileMetres) - 1 - firstX), bx1 = Math.Min(tilesX - 1, (int)MathF.Floor((vx1 + pad) / TileMetres) + 1 - firstX);
            int bz0 = Math.Max(0, (int)MathF.Floor((vz0 - pad) / TileMetres) - 1 - firstZ), bz1 = Math.Min(tilesZ - 1, (int)MathF.Floor((vz1 + pad) / TileMetres) + 1 - firstZ);
            for (int bz = bz0; bz <= bz1; bz++)
                for (int bx = bx0; bx <= bx1; bx++)
                    (volumesByTile[bz * tilesX + bx] ??= new List<int>()).Add(vi);
        }

        // per tile (row-major) and layer: the baked data (null = no walkable polygon) — filled in parallel, one tile per task
        var baked = new DtMeshData?[tilesX * tilesZ, layers.Count];
        var present = new bool[tilesX * tilesZ];
        var fineOf = new bool[tilesX * tilesZ, layers.Count];
        int nBaked = 0, fromCache = 0;
        var tileKeys = new System.Collections.Concurrent.ConcurrentDictionary<long, ulong>();
        int tileCount = tilesX * tilesZ, tilesDone = 0;
        var parallel = new ParallelOptions { CancellationToken = Cancel };
        // ⭐ O-7 — where the time of a bake goes: thread-time per stage (Stopwatch ticks, Interlocked), the parallel section's and the assembly's wall time. Allocation-free.
        long tGather = 0, tHash = 0, tCache = 0, tBake = 0;
        long parallelStart = System.Diagnostics.Stopwatch.GetTimestamp();
        Parallel.For(0, tileCount, parallel, ti =>
        {
            try { BakeOneTile(ti); }
            finally
            {
                int done = Interlocked.Increment(ref tilesDone);
                if (Progress != null && (done % 256 == 0 || done == tileCount)) Progress(done, tileCount);
            }
        });

        void BakeOneTile(int ti)
        {
            int tx = ti % tilesX, tz = ti / tilesX;
            int ax = firstX + tx, az = firstZ + tz;
            Fdp.Core.LoadProgress.Report();   // ⭐ CE-2417 — the load advances (docs/DESIGN_Cluster_Load_Phase.md §12)
            long g0 = System.Diagnostics.Stopwatch.GetTimestamp();
            bool anyTriangles = TileTriangles(source, ax, az, firstX, firstZ, tilesX, tilesZ, margin, out var tv, out var tidx);
            Interlocked.Add(ref tGather, System.Diagnostics.Stopwatch.GetTimestamp() - g0);
            if (!anyTriangles) return;
            present[ti] = true;
            // ⭐ CE-4366 — the Recast input (its chunky index) only when a layer must really bake: a warm load hashes and hits
            var tileVolumes = new List<NavDoorways.Volume>();
            // ⭐ Buildings Stage 5c — each doorway's spans get their own area (the mesh is baked with every door OPEN; the query filter
            //   judges those polygons by the door's live state, §3j "5c"). Only the volumes that can reach this tile (+ the hash's 3 m).
            float px = margin + 3f;
            float x0 = ax * TileMetres - px, z0 = az * TileMetres - px, x1 = (ax + 1) * TileMetres + px, z1 = (az + 1) * TileMetres + px;
            if (volumesByTile[ti] is { } filed)
                foreach (int vi in filed)
                {
                    var (vx0, vz0, vx1, vz1) = volumeBox[vi];
                    if (vx0 > x1 || vx1 < x0 || vz0 > z1 || vz1 < z0) continue;   // the exact test, as before
                    tileVolumes.Add(volumes[vi]);
                }
            RcSampleInputGeomProvider? geom = null;
            for (int li = 0; li < layers.Count; li++)
            {
                var layer = layers[li];
                bool fine = layer == NavLayerMask.Infantry && fineAreas != null && TouchesAny(fineAreas, ax, az);
                fineOf[ti, li] = fine;
                var p = GetParams(layer);
                float cs = fine ? FineCellSize : CoarseCellSize;
                TileYRange(tv, tidx, p, out float yMin, out float yMax);
                NavTileKey key = default;
                if (Cache != null)
                {
                    long h0 = System.Diagnostics.Stopwatch.GetTimestamp();
                    key = new NavTileKey(layer, ax, az, HashTile(tv, tidx, tileVolumes, layer, p, cs, ax, az, yMin, yMax));
                    Interlocked.Add(ref tHash, System.Diagnostics.Stopwatch.GetTimestamp() - h0);
                    ulong kh = 1469598103934665603UL ^ (ulong)(int)layer;   // ⭐ CE-4391 — the tile's identity for P3-a, folded over its layers (xor: order-free)
                    foreach (char ch in key.Hash) { kh ^= ch; kh *= 1099511628211UL; }
                    tileKeys.AddOrUpdate(((long)ax << 32) | (uint)az, kh, (_, old) => old ^ kh);
                    long c0 = System.Diagnostics.Stopwatch.GetTimestamp();
                    bool cached = Cache.TryGet(key, Seed, out var hit);
                    Interlocked.Add(ref tCache, System.Diagnostics.Stopwatch.GetTimestamp() - c0);
                    if (cached) { baked[ti, li] = hit; Interlocked.Increment(ref fromCache); continue; }
                }
                long b0 = System.Diagnostics.Stopwatch.GetTimestamp();
                if (geom == null)
                {
                    geom = new RcSampleInputGeomProvider(tv, tidx);
                    foreach (var d in tileVolumes) geom.AddConvexVolume(d.Verts, d.MinY, d.MaxY, new RcAreaModification(d.Area));   // ⭐ CE-4369 — a door or a wood
                }
                var data = BakeTile(geom, p, cs, ax, az, yMin, yMax);
                baked[ti, li] = data;
                Interlocked.Increment(ref nBaked);
                if (Cache != null) Cache.Put(key, data);   // an EMPTY tile is cached too (null)
                Interlocked.Add(ref tBake, System.Diagnostics.Stopwatch.GetTimestamp() - b0);
            }
        }

        long parallelTicks = System.Diagnostics.Stopwatch.GetTimestamp() - parallelStart;
        // the work in the old order (layer, then row-major tiles) for Assemble
        var work = new List<(NavLayerMask Layer, int Tx, int Tz, bool Fine)>();
        var tiles = new List<DtMeshData?>();
        int fineCount = 0;
        for (int li = 0; li < layers.Count; li++)
            for (int ti = 0; ti < tilesX * tilesZ; ti++)
            {
                if (!present[ti]) continue;
                work.Add((layers[li], ti % tilesX, ti / tilesX, fineOf[ti, li]));
                tiles.Add(baked[ti, li]);
                if (fineOf[ti, li]) fineCount++;
            }
        var tileArray = tiles.ToArray();
        long assembleStart = System.Diagnostics.Stopwatch.GetTimestamp();
        foreach (var layer in layers)
        {
            var p = GetParams(layer);
            var mesh = Assemble(layer, work, tileArray);
            if (mesh != null)
            {
                result[layer] = mesh;
                _bakedParams[layer] = p;
            }
        }
        static long Ms(long ticks) => ticks * 1000L / System.Diagnostics.Stopwatch.Frequency;
        LastStats = new BakeStats(work.Count, fineCount, nBaked, fromCache, sw.ElapsedMilliseconds,
            Ms(tGather), Ms(tHash), Ms(tCache), Ms(tBake), Ms(parallelTicks), Ms(System.Diagnostics.Stopwatch.GetTimestamp() - assembleStart));
        LastTileKeys = tileKeys;
        return result;
    }

    /// <summary>
    /// The triangles of absolute tile (<paramref name="ax"/>, <paramref name="az"/>): of the source's candidates, those whose x/z box reaches
    /// into the tile grown by <paramref name="margin"/> — the whole-mesh binning's own test (same arithmetic, same clamps, same order), copied
    /// into compact arrays (three fresh vertices per triangle). False when none.
    /// </summary>
    private bool TileTriangles(ITileTriangles source, int ax, int az, int firstX, int firstZ, int tilesX, int tilesZ, float margin,
                               out float[] verts, out int[] indices)
    {
        int tx = ax - firstX, tz = az - firstZ;
        var cv = new List<float>(); var ci = new List<int>();
        source.Gather(ax * TileMetres - margin, az * TileMetres - margin, (ax + 1) * TileMetres + margin, (az + 1) * TileMetres + margin, cv, ci);
        var ov = new List<float>(); var oi = new List<int>();
        for (int t = 0; t + 2 < ci.Count; t += 3)
        {
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            for (int k = 0; k < 3; k++)
            {
                int b = ci[t + k] * 3;
                minX = MathF.Min(minX, cv[b]); maxX = MathF.Max(maxX, cv[b]);
                minZ = MathF.Min(minZ, cv[b + 2]); maxZ = MathF.Max(maxZ, cv[b + 2]);
            }
            int tx0 = Math.Max(0, (int)MathF.Floor((minX - margin) / TileMetres) - firstX);
            int tx1 = Math.Min(tilesX - 1, (int)MathF.Floor((maxX + margin) / TileMetres) - firstX);
            int tz0 = Math.Max(0, (int)MathF.Floor((minZ - margin) / TileMetres) - firstZ);
            int tz1 = Math.Min(tilesZ - 1, (int)MathF.Floor((maxZ + margin) / TileMetres) - firstZ);
            if (tx < tx0 || tx > tx1 || tz < tz0 || tz > tz1) continue;
            for (int k = 0; k < 3; k++)
            {
                int b = ci[t + k] * 3;
                oi.Add(ov.Count / 3);
                ov.Add(cv[b]); ov.Add(cv[b + 1]); ov.Add(cv[b + 2]);
            }
        }
        verts = ov.ToArray(); indices = oi.ToArray();
        return indices.Length > 0;
    }

    private static void CheckTileMetres(float t)
    {
        static bool Multiple(float t, float cs) => MathF.Abs(t / cs - MathF.Round(t / cs)) < 1e-3f;
        if (t <= 0f || !Multiple(t, CoarseCellSize) || !Multiple(t, FineCellSize))
            throw new ArgumentException($"TileMetres {t} must be a positive multiple of {CoarseCellSize} and {FineCellSize}.");
    }

    private bool TouchesAny(IReadOnlyList<Area> areas, int ax, int az)
    {
        float x0 = ax * TileMetres - FineMarginMetres, z0 = az * TileMetres - FineMarginMetres;
        float x1 = (ax + 1) * TileMetres + FineMarginMetres, z1 = (az + 1) * TileMetres + FineMarginMetres;
        foreach (var a in areas)
            if (a.MinX < x1 && a.MaxX > x0 && a.MinZ < z1 && a.MaxZ > z0) return true;
        return false;
    }

    /// <summary>
    /// The tile's vertical extent: its own triangles, padded (below for the ground voxels, above by the agent height so open spans
    /// pass the walkable-height test), SNAPPED to the <see cref="CellHeight"/> lattice — so neighbouring tiles quantise heights the
    /// same way, and the extent depends on the tile's geometry only (never on a far-away hill: the cache key stays local).
    /// </summary>
    private static void TileYRange(float[] v, int[] idx, LayerParams p, out float yMin, out float yMax)
    {
        float lo = float.MaxValue, hi = float.MinValue;
        for (int t = 0; t + 2 < idx.Length; t += 3)
            for (int k = 0; k < 3; k++)
            {
                float y = v[idx[t + k] * 3 + 1];
                lo = MathF.Min(lo, y); hi = MathF.Max(hi, y);
            }
        yMin = MathF.Floor((lo - 0.5f) / CellHeight) * CellHeight;
        yMax = MathF.Ceiling((hi + p.AgentHeight + 0.5f) / CellHeight) * CellHeight;
    }

    /// <summary>
    /// The content key of one tile: everything its bake reads — the bake format, the layer and its params, the cell, the tile's
    /// place and vertical extent, its triangles and the doorway volumes over it. Equal key ⇔ the same tile.
    /// </summary>
    private string HashTile(float[] v, int[] idx, List<NavDoorways.Volume> volumes, NavLayerMask layer, LayerParams p,
        float cs, int ax, int az, float yMin, float yMax)
    {
        // ⭐ CE-4366 — the same byte stream as before (so every cached tile keeps its key), built in one buffer and hashed ONCE: four bytes
        //   per interop call made hashing the warm load's biggest cost (scale-1: 105 800 tiles)
        int n4 = 14 + idx.Length * 3 + volumes.Count * 64;
        var bytes = new byte[n4 * 4];
        int at = 0;
        void Grow() { if (at + 4 > bytes.Length) Array.Resize(ref bytes, bytes.Length * 2); }
        void F(float f) { Grow(); BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(at), f); at += 4; }
        void I(int n) { Grow(); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at), n); at += 4; }

        I(BakeFormat); I((int)layer); I(ax); I(az);
        F(TileMetres); F(cs); F(CellHeight); F(yMin); F(yMax);
        F(p.AgentRadius); F(p.AgentHeight); F(p.MaxSlope); F(p.MaxStepHeight);
        I(idx.Length / 3);
        for (int t = 0; t + 2 < idx.Length; t += 3)
            for (int k = 0; k < 3; k++)
            {
                int b = idx[t + k] * 3;
                F(v[b]); F(v[b + 1]); F(v[b + 2]);
            }
        float x0 = ax * TileMetres - 3f, z0 = az * TileMetres - 3f, x1 = (ax + 1) * TileMetres + 3f, z1 = (az + 1) * TileMetres + 3f;
        foreach (var vol in volumes)
        {
            float vx0 = float.MaxValue, vz0 = float.MaxValue, vx1 = float.MinValue, vz1 = float.MinValue;
            for (int k = 0; k + 2 < vol.Verts.Length; k += 3)
            {
                vx0 = MathF.Min(vx0, vol.Verts[k]); vx1 = MathF.Max(vx1, vol.Verts[k]);
                vz0 = MathF.Min(vz0, vol.Verts[k + 2]); vz1 = MathF.Max(vz1, vol.Verts[k + 2]);
            }
            if (vx0 > x1 || vx1 < x0 || vz0 > z1 || vz1 < z0) continue;
            foreach (var f in vol.Verts) F(f);
            F(vol.MinY); F(vol.MaxY); I(new RcAreaModification(vol.Area).Value);   // as AddConvexVolume stored it
        }
        return Convert.ToHexString(SHA256.HashData(bytes.AsSpan(0, at)), 0, 12);
    }

    // ── Layer enumeration ────────────────────────────────────────────────────

    private static readonly NavLayerMask[] LayerBits = {
        NavLayerMask.Infantry,
        NavLayerMask.Vehicle,
        NavLayerMask.Naval,
        NavLayerMask.Air,
    };

    // ── Per-layer params lookup ──────────────────────────────────────────────

    /// <summary>Returns the design-specified params for the given single-bit layer.</summary>
    public static LayerParams GetParams(NavLayerMask layer) => layer switch
    {
        NavLayerMask.Infantry => InfantryParams,
        NavLayerMask.Vehicle  => VehicleParams,
        NavLayerMask.Naval    => NavalParams,
        NavLayerMask.Air      => AirParams,
        _                     => InfantryParams, // fallback
    };

    // ── One tile ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Bakes one tile (absolute tile <paramref name="ax"/>, <paramref name="az"/>) at cell <paramref name="cs"/>; null when it has no
    /// walkable polygon.
    /// <para>
    /// <b>Triangle winding note.</b>
    /// DotRecast determines walkability from the triangle surface normal: surfaces with a
    /// positive Y component (upward-pointing normal) are walkable.  For a flat horizontal
    /// quad at Y=0 in navmesh-query space (X=East, Y=Up, Z=North), the vertices must be
    /// wound counter-clockwise when viewed from above (i.e., index order 0,2,1 not 0,1,2).
    /// The <see cref="ISceneGeometrySource"/> implementations are responsible for delivering
    /// correctly-wound geometry; this baker does not alter winding.
    /// </para>
    /// </summary>
    private DtMeshData? BakeTile(IRcInputGeomProvider geom, LayerParams p, float cs, int ax, int az, float yMin, float yMax)
    {
        int tileCells = (int)MathF.Round(TileMetres / cs);
        // ⭐ Region thresholds in SQUARE METRES (the pre-tiling bake's 2 / 10 cells at 0.3 m), so a fine tile drops the same
        //   islands a coarse one does. edgeMaxLen and the detail sampling are in metres already.
        const float MinRegionM2 = 2 * 2 * CoarseCellSize * CoarseCellSize, MergeRegionM2 = 10 * 10 * CoarseCellSize * CoarseCellSize;
        var cfg = new RcConfig(
            useTiles:          true,
            tileSizeX:         tileCells,
            tileSizeZ:         tileCells,
            borderSize:        RcConfig.CalcBorder(p.AgentRadius, cs),
            partition:         RcPartition.WATERSHED,
            cellSize:          cs,
            cellHeight:        CellHeight,
            agentMaxSlope:     p.MaxSlope,
            agentHeight:       p.AgentHeight,
            agentRadius:       p.AgentRadius,
            agentMaxClimb:     p.MaxStepHeight,
            minRegionArea:     MinRegionM2,
            mergeRegionArea:   MergeRegionM2,
            edgeMaxLen:        12f,
            edgeMaxError:      1.3f,
            vertsPerPoly:      6,
            detailSampleDist:  6f,
            detailSampleMaxError: 1f,
            filterLowHangingObstacles: true,
            filterLedgeSpans:          false,   // disabled: flat terrain edges are valid
            filterWalkableLowHeightSpans: true,
            walkableAreaMod:   new RcAreaModification(RcAreaModification.RC_AREA_FLAGS_MASK),
            buildMeshDetail:   true);

        // The tile's square is (ax, az) on the origin-anchored grid: hand Recast that tile's corner as the bounds origin, tile 0,0.
        var bmin = new RcVec3f(ax * TileMetres, yMin, az * TileMetres);
        var bmax = new RcVec3f((ax + 1) * TileMetres, yMax, (az + 1) * TileMetres);
        var r = new RcBuilder().BuildTile(geom, cfg, bmin, bmax, 0, 0, new RcAtomicInteger(0), 1, false);
        var polyMesh = r.Mesh;
        if (polyMesh == null || polyMesh.npolys == 0) return null;
        var detailMesh = r.MeshDetail;

        var @params = new DtNavMeshCreateParams
        {
            verts      = polyMesh.verts,
            vertCount  = polyMesh.nverts,
            polys      = polyMesh.polys,
            polyAreas  = polyMesh.areas,
            polyCount  = polyMesh.npolys,
            nvp        = polyMesh.nvp,
        };
        // DtQueryDefaultFilter's default includeFlags = 0xFFFF; if polyFlags[i] == 0,
        // PassFilter returns false and FindNearestPoly silently skips that polygon.
        // Mark every polygon with flag 1 (walkable) so queries work out of the box.
        var flags = new int[polyMesh.npolys];
        Array.Fill(flags, 1);
        @params.polyFlags = flags;

        // Detail mesh (used for height lookups).
        if (detailMesh != null)
        {
            @params.detailMeshes     = detailMesh.meshes;
            @params.detailVerts      = detailMesh.verts;
            @params.detailVertsCount = detailMesh.nverts;
            @params.detailTris       = detailMesh.tris;
            @params.detailTriCount   = detailMesh.ntris;
        }

        @params.walkableHeight = p.AgentHeight;
        @params.walkableRadius = p.AgentRadius;
        @params.walkableClimb  = p.MaxStepHeight;
        @params.bmin           = polyMesh.bmin;
        @params.bmax           = polyMesh.bmax;
        @params.cs             = cfg.Cs;
        @params.ch             = cfg.Ch;
        @params.buildBvTree    = true;
        @params.tileX          = ax;   // ⭐ ABSOLUTE: the mesh's origin is the world origin, so a cached tile fits any bake
        @params.tileZ          = az;

        return DtNavMeshBuilder.CreateNavMeshData(@params);
    }

    /// <summary>One layer's tiles into one Detour mesh (origin = the world origin; tiles carry absolute coordinates).</summary>
    private DtNavMesh? Assemble(NavLayerMask layer, List<(NavLayerMask Layer, int Tx, int Tz, bool Fine)> work, DtMeshData?[] tiles)
    {
        int count = 0, maxPolys = 1;
        for (int i = 0; i < work.Count; i++)
            if (work[i].Layer == layer && tiles[i] != null) { count++; maxPolys = Math.Max(maxPolys, tiles[i]!.header.polyCount); }
        if (count == 0) return null;

        var prm = new DtNavMeshParams
        {
            orig = RcVec3f.Zero, tileWidth = TileMetres, tileHeight = TileMetres, maxTiles = count, maxPolys = maxPolys,
        };
        var nav = new DtNavMesh();
        if (nav.Init(prm, 6).Failed()) return null;
        for (int i = 0; i < work.Count; i++)
            if (work[i].Layer == layer && tiles[i] != null && nav.AddTile(tiles[i]!, 0, 0, out _).Failed()) return null;
        return nav;
    }
}
