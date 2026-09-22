using System;
using Fbt;
using Fdp.Core;
using Fdp.Presentation.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Blueprints.Partitioning;
using ImGuiNET;

namespace Hrot.Presentation.Renderers;

/// <summary>
/// ⭐⭐⭐ <b><c>O7c</c>-② / <c>CE-319</c> — the ROOT BTREE EXECUTION PATH as a section of the tier
/// renderer.</b> 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §31.
///
/// <para>⛔⛔ <b>Why this type exists at all.</b> <c>BTreeVisualizerRenderer</c> was reached through
/// <c>[ImGuiRenderer(typeof(BrainBTreeState))]</c> — keyed on the component's IDENTITY, so deleting
/// the component deleted the ENTRY POINT rather than merely a read. ⭐ That is the <c>CE-303</c>
/// shape, and this is the remedy <c>P4</c>-③ established for the four surfaces before it: a section
/// invoked from <see cref="BlueprintBlackboardRendererBase"/>, reading through
/// <see cref="RootStateAccess"/>.</para>
///
/// <para>⭐ <b>The deliberate twin of <see cref="RootParamsProjection"/></b> — same resolve-then-draw
/// shape, same silent return when the entity has no behaviour or no slot. ⚠ A debug surface draws
/// what is there; ⛔ the loud form belongs to the execution path
/// (<c>RootStateAccess.RequireStateRef</c>), never here.</para>
/// </summary>
public static class RootTreeStateProjection
{
    /// <summary>
    /// Renders the "BTree execution path" section for <paramref name="entity"/>, reading the cursor
    /// out of the root state slot inside <paramref name="memory"/> — the tier component's raw base.
    ///
    /// <para>⚠ Silent no-op when the entity is not a BTree brain, has no behaviour assigned, or has
    /// no root state slot yet. ⭐ All three are ordinary states of a live entity, not faults.</para>
    /// </summary>
    public static unsafe void RenderRootTree(IInspectableSession session, Entity entity, byte* memory)
    {
        if (session == null || memory == null) return;

        var registry = BlueprintBlackboardRenderers.BehaviorRegistry;
        if (registry == null) return;

        if (!session.HasComponent(entity, typeof(BehaviorState))) return;
        if (session.GetComponent(entity, typeof(BehaviorState)) is not BehaviorState bs) return;
        if (bs.ActiveBehaviorHash == 0) return;

        // ⭐ Ask the DEFINITION whether this is a BTree brain, rather than inferring from a lookup
        //   miss — the same checkable-predicate discipline CE-307 forced on the params path.
        if (!registry.TryGetDefinition(bs.ActiveBehaviorHash, out var def) || def == null) return;
        if (def.BrainTier != BehaviorConstants.BrainTierBTree) return;

        int key = RootStateAccess.KeyForBehaviour(bs.ActiveBehaviorHash);
        if (key == 0) return;

        if (!BlueprintBlackboardPartitions.TryGetSlotOffset(memory, key, out int payloadOffset, out _))
            return;

        var state = *(BehaviorTreeState*)(memory + payloadOffset);

        ImGui.Separator();
        string summary = BTreeVisualizerRenderer.GetSummary(session, entity, in state) ?? "BTree";
        if (!ImGui.CollapsingHeader($"BTree execution path — {summary}"))
            return;

        BTreeVisualizerRenderer.RenderTree(session, entity, in state, out _);
    }
}
