using System.Runtime.CompilerServices;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Partitioning;

namespace Hrot.Blueprints.Tests.Runtime;

/// <summary>
/// Direct unit tests for BlueprintBlackboardPartitions.
/// Tests work with raw byte buffers (no EntityRepository). Per Runtime DD §5.10.
/// </summary>
public sealed unsafe class PartitionAllocatorTests
{
    // ---- Helpers ------------------------------------------------------------

    /// <summary>
    /// Allocates a zeroed managed buffer and returns a pinned pointer.
    /// Caller must keep the GCHandle alive for the duration of use (via fixed).
    /// Tests use the managed-heap approach with fixed statements.
    /// </summary>
    private static byte[] MakeBuffer(int size)
    {
        var buf = new byte[size];
        return buf;
    }

    /// <summary>Reads the header from the given memory pointer.</summary>
    private static ref BlueprintBlackboardHeader Header(byte* memory)
        => ref Unsafe.AsRef<BlueprintBlackboardHeader>(memory);

    /// <summary>Reads a slot from the slot table.</summary>
    private static ref BlueprintSlotEntry Slot(byte* memory, int index)
        => ref BlueprintBlackboardPartitions.GetSlot(memory, index);

    /// <summary>
    /// Reads the free-block header at the given payload offset.
    /// </summary>
    private static ref BlueprintFreeBlockHeader FreeBlock(byte* memory, int offset)
        => ref Unsafe.AsRef<BlueprintFreeBlockHeader>(memory + offset);

    /// <summary>
    /// Walks the free list and returns sum of all free-block sizes.
    /// </summary>
    private static int SumFreeListSizes(byte* memory)
    {
        ref var header = ref Header(memory);
        int total = 0;
        ushort cursor = header.FreeListHead;
        while (cursor != 0)
        {
            ref var block = ref FreeBlock(memory, cursor);
            total += block.Size;
            cursor = block.NextFreeOffset;
        }
        return total;
    }

    /// <summary>
    /// Returns the number of free-list blocks.
    /// </summary>
    private static int FreeListLength(byte* memory)
    {
        ref var header = ref Header(memory);
        int count = 0;
        ushort cursor = header.FreeListHead;
        while (cursor != 0)
        {
            ref var block = ref FreeBlock(memory, cursor);
            cursor = block.NextFreeOffset;
            count++;
            if (count > 1000) break; // cycle guard
        }
        return count;
    }

