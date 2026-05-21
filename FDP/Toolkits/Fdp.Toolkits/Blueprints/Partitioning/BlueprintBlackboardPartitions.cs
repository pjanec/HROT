using System.Runtime.CompilerServices;

namespace Fdp.Toolkit.Blueprints.Partitioning;

/// <summary>
/// Pointer-based partition allocator for Blueprint blackboard components.
/// Slices a flat byte buffer (a tier component) into per-Blueprint slots.
/// Per Runtime DD §5.
/// </summary>
public static unsafe class BlueprintBlackboardPartitions
{
    /// <summary>Byte size of a single <see cref="BlueprintSlotEntry"/>.</summary>
    public const int SlotEntrySize       = 16;

    /// <summary>Byte size of the in-payload free-block header.</summary>
    public const int FreeBlockHeaderSize = 4;

    /// <summary>Payload byte alignment: all slot offsets are multiples of 8.</summary>
    public const int Alignment           = 8;

    // Same constant as BlueprintBlackboardHeader.MagicValue.
    private const uint HeaderMagicV1 = 0x42504257u;

    // ---- Public API ---------------------------------------------------------

    /// <summary>
    /// Initializes a freshly-zeroed component to be ready for slot allocation.
    /// Idempotent if the header magic already matches; otherwise zeroes and re-initializes.
    /// </summary>
    public static void Initialize(byte* memory, int totalSize, byte maxSlots)
    {
        ref var header = ref Unsafe.AsRef<BlueprintBlackboardHeader>(memory);

        // Idempotent: already initialized
        if (header.MagicAndVersion == HeaderMagicV1)
            return;

        Unsafe.InitBlock(memory, 0, (uint)totalSize);

        int slotTableSize = maxSlots * SlotEntrySize;
        int payloadStart  = sizeof(BlueprintBlackboardHeader) + slotTableSize;
        int payloadSize   = totalSize - payloadStart;

        header.MagicAndVersion  = HeaderMagicV1;
        header.SlotCount        = 0;
        header.MaxSlots         = maxSlots;
        header.FreeListHead     = 0;
        header.PayloadStart     = (ushort)payloadStart;
        header.PayloadSize      = (ushort)payloadSize;
        header.PayloadFree      = (ushort)payloadSize;
        header.PayloadHighWater = (ushort)payloadStart;
    }

    /// <summary>
    /// Hot-path linear scan: finds the slot occupied by <paramref name="blueprintId"/>
    /// and returns its payload offset. Iterates only allocated slots (0..SlotCount).
    /// </summary>
    public static bool TryGetSlotOffset(byte* memory, int blueprintId, out int payloadOffset)
    {
        ref var header = ref Unsafe.AsRef<BlueprintBlackboardHeader>(memory);
        int slotCount = header.SlotCount;
        byte* slotTable = memory + sizeof(BlueprintBlackboardHeader);

        for (int i = 0; i < slotCount; i++)
        {
            ref var slot = ref Unsafe.AsRef<BlueprintSlotEntry>(slotTable + i * SlotEntrySize);
            if (slot.BlueprintId == blueprintId)
            {
                payloadOffset = slot.PayloadOffset;
                return true;
            }
        }

        payloadOffset = 0;
        return false;
    }

