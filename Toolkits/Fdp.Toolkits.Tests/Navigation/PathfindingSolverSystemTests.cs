using System;
using System.Numerics;
using CarKinem.Road;
using CarKinem.Trajectory;
using Fdp.Core;
using Fdp.Core.Collections;
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
    /// </summary>
    public sealed class PathfindingSolverSystemTests : IDisposable
    {
        private readonly EntityRepository _world;

        public PathfindingSolverSystemTests()
        {
            _world = new EntityRepository();
            var batch = new PathfindingBatchData
            {
                Count    = 0,
                Requests = new NativeArray<PathRequest>(PathfindingBatchData.DefaultCapacity, Allocator.Persistent),
                Results  = new NativeArray<PathResult>(PathfindingBatchData.DefaultCapacity,  Allocator.Persistent),
            };
            _world.SetSingleton(batch);
        }

        public void Dispose()
        {
            if (!_world.HasSingleton<PathfindingBatchData>()) return;
            ref var b = ref _world.GetSingleton<PathfindingBatchData>();
            if (b.Requests.IsCreated) b.Requests.Dispose();
            if (b.Results.IsCreated)  b.Results.Dispose();
        }

        // ── Test 1: route found ───────────────────────────────────────────────────

        [Fact]
        public void PathfindingSolverSystem_WritesRouteHandle()
        {
            // Arrange — two-node road network: node 0 at origin, node 1 at (100,0).
            var builder = new RoadNetworkBuilder();
            builder.AddNode(new Vector2(0f, 0f));
            builder.AddNode(new Vector2(100f, 0f));
            builder.AddSegment(
                new Vector2(0f, 0f),   new Vector2(50f, 0f),
                new Vector2(100f, 0f), new Vector2(50f, 0f),
                startNodeIdx: 0, endNodeIdx: 1);
            var roadNet = builder.Build(cellSize: 20f, gridWidth: 10, gridHeight: 10);

            var pool = new TrajectoryPoolManager();
            var view = (ISimulationView)_world;

            ref var batch = ref _world.GetSingleton<PathfindingBatchData>();
            batch.Requests[0] = new PathRequest
            {
                RequestId      = 1L,
                Start          = Vector3.Zero,
                End            = new Vector3(100f, 0f, 0f),
                MobilityProfile = 0,
            };
            batch.Count = 1;

            var system = new PathfindingSolverSystem(roadNet, pool);

            // Act
            system.Execute(view, 0f);

            // Assert
            ref readonly var result = ref _world.GetSingleton<PathfindingBatchData>();
            var r = result.Results[0];
            Assert.True(r.IsReachable);
            Assert.True(r.RouteHandle >= 0);

            // Cleanup
            roadNet.Dispose();
            pool.Dispose();
        }

        // ── Test 2: empty network → unreachable ──────────────────────────────────

        [Fact]
        public void PathfindingSolverSystem_WritesUnreachable_WhenNoPath()
        {
            // Arrange — empty (default) road network has no nodes.
            var pool = new TrajectoryPoolManager();
            var view = (ISimulationView)_world;

            ref var batch = ref _world.GetSingleton<PathfindingBatchData>();
            batch.Requests[0] = new PathRequest
            {
                RequestId      = 2L,
                Start          = Vector3.Zero,
                End            = new Vector3(500f, 500f, 0f),
                MobilityProfile = 0,
            };
            batch.Count = 1;

            var system = new PathfindingSolverSystem(default(RoadNetworkBlob), pool);

            // Act
            system.Execute(view, 0f);

            // Assert
            ref readonly var result = ref _world.GetSingleton<PathfindingBatchData>();
            Assert.False(result.Results[0].IsReachable);

            pool.Dispose();
        }

        // ── Test 3: NavigationSolverModule registers PathfindingSolverSystem ─────

        [Fact]
        public void NavigationSolverModule_RegistersPathfindingSystem()
        {
            // Arrange
            var module       = new NavigationSolverModule(default(RoadNetworkBlob));
            var mockRegistry = new Mock<ISystemRegistry>();

            // Act
            module.RegisterSystems(mockRegistry.Object);

            // Assert
            mockRegistry.Verify(r => r.RegisterSystem(It.IsAny<PathfindingSolverSystem>()), Times.Once);
        }

        // ── Test 4: SourceNodeId propagation ─────────────────────────────────────

        /// <summary>
        /// Verifies that <see cref="PathfindingSolverSystem"/> copies
        /// <see cref="PathRequest.SourceNodeId"/> verbatim into the corresponding
        /// <see cref="PathResult.SourceNodeId"/>.
        ///
        /// <para>
        /// The Distributed Pathfinding Pipeline relies on this field to route
        /// path responses back to the originating Brain node.
        /// </para>
        /// </summary>
        [Fact]
        public void PathfindingSolverSystem_PropagatesSourceNodeId_ToResult()
        {
            // Arrange — two-node network so the solver can find a route.
            var builder = new RoadNetworkBuilder();
            builder.AddNode(new Vector2(0f, 0f));
            builder.AddNode(new Vector2(100f, 0f));
            builder.AddSegment(
                new Vector2(0f, 0f),   new Vector2(50f, 0f),
                new Vector2(100f, 0f), new Vector2(50f, 0f),
                startNodeIdx: 0, endNodeIdx: 1);
            var roadNet = builder.Build(cellSize: 20f, gridWidth: 10, gridHeight: 10);

            var pool = new TrajectoryPoolManager();
            var view = (ISimulationView)_world;

            ref var batch = ref _world.GetSingleton<PathfindingBatchData>();
            batch.Requests[0] = new PathRequest
            {
                RequestId       = 10L,
                Start           = Vector3.Zero,
                End             = new Vector3(100f, 0f, 0f),
                MobilityProfile = 0,
                SourceNodeId    = 5,
            };
            batch.Count = 1;

            var system = new PathfindingSolverSystem(roadNet, pool);

            // Act
            system.Execute(view, 0f);

            // Assert
            ref readonly var result = ref _world.GetSingleton<PathfindingBatchData>();
            Assert.Equal(5, result.Results[0].SourceNodeId);

            roadNet.Dispose();
            pool.Dispose();
        }

        /// <summary>
        /// SourceNodeId is propagated even when the solver cannot find a path
        /// (IsReachable == false), so the egress translator can still route the
        /// "unreachable" result back to the originating Brain node.
        /// </summary>
        [Fact]
        public void PathfindingSolverSystem_PropagatesSourceNodeId_WhenUnreachable()
        {
            var pool = new TrajectoryPoolManager();
            var view = (ISimulationView)_world;

            ref var batch = ref _world.GetSingleton<PathfindingBatchData>();
            batch.Requests[0] = new PathRequest
            {
                RequestId       = 11L,
                Start           = Vector3.Zero,
                End             = new Vector3(500f, 500f, 0f),
                MobilityProfile = 0,
                SourceNodeId    = 99,
            };
            batch.Count = 1;

            var system = new PathfindingSolverSystem(default(RoadNetworkBlob), pool);

            // Act
            system.Execute(view, 0f);

            // Assert
            ref readonly var result = ref _world.GetSingleton<PathfindingBatchData>();
            Assert.False(result.Results[0].IsReachable);
            Assert.Equal(99, result.Results[0].SourceNodeId);

            pool.Dispose();
        }
    }
}
