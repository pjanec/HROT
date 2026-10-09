using System;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Blueprints.Components;

namespace Fdp.Toolkit.Blueprints.Partitioning;

/// <summary>
/// A2 (<c>PLAN_Occurrence_Storage_Build</c>) — THE one place that answers
/// <i>"where does this entity's occurrence store live, and how big is it?"</i>.
///
/// <para><b>What it replaces.</b> 📐 Measured <c>2026-09-20</c>: <b>20 sites across 11 production
/// files</b> hand-rolled the same three-way ladder — <c>HasComponent&lt;…16384&gt;</c> →
/// <c>GetComponentRW</c> → <c>fixed</c>, then the same again for 4096 and 1024 — plus <b>2 more
/// copies emitted into generated code</b> by <c>BTreeBridgeEmitCore</c> (<c>:650</c>, <c>:726</c>).
/// Adding a fourth tier (<c>O3b</c>'s 256) means a fourth arm in every one of them.</para>
///
/// <para>⭐⭐⭐ <b>Why it was duplicated, and why this seam can exist at all.</b> Every call site
/// inlined the ladder because <c>fixed</c> is a SCOPE and a <c>byte*</c> appears unable to outlive
/// it — so "resolve once, use later" looked impossible. 📐 <b>That reading is wrong, and the
/// allocator proves it</b>: unmanaged component storage is <b>native</b> memory, reserved with
/// <c>VirtualAlloc</c>/<c>mmap</c> (<c>NativeMemoryAllocator</c> → <c>WindowsVirtualMemoryBackend:35</c>
/// / <c>PosixVirtualMemoryBackend:55</c>) and handed out as <c>ref _chunks[chunk][local]</c>
/// (<c>NativeChunkTable.GetRefRW:163</c>). ⇒ <b>the GC never moves it, so there is nothing to pin</b>;
/// <c>fixed</c> at those 20 sites is the language formality for converting a fixed-size buffer to a
/// pointer, NOT a safety measure. A pointer may therefore cross this call boundary.</para>
///
/// <para>⭐⭐⭐ <b><c>CE-3137</c> U-0 (§34, <c>R-236</c>) — THE STORE IS MULTI-BLOCK AND NEVER MOVES A SLOT.</b> A
/// unit's store is every tier component it carries (at most one of each: 256 / 1024 / 4096 / 16384 ⇒ up to
/// 4 blocks, 47 slots). Growth APPENDS the next absent tier as another block; an allocated slot stays where
/// it is for its whole life. ⇒ a slot's payload pointer is valid until THAT slot is detached (or the entity
/// destroyed), even across an attach that grows the store mid-tick — FDP's add writes only the new type's
/// table and a mask bit (<c>EntityRepository.cs:958-975</c>) and is allowed mid-phase (<c>:1189</c>).</para>
///
/// <para>⛔ <b>What still shifts:</b> a slot INDEX, on a <c>TryDetach</c> in the same block (the slot table is
/// dense-compacted). ⛔ <b>What is no longer true:</b> "one tier per entity" and the copy-promotion — both
/// retired by U-0. <c>Chunk decommit</c> (<c>NativeChunkTable</c> <c>:272</c>, <c>:310</c>) still frees a whole
/// chunk's memory, so a pointer must not outlive its entity.</para>
///
/// <para>⭐ <b>The key-based API is the production surface</b>: <see cref="TryFindSlot"/> /
/// <see cref="TryAttachSlot"/> / <see cref="TryDetachSlot"/> / <see cref="GetBlocks"/> (and their read-only /
/// view twins). ⚠ <see cref="TryGetStore"/> and its twins return ONE block — the largest — and are kept for
/// tests and diagnostics only; rail <c>U0_R0</c> fails if a production file calls them.</para>
/// </summary>
public static unsafe class OccurrenceStoreAccess
{
    /// <summary>
    /// Resolves the entity's occurrence-store memory, or <see langword="null"/> when it has no tier.
    ///
    /// <para>⛔ Read the LIFETIME RULE on the class before storing the result anywhere.</para>
    /// </summary>
    /// <param name="totalSize">
    /// The tier's <c>TotalSize</c> (1024 / 4096 / 16384), or <c>0</c> when there is no store.
    /// ⭐ This is the whole component, header included — it is what
    /// <c>BlueprintBlackboardPartitions</c> expects, not the payload size.
    /// </param>
    /// <returns><c>null</c> when the entity carries no tier component — ⛔ a normal, expected answer.</returns>
    public static byte* TryGetStore(EntityRepository world, Entity entity, out int totalSize)
    {
        // ⭐ O3a: the ladder is BlueprintTierTable.Descending. The probe order — and the reason it is
        //   load-bearing — is unchanged; it simply is not spelled here any more.
        var spec = BlueprintTierTable.Of(world, entity);
        if (spec is null)
        {
            totalSize = 0;
            return null;
        }

        totalSize = spec.TotalSize;
        return spec.Memory(world, entity);
    }

