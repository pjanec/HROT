using System;
using System.Numerics;
using CarKinem.Road;
using CarKinem.Trajectory;
using Fdp.Core;
using Fdp.Core.Collections;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Systems;
using Fdp.ModuleHost.Abstractions;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests
{
    /// <summary>
    /// T4 -- Result materialization: <see cref="PathfindingResultMaterializationSystem"/>
    /// writes <see cref="NavigationCorridorMuscle"/>, updates <see cref="NavigationStatus"/>,
    /// and fires <see cref="MoveStartedEvent"/> according to the originating action.
    /// Also verifies the capacity change PathfindingBatchData.DefaultCapacity == 256.
    /// </summary>
    public sealed class PathfindingResultMaterializationT4Tests : IDisposable
    {
        private readonly EntityRepository _world;
        private readonly TrajectoryPoolManager _pool;

        public PathfindingResultMaterializationT4Tests()
        {
            _world = new EntityRepository();

            // Events consumed by the solver and materialization systems.
            _world.RegisterEvent<PathfindingRequestEvent>();
            _world.RegisterEvent<PathfindingResultEvent>();
            _world.RegisterEvent<MoveStartedEvent>();

            // Components needed by the materialization system.
            _world.RegisterComponent<SimTransform>();
            _world.RegisterComponent<LocomotionChannel>();
            _world.RegisterComponent<NavigationStatus>();
            _world.RegisterComponent<NavigationCorridorMuscle>();

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
        /// Runs solver + materialization pipeline.
        /// Returns after materialization; MoveStartedEvent is in the write buffer
        /// (caller must call Bus.SwapBuffers() to read it).
        /// </summary>
        private void RunFullPipeline(PathfindingSolverSystem solver, float dt = 0f)
        {
            var view = (ISimulationView)_world;
            _world.Bus.SwapBuffers();          // requests → readable
            solver.Execute(view, dt);
            var ecb = (EntityCommandBuffer)view.GetCommandBuffer();
            ecb.Playback(_world);
            _world.Bus.SwapBuffers();          // result events → readable
            new PathfindingResultMaterializationSystem().Execute(view, dt);
            // MoveStartedEvent is now in the write buffer; caller swaps to read it.
        }

        // ── Test 1: BatchCapacity == 256 ─────────────────────────────────────────

        [Fact]
        public void BatchCapacity_Is256()
        {
            Assert.Equal(256, PathfindingBatchData.DefaultCapacity);
        }

        // ── Test 2: MoveTo reachable → corridor + Following status + MoveStartedEvent

        [Fact]
        public void MoveTo_Reachable_PopulatesCorridorAndFiresMoveStartedEvent()
        {
            // Arrange
            var roadNet = BuildTwoNodeNetwork();
            var solver  = new PathfindingSolverSystem(roadNet, _pool);

            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new NavigationStatus());
            _world.AddComponent(entity, new LocomotionChannel
            {
                ActiveAction = NavigationConstants.ActionIdMoveTo,
            });

            long requestId = ((long)entity.Index << 32) | _world.GlobalVersion;
            _world.Bus.Publish(new PathfindingRequestEvent
            {
                RequestId = requestId,
                Start     = new Vector3(0f, 0f, 0f),
                End       = new Vector3(100f, 0f, 0f),
            });

            // Act
            RunFullPipeline(solver);

            // Assert corridor
            ref readonly var corridor = ref _world.GetComponent<NavigationCorridorMuscle>(entity);
            Assert.NotEqual(0, corridor.RouteHandle);
            Assert.True(corridor.TotalDistance > 0f,
                "TotalDistance must be positive for a reachable path.");

            // Assert status
            ref readonly var status = ref _world.GetComponent<NavigationStatus>(entity);
            Assert.Equal(NavigationPhase.Following, status.Phase);
            Assert.Equal(NavigationResult.InProgress, status.Result);

            // Assert MoveStartedEvent
            _world.Bus.SwapBuffers();
            var moveEvents = ((ISimulationView)_world).ReadEvents<MoveStartedEvent>();
            Assert.Equal(1, moveEvents.Length);
            Assert.Equal(requestId, moveEvents[0].RequestId);
            Assert.Equal(corridor.RouteHandle, moveEvents[0].RouteHandle);

            roadNet.Dispose();
        }

        // ── Test 3: MoveTo unreachable → FailedUnreachable, no MoveStartedEvent ──

        [Fact]
        public void MoveTo_Unreachable_SetsFailedUnreachable_NoMoveStartedEvent()
        {
            // Arrange -- empty road network guarantees unreachable.
            var solver = new PathfindingSolverSystem(default(RoadNetworkBlob), _pool);

            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new NavigationStatus());
            _world.AddComponent(entity, new LocomotionChannel
            {
                ActiveAction = NavigationConstants.ActionIdMoveTo,
            });

            long requestId = ((long)entity.Index << 32) | _world.GlobalVersion;
            _world.Bus.Publish(new PathfindingRequestEvent
            {
                RequestId = requestId,
                Start     = new Vector3(0f, 0f, 0f),
                End       = new Vector3(500f, 500f, 0f),
            });

            // Act
            RunFullPipeline(solver);

            // Assert status
            ref readonly var status = ref _world.GetComponent<NavigationStatus>(entity);
            Assert.Equal(NavigationResult.FailedUnreachable, status.Result);

            // Assert no corridor (not set -- component stays default if upsert was not called)
            Assert.False(_world.HasComponent<NavigationCorridorMuscle>(entity),
                "Corridor must NOT be written for an unreachable MoveTo.");

            // Assert no MoveStartedEvent
            _world.Bus.SwapBuffers();
            var moveEvents = ((ISimulationView)_world).ReadEvents<MoveStartedEvent>();
            Assert.Equal(0, moveEvents.Length);
        }

        // ── Test 4: PlanRoute reachable → PathFound status, no corridor, no MoveStartedEvent

        [Fact]
        public void PlanRoute_Reachable_SetsPathFound_NoCorridor_NoMoveStartedEvent()
        {
            // Arrange
            var roadNet = BuildTwoNodeNetwork();
            var solver  = new PathfindingSolverSystem(roadNet, _pool);

            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new NavigationStatus());
            _world.AddComponent(entity, new LocomotionChannel
            {
                ActiveAction = NavigationConstants.ActionIdPlanRoute,
            });

            long requestId = ((long)entity.Index << 32) | _world.GlobalVersion;
            _world.Bus.Publish(new PathfindingRequestEvent
            {
                RequestId = requestId,
                Start     = new Vector3(0f, 0f, 0f),
                End       = new Vector3(100f, 0f, 0f),
            });

            // Act
            RunFullPipeline(solver);

            // Assert status
            ref readonly var status = ref _world.GetComponent<NavigationStatus>(entity);
            Assert.Equal(NavigationPhase.Idle, status.Phase);
            Assert.Equal(NavigationResult.PathFound, status.Result);
            Assert.NotEqual(0, status.RouteHandle);

            // Assert no corridor
            Assert.False(_world.HasComponent<NavigationCorridorMuscle>(entity),
                "Corridor must NOT be written for PlanRoute.");

            // Assert no MoveStartedEvent
            _world.Bus.SwapBuffers();
            var moveEvents = ((ISimulationView)_world).ReadEvents<MoveStartedEvent>();
            Assert.Equal(0, moveEvents.Length);

            roadNet.Dispose();
        }

        // ── Test 5: PlanRoute unreachable → NoPath status ────────────────────────

        [Fact]
        public void PlanRoute_Unreachable_SetsNoPath()
        {
            // Arrange -- empty road network guarantees unreachable.
            var solver = new PathfindingSolverSystem(default(RoadNetworkBlob), _pool);

            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new NavigationStatus());
            _world.AddComponent(entity, new LocomotionChannel
            {
                ActiveAction = NavigationConstants.ActionIdPlanRoute,
            });

            long requestId = ((long)entity.Index << 32) | _world.GlobalVersion;
            _world.Bus.Publish(new PathfindingRequestEvent
            {
                RequestId = requestId,
                Start     = new Vector3(0f, 0f, 0f),
                End       = new Vector3(500f, 500f, 0f),
            });

            // Act
            RunFullPipeline(solver);

            // Assert status
            ref readonly var status = ref _world.GetComponent<NavigationStatus>(entity);
            Assert.Equal(NavigationResult.NoPath, status.Result);
        }
    }
}
