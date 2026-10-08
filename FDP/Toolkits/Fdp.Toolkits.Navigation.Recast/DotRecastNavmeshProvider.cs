using System.Threading;
#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using Fdp.Core;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Terrain;

namespace Fdp.Toolkit.Navigation.Recast;

/// <summary>
/// <see cref="INavmeshProvider"/> backed by DotRecast baked navmeshes.
/// Drop-in replacement for <c>FakeNavmeshProvider</c>.
///
/// <para>
/// ⭐ <b>Coordinate convention — Z-up API, Y-up inside</b> (R-182, DESIGN_Terrain_World W7).
/// Every <see cref="Vector3"/> in and out of this class is in the engine's space: X east, Y north, Z up.
/// DotRecast (and the baked <c>DtNavMesh</c>) are Y-up (X east, Y up, Z north), so each public method converts
/// its inputs with <see cref="ToRcVec"/> and its outputs with <see cref="ToVector3"/> — <c>(x, y, z)</c> engine
/// ⇄ <c>(x, z, y)</c> Recast. Callers never swizzle. The baked meshes themselves stay in Recast space
/// (<see cref="ISceneGeometrySource"/> triangles are Recast-space input), and <see cref="TryGetNavMesh"/> hands out the raw
/// Recast-space mesh for <see cref="DotRecastDtCrowdProvider"/>, which does its own conversion.
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
        /// <summary>⭐ Stage 5c — judges doorway polygons by the doors as the terrain authored them; a query passing its view's DoorStates gets a per-call filter (R-219).</summary>
        public DoorAwareQueryFilter Filter { get; }

        // ⭐ CE-2122 — ONE QUERY PER THREAD. DtNavMeshQuery keeps its node pool / open list as instance state, so the EQS module
        //   and the NavigationSolver module (two background threads, one provider) corrupted it when they shared one instance:
        //   "Operations that change non-concurrent collections must have exclusive access" in DtNodePool.GetNode, swallowed by the
        //   module host (14 faults in one --mode all run, behaviors' measurement). The mesh itself is read-only after the bake and
        //   is shared; only the scratch query is per thread. No lock — the two modules keep running in parallel.
        private readonly ThreadLocal<DtNavMeshQuery> _query;
        public DtNavMeshQuery Query => _query.Value!;

        // ⭐ R-220 — the filter a query judged by its CALLER's doors uses: one working copy per thread (the same reason as the query),
        //   re-pointed per call, so a per-tick query allocates no filter.
        private readonly ThreadLocal<DoorAwareQueryFilter> _working;

        public LayerState(DtNavMesh mesh, DoorAwareQueryFilter? filter = null)
        {
            NavMesh  = mesh;
            Filter   = filter ?? new DoorAwareQueryFilter(new Dictionary<long, int>(), null, canOpenDoors: true);
            _query   = new ThreadLocal<DtNavMeshQuery>(() => new DtNavMeshQuery(mesh));
            var shared = Filter;
            _working = new ThreadLocal<DoorAwareQueryFilter>(() => shared.WorkingCopy());
        }

        /// <summary>The filter judging by <paramref name="doors"/> (R-219); null = the doors as authored (the shared filter).</summary>
        public DoorAwareQueryFilter FilterFor(DoorStates? doors) => doors == null ? Filter : _working.Value!.JudgeBy(doors);

        // ⚠ Deliberately NOT disposing the ThreadLocal: a swapped-out snapshot may still be mid-query on a background module
        //   (P1); a disposed ThreadLocal would throw there. The old queries are collected with the snapshot.
        public void Dispose() { /* DotRecast meshes have no unmanaged resources */ }
    }

    // ── Fields ───────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ Navigation v2 §14 P1 (R-218) — ONE immutable snapshot of every layer's mesh + filter, behind a single field. A query reads it
    /// ONCE (<see cref="Current"/>) and uses only that object; a change builds a NEW snapshot and swaps the field (<see cref="Rebake"/>,
    /// later P2's tile rebuilder). ⇒ the navmesh can change at runtime and an in-flight query on another thread (CE-2122) finishes on the
    /// old snapshot, untouched. ⛔ Never mutate a snapshot's dictionary or mesh after it is published.
    /// </summary>
    private sealed class Snapshot
    {
        public readonly IReadOnlyDictionary<NavLayerMask, LayerState> Layers;
        public readonly uint Version;
        // ⭐ R-220 — the same layers as arrays, in the dictionary's order: a query walks these (an interface foreach boxes its enumerator)
        public readonly NavLayerMask[] Masks;
        public readonly LayerState[] States;
        public Snapshot(IReadOnlyDictionary<NavLayerMask, LayerState> layers, uint version)
        {
            Layers = layers; Version = version;
            Masks = new NavLayerMask[layers.Count]; States = new LayerState[layers.Count];
            int i = 0;
            foreach (var kv in layers) { Masks[i] = kv.Key; States[i] = kv.Value; i++; }
        }
    }

    // ⭐ R-220 — per-THREAD scratch for the path queries (two background modules query one provider, CE-2122): a per-tick query
    //   allocates nothing. Never handed out — each is filled and read within one call on its own thread.
    private const int MaxPath = 256, MaxStraight = 256, MaxWaypoints = 256, MaxPolys = 128;
    [ThreadStatic] private static long[]? t_polyPath;
    [ThreadStatic] private static DtStraightPath[]? t_straight;
    [ThreadStatic] private static NavWaypoint[]? t_waypoints;
    [ThreadStatic] private static long[]? t_refs;
    [ThreadStatic] private static long[]? t_parents;
    [ThreadStatic] private static float[]? t_costs;
    [ThreadStatic] private static NearestPolyQuery? t_nearest;

    /// <summary>
    /// ⭐ R-220 — DotRecast's <c>FindNearestPoly</c> allocates a <c>DtFindNearestPolyQuery</c> per call; this is the same search
    /// (its <c>Process</c> rule, verbatim) as a reusable per-thread object, run through the public <c>QueryPolygons</c>.
    /// </summary>
    private sealed class NearestPolyQuery : IDtPolyQuery
    {
        private DtNavMeshQuery _query = null!;
        private RcVec3f _center;
        private float _nearestDistanceSqr;
        public long NearestRef;
        public RcVec3f NearestPt;

        public NearestPolyQuery Reset(DtNavMeshQuery query, RcVec3f center)
        {
            _query = query; _center = center;
            _nearestDistanceSqr = float.MaxValue; NearestRef = 0; NearestPt = center;
            return this;
        }

        public void Process(DtMeshTile tile, ReadOnlySpan<int> polys, ReadOnlySpan<long> refs, int count)
        {
            for (int i = 0; i < count; i++)
            {
                long r = refs[i];
                _query.ClosestPointOnPoly(r, _center, out var closest, out bool overPoly);
                var diff = RcVec3f.Subtract(_center, closest);
                float d;
                if (overPoly)
                {
                    d = MathF.Abs(diff.Y) - tile.data.header.walkableClimb;
                    d = d > 0f ? d * d : 0f;
                }
                else d = diff.LengthSquared();
                if (d < _nearestDistanceSqr) { NearestPt = closest; _nearestDistanceSqr = d; NearestRef = r; }
            }
        }
    }

    /// <summary>⭐ R-220 — <c>FindNearestPoly</c> without its per-call allocation.</summary>
    private static DtStatus FindNearestPoly(DtNavMeshQuery query, RcVec3f center, IDtQueryFilter filter, out long nearestRef, out RcVec3f nearestPt)
    {
        var q = (t_nearest ??= new NearestPolyQuery()).Reset(query, center);
        var status = query.QueryPolygons(center, SearchExtents, filter, q);
        nearestRef = status.Failed() ? 0 : q.NearestRef;
        nearestPt  = status.Failed() ? center : q.NearestPt;
        return status;
    }

    private Snapshot _snapshot = new(new Dictionary<NavLayerMask, LayerState>(), 0);

    /// <summary>The published snapshot — read it ONCE per query.</summary>
    private Snapshot Current => Volatile.Read(ref _snapshot);

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
        var layers = new Dictionary<NavLayerMask, LayerState>();
        foreach (var kv in meshes)
            layers[kv.Key] = new LayerState(kv.Value);
        _snapshot = new Snapshot(layers, 1);
    }

    /// <summary>
    /// ⭐ Buildings Stage 5c — a provider over meshes baked with <paramref name="doorways"/>: each layer maps its doorway polygons to
    /// their doors and judges them through a <see cref="DoorAwareQueryFilter"/> — by the caller's <see cref="DoorStates"/> when a query
    /// passes one (R-219), else by the doors as <paramref name="world"/> authored them.
    /// The Infantry layer may use a closed door (at a cost); every other layer never uses a doorway (§3j "5c" N2).
    /// </summary>
    public DotRecastNavmeshProvider(IReadOnlyDictionary<NavLayerMask, DtNavMesh> meshes, Fdp.Toolkit.Terrain.TerrainWorld world,
        IReadOnlyList<NavDoorways.Volume> doorways)
        => _snapshot = new Snapshot(DoorAwareLayers(meshes, world, doorways), 1);

    private static Dictionary<NavLayerMask, LayerState> DoorAwareLayers(IReadOnlyDictionary<NavLayerMask, DtNavMesh> meshes,
        Fdp.Toolkit.Terrain.TerrainWorld world, IReadOnlyList<NavDoorways.Volume> doorways)
    {
        var layers = new Dictionary<NavLayerMask, LayerState>();
        foreach (var kv in meshes)
            layers[kv.Key] = new LayerState(kv.Value, new DoorAwareQueryFilter(
                NavDoorways.DoorPolys(kv.Value, doorways), DoorStates.Authored(world), canOpenDoors: kv.Key == NavLayerMask.Infantry));
        return layers;
    }

    /// <summary>⭐ Stage 5c — how many polygons of <paramref name="layer"/> are doorway polygons (a rail reads it).</summary>
    public int DoorPolyCount(NavLayerMask layer) => Current.Layers.TryGetValue(layer, out var ls) ? ls.Filter.DoorPolyCount : 0;

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
        if (Current.Layers.TryGetValue(layer, out var ls))
        {
            navMesh = ls.NavMesh;
            return true;
        }
        navMesh = null;
        return false;
    }

    // ── Rebake ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Replaces all navmeshes with a new baked set and increments <see cref="QueryVersion"/>. ⭐ P1 (R-218): safe while other threads
    /// query — they finish on the snapshot they already hold. Concurrent CALLS of Rebake are last-writer-wins (one writer expected).
    /// <para>⚠ The new layers carry the plain filter; the door-aware rebake is the overload taking the world (P2, <see cref="RecastNavmeshFactory.Rebake"/>).</para>
    /// </summary>
    public void Rebake(IReadOnlyDictionary<NavLayerMask, DtNavMesh> meshes)
    {
        // ⭐ P1 — build a NEW snapshot and swap it in; the old one is left to the queries still holding it (and the GC).
        var layers = new Dictionary<NavLayerMask, LayerState>();
        foreach (var kv in meshes)
            layers[kv.Key] = new LayerState(kv.Value);
        Volatile.Write(ref _snapshot, new Snapshot(layers, unchecked(Current.Version + 1)));
    }

    /// <summary>
    /// ⭐ R-218 P2 — the door-aware rebake: like <see cref="Rebake(IReadOnlyDictionary{NavLayerMask, DtNavMesh})"/>, with each layer's
    /// doorway polygons mapped to their doors (Stage 5c). <see cref="RecastNavmeshFactory.Rebake"/> calls it after a touched-tile bake.
    /// </summary>
    public void Rebake(IReadOnlyDictionary<NavLayerMask, DtNavMesh> meshes, Fdp.Toolkit.Terrain.TerrainWorld world,
        IReadOnlyList<NavDoorways.Volume> doorways)
        => Volatile.Write(ref _snapshot, new Snapshot(DoorAwareLayers(meshes, world, doorways), unchecked(Current.Version + 1)));

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

        var refs    = t_refs    ??= new long[MaxPolys];    // ⭐ R-220 — per-thread scratch
        var parents = t_parents ??= new long[MaxPolys];
        var costs   = t_costs   ??= new float[MaxPolys];

        var snap = Current;   // ⭐ P1 — one snapshot for the whole query
        for (int li = 0; li < snap.States.Length; li++)
        {
            if (((uint)snap.Masks[li] & layerMask) == 0) continue;
            var ls = snap.States[li];

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
    public bool PathExists(Vector3 from, Vector3 to, uint layerMask = 0xFFFFFFFF) => PathExists(from, to, layerMask, null);

    /// <inheritdoc/>
    public bool PathExists(Vector3 from, Vector3 to, uint layerMask, DoorStates? doors)
    {
        // ⭐ Stage 5c — a PARTIAL path (the search reached only the polygon nearest an unreachable goal) is NOT a path. Before
        //   doors could be locked it was rare (an island); a locked doorway makes it the normal answer for "into that room".
        var buf = t_waypoints ??= new NavWaypoint[MaxWaypoints];   // ⭐ R-220 — per-thread scratch
        return PlanPathCore(from, to, buf.AsSpan(0, 2), layerMask, doors, out bool complete) > 0 && complete;   // completeness is the poly path's
    }

    /// <inheritdoc/>
    public float PathCost(Vector3 from, Vector3 to, uint layerMask = 0xFFFFFFFF) => PathCost(from, to, layerMask, null);

    /// <inheritdoc/>
    public float PathCost(Vector3 from, Vector3 to, uint layerMask, DoorStates? doors)
    {
        var buf = t_waypoints ??= new NavWaypoint[MaxWaypoints];   // ⭐ R-220 — per-thread scratch
        int n   = PlanPathCore(from, to, buf.AsSpan(), layerMask, doors, out bool complete);
        if (n == 0 || !complete) return float.MaxValue;   // ⭐ Stage 5c — the contract: no (complete) path ⇒ MaxValue
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
    public uint QueryVersion() => Current.Version;

    /// <inheritdoc/>
    public int PlanPath(Vector3 from, Vector3 to, Span<NavWaypoint> waypoints, uint layerMask = 0xFFFFFFFF)
        => PlanPathCore(from, to, waypoints, layerMask, null, out _);   // a partial path is still returned — the agent goes as near as it can

    /// <inheritdoc/>
    public int PlanPath(Vector3 from, Vector3 to, Span<NavWaypoint> waypoints, uint layerMask, DoorStates? doors)
        => PlanPathCore(from, to, waypoints, layerMask, doors, out _);

    /// <param name="complete">False when the polygon path stops short of the goal's polygon (DotRecast's partial result).</param>
    /// <param name="doors">⭐ R-219 — the caller's door states; null = the doors as the terrain authored them.</param>
    private int PlanPathCore(Vector3 from, Vector3 to, Span<NavWaypoint> waypoints, uint layerMask, DoorStates? doors, out bool complete)
    {
        complete = false;
        if (waypoints.Length < 2) return 0;

        var polyPathBuf = t_polyPath ??= new long[MaxPath];             // ⭐ R-220 — per-thread scratch
        var straightBuf = t_straight ??= new DtStraightPath[MaxStraight];

        // Try each matching layer; use the first that finds a complete path.
        var snap = Current;   // ⭐ P1 — one snapshot for the whole query
        for (int li = 0; li < snap.States.Length; li++)
        {
            if (((uint)snap.Masks[li] & layerMask) == 0) continue;
            var ls = snap.States[li];
            var filter = ls.FilterFor(doors);   // ⭐ R-219 — judged by the caller's doors (R-220: this thread's working copy, no allocation)

            var startPos = ToRcVec(from);
            var endPos   = ToRcVec(to);

            // Find start and end polys.
            var startStatus = FindNearestPoly(ls.Query, startPos, filter, out long startRef, out _);
            var endStatus   = FindNearestPoly(ls.Query, endPos,   filter, out long endRef,   out _);

            if (startStatus.Failed() || startRef == 0) continue;
            if (endStatus.Failed()   || endRef   == 0) continue;

            // Polygon path.
            var pathStatus = ls.Query.FindPath(
                startRef, endRef, startPos, endPos,
                filter, polyPathBuf.AsSpan(), out int pathCount, MaxPath);

            if (pathStatus.Failed() || pathCount == 0) continue;

            // Straight path (the actual waypoints along the corridor).
            var straightStatus = ls.Query.FindStraightPath(
                startPos, endPos,
                polyPathBuf.AsSpan(0, pathCount),
                pathCount,
                straightBuf.AsSpan(),
                out int straightCount,
                MaxStraight,
                // ⭐ CE-3111 (live) — a vertex where the AREA changes (a doorway's own area, 5c), not at every polygon edge: the
                //   fine tiles over a building cut a corner into 10 cm segments, and the mover's tangent-following overshot them
                //   (bt-doors: the Visitor ran 1.4 m inside the house wall and passed the back door's mark out of reach).
                //   ⛔ SUPERSEDED: DT_STRAIGHTPATH_ALL_CROSSINGS (5c), which the door mark never needed.
                DtStraightPathOptions.DT_STRAIGHTPATH_AREA_CROSSINGS);

            if (straightStatus.Failed() || straightCount == 0) continue;

            complete = polyPathBuf[pathCount - 1] == endRef;
            int count = Math.Min(straightCount, waypoints.Length);
            for (int i = 0; i < count; i++)
            {
                waypoints[i] = new NavWaypoint
                {
                    Position  = ToVector3(straightBuf[i].pos),
                    // ⭐ Stage 5c — a corner on a doorway polygon is a door crossing (N4: carried to the agent in 5d)
                    Traversal = filter.DoorOf(straightBuf[i].refs) >= 0 ? TraversalKind.Door : TraversalKind.Walk,
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

        var snap = Current;   // ⭐ P1 — one snapshot for the whole query
        for (int li = 0; li < snap.States.Length; li++)
        {
            if (((uint)snap.Masks[li] & layerMask) == 0) continue;
            var ls = snap.States[li];

            var status = FindNearestPoly(ls.Query, rc, ls.Filter, out long nearestRef, out RcVec3f np);

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
        FindNearestPoly(ls.Query, pos, ls.Filter, out long r, out _);
        return r;
    }

    // ── Coordinate helpers ───────────────────────────────────────────────────

    /// <summary>
    /// Engine (Z-up: X east, Y north, Z up) → DotRecast (Y-up: X east, Y up, Z north): <c>(x, y, z) → (x, z, y)</c>.
    /// The ONLY place an input leaves the engine's coordinate system.
    /// </summary>
    private static RcVec3f ToRcVec(Vector3 v) => new(v.X, v.Z, v.Y);

    /// <summary>
    /// DotRecast (Y-up) → engine (Z-up): <c>(x, y, z) → (x, z, y)</c>. The ONLY place an output enters the engine's
    /// coordinate system.
    /// </summary>
    private static Vector3 ToVector3(RcVec3f v) => new(v.X, v.Z, v.Y);
}