    /// <summary>
    /// Verifies all 7 layout invariants from Runtime DD §4.6.
    /// Throws <see cref="Xunit.Sdk.XunitException"/> on violation.
    /// </summary>
    private static void AssertInvariants(byte* memory, int totalSize, int maxSlots)
    {
        ref var header = ref Header(memory);

        // 1. Header magic
        uint actualMagic = *(uint*)memory;
        Assert.Equal(0x42504257u, actualMagic);

        // 2. SlotCount bound
        Assert.True(header.SlotCount <= header.MaxSlots,
            $"SlotCount {header.SlotCount} exceeds MaxSlots {header.MaxSlots}");

        // 3. Slot table contiguous: allocated slots have non-zero id, unallocated have id==0
        byte* slotTable = memory + sizeof(BlueprintBlackboardHeader);
        for (int i = 0; i < maxSlots; i++)
        {
            ref var slot = ref Unsafe.AsRef<BlueprintSlotEntry>(
                slotTable + i * BlueprintBlackboardPartitions.SlotEntrySize);
            if (i < header.SlotCount)
                Assert.NotEqual(0, slot.BlueprintId);
            else
                Assert.Equal(0, slot.BlueprintId);
        }

        // 4. Allocated slots within payload bounds
        for (int i = 0; i < header.SlotCount; i++)
        {
            ref var slot = ref Unsafe.AsRef<BlueprintSlotEntry>(
                slotTable + i * BlueprintBlackboardPartitions.SlotEntrySize);
            Assert.True(slot.PayloadOffset >= header.PayloadStart,
                $"Slot {i} PayloadOffset {slot.PayloadOffset} < PayloadStart {header.PayloadStart}");
            Assert.True(slot.PayloadOffset + slot.PayloadSize <= totalSize,
                $"Slot {i} end {slot.PayloadOffset + slot.PayloadSize} > TotalSize {totalSize}");
        }

        // 5. No overlap between allocated slot ranges
        for (int a = 0; a < header.SlotCount; a++)
        {
            ref var sa = ref Unsafe.AsRef<BlueprintSlotEntry>(
                slotTable + a * BlueprintBlackboardPartitions.SlotEntrySize);
            for (int b = a + 1; b < header.SlotCount; b++)
            {
                ref var sb = ref Unsafe.AsRef<BlueprintSlotEntry>(
                    slotTable + b * BlueprintBlackboardPartitions.SlotEntrySize);
                int aEnd = sa.PayloadOffset + sa.PayloadSize;
                int bEnd = sb.PayloadOffset + sb.PayloadSize;
                bool overlaps = !(aEnd <= sb.PayloadOffset || bEnd <= sa.PayloadOffset);
                Assert.False(overlaps,
                    $"Slots {a}[{sa.PayloadOffset}..{aEnd}) and {b}[{sb.PayloadOffset}..{bEnd}) overlap");
            }
        }

        // 6. Free list is well-formed (no cycles, all offsets within payload bounds)
        {
            int visited = 0;
            ushort cursor = header.FreeListHead;
            ushort prev = 0;
            while (cursor != 0)
            {
                Assert.True(cursor >= header.PayloadStart && cursor < totalSize,
                    $"Free block at offset {cursor} is outside payload bounds");
                ref var block = ref FreeBlock(memory, cursor);
                Assert.True(block.Size >= BlueprintBlackboardPartitions.FreeBlockHeaderSize,
                    $"Free block at {cursor} has size {block.Size} < FreeBlockHeaderSize");
                Assert.True(block.NextFreeOffset == 0 || block.NextFreeOffset > cursor,
                    $"Free list not sorted: block at {cursor} points to {block.NextFreeOffset}");
                prev = cursor;
                cursor = block.NextFreeOffset;
                visited++;
                Assert.True(visited <= maxSlots + 1, "Free list cycle detected");
            }
        }

        // 7. PayloadFree == sum-of-free-block-sizes + (TotalSize - PayloadHighWater)
        {
            int sumFree = SumFreeListSizes(memory);
            int bumpFree = totalSize - header.PayloadHighWater;
            Assert.Equal((int)header.PayloadFree, sumFree + bumpFree);
        }
    }

    // ---- SC1: Initialize_ZeroedMemory_SetsHeader ----------------------------

    [Fact]
    public void Initialize_ZeroedMemory_SetsHeader()
    {
        var buf = MakeBuffer(BlueprintBlackboard1024.TotalSize);
        fixed (byte* memory = buf)
        {
            BlueprintBlackboardPartitions.Initialize(
                memory,
                BlueprintBlackboard1024.TotalSize,
                BlueprintBlackboard1024.MaxSlots);

            ref var header = ref Header(memory);
            Assert.Equal(0x42504257u, header.MagicAndVersion);
            Assert.Equal(0, (int)header.SlotCount);
            Assert.Equal(BlueprintBlackboard1024.MaxSlots, (int)header.MaxSlots);
            Assert.Equal(0, (int)header.FreeListHead);
            Assert.Equal(BlueprintBlackboard1024.PayloadStart, (int)header.PayloadStart);
            Assert.Equal(BlueprintBlackboard1024.PayloadSize, (int)header.PayloadSize);
            Assert.Equal(BlueprintBlackboard1024.PayloadSize, (int)header.PayloadFree);
            Assert.Equal(BlueprintBlackboard1024.PayloadStart, (int)header.PayloadHighWater);
        }
    }

    // ---- SC2: Attach_SingleBlueprint_AllocatesFromBump ----------------------