    /// <summary>
    /// ⭐⭐ The READ-ONLY resolution — identical to <see cref="TryGetStore"/> except that it reads
    /// through <c>GetComponentRO</c>.
    ///
    /// <para>🔴 <b>This overload is NOT a stylistic nicety, and using the wrong one is a real
    /// behaviour change.</b> 📐 <c>NativeChunkTable.GetRefRW</c> WRITES the chunk version
    /// (<c>:158-161</c>) while <c>GetRefRO</c> explicitly does not (<c>:166-167</c>, "Does not update
    /// version"), and <c>EntityRepository.DeltaQuery</c> reads those versions. ⇒ resolving a
    /// read-only consumer — a scenario translator, a renderer, a debug dump — through the RW form
    /// would mark every blackboard chunk dirty on every pass, changing what delta queries and
    /// replication observe.</para>
    ///
    /// <para>⭐ <b>Rule: if the caller only READS the bytes, call this.</b> The two are otherwise
    /// identical, including the probe order.</para>
    /// </summary>
    public static byte* TryGetStoreReadOnly(EntityRepository world, Entity entity, out int totalSize)
    {
        var spec = BlueprintTierTable.Of(world, entity);
        if (spec is null)
        {
            totalSize = 0;
            return null;
        }

        totalSize = spec.TotalSize;
        return spec.MemoryReadOnly(world, entity);
    }

    /// <summary>
    /// ⭐⭐ <b>The <see cref="ISimulationView"/> form of <see cref="TryGetStoreReadOnly"/>, for the
    /// surfaces that never hold an <see cref="EntityRepository"/>: gizmos, renderers, the debug API.</b>
    ///
    /// <para>⛔⛔ <b>Why this is not "just cast the view".</b> A view may be a read-only SNAPSHOT, and
    /// <c>BlueprintTierSpec</c> exists precisely because the component fetch differs between the two
    /// (<c>BlueprintTierSpec.cs:39</c> names the debug surfaces as the reason). ⇒ casting would either
    /// throw on a snapshot or silently read the live world while rendering a snapshot frame.</para>
    ///
    /// <para>⚠ <b><c>P3-C</c> is what made this load-bearing.</b> Before it, a gizmo read behaviour
    /// params straight off <c>BrainBlackboard</c> — an ordinary component every view can serve. The
    /// params now live in the store, so every read-only surface needs this door.</para>
    /// </summary>
    public static byte* TryGetStoreInView(ISimulationView view, Entity entity, out int totalSize)
    {
        var spec = BlueprintTierTable.OfInView(view, entity);
        if (spec is null)
        {
            totalSize = 0;
            return null;
        }

        totalSize = spec.TotalSize;
        return spec.MemoryReadOnlyInView(view, entity);
    }

    /// <summary>
    /// <see langword="true"/> when the entity carries any occurrence store.
    /// ⭐ Prefer <see cref="TryGetStore"/> when the memory is wanted — this exists for the sites that
    /// genuinely only ask the question (e.g. a translator's "should I serialise this entity?").
    /// </summary>
    public static bool HasStore(EntityRepository world, Entity entity)
        => PresentTiers(world, entity) != 0;

    /// <summary>
    /// The entity's tier <c>TotalSize</c>, or <c>0</c> when it has no store — the size-only half of
    /// the same ladder (<c>BehaviorIngressSystem.GetCurrentTierSize</c> was a verbatim copy).
    /// </summary>
    public static int GetStoreSize(EntityRepository world, Entity entity)
        => BlueprintTierTable.Of(world, entity)?.TotalSize ?? 0;

