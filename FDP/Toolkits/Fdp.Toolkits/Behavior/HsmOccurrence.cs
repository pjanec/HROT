using System;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Toolkit.Blueprints.Partitioning;
using Fhsm.Kernel.Data;

namespace Fdp.Toolkit.Behavior;

/// <summary>
/// ⭐⭐⭐ <b>THE HSM hosting lookup — one body, used by every emitted HSM thunk and by hand-written
/// hosts alike.</b> <c>O7</c> / <c>E3</c> — 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §24.
///
/// <para>🔴🔴 <b>What it replaces (<c>BP-297</c>).</b> <c>AiPrimitiveEmitter</c> emits all three HSM
/// thunks — action, activity, guard — as
/// <c>world.GetComponentRW&lt;Blackboard1024&gt;(bridge-&gt;Self)</c> followed by a hard-coded
/// <c>memory + 8</c>. That is <b>one working state per ENTITY</b>: two concurrently-active HSM regions
/// running the same asset write the same bytes, and nothing says so. ⭐ The BTree hosting path has been
/// occurrence-keyed since <c>S2</c> — the seam existed and the HSM path was never brought onto it.</para>
///
/// <para>⭐⭐ <b><c>O6</c> supplied the missing four bytes.</b> The kernel stamps
/// <see cref="HsmCommandWriter.OccurrenceRegionSlotIndex"/> and
/// <see cref="HsmCommandWriter.OccurrenceStateId"/> before every dispatch, so the thunk can key its own
/// storage. 🔒 <b>The kernel supplies IDENTITY, the thunk does the LOOKUP</b> — this type is the lookup,
/// and it lives on our side of the seam because the kernel must never learn about the allocator.</para>
///
/// <para>⚠ <b>This is the mirror of <see cref="HostedSubtree"/>, deliberately.</b> Same shape, same
/// lifetime rule, same loud failure on a missing slot — two hosting paradigms, ONE storage model.</para>
/// </summary>
public static unsafe class HsmOccurrence
{
    /// <summary>
    /// ⭐⭐ The slot key for the occurrence the kernel has just stamped on <paramref name="writer"/>.
    ///
    /// <para>⭐ <b>The emitter bakes the host and child ids as literals and calls this at runtime</b>,
    /// because the region and state are only known per dispatch. ⛔ There is exactly ONE key function,
    /// so a generated thunk and a hand-written host cannot disagree — the same <c>D3</c> rule
    /// <see cref="OccurrenceSlots.TreeStateKeyFor"/> follows.</para>
    ///
    /// <para>⛔ <b>Throws on an UNSTAMPED writer.</b> Reaching here with the sentinels means the thunk
    /// ran outside a kernel dispatch, and resolving that to <i>"region 0, state 0"</i> would hand it a
    /// real occurrence's bytes — a silent cross-occurrence alias, which is the exact failure this model
    /// exists to remove.</para>
    /// </summary>
    public static int KeyFor(Guid hostAssetId, Guid childAssetId, HsmCommandWriter* writer)
    {
        if (writer == null) throw new ArgumentNullException(nameof(writer));

        int region = writer->OccurrenceRegionSlotIndex;
        ushort state = writer->OccurrenceStateId;

        if (region == HsmCommandWriter.NoRegionSlot || state == HsmCommandWriter.NoStateId)
            throw new InvalidOperationException(
                "The HsmCommandWriter carries no occurrence stamp, so an HSM-hosted blueprint cannot " +
                "key its working state. The kernel stamps the (region, state) pair before every " +
                "dispatch (O6); reaching here without one means this thunk was invoked outside a " +
                "kernel dispatch.");

        return Shared.OccurrenceSlotKey.ComputeHsmStateKey(hostAssetId, region, state, childAssetId);
    }

    /// <summary>Payload bytes one HSM-hosted occurrence's working state costs, for a given state type.</summary>
    public static int PayloadSizeOf<TWorkingState>() where TWorkingState : unmanaged
        => sizeof(TWorkingState);

    /// <summary>
    /// ⭐⭐ The occurrence's working state, as a <c>ref</c> into the entity's occurrence store.
    ///
    /// <para>⛔ <b>Valid for the CALLING FRAME ONLY</b> — the seam's lifetime rule. Native ECS storage
    /// means a structural change can move the chunk, so it is resolved per dispatch and never cached.</para>
    ///
    /// <para>⛔⛔ <b>A missing slot is a HARD failure, not a silent one</b> (§19.6 ⑤). The alternative —
    /// returning a zeroed scratch — reads as <i>"the action just does nothing"</i>, which is precisely
    /// the silent slot miss <c>A1</c> exists to kill.</para>
    /// </summary>
    public static ref TWorkingState Resolve<TWorkingState>(
        EntityRepository world, Entity self, int slotKey)
        where TWorkingState : unmanaged
    {
        byte* store = OccurrenceStoreAccess.TryGetStore(world, self, out _);

        if (store == null)
            throw new InvalidOperationException(
                $"Entity {self} carries no occurrence store, so HSM-hosted slot {slotKey} cannot be " +
                "resolved. The hosting site's slot must be declared in the behaviour's stateful " +
                "manifest so BehaviorIngressSystem provisions it.");

        if (!BlueprintBlackboardPartitions.TryGetSlotOffset(store, slotKey, out int payloadOffset))
            throw new InvalidOperationException(
                $"Entity {self} has an occurrence store but no slot {slotKey} for this HSM-hosted " +
                "occurrence. Either the manifest does not declare it, or the hosting site computed a " +
                "different key than the one registered — see HsmOccurrence.KeyFor.");

        return ref Unsafe.AsRef<TWorkingState>(store + payloadOffset);
    }
}