    [Fact]
    public void Attach_SingleBlueprint_AllocatesFromBump()
    {
        var buf = MakeBuffer(BlueprintBlackboard1024.TotalSize);
        fixed (byte* memory = buf)
        {
            BlueprintBlackboardPartitions.Initialize(
                memory, BlueprintBlackboard1024.TotalSize, BlueprintBlackboard1024.MaxSlots);

            bool ok = BlueprintBlackboardPartitions.TryAttach(
                memory, blueprintId: 1, requestedSize: 8, structureHash: 0xABCDu, out int payloadOffset);

            Assert.True(ok);
            Assert.Equal(BlueprintBlackboard1024.PayloadStart, payloadOffset);

            ref var header = ref Header(memory);
            Assert.Equal(1, (int)header.SlotCount);
            Assert.Equal(BlueprintBlackboard1024.PayloadStart + 8, (int)header.PayloadHighWater);
            Assert.Equal(BlueprintBlackboard1024.PayloadSize - 8, (int)header.PayloadFree);
            Assert.Equal(0, (int)header.FreeListHead); // no free list activity

            ref var slot = ref Slot(memory, 0);
            Assert.Equal(1, slot.BlueprintId);
            Assert.Equal((ushort)payloadOffset, slot.PayloadOffset);
            Assert.Equal((ushort)8, slot.PayloadSize);
            Assert.Equal(0xABCDu & 0xFFFFFFFFu, (ulong)slot.StructureHash);
        }
    }

    // ---- SC3: Attach_MultipleBlueprints_UsesContiguousBump ------------------

    [Fact]
    public void Attach_MultipleBlueprints_UsesContiguousBump()
    {
        var buf = MakeBuffer(BlueprintBlackboard1024.TotalSize);
        fixed (byte* memory = buf)
        {
            BlueprintBlackboardPartitions.Initialize(
                memory, BlueprintBlackboard1024.TotalSize, BlueprintBlackboard1024.MaxSlots);

            bool ok1 = BlueprintBlackboardPartitions.TryAttach(memory, 1, 8, 0, out int off1);
            bool ok2 = BlueprintBlackboardPartitions.TryAttach(memory, 2, 16, 0, out int off2);
            bool ok3 = BlueprintBlackboardPartitions.TryAttach(memory, 3, 8, 0, out int off3);

            Assert.True(ok1);
            Assert.True(ok2);
            Assert.True(ok3);

            // All from bump: contiguous
            Assert.Equal(BlueprintBlackboard1024.PayloadStart, off1);
            Assert.Equal(BlueprintBlackboard1024.PayloadStart + 8, off2);
            Assert.Equal(BlueprintBlackboard1024.PayloadStart + 8 + 16, off3);

            ref var header = ref Header(memory);
            Assert.Equal(3, (int)header.SlotCount);
            Assert.Equal(0, (int)header.FreeListHead);
            Assert.Equal(BlueprintBlackboard1024.PayloadStart + 8 + 16 + 8, (int)header.PayloadHighWater);
        }
    }

    // ---- SC4: Detach_Last_FreesSlot -----------------------------------------

    [Fact]
    public void Detach_Last_FreesSlot()
    {
        var buf = MakeBuffer(BlueprintBlackboard1024.TotalSize);
        fixed (byte* memory = buf)
        {
            BlueprintBlackboardPartitions.Initialize(
                memory, BlueprintBlackboard1024.TotalSize, BlueprintBlackboard1024.MaxSlots);

            BlueprintBlackboardPartitions.TryAttach(memory, 1, 8, 0, out _);
            BlueprintBlackboardPartitions.TryAttach(memory, 2, 8, 0, out _);
            BlueprintBlackboardPartitions.TryAttach(memory, 3, 8, 0, out _);

            bool detached = BlueprintBlackboardPartitions.TryDetach(memory, blueprintId: 3);

            Assert.True(detached);
            ref var header = ref Header(memory);
            Assert.Equal(2, (int)header.SlotCount);
            // PayloadFree restored by 8
            Assert.Equal(BlueprintBlackboard1024.PayloadSize - 16, (int)header.PayloadFree);
            // Free list has the freed block
            Assert.NotEqual(0, (int)header.FreeListHead);
            AssertInvariants(memory, BlueprintBlackboard1024.TotalSize, BlueprintBlackboard1024.MaxSlots);
        }
    }

