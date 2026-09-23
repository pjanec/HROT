using System;
using Fdp.Core;
using Fdp.Toolkit.Blueprints.Partitioning;
using Fhsm.Kernel;
using Fhsm.Kernel.Data;

namespace Fdp.Toolkit.Behavior;

/// <summary>
/// ⭐⭐⭐ <b><c>O7c</c>-④ — THE ONE WAY TO *LOCATE* A BEHAVIOUR'S ROOT HSM INSTANCE.</b>
/// 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §31.14.
///
/// <para>⭐ <b>The deliberate mirror of <see cref="RootStateAccess"/></b>, member for member — the key
/// is COMPUTED from <c>BehaviorState.ActiveBehaviorHash</c> and never stored, readers never attach,
/// ingress owns attach and detach.</para>
///
/// <para>🔴🔴 <b>THE ONE PLACE HSM IS GENUINELY HARDER THAN BTREE: the width is a RUNTIME value.</b>
/// A BTree cursor is <c>sizeof(BehaviorTreeState)</c> — a constant the compiler knows. An HSM instance
/// is 64, 128 or 256 bytes depending on the MACHINE, and only
/// <see cref="HsmInstanceManager.SelectTier"/> can say which. ⇒ every entry point here takes either a
/// <see cref="HsmDefinitionBlob"/> or reads the size back off the slot; ⛔ nothing here may size from
/// a type, which is the defect §9.4 names.</para>
///
/// <para>⭐⭐ <b>The size is stored in the slot's guard field and read back as the length</b> — exactly
/// the convention <see cref="RootParamsAccess.TryGetRootBytes"/> already uses, where the guard doubles
/// as the extent. ⇒ the tick arm gets its <c>instanceSize</c> from the SAME lookup that gives it the
/// pointer, so the two cannot disagree; and a machine whose tier changes under a hot reload fails the
/// guard, is detached, and re-attaches at the new width instead of being ticked at the old one.</para>
///
/// <para>🔴 <b>What this replaces, and what it does NOT change.</b> The instance lived in the
/// <c>BrainHsm128</c> COMPONENT — addressed by TYPE, so an entity could only ever have one, and
/// permanently 128 bytes whatever <see cref="HsmInstanceManager.SelectTier"/> said. ⚠ After this, a
/// 64-byte machine occupies 64 bytes and the 64-byte tier becomes REACHABLE for the first time
/// (§9.4's <i>"the tier stops being a TYPE and becomes a PAYLOAD SIZE"</i>, literally). ⛔ The
/// kernel's own semantics are untouched: <c>ValidateInstance</c> still gates on
/// <c>InstanceHeader.MachineId</c>, and <see cref="ResetInstance"/> still stamps it.</para>
/// </summary>
public static unsafe class RootHsmAccess
{
    /// <summary>
    /// ⭐⭐ <b>The instance width for a machine — 64, 128 or 256.</b>
    /// ⛔ The ONE producer of that number in this assembly; every cost, attach and reset routes
    /// through it so a tier change cannot be applied in one place and missed in another.
    /// </summary>
    public static int InstanceBytes(HsmDefinitionBlob? blob)
        => blob is null ? 0 : HsmInstanceManager.SelectTier(blob);

    /// <summary>
    /// ⭐⭐ <b>What the tier demand must reserve for this behaviour's HSM instance.</b>
    ///
    /// <para>⛔ Asks the DEFINITION, never a probe — <see cref="RootStateAccess.RootStateBytes"/>'s
    /// argument verbatim: <i>"the lookup failed"</i> cannot tell <i>"this behaviour has no machine"</i>
    /// from <i>"the slot should exist and does not"</i>.</para>
    /// </summary>
    public static int RootHsmBytes(BehaviorDefinition? def)
        => def is not null
        && def.BrainTier == BehaviorConstants.BrainTierHsm
            ? InstanceBytes(def.HsmDefinition)
            : 0;

    /// <summary>The slot key for this entity's CURRENT root behaviour, or <c>0</c> when it has none.</summary>
    public static int KeyFor(EntityRepository world, Entity self)
    {
        if (!world.HasComponent<Components.BehaviorState>(self)) return 0;
        ref readonly var state = ref world.GetComponentRO<Components.BehaviorState>(self);
        return KeyForBehaviour(state.ActiveBehaviorHash);
    }

