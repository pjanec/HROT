using Fdp.Core;
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
/// <para>⛔⛔ <b>THE LIFETIME RULE — the real one, stated once here instead of assumed 20 times.</b>
/// The danger was never the GC. It is:
/// <list type="number">
///   <item><b>Chunk decommit</b> — <c>NativeChunkTable</c> decommits chunks (<c>:272</c>, <c>:310</c>).</item>
///   <item><b>Tier swap</b> — a promotion adds the larger component and removes the smaller
///   (<c>BehaviorIngressSystem.UpgradeTier</c>, <c>BlueprintMaintenanceSystem</c>,
///   <c>EntityBlueprintsPanel</c>), so the old pointer is stale the moment it returns.</item>
/// </list>
/// ⇒ ⭐ <b>Use the pointer within the call that obtained it. ⛔ Never store it across a frame, and
/// ⛔ never hold it across anything that can add or remove a component on this entity.</b> That is
/// the same rule today's <c>fixed</c> pointers already live under — <c>BlueprintSharedState</c>'s
/// by-value accessors exist precisely because of it.</para>
///
/// <para>⚠ <b>This is a MECHANICAL seam, not a behaviour change.</b> The probe order (16384 → 4096 →
/// 1024) is preserved exactly, including the rule that an entity carries at most one tier so the
/// first match is authoritative.</para>
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
    /// <see langword="true"/> when the entity carries any occurrence store.
    /// ⭐ Prefer <see cref="TryGetStore"/> when the memory is wanted — this exists for the sites that
    /// genuinely only ask the question (e.g. a translator's "should I serialise this entity?").
    /// </summary>
    public static bool HasStore(EntityRepository world, Entity entity)
        => BlueprintTierTable.Of(world, entity) is not null;

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
        byte* mem = TryGetStore(world, entity, out _);
        if (mem == null)
        {
            payload = null;
            return false;
        }

        if (!BlueprintBlackboardPartitions.TryGetSlotOffset(mem, slotKey, out int payloadOffset))
        {
            payload = null;
            return false;
        }

        payload = mem + payloadOffset;
        return true;
    }
}