    /// <summary>
    /// Resolves a single occurrence's payload within the entity's store — the seam
    /// <c>DESIGN_Occurrence_Scoped_Storage</c> §5 classes 4/5/6 all want, so that
    /// <i>"where are this occurrence's bytes"</i> is answered in ONE place rather than at every
    /// consumer.
    ///
    /// <para>⛔ Read the LIFETIME RULE on the class before storing <paramref name="payload"/>.</para>
    /// </summary>
    /// <param name="slotKey">From <c>StatefulBTreeActionBinder.ComputeStatefulSlotKey</c> /
    /// <c>ComputeOccurrenceSlotKey</c> (A1). ⛔ Never a hand-rolled FNV.</param>
    /// <param name="payload">The occurrence's payload base, or <see langword="null"/> when not found.</param>
    /// <returns>
    /// <see langword="false"/> when the entity has no store OR the slot is not allocated — ⚠ the two
    /// are deliberately NOT distinguished, because every caller this replaces treats them alike.
    /// </returns>
    public static bool TryResolveOccurrence(
        EntityRepository world, Entity entity, int slotKey, out byte* payload)
    {
        // ⭐ CE-3137 U-0: any block — the generated thunks call this, so they are multi-block with no re-emit.
        if (!TryFindSlot(world, entity, slotKey, out byte* block, out int payloadOffset, out _))
        {
            payload = null;
            return false;
        }

        payload = block + payloadOffset;
        return true;
    }

    // ═══ CE-3137 U-0 — THE MULTI-BLOCK SURFACE (§34) ══════════════════════════════════════════════════

    /// <summary>
    /// Bit <c>i</c> set ⇔ the unit carries <c>BlueprintTierTable.Descending[i]</c>. ⭐ ONE <c>IsAlive</c> + ONE
    /// component-mask read for all four tiers (Q87 §3b: 10–18 ns, against ~130 ns for the delegate probe).
    /// </summary>
    public static int PresentTiers(EntityRepository world, Entity entity)
    {
        if (!world.IsAlive(entity)) return 0;
        ref var mask = ref world.GetComponentMask(entity.Index);
        var d = BlueprintTierTable.Descending;
        int bits = 0;
        for (int i = 0; i < d.Count; i++)
            if (mask.IsSet(d[i].ComponentId)) bits |= 1 << i;
        return bits;
    }

    /// <summary>
    /// ⭐ <c>CE-3137</c> U-0 — for a walker that visits units through the per-tier queries smallest-first
    /// (<c>BlueprintTierTable.BuildTierQueries</c>): <see langword="true"/> when <paramref name="ascendingIndex"/> is
    /// the FIRST tier this unit carries, so the walker runs the unit ONCE although a multi-block unit sits in
    /// several queries. ⭐ A unit-level walker (the brain tick) needs this; a slot-level walker (the blueprint
    /// tick, which only reads its own block) does not.
    /// </summary>
    public static bool IsFirstVisit(EntityRepository world, Entity entity, int ascendingIndex)
    {
        int bits = PresentTiers(world, entity);
        int n = BlueprintTierTable.Descending.Count;
        for (int j = 0; j < ascendingIndex; j++)
            if ((bits & (1 << (n - 1 - j))) != 0) return false;
        return true;
    }

    /// <summary>Every block the unit carries, largest first, resolved READ-WRITE. ⛔ Marks each block's chunk
    /// version — a caller that only reads uses <see cref="GetBlocksReadOnly"/>.</summary>
    public static int GetBlocks(EntityRepository world, Entity entity, out StoreBlocks blocks)
    {
        blocks = default;
        int bits = PresentTiers(world, entity);
        var d = BlueprintTierTable.Descending;
        for (int i = 0; bits != 0; i++, bits >>= 1)
            if ((bits & 1) != 0) blocks.Add(d[i].Memory(world, entity), d[i].TotalSize);
        return blocks.Count;
    }

    /// <summary>Every block, largest first, READ-ONLY (does not stamp chunk versions).</summary>
    public static int GetBlocksReadOnly(EntityRepository world, Entity entity, out StoreBlocks blocks)
    {
        blocks = default;
        int bits = PresentTiers(world, entity);
        var d = BlueprintTierTable.Descending;
        for (int i = 0; bits != 0; i++, bits >>= 1)
            if ((bits & 1) != 0) blocks.Add(d[i].MemoryReadOnly(world, entity), d[i].TotalSize);
        return blocks.Count;
    }