    // ---- SC5: Detach_Middle_CreatesFreeBlock ---------------------------------

    [Fact]
    public void Detach_Middle_CreatesFreeBlock()
    {
        var buf = MakeBuffer(BlueprintBlackboard1024.TotalSize);
        fixed (byte* memory = buf)
        {
            BlueprintBlackboardPartitions.Initialize(
                memory, BlueprintBlackboard1024.TotalSize, BlueprintBlackboard1024.MaxSlots);

            BlueprintBlackboardPartitions.TryAttach(memory, 1, 8, 0, out _);
            BlueprintBlackboardPartitions.TryAttach(memory, 2, 8, 0, out _);
            BlueprintBlackboardPartitions.TryAttach(memory, 3, 8, 0, out int off3);

            // Detach middle bp2
            bool detached = BlueprintBlackboardPartitions.TryDetach(memory, blueprintId: 2);
            Assert.True(detached);

            ref var header = ref Header(memory);
            Assert.Equal(2, (int)header.SlotCount);

            // After dense-compact: slot[1] should now hold what was slot[2] (bp3)
            ref var s0 = ref Slot(memory, 0);
            ref var s1 = ref Slot(memory, 1);
            Assert.Equal(1, s0.BlueprintId);
            Assert.Equal(3, s1.BlueprintId);

            // Free list has exactly 1 block
            Assert.Equal(1, FreeListLength(memory));
            AssertInvariants(memory, BlueprintBlackboard1024.TotalSize, BlueprintBlackboard1024.MaxSlots);
        }
    }

    // ---- SC6: Detach_AdjacentFree_Coalesces ---------------------------------

    [Fact]
    public void Detach_AdjacentFree_Coalesces()
    {
        var buf = MakeBuffer(BlueprintBlackboard1024.TotalSize);
        fixed (byte* memory = buf)
        {
            BlueprintBlackboardPartitions.Initialize(
                memory, BlueprintBlackboard1024.TotalSize, BlueprintBlackboard1024.MaxSlots);

            BlueprintBlackboardPartitions.TryAttach(memory, 1, 8, 0, out _);
            BlueprintBlackboardPartitions.TryAttach(memory, 2, 8, 0, out _);
            BlueprintBlackboardPartitions.TryAttach(memory, 3, 8, 0, out _);

            // Detach middle (bp2 at offset 104) then last (bp3 at offset 112)
            BlueprintBlackboardPartitions.TryDetach(memory, blueprintId: 2);
            // After detaching bp2: slot[1] now holds bp3; bp2's payload freed
            BlueprintBlackboardPartitions.TryDetach(memory, blueprintId: 3);

            // Two adjacent free blocks should coalesce into one
            Assert.Equal(1, FreeListLength(memory));

            ref var header = ref Header(memory);
            // The coalesced block should be 16 bytes (8+8)
            ref var block = ref FreeBlock(memory, header.FreeListHead);
            Assert.Equal((ushort)16, block.Size);

            AssertInvariants(memory, BlueprintBlackboard1024.TotalSize, BlueprintBlackboard1024.MaxSlots);
        }
    }

    // ---- SC7: Attach_AfterDetach_ReusesFreeBlock ----------------------------

