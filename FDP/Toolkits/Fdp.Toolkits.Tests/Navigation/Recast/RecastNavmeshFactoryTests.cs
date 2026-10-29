#nullable enable
using System;
using System.Linq;
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

    // ── ⭐ Buildings programme Stage 2 — walk inside (docs/DESIGN_Building_Interiors.md §6 B-1) ──────────────────

    // A 10 x 8 m two-storey house at (20,20): a door in the south wall, stairs (8,1)→(8,6) to storey 2, the upper floor
    // split around the stairwell so the stairs have headroom.
    private const string HouseWorld = """
        {"type":"FeatureCollection","hrot":{"schemaVersion":1,"bounds":[0,0,60,60],"groundZ":0},"features":[
          {"type":"Feature","properties":{"kind":"building","label":"H","building":{
             "footprint":[[0,0],[10,0],[10,8],[0,8]],
             "storeys":[
               {"height":3,"walls":[
                  {"from":[0,0],"to":[10,0],"openings":[{"kind":"door","at":4.5,"width":1.2}]},
                  {"from":[10,0],"to":[10,8]},{"from":[10,8],"to":[0,8]},{"from":[0,8],"to":[0,0]}],
                "stairs":[{"from":[8,1],"to":[8,6],"width":1.2}]},
               {"height":3,"floor":[ [[0,0],[7.3,0],[7.3,8],[0,8]], [[8.7,0],[10,0],[10,8],[8.7,8]],
                                     [[7.3,0],[8.7,0],[8.7,1],[7.3,1]], [[7.3,6],[8.7,6],[8.7,8],[7.3,8]] ],
                "walls":[{"from":[0,0],"to":[10,0]},{"from":[10,0],"to":[10,8]},{"from":[10,8],"to":[0,8]},{"from":[0,8],"to":[0,0]}]}]}},
           "geometry":{"type":"Point","coordinates":[20,20]}}]}
        """;

    [Fact]
    public void Stage2_InsideTheHouse_TheGroundIsWalkable_AndAPathFromOutsideReachesTheUpperStoreyByTheStairs()
    {
        var nav = Bake(HouseWorld);
        Assert.True(nav.IsWalkable(new Vector3(22, 24, 0)), "the ground floor inside an enterable building must be walkable");

        Span<NavWaypoint> wps = stackalloc NavWaypoint[128];
        var upstairs = new Vector3(22, 24, 3);
        int n = nav.PlanPath(new Vector3(25, 10, 0), upstairs, wps, (uint)NavLayerMask.Infantry);
        Assert.True(n >= 2, $"no path to the upper storey ({n} waypoints)");
        // ⭐ CE-1189 — a partial path ends at the goal's x/y AND height when the goal is straight above a reachable floor (Navigation design
        //   §14 note): the end point alone cannot tell; completeness is the poly path's
        Assert.True(nav.PathExists(new Vector3(25, 10, 0), upstairs, (uint)NavLayerMask.Infantry), "the path to the upper storey is partial");
        var last = wps[n - 1].Position;
        Assert.InRange(last.Z, 2.6f, 3.4f);                                       // it ENDS on storey 2, not under it
        Assert.InRange(last.X, 21f, 23f);
        for (int i = 0; i < n; i++)
        {
            var p = wps[i].Position;
            // between floors only ON the stair ramp (x 27.4..28.6, y 21..26) — never floating elsewhere
            if (p.Z > 0.4f && p.Z < 2.6f)
                Assert.True(p.X > 27f && p.X < 29f && p.Y > 20.5f && p.Y < 26.5f, $"waypoint {i} {p} is between floors off the stairs");
            // through the south wall (y = 20) only at the doorway (x 24.5..25.7)
            if (MathF.Abs(p.Y - 20f) < 0.15f && p.Z < 2.6f)
                Assert.True(p.X > 24.3f && p.X < 25.9f, $"waypoint {i} {p} crosses the south wall outside the door");
        }
    }

    // ── ⭐ Buildings Stage 5c — doors in the navmesh (docs/DESIGN_Building_Interiors.md §3j "5c") ──────────────────

    // A closed 10 x 8 m room at (20,20) whose ONLY way in is one door ("front", 1.2 m) in the south wall.
    private const string OneDoorRoom = """
        {"type":"FeatureCollection","hrot":{"schemaVersion":1,"bounds":[0,0,60,60],"groundZ":0},"features":[
          {"type":"Feature","properties":{"kind":"building","label":"R","doors":{"front":"open"},"building":{
             "footprint":[[0,0],[10,0],[10,8],[0,8]],
             "storeys":[{"height":3,"walls":[
                  {"from":[0,0],"to":[10,0],"openings":[{"kind":"door","at":4.4,"width":1.2,"doorId":"front"}]},
                  {"from":[10,0],"to":[10,8]},{"from":[10,8],"to":[0,8]},{"from":[0,8],"to":[0,0]}]}]}},
           "geometry":{"type":"Point","coordinates":[20,20]}}]}
        """;

    [Fact]
    public void Stage5c_TheDoorwayBakesIntoDoorPolygons_AndAPathThroughItIsMarkedDoor()
    {
        var world = TerrainWorldParser.Parse(OneDoorRoom, "range");
        var nav = (DotRecastNavmeshProvider)new RecastNavmeshFactory { Layers = NavLayerMask.Infantry }.Build(world)!;
        Assert.True(nav.DoorPolyCount(NavLayerMask.Infantry) > 0, "the doorway must bake into polygons of its own (DoorArea)");

        Span<NavWaypoint> wps = stackalloc NavWaypoint[64];
        int n = nav.PlanPath(new Vector3(25, 10, 0), new Vector3(25, 25, 0), wps, (uint)NavLayerMask.Infantry);
        Assert.True(n >= 2, $"no path into the room ({n})");
        bool door = false;
        for (int i = 0; i < n; i++) door |= wps[i].Traversal == TraversalKind.Door;
        Assert.True(door, "a corner in the doorway must be a Door waypoint (N4)");
    }

    // ── ⭐ CE-3133 — the navmesh's polygons for the map's Navmesh layer (docs/DESIGN_Terrain_Combat_Tuning.md §5c) ──────────

    [Fact]
    public void CE3133_TheDebugMesh_IsEveryPolygon_InEngineSpace_WithItsDoor()
    {
        var world = TerrainWorldParser.Parse(OneDoorRoom, "range");
        var nav = (DotRecastNavmeshProvider)new RecastNavmeshFactory { Layers = NavLayerMask.Infantry }.Build(world)!;
        Assert.True(nav.TryGetNavMesh(NavLayerMask.Infantry, out var dt));
        int baked = 0;
        for (int t = 0; t < dt!.GetMaxTiles(); t++) baked += dt.GetTile(t)?.data?.header?.polyCount ?? 0;

        var mesh = nav.DebugMesh(NavLayerMask.Infantry)!;

        Assert.Equal(baked, mesh.PolyCount);                                       // every polygon (this world has no off-mesh links)
        // Z-up: a vertex is on the ground (Z 0) or on the room's walkable 3 m roof — and then inside the room's footprint
        Assert.All(mesh.Vertices, v => Assert.True(MathF.Abs(v.Z) < 0.5f
            || (v.Z > 2.9f && v.Z < 3.5f && v.X > 19.5f && v.X < 30.5f && v.Y > 19.5f && v.Y < 28.5f), $"{v}"));
        Assert.Contains(mesh.Vertices, v => v.Z > 2.9f);
        Assert.All(mesh.Vertices, v => Assert.InRange(v.Y, -1f, 61f));
        for (int i = 0; i < mesh.PolyCount; i++) Assert.InRange(mesh.Polygon(i).Length, 3, 6);
        Assert.Equal(nav.DoorPolyCount(NavLayerMask.Infantry), mesh.DoorIndex.Count(d => d == 0));   // the doorway polygons, door 0
        Assert.All(mesh.DoorIndex, d => Assert.InRange(d, -1, 0));
        // a doorway polygon sits in the doorway (x 24.4..25.6, the south wall y = 20)
        for (int i = 0; i < mesh.PolyCount; i++)
            if (mesh.DoorIndex[i] == 0)
                Assert.All(mesh.Polygon(i).ToArray(), v => Assert.True(v.X > 23.5f && v.X < 26.5f && v.Y > 18.5f && v.Y < 21.5f, $"{v}"));
        Assert.Null(nav.DebugMesh(NavLayerMask.Vehicle));                           // not baked
    }

    [Fact]
    public void CE3133_TheDebugMesh_IsBuiltOncePerNavmesh_AndAfreshAfterARebake()
    {
        var world = TerrainWorldParser.Parse(OneDoorRoom, "range");
        var nav = (DotRecastNavmeshProvider)new RecastNavmeshFactory { Layers = NavLayerMask.Infantry }.Build(world)!;
        var first = nav.DebugMesh(NavLayerMask.Infantry);
        Assert.Same(first, nav.DebugMesh(NavLayerMask.Infantry));

        Assert.True(nav.TryGetNavMesh(NavLayerMask.Infantry, out var dt));
        nav.Rebake(new System.Collections.Generic.Dictionary<NavLayerMask, DotRecast.Detour.DtNavMesh> { [NavLayerMask.Infantry] = dt! });
        var second = nav.DebugMesh(NavLayerMask.Infantry)!;
        Assert.NotSame(first, second);
        Assert.Equal(first!.Version + 1, second.Version);
    }

    [Fact]
    public void CE3133_TheNodeNavmesh_ForwardsThePublishedMesh()
    {
        var node = new SwitchableNavmeshProvider();
        Assert.Null(node.DebugMesh(NavLayerMask.Infantry));                        // before a bake: the straight-line fallback
        var nav = (DotRecastNavmeshProvider)Bake(BlockWorld);
        node.Publish(nav);
        Assert.Same(nav.DebugMesh(NavLayerMask.Infantry), node.DebugMesh(NavLayerMask.Infantry));
    }

    [Fact]
    public void Stage5c_ALockedDoorIsAWall_AClosedOneIsPassable_JudgedByTheCallersDoorTable()
    {
        var world = TerrainWorldParser.Parse(OneDoorRoom, "range");
        var nav = new RecastNavmeshFactory { Layers = NavLayerMask.Infantry }.Build(world)!;
        var outside = new Vector3(25, 10, 0); var inside = new Vector3(25, 25, 0);
        const uint Inf = (uint)NavLayerMask.Infantry;
        DoorStates As(TerrainDoorState st) => Fdp.Toolkit.Terrain.Tests.DoorFixtures.States(world, ("range/R/front", st));

        Assert.True(nav.PathExists(outside, inside, Inf));                                    // no table: as authored (open)
        Assert.False(nav.PathExists(outside, inside, Inf, As(TerrainDoorState.Locked)), "a locked door is impassable");
        Assert.True(nav.PathExists(outside, inside, Inf, As(TerrainDoorState.Closed)), "infantry may open a closed door (N2)");
        Assert.True(nav.PathExists(outside, inside, Inf, As(TerrainDoorState.Destroyed)));

        // ⭐ R-219 — the switchable node provider forwards the table (the default interface method would drop it)
        var node = new SwitchableNavmeshProvider();
        node.Publish(nav);
        Assert.False(node.PathExists(outside, inside, Inf, As(TerrainDoorState.Locked)));
        Span<NavWaypoint> wps = stackalloc NavWaypoint[64];
        int n = node.PlanPath(outside, inside, wps, Inf, As(TerrainDoorState.Locked));
        Assert.True(n == 0 || Vector3.Distance(wps[n - 1].Position, inside) > 2f, "a locked door yields no path in (at most a partial one)");
    }

    /// <summary>
    /// ⭐⭐ R-220 — the path queries a background solver runs each tick allocate NOTHING of their own once warm (GC stutters):
    /// per-thread scratch buffers, the layers walked as arrays, a per-thread working filter judged by the caller's doors, and a
    /// reusable nearest-polygon search. ⚠ DotRecast's A* node pool allocates one small list per node it visits (<c>DtNodePool.GetNode</c>
    /// after <c>Clear()</c>) — not ours to remove without forking it — so a query that runs A* is held to DotRecast's OWN cost for the
    /// same search: every byte above that would be ours. Measured per query so a regression names its culprit.
    /// </summary>
    [Fact]
    public void R220_PathQueries_AllocateNothingOfTheirOwn_WithOrWithoutTheCallersDoors()
    {
        var world = TerrainWorldParser.Parse(OneDoorRoom, "range");
        var nav = (DotRecastNavmeshProvider)new RecastNavmeshFactory { Layers = NavLayerMask.Infantry }.Build(world)!;
        var closed = Fdp.Toolkit.Terrain.Tests.DoorFixtures.States(world, ("range/R/front", TerrainDoorState.Closed));
        var outside = new Vector3(25, 10, 0); var inside = new Vector3(25, 25, 0);
        const uint Inf = (uint)NavLayerMask.Infantry;
        var wps = new NavWaypoint[64];
        var points = new Vector3[16];
        Assert.True(nav.PathExists(outside, inside, Inf, closed));   // the doors are really judged (a closed door is passable)

        // DotRecast's own cost for the same A* searches: its query, preallocated buffers, start/end polygons found up front
        Assert.True(nav.TryGetNavMesh(NavLayerMask.Infantry, out var mesh));
        var raw = new DotRecast.Detour.DtNavMeshQuery(mesh!);
        var plain = new DotRecast.Detour.DtQueryDefaultFilter();
        var extents = new DotRecast.Core.Numerics.RcVec3f(2f, 4f, 2f);
        var s = new DotRecast.Core.Numerics.RcVec3f(outside.X, outside.Z, outside.Y);
        var e = new DotRecast.Core.Numerics.RcVec3f(inside.X, inside.Z, inside.Y);
        raw.FindNearestPoly(s, extents, plain, out long sRef, out _, out _);
        raw.FindNearestPoly(e, extents, plain, out long eRef, out _, out _);
        var polys = new long[256]; var straight = new DotRecast.Detour.DtStraightPath[256];
        var refs = new long[128]; var parents = new long[128]; var costs = new float[128];
        void RawPath(DotRecast.Detour.IDtQueryFilter f)
        {
            raw.FindPath(sRef, eRef, s, e, f, polys, out int n, 256);
            raw.FindStraightPath(s, e, polys.AsSpan(0, n), n, straight, out _, 256, DotRecast.Detour.DtStraightPathOptions.DT_STRAIGHTPATH_AREA_CROSSINGS);
        }
        var doorFilter = new DoorAwareQueryFilter(NavDoorways.DoorPolys(mesh!, NavDoorways.For(world)), closed, canOpenDoors: true);   // the same search ours runs

        var queries = new (string Name, Action Ours, Action? DotRecastAlone)[]
        {
            ("PlanPath",            () => nav.PlanPath(outside, inside, wps, Inf),             () => RawPath(plain)),
            ("PlanPath(doors)",     () => nav.PlanPath(outside, inside, wps, Inf, closed),     () => RawPath(doorFilter)),
            ("PathExists",          () => nav.PathExists(outside, inside, Inf),                () => RawPath(plain)),
            ("PathExists(doors)",   () => nav.PathExists(outside, inside, Inf, closed),        () => RawPath(doorFilter)),
            ("PathCost",            () => nav.PathCost(outside, inside, Inf),                  () => RawPath(plain)),
            ("PathCost(doors)",     () => nav.PathCost(outside, inside, Inf, closed),          () => RawPath(doorFilter)),
            ("IsWalkable",          () => nav.IsWalkable(outside, Inf),                        null),
            ("ProjectToNavmesh",    () => nav.ProjectToNavmesh(outside, out _, Inf),           null),
            ("SampleNavmeshPoints", () => nav.SampleNavmeshPoints(outside, 5f, points, Inf),
                                    () => raw.FindPolysAroundCircle(sRef, s, 5f, plain, refs, parents, costs, out _, 128)),
        };

        static long PerCall(Action run)
        {
            run(); run();   // warm-up: this thread's query, working filter and scratch
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10; i++) run();
            return (GC.GetAllocatedBytesForCurrentThread() - before) / 10;
        }

        var ours = new System.Collections.Generic.List<string>();
        foreach (var (name, run, dotRecast) in queries)
        {
            long bytes = PerCall(run), library = dotRecast == null ? 0 : PerCall(dotRecast);
            if (bytes > library) ours.Add($"{name}: {bytes} B/call vs DotRecast alone {library}");
        }
        Assert.True(ours.Count == 0, string.Join("; ", ours));
    }

    [Fact]
    public void Stage5c_TheFilter_ChargesAClosedDoorOnceOnEntry_AndKeepsVehiclesOut()
    {
        var world = TerrainWorldParser.Parse(OneDoorRoom, "range");
        int door = world.DoorIndexOf("range/R/front");
        var doorPolys = new System.Collections.Generic.Dictionary<long, int> { [5] = door, [6] = door };
        DoorStates As(TerrainDoorState st) => Fdp.Toolkit.Terrain.Tests.DoorFixtures.States(world, ("range/R/front", st));
        var infantry = new DoorAwareQueryFilter(doorPolys, As(TerrainDoorState.Open), canOpenDoors: true);
        var vehicle  = new DoorAwareQueryFilter(doorPolys, As(TerrainDoorState.Open), canOpenDoors: false);
        var poly = new DotRecast.Detour.DtPoly(0, 6) { flags = 1 };
        poly.SetArea(NavDoorways.DoorArea);
        var a = new DotRecast.Core.Numerics.RcVec3f(0, 0, 0); var b = new DotRecast.Core.Numerics.RcVec3f(1, 0, 0);
        float Cost(DoorAwareQueryFilter f, long prev, long cur) => f.GetCost(a, b, prev, null!, poly, cur, null!, poly, 0, null!, poly);

        Assert.Equal(1f, Cost(infantry, 1, 5), 3);                                   // open: the plain distance
        var closed = infantry.With(As(TerrainDoorState.Closed));
        Assert.Equal(1f + DoorAwareQueryFilter.ClosedDoorPenaltyMetres, Cost(closed, 1, 5), 3);   // entering it
        Assert.Equal(1f, Cost(closed, 5, 6), 3);                                     // already inside the same door
        Assert.Equal(1f, Cost(closed, 1, 9), 3);                                     // not a doorway polygon

        Assert.True(infantry.PassFilter(5, null!, poly));
        Assert.False(vehicle.PassFilter(5, null!, poly));                            // N2 — vehicles never use a doorway
        Assert.True(vehicle.PassFilter(9, null!, poly));
        Assert.False(infantry.With(As(TerrainDoorState.Locked)).PassFilter(5, null!, poly));

        // ⭐ CE-3290 (R-3033) — a door whose state is UNKNOWN (no table) is a closed door: passable, at a closed door's cost.
        // 🔴 Red-proof: the old fallback (Open) charges the plain distance.
        var unknown = infantry.With(null);
        Assert.True(unknown.PassFilter(5, null!, poly));
        Assert.Equal(1f + DoorAwareQueryFilter.ClosedDoorPenaltyMetres, Cost(unknown, 1, 5), 3);
    }

    // ── ⭐ CE-2122 — the EQS and NavigationSolver modules query ONE provider from two background threads ─────────

    /// <summary>
    /// ⭐ Navigation v2 §14 P1 (R-218) — the navmesh CHANGES at runtime while other threads query it: <see cref="DotRecastNavmeshProvider.Rebake"/>
    /// swaps one immutable snapshot, so a query never sees a half-swapped provider and never throws. Before P1, Rebake cleared the very
    /// dictionary a background PlanPath was enumerating.
    /// </summary>
    [Fact]
    public void P1_RebakeWhileOtherThreadsQuery_NeverSeesAHalfSwappedMesh_AndTheVersionMoves()
    {
        var a = new RecastNavmeshBaker().Bake(Geometry(BlockWorld).v, Geometry(BlockWorld).i, NavLayerMask.Infantry);
        var b = new RecastNavmeshBaker().Bake(Geometry(OneDoorRoom).v, Geometry(OneDoorRoom).i, NavLayerMask.Infantry);
        var nav = new DotRecastNavmeshProvider(a);
        uint v0 = nav.QueryVersion();
        var errors = new System.Collections.Concurrent.ConcurrentQueue<string>();
        using var stop = new System.Threading.CancellationTokenSource();
        var readers = Enumerable.Range(0, 3).Select(_ => new System.Threading.Thread(() =>
        {
            try
            {
                var wps = new NavWaypoint[64];
                while (!stop.IsCancellationRequested)
                {
                    // (5,30) and (5,5) are open ground in BOTH worlds ⇒ every answer must be "walkable" / "a path" — an empty or
                    // half-built provider (the pre-P1 Rebake cleared the dictionary a reader was walking) answers "no"
                    if (!nav.IsWalkable(new Vector3(5, 30, 0))) errors.Enqueue("IsWalkable saw no mesh");
                    if (nav.PlanPath(new Vector3(5, 30, 0), new Vector3(5, 5, 0), wps, (uint)NavLayerMask.Infantry) < 2)
                        errors.Enqueue("PlanPath saw no mesh");
                }
            }
            catch (Exception ex) { errors.Enqueue(ex.GetType().Name + ": " + ex.Message); }
        })).ToArray();
        foreach (var t in readers) t.Start();
        for (int i = 0; i < 20000; i++) nav.Rebake(i % 2 == 0 ? b : a);
        stop.Cancel();
        foreach (var t in readers) t.Join();

        Assert.True(errors.IsEmpty, $"{errors.Count} bad answers, e.g. " + string.Join(" | ", errors.Distinct().Take(3)));
        Assert.Equal(v0 + 20000, nav.QueryVersion());
        Assert.False(nav.IsWalkable(new Vector3(30, 30, 0)));          // the last swap (a: the block) is what queries see now
    }

    private static (float[] v, int[] i) Geometry(string json)
    {
        Assert.True(new TerrainWorldGeometrySource(TerrainWorldParser.Parse(json)).TryGetTriangles(out var v, out var i));
        return (v, i);
    }

    [Fact]
    public void CE2122_ConcurrentPlanPathAndPathCost_FromSeveralThreads_NeverCorruptTheQuery()
    {
        var nav = Bake(BlockWorld);
        var errors = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
        var threads = new System.Threading.Thread[4];
        for (int t = 0; t < threads.Length; t++)
        {
            int seed = t;
            threads[t] = new System.Threading.Thread(() =>
            {
                try
                {
                    Span<NavWaypoint> wps = stackalloc NavWaypoint[64];
                    for (int i = 0; i < 400; i++)
                    {
                        float y = 5f + ((i + seed) % 50);
                        int n = nav.PlanPath(new Vector3(5, y, 0), new Vector3(55, 60 - y, 0), wps, (uint)NavLayerMask.Infantry);
                        if (n < 2) throw new InvalidOperationException($"no path ({n}) at i={i}");
                        if (!(nav.PathCost(new Vector3(5, y, 0), new Vector3(55, y, 0), (uint)NavLayerMask.Infantry) > 0f))
                            throw new InvalidOperationException($"no cost at i={i}");
                    }
                }
                catch (Exception ex) { errors.Enqueue(ex); }
            });
        }
        foreach (var th in threads) th.Start();
        foreach (var th in threads) th.Join();
        Assert.True(errors.IsEmpty, string.Join(" | ", errors.Select(e => e.GetType().Name + ": " + e.Message).Take(3)));
    }

    /// <summary>
    /// ⭐ Buildings 5d (bt-doors) — the SHIPPED House A (bt-range) is reachable through EACH of its doorways for infantry: the front
    /// (outside → west room), the back (outside → east room) and the inner hall door (east → west room); and with the front LOCKED and
    /// the back CLOSED (the bt-doors scenario) the route into the west room goes round by the back door and marks it.
    /// <para>📐 Found by the first bt-doors live run (`2026-10-08`), three causes: the template's front door was at 4.5 (`at` is the
    /// opening's START), so its 1.0 m spanned 104.5..105.5 — centred ON the inner wall (x 105); a 1.0 m doorway leaves 0.4 m after the
    /// 0.3 m infantry erosion — one or two 0.3 m voxels, so whether it bakes depends on grid alignment (they were 1.2 m until the tiled bake; real 0.9 m since — infantry tiles over a building bake at 0.15 m, CE-3111); and
    /// <c>TerrainWorldMesh</c> dropped every
    /// 2 m ground cell whose centre lay in a wall panel, so the 0.15 m inner wall at x 105 (a cell centre) cut a 2 m strip out of the
    /// floor (walkable ended at 103.8 and began at 106.2) and neither the hall nor the front doorway connected. The agent got a
    /// partial path to the wall and "arrived" outside.</para>
    /// </summary>
    [Fact]
    public void Stage5d_BtRangeHouseA_EveryDoorwayConnectsForInfantry()
    {
        var folder = System.IO.Path.Combine(Fdp.Toolkit.Orchestration.ContentLayout.TerrainAssets(Fdp.Toolkit.Orchestration.ContentLayout.TryFindSourceTree()!), "bt-range");
        var world = TerrainWorldParser.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(folder, "bt-range.world.geojson")), "bt-range",
            TerrainAssets.ForFolder(folder));
        var nav = (DotRecastNavmeshProvider)new RecastNavmeshFactory { Layers = NavLayerMask.Infantry }.Build(world)!;
        var open = Fdp.Toolkit.Terrain.Tests.DoorFixtures.States(world,
            ("bt-range/House A/front", TerrainDoorState.Open), ("bt-range/House A/back", TerrainDoorState.Open), ("bt-range/House A/hall", TerrainDoorState.Open));
        const uint Inf = (uint)NavLayerMask.Infantry;
        var south = new Vector3(104.5f, 94, 0); var north = new Vector3(108, 112, 0);
        var west = new Vector3(102, 104, 0);   var east = new Vector3(108, 104, 0);
        var results = new[]
        {
            ("front: outside S -> west room", nav.PathExists(south, west, Inf, open)),
            ("back: outside N -> east room",  nav.PathExists(north, east, Inf, open)),
            ("hall: east room -> west room",  nav.PathExists(east, west, Inf, open)),
        };
        Assert.True(results.All(r => r.Item2), string.Join(" · ", results.Select(r => $"{r.Item1}={r.Item2}")) + $" · doorPolys={nav.DoorPolyCount(NavLayerMask.Infantry)}");

        var scenario = Fdp.Toolkit.Terrain.Tests.DoorFixtures.States(world,
            ("bt-range/House A/front", TerrainDoorState.Locked), ("bt-range/House A/back", TerrainDoorState.Closed), ("bt-range/House A/hall", TerrainDoorState.Open));
        var wps = new NavWaypoint[128];
        int n = nav.PlanPath(new Vector3(104.2f, 94, 0), west, wps, Inf, scenario);
        Assert.True(n > 0);
        Assert.True(Vector2.Distance(new Vector2(wps[n - 1].Position.X, wps[n - 1].Position.Y), new Vector2(west.X, west.Y)) < 0.5f,
            $"the path must reach the west room, not stop short (partial path) — ends at {wps[n - 1].Position}");
        Assert.Contains(Enumerable.Range(0, n), i => wps[i].Traversal == TraversalKind.Door
            && Vector2.Distance(new Vector2(wps[i].Position.X, wps[i].Position.Y), new Vector2(107.4f, 108)) < 1.5f);   // the back door (x 108..106.8)
        Assert.DoesNotContain(Enumerable.Range(0, n), i => Vector2.Distance(new Vector2(wps[i].Position.X, wps[i].Position.Y), new Vector2(104.2f, 100)) < 0.8f);   // not the locked front

        // the cluster bakes BOTH layers and a request arriving with no layer (0 ⇒ "any") must still get the infantry route, not the
        // first layer's partial one
        var both = (DotRecastNavmeshProvider)new RecastNavmeshFactory().Build(world)!;
        int m = both.PlanPath(new Vector3(104.2f, 94, 0), west, wps, 0xFFFFFFFFu, scenario);
        Assert.True(m > 0 && Vector2.Distance(new Vector2(wps[m - 1].Position.X, wps[m - 1].Position.Y), new Vector2(west.X, west.Y)) < 0.5f,
            $"any-layer request: the path must reach the west room — ends at {(m > 0 ? wps[m - 1].Position : default)}");
    }

    /// <summary>
    /// ⭐ UI-2 (Defend §16 DA-22, <c>CE-2285</c>) — <c>house-3f</c> (shared <c>assets/buildings</c>): a path from the street through
    /// the front door, up both flights and out of the STAIR HOUSE's door reaches the roof at 6 m. Between storeys it is only on a
    /// flight; on the roof it leaves the stair house only by its door.
    /// </summary>
    [Fact]
    public void UI2_House3f_APathFromTheFrontDoor_ReachesTheRoof_ThroughTheStairHouse()
    {
        var folder = System.IO.Path.Combine(Fdp.Toolkit.Orchestration.ContentLayout.TerrainAssets(Fdp.Toolkit.Orchestration.ContentLayout.TryFindSourceTree()!), "bt-range");
        var world = TerrainWorldParser.Parse("""
            {"type":"FeatureCollection","hrot":{"schemaVersion":1,"bounds":[0,0,60,60],"groundZ":0},"features":[
              {"type":"Feature","properties":{"kind":"building","template":"house-3f","label":"H3"},"geometry":{"type":"Point","coordinates":[20,20]}}]}
            """, "range", TerrainAssets.ForFolder(folder));
        var nav = (DotRecastNavmeshProvider)new RecastNavmeshFactory { Layers = NavLayerMask.Infantry }.Build(world)!;
        var open = Fdp.Toolkit.Terrain.Tests.DoorFixtures.States(world,
            ("range/H3/front", TerrainDoorState.Open), ("range/H3/hall", TerrainDoorState.Open), ("range/H3/roof", TerrainDoorState.Open));
        const uint Inf = (uint)NavLayerMask.Infantry;

        var roof = new Vector3(22, 22, 6);   // the roof's south-west part, outside the stair house
        var wps = new NavWaypoint[256];
        int n = nav.PlanPath(new Vector3(24.2f, 12, 0), roof, wps, Inf, open);
        Assert.True(n >= 2, $"no path to the roof ({n} waypoints)");
        // ⚠ a PARTIAL path ends at the goal's x/y clamped onto the last reachable polygon WITH THE GOAL'S OWN HEIGHT (Detour), so a goal straight
        //   above a reachable floor looks reached by its end point alone — completeness is the poly path's (PathExists)
        Assert.True(nav.PathExists(new Vector3(24.2f, 12, 0), roof, Inf, open), "the path to the roof is partial");
        var last = wps[n - 1].Position;
        Assert.True(MathF.Abs(last.Z - 6f) < 0.4f && Vector2.Distance(new Vector2(last.X, last.Y), new Vector2(roof.X, roof.Y)) < 0.5f,
            $"the path must END on the roof at {roof}, not short of it — ends at {last}");
        for (int i = 0; i < n; i++)
        {
            var p = wps[i].Position;
            bool flight1 = p.X > 27.2f && p.X < 28.8f && p.Y > 20.5f && p.Y < 26.5f;   // x 27.5..28.5, y 21..26, 0 → 3
            bool flight2 = p.X > 28.5f && p.X < 30.1f && p.Y > 23.5f && p.Y < 27.5f;   // x 28.8..29.8, y 27..24, 3 → 6
            if ((p.Z > 0.4f && p.Z < 2.6f) || (p.Z > 3.4f && p.Z < 5.6f))
                Assert.True(flight1 || flight2, $"waypoint {i} {p} is between storeys off the flights");
            // on the roof, crossing the stair house's south wall (y 22, x 27..30) only at its door (x 27.5..28.4)
            if (p.Z > 5.6f && MathF.Abs(p.Y - 22f) < 0.15f && p.X > 27f && p.X < 30f)
                Assert.True(p.X > 27.3f && p.X < 28.6f, $"waypoint {i} {p} leaves the stair house outside its door");
        }
        Assert.Contains(Enumerable.Range(0, n), i => wps[i].Position.Z > 5.6f && wps[i].Traversal == TraversalKind.Door);
    }

    /// <summary>
    /// ⭐ UI-9 (HANDOFF_Behaviors_To_Ui_Demo_City_Quarters) — each new shared template, placed at (20, 20) with every door open: a path
    /// from the street ENDS in every room it has, on every storey, and on the office block's roof (through its stair house). Points are
    /// template-local (x, y, floor z).
    /// </summary>
    [Theory]
    [InlineData("courtyard-house", 8f, -6f, "yard 8,4,0 · west room 4,11,0 · east room 12,10,0 · upstairs 4,10,3")]
    [InlineData("office-4f", 10f, -6f, "lobby 5,6,0 · storey 2 5,6,3.2 · storey 3 5,6,6.4 · storey 4 5,6,9.6 · roof 5,6,12.8")]
    [InlineData("shop-2f", 6f, -6f, "shop 4,3,0 · store 4,8.5,0 · flat 4,5,3.5")]
    [InlineData("market-hall", 12f, -6f, "under the roof 12,4,0 · far side 20,12,0")]
    [InlineData("cqb-house", -6f, 5f, "hallway 7,5,0 · A 3.5,8,0 · B 10.5,8,0 · C 4.5,2,0 · D 10.4,2,0 · closet E 12.9,2,0")]   // UI-3: the front door is in the WEST wall
    public void UI9_EachQuarterTemplate_ThePathReachesEveryRoom_AndTheOfficeRoof(string template, float startX, float startY, string targets)
    {
        var folder = System.IO.Path.Combine(Fdp.Toolkit.Orchestration.ContentLayout.TerrainAssets(Fdp.Toolkit.Orchestration.ContentLayout.TryFindSourceTree()!), "bt-range");
        var world = TerrainWorldParser.Parse($$$"""
            {"type":"FeatureCollection","hrot":{"schemaVersion":1,"bounds":[0,0,60,60],"groundZ":0},"features":[
              {"type":"Feature","properties":{"kind":"building","template":"{{{template}}}","label":"T"},"geometry":{"type":"Point","coordinates":[20,20]}}]}
            """, "range", TerrainAssets.ForFolder(folder));
        var nav = (DotRecastNavmeshProvider)new RecastNavmeshFactory { Layers = NavLayerMask.Infantry }.Build(world)!;
        var open = Fdp.Toolkit.Terrain.Tests.DoorFixtures.States(world, world.Doors.Select(d => (d.Key, TerrainDoorState.Open)).ToArray());
        const uint Inf = (uint)NavLayerMask.Infantry;

        var start = new Vector3(20 + startX, 20 + startY, 0);
        var wps = new NavWaypoint[512];
        var misses = new System.Collections.Generic.List<string>();
        foreach (var t in targets.Split(" · "))
        {
            var parts = t.Split(' ');
            var xyz = parts[^1].Split(',').Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            var goal = new Vector3(20 + xyz[0], 20 + xyz[1], xyz[2]);
            int n = nav.PlanPath(start, goal, wps, Inf, open);
            var last = n > 0 ? wps[n - 1].Position : default;
            // ⚠ completeness is the poly path's: a partial path ends at the goal's x/y and height when the goal is straight above a reachable floor
            if (n < 2 || !nav.PathExists(start, goal, Inf, open) || MathF.Abs(last.Z - goal.Z) > 0.4f || Vector2.Distance(new Vector2(last.X, last.Y), new Vector2(goal.X, goal.Y)) > 0.5f)
                misses.Add($"{string.Join(' ', parts[..^1])} {goal}: ends at {(n > 0 ? last.ToString() : "nothing")}");
        }
        Assert.True(misses.Count == 0, $"{template}: " + string.Join(" | ", misses));
    }

    /// <summary>
    /// ⭐ UI-10 (HANDOFF_Behaviors_To_Ui_Demo_City_Quarters) — demo-city WITH ITS QUARTERS bakes, and every quarter building is entered
    /// from its street: a path from 3 m outside its front door (or a compound's gate) ends 2 m inside, all doors open. The bake time is
    /// printed (the terrain loads on every host — DESIGN_Urban_Battle_Demo §4a records it).
    /// </summary>
    [Fact]
    public void UI10_DemoCity_EveryQuarterBuilding_IsEnteredFromItsStreet_ThroughItsFrontDoor()
    {
        var folder = System.IO.Path.Combine(Fdp.Toolkit.Orchestration.ContentLayout.TerrainAssets(Fdp.Toolkit.Orchestration.ContentLayout.TryFindSourceTree()!), "demo-city");
        var world = TerrainWorldParser.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(folder, "demo-city.world.geojson")), "demo-city",
            TerrainAssets.ForFolder(folder));
        static bool Quarter(string label) => label.StartsWith("res-") || label.StartsWith("com-") || label.StartsWith("biz-");
        Assert.True(world.Buildings.Count(b => Quarter(b.Label)) >= 130, $"{world.Buildings.Count(b => Quarter(b.Label))} quarter buildings");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var nav = (DotRecastNavmeshProvider)new RecastNavmeshFactory { Layers = NavLayerMask.Infantry, TileCache = null }.Build(world)!;
        long bakeMs = sw.ElapsedMilliseconds;
        var open = Fdp.Toolkit.Terrain.Tests.DoorFixtures.States(world, world.Doors.Select(d => (d.Key, TerrainDoorState.Open)).ToArray());
        const uint Inf = (uint)NavLayerMask.Infantry;

        var wps = new NavWaypoint[256];
        var misses = new System.Collections.Generic.List<string>();
        int entered = 0;
        foreach (var door in world.Doors)
        {
            var parts = door.Key.Split('/');
            if (parts.Length < 3 || !Quarter(parts[1]) || (parts[2] != "front" && parts[2] != "gate")) continue;
            Assert.True(Fdp.Toolkit.Spatial.Eqs.EnterableDoorGenerator.TryInside(world, door, 2f, out var inside), $"{door.Key}: no inside");
            var outward = Vector2.Normalize(door.Center - inside);
            var outside = door.Center + outward * 3f;
            int n = nav.PlanPath(new Vector3(outside, door.SillZ), new Vector3(inside, door.SillZ), wps, Inf, open);
            var last = n > 0 ? wps[n - 1].Position : default;
            if (n < 2 || !nav.PathExists(new Vector3(outside, door.SillZ), new Vector3(inside, door.SillZ), Inf, open)
                || Vector2.Distance(new Vector2(last.X, last.Y), inside) > 0.5f) misses.Add($"{door.Key} (ends at {last})");
            else entered++;
        }
        Console.WriteLine($"[UI-10] demo-city: {world.Buildings.Count} buildings, {world.Doors.Count} doors; infantry bake {bakeMs} ms; {entered} front doors / gates entered");
        Assert.True(entered >= 130, $"only {entered} quarter front doors were tried");
        Assert.True(misses.Count == 0, $"{misses.Count} quarter buildings not entered from their street: " + string.Join(" | ", misses.Take(12)));
    }

    /// <summary>
    /// ⭐ UI-4 (HANDOFF_Behaviors_To_Ui_Platoon_Demos, <c>CE-2290</c>) — <c>cqb-village</c> bakes, and the demos can walk it: every house is entered
    /// from its street through its front door (3 m outside → 2 m inside, doors open), a path from the attacker assembly area (AA) ends on OBJ's
    /// roof through its stair house, and one from outside the compound ends in its yard through the west gate.
    /// </summary>
    [Fact]
    public void UI4_CqbVillage_EveryHouseIsEntered_OBJsRoofIsReachedFromTheAssemblyArea_AndTheCompoundByItsGate()
    {
        var folder = System.IO.Path.Combine(Fdp.Toolkit.Orchestration.ContentLayout.TerrainAssets(Fdp.Toolkit.Orchestration.ContentLayout.TryFindSourceTree()!), "cqb-village");
        var world = TerrainWorldParser.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(folder, "cqb-village.world.geojson")), "cqb-village",
            TerrainAssets.ForFolder(folder));
        var nav = (DotRecastNavmeshProvider)new RecastNavmeshFactory { Layers = NavLayerMask.Infantry, TileCache = null }.Build(world)!;
        var open = Fdp.Toolkit.Terrain.Tests.DoorFixtures.States(world, world.Doors.Select(d => (d.Key, TerrainDoorState.Open)).ToArray());
        const uint Inf = (uint)NavLayerMask.Infantry;
        var wps = new NavWaypoint[512];
        var misses = new System.Collections.Generic.List<string>();
        bool Reaches(Vector3 from, Vector3 to, string what)
        {
            int n = nav.PlanPath(from, to, wps, Inf, open);
            var last = n > 0 ? wps[n - 1].Position : default;
            // ⚠ completeness is the poly path's (PathExists): a partial path ends at the goal's x/y AND height when the goal is above a reachable floor
            bool ok = n >= 2 && nav.PathExists(from, to, Inf, open) && MathF.Abs(last.Z - to.Z) < 0.4f && Vector2.Distance(new Vector2(last.X, last.Y), new Vector2(to.X, to.Y)) < 0.5f;
            if (!ok) misses.Add($"{what} {to}: ends at {(n > 0 ? last.ToString() : "nothing")}");
            return ok;
        }
        foreach (var door in world.Doors.Where(d => d.Key.EndsWith("/front")))
        {
            Assert.True(Fdp.Toolkit.Spatial.Eqs.EnterableDoorGenerator.TryInside(world, door, 2f, out var inside), $"{door.Key}: no inside");
            var outside = door.Center + Vector2.Normalize(door.Center - inside) * 3f;
            Reaches(new Vector3(outside, door.SillZ), new Vector3(inside, door.SillZ), door.Key);
        }
        Reaches(new Vector3(120, 10, 0), new Vector3(123, 157, 6), "AA -> OBJ's roof");
        Reaches(new Vector3(230, 84, 0), new Vector3(265, 95, 0), "outside -> the CMP yard by the west gate");
        Assert.Equal(7, world.Doors.Count(d => d.Key.EndsWith("/front")));
        Assert.True(misses.Count == 0, string.Join(" | ", misses));
    }

    // ── CE-3111 · CE-1029 · R-218 P2 — the tiled bake (📄 Navigation_Design_v2_0.md §14 "P2 as built") ─────────────────

    /// <summary>A world of two rooms, 100 m apart, each with ONE real-width (0.9 m) door in its south wall; <paramref name="eastDoorAt"/>
    /// moves the east room's door along its wall (a geometry change the rebake rail makes).</summary>
    private static string TwoRooms(float eastDoorAt = 4.55f) => $$$"""
        {"type":"FeatureCollection","hrot":{"schemaVersion":1,"bounds":[0,0,160,60],"groundZ":0},"features":[
          {"type":"Feature","properties":{"kind":"building","label":"W","doors":{"front":"open"},"building":{
             "footprint":[[0,0],[10,0],[10,8],[0,8]],
             "storeys":[{"height":3,"walls":[
                  {"from":[0,0],"to":[10,0],"openings":[{"kind":"door","at":4.55,"width":0.9,"doorId":"front"}]},
                  {"from":[10,0],"to":[10,8]},{"from":[10,8],"to":[0,8]},{"from":[0,8],"to":[0,0]}]}]}},
           "geometry":{"type":"Point","coordinates":[20,20]}},
          {"type":"Feature","properties":{"kind":"building","label":"E","doors":{"front":"open"},"building":{
             "footprint":[[0,0],[10,0],[10,8],[0,8]],
             "storeys":[{"height":3,"walls":[
                  {"from":[0,0],"to":[10,0],"openings":[{"kind":"door","at":{{{eastDoorAt.ToString(System.Globalization.CultureInfo.InvariantCulture)}}},"width":0.9,"doorId":"front"}]},
                  {"from":[10,0],"to":[10,8]},{"from":[10,8],"to":[0,8]},{"from":[0,8],"to":[0,0]}]}]}},
           "geometry":{"type":"Point","coordinates":[120,20]}}]}
        """;

    private static string TempCacheFolder() => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hrot-navtiles-test-" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// ⭐ CE-3111 — a REAL door (0.9 m) is passable: the infantry tiles over each building bake at 0.15 m, the open ground stays at
    /// 0.3 m, and a walk from outside into each room goes through its door. (Before tiling: one 0.3 m tile ⇒ 0/5 grid alignments.)
    /// </summary>
    [Fact]
    public void CE3111_RealWidthDoors_BakeThroughFineTiles_TheOpenGroundStaysCoarse()
    {
        var factory = new RecastNavmeshFactory { Layers = NavLayerMask.Infantry, TileCache = null };
        var nav = factory.Build(TerrainWorldParser.Parse(TwoRooms(), "range"))!;
        var st = factory.LastStats;
        Assert.True(st.FineTiles > 0 && st.FineTiles < st.Tiles, $"fine tiles only over the buildings: {st}");
        Assert.True(nav.PathExists(new Vector3(25, 10, 0), new Vector3(25, 25, 0)), "into the west room through its 0.9 m door");
        Assert.True(nav.PathExists(new Vector3(125, 10, 0), new Vector3(125, 25, 0)), "into the east room through its 0.9 m door");
        Assert.True(nav.PathExists(new Vector3(5, 5, 0), new Vector3(150, 50, 0)), "the open ground connects across every tile border");
    }

    /// <summary>
    /// ⭐ CE-1029 — a second load bakes NOTHING: every tile comes from the cache — from memory, and (memory dropped, as in a new
    /// process) from the disk folder — and the mesh answers exactly as the baked one did.
    /// </summary>
    [Fact]
    public void CE1029_ASecondLoad_BakesNoTile_FromMemoryOrFromDisk_AndAnswersTheSame()
    {
        var folder = TempCacheFolder();
        try
        {
            var cache = new NavTileCache(folder);
            var factory = new RecastNavmeshFactory { TileCache = cache };
            var world = TerrainWorldParser.Parse(TwoRooms(), "range");

            var first = factory.Build(world)!;
            var baked = factory.LastStats;
            Assert.True(baked.Baked > 0 && baked.FromCache == 0, $"first load bakes: {baked}");

            var second = factory.Build(world)!;
            Assert.Equal((0, baked.Tiles), (factory.LastStats.Baked, factory.LastStats.FromCache));

            cache.ClearMemory();
            var third = factory.Build(world)!;
            Assert.Equal((0, baked.Tiles), (factory.LastStats.Baked, factory.LastStats.FromCache));
            Assert.NotEmpty(System.IO.Directory.GetFiles(folder, "*.navtile", System.IO.SearchOption.AllDirectories));

            var a = new Vector3(25, 10, 0); var b = new Vector3(125, 25, 0);
            float cost = first.PathCost(a, b, (uint)NavLayerMask.Infantry);
            Assert.True(cost > 0f && !float.IsInfinity(cost), $"a baked path: {cost}");
            Assert.Equal(cost, second.PathCost(a, b, (uint)NavLayerMask.Infantry), 3);
            Assert.Equal(cost, third.PathCost(a, b, (uint)NavLayerMask.Infantry), 3);
        }
        finally { try { System.IO.Directory.Delete(folder, true); } catch { } }
    }

    /// <summary>
    /// ⭐ <c>CE-3114</c> — writing a square's tile deletes that square's OTHER tiles unused for the prune age (a changed input or an old
    /// bake format leaves them behind), keeps the square's recent ones (another terrain sharing it), never touches another square; and a
    /// tile READ from disk is made young again, so a square still in use is never pruned.
    /// </summary>
    [Fact]
    public void CE3114_AWrittenTile_PrunesItsSquaresStaleTiles_KeepsRecentOnes_AndOtherSquares()
    {
        var folder = TempCacheFolder();
        try
        {
            var cache = new NavTileCache(folder, pruneAfter: TimeSpan.FromDays(1));
            var layerDir = System.IO.Path.Combine(folder, NavLayerMask.Infantry.ToString());
            System.IO.Directory.CreateDirectory(layerDir);
            string Make(string name, double daysOld)
            {
                var path = System.IO.Path.Combine(layerDir, name);
                System.IO.File.WriteAllBytes(path, Array.Empty<byte>());
                System.IO.File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-daysOld));
                return path;
            }
            var stale = Make("3_4_oldhash.navtile", 3);
            var recent = Make("3_4_otherterrain.navtile", 0.1);
            var otherSquare = Make("3_5_oldhash.navtile", 3);
            var readBack = Make("3_4_inuse.navtile", 3);

            Assert.True(cache.TryGet(new NavTileKey(NavLayerMask.Infantry, 3, 4, "inuse"), out _));   // read from disk ⇒ young again
            cache.Put(new NavTileKey(NavLayerMask.Infantry, 3, 4, "newhash"), null);

            Assert.False(System.IO.File.Exists(stale), "the square's stale tile is pruned");
            Assert.True(System.IO.File.Exists(recent), "a recent tile of the same square stays (another terrain)");
            Assert.True(System.IO.File.Exists(otherSquare), "another square is never touched");
            Assert.True(System.IO.File.Exists(readBack), "a tile read from disk is in use — kept");
            Assert.True(System.IO.File.Exists(System.IO.Path.Combine(layerDir, "3_4_newhash.navtile")));
        }
        finally { try { System.IO.Directory.Delete(folder, true); } catch { } }
    }

    /// <summary>
    /// ⭐ R-218 P2 — the touched-tile rebuild: after a geometry change (the east room's door moves) <see cref="RecastNavmeshFactory.Rebake"/>
    /// bakes ONLY the tiles the change reaches — the west room's and the open ground's come from the cache — and swaps one new
    /// snapshot (the version moves); the moved door is where the path now goes.
    /// </summary>
    [Fact]
    public void R218P2_Rebake_BakesOnlyTheTilesAChangeTouched_AndSwapsOneSnapshot()
    {
        var factory = new RecastNavmeshFactory { Layers = NavLayerMask.Infantry, TileCache = new NavTileCache() };
        var provider = (DotRecastNavmeshProvider)factory.Build(TerrainWorldParser.Parse(TwoRooms(eastDoorAt: 1.0f), "range"))!;
        var full = factory.LastStats;
        uint v0 = provider.QueryVersion();

        Assert.True(factory.Rebake(provider, TerrainWorldParser.Parse(TwoRooms(eastDoorAt: 8.0f), "range")));
        var re = factory.LastStats;
        Assert.True(re.Baked > 0 && re.Baked < full.Tiles / 2, $"only the east room's tiles re-bake: {re} (full bake {full})");
        Assert.Equal(full.Tiles, re.Baked + re.FromCache);
        Assert.Equal(v0 + 1, provider.QueryVersion());

        // The door moved from x 121.0–121.9 to 128.0–128.9: a walk in from (128.5, 10) now goes straight north through it.
        var wps = new NavWaypoint[64];
        int n = provider.PlanPath(new Vector3(128.5f, 10, 0), new Vector3(128.5f, 25, 0), wps, (uint)NavLayerMask.Infantry);
        Assert.True(n > 0, "a path into the east room");
        float len = 0f;
        var prev = new Vector3(128.5f, 10, 0);
        for (int i = 0; i < n; i++) { len += Vector2.Distance(new Vector2(prev.X, prev.Y), new Vector2(wps[i].Position.X, wps[i].Position.Y)); prev = wps[i].Position; }
        Assert.InRange(len, 14.9f, 15.6f);   // straight through the moved door (via the old one, at x 121.45, ≈ 20.8 m)
    }

    /// <summary>
    /// ⭐⭐ CE-3279 (b) — <c>PathCosts</c> answers an EQS sensor's candidates from ONE search, and agrees with an exact path per
    /// point: the same "no path" whenever the exact path fits the reach, and a cost that is the corridor through edge midpoints —
    /// never much shorter than the straightened path, at most a little longer. On the shipped test-town (buildings, walls).
    /// </summary>
    [Fact]
    public void CE3279_PathCosts_FromOneSearch_AgreeWithAPathPerPoint_OnTestTown()
    {
        var folder = System.IO.Path.Combine(Fdp.Toolkit.Orchestration.ContentLayout.TerrainAssets(Fdp.Toolkit.Orchestration.ContentLayout.TryFindSourceTree()!), "test-town");
        var world = TerrainWorldParser.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(folder, "test-town.world.geojson")), "test-town",
            TerrainAssets.ForFolder(folder));
        var nav = (DotRecastNavmeshProvider)new RecastNavmeshFactory { Layers = NavLayerMask.Infantry }.Build(world)!;
        const uint Inf = (uint)NavLayerMask.Infantry;

        var rng = new Random(3279);
        var min = world.BoundsMin; var max = world.BoundsMax;
        Vector3 Walkable()
        {
            while (true)
            {
                var p = new Vector3(min.X + (float)rng.NextDouble() * (max.X - min.X), min.Y + (float)rng.NextDouble() * (max.Y - min.Y), 0f);
                if (nav.ProjectToNavmesh(p, out var on, Inf)) return on;
            }
        }

        int compared = 0, unreachable = 0;
        double ratioSum = 0, worst = 0;
        var to = new Vector3[48]; var batch = new float[48];
        for (int o = 0; o < 40; o++)
        {
            var from = Walkable();
            float far = 0f;
            for (int i = 0; i < to.Length; i++)
            {
                float ang = (float)(rng.NextDouble() * Math.PI * 2), r = 5f + (float)rng.NextDouble() * 55f;
                to[i] = new Vector3(from.X + MathF.Cos(ang) * r, from.Y + MathF.Sin(ang) * r, from.Z);
                far = Math.Max(far, Vector3.Distance(from, to[i]));
            }
            float reach = far * 1.5f + 25f;
            nav.PathCosts(from, to, batch, reach, Inf, null);
            for (int i = 0; i < to.Length; i++)
            {
                float exact = nav.PathCost(from, to[i], Inf);
                if (exact == float.MaxValue)
                {
                    Assert.Equal(float.MaxValue, batch[i]);   // the batch never reaches what a full path cannot
                    unreachable++;
                    continue;
                }
                if (exact > reach * 0.9f) continue;   // a path near the reach may leave the circle — that is the documented trade
                Assert.True(batch[i] != float.MaxValue, $"origin {from}, point {to[i]}: a {exact:F1} m path inside the {reach:F0} m reach read as no path");
                Assert.True(batch[i] >= exact * 0.95f - 1f, $"{batch[i]:F1} m is shorter than the exact {exact:F1} m");
                Assert.True(batch[i] <= exact * 1.5f + 5f, $"{batch[i]:F1} m is far longer than the exact {exact:F1} m");
                double ratio = exact < 1f ? 1 : batch[i] / exact;
                ratioSum += ratio; worst = Math.Max(worst, ratio); compared++;
            }
        }
        Assert.True(compared >= 1000, $"only {compared} points compared");
        Assert.Equal(0, nav.PathCostFallbacks);   // the search never outgrew its buffer here
        Console.WriteLine($"[CE3279] test-town: {compared} points compared, {unreachable} unreachable (both agree), cost ratio mean {ratioSum / compared:F3}, worst {worst:F3}");
    }
}