    /// <summary>Every block, largest first, through a VIEW (a snapshot or replay frame). ⭐ An old single-tier
    /// recording is simply a one-block store.</summary>
    public static int GetBlocksInView(ISimulationView view, Entity entity, out StoreBlocks blocks)
    {
        blocks = default;
        var d = BlueprintTierTable.Descending;
        for (int i = 0; i < d.Count; i++)
            if (d[i].HasInView(view, entity)) blocks.Add(d[i].MemoryReadOnlyInView(view, entity), d[i].TotalSize);
        return blocks.Count;
    }

    /// <summary>
    /// ⭐⭐ THE lookup: the block holding <paramref name="slotKey"/>, its payload offset and stored hash, with the
    /// block resolved READ-WRITE. ⭐ Scans read-only and takes RW only on the hit block (§34, Q87 §3b ③), so a
    /// lookup does not stamp every block's chunk version; a one-block store resolves RW directly, as before.
    /// </summary>
    public static bool TryFindSlot(
        EntityRepository world, Entity entity, int slotKey,
        out byte* block, out int payloadOffset, out uint structureHash)
    {
        int bits = PresentTiers(world, entity);
        var d = BlueprintTierTable.Descending;
        bool single = bits != 0 && (bits & (bits - 1)) == 0;
        for (int i = 0; bits != 0; i++, bits >>= 1)
        {
            if ((bits & 1) == 0) continue;
            if (single)
            {
                byte* rw = d[i].Memory(world, entity);
                if (BlueprintBlackboardPartitions.TryGetSlotOffset(rw, slotKey, out payloadOffset, out structureHash))
                { block = rw; return true; }
                break;
            }
            byte* ro = d[i].MemoryReadOnly(world, entity);
            if (BlueprintBlackboardPartitions.TryGetSlotOffset(ro, slotKey, out payloadOffset, out structureHash))
            { block = d[i].Memory(world, entity); return true; }
        }
        block = null; payloadOffset = 0; structureHash = 0;
        return false;
    }

    /// <summary>
    /// The lookup that also yields the slot's INDEX in its block — for callers that read the entry itself
    /// (its <c>PayloadSize</c>). ⛔ The index shifts on a detach in the same block; use it within the call.
    /// </summary>
    public static bool TryFindSlotIndex(
        EntityRepository world, Entity entity, int slotKey, out byte* block, out int slotIndex)
    {
        int bits = PresentTiers(world, entity);
        var d = BlueprintTierTable.Descending;
        bool single = bits != 0 && (bits & (bits - 1)) == 0;
        for (int i = 0; bits != 0; i++, bits >>= 1)
        {
            if ((bits & 1) == 0) continue;
            byte* mem = single ? d[i].Memory(world, entity) : d[i].MemoryReadOnly(world, entity);
            if (BlueprintBlackboardPartitions.TryGetSlotIndex(mem, slotKey, out slotIndex))
            { block = single ? mem : d[i].Memory(world, entity); return true; }
            if (single) break;
        }
        block = null; slotIndex = -1;
        return false;
    }

    /// <summary>The read-only lookup (no chunk-version stamp).</summary>
    public static bool TryFindSlotReadOnly(
        EntityRepository world, Entity entity, int slotKey,
        out byte* block, out int payloadOffset, out uint structureHash)
    {
        GetBlocksReadOnly(world, entity, out var blocks);
        return blocks.TryFind(slotKey, out block, out payloadOffset, out structureHash);
    }

    /// <summary>The lookup through a view.</summary>
    public static bool TryFindSlotInView(
        ISimulationView view, Entity entity, int slotKey,
        out byte* block, out int payloadOffset, out uint structureHash)
    {
        GetBlocksInView(view, entity, out var blocks);
        return blocks.TryFind(slotKey, out block, out payloadOffset, out structureHash);
    }

    /// <summary>Detaches <paramref name="slotKey"/> from whichever block holds it.
    /// <returns><see langword="false"/> when no block holds it.</returns></summary>
    public static bool TryDetachSlot(EntityRepository world, Entity entity, int slotKey)
        => TryFindSlot(world, entity, slotKey, out byte* block, out _, out _)
           && BlueprintBlackboardPartitions.TryDetach(block, slotKey);

