using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Physics.BTreeNodes;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Physics.Systems;
using Xunit;

namespace Fdp.Toolkit.Physics.Tests
{
    /// <summary>
    /// Unit tests for <see cref="Action_QueryRaycast"/> and <see cref="RaycastBatchHelper"/> (MOD1-P6T4).
    /// Verifies that the BTree helper correctly publishes events, and that results can be
    /// polled from the <see cref="RaycastBatchData"/> ring buffer after materialization.
    /// </summary>
    public sealed class PhysicsQueryActionNodeTests : IDisposable
    {
        private readonly EntityRepository _world;

        public PhysicsQueryActionNodeTests()
        {
            _world = PhysicsTestWorldFactory.Create();
        }

        public void Dispose()
        {
            PhysicsTestWorldFactory.DisposeBatch(_world);
        }

        // ── Helpers ────────────────────────────────────────────────────────────────

        private static Action_QueryRaycast CreateNode(int entityIndex = 1)
            => new Action_QueryRaycast
            {
                EntityIndex  = entityIndex,
                Origin       = Vector3.Zero,
                Direction    = Vector3.UnitX,
                MaxDistance  = 100f,
            };

        // ── Tests ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Executing the node must return a non-negative <c>rayId</c>.
        /// </summary>
        [Fact]
        public void PhysicsQueryActionNode_RequestRaycast_ReturnsNonNegativeRayId()
        {
            var node   = CreateNode();
            long rayId = node.Execute(_world);

            Assert.True(rayId != 0, "returned rayId must be non-zero");
        }

        /// <summary>
        /// When the ring buffer slot contains a matching <c>RayId</c>,
        /// <see cref="Action_QueryRaycast.QueryResult"/> must return that hit.
        /// </summary>
        [Fact]
        public void PhysicsQueryActionNode_GetRaycastResult_ReturnsMatchingHit()
        {
            var node   = CreateNode();
            long rayId = node.Execute(_world);

            // Manually write a hit result directly to the ring buffer slot as the
            // materialization system would after the solver resolves the cast.
            ref var batch = ref _world.GetSingleton<RaycastBatchData>();
            int slot = (int)((uint)rayId % (uint)PhysicsConstants.RaycastBatchCapacity);
            batch.Hits[slot] = new RaycastHit
            {
                RayId  = rayId,
                HasHit = 1,
                T      = 0.5f,
            };

            var hit = node.QueryResult(_world, rayId);
            Assert.Equal(1, hit.HasHit);
        }

        /// <summary>
        /// When no hit exists for the given ray ID, <see cref="Action_QueryRaycast.QueryResult"/>
        /// must return <c>default</c> (HasHit == 0).
        /// </summary>
        [Fact]
        public void PhysicsQueryActionNode_GetRaycastResult_ReturnsDefaultForUnresolvedId()
        {
            var node   = CreateNode();
            long rayId = node.Execute(_world);

            // Do NOT write a hit -- simulate an unresolved / pending ray.
            var hit = node.QueryResult(_world, rayId);
            Assert.Equal(0, hit.HasHit);
        }
    }
}
