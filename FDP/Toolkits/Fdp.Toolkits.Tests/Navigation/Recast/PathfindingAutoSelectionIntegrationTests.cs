#nullable enable
using Fdp.Toolkit.Navigation.Recast;
using System;
using System.Collections.Generic;
using System.Numerics;
using CarKinem.Road;
using CarKinem.Trajectory;
using DotRecast.Detour;
using Fdp.Core;
using Fdp.Core.Collections;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Systems;
using Xunit;

namespace Fdp.Toolkit.Navigation.Recast.Tests;

/// <summary>
/// Integration tests for T5 (STR-P2-T5): verifies that <see cref="PathfindingSolverSystem"/>
/// already implements Auto backend selection and that it works correctly when both a
/// real <see cref="DotRecastNavmeshProvider"/> (from a baked navmesh) and a
/// <see cref="ZoneEnvironmentData"/> (road graph) singleton are present.
///
/// <para>
/// <b>T5 confirms.</b> <see cref="PathfindingSolverSystem"/> already has Auto selection
/// (added by prior nav-work batches).  This file is a verification harness, not new
/// logic: the three scenarios (RoadGraph / Navmesh / Hybrid) are asserted using
/// real singletons materialized via <see cref="ZoneManagerService"/>-equivalent setup.
/// </para>
///
/// <para>
/// Scenarios:
/// <list type="bullet">
///   <item>⛔ T5-SC1 / T5-SC3 (the distance heuristic) SUPERSEDED by CE-3128 (R-230): the road is taken on COST per the
///   actor's <see cref="RoadUse"/> — <c>CE3128_*</c> below.</item>
///   <item>T5-SC2: Both endpoints far from all road nodes, navmesh available → Navmesh.</item>
///   <item>T5-SC4: <see cref="ZoneEnvironmentData"/> singleton is present in ECS after
///               materialization via <see cref="RoadNetworkBuilder"/>.</item>
/// </list>
/// </para>
/// </summary>
public sealed class PathfindingAutoSelectionIntegrationTests : IDisposable
{
    // ── Shared navmesh fixture ────────────────────────────────────────────────

    // Bake a flat navmesh once to serve as the INavmeshProvider for all tests.
    private static readonly Dictionary<NavLayerMask, DtNavMesh> SharedNavMeshes = BakeFlatGround(-100f, 100f, -100f, 100f);

    // ── Infrastructure ────────────────────────────────────────────────────────

    private readonly EntityRepository       _world;
    private readonly TrajectoryPoolManager  _pool;

    public PathfindingAutoSelectionIntegrationTests()
    {
        _world = new EntityRepository();
        _world.RegisterEvent<PathfindingRequestEvent>();
        _world.RegisterEvent<PathfindingResultEvent>();

        var batch = new PathfindingBatchData
        {
            Results = new NativeArray<PathResult>(PathfindingBatchData.DefaultCapacity, Allocator.Persistent),
        };
        _world.SetSingleton(batch);

        _pool = new TrajectoryPoolManager();
    }

