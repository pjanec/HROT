using System;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Toolkit.Blueprints.Partitioning;

namespace Fdp.Toolkit.Behavior;

/// <summary>
/// ⭐⭐⭐ <b>ONE body for "resolve this occurrence's working state, attaching it on first use".</b>
/// 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §26.
///
/// <para>⭐ <b>Two callers, one implementation</b> (ruling 9): <see cref="HsmOccurrence"/> for an
/// HSM-hosted occurrence keyed by <c>(region, state)</c>, and the standalone BTree thunks keyed by
/// ASSET. ⛔ They differ only in the KEY and the declared <see cref="OccurrenceKind"/> — everything
/// else, including the hash-mismatch reset and the two distinct failure messages, is identical, and
/// a second copy is how the two would drift.</para>
/// </summary>
public static unsafe class OccurrenceWorkingState
{
    /// <summary>
    /// ⭐⭐ Resolves the occurrence's working state, attaching it on first use.
    ///
    /// <para>⛔ <b>A <c>StructureHash</c> mismatch RESETS</b> — the layout changed under the slot, so
    /// the old bytes are not this type's.</para>
    ///
    /// <para>⛔⛔ <b>A missing STORE is a hard failure, and a DIFFERENT one from a missing slot.</b>
    /// Adding a tier component is a STRUCTURAL change and must never happen inside a tick; the entity
    /// must already carry one. ⚠ The two messages say which, because conflating them is how a caller
    /// fixes the wrong thing.</para>
    /// </summary>
    /// <param name="freshlyAttached">
    /// <c>true</c> when the slot was just created (or reset by a hash mismatch), so the caller must
    /// initialise its default working state. ⛔ Not an error either way.
    /// </param>
    public static ref TWorkingState ResolveOrAttach<TWorkingState>(
        EntityRepository world, Entity self, int slotKey, ulong structureHash,
        OccurrenceKind kind, out bool freshlyAttached)
        where TWorkingState : unmanaged
    {
        byte* store = OccurrenceStoreAccess.TryGetStore(world, self, out _);

        if (store == null)
            throw new InvalidOperationException(
                $"Entity {self} carries no occurrence store, so hosted slot {slotKey} cannot be " +
                "attached. Adding a tier component is a structural change and must not happen inside " +
                "a tick — the entity must already carry one. Two causes, in order of likelihood: " +
                "(1) this host never registered the BlueprintBlackboard* tier components, so " +
                "BehaviorIngressSystem skipped provisioning — call BlueprintTierTable.RegisterAll; " +
                "(2) the entity was built by hand and never went through a behaviour assign.");

        if (BlueprintBlackboardPartitions.TryGetSlotOffset(store, slotKey, out int offset, out uint existingHash))
        {
            if (existingHash == (uint)structureHash)
            {
                freshlyAttached = false;
                return ref Unsafe.AsRef<TWorkingState>(store + offset);
            }

            // The layout changed under this slot — the old bytes are not this type's.
            BlueprintBlackboardPartitions.TryDetach(store, slotKey);
        }

        if (!BlueprintBlackboardPartitions.TryAttach(
                store, slotKey, sizeof(TWorkingState), structureHash, kind, out int newOffset))
            throw new InvalidOperationException(
                $"Entity {self} has an occurrence store with no room for hosted slot {slotKey} " +
                $"({sizeof(TWorkingState)} bytes). The tier is full — provision a larger one, or " +
                "declare this occurrence in the behaviour's stateful manifest so the ingress sizes " +
                "the tier for it.");

        freshlyAttached = true;
        return ref Unsafe.AsRef<TWorkingState>(store + newOffset);
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>E3a</c> / <c>CE-298</c> — the same slot, carrying <c>[WorkingState M][Params N]</c>.</b>
    /// 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §28.
    ///
    /// <para>🔴 <b>Why params had to move.</b> Every emitted HSM thunk projected its <c>Params</c> at
    /// <c>BrainBlackboard.BehaviorParameters[0] + 0</c> — a literal <c>0</c>, shared by every occurrence
    /// on the entity. 🔒 <b>User, <c>2026-09-21</c>:</b> <i>"two actions running in two hsm regions would
    /// overwrite the params … forget the fact it is not in use now. it will be."</i> ⚠ And it is worse
    /// than an overwrite: two DIFFERENT blueprints project two DIFFERENT <c>Params</c> types over the
    /// same bytes, so even a read-only thunk type-puns its sibling's variable.</para>
    ///
    /// <para>⛔ <b>The cheap fix is refuted, not merely rejected</b> (§28.2): the blueprint's emitter
    /// emits ONE thunk per blueprint and <b>cannot see its HSM hosts</b>, so there is no per-site offset
    /// to bake. ⭐ Only the occurrence slot distinguishes two regions — and it already does.</para>
    ///
    /// <para>⭐⭐ <b>ONE slot, not two.</b> One key, one lookup, one lifetime. ⛔ A second slot for params
    /// would need a second key, a second hash guard and a second detach — three more things to drift.</para>
    ///
    /// <para>⛔⛔ <b>WORKING STATE FIRST — and it was <c>[Params][WorkingState]</c> for one build.</b>
    /// 📐 Measured: params-first shifted every existing reader that decodes working state at the
    /// payload BASE and reddened three <c>AiPrimitiveStateMetadataTests</c> inspector rails with
    /// zeros. ⭐ State-first keeps all of them correct BY CONSTRUCTION — the same <c>O7b-1</c> lesson,
    /// caught by a rail this time instead of by the user. 📄 §28.3a.</para>
    ///
    /// <para>⚠ <b>The caller must SEED <paramref name="paramsPtr"/> when <paramref name="freshlyAttached"/>
    /// is <c>true</c></b>, and this seam deliberately does not do it: only the emitter knows where a
    /// given occurrence's authored bytes live today. ⛔ Leaving them zeroed would be the regression
    /// §26.1 names — the old location at least had a producer.</para>
    /// </summary>
    /// <param name="paramsPtr">
    /// The occurrence's OWN params region, at <see cref="ParamsOffsetOf{T}"/> inside the payload.
    /// ⛔ Valid for the calling frame only — the seam's lifetime rule; a structural change can move
    /// the chunk.
    /// </param>
    public static ref TWorkingState ResolveOrAttach<TParams, TWorkingState>(
        EntityRepository world, Entity self, int slotKey, ulong structureHash,
        OccurrenceKind kind, out bool freshlyAttached, out TParams* paramsPtr)
        where TParams : unmanaged
        where TWorkingState : unmanaged
    {
        // ⭐⭐⭐ WORKING STATE FIRST, PARAMS SECOND — and the order is load-bearing (§28.3a).
        //   Every existing reader of a slot decodes working state at the payload's BASE:
        //   BlueprintDebugSession, the live renderers, Resolve<T> above. Putting params first
        //   shifted all of them and reddened three inspector rails; putting them SECOND keeps
        //   every one of those readers correct BY CONSTRUCTION, and only the emitter — which
        //   computes the offset through this same helper — needs to know params exist at all.
        int stateBytes = AlignedBytes(sizeof(TWorkingState));

        byte* store = OccurrenceStoreAccess.TryGetStore(world, self, out _);

        if (store == null)
            throw new InvalidOperationException(
                $"Entity {self} carries no occurrence store, so hosted slot {slotKey} cannot be " +
                "attached. Adding a tier component is a structural change and must not happen inside " +
                "a tick — the entity must already carry one. Two causes, in order of likelihood: " +
                "(1) this host never registered the BlueprintBlackboard* tier components, so " +
                "BehaviorIngressSystem skipped provisioning — call BlueprintTierTable.RegisterAll; " +
                "(2) the entity was built by hand and never went through a behaviour assign.");

        if (BlueprintBlackboardPartitions.TryGetSlotOffset(store, slotKey, out int offset, out uint existingHash))
        {
            if (existingHash == (uint)structureHash)
            {
                freshlyAttached = false;
                paramsPtr = (TParams*)(store + offset + stateBytes);
                return ref Unsafe.AsRef<TWorkingState>(store + offset);
            }

            BlueprintBlackboardPartitions.TryDetach(store, slotKey);
        }

        int payload = stateBytes + sizeof(TParams);
        if (!BlueprintBlackboardPartitions.TryAttach(
                store, slotKey, payload, structureHash, kind, out int newOffset))
            throw new InvalidOperationException(
                $"Entity {self} has an occurrence store with no room for hosted slot {slotKey} " +
                $"({payload} bytes = {stateBytes} working state + {sizeof(TParams)} params). " +
                "The tier is full — provision a larger one, or declare this occurrence in the " +
                "behaviour's stateful manifest so the ingress sizes the tier for it.");

        freshlyAttached = true;
        paramsPtr = (TParams*)(store + newOffset + stateBytes);
        return ref Unsafe.AsRef<TWorkingState>(store + newOffset);
    }

    /// <summary>
    /// ⭐ Rounds a region up to the store's alignment. ONE spelling — the emitter never computes
    /// this, <c>HostedOccurrenceDemand.Of</c> sizes the tier through the same rounding (<c>O7b-3</c>),
    /// and the two must agree or the tier is sized for a payload that does not fit.
    /// </summary>
    public static int AlignedBytes(int rawBytes)
    {
        const int alignment = BlueprintBlackboardPartitions.Alignment;
        return (rawBytes + alignment - 1) & ~(alignment - 1);
    }

    /// <summary>
    /// Byte offset of the PARAMS region inside a <c>[WorkingState][Params]</c> slot payload.
    /// ⭐ For any reader that is not the emitter — the inspector, a hand-written host.
    /// </summary>
    public static int ParamsOffsetOf<TWorkingState>() where TWorkingState : unmanaged
        => AlignedBytes(sizeof(TWorkingState));
}
