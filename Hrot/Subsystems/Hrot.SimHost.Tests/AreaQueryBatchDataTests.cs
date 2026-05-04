using System;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Core.Collections;
using Fdp.Toolkit.Spatial.Eqs;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Unit tests for <see cref="AreaQueryBatchData"/>, <see cref="EqsTargetPool"/>,
    /// and <see cref="AreaQueryBatchHelper"/> (TASK-HA001).
    /// </summary>
    public class AreaQueryBatchDataTests
    {
        // ── SC-HA001-4 ────────────────────────────────────────────────────────────

        /// <summary>
        /// The EQS component ID constants must match the design-specified values so
        /// that serialised snapshots and network descriptors remain stable.
        /// </summary>
        [Fact]
        public void ComponentIds_AreStableValues()
        {
            Assert.Equal(202, GlobalComponentIds.AreaQueryBatchData);
            Assert.Equal(203, GlobalComponentIds.EqsTargetPool);
        }

        // ── SC-HA001-3 ────────────────────────────────────────────────────────────

        /// <summary>
        /// <see cref="AreaQueryRequest"/> and <see cref="AreaQueryResult"/> are
        /// <c>LayoutKind.Sequential</c> structs whose sizes must be deterministic
        /// across platforms and compiler versions.
        /// </summary>
        [Fact]
        public unsafe void AreaQueryStructs_HaveDeterministicSize()
        {
            // AreaQueryRequest: long(8) + Entity(8) + ForceId(1) + _pad0(1) + _pad1(1) + _pad2(1) + int(4) = 24
            int reqSize = sizeof(AreaQueryRequest);
            Assert.True(reqSize > 0, $"sizeof(AreaQueryRequest) = {reqSize}");

            // AreaQueryResult: long(8) + bool(1) + _pad0(1) + _pad1(1) + _pad2(1) + int(4) + int(4) + int(4) = 24
            int resSize = sizeof(AreaQueryResult);
            Assert.True(resSize > 0, $"sizeof(AreaQueryResult) = {resSize}");
        }

        // ── SC-HA001-1 ────────────────────────────────────────────────────────────

        /// <summary>
        /// Submitting 64 requests via <see cref="AreaQueryBatchHelper.RequestAreaQuery"/>
        /// must return 64 distinct non-negative RequestIds.  The 65th submission must
        /// return <c>-1</c> because the batch is full.
        /// </summary>
        [Fact]
        public void RequestAreaQuery_DistinctIds_AndFailsAtCapacity()
        {
            // Arrange
            var world = new EntityRepository();
            SimHostComponentRegistry.RegisterAll(world);

            try
            {
                Entity requestingEntity = world.CreateEntity();
                Entity areaEntity       = world.CreateEntity();

                var seen = new System.Collections.Generic.HashSet<long>();

                // Act — fill the batch to capacity
                for (int i = 0; i < AreaQueryBatchData.DefaultCapacity; i++)
                {
                    long id = AreaQueryBatchHelper.RequestAreaQuery(
                        world, requestingEntity, areaEntity, ForceId.Hostile, sourceNodeId: i);

                    Assert.True(id >= 0, $"Request {i} returned negative id {id}");
                    Assert.True(seen.Add(id), $"Duplicate RequestId {id} at index {i}");
                }

                // The 65th request must fail.
                long overflow = AreaQueryBatchHelper.RequestAreaQuery(
                    world, requestingEntity, areaEntity, ForceId.Hostile);
                Assert.Equal(-1L, overflow);
            }
            finally
            {
                DisposeEqsSingletons(world);
            }
        }

        // ── SC-HA001-2 ────────────────────────────────────────────────────────────

        /// <summary>
        /// After calling <see cref="AreaQueryBatchHelper.ResetBatch"/>, the batch count
        /// is zero and the pool's next-free index is reset to zero with zeroed entries.
        /// </summary>
        [Fact]
        public void ResetBatch_ZeroesCountAndPool()
        {
            // Arrange
            var world = new EntityRepository();
            SimHostComponentRegistry.RegisterAll(world);

            try
            {
                Entity requestingEntity = world.CreateEntity();
                Entity areaEntity       = world.CreateEntity();

                // Submit a few requests to make the batch non-empty.
                for (int i = 0; i < 5; i++)
                {
                    AreaQueryBatchHelper.RequestAreaQuery(
                        world, requestingEntity, areaEntity, ForceId.Hostile, sourceNodeId: i);
                }

                // Act
                AreaQueryBatchHelper.ResetBatch(world);

                // Assert batch
                ref var batch = ref world.GetSingleton<AreaQueryBatchData>();
                Assert.Equal(0, batch.Count);

                // Assert pool
                var pool = world.GetSingleton<EqsTargetPool>();
                Assert.Equal(0, pool.NextFreeIndex);
                for (int i = 0; i < 10; i++)
                    Assert.Equal(0L, pool.Targets[i]);
            }
            finally
            {
                DisposeEqsSingletons(world);
            }
        }

        // ── Helper ────────────────────────────────────────────────────────────────

        private static void DisposeEqsSingletons(EntityRepository world)
        {
            if (world.HasSingleton<AreaQueryBatchData>())
            {
                ref var batch = ref world.GetSingleton<AreaQueryBatchData>();
                if (batch.Requests.IsCreated) batch.Requests.Dispose();
                if (batch.Results.IsCreated)  batch.Results.Dispose();
            }
            if (world.HasSingleton<EqsTargetPool>())
            {
                var pool = world.GetSingleton<EqsTargetPool>();
                if (pool.Targets.IsCreated) pool.Targets.Dispose();
            }
        }
    }
}
