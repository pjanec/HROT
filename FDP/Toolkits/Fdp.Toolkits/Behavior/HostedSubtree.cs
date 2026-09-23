using System;
using System.Runtime.CompilerServices;
using Fbt;
using Fbt.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Blueprints.Partitioning;

namespace Fdp.Toolkit.Behavior;

/// <summary>
/// ⭐⭐⭐ <b>THE hosting call — one body, used by the generated orchestrator AND by hand-written
/// hosts.</b> <c>O4</c> / task <c>C1</c> — 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §19.
///
/// <para>🔴 <b>What it replaces.</b> <c>BTreeOrchestratorEmitCore</c> emitted
/// <c>child.Tick(ref subBb, <b>ref state</b>, ref ctx)</c> at <c>:142</c> and <c>:171</c> — handing the
/// child the MASTER's <see cref="BehaviorTreeState"/>. One 64-byte struct, one
/// <c>RunningNodeIndex</c> ⇒ host and child share a cursor, and the host wins because its
/// <c>ExecuteAction</c> writes AFTER the hosting action returns. Rail
/// <c>HostedSubtreeCursorTests.O4_R1</c> measures it (§18).</para>
///
/// <para>⛔⛔ <b>Why a runtime helper and not a baked pointer.</b> §19.6 ③: a host written in C# is
/// not an <c>AdditionalText</c> and is INVISIBLE to the generator, and hand-written trees are
/// production here. ⇒ the hosting site supplies its key and the resolution happens at runtime;
/// the emitter's const-bake of that key is an optimisation, not the contract.</para>
///
/// <para>⭐ <b>Zero ExtDeps.</b> <see cref="BehaviorTreeState"/> is untouched — the slot holds the
/// existing 64-byte struct. No new delegate parameter, no <c>BTreeContext</c> field, no kernel
/// change, which is what keeps <c>O4</c> a valid stop-or-go gate (§4.1).</para>
/// </summary>
public static unsafe class HostedSubtree
{
    /// <summary>Payload bytes one hosted occurrence's tree state costs. ⚠ §19.4's sizing term.</summary>
    public static int TreeStatePayloadSize => sizeof(BehaviorTreeState);