    [Fact]
    public void Attach_AfterDetach_ReusesFreeBlock()
    {
        var buf = MakeBuffer(BlueprintBlackboard1024.TotalSize);
        fixed (byte* memory = buf)
        {
            BlueprintBlackboardPartitions.Initialize(
                memory, BlueprintBlackboard1024.TotalSize, BlueprintBlackboard1024.MaxSlots);

            BlueprintBlackboardPartitions.TryAttach(memory, 1, 8, 0, out _);
            BlueprintBlackboardPartitions.TryAttach(memory, 2, 8, 0, out int off2);
            BlueprintBlackboardPartitions.TryAttach(memory, 3, 8, 0, out _);

            // Detach bp2 (middle)
            BlueprintBlackboardPartitions.TryDetach(memory, blueprintId: 2);

            // Attach bp4 of same size -- should reuse bp2's freed payload slot
            bool ok = BlueprintBlackboardPartitions.TryAttach(memory, 4, 8, 0, out int off4);

            Assert.True(ok);
            Assert.Equal(off2, off4); // reused freed offset
            Assert.Equal(0, (int)Header(memory).FreeListHead); // free list consumed

            AssertInvariants(memory, BlueprintBlackboard1024.TotalSize, BlueprintBlackboard1024.MaxSlots);
        }
    }

    // ---- SC8: TryGetSlotOffset_AbsentBlueprint_ReturnsFalse -----------------

    [Fact]
    public void TryGetSlotOffset_AbsentBlueprint_ReturnsFalse()
    {
        var buf = MakeBuffer(BlueprintBlackboard1024.TotalSize);
        fixed (byte* memory = buf)
        {
            BlueprintBlackboardPartitions.Initialize(
                memory, BlueprintBlackboard1024.TotalSize, BlueprintBlackboard1024.MaxSlots);

            BlueprintBlackboardPartitions.TryAttach(memory, 1, 8, 0, out _);

            bool found = BlueprintBlackboardPartitions.TryGetSlotOffset(
                memory, blueprintId: 999, out int offset);

            Assert.False(found);
            Assert.Equal(0, offset);
        }
    }

    // ---- SC9: Attach_WhenSlotsFull_ReturnsFalse -----------------------------

    [Fact]
    public void Attach_WhenSlotsFull_ReturnsFalse()
    {
        var buf = MakeBuffer(BlueprintBlackboard1024.TotalSize);
        fixed (byte* memory = buf)
        {
            BlueprintBlackboardPartitions.Initialize(
                memory, BlueprintBlackboard1024.TotalSize, BlueprintBlackboard1024.MaxSlots);

            // Fill all MaxSlots=4 slots
            for (int i = 1; i <= BlueprintBlackboard1024.MaxSlots; i++)
                Assert.True(BlueprintBlackboardPartitions.TryAttach(memory, i, 8, 0, out _));

            // One more must fail
            bool ok = BlueprintBlackboardPartitions.TryAttach(memory, 99, 8, 0, out int offset);

            Assert.False(ok);
            Assert.Equal(0, offset);
        }
    }

    // ---- SC10: Attach_WhenInsufficientSpace_ReturnsFalse --------------------

    [Fact]
    public void Attach_WhenInsufficientSpace_ReturnsFalse()
    {
        var buf = MakeBuffer(BlueprintBlackboard1024.TotalSize);
        fixed (byte* memory = buf)
        {
            BlueprintBlackboardPartitions.Initialize(
                memory, BlueprintBlackboard1024.TotalSize, BlueprintBlackboard1024.MaxSlots);

            // Request more bytes than the entire payload
            bool ok = BlueprintBlackboardPartitions.TryAttach(
                memory, blueprintId: 1,
                requestedSize: BlueprintBlackboard1024.PayloadSize + 8,
                structureHash: 0,
                out int offset);

            Assert.False(ok);
            Assert.Equal(0, offset);
        }
    }

    // ---- SC11: Attach_Fragmented_ReturnsFalseEvenIfTotalFreeBigEnough --------