    /// <summary>
    /// ⭐⭐⭐ THE attach: into the first block (largest first) with room, else into a newly APPENDED block — the
    /// smallest absent registered tier that holds it (§34, <c>R-236</c>). Nothing already allocated moves, so
    /// a pointer the caller (or anyone up its stack) holds stays valid; that is what makes a mid-tick attach
    /// safe. ⛔ The caller must already know <paramref name="slotKey"/> is not attached (every caller looks it
    /// up first); a duplicate key would shadow.
    /// </summary>
    /// <returns><see langword="false"/> only when every block is full AND no absent tier can take it — the
    /// whole ladder (47 slots) is in use. ⛔ Callers must surface that, never swallow it.</returns>
    public static bool TryAttachSlot(
        EntityRepository world, Entity entity, int slotKey, int requestedSize, ulong structureHash,
        OccurrenceKind kind, out byte* block, out int payloadOffset)
    {
        int bits = PresentTiers(world, entity);
        var d = BlueprintTierTable.Descending;
        for (int i = 0, b = bits; b != 0; i++, b >>= 1)
        {
            if ((b & 1) == 0) continue;
            byte* mem = d[i].Memory(world, entity);
            if (BlueprintBlackboardPartitions.TryAttach(mem, slotKey, requestedSize, structureHash, kind, out payloadOffset))
            { block = mem; return true; }
        }

        var spec = SmallestAbsentFitting(world, bits, BlueprintBlackboardPartitions.PayloadCost(requestedSize), 1);
        if (spec != null)
        {
            byte* mem = AddBlock(world, entity, spec);
            if (BlueprintBlackboardPartitions.TryAttach(mem, slotKey, requestedSize, structureHash, kind, out payloadOffset))
            { block = mem; return true; }
        }

        block = null; payloadOffset = 0;
        return false;
    }

    /// <summary>
    /// The store's totals across every block (read-only): capacity (<c>PayloadSize</c>, <c>MaxSlots</c>) and use
    /// (<c>PayloadFree</c>, <c>SlotCount</c>), summed. ⚠ A slot cannot span blocks, so a sum is an ESTIMATE of
    /// what fits; <see cref="TryAttachSlot"/> still appends on a miss.
    /// </summary>
    public static StoreMeasure Measure(EntityRepository world, Entity entity)
    {
        GetBlocksReadOnly(world, entity, out var blocks);
        var m = new StoreMeasure { Blocks = blocks.Count };
        for (int i = 0; i < blocks.Count; i++)
        {
            ref var h = ref Unsafe.AsRef<BlueprintBlackboardHeader>(blocks.Memory(i));
            m.PayloadSize += h.PayloadSize;
            m.PayloadFree += h.PayloadFree;
            m.MaxSlots    += h.MaxSlots;
            m.SlotCount   += h.SlotCount;
        }
        return m;
    }

    /// <summary>
    /// ⭐ THE replacement for the copy-promotion: append ONE block for a shortfall of
    /// <paramref name="payloadShort"/> bytes and <paramref name="slotsShort"/> slot entries — the smallest absent
    /// registered tier that covers it, else the largest absent one.
    /// </summary>
    /// <returns>Whether a block was appended (<see langword="false"/> when every tier is already carried or
    /// unregistered).</returns>
    public static bool AppendBlockFor(EntityRepository world, Entity entity, int payloadShort, int slotsShort)
    {
        int bits = PresentTiers(world, entity);
        var spec = SmallestAbsentFitting(world, bits, Math.Max(0, payloadShort), Math.Max(1, slotsShort))
                   ?? LargestAbsent(world, bits);
        if (spec == null) return false;
        AddBlock(world, entity, spec);
        return true;
    }

    /// <summary>
    /// ⭐ Pre-sizing for a known demand: when the blocks' combined FREE room cannot take <paramref name="payload"/>
    /// bytes and <paramref name="slots"/> slot entries, append one block for the shortfall.
    /// </summary>
    /// <returns>Whether a block was appended.</returns>
    public static bool EnsureRoom(EntityRepository world, Entity entity, int payload, int slots)
    {
        var m = Measure(world, entity);
        if (payload <= m.PayloadFree && slots <= m.FreeSlots) return false;
        return AppendBlockFor(world, entity, payload - m.PayloadFree, slots - m.FreeSlots);
    }

