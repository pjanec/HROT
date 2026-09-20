using System;
using Fdp.Core;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Partitioning;
using Fdp.Toolkit.Blueprints.Shared;
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
            // ⭐ B4: register from the LADDER, not a hand-list. A tier added to the table but
            //   missing here would make every rail below silently test a smaller ladder.
            BlueprintTierTable.RegisterAll(world);
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

                // ⛔⛔ B3② — THE SLOTS AXIS MUST NOT GO BACKWARDS, and the re-pick is exactly what
                //   could break it. A larger tier offering FEWER slots makes promotion a capacity
                //   REDUCTION, and CopyToLargerTier would be handed a dstMaxSlots smaller than the
                //   slot count it is copying. 📌 With the small tier re-picked 4 → 12, leaving the
                //   medium tier at 8 would have done precisely that — and every OTHER assertion in
                //   this rail would still have passed, because payload kept increasing.
                //   ⚠ NON-decreasing, not strictly increasing: MaxKindSlots caps the ladder at 16,
                //   so the top tiers legitimately share a value.
                Assert.True(asc[i].MaxSlots >= asc[i - 1].MaxSlots,
                    $"A larger tier must not offer FEWER slots: {asc[i - 1]} then {asc[i]}.");
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
        /// <summary>
        /// ⭐⭐⭐ <b>The tier structs agree with <c>BlueprintTierLadder</c> — the file LINKED into
        /// <c>Hrot.Blueprints.Compiler</c>.</b>
        ///
        /// <para>🔴 This closes §17.1 <c>N1</c>. <c>Stage2_Validate</c> spelled the payload budgets as
        /// the literals <c>928 / 3936 / 16096</c>, because its project targets <c>netstandard2.0</c>
        /// and can reference <c>Fdp.Toolkits</c> only under <c>net8.0</c> ⇒ a <c>MaxSlots</c> re-pick
        /// moved the runtime's capacity and left compile-time validation behind, silently.</para>
        ///
        /// <para>⚠ <b>What this rail does and does not cover.</b> Drift between the two COPIES is
        /// impossible by construction — it is one file on disk, linked, and dropping the link stops
        /// <c>Stage2_Validate</c> compiling. ⛔ What is still possible, and what this pins, is a tier
        /// struct quietly re-declaring a literal of its own instead of reading the ladder.</para>
        /// </summary>
        [Fact]
        public void B3_R8_TheTierStructsAgreeWithTheLinkedLadderFile()
        {
            Assert.Equal(BlueprintTierLadder.Tier256TotalSize,     BlueprintBlackboard256.TotalSize);
            Assert.Equal(BlueprintTierLadder.Tier256MaxSlots,      BlueprintBlackboard256.MaxSlots);
            Assert.Equal(BlueprintTierLadder.Tier256PayloadSize,   BlueprintBlackboard256.PayloadSize);

            Assert.Equal(BlueprintTierLadder.Tier1024TotalSize,    BlueprintBlackboard1024.TotalSize);
            Assert.Equal(BlueprintTierLadder.Tier1024MaxSlots,     BlueprintBlackboard1024.MaxSlots);
            Assert.Equal(BlueprintTierLadder.Tier1024PayloadSize,  BlueprintBlackboard1024.PayloadSize);

            Assert.Equal(BlueprintTierLadder.Tier4096TotalSize,    BlueprintBlackboard4096.TotalSize);
            Assert.Equal(BlueprintTierLadder.Tier4096MaxSlots,     BlueprintBlackboard4096.MaxSlots);
            Assert.Equal(BlueprintTierLadder.Tier4096PayloadSize,  BlueprintBlackboard4096.PayloadSize);

            Assert.Equal(BlueprintTierLadder.Tier16384TotalSize,   BlueprintBlackboard16384.TotalSize);
            Assert.Equal(BlueprintTierLadder.Tier16384MaxSlots,    BlueprintBlackboard16384.MaxSlots);
            Assert.Equal(BlueprintTierLadder.Tier16384PayloadSize, BlueprintBlackboard16384.PayloadSize);
        }

        /// <summary>
        /// ⚠ <b>The ladder file MIRRORS three allocator constants by hand</b>, because a linked
        /// netstandard2.0 file cannot reference the allocator. ⛔ Nothing but this rail says they
        /// still match — and a wrong <c>SlotEntrySize</c> there mis-sizes every payload budget the
        /// compiler validates against, on the side of the wall that cannot be checked at all.
        /// </summary>
        [Fact]
        public void B3_R9_TheLadderFileMirrorsTheAllocatorConstants()
        {
            Assert.Equal(BlueprintBlackboardPartitions.SlotEntrySize, BlueprintTierLadder.SlotEntrySize);
            Assert.Equal(BlueprintBlackboardPartitions.MaxKindSlots,  BlueprintTierLadder.MaxKindSlots);
            Assert.Equal(System.Runtime.CompilerServices.Unsafe.SizeOf<BlueprintBlackboardHeader>(),
                         BlueprintTierLadder.HeaderSize);
        }
        /// <summary>
        /// ⭐⭐⭐ <b><c>B4</c> / <c>O3b</c> — THE 256 TIER IS ACTUALLY SELECTED.</b>
        ///
        /// <para>⛔⛔ Anti-vacuity, and it is the whole point of the task. Adding a tier to the table
        /// proves nothing: <c>B3_R1</c>/<c>R2</c>/<c>R5</c>/<c>R8</c> would all stay green for a tier
        /// that <see cref="BlueprintTierTable.Select"/> never returns. 📐 The measured simple case —
        /// one root occurrence plus a stateful slot or two — must land HERE, or the tier costs a
        /// component id and buys nothing.</para>
        ///
        /// <para>📐 The sizing that chose <c>MaxSlots 3</c> (design §17, "B4's PRE-MEASUREMENT"):
        /// 25 of 30 generated behaviours fit at 83 %, the peak of a 1–6 sweep.</para>
        /// </summary>
        [Fact]
        public void B4_R1_TheSmallestTierIsSelectedForTheSimpleCase()
        {
            var smallest = BlueprintTierTable.Ascending[0];
            Assert.Equal(BlueprintBlackboard256.TotalSize, smallest.TotalSize);

            // The measured worst case that still fits: 3 occurrences totalling the whole payload.
            Assert.Same(smallest, BlueprintTierTable.Select(smallest.PayloadSize, smallest.MaxSlots));

            // A bare root occurrence — BehaviorTreeState (64 B) + entry, no params, one slot.
            Assert.Same(smallest, BlueprintTierTable.Select(64 + 16, 1));

            // ⛔ And it must NOT swallow what it cannot hold: one slot too many, or one byte too many.
            Assert.NotSame(smallest, BlueprintTierTable.Select(1, smallest.MaxSlots + 1));
            Assert.NotSame(smallest, BlueprintTierTable.Select(smallest.PayloadSize + 1, 1));
        }

        /// <summary>
        /// ⚠ <b>The enum's numeric order is deliberately NOT the ladder's size order.</b>
        /// <c>BlackboardTier.B256</c> is <b>3</b>, the last member, because the ordinal is ABI and a
        /// new tier must be APPENDED (§17.1 <c>N2</c> measured three enums spelling this ladder, one
        /// of them <c>: byte</c>). ⛔ If someone "tidies" the enum into size order, every persisted
        /// ordinal shifts — this rail is what says so.
        /// </summary>
        [Fact]
        public void B4_R2_TheTierEnumIsAppendOnly_NotInSizeOrder()
        {
            Assert.Equal(0, (int)BlackboardTier.B1024);
            Assert.Equal(1, (int)BlackboardTier.B4096);
            Assert.Equal(2, (int)BlackboardTier.B16384);
            Assert.Equal(3, (int)BlackboardTier.B256);

            // The SIZE order is the table's, and it disagrees with the enum's — by design.
            Assert.Equal(BlackboardTier.B256, BlueprintTierTable.Ascending[0].Tier);
        }

        // ── B4's real find: the at-most-one-tier invariant, under ATTACH ────────────────────────

        private static int RegisterInstanceBlueprint(
            BlueprintRegistry registry, string name, int stateSize)
        {
            int blueprintId = name.GetHashCode();
            registry.RegisterInstance(blueprintId, new BlueprintDefinition
            {
                Name          = name,
                Kind          = BlueprintDispatchKind.Instance,
                StructureHash = (ulong)blueprintId,
                StateSize     = stateSize,
                AssetId       = Guid.NewGuid(),
            });
            return blueprintId;
        }

        private static int TierComponentCount(EntityRepository world, Entity entity)
        {
            int n = 0;
            var ascending = BlueprintTierTable.Ascending;
            for (int i = 0; i < ascending.Count; i++)
                if (ascending[i].Has(world, entity)) n++;
            return n;
        }

        /// <summary>
        /// 🔴🔴 <b><c>B4_R3</c> — attaching a SMALL instance to an entity that already carries a
        /// LARGER tier must not add a second blackboard component.</b>
        /// 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §17.7.
        ///
        /// <para>⛔⛔ This is the production defect <c>O3b</c> exposed. <c>AttachToEntity</c> picked its
        /// tier with <c>ChooseTier(def.StateSize)</c>, which sizes the ONE instance and knows nothing
        /// about the entity — so once a tier SMALLER than 1024 existed, every small instance attached
        /// to a 1024-carrying entity chose 256 and <c>EnsureTierComponent</c> bolted it on beside the
        /// 1024. ⇒ the entity carried TWO stores, breaking the invariant every consumer reads through
        /// <see cref="OccurrenceStoreAccess"/>, and since the probe order is largest-first the slots
        /// just written became INVISIBLE.</para>
        ///
        /// <para>📌 How it presented: <c>BlueprintStateTranslatorTests.Extract_TwoBlueprintsAttached</c>
        /// returned <b>0</b> assignments for two successfully-attached blueprints. ⚠ Both attaches
        /// reported <c>Attached</c> — the corruption was silent at the call site.</para>
        /// </summary>
        [Fact]
        public void B4_R3_AttachIntoAnExistingLargerTierDoesNotAddASecondStore()
        {
            using var world = CreateWorld();
            var registry    = new BlueprintRegistry();
            var entity      = world.CreateEntity();

            // The entity already carries 1024 — the shape a behaviour manifest leaves behind.
            var big = BlueprintTierTable.ByTier(BlackboardTier.B1024);
            big.Add(world, entity);

            // A small instance: its state fits the 256 tier, so ChooseTier alone would pick 256.
            int smallId = RegisterInstanceBlueprint(registry, "Small", stateSize: 16);
            Assert.True(BlueprintTierTable.Ascending[0].PayloadSize >= 16,
                "premise: the smallest tier really does hold this state");

            var result = BlueprintInstanceService.AttachToEntity(world, registry, smallId, entity);
            Assert.Equal(BlueprintAttachStatus.Attached, result.Status);

            // ⭐ THE RAIL: exactly one tier, and it is the one the entity already had.
            Assert.Equal(1, TierComponentCount(world, entity));
            Assert.Equal(BlackboardTier.B1024, result.Tier);
            Assert.False(BlueprintTierTable.ByTier(BlackboardTier.B256).Has(world, entity));

            // ⭐ And the slot is readable through the seam — the property the defect destroyed.
            byte* store = OccurrenceStoreAccess.TryGetStoreReadOnly(world, entity, out int size);
            Assert.True(store != null);
            Assert.Equal(BlueprintBlackboard1024.TotalSize, size);
            Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(store, smallId, out _));
        }

        /// <summary>
        /// ⭐ <b><c>B4_R4</c> — when the instance genuinely does NOT fit the tier the entity carries,
        /// the store is PROMOTED, not doubled.</b> The other half of <c>B4_R3</c>, and the direction
        /// that was reachable even before <c>O3b</c>: a large instance on a small-tier entity.
        ///
        /// <para>⛔ The old code added the larger component and left the smaller one orphaned beside
        /// it — with its existing slots stranded in a store nothing would read again.
        /// ⭐ <see cref="BlueprintTierTable.Promote"/> carries them across, and with them the header's
        /// <c>Reserved</c> <c>Kind</c> nibbles (<c>H1</c>).</para>
        /// </summary>
        [Fact]
        public void B4_R4_AttachThatOutgrowsTheCurrentTierPromotesItAndCarriesTheSlots()
        {
            using var world = CreateWorld();
            var registry    = new BlueprintRegistry();
            var entity      = world.CreateEntity();

            var small = BlueprintTierTable.Ascending[0];
            var large = BlueprintTierTable.ByTier(BlackboardTier.B1024);

            // Seat a first, small instance — it lands on the smallest tier.
            int firstId  = RegisterInstanceBlueprint(registry, "First", stateSize: 16);
            Assert.Equal(
                BlueprintAttachStatus.Attached,
                BlueprintInstanceService.AttachToEntity(world, registry, firstId, entity).Status);
            Assert.True(small.Has(world, entity));

            // Now one that cannot fit the smallest tier's payload at all.
            int bigId = RegisterInstanceBlueprint(registry, "Big", stateSize: small.PayloadSize + 1);
            var result = BlueprintInstanceService.AttachToEntity(world, registry, bigId, entity);
            Assert.Equal(BlueprintAttachStatus.Attached, result.Status);

            // ⭐ THE RAIL: promoted, not doubled — and the FIRST slot came with it.
            Assert.Equal(1, TierComponentCount(world, entity));
            Assert.False(small.Has(world, entity));
            Assert.True(large.Has(world, entity));

            byte* store = OccurrenceStoreAccess.TryGetStoreReadOnly(world, entity, out _);
            Assert.True(store != null);
            Assert_BothSlotsPresent(store, firstId, bigId);
        }

        private static void Assert_BothSlotsPresent(byte* store, int firstId, int bigId)
        {
            Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(store, firstId, out _),
                "the pre-existing slot must survive the promotion");
            Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(store, bigId, out _),
                "the slot that forced the promotion must be present");
        }

        /// <summary>
        /// ⛔⛔ <b><c>B4_R5</c> — the enum's ORDINAL order and the ladder's SIZE order really do
        /// disagree</b>, so <see cref="BlueprintTierTable.IsLargerThan"/> cannot be "simplified"
        /// back into <c>&gt;</c>. 📄 design §17.7.
        ///
        /// <para>📌 This is not a style point. <c>B256 = 3</c> is the enum's LAST member (the
        /// ordinal is ABI, §17.1 <c>N2</c>) and the ladder's SMALLEST tier ⇒ <c>B256 &gt; B1024</c>
        /// is <c>true</c> by ordinal and <c>false</c> by size. Two comparisons in
        /// <c>EntityBlueprintsEditModel</c> read a DOWNGRADE as <i>"upgrade needed"</i> and put it
        /// in the commit plan.</para>
        /// </summary>
        [Fact]
        public void B4_R5_TierComparisonGoesBySIZE_NotByTheEnumOrdinal()
        {
            var smallest = BlueprintTierTable.Ascending[0];
            var next     = BlueprintTierTable.Ascending[1];

            // By SIZE: the first entry is smaller than the second. That is what callers mean.
            Assert.False(BlueprintTierTable.IsLargerThan(smallest.Tier, next.Tier));
            Assert.True(BlueprintTierTable.IsLargerThan(next.Tier, smallest.Tier));

            // ⛔ And the ordinal DISAGREES for at least one pair — the whole reason this exists.
            bool anyDisagreement = false;
            var ladder = BlueprintTierTable.Ascending;
            for (int i = 0; i < ladder.Count; i++)
            for (int j = 0; j < ladder.Count; j++)
            {
                bool byOrdinal = ladder[i].Tier > ladder[j].Tier;
                bool bySize    = BlueprintTierTable.IsLargerThan(ladder[i].Tier, ladder[j].Tier);
                if (byOrdinal != bySize) anyDisagreement = true;
            }

            Assert.True(anyDisagreement,
                "if the two orders ever agree everywhere, a tier was INSERTED rather than appended " +
                "- which breaks the ABI (B4_R2). This rail is what says the helper is load-bearing.");
        }
    }
}