    [Fact]
    public void Attach_Fragmented_ReturnsFalseEvenIfTotalFreeBigEnough()
    {
        var buf = MakeBuffer(BlueprintBlackboard1024.TotalSize);
        fixed (byte* memory = buf)
        {
            BlueprintBlackboardPartitions.Initialize(
                memory, BlueprintBlackboard1024.TotalSize, BlueprintBlackboard1024.MaxSlots);

            // Fill all 4 slots to exhaust bump region
            // bp1=112, bp2=112, bp3=112, bp4=496 -> 432 payload bytes used, PayloadHighWater=96+832=928? 
            // Wait: 96 + 112 + 112 + 112 + 496 = 96 + 832 = 928 = PayloadStart + PayloadSize (TotalSize)
            // But we need to check PayloadEnd = PayloadStart + PayloadSize = 96 + 928 = 1024
            // So PayloadHighWater after all 4 attaches = 96 + 112 + 112 + 112 + 496 = 928. Not TotalSize.
            // Correction: PayloadHighWater starts at PayloadStart=96.
            // After bp1(112): PayloadHighWater = 96+112 = 208
            // After bp2(112): PayloadHighWater = 208+112 = 320
            // After bp3(112): PayloadHighWater = 320+112 = 432
            // After bp4(496): PayloadHighWater = 432+496 = 928
            // PayloadEnd = 96 + 928 = 1024.
            // Available in bump for next = 1024 - 928 = 96. So bump IS exhausted for large allocations.
            Assert.True(BlueprintBlackboardPartitions.TryAttach(memory, 1, 112, 0, out _));
            Assert.True(BlueprintBlackboardPartitions.TryAttach(memory, 2, 112, 0, out _));
            Assert.True(BlueprintBlackboardPartitions.TryAttach(memory, 3, 112, 0, out _));
            Assert.True(BlueprintBlackboardPartitions.TryAttach(memory, 4, 496, 0, out _));

            // Detach bp1 and bp3 to create non-adjacent free blocks (each 112 bytes)
            BlueprintBlackboardPartitions.TryDetach(memory, 1);
            BlueprintBlackboardPartitions.TryDetach(memory, 3);

            // PayloadFree = 224, but two 112-byte holes separated by bp2.
            // Try to attach 200 bytes -- larger than either hole but < total free.
            bool ok = BlueprintBlackboardPartitions.TryAttach(memory, 5, 200, 0, out int offset);

            Assert.False(ok);
            Assert.Equal(0, offset);
            // Total free (224) > 200 but no contiguous block available
            Assert.True((int)Header(memory).PayloadFree >= 200);
        }
    }

    // ---- SC12: ResetSlot_PreservesSlotIdentity ------------------------------

    [Fact]
    public void ResetSlot_PreservesSlotIdentity()
    {
        var buf = MakeBuffer(BlueprintBlackboard1024.TotalSize);
        fixed (byte* memory = buf)
        {
            BlueprintBlackboardPartitions.Initialize(
                memory, BlueprintBlackboard1024.TotalSize, BlueprintBlackboard1024.MaxSlots);

            BlueprintBlackboardPartitions.TryAttach(memory, 1, 8, 0xDEADBEEFCAFEBABEUL, out int off);

            // Write some payload bytes
            *(ulong*)(memory + off) = 0xFFFF_FFFF_FFFF_FFFFul;

            ref var slotBefore = ref Slot(memory, 0);
            int idBefore     = slotBefore.BlueprintId;
            ushort offBefore = slotBefore.PayloadOffset;
            ushort sizeBefore = slotBefore.PayloadSize;
            uint verBefore   = slotBefore.InstanceVersion;

            BlueprintBlackboardPartitions.ResetSlot(memory, slotIndex: 0, newStructureHash: 0xC0FFEE00u);

            ref var slotAfter = ref Slot(memory, 0);
            Assert.Equal(idBefore, slotAfter.BlueprintId);
            Assert.Equal(offBefore, slotAfter.PayloadOffset);
            Assert.Equal(sizeBefore, slotAfter.PayloadSize);
            Assert.Equal(verBefore + 1, slotAfter.InstanceVersion);
            Assert.Equal(0xC0FFEE00u, slotAfter.StructureHash);

            // Payload zeroed
            ulong payloadValue = *(ulong*)(memory + off);
            Assert.Equal(0ul, payloadValue);

            AssertInvariants(memory, BlueprintBlackboard1024.TotalSize, BlueprintBlackboard1024.MaxSlots);
        }
    }

