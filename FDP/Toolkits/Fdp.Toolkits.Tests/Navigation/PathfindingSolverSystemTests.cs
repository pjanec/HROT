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
    }
}
