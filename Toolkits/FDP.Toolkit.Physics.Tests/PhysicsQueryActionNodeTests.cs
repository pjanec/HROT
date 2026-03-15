using System;
using System.Numerics;
using Fdp.Kernel;
using Fdp.Kernel.Collections;
using FDP.Toolkit.Physics.BTreeNodes;
using FDP.Toolkit.Physics.Components;
using Xunit;

namespace FDP.Toolkit.Physics.Tests
{
    /// <summary>
    /// Unit tests for <see cref="PhysicsQueryActionNode"/> (MOD1-P6T4).
    /// Verifies that <see cref="Action_QueryRaycast"/> correctly writes to and reads from
    /// <see cref="RaycastBatchData"/> without going through <c>IAIContext</c>.
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

        private static Action_QueryRaycast CreateNode(int entityIndex = 1, ushort gen = 1)
            => new Action_QueryRaycast
            {
                EntityIndex      = entityIndex,
                EntityGeneration = gen,
                Origin           = Vector3.Zero,
                Direction        = Vector3.UnitX,
                MaxDistance      = 100f,
            };

        // ── Tests ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Executing the node must append one request to <see cref="RaycastBatchData"/>,
        /// setting <c>Count == 1</c> and a non-negative (non-sentinel) <c>RayId</c>.
        /// </summary>
        [Fact]
        public void PhysicsQueryActionNode_RequestRaycast_WritesToBatch()
        {
            var node  = CreateNode();
            int rayId = node.Execute(_world);

            ref readonly var batch = ref _world.GetSingleton<RaycastBatchData>();
            Assert.Equal(1, batch.Count);
            Assert.True(rayId >= 0, "returned rayId must be non-negative");
            Assert.True(batch.Requests[0].RayId != -1, "RayId in batch must be set");
        }

        /// <summary>
        /// When the solver has populated a hit with a matching <c>RayId</c>,
        /// <see cref="Action_QueryRaycast.QueryResult"/> must return that hit.
        /// </summary>
        [Fact]
        public void PhysicsQueryActionNode_GetRaycastResult_ReturnsMatchingHit()
        {
            var node  = CreateNode();
            int rayId = node.Execute(_world);

            // Manually write a hit result as the solver would.
            ref var batch = ref _world.GetSingleton<RaycastBatchData>();
            batch.Hits[0] = new RaycastHit
            {
                RayId  = batch.Requests[0].RayId,
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
            int rayId  = node.Execute(_world);

            // Do NOT write a hit — simulate an unresolved / pending ray.
            var hit = node.QueryResult(_world, rayId);
            Assert.Equal(0, hit.HasHit);
        }
    }
}
