#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using Fdp.Core;
using Fdp.Toolkit.Navigation;

namespace Hrot.Stride.Core;

/// <summary>
/// <see cref="INavmeshProvider"/> backed by DotRecast baked navmeshes.
/// Drop-in replacement for <c>FakeNavmeshProvider</c>.
///
/// <para>
/// <b>Coordinate convention.</b>
/// All input/output <see cref="Vector3"/> values follow the <see cref="INavmeshProvider"/>
/// contract: <c>new Vector3(x_east, altitude, z_north)</c> — i.e. the same as Stride world
/// space (X=East, Y=Up, Z=North).  FDP world positions (X=East, Y=North, Z=Up) must be
/// converted by <see cref="FdpStrideTransform.ToStridePosition"/> by callers before being
/// passed here.  Internally all DotRecast calls use the same X/Y/Z ordering, so no additional
/// swizzle is required.
/// </para>
///
/// <para>
/// <b>Registration.</b>
/// Register as the <see cref="INavmeshProvider"/> managed singleton:
/// <code>repo.SetSingletonManaged&lt;INavmeshProvider&gt;(provider);</code>
/// </para>
/// </summary>
[ComponentId(GlobalComponentIds.INavmeshProvider)]
public sealed class DotRecastNavmeshProvider : INavmeshProvider
{
    // ── Search half-extents (metres) for FindNearestPoly ─────────────────────

    /// <summary>
    /// Half-extents used for nearest-polygon search around a query point.
    /// Generous enough to snap a point slightly above the mesh surface.
    /// </summary>
    private static readonly RcVec3f SearchExtents = new(2f, 4f, 2f);

    // ── Per-layer state ──────────────────────────────────────────────────────

    private sealed class LayerState : IDisposable
    {
        public DtNavMesh         NavMesh { get; }
        public DtNavMeshQuery    Query   { get; }
        public DtQueryDefaultFilter Filter { get; } = new DtQueryDefaultFilter();

        public LayerState(DtNavMesh mesh)
        {
            NavMesh = mesh;
            Query   = new DtNavMeshQuery(mesh);
        }

        public void Dispose() { /* DotRecast meshes have no unmanaged resources */ }
    }

    // ── Fields ───────────────────────────────────────────────────────────────

    private readonly Dictionary<NavLayerMask, LayerState> _layers = new();
    private uint _queryVersion;

    // ── Construction ─────────────────────────────────────────────────────────

    /// <summary>
    /// Constructs an empty provider.  Call <see cref="Rebake"/> to load meshes.
    /// </summary>
    public DotRecastNavmeshProvider() { }

    /// <summary>
    /// Constructs a provider from a pre-baked set of navmeshes (one per layer bit).
    /// </summary>
    /// <param name="meshes">Map from single-bit <see cref="NavLayerMask"/> to its <see cref="DtNavMesh"/>.</param>
    public DotRecastNavmeshProvider(IReadOnlyDictionary<NavLayerMask, DtNavMesh> meshes)
    {
        foreach (var kv in meshes)
            _layers[kv.Key] = new LayerState(kv.Value);
        _queryVersion = 1;
    }

    // ── NavMesh access (for DotRecastDtCrowdProvider construction) ───────────

    /// <summary>
    /// Returns the raw <see cref="DtNavMesh"/> for the given single-bit
    /// <paramref name="layer"/>, if it was supplied at construction or via
    /// <see cref="Rebake"/>.
    /// </summary>
    /// <param name="layer">Single-bit <see cref="NavLayerMask"/> (e.g. <c>NavLayerMask.Infantry</c>).</param>
    /// <param name="navMesh">The baked mesh, or <c>null</c> if the layer was not baked.</param>
    /// <returns>True when the layer is present.</returns>
    public bool TryGetNavMesh(NavLayerMask layer, out DtNavMesh? navMesh)
    {
        if (_layers.TryGetValue(layer, out var ls))
        {
            navMesh = ls.NavMesh;
            return true;
        }
        navMesh = null;
        return false;
    }