    /// <summary>
    /// Allocates a payload slot for <paramref name="blueprintId"/>.
    /// Tries the free list first, falls back to bump allocation.
    /// Returns false if no slot or no payload space is available.
    /// </summary>
    public static bool TryAttach(
        byte*  memory,
        int    blueprintId,
        int    requestedSize,
        ulong  structureHash,
        out int payloadOffset)
    {
        ref var header = ref Unsafe.AsRef<BlueprintBlackboardHeader>(memory);
        byte* slotTable = memory + sizeof(BlueprintBlackboardHeader);

        if (header.SlotCount >= header.MaxSlots)
        {
            payloadOffset = 0;
            return false;
        }

        int alignedSize = AlignUp(requestedSize, Alignment);

        if (alignedSize > header.PayloadFree)
        {
            payloadOffset = 0;
            return false;
        }

        int allocatedOffset = TryAllocateFromFreeList(memory, ref header, alignedSize);
        if (allocatedOffset == 0)
            allocatedOffset = BumpAllocate(memory, ref header, alignedSize);

        if (allocatedOffset == 0)
        {
            // Fragmented: free space exists but no contiguous block
            payloadOffset = 0;
            return false;
        }

        int slotIndex = header.SlotCount;
        ref var slot = ref Unsafe.AsRef<BlueprintSlotEntry>(slotTable + slotIndex * SlotEntrySize);
        slot.BlueprintId     = blueprintId;
        slot.InstanceVersion = 1;
        slot.PayloadOffset   = (ushort)allocatedOffset;
        slot.PayloadSize     = (ushort)alignedSize;
        slot.StructureHash   = (uint)structureHash; // Lower 32 bits -- DEBT-014

        header.SlotCount++;
        header.PayloadFree = (ushort)(header.PayloadFree - alignedSize);

        payloadOffset = allocatedOffset;
        return true;
    }

    /// <summary>
    /// Marks the slot for <paramref name="blueprintId"/> as free, returns its payload
    /// bytes to the free list, and attempts coalescing with adjacent free blocks.
    /// Returns false if no matching slot is found.
    /// </summary>
    public static bool TryDetach(byte* memory, int blueprintId)
    {
        ref var header = ref Unsafe.AsRef<BlueprintBlackboardHeader>(memory);
        byte* slotTable = memory + sizeof(BlueprintBlackboardHeader);

        int foundIndex = -1;
        for (int i = 0; i < header.SlotCount; i++)
        {
            ref var slot = ref Unsafe.AsRef<BlueprintSlotEntry>(slotTable + i * SlotEntrySize);
            if (slot.BlueprintId == blueprintId)
            {
                foundIndex = i;
                break;
            }
        }

        if (foundIndex < 0) return false;

        ref var foundSlot = ref Unsafe.AsRef<BlueprintSlotEntry>(slotTable + foundIndex * SlotEntrySize);
        int releasedOffset = foundSlot.PayloadOffset;
        int releasedSize   = foundSlot.PayloadSize;

        ReturnToFreeList(memory, ref header, releasedOffset, releasedSize);
        header.PayloadFree = (ushort)(header.PayloadFree + releasedSize);

        // Dense-compact slot table: move last entry into the freed slot
        int lastIndex = header.SlotCount - 1;
        if (foundIndex != lastIndex)
        {
            ref var lastSlot = ref Unsafe.AsRef<BlueprintSlotEntry>(slotTable + lastIndex * SlotEntrySize);
            foundSlot = lastSlot;
        }

        // Clear the (now duplicated) last slot
        ref var clearedSlot = ref Unsafe.AsRef<BlueprintSlotEntry>(slotTable + lastIndex * SlotEntrySize);
        clearedSlot = default;
        header.SlotCount--;

        return true;
    }

    /// <summary>Returns the number of currently-allocated slots.</summary>
    public static int GetSlotCount(byte* memory)
    {
        ref var header = ref Unsafe.AsRef<BlueprintBlackboardHeader>(memory);
        return header.SlotCount;
    }

    /// <summary>Returns a ref to slot <paramref name="slotIndex"/> in the slot table.</summary>
    public static ref BlueprintSlotEntry GetSlot(byte* memory, int slotIndex)
    {
        byte* slotTable = memory + sizeof(BlueprintBlackboardHeader);
        return ref Unsafe.AsRef<BlueprintSlotEntry>(slotTable + slotIndex * SlotEntrySize);
    }

