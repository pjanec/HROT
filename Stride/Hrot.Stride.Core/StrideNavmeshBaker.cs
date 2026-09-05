#nullable enable
using System;
using System.Collections.Generic;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using DotRecast.Recast;
using DotRecast.Recast.Geom;
using Fdp.Toolkit.Navigation;

namespace Hrot.Stride.Core;

/// <summary>
/// Bakes per-<see cref="NavLayerMask"/> DotRecast navmeshes from a triangle soup.
///
/// <para>
/// <b>Coordinate convention (the #1 risk).</b>
/// All input vertices and all baked/query coordinates are in <b>navmesh-query space</b>:
/// <c>System.Numerics.Vector3(x_east, altitude_up, z_north)</c> — the same as Stride world
/// space (X=East, Y=Up, Z=North) and the same convention as
/// <see cref="INavmeshProvider"/>'s doc comment.
/// FDP world positions (X=East, Y=North, Z=Up) must be swizzled by
/// <see cref="FdpStrideTransform.ToStridePosition"/> before being passed as input
/// vertices. The synthetic soups used in headless tests are authored directly in
/// navmesh-query space (Y-up, X-East, Z-North).
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
public sealed class StrideNavmeshBaker
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

    private static readonly LayerParams VehicleParams = new()
    {
        AgentRadius   = 1.5f,
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
    /// </summary>
    /// <param name="verts">
    /// Flat vertex array in navmesh-query space: <c>[x0, y0, z0, x1, y1, z1, …]</c>.
    /// This is X=East, Y=altitude(up), Z=North — Stride/navmesh space, not raw FDP.
    /// FDP positions must be swizzled via <see cref="FdpStrideTransform.ToStridePosition"/>
    /// before being placed in this array.
    /// </param>
    /// <param name="indices">
    /// Flat triangle index array: <c>[i0, i1, i2, …]</c> (one triangle per 3 elements).
    /// </param>
    /// <param name="layerMask">Which layers to bake (default: Infantry + Vehicle).</param>
    /// <returns>
    /// Dictionary mapping each requested layer bit to its baked <see cref="DtNavMesh"/>.
    /// Layers for which baking produced no polygons are omitted.
    /// </returns>
    /// <exception cref="ArgumentException">If <paramref name="verts"/> or <paramref name="indices"/> are invalid.</exception>
    public Dictionary<NavLayerMask, DtNavMesh> Bake(
        float[]    verts,
        int[]      indices,
        NavLayerMask layerMask = NavLayerMask.Infantry | NavLayerMask.Vehicle)
    {
        if (verts   == null) throw new ArgumentNullException(nameof(verts));
        if (indices == null) throw new ArgumentNullException(nameof(indices));
        if (verts.Length % 3 != 0) throw new ArgumentException("verts length must be a multiple of 3.", nameof(verts));
        if (indices.Length % 3 != 0) throw new ArgumentException("indices length must be a multiple of 3.", nameof(indices));

        var geom = new RcSampleInputGeomProvider(verts, indices);
        var result = new Dictionary<NavLayerMask, DtNavMesh>();

        foreach (NavLayerMask layer in LayerBits)
        {
            if ((layerMask & layer) == 0) continue;

            var p = GetParams(layer);
            var mesh = BakeLayer(geom, p);
            if (mesh != null)
            {
                result[layer] = mesh;
                _bakedParams[layer] = p;
            }
        }
        return result;
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

    // ── Single-layer bake ────────────────────────────────────────────────────

    /// <summary>
    /// Bakes one DotRecast navmesh for the given layer params.
    /// Returns null if the bake produced 0 polygons.
    ///
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
    private static DtNavMesh? BakeLayer(IRcInputGeomProvider geom, LayerParams p)
    {
        // Cell size: balance between fidelity and performance.
        // 0.3 m cell is a good balance; for vehicle (1.5m radius = 5 cells) erosion
        // is well-represented while keeping bake time manageable.
        const float CellSize   = 0.3f;
        const float CellHeight = 0.2f;

        var cfg = new RcConfig(
            partitionType:     RcPartition.WATERSHED,
            cellSize:          CellSize,
            cellHeight:        CellHeight,
            agentMaxSlope:     p.MaxSlope,
            agentHeight:       p.AgentHeight,
            agentRadius:       p.AgentRadius,
            agentMaxClimb:     p.MaxStepHeight,
            regionMinSize:     2,
            regionMergeSize:   10,
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

        // Expand the bounding box to give DotRecast's heightfield proper Y extent:
        // - pad below so the ground voxels are computed correctly
        // - pad above by at least agentHeight so open spans pass the walkable-height test
        RcVec3f rawMin = geom.GetMeshBoundsMin();
        RcVec3f rawMax = geom.GetMeshBoundsMax();
        const float YPadBelow = 0.5f;
        float yPadAbove = p.AgentHeight + 0.5f;
        RcVec3f bmin = new RcVec3f(rawMin.X, rawMin.Y - YPadBelow, rawMin.Z);
        RcVec3f bmax = new RcVec3f(rawMax.X, rawMax.Y + yPadAbove, rawMax.Z);

        var bcfg    = new RcBuilderConfig(cfg, bmin, bmax);
        var builder = new RcBuilder();
        var result  = builder.Build(geom, bcfg, keepInterResults: false);

        var polyMesh   = result.Mesh;
        var detailMesh = result.MeshDetail;

        if (polyMesh == null || polyMesh.npolys == 0)
            return null;

        // Populate DtNavMeshCreateParams from the baked poly mesh.
        var @params = new DtNavMeshCreateParams();

        // Vertices (quantised) and polygon connectivity.
        @params.verts      = polyMesh.verts;
        @params.vertCount  = polyMesh.nverts;
        @params.polys      = polyMesh.polys;
        @params.polyAreas  = polyMesh.areas;
        @params.polyCount  = polyMesh.npolys;
        @params.nvp        = polyMesh.nvp;

        // Set walkable flags on all polygons.
        // DtQueryDefaultFilter's default includeFlags = 0xFFFF; if polyFlags[i] == 0,
        // PassFilter returns false and FindNearestPoly silently skips that polygon.
        // Mark every polygon with flag 1 (walkable) so queries work out of the box.
        var flags = new int[polyMesh.npolys];
        for (int i = 0; i < polyMesh.npolys; i++) flags[i] = 1;
        @params.polyFlags  = flags;

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
        @params.bmin           = bmin;
        @params.bmax           = bmax;
        @params.cs             = cfg.Cs;
        @params.ch             = cfg.Ch;
        @params.buildBvTree    = true;

        DtMeshData? meshData = DtNavMeshBuilder.CreateNavMeshData(@params);
        if (meshData == null) return null;

        var navMesh = new DtNavMesh();
        var status  = navMesh.Init(meshData, polyMesh.nvp, 0);
        if (status.Failed()) return null;

        return navMesh;
    }
}