    public void Dispose()
    {
        if (_world.HasSingleton<PathfindingBatchData>())
        {
            ref var b = ref _world.GetSingleton<PathfindingBatchData>();
            if (b.Results.IsCreated) b.Results.Dispose();
        }
        _pool.Dispose();
        _world.Dispose();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a two-node road network (nodes at (0,0) and (100,0) in FDP XY).
    /// </summary>
    private static RoadNetworkBlob BuildTwoNodeRoadNetwork()
    {
        var builder = new RoadNetworkBuilder();
        builder.AddNode(new Vector2(0f, 0f));
        builder.AddNode(new Vector2(100f, 0f));
        builder.AddSegment(
            new Vector2(0f, 0f),   new Vector2(50f, 0f),
            new Vector2(100f, 0f), new Vector2(50f, 0f),
            startNodeIdx: 0, endNodeIdx: 1);
        return builder.Build(cellSize: 20f, gridWidth: 10, gridHeight: 10);
    }

    /// <summary>
    /// Materializes <see cref="ZoneEnvironmentData"/> from a <see cref="RoadNetworkBlob"/>
    /// and injects it as an ECS singleton — equivalent to what <c>ZoneManagerService.LoadZones</c>
    /// does when it calls <c>RoadNetworkLoader.LoadFromJson</c>.
    /// </summary>
    private void InjectZoneEnvironmentData(RoadNetworkBlob blob)
    {
        _world.SetSingleton(new ZoneEnvironmentData { RoadNetwork = blob });
    }

    /// <summary>
    /// Publishes a request, advances the solver, and returns the result event.
    /// </summary>
    private PathfindingResultEvent RunSolver(
        PathfindingSolverSystem solver,
        Vector3 start, Vector3 end, RoadUse roadUse = RoadUse.Unspecified)
    {
        long requestId = ((long)_world.GlobalVersion << 16) | (long)start.GetHashCode();
        _world.Bus.Publish(new PathfindingRequestEvent
        {
            RequestId    = requestId,
            Start        = start,
            End          = end,
            BackendForce = NavigationBackend.Auto,
            RoadUse      = roadUse,
        });

        _world.Bus.SwapBuffers();
        solver.Execute((ISimulationView)_world, 0f);

        var ecb = (EntityCommandBuffer)((ISimulationView)_world).GetCommandBuffer();
        ecb.Playback(_world);
        _world.Bus.SwapBuffers();

        var events = ((ISimulationView)_world).ReadEvents<PathfindingResultEvent>();
        Assert.Equal(1, events.Length);
        return events[0];
    }

    // ── CE-3128 (R-230): the road is taken on COST, per the actor's RoadUse ──────

    /// <summary>A road along y = 0 from x −90 to x 90, inside the baked flat ground.</summary>
    private static RoadNetworkBlob BuildInMeshRoad()
    {
        var builder = new RoadNetworkBuilder();
        builder.AddNode(new Vector2(-90f, 0f));
        builder.AddNode(new Vector2(90f, 0f));
        builder.AddSegment(new Vector2(-90f, 0f), new Vector2(180f, 0f), new Vector2(90f, 0f), new Vector2(180f, 0f),
            laneWidth: 5f, laneCount: 2, startNodeIdx: 0, endNodeIdx: 1);
        return builder.Build(cellSize: 20f, gridWidth: 10, gridHeight: 10);
    }

    /// <summary>
    /// ⭐ CE-3128 — with a REAL navmesh and a road 10 m off the line: walking direct costs 160 m; via the road 10 + 160 + 10.
    /// Neutral (×1) walks direct; Prefer (×0.5: 10 + 80 + 10 = 100) splices through the road (Hybrid). ⛔ Supersedes T5-SC1
    /// ("both ends near a road node ⇒ NavRoadGraph"), the geometry heuristic R-230 retired.
    /// </summary>
    [Fact]
    public void CE3128_RealNavmesh_NeutralWalksDirect_PreferSplicesThroughTheRoad()
    {
        var roadNet = BuildInMeshRoad();
        var navmesh = new DotRecastNavmeshProvider(SharedNavMeshes);
        var solver  = new PathfindingSolverSystem(roadNet, _pool, navmesh: navmesh);
        InjectZoneEnvironmentData(roadNet);

        var neutral = RunSolver(solver, new Vector3(-80f, -10f, 0f), new Vector3(80f, -10f, 0f), RoadUse.Neutral);
        Assert.True(neutral.IsReachable);
        Assert.Equal(NavigationBackend.Navmesh, neutral.PrimaryBackend);

        var prefer = RunSolver(solver, new Vector3(-80f, -10f, 0f), new Vector3(80f, -10f, 0f), RoadUse.Prefer);
        Assert.True(prefer.IsReachable);
        Assert.Equal(NavigationBackend.Hybrid, prefer.PrimaryBackend);
        Assert.True(_pool.TryGetTrajectory(prefer.RouteHandle, out var route));
        bool alongTheRoad = false;
        for (int i = 0; i < route.Waypoints.Length; i++)
        {
            var p = route.Waypoints[i].Position;
            if (MathF.Abs(p.Y) < 0.5f && p.X > -50f && p.X < 50f) alongTheRoad = true;
        }
        Assert.True(alongTheRoad, "a Prefer route must run along the road (y = 0) between the access and egress legs");

        roadNet.Dispose();
    }

    /// <summary>
    /// ⭐ CE-3128 — a move to where the unit already stands (a behaviour sent to a handle it is on) is a two-point route, not
    /// "no path". 📌 Measured live in ua-danger-crossing: a real navmesh answers coincident points with no path, so the
    /// rush's first leg failed every tick and the rifleman crept across at ~0.03 m/s and never arrived. The pre-CE-3128 navmesh
    /// solve returned the two points; this pins it for every RoadUse, with roads on the map.
    /// </summary>
    [Theory]
    [InlineData(0f, RoadUse.Neutral)]
    [InlineData(0.05f, RoadUse.Neutral)]
    [InlineData(0.05f, RoadUse.Prefer)]
    [InlineData(0.05f, RoadUse.Never)]
    public void CE3128_RealNavmesh_AMoveToWhereTheUnitStands_IsATwoPointRoute(float offset, RoadUse roadUse)
    {
        var roadNet = BuildInMeshRoad();
        var navmesh = new DotRecastNavmeshProvider(SharedNavMeshes);
        var solver  = new PathfindingSolverSystem(roadNet, _pool, navmesh: navmesh);
        InjectZoneEnvironmentData(roadNet);

        var start = new Vector3(20f, -12f, 0f);
        var result = RunSolver(solver, start, start + new Vector3(offset, offset, 0f), roadUse);
        Assert.True(result.IsReachable);
        Assert.True(_pool.TryGetTrajectory(result.RouteHandle, out var route));
        Assert.Equal(2, route.Waypoints.Length);

        roadNet.Dispose();
    }

    // ── T5-SC2: Both endpoints far from road → Navmesh ───────────────────────

    /// <summary>
    /// With both singletons present, endpoints far from all road nodes select Navmesh.
    ///
    /// Confirms §10.3: "else → Navmesh".
    /// </summary>
    [Fact]
    public void AutoSelect_BothFarFromRoad_WithNavmeshAndRoadGraph_ReturnsNavmesh()
    {
        // Road nodes are at (0,0) and (100,0); endpoints at (2000,2000)/(3000,3000) are
        // far beyond the 500 m RoadRadiusThreshold.
        var roadNet = BuildTwoNodeRoadNetwork();
        var navmesh = new DotRecastNavmeshProvider(SharedNavMeshes);
        var solver  = new PathfindingSolverSystem(roadNet, _pool, navmesh: navmesh);

        InjectZoneEnvironmentData(roadNet);

        var result = RunSolver(
            solver,
            new Vector3(2000f, 2000f, 0f),
            new Vector3(3000f, 3000f, 0f));

        Assert.Equal(NavigationBackend.Navmesh, result.PrimaryBackend);

        roadNet.Dispose();
    }

    // ── CE-3128: Never ─────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ CE-3128 — a sneaking unit (<see cref="RoadUse.Never"/>) walks the navmesh even when both ends lie ON the road. ⛔
    /// Supersedes T5-SC3 ("one end near a road node ⇒ Hybrid"), the geometry heuristic R-230 retired.
    /// </summary>
    [Fact]
    public void CE3128_RealNavmesh_Never_KeepsOffTheRoad_EvenFromTheRoad()
    {
        var roadNet = BuildInMeshRoad();
        var navmesh = new DotRecastNavmeshProvider(SharedNavMeshes);
        var solver  = new PathfindingSolverSystem(roadNet, _pool, navmesh: navmesh);
        InjectZoneEnvironmentData(roadNet);

        var result = RunSolver(solver, new Vector3(-80f, 0f, 0f), new Vector3(80f, 0f, 0f), RoadUse.Never);

        Assert.True(result.IsReachable);
        Assert.Equal(NavigationBackend.Navmesh, result.PrimaryBackend);
        roadNet.Dispose();
    }

    // ── T5-SC4: ZoneEnvironmentData singleton materialization ────────────────

    /// <summary>
    /// After calling <see cref="InjectZoneEnvironmentData"/> (which mirrors
    /// <c>ZoneManagerService.LoadZones</c>), the <see cref="ZoneEnvironmentData"/>
    /// ECS singleton is present and contains road nodes.
    /// </summary>
    [Fact]
    public void ZoneEnvironmentData_AfterMaterialization_IsPresent_WithRoadNodes()
    {
        var roadNet = BuildTwoNodeRoadNetwork();
        InjectZoneEnvironmentData(roadNet);

        Assert.True(_world.HasSingleton<ZoneEnvironmentData>(),
            "ZoneEnvironmentData singleton must be present after materialization.");

        ref var zed = ref _world.GetSingleton<ZoneEnvironmentData>();
        Assert.True(zed.RoadNetwork.Nodes.IsCreated && zed.RoadNetwork.Nodes.Length == 2,
            $"Road network must have 2 nodes; got {(zed.RoadNetwork.Nodes.IsCreated ? zed.RoadNetwork.Nodes.Length.ToString() : "not created")}.");

        roadNet.Dispose();
    }

    // ── T5-SC5: PathfindingSolverSystem already has Auto (verification) ───────

    /// <summary>
    /// Verifies that <see cref="PathfindingSolverSystem.SelectBackend"/> (via Auto)
    /// falls back to NavRoadGraph when no navmesh is available and both endpoints are
    /// near road nodes.  This confirms the system was not modified — the Auto logic
    /// pre-exists from prior nav-work batches.
    /// </summary>
    [Fact]
    public void AutoSelect_NoNavmesh_BothNearRoad_FallsBackToNavRoadGraph()
    {
        var roadNet = BuildTwoNodeRoadNetwork();
        // No navmesh provider.
        var solver  = new PathfindingSolverSystem(roadNet, _pool);

        var result = RunSolver(solver, new Vector3(0f, 0f, 0f), new Vector3(100f, 0f, 0f));

        Assert.Equal(NavigationBackend.NavRoadGraph, result.PrimaryBackend);
        Assert.True(result.IsReachable);

        roadNet.Dispose();
    }

    // ── Navmesh baker helper ──────────────────────────────────────────────────

    private static Dictionary<NavLayerMask, DtNavMesh> BakeFlatGround(
        float xMin, float xMax, float zMin, float zMax)
    {
        // Crowd/navmesh space: X=East, Y=0=altitude, Z=North.
        float[] verts =
        {
            xMin, 0f, zMin,
            xMax, 0f, zMin,
            xMax, 0f, zMax,
            xMin, 0f, zMax,
        };
        int[] indices = { 0, 2, 1,  0, 3, 2 };

        var baker  = new RecastNavmeshBaker();
        var meshes = baker.Bake(verts, indices, NavLayerMask.Infantry);

        if (!meshes.ContainsKey(NavLayerMask.Infantry))
            throw new InvalidOperationException("Infantry navmesh bake failed for flat ground.");

        return meshes;
    }

    // ── CE-3128 on the SHIPPED test-town: its world, its roads.json, a real vehicle-layer bake ──────────

    private static string ShippedTerrain(string name)
    {
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "Hrot", "Subsystems"))) dir = dir.Parent;
        return System.IO.Path.Combine(dir!.FullName, "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Recipes", "Terrain", name);
    }

    /// <summary>
    /// ⭐⭐ CE-3128 (R-230) — the whole chain on the real terrain: a vehicle crossing test-town from just south-west of Main Street's
    /// west end to just north of its east end. <see cref="RoadUse.Prefer"/> drives Main Street (y 200) through the middle of town;
    /// <see cref="RoadUse.Never"/> stays on the navmesh and never runs along the street. The terrain, the road graph and the bake
    /// are the shipped ones, so this is what a cluster vehicle does.
    /// </summary>
    [Fact]
    public void CE3128_ShippedTestTown_PreferDrivesMainStreet_NeverDoesNot()
    {
        var folder = ShippedTerrain("test-town");
        var world = Fdp.Toolkit.Terrain.TerrainWorldParser.Parse(
            System.IO.File.ReadAllText(System.IO.Path.Combine(folder, "test-town.world.geojson")), "test-town",
            Fdp.Toolkit.Terrain.TerrainAssets.ForFolder(folder));
        var navmesh = new RecastNavmeshFactory { Layers = NavLayerMask.Vehicle }.Build(world)!;
        Assert.NotNull(navmesh);
        var roadNet = RoadNetworkLoader.LoadFromJson(System.IO.Path.Combine(folder, "roads.json"));
        var solver  = new PathfindingSolverSystem(roadNet, _pool, navmesh: navmesh);
        InjectZoneEnvironmentData(roadNet);

        var start = new Vector3(10f, 185f, 0f);
        var end   = new Vector3(390f, 215f, 0f);

        var prefer = RunSolver(solver, start, end, RoadUse.Prefer);
        Assert.True(prefer.IsReachable);
        Assert.Contains(prefer.PrimaryBackend, new[] { NavigationBackend.Hybrid, NavigationBackend.NavRoadGraph });
        Assert.True(_pool.TryGetTrajectory(prefer.RouteHandle, out var preferRoute));
        int onStreet = 0;
        for (int i = 0; i < preferRoute.Waypoints.Length; i++)
        {
            var p = preferRoute.Waypoints[i].Position;
            if (MathF.Abs(p.Y - 200f) < 1f && p.X > 60f && p.X < 340f) onStreet++;
        }
        Assert.True(onStreet >= 4, $"Prefer drives Main Street: {onStreet} waypoints on its centre line");

        var never = RunSolver(solver, start, end, RoadUse.Never);
        Assert.Equal(NavigationBackend.Navmesh, never.PrimaryBackend);

        roadNet.Dispose();
    }
}

