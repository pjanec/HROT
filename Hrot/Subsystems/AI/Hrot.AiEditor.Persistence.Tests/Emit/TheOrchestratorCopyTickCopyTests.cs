using System;
using System.Collections.Generic;
using FluentAssertions;
using Hrot.AiEditor.Persistence.BTree;
using Hrot.AiEditor.Persistence.Emit;
using Xunit;

namespace Hrot.AiEditor.Persistence.Tests.Emit;

/// <summary>
/// ⭐⭐⭐ <b>Batch 92 (<c>92a</c>) — Approach B's <b>COPY · TICK · COPY</b> shape, asserted as TEXT.</b>
///
/// <para>⛔⛔ <b>Why these rails live at the CORE and not at the generator.</b> The generator provably
/// cannot supply an Approach-B group, and that is measured, not assumed:</para>
///
/// <list type="number">
/// <item>⭐⭐ <c>BehaviorTreeAsset.GetApproachBSyncGroups()</c> (<c>:719</c>) skips every node absent
/// from <c>_syncNodeMeta</c>, whose <b>only</b> writer is <c>InspectorWindow:194</c> — a UI draw. It
/// has no load path, <c>BehaviorTreeAssetDto.cs:10</c> names it deliberately excluded, and
/// <c>BTreeDtoRuntimeFieldExclusionTests:29</c> enforces that exclusion. ⚠ So even in the EDITOR,
/// Approach B emits nothing after a reload until a designer re-opens that panel.</item>
/// <item>⛔⛔ The field the body writes into — <c>master.{Subtree}_{DtoType}</c> — comes from
/// <c>GetAutoAllocatedVariables()</c> (<c>:768</c>), whose only consumer
/// (<c>BlackboardAuthoringWindow:529</c>) merely <b>displays</b> it greyed as
/// <i>"(size unknown until build)"</i>. ⇒ it never reaches <c>Blackboard.Variables</c> and no
/// blackboard emitter declares it.</item>
/// </list>
///
/// <para>⇒ ⭐ the groups are an explicit parameter of <see cref="BTreeOrchestratorEmitCore.Emit"/>;
/// these rails supply them the way the editor does, so ⛔ <b>the algorithm is exercised even though
/// the generated arm cannot reach it yet.</b></para>
/// </summary>
public sealed class TheOrchestratorCopyTickCopyTests
{
    private static BehaviorTreeAssetDto MakeDto() => new()
    {
        AssetId            = Guid.NewGuid(),
        Name               = "Alpha",
        TargetNamespace    = "Hrot.AI.Behaviors.Trees",
        BlackboardTypeName = "Hrot.Game.MasterBlackboard",
        ContextTypeName    = "Hrot.Game.MasterContext",
        Blackboard         = new BlackboardBlockDto
        {
            Managed   = true,
            TypeName  = "MasterBlackboard",
            Variables = { new BlackboardVariableDto { Name = "Health", Type = new() { TypeId = "System.Single" } } },
        },
    };

    private static OrchestratorSyncGroup Group(params OrchestratorSyncBinding[] bindings) =>
        new("PatrolSubTree", "PatrolParams", "Made.Up.Behaviors", bindings);

    private static readonly IReadOnlyList<OrchestratorSyncGroup> NoGroups =
        Array.Empty<OrchestratorSyncGroup>();

    // ══ the null contract ════════════════════════════════════════════════════

    /// <summary>⭐⭐⭐ Nothing to emit ⇒ <c>null</c> ⇒ the caller writes NO file. The corpus's case.</summary>
    [Fact]
    public void NoAliasAndNoSyncGroupEmitsNothingAtAll()
        => BTreeOrchestratorEmitCore.Emit(MakeDto(), NoGroups).Should().BeNull();

    /// <summary>
    /// ⚠ A group whose every binding is INACTIVE is not "something to emit" — ⛔ an empty orchestrator
    /// class would be worse than none.
    /// </summary>
    [Fact]
    public void ASyncGroupWithNoActiveDirectionEmitsNothing()
    {
        var group = Group(new OrchestratorSyncBinding("Target", "Health", syncIn: false, syncOut: false));

        BTreeOrchestratorEmitCore.Emit(MakeDto(), new[] { group }).Should().BeNull();
    }

    /// <summary>⚠ …and neither is a binding that names no master variable to copy from or to.</summary>
    [Fact]
    public void ABindingWithNoMasterVariableIsNotAnActiveSync()
    {
        var group = Group(new OrchestratorSyncBinding("Target", null, syncIn: true, syncOut: true));

        BTreeOrchestratorEmitCore.Emit(MakeDto(), new[] { group }).Should().BeNull();
    }

    // ══ COPY · TICK · COPY ═══════════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>SUPERSEDED <c>2026-09-23</c> (<c>CE-337</c>) — APPROACH B EMITS NOTHING, AND FIVE
    /// SHAPE RAILS WENT WITH IT.</b> 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §32.12.
    ///
    /// <para>🔴 <b>What was removed, named so it is not lost:</b>
    /// <c>SyncInCopiesPrecedeTheTickAndSyncOutCopiesFollowIt</c> ·
    /// <c>TheHostedChildIsNeverTickedWithTheMastersState</c> ·
    /// <c>TheSubDtoIsTakenByRefFromTheAutoAllocatedSliceField</c> ·
    /// <c>CopiesAreOrderedByFieldNameSoTheOutputIsDeterministic</c> ·
    /// <c>AnAliasOnTheSameSubTreeSuppressesTheApproachBMethod</c>. ⛔ Each asserted the SHAPE of
    /// emitted text that no longer exists, and there is no sibling arm to re-home them to — unlike
    /// the de-duplication and <c>DtoTypeId</c>-split claims, which moved rather than died.</para>
    ///
    /// <para>⭐⭐ <b>The ONE claim among them that outlives the emission is already railed elsewhere:</b>
    /// <i>"the hosted child is never ticked with the master's state"</i> is <c>O4</c>/<c>C1</c>'s
    /// invariant, and <c>HostedSubtreeCursorTests.O4_R1</c> pins it at RUNTIME — which is stronger
    /// than pinning the text that used to express it.</para>
    ///
    /// <para>⛔ <b>Why the mechanism went, in one line:</b> both arms project onto a master blackboard
    /// STRUCT, and <c>P4</c> deleted <c>BrainBlackboard</c> — so no asset can satisfy them. Hosting is
    /// per-SITE now (<c>E5</c>).</para>
    /// </summary>
    [Fact]
    public void ApproachBEmitsNothingBecauseTheArmIsRetired_CE337()
    {
        var group = Group(
            new OrchestratorSyncBinding("Ammo",   "MasterAmmo",   syncIn: true,  syncOut: false),
            new OrchestratorSyncBinding("Health", "MasterHealth", syncIn: false, syncOut: true));

        BTreeOrchestratorEmitCore.Emit(MakeDto(), new[] { group }).Should().BeNull(
            "CE-337 retired both orchestrator arms: the emission never compiled (CE-335/CE-336) and "
            + "its `ref master` projection needs a blackboard struct P4 deleted. Sub-tree hosting is "
            + "declared per SITE and ticked by the brain — DESIGN §32.");
    }
}
