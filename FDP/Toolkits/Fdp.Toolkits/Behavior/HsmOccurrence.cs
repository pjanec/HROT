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
    /// <para>⭐ <b>The emitter bakes only the CHILD's asset id; the host comes from the instance
    /// pointer the kernel passes</b>, and the region and state from the stamp — all three per dispatch. ⛔ There is exactly ONE key function,
    /// so a generated thunk and a hand-written host cannot disagree — the same <c>D3</c> rule
    /// <see cref="OccurrenceSlots.TreeStateKeyFor"/> follows.</para>
    ///
    /// <para>⛔ <b>Throws on an UNSTAMPED writer.</b> Reaching here with the sentinels means the thunk
    /// ran outside a kernel dispatch, and resolving that to <i>"region 0, state 0"</i> would hand it a
    /// real occurrence's bytes — a silent cross-occurrence alias, which is the exact failure this model
    /// exists to remove.</para>
    /// </summary>
    public static int KeyFor(void* hsmInstance, Guid childAssetId, HsmCommandWriter* writer)
    {
        if (writer == null) throw new ArgumentNullException(nameof(writer));
        if (hsmInstance == null) throw new ArgumentNullException(nameof(hsmInstance));

        int region = writer->OccurrenceRegionSlotIndex;
        ushort state = writer->OccurrenceStateId;

        if (region == HsmCommandWriter.NoRegionSlot || state == HsmCommandWriter.NoStateId)
            throw new InvalidOperationException(
                "The HsmCommandWriter carries no occurrence stamp, so an HSM-hosted blueprint cannot " +
                "key its working state. The kernel stamps the (region, state) pair before every " +
                "dispatch (O6); reaching here without one means this thunk was invoked outside a " +
                "kernel dispatch.");

        // ⭐ The host identity comes from the instance the kernel just handed us — its header carries
        //   the HSM definition's StructureHash. No literal to bake, no new plumbing (§24.9).
        uint machineId = ((InstanceHeader*)hsmInstance)->MachineId;

        return Shared.OccurrenceSlotKey.ComputeHsmStateKey(machineId, region, state, childAssetId);
    }

    /// <summary>
    /// The same key from an explicit machine id — for hand-written hosts and for rails that want to
    /// state the host rather than construct an instance. ⛔ ONE key function underneath, always.
    /// </summary>
    public static int KeyFor(uint hostMachineId, Guid childAssetId, int regionSlotIndex, ushort stateId)
        => Shared.OccurrenceSlotKey.ComputeHsmStateKey(hostMachineId, regionSlotIndex, stateId, childAssetId);

    /// <summary>
    /// ⭐⭐⭐ <b>The inverse the debugger needs: which <c>(region, state)</c> does this slot key name?</b>
    /// <c>O7b-2</c> — 📄 §24.11.
    ///
    /// <para>⛔⛔ <b>The key is an FNV fold and CANNOT be inverted</b>, and there is nowhere to record
    /// the pair: <c>BlueprintSlotEntry</c> is <b>exactly 16 bytes with no padding</b>, and widening it
    /// changes every tier's capacity arithmetic — the one part of this store that is not additive.</para>
    ///
    /// <para>⭐⭐ <b>So this searches FORWARD instead.</b> Computing a key is ~20 operations; the space
    /// is tiny (regions are 2/4/8 by instance size, and state ids are small); and a hit is EXACT, never
    /// a guess — the key either equals the one the thunk computed or it does not. ⚠ It runs when a
    /// human inspects an entity, not per frame.</para>
    ///
    /// <para>⚠ <b>The honest limit:</b> a state id beyond <paramref name="maxStateId"/> is simply not
    /// found, and the caller labels it by its raw key. ⛔ It never mislabels — that is the property
    /// worth having.</para>
    /// </summary>
    public static bool TryDescribe(
        uint hostMachineId, Guid childAssetId, int slotKey,
        out int regionSlotIndex, out ushort stateId,
        int maxRegionSlots = 8, int maxStateId = 1024)
    {
        for (int r = 0; r < maxRegionSlots; r++)
        {
            for (int s = 0; s <= maxStateId; s++)
            {
                if (KeyFor(hostMachineId, childAssetId, r, (ushort)s) != slotKey) continue;
                regionSlotIndex = r;
                stateId = (ushort)s;
                return true;
            }
        }

        regionSlotIndex = -1;
        stateId = HsmCommandWriter.NoStateId;
        return false;
    }

    /// <summary>
    /// ⭐ The human-readable name of an occurrence, for the inspector. ⚠ One spelling, one place — a
    /// label invented at each call site is a label that drifts.
    /// </summary>
    public static string DescribeLabel(int regionSlotIndex, ushort stateId)
        => $"Region {regionSlotIndex} / State {stateId}";

    /// <summary>Payload bytes one HSM-hosted occurrence's working state costs, for a given state type.</summary>
    public static int PayloadSizeOf<TWorkingState>() where TWorkingState : unmanaged
        => sizeof(TWorkingState);

    /// <summary>
    /// ⭐⭐⭐ <b>The occurrence's working state, ATTACHING it on first use.</b> This is the call an
    /// emitted HSM thunk makes.
    ///
    /// <para>⭐⭐ <b>Why LAZY, decided by measurement (§24.8):</b> ① the shipped thunk is <b>already</b>
    /// self-initialising — <c>if (storedHash != StructureHash) { InitBlock; InitDefaultWorkingState; }</c>
    /// — so this is a like-for-like replacement rather than a new lifecycle; ② the blueprint's emitter
    /// <b>cannot see its HSM host</b>, the same invisibility §19.6 ③ records for hand-written hosts, so
    /// there is no host-side manifest to read; ③ the live renderer walks the <b>store's</b> slot table
    /// (<c>BlueprintBlackboardRendererBase:72</c>), not the manifest, so a lazily-attached slot is
    /// renderable the moment it exists.</para>
    ///
    /// <para>⭐ <b>And it COMPOSES with eager provisioning</b> — if a manifest ever declares the same
    /// key, <c>BehaviorIngressSystem</c> will have attached it already and this is a no-op lookup. ⇒
    /// adding manifest entries later (for typed labels in the inspector) changes nothing here.</para>
    ///
    /// <para>⛔ <b>A <c>StructureHash</c> mismatch RESETS the state</b>, which is the documented
    /// behaviour everywhere else in this store: the layout changed under the slot, so the old bytes are
    /// not this type's. ⚠ It is also the only defence against an HSM recompile renumbering states — see
    /// §24.5.</para>
    ///
    /// <para>⛔⛔ <b>A missing STORE is still a hard failure.</b> Adding a tier component is a
    /// STRUCTURAL change and must never happen inside a kernel dispatch; the entity must already carry
    /// one. ⚠ That is a different failure from a missing slot, and the message says which.</para>
    /// </summary>
    /// <param name="freshlyAttached">
    /// <c>true</c> when the slot was just created (or reset by a hash mismatch), so the caller must
    /// initialise its default working state. ⛔ Not an error either way.
    /// </param>
    public static ref TWorkingState ResolveOrAttach<TWorkingState>(
        EntityRepository world, Entity self, int slotKey, ulong structureHash, out bool freshlyAttached)
        where TWorkingState : unmanaged
        => ref OccurrenceWorkingState.ResolveOrAttach<TWorkingState>(
               world, self, slotKey, structureHash, OccurrenceKind.Hsm, out freshlyAttached);

    /// <summary>
    /// ⭐⭐⭐ <c>E3a</c> / <c>CE-298</c> — the occurrence's <b>params AND</b> working state, one slot.
    /// 📄 §28. ⛔ ONE body underneath (<see cref="OccurrenceWorkingState"/>), as always — this forwards
    /// only to bind <see cref="OccurrenceKind.Hsm"/>.
    ///
    /// <para>⚠ The caller SEEDS <paramref name="paramsPtr"/> when <paramref name="freshlyAttached"/>;
    /// §28.4 says from where, and why seeding from zeros would be a regression.</para>
    /// </summary>
    public static ref TWorkingState ResolveOrAttach<TParams, TWorkingState>(
        EntityRepository world, Entity self, int slotKey, ulong structureHash,
        out bool freshlyAttached, out TParams* paramsPtr)
        where TParams : unmanaged
        where TWorkingState : unmanaged
        => ref OccurrenceWorkingState.ResolveOrAttach<TParams, TWorkingState>(
               world, self, slotKey, structureHash, OccurrenceKind.Hsm, out freshlyAttached, out paramsPtr);

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
