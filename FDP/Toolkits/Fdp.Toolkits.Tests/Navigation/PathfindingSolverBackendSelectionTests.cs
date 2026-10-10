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
        /// ⭐ CE-3128 — with NO navmesh the road graph is the only planner: even an end far off the network is reached along the
        /// road and a straight connector (CE-2059). ⛔ Supersedes OFX-001's "one end near a road ⇒ Hybrid" — Hybrid now means a
        /// navmesh → road → navmesh splice, chosen on cost (R-230).
        /// </summary>
        [Fact]
        public void CE3128_NoNavmesh_FarEnd_StillRoutesOnTheRoadGraph()
        {
            var roadNet = BuildTwoNodeNetwork();
            var solver  = new PathfindingSolverSystem(roadNet, _pool);

            _world.Bus.Publish(new PathfindingRequestEvent
            {
                RequestId    = ((long)11 << 32) | _world.GlobalVersion,
                Start        = new Vector3(0f, 0f, 0f),
                End          = new Vector3(2000f, 2000f, 0f),
                BackendForce = NavigationBackend.Auto,
            });
            RunSolverPipeline(_world, solver);

            var events = ((ISimulationView)_world).ReadEvents<PathfindingResultEvent>();
            Assert.Equal(1, events.Length);
            Assert.True(events[0].IsReachable);
            Assert.Equal(NavigationBackend.NavRoadGraph, events[0].PrimaryBackend);
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

        // ── CE-3128 (R-230): the road network as a chosen layer — the ONE route planner ─────────────────

        private PathfindingResultEvent Solve(PathfindingSolverSystem solver, Vector3 start, Vector3 end, RoadUse use,
            NavigationBackend force = NavigationBackend.Auto)
        {
            _world.Bus.Publish(new PathfindingRequestEvent
            {
                RequestId = ((long)77 << 32) | _world.GlobalVersion, Start = start, End = end, BackendForce = force, RoadUse = use,
            });
            RunSolverPipeline(_world, solver);
            var events = ((ISimulationView)_world).ReadEvents<PathfindingResultEvent>();
            Assert.Equal(1, events.Length);
            return events[0];
        }

        private CustomTrajectory Route(int handle)
        {
            Assert.True(_pool.TryGetTrajectory(handle, out var t));
            return t;
        }

        private static bool Along(CustomTrajectory t, Func<Vector3, bool> where)
        {
            for (int i = 0; i < t.Waypoints.Length; i++) if (where(t.Waypoints[i].Position)) return true;
            return false;
        }

        /// <summary>
        /// ⭐⭐ CE-3128 D3 — on test-town (open ground), from (100,150) to (380,250): direct ≈ 297 m; via Main Street 50 + 280 + 50.
        /// Prefer (×0.5 ⇒ 240) splices through the road; Neutral (380) and Never walk direct. The SAME request, three actors.
        /// </summary>
        [Fact]
        public void CE3128_TheActorChooses_PreferTakesMainStreet_NeutralAndNeverGoDirect()
        {
            using var roads = Fdp.Toolkit.Tests.Squad.TestTownRoads.Build();
            var solver = new PathfindingSolverSystem(roads, _pool, navmesh: new Fdp.Toolkit.Tests.Squad.StraightNavmesh());
            var start = new Vector3(100, 150, 0); var end = new Vector3(380, 250, 0);

            var prefer = Solve(solver, start, end, RoadUse.Prefer);
            Assert.Equal(NavigationBackend.Hybrid, prefer.PrimaryBackend);
            Assert.True(Along(Route(prefer.RouteHandle), p => MathF.Abs(p.Y - 200f) < 0.5f && p.X > 150f && p.X < 350f),
                "the Prefer route runs along Main Street (y 200)");

            var neutral = Solve(solver, start, end, RoadUse.Neutral);
            Assert.Equal(NavigationBackend.Navmesh, neutral.PrimaryBackend);
            Assert.InRange(neutral.TotalDistanceMeters, 296f, 299f);

            var never = Solve(solver, start, end, RoadUse.Never);
            Assert.Equal(NavigationBackend.Navmesh, never.PrimaryBackend);
        }

        /// <summary>⭐ CE-3128 D5 — a segment authored one way (0 → 1) is driven both ways.</summary>
        [Fact]
        public void CE3128_SegmentsAreTwoWay()
        {
            var roadNet = BuildTwoNodeNetwork();
            var solver = new PathfindingSolverSystem(roadNet, _pool);
            var back = Solve(solver, new Vector3(100f, 0f, 0f), new Vector3(0f, 0f, 0f), RoadUse.Unspecified, NavigationBackend.NavRoadGraph);
            Assert.True(back.IsReachable, "the reverse of a one-way-authored segment must route");
            Assert.InRange(back.TotalDistanceMeters, 99f, 101f);
            roadNet.Dispose();
        }

        /// <summary>⭐ CE-3128 D3 — the route joins the road at the nearest point ON it, mid-segment, not at the nearest node.</summary>
        [Fact]
        public void CE3128_JoinsTheRoadMidSegment()
        {
            var roadNet = BuildTwoNodeNetwork();
            var solver = new PathfindingSolverSystem(roadNet, _pool);
            var r = Solve(solver, new Vector3(50f, 10f, 0f), new Vector3(100f, 0f, 0f), RoadUse.Unspecified, NavigationBackend.NavRoadGraph);
            Assert.True(r.IsReachable);
            var t = Route(r.RouteHandle);
            Assert.True(t.Waypoints.Length >= 2);
            var join = t.Waypoints[1].Position;
            Assert.InRange(join.X, 48f, 52f);      // straight down onto the road, not back to node (0,0)
            Assert.InRange(join.Y, -0.5f, 0.5f);
            Assert.InRange(r.TotalDistanceMeters, 59f, 61f);   // 10 down + 50 along
            roadNet.Dispose();
        }

        /// <summary>⭐ CE-3128 D4 — the road leg follows the segment's curve (Hermite samples), not its chord.</summary>
        [Fact]
        public void CE3128_TheRoadLegFollowsTheCurve()
        {
            var b = new RoadNetworkBuilder();
            b.AddNode(new Vector2(0f, 0f)); b.AddNode(new Vector2(100f, 0f));
            // leaves north-east, arrives south-east: a hump north of the chord y = 0
            b.AddSegment(new Vector2(0f, 0f), new Vector2(100f, 100f), new Vector2(100f, 0f), new Vector2(100f, -100f),
                startNodeIdx: 0, endNodeIdx: 1);
            var roadNet = b.Build(cellSize: 20f, gridWidth: 10, gridHeight: 10);
            var solver = new PathfindingSolverSystem(roadNet, _pool);
            var r = Solve(solver, new Vector3(0f, 0f, 0f), new Vector3(100f, 0f, 0f), RoadUse.Unspecified, NavigationBackend.NavRoadGraph);
            Assert.True(r.IsReachable);
            var t = Route(r.RouteHandle);
            Assert.True(t.Waypoints.Length >= 6, $"sampled along the curve, got {t.Waypoints.Length} points");
            Assert.True(Along(t, p => p.Y > 15f), "the route bulges north with the curve");
            Assert.True(r.TotalDistanceMeters > 105f, "longer than the 100 m chord");
            roadNet.Dispose();
        }

        /// <summary>
        /// ⭐⭐ CE-3128 D6 — the first plan and the replan build ONE request from the intent: the road use, the forced backend and the
        /// layer reach the solver on both. ⛔ The replan used to drop BackendForce and the intent's layer mask.
        /// </summary>
        [Fact]
        public void CE3128_TheReplanCarriesTheOrder_BackendLayerAndRoadUse()
        {
            using var repo = new EntityRepository();
            repo.RegisterEvent<PathfindingRequestEvent>();
            repo.RegisterEvent<PathReplannedEvent>();
            repo.RegisterComponent<NavigationStatus>();
            repo.RegisterComponent<NavAgentProfile>();
            repo.RegisterComponent<global::CarKinem.Core.VehicleState>();
            repo.RegisterComponent<global::CarKinem.Core.VehicleParams>();
            var e = repo.CreateEntity();
            repo.AddComponent(e, default(NavigationStatus));
            var intent = new NavigationIntent
            {
                Mode = NavigationMode.PathToPoint, FinalDestination = new Vector3(50, 60, 0), RouteHandle = 9,
                BackendForce = (byte)NavigationBackend.Navmesh, LayerMask = 4u,
            };
            intent.RoadUse = RoadUse.StronglyPrefer;
            var status = repo.GetComponent<NavigationStatus>(e);

            global::CarKinem.Systems.NavigationExecutionSystem.RequestReplan(repo, e, in intent, ref status, new Vector3(1, 2, 0));
            repo.Bus.SwapBuffers();
            var req = Assert.Single(((ISimulationView)repo).ReadEvents<PathfindingRequestEvent>().ToArray());
            Assert.Equal(NavigationBackend.Navmesh, req.BackendForce);
            Assert.Equal(RoadUse.StronglyPrefer, req.RoadUse);
            Assert.Equal(4, req.NavLayerMask);
            Assert.Equal(9, req.RouteHandle);
            Assert.Equal(new Vector3(50, 60, 0), req.End);
        }

        /// <summary>⭐ CE-3128 D1 — an order that does not say: a vehicle prefers the roads; a soldier (Pedestrian) is neutral even
        /// though SimHost infantry carries VehicleState too; the flags round-trip without touching the other bits.</summary>
        [Fact]
        public void CE3128_DefaultRoadUse_ByLocomotionClass_AndTheFlagsBitsRoundTrip()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<global::CarKinem.Core.VehicleState>();
            repo.RegisterComponent<global::CarKinem.Core.VehicleParams>();
            var tank = repo.CreateEntity();
            repo.AddComponent(tank, default(global::CarKinem.Core.VehicleState));
            var soldier = repo.CreateEntity();
            repo.AddComponent(soldier, default(global::CarKinem.Core.VehicleState));
            repo.AddComponent(soldier, new global::CarKinem.Core.VehicleParams { Class = global::CarKinem.Core.VehicleClass.Pedestrian });
            Assert.Equal(RoadUse.Prefer, PathRequests.ResolveRoadUse(repo, tank, RoadUse.Unspecified));
            Assert.Equal(RoadUse.Neutral, PathRequests.ResolveRoadUse(repo, soldier, RoadUse.Unspecified));
            Assert.Equal(RoadUse.Never, PathRequests.ResolveRoadUse(repo, tank, RoadUse.Never));

            var p = new MoveToParams { Flags = (byte)((1 << NavigationConstants.FlagBitAllowReplan) | (1 << NavigationConstants.FlagBitStreamCorridorPreview)) };
            p.RoadUse = RoadUse.StronglyPrefer;
            Assert.Equal(RoadUse.StronglyPrefer, p.RoadUse);
            Assert.Equal(1 << NavigationConstants.FlagBitAllowReplan | 1 << NavigationConstants.FlagBitStreamCorridorPreview,
                p.Flags & ~NavigationConstants.FlagMaskRoadUse);
        }
    }
}
