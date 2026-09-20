using System;
using Fdp.Core;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Partitioning;
using Xunit;

namespace Fdp.Toolkits.Tests.Blueprints.Partitioning
{
    /// <summary>
    /// A2 (<c>PLAN_Occurrence_Storage_Build</c>) — rails for the one seam that replaces the
    /// <c>16384 → 4096 → 1024</c> ladder hand-rolled at 20 production sites.
    ///
    /// <para>⭐ <b>A2 is explicitly a NO-BEHAVIOUR-CHANGE task</b>, so these rails pin the ladder's
    /// existing semantics rather than new ones: the probe order, "at most one tier so the first match
    /// wins", and "no store and no slot are the same answer to the caller".</para>
    /// </summary>
    public unsafe class OccurrenceStoreAccessTests
    {
        private static EntityRepository CreateWorld()
        {
            var world = new EntityRepository();
            world.RegisterComponent<BlueprintBlackboard1024>();
            world.RegisterComponent<BlueprintBlackboard4096>();
            world.RegisterComponent<BlueprintBlackboard16384>();
            return world;
        }

        // ── the absent case ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// ⛔ No store is a NORMAL answer, not an error — every site this replaces treated it that
        /// way, and <c>BlueprintTickSystem</c> relies on it while walking entities that carry none.
        /// </summary>
        [Fact]
        public void A2_R1_NoTier_YieldsNullAndZero_WithoutThrowing()
        {
            var world = CreateWorld();
            var entity = world.CreateEntity();

            byte* mem = OccurrenceStoreAccess.TryGetStore(world, entity, out int totalSize);

            Assert.True(mem == null);
            Assert.Equal(0, totalSize);
            Assert.False(OccurrenceStoreAccess.HasStore(world, entity));
            Assert.Equal(0, OccurrenceStoreAccess.GetStoreSize(world, entity));

            world.Dispose();
        }

        // ── each tier resolves to its own size ──────────────────────────────────────────────────

        [Fact]
        public void A2_R2_Tier1024_ResolvesToItsOwnTotalSize()
        {
            var world = CreateWorld();
            var entity = world.CreateEntity();
            world.AddComponent(entity, new BlueprintBlackboard1024());

            byte* mem = OccurrenceStoreAccess.TryGetStore(world, entity, out int totalSize);

            Assert.True(mem != null);
            Assert.Equal(BlueprintBlackboard1024.TotalSize, totalSize);
            world.Dispose();
        }

        [Fact]
        public void A2_R2b_Tier4096_ResolvesToItsOwnTotalSize()
        {
            var world = CreateWorld();
            var entity = world.CreateEntity();
            world.AddComponent(entity, new BlueprintBlackboard4096());

            byte* mem = OccurrenceStoreAccess.TryGetStore(world, entity, out int totalSize);

            Assert.True(mem != null);
            Assert.Equal(BlueprintBlackboard4096.TotalSize, totalSize);
            world.Dispose();
        }

        [Fact]
        public void A2_R2c_Tier16384_ResolvesToItsOwnTotalSize()
        {
            var world = CreateWorld();
            var entity = world.CreateEntity();
            world.AddComponent(entity, new BlueprintBlackboard16384());

            byte* mem = OccurrenceStoreAccess.TryGetStore(world, entity, out int totalSize);

            Assert.True(mem != null);
            Assert.Equal(BlueprintBlackboard16384.TotalSize, totalSize);
            world.Dispose();
        }

        /// <summary>
        /// ⚠ ANTI-VACUITY for R2. If <c>totalSize</c> were hard-coded, or every tier collapsed to one
        /// constant, the three cases above would still pass. The sizes must be three DIFFERENT values.
        /// </summary>
        [Fact]
        public void A2_R2d_TheThreeTiers_ReportThreeDifferentSizes()
        {
            Assert.NotEqual(BlueprintBlackboard1024.TotalSize, BlueprintBlackboard4096.TotalSize);
            Assert.NotEqual(BlueprintBlackboard4096.TotalSize, BlueprintBlackboard16384.TotalSize);
            Assert.NotEqual(BlueprintBlackboard1024.TotalSize, BlueprintBlackboard16384.TotalSize);
        }

        // ── the two ladders must not drift apart ────────────────────────────────────────────────

