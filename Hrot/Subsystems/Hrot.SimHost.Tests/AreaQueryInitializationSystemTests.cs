using Fdp.Core;
using Fdp.Core.Collections;
using Fdp.Toolkit.Spatial.Eqs;
using Hrot.CGF.Systems;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Unit tests for <see cref="AreaQueryInitializationSystem"/> (TASK-HA003).
    /// </summary>
    public class AreaQueryInitializationSystemTests
    {
        // ── SC-HA003-1 ────────────────────────────────────────────────────────────

        /// <summary>
        /// After a tick of <see cref="AreaQueryInitializationSystem"/>, the
        /// <see cref="AreaQueryBatchData.Count"/> is reset to zero regardless of how
        /// many requests were present before the tick.
        /// </summary>
        [Fact]
        public void InitSystem_ResetsBatchCount_OnEachTick()
        {
            // Arrange
            var world = new EntityRepository();
            SimHostComponentRegistry.RegisterAll(world);

            try
            {
                // Submit requests so batch.Count > 0.
                Entity requestingEntity = world.CreateEntity();
                Entity areaEntity       = world.CreateEntity();

                for (int i = 0; i < 3; i++)
                {
                    AreaQueryBatchHelper.RequestAreaQuery(
                        world, requestingEntity, areaEntity, ForceId.Hostile);
                }

                ref var batch = ref world.GetSingleton<AreaQueryBatchData>();
                Assert.Equal(3, batch.Count);

                // Act
                var sys = new AreaQueryInitializationSystem();
                sys.Execute(world, 0.016f);

                // Assert — count must be zero after reset
                Assert.Equal(0, world.GetSingleton<AreaQueryBatchData>().Count);
            }
            finally
            {
                DisposeEqsSingletons(world);
            }
        }

        // ── SC-HA003-2 ────────────────────────────────────────────────────────────

        /// <summary>
        /// After a tick of <see cref="AreaQueryInitializationSystem"/>, all slots in the
        /// <see cref="EqsTargetPool"/> read as zero, confirming that stale packed entity
        /// handles written by a previous solver run cannot leak into the next frame.
        /// This validates the automatic pool-lifetime management that prevents unmanaged
        /// memory leaks under BTree branch preemption (SC-HA003-2).
        /// </summary>
        [Fact]
        public void InitSystem_ZerosAllPoolSlots_OnEachTick()
        {
            // Arrange
            var world = new EntityRepository();
            SimHostComponentRegistry.RegisterAll(world);

            try
            {
                // Manually write sentinel values into pool slots to simulate a previous
                // solver run that left packed entity handles in the pool.
                ref var pool = ref world.GetSingleton<EqsTargetPool>();
                for (int i = 0; i < pool.Targets.Length; i++)
                    pool.Targets[i] = (long)(i + 1); // non-zero sentinel

                pool.NextFreeIndex = pool.Targets.Length; // mark as full

                // Confirm pool is dirty before reset.
                Assert.True(pool.Targets[0] != 0L, "Pool must be dirty before the test.");

                // Act
                var sys = new AreaQueryInitializationSystem();
                sys.Execute(world, 0.016f);

                // Assert — every pool slot must be zero after reset.
                ref var poolAfter = ref world.GetSingleton<EqsTargetPool>();
                Assert.Equal(0, poolAfter.NextFreeIndex);
                for (int i = 0; i < poolAfter.Targets.Length; i++)
                {
                    Assert.True(poolAfter.Targets[i] == 0L,
                        $"Pool slot {i} is {poolAfter.Targets[i]} — expected 0 after reset.");
                }
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