    /// <summary>
    /// ⭐⭐ Ticks <paramref name="child"/> against ITS OWN <see cref="BehaviorTreeState"/>, resolved
    /// from the entity's occurrence store by <paramref name="treeStateSlotKey"/>.
    ///
    /// <para>⭐ <b><c>D4</c> is in TWO halves and this is only the first</b> — see <see cref="Reset"/>.
    /// ⚠ A hosting action runs only when the host ENTERS it, so it can never observe the host
    /// ABANDONING a still-<c>Running</c> child. That is the <c>F14</c> case and it needs the
    /// deactivator.</para>
    ///
    /// <para>⛔⛔ <b>A missing slot is a HARD failure, not a silent one</b> — §19.6 ⑤. The hosting site
    /// owns its own identity now, so a wrong key would otherwise read as
    /// <i>"the subtree just fails"</i>: exactly the silent slot miss <c>A1</c> exists to kill.</para>
    /// </summary>
    public static NodeStatus Tick<TChildBb>(
        Interpreter<TChildBb, BTreeContext> child,
        ref TChildBb childBb,
        ref BTreeContext ctx,
        int treeStateSlotKey)
        where TChildBb : unmanaged
    {
        if (child is null) throw new ArgumentNullException(nameof(child));

        ref var state = ref ResolveState(ref ctx, treeStateSlotKey);

        var status = child.Tick(ref childBb, ref state, ref ctx);

        // ⭐ D4, HALF ONE — the child COMPLETED, so it starts fresh next entry.
        // ⚠ The interpreter's own cleanup already zeroes RunningNodeIndex on a non-Running result;
        //   this is the DEEPER reset — StackPointer, NodeIndexStack, LocalRegisters, InstanceFlags —
        //   which the kernel does not clear and which would otherwise leak into the next entry.
        // ⛔ It is NOT the F14 case: see Reset, which handles the child left RUNNING.
        if (status != NodeStatus.Running)
            state = default;

        return status;
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>D4</c>, HALF TWO — the <c>F14</c> case: the host ABANDONS a child that is still
    /// <c>Running</c>.</b> Registered as the hosting node's <b>deactivator</b>.
    ///
    /// <para>🔴🔴 <b>Why <see cref="Tick"/> alone cannot do this.</b> A hosting action only runs when
    /// the host ENTERS it. If the host leaves the hosting node while the child is mid-tree — a
    /// sibling fails, a Selector moves on, the host resets — the hosting action is never called
    /// again, so nothing clears the child's cursor and the next entry RESUMES MID-TREE. ⚠ Sharing
    /// the master's state used to hide this: the host's own write clobbered the child every tick, so
    /// the reset was ACCIDENTAL. ⇒ own state removes it, and <c>F14</c> is the bill.</para>
    ///
    /// <para>⭐⭐ <b>And the hook already exists — zero ExtDeps.</b> <c>Interpreter.SweepExitedNodes</c>
    /// invokes a deactivator for any node that leaves the active path and is
    /// <c>IsResourceOwning</c>; <c>BTreeBuilder.Compile</c> sets that bit <b>automatically</b> for a
    /// node whose key has a registered deactivator. ⇒ registering this IS the opt-in.</para>
    /// </summary>
    public static void Reset(ref BTreeContext ctx, int treeStateSlotKey)
        => Reset(ctx.World, ctx.Self, treeStateSlotKey);

    /// <summary>
    /// ⭐⭐ <b><c>D4</c>, HALF TWO — the EXTERNAL form.</b> Same body, reached without a
    /// <see cref="BTreeContext"/>, because the reset does not always arrive through a tick.
    ///
    /// <para>🔴 <b><c>F14b</c> — why this overload exists.</b> <c>BehaviorIngressSystem</c> zeroes the
    /// host's <c>BrainBTreeState.State</c> on a behaviour change <b>without ticking</b>
    /// (<c>:163</c> on assign-by-name, <c>:235</c> on assign-by-hash). ⇒ <c>SweepExitedNodes</c> never
    /// runs, so the deactivator above never fires, and a hosted child that was <c>Running</c> keeps its
    /// cursor while the host restarts from the root. ⚠ The ingress has <c>(repo, entity)</c> and no
    /// context — hence the split. 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §21.2.</para>
    /// </summary>
    public static void Reset(EntityRepository world, Entity self, int treeStateSlotKey)
    {
        byte* store = OccurrenceStoreAccess.TryGetStore(world, self, out _);
        if (store == null) return;   // ⚠ torn down already — nothing to reset, and not an error

        if (BlueprintBlackboardPartitions.TryGetSlotOffset(store, treeStateSlotKey, out int payloadOffset))
            Unsafe.AsRef<BehaviorTreeState>(store + payloadOffset) = default;
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The manifest test: is this slot a hosted occurrence's tree state?</b>
    ///
    /// <para>⭐ The emitter stamps <c>WorkingStateType = typeof(BehaviorTreeState)</c> on exactly the
    /// slots <c>BTreeBridgeEmitCore.CollectHostedTreeStateSlots</c> adds, and an authored
    /// <c>WorkingState</c> struct can never be that type — so the manifest itself says which slots
    /// carry a CURSOR rather than author state. ⛔ Nothing else in the manifest distinguishes them,
    /// and a caller re-spelling this test is how the two would drift.</para>
    ///
    /// <para>⚠ It is deliberately NARROW: an external reset must clear a hosted <i>cursor</i> and must
    /// NOT clear author working state, which survives a no-op re-assign on purpose
    /// (<c>AttachSlotsToMemory</c>'s idempotent arm).</para>
    /// </summary>
    public static bool IsTreeStateSlot(StatefulSlotInfo slot)
        => slot is not null && slot.WorkingStateType == typeof(BehaviorTreeState);

    /// <summary>
    /// The hosted occurrence's state bytes, as a <c>ref</c> into the entity's store.
    ///
    /// <para>⛔ <b>The reference is valid for the CALLING FRAME ONLY</b> — the seam's lifetime rule.
    /// Native ECS storage means a structural change can move the chunk, so it is resolved per tick
    /// and never cached.</para>
    /// </summary>
    private static ref BehaviorTreeState ResolveState(ref BTreeContext ctx, int treeStateSlotKey)
    {
        byte* store = OccurrenceStoreAccess.TryGetStore(ctx.World, ctx.Self, out _);

        if (store == null)
            throw new InvalidOperationException(
                $"Entity {ctx.Self} carries no occurrence store, so hosted subtree slot " +
                $"{treeStateSlotKey} cannot be resolved. The hosting site's slot must be declared in " +
                "the behaviour's stateful manifest so BehaviorIngressSystem provisions it.");

        if (!BlueprintBlackboardPartitions.TryGetSlotOffset(store, treeStateSlotKey, out int payloadOffset))
            throw new InvalidOperationException(
                $"Entity {ctx.Self} has an occurrence store but no slot {treeStateSlotKey} for the " +
                "hosted subtree's BehaviorTreeState. Either the manifest does not declare it, or the " +
                "hosting site computed a different key than the one registered — see " +
                "OccurrenceSlots.TreeStateKeyFor.");

        return ref Unsafe.AsRef<BehaviorTreeState>(store + payloadOffset);
    }
}
