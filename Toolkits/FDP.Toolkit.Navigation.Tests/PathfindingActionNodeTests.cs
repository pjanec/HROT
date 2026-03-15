using System;
using System.Numerics;
using Fdp.Kernel;
using Fdp.Kernel.Collections;
using FDP.Toolkit.Navigation.BTreeNodes;
using Xunit;

namespace FDP.Toolkit.Navigation.Tests
{
    /// <summary>
    /// Unit tests for <see cref="PathfindingActionNode"/> (MOD1-P6T5).
    /// Verifies that <see cref="Action_PlanRoute"/> correctly writes to and reads from
    /// <see cref="PathfindingBatchData"/> without going through <c>IAIContext</c>.
    /// </summary>
    public sealed class PathfindingActionNodeTests : IDisposable
    {
        private readonly EntityRepository _world;

        public PathfindingActionNodeTests()
        {
            _world = CreateWorld();
        }

        public void Dispose()
        {
            DisposeWorld(_world);
        }

        // ── World factory ─────────────────────────────────────────────────────────

        private static EntityRepository CreateWorld()
        {
            var world = new EntityRepository();
            var batch = new PathfindingBatchData
            {
                Count    = 0,
                Requests = new NativeArray<PathRequest>(PathfindingBatchData.DefaultCapacity, Allocator.Persistent),
                Results  = new NativeArray<PathResult>(PathfindingBatchData.DefaultCapacity,  Allocator.Persistent),
            };
            world.SetSingleton(batch);
            return world;
        }

        private static void DisposeWorld(EntityRepository world)
        {
            if (!world.HasSingleton<PathfindingBatchData>()) return;
            ref var b = ref world.GetSingleton<PathfindingBatchData>();
            if (b.Requests.IsCreated) b.Requests.Dispose();
            if (b.Results.IsCreated)  b.Results.Dispose();
        }

        // ── Helpers ────────────────────────────────────────────────────────────────

        private static Action_PlanRoute CreateNode(int entityIndex = 1)
            => new Action_PlanRoute
            {
                EntityIndex = entityIndex,
                From        = Vector3.Zero,
                To          = new Vector3(100f, 100f, 0f),
            };

        // ── Tests ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Executing the node must append one request to <see cref="PathfindingBatchData"/>,
        /// setting <c>Count == 1</c>.
        /// </summary>
        [Fact]
        public void PathfindingActionNode_RequestPath_WritesToBatch()
        {
            var node      = CreateNode();
            int requestId = node.Execute(_world);

            ref readonly var batch = ref _world.GetSingleton<PathfindingBatchData>();
            Assert.Equal(1, batch.Count);
            Assert.True(requestId >= 0, "returned requestId must be non-negative");
        }

        /// <summary>
        /// When the solver has populated a result with a matching <c>RequestId</c>,
        /// <see cref="Action_PlanRoute.QueryResult"/> must return <c>RouteHandle == 42</c>.
        /// </summary>
        [Fact]
        public void PathfindingActionNode_GetPathResult_ReturnsRouteHandleWhenResolved()
        {
            var node      = CreateNode();
            int requestId = node.Execute(_world);

            // Manually write a resolved result as the solver would.
            ref var batch = ref _world.GetSingleton<PathfindingBatchData>();
            batch.Results[0] = new PathResult
            {
                RequestId          = batch.Requests[0].RequestId,
                IsReachable        = true,
                TotalDistanceMeters = 200f,
                RouteHandle        = 42,
            };

            var result = node.QueryResult(_world, requestId);
            Assert.True(result.IsReachable);
            Assert.Equal(42, result.RouteHandle);
        }

        /// <summary>
        /// When no result exists for the given request ID, <see cref="Action_PlanRoute.QueryResult"/>
        /// must return <c>default</c> (<c>IsReachable == false</c>, <c>RouteHandle == 0</c>).
        /// </summary>
        [Fact]
        public void PathfindingActionNode_GetPathResult_ReturnsDefaultWhilePending()
        {
            var node      = CreateNode();
            int requestId = node.Execute(_world);

            // Do NOT populate Results — simulate a still-pending request.
            var result = node.QueryResult(_world, requestId);
            Assert.False(result.IsReachable);
        }
    }
}
