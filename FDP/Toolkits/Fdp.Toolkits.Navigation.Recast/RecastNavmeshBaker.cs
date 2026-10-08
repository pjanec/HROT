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
        MaxStepHeight = 0.1f,
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
    public readonly record struct BakeStats(int Tiles, int FineTiles, int Baked, int FromCache, long Milliseconds);

    /// <summary>The tile edge (m); must be a multiple of both cell sizes.</summary>
    public float TileMetres { get; init; } = DefaultTileMetres;

    /// <summary>The tile cache; null ⇒ every tile is baked.</summary>
    public NavTileCache? Cache { get; init; }

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

        var sw = Stopwatch.StartNew();
        var geom = new RcSampleInputGeomProvider(verts, indices);
        // ⭐ Buildings Stage 5c — each doorway's spans get their own area, so its passage bakes into polygons of its own (the mesh is
        //   baked with every door OPEN; the query filter judges those polygons by the door's live state). §3j "5c".
        if (doorways != null)
            foreach (var d in doorways)
                geom.AddConvexVolume(d.Verts, d.MinY, d.MaxY, new RcAreaModification(NavDoorways.DoorArea));

        var layers = new List<NavLayerMask>();
        foreach (NavLayerMask layer in LayerBits)
            if ((layerMask & layer) != 0) layers.Add(layer);
        var result = new Dictionary<NavLayerMask, DtNavMesh>();
        if (indices.Length == 0 || layers.Count == 0) { LastStats = default; return result; }

        // ⭐ The grid: anchored at the world origin, so tile (x, z) is the same square in every bake (Q71 R7 — the cache key).
        RcVec3f rawMin = geom.GetMeshBoundsMin(), rawMax = geom.GetMeshBoundsMax();
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
        var tileTris = BinTriangles(verts, indices, firstX, firstZ, tilesX, tilesZ, margin);

        var work = new List<(NavLayerMask Layer, int Tx, int Tz, bool Fine)>();
        foreach (var layer in layers)
            for (int tz = 0; tz < tilesZ; tz++)
            for (int tx = 0; tx < tilesX; tx++)
            {
                if (tileTris[tz * tilesX + tx] == null) continue;   // no geometry, no tile
                bool fine = layer == NavLayerMask.Infantry && fineAreas != null && TouchesAny(fineAreas, firstX + tx, firstZ + tz);
                work.Add((layer, tx, tz, fine));
            }

        var tiles = new DtMeshData?[work.Count];
        int baked = 0, fromCache = 0, fineCount = 0;
        Parallel.For(0, work.Count, i =>
        {
            var (layer, tx, tz, fine) = work[i];
            var p = GetParams(layer);
            float cs = fine ? FineCellSize : CoarseCellSize;
            var tris = tileTris[tz * tilesX + tx]!;
            TileYRange(verts, indices, tris, p, out float yMin, out float yMax);
            int ax = firstX + tx, az = firstZ + tz;

            NavTileKey key = default;
            if (Cache != null)
            {
                key = new NavTileKey(layer, ax, az, HashTile(verts, indices, tris, geom, layer, p, cs, ax, az, yMin, yMax));
                if (Cache.TryGet(key, out var hit)) { tiles[i] = hit; Interlocked.Increment(ref fromCache); if (fine) Interlocked.Increment(ref fineCount); return; }
            }
            var data = BakeTile(geom, p, cs, ax, az, yMin, yMax);
            tiles[i] = data;
            Interlocked.Increment(ref baked);
            if (fine) Interlocked.Increment(ref fineCount);
            if (Cache != null) Cache.Put(key, data);   // an EMPTY tile is cached too (null)
        });

        foreach (var layer in layers)
        {
            var p = GetParams(layer);
            var mesh = Assemble(layer, work, tiles);
            if (mesh != null)
            {
                result[layer] = mesh;
                _bakedParams[layer] = p;
            }
        }
        LastStats = new BakeStats(work.Count, fineCount, baked, fromCache, sw.ElapsedMilliseconds);
        return result;
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

    /// <summary>Per tile (row-major), the triangles whose x/z box reaches into the tile grown by <paramref name="margin"/>; null = none.</summary>
    private List<int>?[] BinTriangles(float[] v, int[] idx, int firstX, int firstZ, int tilesX, int tilesZ, float margin)
    {
        var bins = new List<int>?[tilesX * tilesZ];
        for (int t = 0; t + 2 < idx.Length; t += 3)
        {
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            for (int k = 0; k < 3; k++)
            {
                int b = idx[t + k] * 3;
                minX = MathF.Min(minX, v[b]); maxX = MathF.Max(maxX, v[b]);
                minZ = MathF.Min(minZ, v[b + 2]); maxZ = MathF.Max(maxZ, v[b + 2]);
            }
            int tx0 = Math.Max(0, (int)MathF.Floor((minX - margin) / TileMetres) - firstX);
            int tx1 = Math.Min(tilesX - 1, (int)MathF.Floor((maxX + margin) / TileMetres) - firstX);
            int tz0 = Math.Max(0, (int)MathF.Floor((minZ - margin) / TileMetres) - firstZ);
            int tz1 = Math.Min(tilesZ - 1, (int)MathF.Floor((maxZ + margin) / TileMetres) - firstZ);
            for (int tz = tz0; tz <= tz1; tz++)
            for (int tx = tx0; tx <= tx1; tx++)
                (bins[tz * tilesX + tx] ??= new List<int>()).Add(t);
        }
        return bins;
    }

    /// <summary>
    /// The tile's vertical extent: its own triangles, padded (below for the ground voxels, above by the agent height so open spans
    /// pass the walkable-height test), SNAPPED to the <see cref="CellHeight"/> lattice — so neighbouring tiles quantise heights the
    /// same way, and the extent depends on the tile's geometry only (never on a far-away hill: the cache key stays local).
    /// </summary>
    private static void TileYRange(float[] v, int[] idx, List<int> tris, LayerParams p, out float yMin, out float yMax)
    {
        float lo = float.MaxValue, hi = float.MinValue;
        foreach (int t in tris)
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
    private string HashTile(float[] v, int[] idx, List<int> tris, IRcInputGeomProvider geom, NavLayerMask layer, LayerParams p,
        float cs, int ax, int az, float yMin, float yMax)
    {
        using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buf = new byte[4];
        void F(float f) { BinaryPrimitives.WriteSingleLittleEndian(buf, f); h.AppendData(buf, 0, 4); }
        void I(int n) { BinaryPrimitives.WriteInt32LittleEndian(buf, n); h.AppendData(buf, 0, 4); }

        I(BakeFormat); I((int)layer); I(ax); I(az);
        F(TileMetres); F(cs); F(CellHeight); F(yMin); F(yMax);
        F(p.AgentRadius); F(p.AgentHeight); F(p.MaxSlope); F(p.MaxStepHeight);
        I(tris.Count);
        foreach (int t in tris)
            for (int k = 0; k < 3; k++)
            {
                int b = idx[t + k] * 3;
                F(v[b]); F(v[b + 1]); F(v[b + 2]);
            }
        float x0 = ax * TileMetres - 3f, z0 = az * TileMetres - 3f, x1 = (ax + 1) * TileMetres + 3f, z1 = (az + 1) * TileMetres + 3f;
        foreach (var vol in geom.ConvexVolumes())
        {
            float vx0 = float.MaxValue, vz0 = float.MaxValue, vx1 = float.MinValue, vz1 = float.MinValue;
            for (int k = 0; k + 2 < vol.verts.Length; k += 3)
            {
                vx0 = MathF.Min(vx0, vol.verts[k]); vx1 = MathF.Max(vx1, vol.verts[k]);
                vz0 = MathF.Min(vz0, vol.verts[k + 2]); vz1 = MathF.Max(vz1, vol.verts[k + 2]);
            }
            if (vx0 > x1 || vx1 < x0 || vz0 > z1 || vz1 < z0) continue;
            foreach (var f in vol.verts) F(f);
            F(vol.hmin); F(vol.hmax); I(vol.areaMod.Value);
        }
        return Convert.ToHexString(h.GetHashAndReset(), 0, 12);
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
