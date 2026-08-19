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
/// from <c>_syncNodeMeta</c>, whose <b>only</b> writer is <c>InspectorWindow:590</c> — a UI draw. It
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
    /// ⭐⭐⭐ <b>THE rail — the ORDER is the contract.</b> ⛔ A sync-out that ran before the tick would
    /// copy back last frame's value; a sync-in after it would arrive too late. ⇒ the assertion is on
    /// the INDICES, not on mere presence.
    /// </summary>
    [Fact]
    public void SyncInCopiesPrecedeTheTickAndSyncOutCopiesFollowIt()
    {
        var group = Group(
            new OrchestratorSyncBinding("InField",  "Health", syncIn: true,  syncOut: false),
            new OrchestratorSyncBinding("OutField", "Health", syncIn: false, syncOut: true));

        string text = BTreeOrchestratorEmitCore.Emit(MakeDto(), new[] { group })!;

        int copyIn  = text.IndexOf("subDto.InField = master.Health;", StringComparison.Ordinal);
        int tick    = text.IndexOf("var result = PatrolSubTree.GetInterpreter().Tick(", StringComparison.Ordinal);
        int copyOut = text.IndexOf("master.Health = subDto.OutField;", StringComparison.Ordinal);

        copyIn.Should().BeGreaterThan(0,  "the sync-in copy must be emitted");
        tick.Should().BeGreaterThan(copyIn,  "⛔ the tick must come AFTER every sync-in copy");
        copyOut.Should().BeGreaterThan(tick, "⛔ the sync-out copy must come AFTER the tick");
    }

    /// <summary>
    /// ⭐⭐ <b>The slice field is <c>{SubtreeName}_{DtoTypeName}</c></b> — the auto-allocated master
    /// slot the sub-tree's blackboard lives in. ⚠ 📌 <b>Nothing declares this field today</b> (see the
    /// type remarks); this rail pins the NAME the emitter expects, so whoever closes that gap knows
    /// exactly what to declare.
    /// </summary>
    [Fact]
    public void TheSubDtoIsTakenByRefFromTheAutoAllocatedSliceField()
    {
        var group = Group(new OrchestratorSyncBinding("InField", "Health", syncIn: true, syncOut: false));

        string text = BTreeOrchestratorEmitCore.Emit(MakeDto(), new[] { group })!;

        text.Should().Contain("ref var subDto = ref master.PatrolSubTree_PatrolParams;");
        text.Should().Contain("[BTreeAction(Name = \"Orchestrate_PatrolSubTree\")]");
        text.Should().Contain("return result;", "the sub-tree's status is the orchestrator's status");
    }

    /// <summary>⭐ Bindings are ordered by field name, so the emitted text is deterministic.</summary>
    [Fact]
    public void CopiesAreOrderedByFieldNameSoTheOutputIsDeterministic()
    {
        var group = Group(
            new OrchestratorSyncBinding("Zulu",  "Health", syncIn: true, syncOut: false),
            new OrchestratorSyncBinding("Alpha", "Health", syncIn: true, syncOut: false));

        string text = BTreeOrchestratorEmitCore.Emit(MakeDto(), new[] { group })!;

        text.IndexOf("subDto.Alpha =", StringComparison.Ordinal)
            .Should().BeLessThan(text.IndexOf("subDto.Zulu =", StringComparison.Ordinal));
    }

    /// <summary>
    /// ⭐⭐ <b>Approach A WINS.</b> When an alias already covers the same sub-tree, the Approach-B
    /// method is suppressed — ⛔ two methods with the same <c>[BTreeAction]</c> name would collide in
    /// the registry.
    /// </summary>
    [Fact]
    public void AnAliasOnTheSameSubTreeSuppressesTheApproachBMethod()
    {
        var dto = MakeDto();
        dto.Aliases = new Dictionary<string, List<BlackboardAliasBindingDto>>
        {
            ["Health"] = new()
            {
                new BlackboardAliasBindingDto
                {
                    RequiringAssetName = "PatrolSubTree",
                    DtoTypeId          = "Made.Up.Behaviors.PatrolParams",
                },
            },
        };
        var group = Group(new OrchestratorSyncBinding("InField", "Health", syncIn: true, syncOut: false));

        string text = BTreeOrchestratorEmitCore.Emit(dto, new[] { group })!;

        text.Should().Contain("Orchestrate_PatrolSubTree_Tick(");
        text.Should().NotContain("ref var subDto = ref master.",
            "⛔ the Approach-B body must not be emitted for a sub-tree Approach A already covers");
    }
}