    /// <summary>
    /// Zeros the slot's payload bytes and bumps its InstanceVersion.
    /// Used during hard reload. Payload offset/size and BlueprintId are preserved.
    /// </summary>
    public static void ResetSlot(byte* memory, int slotIndex, ulong newStructureHash)
    {
        byte* slotTable = memory + sizeof(BlueprintBlackboardHeader);
        ref var slot = ref Unsafe.AsRef<BlueprintSlotEntry>(slotTable + slotIndex * SlotEntrySize);

        Unsafe.InitBlock(memory + slot.PayloadOffset, 0, slot.PayloadSize);

        slot.StructureHash    = (uint)newStructureHash; // Lower 32 bits -- DEBT-014
        slot.InstanceVersion += 1;
    }

    /// <summary>
    /// Copies header + slot table + payload from a smaller tier component to a larger one.
    /// Used by BlueprintMaintenanceSystem during tier upgrade.
    /// </summary>
    public static void CopyToLargerTier(
        byte* src, int srcSize,
        byte* dst, int dstSize, byte dstMaxSlots)
    {
        ref var srcHeader = ref Unsafe.AsRef<BlueprintBlackboardHeader>(src);

        if (srcHeader.MagicAndVersion != HeaderMagicV1)
        {
            Initialize(dst, dstSize, dstMaxSlots);
            return;
        }

        Initialize(dst, dstSize, dstMaxSlots);
        ref var dstHeader = ref Unsafe.AsRef<BlueprintBlackboardHeader>(dst);

        int srcSlotTableSize = srcHeader.MaxSlots * SlotEntrySize;
        int dstSlotTableSize = dstMaxSlots * SlotEntrySize;
        int payloadShift = dstSlotTableSize - srcSlotTableSize;

        byte* srcSlots = src + sizeof(BlueprintBlackboardHeader);
        byte* dstSlots = dst + sizeof(BlueprintBlackboardHeader);

        for (int i = 0; i < srcHeader.SlotCount; i++)
        {
            ref var srcSlot = ref Unsafe.AsRef<BlueprintSlotEntry>(srcSlots + i * SlotEntrySize);
            ref var dstSlot = ref Unsafe.AsRef<BlueprintSlotEntry>(dstSlots + i * SlotEntrySize);
            dstSlot = srcSlot;
            dstSlot.PayloadOffset = (ushort)(srcSlot.PayloadOffset + payloadShift);

            Unsafe.CopyBlock(
                destination: dst + dstSlot.PayloadOffset,
                source:      src + srcSlot.PayloadOffset,
                byteCount:   srcSlot.PayloadSize);
        }

        dstHeader.SlotCount        = srcHeader.SlotCount;
        dstHeader.PayloadFree      = (ushort)(dstHeader.PayloadSize - SumAllocated(srcHeader, srcSlots));
        dstHeader.PayloadHighWater = (ushort)(dstHeader.PayloadStart + (srcHeader.PayloadHighWater - srcHeader.PayloadStart));

        if (srcHeader.FreeListHead != 0)
        {
            dstHeader.FreeListHead = (ushort)(srcHeader.FreeListHead + payloadShift);
            // Walk and shift NextFreeOffset pointers; copy block data from source position
            ushort cursor = dstHeader.FreeListHead;
            while (cursor != 0)
            {
                ref var block    = ref Unsafe.AsRef<BlueprintFreeBlockHeader>(dst + cursor);
                ref var srcBlock = ref Unsafe.AsRef<BlueprintFreeBlockHeader>(src + (cursor - payloadShift));
                block.Size = srcBlock.Size;
                block.NextFreeOffset = (ushort)(srcBlock.NextFreeOffset == 0
                    ? 0 : srcBlock.NextFreeOffset + payloadShift);
                cursor = block.NextFreeOffset;
            }
        }
    }

    // ---- Private helpers ----------------------------------------------------

