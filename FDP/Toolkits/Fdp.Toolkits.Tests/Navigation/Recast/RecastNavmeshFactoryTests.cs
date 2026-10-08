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
            raw.FindStraightPath(s, e, polys.AsSpan(0, n), n, straight, out _, 256, DotRecast.Detour.DtStraightPathOptions.DT_STRAIGHTPATH_ALL_CROSSINGS);
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
    /// 0.3 m infantry erosion — one or two 0.3 m voxels, so whether it bakes depends on grid alignment (the doors are 1.2 m now); and
    /// <c>TerrainWorldMesh</c> dropped every
    /// 2 m ground cell whose centre lay in a wall panel, so the 0.15 m inner wall at x 105 (a cell centre) cut a 2 m strip out of the
    /// floor (walkable ended at 103.8 and began at 106.2) and neither the hall nor the front doorway connected. The agent got a
    /// partial path to the wall and "arrived" outside.</para>
    /// </summary>
    [Fact]
    public void Stage5d_BtRangeHouseA_EveryDoorwayConnectsForInfantry()
    {
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "Hrot", "Subsystems", "Hrot.AI.Behaviors"))) dir = dir.Parent;
        var folder = System.IO.Path.Combine(dir!.FullName, "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Recipes", "Terrain", "bt-range");
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
}
