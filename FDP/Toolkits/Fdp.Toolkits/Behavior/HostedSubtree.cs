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
    /// <para>⭐ <b><c>D4</c> — re-entry reset (<c>F14</c>).</b> When the child returns anything but
    /// <c>Running</c> its state is cleared, because the host has left the hosting node by definition.
    /// ⚠ Own state removes the accidental continuity <c>ref state</c> used to provide, so without this
    /// a completed child would resume mid-tree the next time the host entered.</para>
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

        // D4 — the host has left the hosting node; the child starts fresh next entry.
        if (status != NodeStatus.Running)
            state = default;

        return status;
    }

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