    private static int TryAllocateFromFreeList(byte* memory, ref BlueprintBlackboardHeader header, int alignedSize)
    {
        ushort prev    = 0;
        ushort current = header.FreeListHead;

        while (current != 0)
        {
            ref var block = ref Unsafe.AsRef<BlueprintFreeBlockHeader>(memory + current);

            if (block.Size >= alignedSize + FreeBlockHeaderSize)
            {
                // Split: keep tail as smaller free block
                int remaining     = block.Size - alignedSize;
                int allocOffset   = current;
                int newFreeOffset = current + alignedSize;

                if (prev == 0) header.FreeListHead = (ushort)newFreeOffset;
                else
                {
                    ref var prevBlock = ref Unsafe.AsRef<BlueprintFreeBlockHeader>(memory + prev);
                    prevBlock.NextFreeOffset = (ushort)newFreeOffset;
                }

                ref var newFreeBlock = ref Unsafe.AsRef<BlueprintFreeBlockHeader>(memory + newFreeOffset);
                newFreeBlock.NextFreeOffset = block.NextFreeOffset;
                newFreeBlock.Size           = (ushort)remaining;

                return allocOffset;
            }
            else if (block.Size == alignedSize)
            {
                // Exact fit: unlink this block
                int allocOffset = current;
                if (prev == 0) header.FreeListHead = block.NextFreeOffset;
                else
                {
                    ref var prevBlock = ref Unsafe.AsRef<BlueprintFreeBlockHeader>(memory + prev);
                    prevBlock.NextFreeOffset = block.NextFreeOffset;
                }
                return allocOffset;
            }

            prev    = current;
            current = block.NextFreeOffset;
        }

        return 0; // no fitting block
    }

    private static int BumpAllocate(byte* memory, ref BlueprintBlackboardHeader header, int alignedSize)
    {
        int payloadEnd = header.PayloadStart + header.PayloadSize;
        int available  = payloadEnd - header.PayloadHighWater;
        if (available < alignedSize) return 0;

        int allocOffset = header.PayloadHighWater;
        header.PayloadHighWater = (ushort)(allocOffset + alignedSize);
        return allocOffset;
    }

    private static void ReturnToFreeList(byte* memory, ref BlueprintBlackboardHeader header, int offset, int size)
    {
        ushort prev    = 0;
        ushort current = header.FreeListHead;

        while (current != 0 && current < offset)
        {
            prev = current;
            ref var b = ref Unsafe.AsRef<BlueprintFreeBlockHeader>(memory + current);
            current = b.NextFreeOffset;
        }

        ref var newBlock = ref Unsafe.AsRef<BlueprintFreeBlockHeader>(memory + offset);
        newBlock.Size           = (ushort)size;
        newBlock.NextFreeOffset = current;

        if (prev == 0) header.FreeListHead = (ushort)offset;
        else
        {
            ref var prevBlock = ref Unsafe.AsRef<BlueprintFreeBlockHeader>(memory + prev);
            prevBlock.NextFreeOffset = (ushort)offset;
        }

        // Coalesce with successor
        if (current != 0 && offset + size == current)
        {
            ref var succ = ref Unsafe.AsRef<BlueprintFreeBlockHeader>(memory + current);
            newBlock.Size           = (ushort)(newBlock.Size + succ.Size);
            newBlock.NextFreeOffset = succ.NextFreeOffset;
        }

        // Coalesce with predecessor
        if (prev != 0)
        {
            ref var pred = ref Unsafe.AsRef<BlueprintFreeBlockHeader>(memory + prev);
            if (prev + pred.Size == offset)
            {
                pred.Size           = (ushort)(pred.Size + newBlock.Size);
                pred.NextFreeOffset = newBlock.NextFreeOffset;
            }
        }
    }

    private static int AlignUp(int value, int alignment)
        => (value + alignment - 1) & ~(alignment - 1);

    private static int SumAllocated(BlueprintBlackboardHeader header, byte* slots)
    {
        int total = 0;
        for (int i = 0; i < header.SlotCount; i++)
        {
            ref var s = ref Unsafe.AsRef<BlueprintSlotEntry>(slots + i * SlotEntrySize);
            total += s.PayloadSize;
        }
        return total;
    }
}