    /// <summary>The same key from an explicit behaviour hash — for ingress, which has it in hand.</summary>
    public static int KeyForBehaviour(int behaviourHash)
        => behaviourHash == 0 ? 0 : Shared.OccurrenceSlotKey.ComputeRootHsmKey(behaviourHash);

    /// <summary>
    /// ⭐⭐ <b>The root HSM instance as a pointer AND its size.</b> Returns <c>false</c> — leaving
    /// <paramref name="ptr"/> null and <paramref name="instanceSize"/> zero — when the entity has no
    /// behaviour, no occurrence store, or no instance slot yet.
    ///
    /// <para>⛔⛔ <b>It does NOT attach</b>, for the same reason <see cref="RootStateAccess.TryGetState"/>
    /// does not: a reader that attached on miss would manufacture a ZEROED instance, and a zeroed
    /// instance has <c>MachineId == 0</c>, which <c>HsmKernelCore.ValidateInstance</c> silently
    /// rejects. ⇒ the machine would look present and never run.</para>
    /// </summary>
    public static bool TryGetInstance(
        EntityRepository world, Entity self, out byte* ptr, out int instanceSize)
    {
        ptr = null;
        instanceSize = 0;

        int key = KeyFor(world, self);
        if (key == 0) return false;

        byte* store = OccurrenceStoreAccess.TryGetStore(world, self, out _);
        if (store == null) return false;

        if (!BlueprintBlackboardPartitions.TryGetSlotOffset(store, key, out int offset, out uint guard))
            return false;

        ptr = store + offset;
        instanceSize = unchecked((int)guard);
        return true;
    }

