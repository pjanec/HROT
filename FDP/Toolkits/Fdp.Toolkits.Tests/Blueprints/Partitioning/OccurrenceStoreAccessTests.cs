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
        // ═══ B3 / O3a — THE TIER TABLE ══════════════════════════════════════════════════════════
        //  📄 DESIGN_Occurrence_Scoped_Storage.md §17. ⭐ These live in the SEAM'S OWN suite
        //  (R-142 ④) rather than a new class: the table IS the ladder the seam used to spell.
        //  ⛔⛔ B3-① is a NO-BEHAVIOUR-CHANGE collapse, so every rail below pins a property the
        //  hand-rolled ladders already had — not a new one.

        /// <summary>
        /// ⭐⭐ <b>The ladder is ORDERED, ascending, and <c>Descending</c> is its exact reverse.</b>
        /// ⛔ Both orders are load-bearing: <c>Select</c> walks ascending so the first fit is the
        /// cheapest, and every probe walks descending. A table built in the wrong order would still
        /// compile and would silently seat everything on the largest tier.
        /// </summary>
        [Fact]
        public void B3_R1_TheLadderIsOrderedAscending_AndDescendingIsItsReverse()
        {
            var asc = BlueprintTierTable.Ascending;
            Assert.NotEmpty(asc);

            for (int i = 1; i < asc.Count; i++)
            {
                Assert.True(asc[i].TotalSize > asc[i - 1].TotalSize,
                    $"Ascending must be strictly increasing by TotalSize: {asc[i - 1]} then {asc[i]}.");
                Assert.True(asc[i].PayloadSize > asc[i - 1].PayloadSize,
                    $"A larger tier must offer more payload: {asc[i - 1]} then {asc[i]}.");
            }

            var desc = BlueprintTierTable.Descending;
            Assert.Equal(asc.Count, desc.Count);
            for (int i = 0; i < asc.Count; i++)
                Assert.Same(asc[i], desc[desc.Count - 1 - i]);
        }

        /// <summary>
        /// ⭐⭐⭐ <b>Every tier's declared numbers match the struct they describe.</b>
        /// 🔴 This is the rail that makes the table safe to read INSTEAD of the constants: if a tier
        /// struct's <c>MaxSlots</c> is re-picked (<c>W1</c>) and the table is not updated with it,
        /// the allocator would initialise a header with one capacity while every fit decision used
        /// another — a silent over-commit, not a crash.
        /// </summary>
        [Fact]
        public void B3_R2_EveryTierSpecAgreesWithItsComponentStruct()
        {
            foreach (var spec in BlueprintTierTable.Ascending)
            {
                Assert.Equal(spec.TotalSize, System.Runtime.InteropServices.Marshal.SizeOf(spec.ComponentType));

                int headerAndTable =
                    sizeof(BlueprintBlackboardHeader) + spec.MaxSlots * BlueprintBlackboardPartitions.SlotEntrySize;
                Assert.Equal(spec.TotalSize - headerAndTable, spec.PayloadSize);

                // ⛔ D1'/A3: the Kind nibble array in the header's 8-byte Reserved holds exactly
                //   MaxKindSlots kinds. A tier with more slots has slots whose kind cannot be
                //   recorded, and the tick walker filters ON the kind ⇒ they would be skipped.
                Assert.InRange(spec.MaxSlots, 1, BlueprintBlackboardPartitions.MaxKindSlots);
            }
        }

        /// <summary>
        /// ⭐⭐ <b><c>Select</c> gates on BOTH axes, and falls through to the largest tier.</b>
        /// 📌 §5a's <c>F3</c>: a byte-only fit silently seats a behaviour whose slot table is full.
        /// ⚠ The fall-through is the PRESERVED behaviour of <c>SelectTierForPayload</c>, which ended
        /// with an unconditional <c>return …16384</c>.
        /// </summary>
        [Fact]
        public void B3_R3_SelectGatesOnBothAxes_AndFallsThroughToLargest()
        {
            var smallest = BlueprintTierTable.Ascending[0];
            var largest  = BlueprintTierTable.Largest;

            // Fits both axes on the smallest tier.
            Assert.Same(smallest, BlueprintTierTable.Select(1, 1));

            // ⛔ Bytes fit the smallest tier but the SLOT count does not ⇒ must NOT pick it.
            var bySlots = BlueprintTierTable.Select(1, smallest.MaxSlots + 1);
            Assert.NotSame(smallest, bySlots);
            Assert.True(bySlots.MaxSlots >= smallest.MaxSlots + 1);

            // ⛔ Slots fit but the BYTES do not ⇒ must NOT pick it either.
            var byBytes = BlueprintTierTable.Select(smallest.PayloadSize + 1, 1);
            Assert.NotSame(smallest, byBytes);

            // Nothing fits ⇒ the largest, not an exception and not null.
            Assert.Same(largest, BlueprintTierTable.Select(int.MaxValue, int.MaxValue));
            Assert.Same(largest, BlueprintTierTable.Select(1, int.MaxValue));
        }

        /// <summary>
        /// ⭐⭐ <b><c>Of</c> answers exactly what the hand-rolled probe answered</b> — the tier the
        /// entity carries, or <see langword="null"/>. ⚠ And with TWO tiers present (the transient
        /// mid-promotion state), the LARGER wins, because the probe was largest-first.
        /// </summary>
        [Fact]
        public void B3_R4_OfProbesLargestFirst_AndNullWhenAbsent()
        {
            var world  = CreateWorld();
            var entity = world.CreateEntity();

            Assert.Null(BlueprintTierTable.Of(world, entity));

            var small = BlueprintTierTable.Ascending[0];
            var big   = BlueprintTierTable.Largest;

            small.Add(world, entity);
            Assert.Same(small, BlueprintTierTable.Of(world, entity));

            // Mid-promotion: both components present. The larger is authoritative.
            big.Add(world, entity);
            Assert.Same(big, BlueprintTierTable.Of(world, entity));
            Assert.Equal(big.TotalSize, OccurrenceStoreAccess.GetStoreSize(world, entity));

            small.Remove(world, entity);
            Assert.Same(big, BlueprintTierTable.Of(world, entity));

            world.Dispose();
        }

        /// <summary>
        /// ⭐⭐ <b><c>AdjacentPairs</c> is the promotion ladder</b> — <c>N-1</c> consecutive
        /// (smaller → larger) pairs. <c>BlueprintMaintenanceSystem</c> builds one query per pair, so
        /// a gap here is a promotion that never happens and an entity stuck carrying two tiers.
        /// </summary>
        [Fact]
        public void B3_R5_AdjacentPairsCoverTheWholeLadderWithNoGaps()
        {
            var asc   = BlueprintTierTable.Ascending;
            var pairs = BlueprintTierTable.AdjacentPairs;

            Assert.Equal(asc.Count - 1, pairs.Count);
            for (int i = 0; i < pairs.Count; i++)
            {
                Assert.Same(asc[i],     pairs[i].From);
                Assert.Same(asc[i + 1], pairs[i].To);
            }
        }

        /// <summary>
        /// ⭐⭐⭐ <b>A tier resolved through the table points at the SAME bytes the seam returns.</b>
        /// 🔴 The collapse rests on <c>Unsafe.As&lt;TTier, byte&gt;</c> addressing the same memory the
        /// old <c>fixed (byte* m = tier.Memory)</c> did. ⛔ If that were not true every read and
        /// write would land somewhere plausible and wrong, and no other rail would say so.
        /// </summary>
        [Fact]
        public void B3_R6_SpecMemoryIsTheSameBytesTheSeamReturns()
        {
            var world  = CreateWorld();
            var entity = world.CreateEntity();

            var spec = BlueprintTierTable.Ascending[0];
            spec.Add(world, entity);
            BlueprintBlackboardPartitions.Initialize(
                spec.Memory(world, entity), spec.TotalSize, (byte)spec.MaxSlots);

            byte* viaSeam = OccurrenceStoreAccess.TryGetStore(world, entity, out int totalSize);
            byte* viaSpec = spec.Memory(world, entity);

            Assert.True(viaSeam == viaSpec);
            Assert.Equal(spec.TotalSize, totalSize);

            // And the header the allocator wrote reports the spec's own capacity back.
            ref var header = ref System.Runtime.CompilerServices.Unsafe
                .AsRef<BlueprintBlackboardHeader>(viaSeam);
            Assert.Equal(BlueprintBlackboardHeader.MagicValue, header.MagicAndVersion);
            Assert.Equal(spec.MaxSlots, header.MaxSlots);
            Assert.Equal(spec.PayloadSize, header.PayloadSize);

            world.Dispose();
        }

        /// <summary>
        /// ⚠ <b><c>RegisterAll</c> covers the whole ladder</b> — the <c>CE-161</c> property.
        /// A tier in the table but missing from registration is the exact crash that made
        /// <c>BlueprintBlackboardTiers</c> exist: <i>"Component … is not registered"</i> on the first
        /// live scenario load, on whichever host took the path nobody updated.
        /// </summary>
        [Fact]
        public void B3_R7_RegisterAllCoversEveryTierInTheLadder()
        {
            var world = new EntityRepository();
            BlueprintBlackboardTiers.RegisterAll(world);

            foreach (var spec in BlueprintTierTable.Ascending)
            {
                var entity = world.CreateEntity();
                spec.Add(world, entity);              // throws if the type was never registered
                Assert.True(spec.Has(world, entity));
            }

            world.Dispose();
        }
    }
}
