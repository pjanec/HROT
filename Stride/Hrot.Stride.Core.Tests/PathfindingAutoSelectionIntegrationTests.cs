#nullable enable
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
using Hrot.Stride.Core;
using Xunit;

namespace Hrot.Stride.Core.Tests;

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
///   <item>T5-SC1: Both endpoints within <c>RoadRadiusThresholdSq</c> of road nodes → NavRoadGraph.</item>
///   <item>T5-SC2: Both endpoints far from all road nodes, navmesh available → Navmesh.</item>
///   <item>T5-SC3: One endpoint near a road node, the other far away → Hybrid.</item>
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
        Vector3 start, Vector3 end)
    {
        long requestId = ((long)_world.GlobalVersion << 16) | (long)start.GetHashCode();
        _world.Bus.Publish(new PathfindingRequestEvent
        {
            RequestId    = requestId,
            Start        = start,
            End          = end,
            BackendForce = NavigationBackend.Auto,
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

    // ── T5-SC1: Both endpoints near road nodes → NavRoadGraph ────────────────

    /// <summary>
    /// With both <see cref="DotRecastNavmeshProvider"/> and <see cref="ZoneEnvironmentData"/>
    /// singletons present, endpoints within the road-radius threshold select NavRoadGraph.
    ///
    /// Confirms §10.3 Auto selection: "endpoints within <c>RoadRadiusThresholdSq</c> of
    /// road nodes → RoadGraph".
    /// </summary>
    [Fact]
    public void AutoSelect_BothNearRoadNode_WithNavmeshAndRoadGraph_ReturnsNavRoadGraph()
    {
        // Arrange: road nodes at (0,0) and (100,0) in FDP XY.
        var roadNet  = BuildTwoNodeRoadNetwork();
        var navmesh  = new DotRecastNavmeshProvider(SharedNavMeshes);
        var solver   = new PathfindingSolverSystem(roadNet, _pool, navmesh: navmesh);

        // Materialize ZoneEnvironmentData (as ZoneManagerService.LoadZones would).
        InjectZoneEnvironmentData(roadNet);

        // Endpoints on/near the road nodes (within 500 m threshold).
        var result = RunSolver(solver, new Vector3(0f, 0f, 0f), new Vector3(100f, 0f, 0f));

        Assert.Equal(NavigationBackend.NavRoadGraph, result.PrimaryBackend);
        Assert.True(result.IsReachable, "Road path between the two nodes must be reachable.");

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

    // ── T5-SC3: Mixed endpoints → Hybrid ─────────────────────────────────────

    /// <summary>
    /// With both singletons present, one endpoint near a road node and one far away
    /// selects Hybrid.
    ///
    /// Confirms §10.3: "mixed → spliced Hybrid".
    /// </summary>
    [Fact]
    public void AutoSelect_MixedEndpoints_WithNavmeshAndRoadGraph_ReturnsHybrid()
    {
        var roadNet = BuildTwoNodeRoadNetwork();
        var navmesh = new DotRecastNavmeshProvider(SharedNavMeshes);
        var solver  = new PathfindingSolverSystem(roadNet, _pool, navmesh: navmesh);

        InjectZoneEnvironmentData(roadNet);

        // Start near road node (0,0); end far off-road.
        var result = RunSolver(
            solver,
            new Vector3(0f, 0f, 0f),        // near road node 0
            new Vector3(2000f, 2000f, 0f)); // far from all road nodes

        Assert.Equal(NavigationBackend.Hybrid, result.PrimaryBackend);

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

        var baker  = new StrideNavmeshBaker();
        var meshes = baker.Bake(verts, indices, NavLayerMask.Infantry);

        if (!meshes.ContainsKey(NavLayerMask.Infantry))
            throw new InvalidOperationException("Infantry navmesh bake failed for flat ground.");

        return meshes;
    }
}
