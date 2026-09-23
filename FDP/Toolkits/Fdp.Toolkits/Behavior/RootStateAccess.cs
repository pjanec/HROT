using System;
using System.Runtime.CompilerServices;
using Fbt;
using Fdp.Core;
using Fdp.Toolkit.Blueprints.Partitioning;

namespace Fdp.Toolkit.Behavior;

/// <summary>
/// ⭐⭐⭐ <b><c>O7c</c>-② / <c>CE-319</c> — THE ONE WAY TO *LOCATE* A BEHAVIOUR'S ROOT TREE STATE.</b>
/// 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §31.
///
/// <para>⭐ <b>The deliberate mirror of <see cref="RootParamsAccess"/></b>, member for member: the key
/// is COMPUTED from <c>BehaviorState.ActiveBehaviorHash</c> and never stored, readers never attach,
/// ingress owns attach and detach, and the loud form is for EXECUTION while the <c>Try</c> forms are
/// for debug surfaces. ⛔ Two spellings of "find the root slot" is exactly what <c>CE-303</c> cost,
/// so this one is a seam from the start rather than a helper someone may bypass.</para>
///
/// <para>🔴 <b>What this replaces, and WHY — the reason is capability, not bytes.</b> The root tree
/// cursor lived in the <c>BrainBTreeState</c> COMPONENT. A component is addressed by its TYPE, so an
/// entity could only ever have ONE — which is precisely the limit that stopped a hosted subtree
/// owning its own cursor and forced <c>BTreeOrchestratorEmitCore</c> to pass the MASTER's
/// <c>ref state</c> down (the defect <c>C1</c> railed). ⭐⭐ A KEYED slot is what makes "multiple
/// trees on one entity" expressible. 🔒 User, <c>2026-09-22</c>: <i>"i thought the reason is to allow
/// for subtrees (multiple trees on a single entity)"</i>.</para>
///
/// <para>⚠ <b>The layout guard is <c>sizeof(BehaviorTreeState)</c></b>, matching
/// <see cref="RootParamsAccess"/>'s convention of storing the region's own extent. ⛔ It is a CONSTANT
/// (64 — <c>[StructLayout(Size = 64)]</c>), so unlike the params guard it cannot detect a changed
/// layout; a different behaviour is already a different key, and nothing else varies. ⚠ A structure-hash
/// guard — resetting the cursor when a hot reload changes the tree's topology — would be a
/// BEHAVIOUR CHANGE, not a port: the component never did that, and <c>O7c</c>-② is a clean cut, not a
/// redesign. Worth considering separately.</para>
/// </summary>
public static unsafe class RootStateAccess
{
    /// <summary>The fixed width of a root tree-state region. ⭐ <c>BehaviorTreeState</c> is
    /// <c>[StructLayout(LayoutKind.Explicit, Size = 64)]</c>, so this is a constant, not a policy.</summary>
    public static int StateBytes => sizeof(BehaviorTreeState);

    /// <summary>
    /// ⭐ The slot key for this entity's CURRENT root behaviour, or <c>0</c> when it has none.
    /// ⛔ Computed, never stored — see <c>OccurrenceSlotKey.ComputeRootStateKey</c>.
    /// </summary>
    public static int KeyFor(EntityRepository world, Entity self)
    {
        if (!world.HasComponent<Components.BehaviorState>(self)) return 0;
        ref readonly var state = ref world.GetComponentRO<Components.BehaviorState>(self);
        return KeyForBehaviour(state.ActiveBehaviorHash);
    }

    /// <summary>The same key from an explicit behaviour hash — for ingress, which has it in hand.</summary>
    public static int KeyForBehaviour(int behaviourHash)
        => behaviourHash == 0 ? 0 : Shared.OccurrenceSlotKey.ComputeRootStateKey(behaviourHash);

    /// <summary>
    /// ⭐⭐ <b>The root tree state as a pointer.</b> Returns <c>false</c> — leaving <paramref name="ptr"/>
    /// null — when the entity has no behaviour, no occurrence store, or no root state slot yet.
    ///
    /// <para>⛔⛔ <b>It does NOT attach.</b> Attaching is ingress's job, once per behaviour assign. A
    /// reader that attached on miss would manufacture a zero cursor — which reads as "the tree is at
    /// its root", a plausible-looking lie — and hide the real fault.</para>
    /// </summary>
    public static bool TryGetState(EntityRepository world, Entity self, out BehaviorTreeState* ptr)
    {
        ptr = null;

        int key = KeyFor(world, self);
        if (key == 0) return false;

        byte* store = OccurrenceStoreAccess.TryGetStore(world, self, out _);
        if (store == null) return false;

        if (!BlueprintBlackboardPartitions.TryGetSlotOffset(store, key, out int offset, out _))
            return false;

        ptr = (BehaviorTreeState*)(store + offset);
        return true;
    }

