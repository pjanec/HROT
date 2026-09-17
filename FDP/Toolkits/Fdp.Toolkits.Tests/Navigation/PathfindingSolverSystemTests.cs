using System;
using System.Numerics;
using CarKinem.Road;
using CarKinem.Trajectory;
using Fdp.Core;
using Fdp.Core.Collections;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.BTreeNodes;
using Fdp.Toolkit.Navigation.Modules;
using Fdp.Toolkit.Navigation.Systems;
using Moq;
using Fdp.ModuleHost.Abstractions;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests
{
    /// <summary>
    /// Unit tests for <see cref="PathfindingSolverSystem"/> and
    /// <see cref="NavigationSolverModule"/> (MOD1-P6T7).
    /// Tests use the full event pipeline: publish PathfindingRequestEvent, swap, solve,
    /// playback, swap, materialize -- then check the ring buffer slot.
    /// </summary>
    public sealed class PathfindingSolverSystemTests : IDisposable
    {
        private readonly EntityRepository _world;

        public PathfindingSolverSystemTests()
        {
            _world = new EntityRepository();

            // Register the events consumed by the solver and materialization system.
            _world.RegisterEvent<PathfindingRequestEvent>();
            _world.RegisterEvent<PathfindingResultEvent>();

            var batch = new PathfindingBatchData
            {
                Results = new NativeArray<PathResult>(PathfindingBatchData.DefaultCapacity, Allocator.Persistent),
            };
            _world.SetSingleton(batch);
        }

        public void Dispose()
        {
            if (!_world.HasSingleton<PathfindingBatchData>()) return;
            ref var b = ref _world.GetSingleton<PathfindingBatchData>();
            if (b.Results.IsCreated) b.Results.Dispose();
        }

        // ── Helpers ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Runs the full event pipeline: swap (requests now readable) -> solve -> playback
        /// -> swap (results now readable) -> materialize.
        /// Returns the ring buffer result for <paramref name="requestId"/>.
        /// </summary>
        private PathResult RunSolverPipeline(PathfindingSolverSystem solver, long requestId, float dt = 0f)
        {
            var view = (ISimulationView)_world;
            _world.Bus.SwapBuffers();
            solver.Execute(view, dt);
            var ecb = (EntityCommandBuffer)view.GetCommandBuffer();
            ecb.Playback(_world);
            _world.Bus.SwapBuffers();
            new PathfindingResultMaterializationSystem().Execute(view, dt);

            int slot = (int)((uint)requestId % (uint)PathfindingBatchData.DefaultCapacity);
            return _world.GetSingleton<PathfindingBatchData>().Results[slot];
        }

        // ── Test 1: route found ───────────────────────────────────────────────────

        [Fact]
        public void PathfindingSolverSystem_WritesRouteHandle()
        {
            // Arrange -- two-node road network: node 0 at origin, node 1 at (100,0).
            var builder = new RoadNetworkBuilder();
            builder.AddNode(new Vector2(0f, 0f));
            builder.AddNode(new Vector2(100f, 0f));
            builder.AddSegment(
                new Vector2(0f, 0f),   new Vector2(50f, 0f),
                new Vector2(100f, 0f), new Vector2(50f, 0f),
                startNodeIdx: 0, endNodeIdx: 1);
            var roadNet = builder.Build(cellSize: 20f, gridWidth: 10, gridHeight: 10);

            var pool      = new TrajectoryPoolManager();
            long requestId = PathfindingBatchHelper.RequestPath(_world, entityIndex: 1, from: Vector3.Zero, to: new Vector3(100f, 0f, 0f));
            var system    = new PathfindingSolverSystem(roadNet, pool);

            // Act
            var r = RunSolverPipeline(system, requestId);

            // Assert
            Assert.True(r.IsReachable);
            Assert.True(r.RouteHandle >= 0);

            // Cleanup
            roadNet.Dispose();
            pool.Dispose();
        }

        // ── Test 2: empty network -> unreachable ──────────────────────────────────

        [Fact]
        public void PathfindingSolverSystem_WritesUnreachable_WhenNoPath()
        {
            // Arrange -- empty (default) road network has no nodes.
            var pool      = new TrajectoryPoolManager();
            long requestId = PathfindingBatchHelper.RequestPath(_world, entityIndex: 2, from: Vector3.Zero, to: new Vector3(500f, 500f, 0f));
            var system    = new PathfindingSolverSystem(default(RoadNetworkBlob), pool);

            // Act
            var r = RunSolverPipeline(system, requestId);

            // Assert
            Assert.False(r.IsReachable);

            pool.Dispose();
        }

        // ── Test 3: NavigationSolverModule registers materialization system ───────

        [Fact]
        public void NavigationSolverModule_RegistersMaterializationSystem()
        {
            // Arrange
            // B3: the pool is required — the module must share the node's pool, never default one.
            using var pool   = new TrajectoryPoolManager();
            var module       = new NavigationSolverModule(default(RoadNetworkBlob), pool);
            var mockRegistry = new Mock<ISystemRegistry>();

            // Act
            module.RegisterSystems(mockRegistry.Object);

            // Assert
            mockRegistry.Verify(r => r.RegisterSystem(It.IsAny<PathfindingResultMaterializationSystem>()), Times.Once);
        }

        // ── Test 4: SourceNodeId propagation ─────────────────────────────────────

        [Fact]
        public void PathfindingSolverSystem_PropagatesSourceNodeId_ToResult()
        {
            // Arrange -- two-node network so the solver can find a route.
            var builder = new RoadNetworkBuilder();
            builder.AddNode(new Vector2(0f, 0f));
            builder.AddNode(new Vector2(100f, 0f));
            builder.AddSegment(
                new Vector2(0f, 0f),   new Vector2(50f, 0f),
                new Vector2(100f, 0f), new Vector2(50f, 0f),
                startNodeIdx: 0, endNodeIdx: 1);
            var roadNet = builder.Build(cellSize: 20f, gridWidth: 10, gridHeight: 10);

            var pool      = new TrajectoryPoolManager();
            long requestId = PathfindingBatchHelper.RequestPath(_world, entityIndex: 3, from: Vector3.Zero, to: new Vector3(100f, 0f, 0f), sourceNodeId: 5);
            var system    = new PathfindingSolverSystem(roadNet, pool);

            // Act
            var r = RunSolverPipeline(system, requestId);

            // Assert
            Assert.Equal(5, r.SourceNodeId);

            roadNet.Dispose();
            pool.Dispose();
        }

        // ── Test 5: SourceNodeId on unreachable path ──────────────────────────────

        [Fact]
        public void PathfindingSolverSystem_PropagatesSourceNodeId_WhenUnreachable()
        {
            var pool      = new TrajectoryPoolManager();
            long requestId = PathfindingBatchHelper.RequestPath(_world, entityIndex: 4, from: Vector3.Zero, to: new Vector3(500f, 500f, 0f), sourceNodeId: 99);
            var system    = new PathfindingSolverSystem(default(RoadNetworkBlob), pool);

            // Act
            var r = RunSolverPipeline(system, requestId);

            // Assert
            Assert.False(r.IsReachable);
            Assert.Equal(99, r.SourceNodeId);

            pool.Dispose();
        }

        // ── Test 6 (A3 / R2): the road graph is re-read from the singleton EVERY tick ─────────────
        //
        // ⭐⭐ THE DEFECT THIS PINS: the solver used to capture the blob in its constructor
        //    (`private readonly RoadNetworkBlob _roadNetwork`). The terrain loader publishes a NEW
        //    ZoneEnvironmentData when terrain or a zone loads, and CarKinematicsSystem already re-reads
        //    that singleton per tick — so a swap reached the DRIVING code and silently did nothing for
        //    PATH PLANNING. Vehicles would follow the new roads while routes were still planned over the
        //    old graph, with no exception and nothing in a log.
        //
        // ⛔ RED-PROOF SHAPE: revert Execute's refresh to use the constructor blob and this fails —
        //    the post-swap request comes back IsReachable=false, exactly as before the swap.
        // 📄 docs/DESIGN_Terrain_Zones_And_Assets.md §5.4 (R2), §4 second red box.

        [Fact]
        public void PathfindingSolverSystem_ObservesARoadNetworkPublishedAfterConstruction()
        {
            var pool = new TrajectoryPoolManager();

            // Constructed with an EMPTY graph — nothing reachable, as Test 2 establishes.
            var system = new PathfindingSolverSystem(default(RoadNetworkBlob), pool);

            long beforeId = PathfindingBatchHelper.RequestPath(
                _world, entityIndex: 7, from: Vector3.Zero, to: new Vector3(100f, 0f, 0f));
            Assert.False(RunSolverPipeline(system, beforeId).IsReachable);

            // ── the swap: a terrain/zone load publishes the road graph AFTER construction ──
            var builder = new RoadNetworkBuilder();
            builder.AddNode(new Vector2(0f, 0f));
            builder.AddNode(new Vector2(100f, 0f));
            builder.AddSegment(
                new Vector2(0f, 0f),   new Vector2(50f, 0f),
                new Vector2(100f, 0f), new Vector2(50f, 0f),
                startNodeIdx: 0, endNodeIdx: 1);
            var roadNet = builder.Build(cellSize: 20f, gridWidth: 10, gridHeight: 10);
            _world.SetSingleton(new ZoneEnvironmentData { RoadNetwork = roadNet });

            // The SAME solver instance must now plan over the new graph.
            long afterId = PathfindingBatchHelper.RequestPath(
                _world, entityIndex: 8, from: Vector3.Zero, to: new Vector3(100f, 0f, 0f));
            var after = RunSolverPipeline(system, afterId);

            Assert.True(after.IsReachable,
                "the solver must re-read ZoneEnvironmentData per tick; a constructor-captured blob "
              + "makes a terrain/zone load invisible to path planning");
            Assert.True(after.RouteHandle >= 0);

            roadNet.Dispose();
            pool.Dispose();
        }

        // Test 6b: with NO singleton published, the constructor blob is still honoured — the fallback
        // must not regress the hosts that supply a graph statically and never publish one.
        [Fact]
        public void PathfindingSolverSystem_FallsBackToTheConstructorBlob_WhenNoSingletonExists()
        {
            var builder = new RoadNetworkBuilder();
            builder.AddNode(new Vector2(0f, 0f));
            builder.AddNode(new Vector2(100f, 0f));
            builder.AddSegment(
                new Vector2(0f, 0f),   new Vector2(50f, 0f),
                new Vector2(100f, 0f), new Vector2(50f, 0f),
                startNodeIdx: 0, endNodeIdx: 1);
            var roadNet = builder.Build(cellSize: 20f, gridWidth: 10, gridHeight: 10);

            Assert.False(_world.HasSingleton<ZoneEnvironmentData>(),
                "Precondition: no ZoneEnvironmentData singleton");

            var pool   = new TrajectoryPoolManager();
            var system = new PathfindingSolverSystem(roadNet, pool);

            long requestId = PathfindingBatchHelper.RequestPath(
                _world, entityIndex: 9, from: Vector3.Zero, to: new Vector3(100f, 0f, 0f));

            Assert.True(RunSolverPipeline(system, requestId).IsReachable);

            roadNet.Dispose();
            pool.Dispose();
        }

        // Test 6c: the HOLDER path — the channel a BACKGROUND module must use.
        //
        // ⚠ NavigationSolverModule is SlowBackground ⇒ DataStrategy.SoD, so its Tick receives a snapshot
        //    and ISimulationView has NO singleton API at all. The singleton read in Test 6 is therefore
        //    unavailable on that path, and a RoadNetworkHolder carries the swap instead. This asserts the
        //    holder is honoured and, crucially, that a LATER publish is picked up by the SAME instance.
        [Fact]
        public void PathfindingSolverSystem_ObservesAGraphPublishedThroughTheHolder()
        {
            var pool   = new TrajectoryPoolManager();
            var holder = new RoadNetworkHolder();   // starts empty

            var system = new PathfindingSolverSystem(
                default(RoadNetworkBlob), pool, roadNetworkHolder: holder);

            long beforeId = PathfindingBatchHelper.RequestPath(
                _world, entityIndex: 10, from: Vector3.Zero, to: new Vector3(100f, 0f, 0f));
            Assert.False(RunSolverPipeline(system, beforeId).IsReachable);

            var builder = new RoadNetworkBuilder();
            builder.AddNode(new Vector2(0f, 0f));
            builder.AddNode(new Vector2(100f, 0f));
            builder.AddSegment(
                new Vector2(0f, 0f),   new Vector2(50f, 0f),
                new Vector2(100f, 0f), new Vector2(50f, 0f),
                startNodeIdx: 0, endNodeIdx: 1);
            var roadNet = builder.Build(cellSize: 20f, gridWidth: 10, gridHeight: 10);

            holder.Publish(roadNet);

            long afterId = PathfindingBatchHelper.RequestPath(
                _world, entityIndex: 11, from: Vector3.Zero, to: new Vector3(100f, 0f, 0f));
            Assert.True(RunSolverPipeline(system, afterId).IsReachable,
                "a graph published through the holder must be visible to the next tick");

            roadNet.Dispose();
            pool.Dispose();
        }
    }
}