        /// <summary>
        /// ⭐⭐ <c>GetStoreSize</c> is the size-only half of the same ladder
        /// (<c>BehaviorIngressSystem.GetCurrentTierSize</c> was a verbatim copy of it). If the two
        /// ever disagree, a promotion would size the destination from one and copy from the other —
        /// exactly the class of silent corruption this programme exists to remove.
        /// </summary>
        [Theory]
        [InlineData(1024)]
        [InlineData(4096)]
        [InlineData(16384)]
        public void A2_R3_GetStoreSize_AgreesWithTryGetStore(int tier)
        {
            var world = CreateWorld();
            var entity = world.CreateEntity();
            switch (tier)
            {
                case 1024:  world.AddComponent(entity, new BlueprintBlackboard1024());  break;
                case 4096:  world.AddComponent(entity, new BlueprintBlackboard4096());  break;
                default:    world.AddComponent(entity, new BlueprintBlackboard16384()); break;
            }

            OccurrenceStoreAccess.TryGetStore(world, entity, out int fromPointerForm);

            Assert.Equal(fromPointerForm, OccurrenceStoreAccess.GetStoreSize(world, entity));
            Assert.True(OccurrenceStoreAccess.HasStore(world, entity));

            world.Dispose();
        }

        // ── TryResolveOccurrence ────────────────────────────────────────────────────────────────

        /// <summary>
        /// ⛔ "No store" and "store but no such slot" are deliberately the SAME answer — every caller
        /// this seam replaces treated them alike, and A2 may not change behaviour.
        /// </summary>
        [Fact]
        public void A2_R4_ResolveOccurrence_IsFalse_ForNoStoreAndForAnUnknownSlotAlike()
        {
            var world = CreateWorld();

            var without = world.CreateEntity();
            Assert.False(OccurrenceStoreAccess.TryResolveOccurrence(world, without, 12345, out byte* p1));
            Assert.True(p1 == null);

            var with = world.CreateEntity();
            world.AddComponent(with, new BlueprintBlackboard1024());
            ref var tier = ref world.GetComponentRW<BlueprintBlackboard1024>(with);
            fixed (byte* mem = tier.Memory)
                BlueprintBlackboardPartitions.Initialize(
                    mem, BlueprintBlackboard1024.TotalSize, BlueprintBlackboard1024.MaxSlots);

            Assert.False(OccurrenceStoreAccess.TryResolveOccurrence(world, with, 12345, out byte* p2));
            Assert.True(p2 == null);

            world.Dispose();
        }

        /// <summary>
        /// A provisioned slot resolves, and the payload pointer lands INSIDE the store at exactly the
        /// offset the allocator reports — ⛔ the seam must not add or drop a base.
        /// </summary>
        [Fact]
        public void A2_R5_ResolveOccurrence_LandsAtTheAllocatorsOwnOffset()
        {
            const int slotKey = 987654;
            var world = CreateWorld();
            var entity = world.CreateEntity();
            world.AddComponent(entity, new BlueprintBlackboard1024());

            int expectedOffset;
            ref var tier = ref world.GetComponentRW<BlueprintBlackboard1024>(entity);
            fixed (byte* mem = tier.Memory)
            {
                BlueprintBlackboardPartitions.Initialize(
                    mem, BlueprintBlackboard1024.TotalSize, BlueprintBlackboard1024.MaxSlots);
                Assert.True(BlueprintBlackboardPartitions.TryAttach(mem, slotKey, 32, 0xABCDu, out _));
                Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(mem, slotKey, out expectedOffset));
            }

            byte* store = OccurrenceStoreAccess.TryGetStore(world, entity, out int totalSize);
            Assert.True(OccurrenceStoreAccess.TryResolveOccurrence(world, entity, slotKey, out byte* payload));

            Assert.True(payload != null);
            Assert.Equal(expectedOffset, (int)(payload - store));
            Assert.InRange(expectedOffset, 0, totalSize - 1);

            world.Dispose();
        }

        /// <summary>
        /// ⭐ The pointer addresses the SAME bytes the caller would have reached through the old
        /// ladder: a write through the seam is visible through a direct component read. This is what
        /// makes "mechanical, no behaviour change" checkable rather than asserted.
        /// </summary>
        [Fact]
        public void A2_R6_AWriteThroughTheSeam_IsVisibleThroughTheComponent()
        {
            const int slotKey = 55555;
            var world = CreateWorld();
            var entity = world.CreateEntity();
            world.AddComponent(entity, new BlueprintBlackboard1024());

            ref var tier = ref world.GetComponentRW<BlueprintBlackboard1024>(entity);
            fixed (byte* mem = tier.Memory)
            {
                BlueprintBlackboardPartitions.Initialize(
                    mem, BlueprintBlackboard1024.TotalSize, BlueprintBlackboard1024.MaxSlots);
                Assert.True(BlueprintBlackboardPartitions.TryAttach(mem, slotKey, 8, 0x1234u, out _));
            }

            Assert.True(OccurrenceStoreAccess.TryResolveOccurrence(world, entity, slotKey, out byte* payload));
            *payload = 0x5A;

            ref var again = ref world.GetComponentRW<BlueprintBlackboard1024>(entity);
            fixed (byte* mem = again.Memory)
            {
                Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(mem, slotKey, out int off));
                Assert.Equal(0x5A, mem[off]);
            }

            world.Dispose();
        }
    }
}