    /// <summary>
    /// ⭐⭐ <b>The read-only <see cref="Fdp.ModuleHost.Abstractions.ISimulationView"/> form</b>, for the
    /// BTree debug session and the inspector renderer — surfaces handed a view that may be a SNAPSHOT,
    /// so they must not cast to <see cref="EntityRepository"/>.
    ///
    /// <para>⚠ Deliberately a <c>Try</c>: a debug surface draws what is there.</para>
    /// </summary>
    public static bool TryGetStateInView(
        Fdp.ModuleHost.Abstractions.ISimulationView view, Entity self, out BehaviorTreeState state)
    {
        state = default;

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
                if (!BlueprintBlackboardPartitions.TryGetSlotOffset(mem, key, out int offset, out _))
                    return false;
                state = *(BehaviorTreeState*)(mem + offset);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The EXECUTION form — a <c>ref</c> the interpreter can step.</b>
    ///
    /// <para>⛔⛔ <b>Throws rather than returning a scratch cursor.</b> A BTree-tier entity reaching the
    /// tick with no root state slot means ingress did not provision one, and ticking a stack local
    /// would run the tree from its root every frame — forever, silently, looking like a behaviour that
    /// never progresses. 🔒 That is the silent-default shape this programme keeps filing.</para>
    ///
    /// <para>⚠ <b>The returned <c>ref</c> lives under <c>OccurrenceStoreAccess</c>'s lifetime rule:</b>
    /// use it within the call that obtained it, and never hold it across anything that can add or
    /// remove a component on this entity (a tier promotion swaps the component out).</para>
    /// </summary>
    public static ref BehaviorTreeState RequireStateRef(EntityRepository world, Entity self)
    {
        if (TryGetState(world, self, out BehaviorTreeState* ptr))
            return ref Unsafe.AsRef<BehaviorTreeState>(ptr);

        throw new InvalidOperationException(
            $"Entity {self.Index} has no ROOT TREE STATE slot, so its behaviour tree cannot be " +
            "stepped. Causes, in the order worth checking: (1) no behaviour is assigned — " +
            "BehaviorState.ActiveBehaviorHash is 0; (2) the entity carries no occurrence store, " +
            "because its brain tier is not BTree or the BlueprintBlackboard* tier components were " +
            "never registered on this world; (3) the store had no room at assign time, which means " +
            "the tier demand under-counted (see RootStateCost in BehaviorIngressSystem). " +
            "Before O7c-2 this state came from the BrainBTreeState component and could not fail.");
    }

    /// <summary>
    /// ⭐⭐ <b>Attach-or-find, for INGRESS only.</b> The one place a root state slot is created.
    ///
    /// <para>⚠ <paramref name="kind"/> follows the entity's BRAIN TIER, exactly as the root params
    /// slot's does — there is no "Behavior" kind (<c>A3</c>/<c>D1′</c>).</para>
    /// </summary>
    public static byte* ResolveOrAttachRoot(
        EntityRepository world, Entity self, int behaviourHash,
        OccurrenceKind kind, out bool freshlyAttached)
    {
        freshlyAttached = false;

        int key = KeyForBehaviour(behaviourHash);
        if (key == 0) return null;

        byte* store = OccurrenceStoreAccess.TryGetStore(world, self, out _);
        if (store == null) return null;

        int   bytes = StateBytes;
        ulong guard = unchecked((ulong)bytes);

        if (BlueprintBlackboardPartitions.TryGetSlotOffset(store, key, out int offset, out uint existing))
        {
            if (existing == (uint)guard) return store + offset;
            BlueprintBlackboardPartitions.TryDetach(store, key);
        }

        if (!BlueprintBlackboardPartitions.TryAttach(store, key, bytes, guard, kind, out int newOffset))
            return null;

        freshlyAttached = true;
        return store + newOffset;
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Provision a store if the entity has none, then attach the root state slot.</b>
    ///
    /// <para>⭐⭐ <b>ONE spelling, shared by the two provisioners.</b> <c>BehaviorTkbTranslator</c> calls
    /// it at SPAWN (where a default behaviour is stamped without any assign event) and test fixtures
    /// call it in place of the <c>AddComponent(new BrainBTreeState())</c> they used to write. ⛔ Two
    /// spellings of "give this entity a cursor" is the duplication <c>CE-303</c> cost.</para>
    ///
    /// <para>⚠ <b>Deliberately the SMALLEST tier, and deliberately not a demand calculation.</b>
    /// <c>BehaviorIngressSystem</c> owns sizing — on the first real assign it recomputes the whole
    /// demand (manifest + hosted + root params + root state) and promotes. This only has to make the
    /// pre-assign tick possible.</para>
    ///
    /// <para>⛔ <b>Skips silently when the tier components are not registered</b>, exactly as
    /// <c>EnsureOccurrenceStore</c> does and for the same reason (§27.2): <c>Fdp.Toolkits</c> hosts may
    /// run without the Hrot-wide tier registration, and provisioning must not make them fail at spawn.</para>
    /// </summary>
    public static bool EnsureRootState(EntityRepository world, Entity self, int behaviourHash)
    {
        if (behaviourHash == 0) return false;

        if (BlueprintTierTable.Of(world, self) is null)
        {
            // ⭐⭐ CE-318 (2026-09-23): PAYLOAD only. The slot entry is carved out of the store
            //   up front, so Select's PayloadSize has already had it removed; the slot axis is the
            //   `1` below. 📄 BlueprintBlackboardPartitions.PayloadCost.
            int cost = BlueprintBlackboardPartitions.PayloadCost(StateBytes);

            var target = BlueprintTierTable.Select(cost, 1);
            if (!target.IsRegistered(world)) return false;

            target.Add(world, self);
            BlueprintBlackboardPartitions.Initialize(
                target.Memory(world, self), target.TotalSize, (byte)target.MaxSlots);
        }

        return ResolveOrAttachRoot(world, self, behaviourHash, OccurrenceKind.BTree, out _) != null;
    }

    /// <summary>
    /// ⭐ <b>The cursor BY VALUE</b>, or <c>default</c> when the entity has no root state slot.
    ///
    /// <para>⚠ <b>For SAFE callers</b> — test fixtures and debug surfaces that only want to read. ⛔ Not
    /// for the tick: a <c>default</c> here is indistinguishable from "a tree at its root", which is why
    /// the execution path uses <see cref="RequireStateRef"/> and throws instead.</para>
    /// </summary>
    public static BehaviorTreeState GetStateOrDefault(EntityRepository world, Entity self)
        => TryGetState(world, self, out BehaviorTreeState* ptr) ? *ptr : default;

    /// <summary>
    /// ⭐ <b>Overwrite the cursor</b>, returning <c>false</c> when there is no slot to write into.
    /// ⚠ For fixtures that used to seed <c>BrainBTreeState.State</c> before ticking.
    /// </summary>
    public static bool SetState(EntityRepository world, Entity self, in BehaviorTreeState value)
    {
        if (!TryGetState(world, self, out BehaviorTreeState* ptr)) return false;
        *ptr = value;
        return true;
    }

    /// <summary>
    /// ⭐ The same, taking the hash from the entity's own <c>BehaviorState</c>.
    ///
    /// <para>⚠ <b>ORDER MATTERS and it fails LOUDLY, which is why this overload is safe to offer.</b>
    /// <c>BehaviorState</c> must already carry a non-zero <c>ActiveBehaviorHash</c> — otherwise the key
    /// is 0 and this is a no-op. ⛔ The mistake does not stay hidden: the entity then reaches
    /// <see cref="RequireStateRef"/> with no slot and THROWS, naming the cause. ⭐ That is the whole
    /// argument for the loud form — a silent skip here would look like a tree that never advances.</para>
    /// </summary>
    public static bool EnsureRootState(EntityRepository world, Entity self)
    {
        if (!world.HasComponent<Components.BehaviorState>(self)) return false;
        return EnsureRootState(world, self, world.GetComponentRO<Components.BehaviorState>(self).ActiveBehaviorHash);
    }

    private static int AlignUp(int size, int alignment) => (size + alignment - 1) & ~(alignment - 1);

    /// <summary>
    /// ⭐⭐⭐ <b>Zero the cursor — the replacement for <c>btState.State = default</c>.</b>
    ///
    /// <para>⚠ <b>A no-op when the slot is absent, and that is correct here</b>, unlike the execution
    /// path: every caller is a behaviour-change handler that runs for BTree and HSM entities alike, so
    /// "this entity has no tree cursor" is an ordinary case rather than a fault. ⛔ The three former
    /// call sites were each guarded by <c>HasComponent&lt;BrainBTreeState&gt;</c> for the same reason.</para>
    /// </summary>
    public static bool ResetState(EntityRepository world, Entity self)
    {
        if (!TryGetState(world, self, out BehaviorTreeState* ptr)) return false;
        *ptr = default;
        return true;
    }

    /// <summary>
    /// ⭐ <b>Drop a behaviour's root state slot — for INGRESS only, on a behaviour CHANGE.</b>
    ///
    /// <para>⛔⛔ <b>Mandatory, not a follow-up</b> — the same argument as
    /// <see cref="RootParamsAccess.DetachRoot"/>: a root state slot on a BTree brain declares
    /// <c>OccurrenceKind.BTree</c>, and <c>DetachHostedOccurrenceSlots</c> sweeps only <c>Hsm</c> and
    /// <c>Blueprint</c> kinds, so it is invisible to that sweep. ⇒ without this, every behaviour change
    /// leaks one slot, and on the 256 tier there are only three.</para>
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

    /// <summary>
    /// ⭐⭐ <b>What the tier demand must reserve for this behaviour's root state.</b>
    ///
    /// <para>⭐ <c>StateBytes</c> for a BTree-tier behaviour, <c>0</c> otherwise — ⛔ asking the
    /// DEFINITION rather than probing for a slot, which is the same checkable-predicate discipline
    /// <c>CE-307</c> forced on the params path: "did the lookup fail?" cannot distinguish "this
    /// behaviour has no tree" from "the slot should exist and does not".</para>
    /// </summary>
    public static int RootStateBytes(BehaviorDefinition? def)
        => def is not null && def.BrainTier == BehaviorConstants.BrainTierBTree ? StateBytes : 0;
}