    // ── Rebake ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Replaces all navmeshes with a new baked set and increments <see cref="QueryVersion"/>.
    /// Thread-unsafe; call only from the sim thread.
    /// </summary>
    public void Rebake(IReadOnlyDictionary<NavLayerMask, DtNavMesh> meshes)
    {
        foreach (var ls in _layers.Values) ls.Dispose();
        _layers.Clear();
        foreach (var kv in meshes)
            _layers[kv.Key] = new LayerState(kv.Value);
        _queryVersion++;
    }

    // ── INavmeshProvider ─────────────────────────────────────────────────────

    /// <inheritdoc/>
    public bool IsWalkable(Vector3 position, uint layerMask = 0xFFFFFFFF)
        => TryFindNearestPoly(position, layerMask, out _, out _);

    /// <inheritdoc/>
    public bool ProjectToNavmesh(Vector3 position, out Vector3 snapped, uint layerMask = 0xFFFFFFFF)
    {
        if (TryFindNearestPoly(position, layerMask, out var layer, out var nearestPt))
        {
            // nearestPt is already the closest point on the mesh surface.
            snapped = ToVector3(nearestPt);
            return true;
        }
        snapped = position;
        return false;
    }

    /// <inheritdoc/>
    public int SampleNavmeshPoints(Vector3 center, float radius, Span<Vector3> results, uint layerMask = 0xFFFFFFFF)
    {
        if (results.IsEmpty) return 0;

        int count = 0;
        var rc    = ToRcVec(center);

        // Allocate outside the loop to avoid CA2014 stackalloc-in-loop.
        const int MaxPolys = 128;
        var refs    = new long[MaxPolys];
        var parents = new long[MaxPolys];
        var costs   = new float[MaxPolys];

        foreach (var kv in _layers)
        {
            if (((uint)kv.Key & layerMask) == 0) continue;
            var ls = kv.Value;

            // FindPolysAroundCircle gives all polygons within radius.
            ls.Query.FindPolysAroundCircle(
                startRef:     FindNearestPolyRef(ls, rc),
                centerPos:    rc,
                radius:       radius,
                filter:       ls.Filter,
                resultRef:    refs.AsSpan(),
                resultParent: parents.AsSpan(),
                resultCost:   costs.AsSpan(),
                resultCount:  out int polyCount,
                maxResult:    MaxPolys);

            for (int i = 0; i < polyCount && count < results.Length; i++)
            {
                var center3 = ls.NavMesh.GetPolyCenter(refs[i]);
                results[count++] = ToVector3(center3);
            }
        }
        return count;
    }

    /// <inheritdoc/>
    public bool PathExists(Vector3 from, Vector3 to, uint layerMask = 0xFFFFFFFF)
    {
        var buf = new NavWaypoint[2];
        return PlanPath(from, to, buf.AsSpan(), layerMask) > 0;
    }

    /// <inheritdoc/>
    public float PathCost(Vector3 from, Vector3 to, uint layerMask = 0xFFFFFFFF)
    {
        const int MaxWaypoints = 256;
        var buf = new NavWaypoint[MaxWaypoints];
        int n   = PlanPath(from, to, buf.AsSpan(), layerMask);
        if (n == 0) return float.MaxValue;
        if (n == 1) return 0f;

        float cost = 0f;
        for (int i = 1; i < n; i++)
        {
            var d = buf[i].Position - buf[i - 1].Position;
            cost += d.Length();
        }
        return cost;
    }

    /// <inheritdoc/>
    public uint QueryVersion() => _queryVersion;