    // ---- SC13: CopyToLargerTier_PreservesAllocations ------------------------

    [Fact]
    public void CopyToLargerTier_PreservesAllocations()
    {
        var src = MakeBuffer(BlueprintBlackboard1024.TotalSize);
        var dst = MakeBuffer(BlueprintBlackboard4096.TotalSize);

        fixed (byte* srcMem = src)
        fixed (byte* dstMem = dst)
        {
            BlueprintBlackboardPartitions.Initialize(
                srcMem, BlueprintBlackboard1024.TotalSize, BlueprintBlackboard1024.MaxSlots);

            // Attach 3 blueprints with distinct data
            BlueprintBlackboardPartitions.TryAttach(srcMem, 10, 8, 0x1111u, out int off10);
            BlueprintBlackboardPartitions.TryAttach(srcMem, 20, 16, 0x2222u, out int off20);
            BlueprintBlackboardPartitions.TryAttach(srcMem, 30, 8, 0x3333u, out int off30);

            // Write recognizable data into each slot
            *(ulong*)(srcMem + off10) = 0xAAAA_AAAA_AAAA_AAAAul;
            *(ulong*)(srcMem + off20) = 0xBBBB_BBBB_BBBB_BBBBul;
            *(ulong*)(srcMem + off30) = 0xCCCC_CCCC_CCCC_CCCCul;

            BlueprintBlackboardPartitions.CopyToLargerTier(
                srcMem, BlueprintBlackboard1024.TotalSize,
                dstMem, BlueprintBlackboard4096.TotalSize, BlueprintBlackboard4096.MaxSlots);

            // payloadShift = (8 - 4) * 16 = 64
            int payloadShift = (BlueprintBlackboard4096.MaxSlots - BlueprintBlackboard1024.MaxSlots)
                               * BlueprintBlackboardPartitions.SlotEntrySize;

            ref var dstHeader = ref Header(dstMem);
            Assert.Equal(3, (int)dstHeader.SlotCount);

            // Each slot's PayloadOffset shifted
            bool found10 = BlueprintBlackboardPartitions.TryGetSlotOffset(dstMem, 10, out int doff10);
            bool found20 = BlueprintBlackboardPartitions.TryGetSlotOffset(dstMem, 20, out int doff20);
            bool found30 = BlueprintBlackboardPartitions.TryGetSlotOffset(dstMem, 30, out int doff30);
            Assert.True(found10);
            Assert.True(found20);
            Assert.True(found30);
            Assert.Equal(off10 + payloadShift, doff10);
            Assert.Equal(off20 + payloadShift, doff20);
            Assert.Equal(off30 + payloadShift, doff30);

            // Payload data preserved
            Assert.Equal(0xAAAA_AAAA_AAAA_AAAAul, *(ulong*)(dstMem + doff10));
            Assert.Equal(0xBBBB_BBBB_BBBB_BBBBul, *(ulong*)(dstMem + doff20));
            Assert.Equal(0xCCCC_CCCC_CCCC_CCCCul, *(ulong*)(dstMem + doff30));

            AssertInvariants(dstMem, BlueprintBlackboard4096.TotalSize, BlueprintBlackboard4096.MaxSlots);
        }
    }

    // ---- SC14: CopyToLargerTier_PreservesFreeList ---------------------------

