using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Core.Collections;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.BTreeNodes;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests
{
    /// <summary>
    /// Unit tests for <see cref="PathfindingBatchHelper"/> / <see cref="Action_PlanRoute"/> (MOD1-P6T5).
    /// Verifies that <see cref="Action_PlanRoute"/> correctly publishes events and reads from
    /// the <see cref="PathfindingBatchData"/> ring buffer.
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

            // Register events required by PathfindingBatchHelper.RequestPath.
            world.RegisterEvent<PathfindingRequestEvent>();
            world.RegisterEvent<PathfindingResultEvent>();

            var batch = new PathfindingBatchData
            {
                Results = new NativeArray<PathResult>(PathfindingBatchData.DefaultCapacity, Allocator.Persistent),
            };
            world.SetSingleton(batch);
            return world;
        }

        private static void DisposeWorld(EntityRepository world)
        {
            if (!world.HasSingleton<PathfindingBatchData>()) return;
            ref var b = ref world.GetSingleton<PathfindingBatchData>();
            if (b.Results.IsCreated) b.Results.Dispose();
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
        /// Executing the node must return a non-zero <c>requestId</c>.
        /// </summary>
        [Fact]
        public void PathfindingActionNode_RequestPath_ReturnsNonZeroRequestId()
        {
            var node       = CreateNode();
            long requestId = node.Execute(_world);

            Assert.True(requestId != 0, "returned requestId must be non-zero");
        }

        /// <summary>
        /// When the ring buffer slot contains a result with <c>IsReachable == true</c>,
        /// <see cref="Action_PlanRoute.QueryResult"/> must return that result.
        /// </summary>
        [Fact]
        public void PathfindingActionNode_GetPathResult_ReturnsRouteHandleWhenResolved()
        {
            var node       = CreateNode();
            long requestId = node.Execute(_world);

            // Manually write a resolved result directly to the ring buffer slot as the
            // materialization system would after the solver resolves the route.
            ref var batch = ref _world.GetSingleton<PathfindingBatchData>();
            int slot = (int)((uint)requestId % (uint)PathfindingBatchData.DefaultCapacity);
            batch.Results[slot] = new PathResult
            {
                RequestId           = requestId,
                IsReachable         = true,
                TotalDistanceMeters = 200f,
                RouteHandle         = 42,
            };

            var result = node.QueryResult(_world, requestId);
            Assert.True(result.IsReachable);
            Assert.Equal(42, result.RouteHandle);
        }

        /// <summary>
        /// When no result exists for the given request ID, <see cref="Action_PlanRoute.QueryResult"/>
        /// must return <c>default</c> (<c>IsReachable == false</c>).
        /// </summary>
        [Fact]
        public void PathfindingActionNode_GetPathResult_ReturnsDefaultWhilePending()
        {
            var node       = CreateNode();
            long requestId = node.Execute(_world);

            // Do NOT populate Results -- simulate a still-pending request.
            var result = node.QueryResult(_world, requestId);
            Assert.False(result.IsReachable);
        }
    }
}
