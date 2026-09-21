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
}