    /// <summary>The tier whose block starts at <paramref name="block"/>, or <see langword="null"/>.</summary>
    public static BlueprintTierSpec? TierOf(EntityRepository world, Entity entity, byte* block)
    {
        int bits = PresentTiers(world, entity);
        var d = BlueprintTierTable.Descending;
        for (int i = 0; bits != 0; i++, bits >>= 1)
            if ((bits & 1) != 0 && d[i].MemoryReadOnly(world, entity) == block) return d[i];
        return null;
    }

    /// <summary>Adds <paramref name="spec"/>'s component and initialises its allocator. ⛔ Never for a tier the
    /// unit already carries.</summary>
    public static byte* AddBlock(EntityRepository world, Entity entity, BlueprintTierSpec spec)
    {
        spec.Add(world, entity);
        byte* mem = spec.Memory(world, entity);
        BlueprintBlackboardPartitions.Initialize(mem, spec.TotalSize, (byte)spec.MaxSlots);
        return mem;
    }

    private static BlueprintTierSpec? SmallestAbsentFitting(EntityRepository world, int presentBits, int payload, int slots)
    {
        var d = BlueprintTierTable.Descending;
        for (int i = d.Count - 1; i >= 0; i--)   // Descending reversed = smallest first
        {
            if ((presentBits & (1 << i)) != 0) continue;
            var spec = d[i];
            if (!spec.IsRegistered(world)) continue;
            if (payload <= spec.PayloadSize && slots <= spec.MaxSlots) return spec;
        }
        return null;
    }

    private static BlueprintTierSpec? LargestAbsent(EntityRepository world, int presentBits)
    {
        var d = BlueprintTierTable.Descending;
        for (int i = 0; i < d.Count; i++)
            if ((presentBits & (1 << i)) == 0 && d[i].IsRegistered(world)) return d[i];
        return null;
    }
}

/// <summary>⭐ <c>CE-3137</c> U-0 — a store's totals across its blocks (<see cref="OccurrenceStoreAccess.Measure"/>).</summary>
public struct StoreMeasure
{
    /// <summary>How many blocks.</summary>
    public int Blocks;
    /// <summary>Summed payload capacity.</summary>
    public int PayloadSize;
    /// <summary>Summed free payload.</summary>
    public int PayloadFree;
    /// <summary>Summed slot-table capacity.</summary>
    public int MaxSlots;
    /// <summary>Summed slots in use.</summary>
    public int SlotCount;
    /// <summary>Free slot entries.</summary>
    public readonly int FreeSlots => MaxSlots - SlotCount;
    /// <summary>Payload in use.</summary>
    public readonly int PayloadUsed => PayloadSize - PayloadFree;
}

/// <summary>
/// ⭐ <c>CE-3137</c> U-0 — a unit's blocks (≤ 4), largest first, as resolved by
/// <see cref="OccurrenceStoreAccess.GetBlocks"/>. Unmanaged, so it lives on the stack. ⛔ Same lifetime as the
/// pointers inside: valid until the entity is destroyed; a block is never removed while the unit lives.
/// </summary>
public unsafe struct StoreBlocks
{
    /// <summary>The ladder's size: one block per tier.</summary>
    public const int Max = 4;

    private fixed long _memory[Max];
    private fixed int _totalSize[Max];

    /// <summary>How many blocks the unit carries.</summary>
    public int Count;

    /// <summary>Block <paramref name="i"/>'s memory (header first).</summary>
    public readonly byte* Memory(int i) => (byte*)_memory[i];

    /// <summary>Block <paramref name="i"/>'s whole size, header included.</summary>
    public readonly int TotalSize(int i) => _totalSize[i];

    internal void Add(byte* memory, int totalSize)
    {
        _memory[Count] = (long)memory;
        _totalSize[Count] = totalSize;
        Count++;
    }

    /// <summary>The block holding <paramref name="slotKey"/>, in block order.</summary>
    public readonly bool TryFind(int slotKey, out byte* block, out int payloadOffset, out uint structureHash)
    {
        for (int i = 0; i < Count; i++)
        {
            byte* mem = Memory(i);
            if (BlueprintBlackboardPartitions.TryGetSlotOffset(mem, slotKey, out payloadOffset, out structureHash))
            { block = mem; return true; }
        }
        block = null; payloadOffset = 0; structureHash = 0;
        return false;
    }
}