    /// <inheritdoc/>
    public int PlanPath(Vector3 from, Vector3 to, Span<NavWaypoint> waypoints, uint layerMask = 0xFFFFFFFF)
    {
        if (waypoints.Length < 2) return 0;

        const int MaxPath     = 256;
        const int MaxStraight = 256;

        // Allocate outside the loop to avoid CA2014 stackalloc-in-loop.
        var polyPathBuf    = new long[MaxPath];
        var straightBuf    = new DtStraightPath[MaxStraight];

        // Try each matching layer; use the first that finds a complete path.
        foreach (var kv in _layers)
        {
            if (((uint)kv.Key & layerMask) == 0) continue;
            var ls = kv.Value;

            var startPos = ToRcVec(from);
            var endPos   = ToRcVec(to);

            // Find start and end polys.
            var startStatus = ls.Query.FindNearestPoly(
                startPos, SearchExtents, ls.Filter,
                out long startRef, out _, out _);

            var endStatus = ls.Query.FindNearestPoly(
                endPos, SearchExtents, ls.Filter,
                out long endRef, out _, out _);

            if (startStatus.Failed() || startRef == 0) continue;
            if (endStatus.Failed()   || endRef   == 0) continue;

            // Polygon path.
            var pathStatus = ls.Query.FindPath(
                startRef, endRef, startPos, endPos,
                ls.Filter, polyPathBuf.AsSpan(), out int pathCount, MaxPath);

            if (pathStatus.Failed() || pathCount == 0) continue;

            // Straight path (the actual waypoints along the corridor).
            var straightStatus = ls.Query.FindStraightPath(
                startPos, endPos,
                polyPathBuf.AsSpan(0, pathCount),
                pathCount,
                straightBuf.AsSpan(),
                out int straightCount,
                MaxStraight,
                DtStraightPathOptions.DT_STRAIGHTPATH_ALL_CROSSINGS);

            if (straightStatus.Failed() || straightCount == 0) continue;

            int count = Math.Min(straightCount, waypoints.Length);
            for (int i = 0; i < count; i++)
            {
                waypoints[i] = new NavWaypoint
                {
                    Position  = ToVector3(straightBuf[i].pos),
                    Traversal = TraversalKind.Walk,
                };
            }
            return count;
        }
        return 0;
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Tries to find the nearest polygon to <paramref name="position"/> in any layer
    /// matching <paramref name="layerMask"/>.
    /// Returns true and sets <paramref name="layerState"/> + <paramref name="nearestPt"/>
    /// on success.
    /// </summary>
    private bool TryFindNearestPoly(
        Vector3             position,
        uint                layerMask,
        out LayerState?     layerState,
        out RcVec3f         nearestPt)
    {
        var rc = ToRcVec(position);

        foreach (var kv in _layers)
        {
            if (((uint)kv.Key & layerMask) == 0) continue;
            var ls = kv.Value;

            var status = ls.Query.FindNearestPoly(
                rc, SearchExtents, ls.Filter,
                out long nearestRef, out RcVec3f np, out _);

            if (status.Succeeded() && nearestRef != 0)
            {
                layerState = ls;
                nearestPt  = np;
                return true;
            }
        }

        layerState = null;
        nearestPt  = rc;
        return false;
    }

    /// <summary>
    /// Returns the nearest polygon reference or 0 if not found.
    /// Used internally for sampling.
    /// </summary>
    private static long FindNearestPolyRef(LayerState ls, RcVec3f pos)
    {
        ls.Query.FindNearestPoly(
            pos, SearchExtents, ls.Filter,
            out long r, out _, out _);
        return r;
    }

    // ── Coordinate helpers ───────────────────────────────────────────────────

    /// <summary>
    /// Converts a <see cref="Vector3"/> in navmesh-query space to a DotRecast <see cref="RcVec3f"/>.
    /// No swizzle needed — both use (X=East, Y=altitude, Z=North).
    /// </summary>
    private static RcVec3f ToRcVec(Vector3 v) => new(v.X, v.Y, v.Z);

    /// <summary>
    /// Converts a DotRecast <see cref="RcVec3f"/> back to a <see cref="Vector3"/>.
    /// No swizzle needed — both use (X=East, Y=altitude, Z=North).
    /// </summary>
    private static Vector3 ToVector3(RcVec3f v) => new(v.X, v.Y, v.Z);
}