    /// <summary>
    /// ⭐⭐ <b>The read-only <see cref="Fdp.ModuleHost.Abstractions.ISimulationView"/> form — a COPY.</b>
    /// For <c>HsmDebugSession</c> and the inspector renderers, which may be handed a SNAPSHOT and so
    /// must not cast to <see cref="EntityRepository"/>.
    ///
    /// <para>⛔ <b>A copy rather than a pointer, and that is forced.</b> The tier component's bytes are
    /// reachable through the view only as a <c>Span</c> whose pin ends with this call, so handing back
    /// a pointer would hand back a dangling one. ⚠ It is also why this cannot mirror
    /// <see cref="RootStateAccess.TryGetStateInView"/>'s by-value return: the instance width is a
    /// runtime value, so there is no struct to return.</para>
    ///
    /// <para>⚠ Deliberately a <c>Try</c>: a debug surface draws what is there.</para>
    /// </summary>
    /// <param name="destination">Must be at least the instance's width; a shorter span returns
    /// <c>false</c> with <paramref name="written"/> set to the width required.</param>
    public static bool TryCopyInstanceInView(
        Fdp.ModuleHost.Abstractions.ISimulationView view, Entity self,
        Span<byte> destination, out int written)
    {
        written = 0;

        if (!view.HasComponent<Components.BehaviorState>(self)) return false;
        int key = KeyForBehaviour(view.GetComponentRO<Components.BehaviorState>(self).ActiveBehaviorHash);
        if (key == 0) return false;

        var tiers = BlueprintTierTable.Descending;
        for (int i = 0; i < tiers.Count; i++)
        {
            if (!tiers[i].HasInView(view, self)) continue;

            var store = tiers[i].BytesInView(view, self);
            if (store.IsEmpty) return false;

            fixed (byte* mem = store)
            {
                if (!BlueprintBlackboardPartitions.TryGetSlotOffset(mem, key, out int offset, out uint guard))
                    return false;

                int size = unchecked((int)guard);
                written = size;
                if (destination.Length < size) return false;

                new ReadOnlySpan<byte>(mem + offset, size).CopyTo(destination);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The EXECUTION form — the pointer and size the kernel steps.</b>
    ///
    /// <para>⛔⛔ <b>Throws rather than returning a scratch buffer</b>, exactly as
    /// <see cref="RootStateAccess.RequireStateRef"/> does: stepping a stack local would run the machine
    /// from its entry state every frame, forever, looking like a machine that never progresses.</para>
    ///
    /// <para>⚠ <b>The returned pointer lives under <see cref="OccurrenceStoreAccess"/>'s lifetime
    /// rule:</b> use it within the call that obtained it, and never hold it across anything that can
    /// add or remove a component on this entity (a tier promotion swaps the component out).</para>
    /// </summary>
    public static byte* RequireInstance(EntityRepository world, Entity self, out int instanceSize)
    {
        if (TryGetInstance(world, self, out byte* ptr, out instanceSize))
            return ptr;

        throw new InvalidOperationException(
            $"Entity {self.Index} has no ROOT HSM INSTANCE slot, so its state machine cannot be " +
            "stepped. Causes, in the order worth checking: (1) no behaviour is assigned — " +
            "BehaviorState.ActiveBehaviorHash is 0; (2) the entity carries no occurrence store, " +
            "because its brain tier is not Hsm or the BlueprintBlackboard* tier components were " +
            "never registered on this world; (3) the store had no room at assign time, which means " +
            "the tier demand under-counted (see RootHsmCost in BehaviorIngressSystem). " +
            "Before O7c-4 this instance came from the BrainHsm128 component and could not fail.");
    }

    /// <summary>
    /// ⭐⭐ <b>Attach-or-find, for INGRESS only.</b> The one place a root HSM instance slot is created.
    ///
    /// <para>⛔⛔ <b>A width MISMATCH detaches and re-attaches</b> rather than reusing the slot. That is
    /// not tidiness: a machine edited from two regions to three moves from the 128-byte tier to the
    /// 256-byte one, and ticking the new definition against the old 128-byte allocation is precisely
    /// the out-of-bounds read §9.4 describes. ⚠ The instance state is lost in that case, which is
    /// correct — the state ids it referred to have been renumbered.</para>
    ///
    /// <para>⭐ <b>A fresh attach is ZEROED by <c>TryAttach</c> but NOT initialised.</b> A zeroed
    /// instance has <c>MachineId == 0</c> and <c>ValidateInstance</c> rejects it, so the caller must
    /// follow with <see cref="ResetInstance"/> or <c>HsmInstanceManager.Initialize</c>. ⇒ that is exactly what the
    /// old <c>new BrainHsm128()</c> + <c>ResetHsmComponents</c> pair did, split the same way.</para>
    /// </summary>
    public static byte* ResolveOrAttachRoot(
        EntityRepository world, Entity self, int behaviourHash,
        int instanceBytes, OccurrenceKind kind, out bool freshlyAttached)
    {
        freshlyAttached = false;

        int key = KeyForBehaviour(behaviourHash);
        if (key == 0 || instanceBytes <= 0) return null;

        byte* store = OccurrenceStoreAccess.TryGetStore(world, self, out _);
        if (store == null) return null;

        ulong guard = unchecked((ulong)instanceBytes);

        if (BlueprintBlackboardPartitions.TryGetSlotOffset(store, key, out int offset, out uint existing))
        {
            if (existing == (uint)guard) return store + offset;
            BlueprintBlackboardPartitions.TryDetach(store, key);
        }

        if (!BlueprintBlackboardPartitions.TryAttach(store, key, instanceBytes, guard, kind, out int newOffset))
            return null;

        freshlyAttached = true;
        return store + newOffset;
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Provision a store if the entity has none, then attach AND initialise the instance.</b>
    ///
    /// <para>⚠ <b>Deliberately the SMALLEST tier that fits</b>, and deliberately not a demand
    /// calculation — <see cref="RootStateAccess.EnsureRootState"/>'s reasoning verbatim.
    /// <c>BehaviorIngressSystem</c> owns sizing; this only has to make the pre-assign tick possible.</para>
    ///
    /// <para>⛔ <b>Skips silently when the tier components are not registered</b> (§27.2), as every
    /// other provisioner in this family does.</para>
    /// </summary>
    public static bool EnsureRootInstance(
        EntityRepository world, Entity self, int behaviourHash, HsmDefinitionBlob? blob)
    {
        if (behaviourHash == 0 || blob is null) return false;

        int bytes = InstanceBytes(blob);
        if (bytes <= 0) return false;

        if (BlueprintTierTable.Of(world, self) is null)
        {
            // ⭐⭐ CE-318 (2026-09-23): PAYLOAD only. The slot entry is carved out of the store
            //   up front, so Select's PayloadSize has already had it removed; the slot axis is the
            //   `1` below. 📄 BlueprintBlackboardPartitions.PayloadCost.
            int cost = BlueprintBlackboardPartitions.PayloadCost(bytes);

            var target = BlueprintTierTable.Select(cost, 1);
            if (!target.IsRegistered(world)) return false;

            target.Add(world, self);
            BlueprintBlackboardPartitions.Initialize(
                target.Memory(world, self), target.TotalSize, (byte)target.MaxSlots);
        }

        byte* ptr = ResolveOrAttachRoot(
            world, self, behaviourHash, bytes, OccurrenceKind.Hsm, out bool fresh);
        if (ptr == null) return false;

        if (fresh) HsmInstanceManager.Initialize(ptr, bytes, blob);
        return true;
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Bind the instance to a definition's topology — the size-driven replacement for
    /// <c>BehaviorIngressSystem.ResetHsmComponents</c>.</b>
    ///
    /// <para>🔴 <b>Why this is not just <see cref="HsmInstanceManager.Reset(byte*, int)"/>.</b> The
    /// kernel's <c>Reset</c> PRESERVES <c>MachineId</c> — it restarts a machine that is already bound.
    /// ⛔ This call exists for the other case: a behaviour CHANGE, where the instance must be re-bound
    /// to a DIFFERENT machine, so <c>MachineId</c> has to be overwritten or
    /// <c>ValidateInstance</c> rejects every subsequent tick. ⇒ initialise to the new blob, which sets
    /// <c>MachineId</c>, clears the flags and resets the active leaves in one size-driven pass.</para>
    ///
    /// <para>⚠ <b>Returns <c>false</c> when there is no slot, and that is correct here</b> rather than
    /// loud: every caller is a behaviour-change handler that runs for BTree and HSM brains alike, so
    /// <i>"this entity has no machine"</i> is an ordinary case — the same argument
    /// <see cref="RootStateAccess.ResetState"/> makes, and the same reason the component version was
    /// guarded by <c>HasComponent&lt;BrainHsm128&gt;</c>.</para>
    /// </summary>
    public static bool ResetInstance(EntityRepository world, Entity self, HsmDefinitionBlob? blob)
    {
        if (blob is null) return false;
        if (!TryGetInstance(world, self, out byte* ptr, out int instanceSize)) return false;

        HsmInstanceManager.Initialize(ptr, instanceSize, blob);
        return true;
    }

    /// <summary>
    /// ⭐ <b>Drop a behaviour's root HSM instance slot — for INGRESS only, on a behaviour CHANGE.</b>
    ///
    /// <para>⛔⛔ <b>Mandatory, not a follow-up.</b> ⚠ Unlike the BTree root slots, this one declares
    /// <c>OccurrenceKind.Hsm</c>, so <c>DetachHostedOccurrenceSlots</c> — which sweeps exactly
    /// <i>"kind Hsm|Blueprint and not named by the manifest"</i> — CAN see it. 🔴 That makes the
    /// ORDERING load-bearing in the opposite direction from the params slot: the attach must happen
    /// AFTER that sweep, or the sweep removes the slot on the very assign that created it. The assign
    /// handler already places both root attaches after it for exactly this reason.</para>
    ///
    /// <para>⚠ Keyed by the OLD behaviour hash, so it cannot touch the new behaviour's slot even when
    /// both are attached in the same frame.</para>
    /// </summary>
    public static bool DetachRoot(EntityRepository world, Entity self, int behaviourHash)
    {
        int key = KeyForBehaviour(behaviourHash);
        if (key == 0) return false;

        byte* store = OccurrenceStoreAccess.TryGetStore(world, self, out _);
        if (store == null) return false;

        return BlueprintBlackboardPartitions.TryDetach(store, key);
    }

    private static int AlignUp(int size, int alignment) => (size + alignment - 1) & ~(alignment - 1);
}
