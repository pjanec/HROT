using System;
using System.Numerics;
using CarKinem.Road;
using CarKinem.Trajectory;
using Fdp.Core;
using Fdp.Core.Collections;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Fake;
using Fdp.Toolkit.Navigation.Systems;
using Fdp.ModuleHost.Abstractions;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests
{
    /// <summary>
    /// T3 -- Backend selection in <see cref="PathfindingSolverSystem"/>.
    /// Verifies: forced backend override, Flying/Volumetric dispatch, handle
    /// allocation for anonymous requests, and Brain-handle echo.
    /// </summary>
    public sealed class PathfindingSolverBackendSelectionTests : IDisposable
    {
        private readonly EntityRepository    _world;
        private readonly TrajectoryPoolManager _pool;

        public PathfindingSolverBackendSelectionTests()
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
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static RoadNetworkBlob BuildTwoNodeNetwork()
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
        /// Publishes a request, runs the solver pipeline, and returns the result event.
        /// Asserts exactly one result event is produced.
        /// </summary>
        private static void RunSolverPipeline(
            EntityRepository world,
            PathfindingSolverSystem solver,
            float dt = 0f)
        {
            var view = (ISimulationView)world;
            world.Bus.SwapBuffers();           // requests become readable
            solver.Execute(view, dt);
            var ecb = (EntityCommandBuffer)view.GetCommandBuffer();
            ecb.Playback(world);
            world.Bus.SwapBuffers();           // results become readable
        }

        // ── Test: BackendForce NavRoadGraph still uses Dijkstra ───────────────────

        [Fact]
        public void BackendForce_NavRoadGraph_UsesRoadGraphAndFindsPath()
        {
            // Arrange
            var roadNet = BuildTwoNodeNetwork();
            var solver  = new PathfindingSolverSystem(roadNet, _pool);

            long requestId = ((long)1 << 32) | _world.GlobalVersion;
            _world.Bus.Publish(new PathfindingRequestEvent
            {
                RequestId    = requestId,
                Start        = new Vector3(0f, 0f, 0f),
                End          = new Vector3(100f, 0f, 0f),
                BackendForce = NavigationBackend.NavRoadGraph,
            });

            // Act
            RunSolverPipeline(_world, solver);

            // Assert
            var view   = (ISimulationView)_world;
            var events = view.ReadEvents<PathfindingResultEvent>();
            Assert.Equal(1, events.Length);
            Assert.True(events[0].IsReachable);
            Assert.Equal(NavigationBackend.NavRoadGraph, events[0].PrimaryBackend);

            roadNet.Dispose();
        }

        // ── Test: Flying MobilityProfile invokes IVolumetricPathProvider ──────────

        [Fact]
        public void MobilityProfile_Flying_InvokesVolumetricProvider_NotNavmesh()
        {
            // Arrange
            var volumetric = new StubVolumetricProvider();
            var navmesh    = new StubNavmeshProvider();
            var solver     = new PathfindingSolverSystem(
                default(RoadNetworkBlob), _pool,
                navmesh: navmesh, volumetric: volumetric);

            long requestId = ((long)2 << 32) | _world.GlobalVersion;
            _world.Bus.Publish(new PathfindingRequestEvent
            {
                RequestId       = requestId,
                Start           = new Vector3(0f, 0f, 0f),
                End             = new Vector3(50f, 0f, 50f),
                MobilityProfile = 4,   // Flying
            });

            // Act
            RunSolverPipeline(_world, solver);

            // Assert
            Assert.True(volumetric.WasCalled,   "Volumetric PlanPath must be called for Flying.");
            Assert.False(navmesh.PlanPathWasCalled, "Navmesh PlanPath must NOT be called for Flying.");

            var view   = (ISimulationView)_world;
            var events = view.ReadEvents<PathfindingResultEvent>();
            Assert.Equal(1, events.Length);
            Assert.True(events[0].IsReachable);
            Assert.Equal(NavigationBackend.Volumetric, events[0].PrimaryBackend);
        }

        // ── Test: anonymous request (RouteHandle == 0) receives internal handle ───

        [Fact]
        public void HandleEcho_AnonymousMoveTo_AssignsInternalHandle()
        {
            // Arrange
            var roadNet = BuildTwoNodeNetwork();
            var solver  = new PathfindingSolverSystem(roadNet, _pool);

            long requestId = ((long)3 << 32) | _world.GlobalVersion;
            _world.Bus.Publish(new PathfindingRequestEvent
            {
                RequestId   = requestId,
                Start       = new Vector3(0f, 0f, 0f),
                End         = new Vector3(100f, 0f, 0f),
                RouteHandle = 0,  // anonymous
            });

            // Act
            RunSolverPipeline(_world, solver);

            // Assert
            var view   = (ISimulationView)_world;
            var events = view.ReadEvents<PathfindingResultEvent>();
            Assert.Equal(1, events.Length);
            Assert.True(events[0].IsReachable);
            Assert.True(
                events[0].RouteHandle >= NavigationHandleAllocator.MuscleHandleBase,
                $"Anonymous handle must be >= 0x{NavigationHandleAllocator.MuscleHandleBase:X8}; got {events[0].RouteHandle}");

            roadNet.Dispose();
        }

        // ── Test: Brain-allocated handle is echoed unchanged ──────────────────────

        [Fact]
        public void HandleEcho_BrainHandle_IsPreserved()
        {
            // Arrange
            var roadNet = BuildTwoNodeNetwork();
            var solver  = new PathfindingSolverSystem(roadNet, _pool);

            long requestId = ((long)4 << 32) | _world.GlobalVersion;
            _world.Bus.Publish(new PathfindingRequestEvent
            {
                RequestId   = requestId,
                Start       = new Vector3(0f, 0f, 0f),
                End         = new Vector3(100f, 0f, 0f),
                RouteHandle = 99,   // Brain-allocated
            });

            // Act
            RunSolverPipeline(_world, solver);

            // Assert
            var view   = (ISimulationView)_world;
            var events = view.ReadEvents<PathfindingResultEvent>();
            Assert.Equal(1, events.Length);
            Assert.True(events[0].IsReachable);
            Assert.Equal(99, events[0].RouteHandle);

            roadNet.Dispose();
        }

        // ── OFX-001: Auto-select backend based on both endpoint proximity ──────────

        /// <summary>
        /// Both start AND end are near a road node → NavRoadGraph (§5.2).
        /// </summary>
        [Fact]
        public void AutoSelect_BothEndpointsNearRoad_ReturnsNavRoadGraph()
        {
            // Arrange: nodes at (0,0) and (100,0); both endpoints within 500m threshold.
            var roadNet = BuildTwoNodeNetwork();
            var solver  = new PathfindingSolverSystem(roadNet, _pool);

            long requestId = ((long)10 << 32) | _world.GlobalVersion;
            _world.Bus.Publish(new PathfindingRequestEvent
            {
                RequestId    = requestId,
                Start        = new Vector3(0f, 0f, 0f),    // on road node 0
                End          = new Vector3(100f, 0f, 0f),  // on road node 1
                BackendForce = NavigationBackend.Auto,
            });

            RunSolverPipeline(_world, solver);

            var events = ((ISimulationView)_world).ReadEvents<PathfindingResultEvent>();
            Assert.Equal(1, events.Length);
            Assert.Equal(NavigationBackend.NavRoadGraph, events[0].PrimaryBackend);

            roadNet.Dispose();
        }

        /// <summary>
        /// Start is near a road node, end is far away → Hybrid (§5.2).
        /// </summary>
        [Fact]
        public void AutoSelect_MixedEndpoints_ReturnsHybrid()
        {
            // Arrange: node at (0,0); start is near (0,0), end is at (2000, 2000) which is
            // further than the 500 m RoadRadiusThreshold from any node.
            var roadNet = BuildTwoNodeNetwork();
            var solver  = new PathfindingSolverSystem(roadNet, _pool);

            long requestId = ((long)11 << 32) | _world.GlobalVersion;
            _world.Bus.Publish(new PathfindingRequestEvent
            {
                RequestId    = requestId,
                Start        = new Vector3(0f, 0f, 0f),       // near road node 0
                End          = new Vector3(2000f, 2000f, 0f), // far from all road nodes
                BackendForce = NavigationBackend.Auto,
            });

            RunSolverPipeline(_world, solver);

            var events = ((ISimulationView)_world).ReadEvents<PathfindingResultEvent>();
            Assert.Equal(1, events.Length);
            Assert.Equal(NavigationBackend.Hybrid, events[0].PrimaryBackend);

            roadNet.Dispose();
        }

        /// <summary>
        /// Both start AND end are far from any road node, navmesh provider available → Navmesh (§5.2).
        /// </summary>
        [Fact]
        public void AutoSelect_BothEndpointsFarFromRoad_WithNavmesh_ReturnsNavmesh()
        {
            // Arrange: both endpoints at (2000, 2000) and (3000, 3000) — far from all road nodes.
            var roadNet = BuildTwoNodeNetwork();
            var navmesh = new StubNavmeshProvider();
            var solver  = new PathfindingSolverSystem(roadNet, _pool, navmesh: navmesh);

            long requestId = ((long)12 << 32) | _world.GlobalVersion;
            _world.Bus.Publish(new PathfindingRequestEvent
            {
                RequestId    = requestId,
                Start        = new Vector3(2000f, 2000f, 0f),
                End          = new Vector3(3000f, 3000f, 0f),
                BackendForce = NavigationBackend.Auto,
            });

            RunSolverPipeline(_world, solver);

            var events = ((ISimulationView)_world).ReadEvents<PathfindingResultEvent>();
            Assert.Equal(1, events.Length);
            Assert.Equal(NavigationBackend.Navmesh, events[0].PrimaryBackend);

            roadNet.Dispose();
        }

        // ── CE-3011 / W7 rail: every navigation API is Z-up ────────────────────────

        /// <summary>
        /// CE-3011 (the solver passed Sim Z-up positions to a Y-up provider and swizzled only the output):
        /// a request whose goal lies NORTH (+Y) of the start, with a blocked polygon (a wall) between them, must come
        /// back as a Z-up path — the detour waypoints carry the polygon ELEVATION in Z and the NORTH distance in Y,
        /// and the final waypoint is the goal itself (Y≈50, Z≈0). Before W7 the swizzle put north into Z.
        /// </summary>
        [Fact]
        public void Navmesh_ZUpRequest_NorthGoalBehindWall_ReturnsZUpWaypoints()
        {
            const float Elev = 2f; // every polygon floats 2 m above the ground (Z), so Z can only come from the mesh
            static Vector3[] Quad(float cx, float cy, float h)
                => new[]
                {
                    new Vector3(cx - h, cy - h, Elev), new Vector3(cx + h, cy - h, Elev),
                    new Vector3(cx + h, cy + h, Elev), new Vector3(cx - h, cy + h, Elev),
                };

            var map = new NavTestMapBuilder()
                .Layer(NavLayerMask.Vehicle, b => b
                    .Polygon(0, Quad(0f,   5f, 5f))   // start
                    .Polygon(1, Quad(0f,  15f, 5f))   // the wall: blocked below
                    .Polygon(2, Quad(0f,  25f, 5f))
                    .Polygon(3, Quad(10f, 15f, 5f))   // bypass east of the wall
                    .Polygon(4, Quad(0f,  50f, 5f))   // goal polygon, far north
                    .Adjacent(0, 1).Adjacent(1, 2).Adjacent(0, 3).Adjacent(3, 2).Adjacent(2, 4))
                .Build();
            map.Layers[0].Polygons[1].IsBlocked = true;

            var navmesh = new FakeNavmeshProvider(map);
            var solver  = new PathfindingSolverSystem(default(RoadNetworkBlob), _pool, navmesh: navmesh);

            var start = new Vector3(0f, 5f, 0f);
            var goal  = new Vector3(0f, 50f, 0f);
            long requestId = ((long)21 << 32) | _world.GlobalVersion;
            _world.Bus.Publish(new PathfindingRequestEvent
            {
                RequestId    = requestId,
                Start        = start,
                End          = goal,
                BackendForce = NavigationBackend.Navmesh,
                NavLayerMask = (int)NavLayerMask.Vehicle,
            });

            RunSolverPipeline(_world, solver);

            var events = ((ISimulationView)_world).ReadEvents<PathfindingResultEvent>();
            Assert.Equal(1, events.Length);
            Assert.True(events[0].IsReachable, "A path around the blocked polygon must exist");
            Assert.Equal(NavigationBackend.Navmesh, events[0].PrimaryBackend);

            Assert.True(_pool.TryGetTrajectory(events[0].RouteHandle, out var traj));
            var wps = traj.Waypoints;
            Assert.True(wps.Length >= 3, $"Expected start + detour + goal, got {wps.Length} waypoints");

            // First / last waypoints are the request endpoints, untouched (north stays in Y).
            var last = wps[wps.Length - 1].Position;
            Assert.Equal(start, wps[0].Position);
            Assert.Equal(50f, last.Y, precision: 3);
            Assert.Equal(0f,  last.Z, precision: 3);
            Assert.Equal(0f,  last.X, precision: 3);

            // The route went around the wall (east bypass centroid (10,15)), and the in-between waypoints
            // carry the polygon elevation in Z — never north (that was the CE-3011 mix-up).
            bool viaBypass = false;
            for (int i = 1; i < wps.Length - 1; i++)
            {
                var p = wps[i].Position;
                Assert.Equal(Elev, p.Z, precision: 3);
                viaBypass |= MathF.Abs(p.X - 10f) < 0.01f && MathF.Abs(p.Y - 15f) < 0.01f;
            }
            Assert.True(viaBypass, "The path must detour through the east bypass polygon (centroid (10,15))");

            // Arc length is measured in the XY ground plane: 0->bypass(10,15)->(0,25)->(0,50) is > 45 m.
            Assert.True(events[0].TotalDistanceMeters > 45f, $"Ground-plane arc length too short: {events[0].TotalDistanceMeters}");
        }

        // ── Stub implementations ─────────────────────────────────────────────────

        /// <summary>Stub volumetric provider that records calls and returns a two-waypoint path.</summary>
        private sealed class StubVolumetricProvider : IVolumetricPathProvider
        {
            public bool WasCalled;

            public int PlanPath(Vector3 from, Vector3 to, Span<NavWaypoint> waypoints)
            {
                WasCalled = true;
                if (waypoints.Length < 2) return 0;
                waypoints[0] = new NavWaypoint { Position = from };
                waypoints[1] = new NavWaypoint { Position = to };
                return 2;
            }

            public uint QueryVersion() => 0;
        }

        /// <summary>Stub navmesh provider that records whether PlanPath was called.</summary>
        private sealed class StubNavmeshProvider : INavmeshProvider
        {
            public bool PlanPathWasCalled;

            public bool IsWalkable(Vector3 position, uint layerMask = 0xFFFFFFFF) => true;

            public bool ProjectToNavmesh(Vector3 position, out Vector3 snapped, uint layerMask = 0xFFFFFFFF)
            {
                snapped = position;
                return true;
            }

            public int SampleNavmeshPoints(Vector3 center, float radius, Span<Vector3> results, uint layerMask = 0xFFFFFFFF)
                => 0;

            public bool PathExists(Vector3 from, Vector3 to, uint layerMask = 0xFFFFFFFF) => true;

            public float PathCost(Vector3 from, Vector3 to, uint layerMask = 0xFFFFFFFF) => 0f;

            public uint QueryVersion() => 0;

            public int PlanPath(Vector3 from, Vector3 to, Span<NavWaypoint> waypoints, uint layerMask = 0xFFFFFFFF)
            {
                PlanPathWasCalled = true;
                if (waypoints.Length < 2) return 0;
                waypoints[0] = new NavWaypoint { Position = from };
                waypoints[1] = new NavWaypoint { Position = to };
                return 2;
            }
        }
    }
}