    [Fact]
    public void CopyToLargerTier_PreservesFreeList()
    {
        var src = MakeBuffer(BlueprintBlackboard1024.TotalSize);
        var dst = MakeBuffer(BlueprintBlackboard4096.TotalSize);

        fixed (byte* srcMem = src)
        fixed (byte* dstMem = dst)
        {
            BlueprintBlackboardPartitions.Initialize(
                srcMem, BlueprintBlackboard1024.TotalSize, BlueprintBlackboard1024.MaxSlots);

            BlueprintBlackboardPartitions.TryAttach(srcMem, 1, 16, 0, out _);
            BlueprintBlackboardPartitions.TryAttach(srcMem, 2, 16, 0, out int off2);
            BlueprintBlackboardPartitions.TryAttach(srcMem, 3, 16, 0, out _);

            // Detach bp2 to create a free block
            BlueprintBlackboardPartitions.TryDetach(srcMem, 2);
            Assert.NotEqual(0, (int)Header(srcMem).FreeListHead);

            int payloadShift = (BlueprintBlackboard4096.MaxSlots - BlueprintBlackboard1024.MaxSlots)
                               * BlueprintBlackboardPartitions.SlotEntrySize;

            BlueprintBlackboardPartitions.CopyToLargerTier(
                srcMem, BlueprintBlackboard1024.TotalSize,
                dstMem, BlueprintBlackboard4096.TotalSize, BlueprintBlackboard4096.MaxSlots);

            ref var dstHeader = ref Header(dstMem);
            // Free list head should be shifted by payloadShift
            Assert.NotEqual(0, (int)dstHeader.FreeListHead);
            Assert.Equal((int)Header(srcMem).FreeListHead + payloadShift, (int)dstHeader.FreeListHead);

            // Free list block size preserved
            ref var srcBlock = ref FreeBlock(srcMem, Header(srcMem).FreeListHead);
            ref var dstBlock = ref FreeBlock(dstMem, dstHeader.FreeListHead);
            Assert.Equal(srcBlock.Size, dstBlock.Size);

            AssertInvariants(dstMem, BlueprintBlackboard4096.TotalSize, BlueprintBlackboard4096.MaxSlots);
        }
    }

    // ---- SC15: LayoutInvariants_HoldAfterEveryOperation --------------------

    [Fact]
    public void LayoutInvariants_HoldAfterEveryOperation()
    {
        const int TotalSize = BlueprintBlackboard1024.TotalSize;
        const int MaxSlots  = BlueprintBlackboard1024.MaxSlots;

        var buf = MakeBuffer(TotalSize);
        fixed (byte* memory = buf)
        {
            BlueprintBlackboardPartitions.Initialize(memory, TotalSize, MaxSlots);
            AssertInvariants(memory, TotalSize, MaxSlots);

            var rng = new Random(42);
            var attached = new HashSet<int>();      // currently-attached blueprint IDs
            var pool = Enumerable.Range(1, 20).ToList(); // pool of possible IDs

            for (int step = 0; step < 100; step++)
            {
                int action = rng.Next(3); // 0=attach, 1=detach, 2=reset

                if (action == 0 && attached.Count < MaxSlots)
                {
                    // Attach a blueprint not currently attached
                    var candidates = pool.Where(id => !attached.Contains(id)).ToList();
                    if (candidates.Count > 0)
                    {
                        int id = candidates[rng.Next(candidates.Count)];
                        int size = (rng.Next(1, 17)) * 8; // 8..128, aligned
                        bool ok = BlueprintBlackboardPartitions.TryAttach(
                            memory, id, size, (ulong)rng.Next(), out _);
                        if (ok) attached.Add(id);
                    }
                }
                else if (action == 1 && attached.Count > 0)
                {
                    // Detach a randomly chosen attached blueprint
                    var list = attached.ToList();
                    int id = list[rng.Next(list.Count)];
                    bool ok = BlueprintBlackboardPartitions.TryDetach(memory, id);
                    if (ok) attached.Remove(id);
                }
                else if (action == 2 && attached.Count > 0)
                {
                    // Reset a randomly chosen slot
                    int slotCount = BlueprintBlackboardPartitions.GetSlotCount(memory);
                    if (slotCount > 0)
                    {
                        int idx = rng.Next(slotCount);
                        BlueprintBlackboardPartitions.ResetSlot(
                            memory, idx, (ulong)rng.Next());
                    }
                }

                AssertInvariants(memory, TotalSize, MaxSlots);
            }
        }
    }
}
