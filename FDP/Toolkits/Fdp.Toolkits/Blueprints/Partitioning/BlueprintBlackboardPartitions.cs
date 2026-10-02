using System;
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

    /// <summary>
    /// A3 / <c>D1′</c> — how many slots the per-slot <see cref="OccurrenceKind"/> nibble array can
    /// address: <c>sizeof(ulong) * 2 == 16</c>, because
    /// <see cref="BlueprintBlackboardHeader.Reserved"/> is the 8 bytes it lives in.
    ///
    /// <para>⚠ <b>This BINDS every tier to <c>MaxSlots &lt;= 16</c>.</b> Today the ladder is 4 / 8 / 16
    /// (<c>BlueprintBlackboard1024/4096/16384</c>), so it is an exact fit with nothing spare —
    /// ⛔ a future tier above 16 slots must widen the scheme <b>deliberately</b> (a second reserved
    /// word, or a packed side table), not discover the limit at runtime. ⭐ The rail
    /// <c>Kind_NibbleArray_CoversEveryTiersMaxSlots</c> fails the moment a tier crosses it.</para>
    /// </summary>
    public const int MaxKindSlots        = 16;

    /// <summary>
    /// The largest <see cref="OccurrenceKind"/> value a 4-bit nibble can hold. ⛔ A kind above this
    /// is rejected by <see cref="SetSlotKind"/> rather than silently truncated.
    /// </summary>
    public const int MaxKind             = 0xF;

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
    /// Delegates to the 4-arg overload; kept for byte-compat with existing call sites
    /// that don't need the slot's <see cref="BlueprintSlotEntry.StructureHash"/>.
    /// </summary>
    public static bool TryGetSlotOffset(byte* memory, int blueprintId, out int payloadOffset)
        => TryGetSlotOffset(memory, blueprintId, out payloadOffset, out _);

    /// <summary>
    /// Hot-path linear scan: finds the slot occupied by <paramref name="blueprintId"/>
    /// and returns both its payload offset and its stored <see cref="BlueprintSlotEntry.StructureHash"/>
    /// (lower 32 bits of the Blueprint's structure hash, set at <see cref="TryAttach"/> time). Iterates
    /// only allocated slots (0..SlotCount). Callers that need to guard against reader/owner layout drift
    /// (e.g. <c>BlueprintSharedState.TryGetShared</c>) compare <paramref name="structureHash"/> against
    /// their own expected hash before trusting <paramref name="payloadOffset"/>.
    /// </summary>
    public static bool TryGetSlotOffset(byte* memory, int blueprintId, out int payloadOffset, out uint structureHash)
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
                structureHash = slot.StructureHash;
                return true;
            }
        }

        payloadOffset = 0;
        structureHash = 0;
        return false;
    }

    /// <summary>
    /// Finds the slot table INDEX occupied by <paramref name="blueprintId"/>.
    ///
    /// <para>⭐ The index — not the payload offset — is what addresses the per-slot
    /// <see cref="OccurrenceKind"/> nibble, and it is what <c>O0</c>'s walker already iterates.</para>
    ///
    /// <para>⛔ <b>An index is only valid until the next <see cref="TryDetach"/></b>, which
    /// dense-compacts the table (<c>:188-199</c>). Never cache one across a mutation.</para>
    /// </summary>
    public static bool TryGetSlotIndex(byte* memory, int blueprintId, out int slotIndex)
    {
        ref var header = ref Unsafe.AsRef<BlueprintBlackboardHeader>(memory);
        byte* slotTable = memory + sizeof(BlueprintBlackboardHeader);

        for (int i = 0; i < header.SlotCount; i++)
        {
            ref var slot = ref Unsafe.AsRef<BlueprintSlotEntry>(slotTable + i * SlotEntrySize);
            if (slot.BlueprintId == blueprintId)
            {
                slotIndex = i;
                return true;
            }
        }

        slotIndex = -1;
        return false;
    }

    /// <summary>
    /// A3 / <c>D1′</c> — the <see cref="OccurrenceKind"/> declared for the slot at
    /// <paramref name="slotIndex"/>, read from its nibble in
    /// <see cref="BlueprintBlackboardHeader.Reserved"/>.
    ///
    /// <para>⭐ <b>Costs no extra fetch on the walk</b> — every walker already holds the header
    /// (<c>BlueprintTickSystem.cs:79, 145, 211, 317</c>).</para>
    ///
    /// <para>⚠ Returns <see cref="OccurrenceKind.Invalid"/> rather than throwing for an index outside
    /// the nibble array: a READ on a hot walk must never throw, and <c>Invalid</c> is already this
    /// scheme's word for <i>"nobody declared one"</i>. ⛔ The asymmetry with
    /// <see cref="SetSlotKind"/> is deliberate — a WRITE out of range would silently lose the
    /// declaration, so that one throws.</para>
    /// </summary>
    public static OccurrenceKind GetSlotKind(byte* memory, int slotIndex)
    {
        if ((uint)slotIndex >= MaxKindSlots) return OccurrenceKind.Invalid;

        ref var header = ref Unsafe.AsRef<BlueprintBlackboardHeader>(memory);
        return (OccurrenceKind)(byte)((header.Reserved >> (slotIndex * 4)) & 0xFUL);
    }

    /// <summary>
    /// A3 / <c>D1′</c> — declares the <see cref="OccurrenceKind"/> of the slot at
    /// <paramref name="slotIndex"/>. ⭐ Called at ATTACH time, never per tick.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="slotIndex"/> is outside the nibble array (see <see cref="MaxKindSlots"/>), or
    /// <paramref name="kind"/> does not fit in 4 bits. ⛔ Both would otherwise lose the declaration
    /// silently, and a slot that reads <c>Invalid</c> when something DID declare it is exactly the
    /// failure this scheme exists to prevent.
    /// </exception>
    public static void SetSlotKind(byte* memory, int slotIndex, OccurrenceKind kind)
    {
        if ((uint)slotIndex >= MaxKindSlots)
            throw new ArgumentOutOfRangeException(nameof(slotIndex), slotIndex,
                $"The occurrence-kind nibble array addresses {MaxKindSlots} slots; a tier with more " +
                "slots must widen the scheme deliberately (D1' in DESIGN_Occurrence_Scoped_Storage §13).");

        if ((uint)kind > MaxKind)
            throw new ArgumentOutOfRangeException(nameof(kind), kind,
                $"OccurrenceKind is stored in 4 bits; values above {MaxKind} cannot be represented.");

        ref var header = ref Unsafe.AsRef<BlueprintBlackboardHeader>(memory);
        int shift = slotIndex * 4;
        header.Reserved = (header.Reserved & ~(0xFUL << shift)) | ((ulong)kind << shift);
    }

    /// <summary>
    /// The <see cref="OccurrenceKind"/> declared for the slot holding <paramref name="blueprintId"/>,
    /// or <see cref="OccurrenceKind.Invalid"/> when there is no such slot — ⚠ the two are deliberately
    /// NOT distinguished, because a caller that cares has already resolved the slot.
    /// </summary>
    public static OccurrenceKind GetKindOf(byte* memory, int blueprintId)
        => TryGetSlotIndex(memory, blueprintId, out int slotIndex)
            ? GetSlotKind(memory, slotIndex)
            : OccurrenceKind.Invalid;

    /// <summary>
    /// Allocates a payload slot for <paramref name="blueprintId"/>.
    /// Tries the free list first, falls back to bump allocation.
    /// Returns false if no slot or no payload space is available.
    ///
    /// <para>⚠ <b>This overload declares no <see cref="OccurrenceKind"/></b>, so the slot reads
    /// <see cref="OccurrenceKind.Invalid"/>. ⭐ Prefer the overload that takes one — a slot nothing
    /// declared is invisible to <c>O0</c>'s walker by design.</para>
    /// </summary>
    public static bool TryAttach(
        byte*  memory,
        int    blueprintId,
        int    requestedSize,
        ulong  structureHash,
        out int payloadOffset)
        => TryAttach(memory, blueprintId, requestedSize, structureHash, OccurrenceKind.Invalid, out payloadOffset);

    /// <summary>
    /// Allocates a payload slot for <paramref name="blueprintId"/> and DECLARES its
    /// <paramref name="kind"/> (A3 / <c>D1′</c>).
    /// Tries the free list first, falls back to bump allocation.
    /// Returns false if no slot or no payload space is available.
    ///
    /// <para>🔴 <b>The nibble is written on EVERY successful attach, including
    /// <see cref="OccurrenceKind.Invalid"/>.</b> ⛔ Not an optimisation to skip: leaving it alone
    /// would let a fresh slot INHERIT whatever the previous occupant of that index declared, and
    /// <c>Invalid</c> must mean <i>"nobody declared one"</i> rather than <i>"nobody declared one
    /// recently"</i>.</para>
    /// </summary>
    public static bool TryAttach(
        byte*  memory,
        int    blueprintId,
        int    requestedSize,
        ulong  structureHash,
        OccurrenceKind kind,
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

        // FC-2/LV-1b (List Variables review F2, memory safety): ZERO the allocated payload. A block
        // taken from the free list still holds the PREVIOUS occupant's bytes (TryDetach returns
        // payload without clearing it), and nothing on the manifest/partition rail runs a
        // definition's init on attach -- a stale non-zero value read as a fixed-list `Count` would
        // drive an unbounded [InlineArray] indexer read (no bounds check). Zeroing here, at the
        // single allocator choke point, makes EVERY slot kind (blueprint state, BTree stateful
        // working state, shared slots) start from default(T) on fresh AND reused attaches alike.
        // Attach happens at provisioning/reload time, never per-tick -- the InitBlock is cheap.
        Unsafe.InitBlock(memory + allocatedOffset, 0, (uint)alignedSize);

        int slotIndex = header.SlotCount;
        ref var slot = ref Unsafe.AsRef<BlueprintSlotEntry>(slotTable + slotIndex * SlotEntrySize);
        slot.BlueprintId     = blueprintId;
        slot.InstanceVersion = 1;
        slot.PayloadOffset   = (ushort)allocatedOffset;
        slot.PayloadSize     = (ushort)alignedSize;
        slot.StructureHash   = (uint)structureHash; // Lower 32 bits -- DEBT-014

        // A3/D1': declare the kind for THIS index. Always written (see the overload's remarks) so a
        // reused index can never inherit the previous occupant's declaration.
        SetSlotKind(memory, slotIndex, kind);

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

            // H2 (A3/D1'): the OccurrenceKind nibble array is indexed by SLOT INDEX, so it must be
            // compacted in LOCKSTEP with the table above -- otherwise the moved entry keeps the
            // DETACHED slot's kind and every occurrence from the hole onward is silently mislabelled.
            SetSlotKind(memory, foundIndex, GetSlotKind(memory, lastIndex));
        }

        // Clear the (now duplicated) last slot
        ref var clearedSlot = ref Unsafe.AsRef<BlueprintSlotEntry>(slotTable + lastIndex * SlotEntrySize);
        clearedSlot = default;

        // H2 (A3/D1'): ...and CLEAR the vacated tail nibble. The `clearedSlot = default` above zeroes
        // the duplicated ENTRY, but that write cannot reach the header, so without this the stale
        // kind survives at lastIndex for the next attach to inherit.
        SetSlotKind(memory, lastIndex, OccurrenceKind.Invalid);

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

        // H1 (A3/D1'): carry the OccurrenceKind nibble array across the promotion.
        // Initialize() above zeroed the destination and then set eight header fields explicitly --
        // Reserved is NOT among them -- so without this line EVERY tier upgrade silently zeroes the
        // whole array while entries and payloads copy correctly, and all occurrences read kind 0.
        // Slot ORDER is preserved (i -> i) directly above, so a whole-word copy is exactly right, and
        // it covers all THREE production promotion sites at once because they all funnel through here
        // (BehaviorIngressSystem.UpgradeTier, BlueprintMaintenanceSystem, EntityBlueprintsPanel).
        dstHeader.Reserved         = srcHeader.Reserved;

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

    /// <summary>
    /// ⭐⭐⭐ <b><c>CE-318</c> — THE PAYLOAD BYTES ONE OCCURRENCE OF <paramref name="requestedSize"/>
    /// COSTS. ⛔ It does NOT include the slot entry, and that is the whole point of this method
    /// existing.</b> 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §31.20.
    ///
    /// <para>🔴 <b>The defect it closes, and it is arithmetic rather than judgement.</b> Six demand
    /// sites each wrote <c>AlignUp(bytes, Alignment) + SlotEntrySize</c> and compared the result against
    /// <c>PayloadSize</c> / <c>PayloadFree</c>. ⛔ <b>That charges the same 16 bytes twice</b>, because
    /// the slot table is carved out of the store ONCE, UP FRONT — <see cref="Initialize"/> computes
    /// <c>payloadStart = sizeof(header) + maxSlots × SlotEntrySize</c> and seeds
    /// <c>PayloadFree = totalSize − payloadStart</c>. ⇒ the payload figure the demand is compared
    /// against has ALREADY had every slot entry removed from it.</para>
    ///
    /// <para>⭐⭐ <b>The allocator's own arithmetic is the specification, and it charges the two axes
    /// SEPARATELY</b> — <see cref="TryAttach"/> tests the slot axis at
    /// <c>SlotCount >= MaxSlots</c>, tests the payload axis at <c>alignedSize > PayloadFree</c>, and
    /// deducts <c>alignedSize</c> ALONE. ⇒ a demand that adds the entry to the payload is describing an
    /// allocator that does not exist.</para>
    ///
    /// <para>⚠ <b>CONSERVATIVE, NEVER UNSAFE.</b> The old form over-reserved, so nothing could
    /// overflow; the cost was spurious tier promotions and the memory they carry. 📐 Measured: an HSM
    /// entity with a 128-byte instance and root params computed <c>40 + 144 = 184 > 176</c> and promoted
    /// 256 → 1024 — <b>missing by 8 bytes</b>. Under the allocator's arithmetic it needs
    /// <c>24 + 128 = 152 ≤ 176</c> and fits, turning a +640 B cost into a −128 B saving.</para>
    ///
    /// <para>⛔⛔ <b>The slot AXIS is still counted, and callers must keep counting it.</b> This method
    /// deliberately answers only half the demand: every caller pairs it with a <c>requiredSlots</c>
    /// increment, and <c>BlueprintTierTable.Select</c> takes both. ⚠ Dropping the entry from the
    /// payload is correct ONLY because the slot count is checked on its own axis.</para>
    /// </summary>
    public static int PayloadCost(int requestedSize)
        => requestedSize <= 0 ? 0 : AlignUp(requestedSize, Alignment);

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

