using System;
using System.Collections.Generic;
using Fbt;
using FluentAssertions;
using Hrot.BTree.Editor.Emit;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared.Blackboard;
using Xunit;

namespace Hrot.BTree.Editor.Tests;

public sealed class BTreeOrchestratorEmitterTests
{
    // ---- Helpers ----

    // Minimal DTO struct used as binding.DtoType in tests.
    private struct ShootBtDto  { public float Aim   { get; set; } }
    private struct PatrolBtDto { public float Range { get; set; } }

    private static BehaviorTreeBlob EmptyBlob() =>
        new BehaviorTreeBlob
        {
            TreeName = "T", Nodes = Array.Empty<NodeDefinition>(),
            MethodNames = Array.Empty<string>(), FloatParams = Array.Empty<float>(),
            IntParams = Array.Empty<int>(), SubtreeAssetIds = Array.Empty<string>(),
        };

    private static BehaviorTreeAsset MakeAsset(string name = "MasterAI") =>
        new BehaviorTreeAsset(
            Guid.NewGuid(), name, $"/trees/{name}.cs", true,
            "Hrot.Game.MasterBlackboard", "Hrot.Game.MasterContext",
            EmptyBlob(), "Hrot.AI.Behaviors.Trees");

    private static BlackboardAliasBinding Binding(
        string requiringAssetName,
        Type dtoType,
        Guid? assetId = null,
        Guid? elementId = null) =>
        new BlackboardAliasBinding(
            assetId ?? Guid.NewGuid(),
            elementId ?? Guid.NewGuid(),
            requiringAssetName,
            $"/{requiringAssetName}.cs",
            dtoType);

    // ---- Tests ----

    [Fact]
    public void Emit_ReturnsNull_WhenNoAliases()
    {
        var asset = MakeAsset();
        asset.AddVariable(new BlackboardVariableEntry("SharedFire", typeof(ShootBtDto), null));

        BTreeOrchestratorEmitter.Emit(asset, NoSubtreeCatalog.Resolve).Should().BeNull();
    }

    /// <summary>
    /// ⭐⭐⭐ <b>SUPERSEDED <c>2026-09-23</c> (<c>CE-337</c>) — THE APPROACH-A ARM IS RETIRED, AND FIVE
    /// SHAPE RAILS WENT WITH IT.</b> 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §32.12.
    ///
    /// <para>🔴 <b>Removed, named so they are not lost:</b>
    /// <c>Emit_ContainsOrchestratorMethod_ForAlias</c> ·
    /// <c>Emit_Deduplicates_SameSubTreeTwoBindings</c> ·
    /// <c>Emit_ContainsTwoMethods_ForTwoDistinctSubTrees</c> ·
    /// <c>Emit_OutputIsDeterministic</c> · <c>Emit_StartsWithEditorGeneratedMarker</c>.</para>
    ///
    /// <para>⭐⭐ <b>Two of those claims SURVIVE and were re-homed, not dropped:</b> de-duplication and
    /// the deterministic ordering belong to <c>OrchestratorAliasCollector</c>, and
    /// <c>TheOrchestratorIsGeneratedTests.EachUniqueVariableSubTreePairIsCollectedExactlyOnce</c>
    /// asserts the first directly against the collector. ⛔ The rest asserted the SHAPE of text that
    /// no longer exists.</para>
    ///
    /// <para>⛔ <b>Why, in one line:</b> the emission never compiled (<c>CE-335</c>/<c>CE-336</c>), and
    /// its <c>ref master</c> projection needs a blackboard struct <c>P4</c> deleted. Hosting is
    /// per-SITE now (<c>E5</c>).</para>
    /// </summary>
    [Fact]
    public void Emit_ReturnsNull_EvenForAnAlias_BecauseTheArmIsRetired_CE337()
    {
        var asset = MakeAsset();
        asset.AddVariable(new BlackboardVariableEntry("SharedFire", typeof(ShootBtDto), null));
        asset.AddAlias("SharedFire", Binding("Shoot_BT", typeof(ShootBtDto)));

        BTreeOrchestratorEmitter.Emit(asset, NoSubtreeCatalog.Resolve).Should().BeNull(
            "CE-337 retired the Approach-A alias arm: it emitted C# that never compiled, and its "
            + "`ref master` projection needs a blackboard struct P4 deleted. Sub-tree hosting is "
            + "declared per SITE and ticked by the brain — DESIGN §32.");
    }

    // ---- Private helpers ----

    private static int CountOccurrences(string source, string pattern)
    {
        int count = 0;
        int idx = 0;
        while ((idx = source.IndexOf(pattern, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += pattern.Length;
        }
        return count;
    }
}

/// <summary>
/// Tests for the Approach B (field-level sync) orchestrator emit path.
/// </summary>
public sealed class BTreeOrchestratorSyncEmitterTests
{
    // ---- Helpers ----

    private static BehaviorTreeBlob EmptyBlob() =>
        new BehaviorTreeBlob
        {
            TreeName = "T", Nodes = Array.Empty<NodeDefinition>(),
            MethodNames = Array.Empty<string>(), FloatParams = Array.Empty<float>(),
            IntParams = Array.Empty<int>(), SubtreeAssetIds = Array.Empty<string>(),
        };

    private static BehaviorTreeAsset MakeAsset(string name = "MasterAI") =>
        new BehaviorTreeAsset(
            Guid.NewGuid(), name, $"/trees/{name}.cs", true,
            "Hrot.Game.MasterBlackboard", "Hrot.Game.MasterContext",
            EmptyBlob(), "Hrot.AI.Behaviors.Trees");

    private static void RegisterGroup(
        BehaviorTreeAsset asset,
        Guid nodeId,
        string subTreeName,
        string dtoTypeName,
        string? dtoTypeNs,
        IReadOnlyList<SubtreeSyncBinding> bindings)
    {
        asset.RecordSubtreeNodeMeta(nodeId, subTreeName, dtoTypeName, dtoTypeNs);
        asset.LoadSyncBindings(new Dictionary<Guid, IReadOnlyList<SubtreeSyncBinding>>
        {
            [nodeId] = bindings
        });
    }

    // ---- T1: returns null, and since CE-337 that is the ONLY outcome ----

    [Fact]
    public void Emit_ReturnsNull_WhenNoAliasesAndNoSyncGroups()
    {
        var asset = MakeAsset();
        asset.AddVariable(new BlackboardVariableEntry("X", typeof(int), null));

        BTreeOrchestratorEmitter.Emit(asset, NoSubtreeCatalog.Resolve).Should().BeNull();
    }

    /// <summary>
    /// ⭐⭐⭐ <b>SUPERSEDED <c>2026-09-23</c> (<c>CE-337</c>) — APPROACH B IS RETIRED TOO, AND SIX SHAPE
    /// RAILS WENT WITH IT.</b> 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §32.12.
    ///
    /// <para>🔴 <b>Removed:</b> <c>Emit_ContainsApproachBMethod_WhenSyncInBinding</c> ·
    /// <c>…WhenSyncOutBinding</c> · <c>Emit_SyncInBeforeTick_SyncOutAfterTick</c> ·
    /// <c>Emit_SyncInFields_InAlphaOrder</c> · <c>Emit_SkipsBinding_WhenNoMasterVar</c> ·
    /// <c>Emit_ApproachAPreemptsApproachB_WhenSameSubtreeName</c>.</para>
    ///
    /// <para>⚠⚠ <b>Approach B was already dead on its own terms, and its own emitter said so</b>
    /// before any of <c>CE-335</c>/<c>CE-336</c>: the sub-tree IDENTITY is session-local
    /// (<c>_syncNodeMeta</c>, written only by an <c>InspectorWindow</c> draw and deliberately excluded
    /// from the DTO), and the destination FIELD <i>"never reaches Blackboard.Variables and no
    /// blackboard emitter declares it"</i>. ⇒ these rails passed only because the fixture supplied
    /// by hand what production has no path to supply.</para>
    ///
    /// <para>⭐ The ONE claim worth keeping — <i>the hosted child is never ticked with the master's
    /// state</i> — is pinned at RUNTIME by
    /// <c>Fdp.Toolkits.Tests.Behavior.HostedSubtreeCursorTests.O4_R1</c>, which is stronger than
    /// pinning the text that used to express it.</para>
    /// </summary>
    [Fact]
    public void Emit_ReturnsNull_EvenWithSyncGroups_BecauseApproachBIsRetired_CE337()
    {
        var asset = MakeAsset();
        var masterVar = new BlackboardVariableEntry("MasterAmmo", typeof(int), null);
        asset.AddVariable(masterVar);

        var nodeId = Guid.NewGuid();
        RegisterGroup(asset, nodeId, "ShootBT", "ShootDto", "Hrot.Game",
            new[] { new SubtreeSyncBinding("Ammo", "MasterAmmo", SyncIn: true, SyncOut: false) });

        BTreeOrchestratorEmitter.Emit(asset, NoSubtreeCatalog.Resolve).Should().BeNull(
            "CE-337 retired Approach B along with Approach A — same `ref master` projection, same "
            + "missing blackboard struct, and no load path for its bindings in the first place");
    }
}

/// <summary>
/// ⭐⭐ <b><c>Q49</c>: an EXPLICIT <i>"I cannot resolve"</i>, not a silent default.</b> These rails build
/// their assets by hand and call <c>RecordSubtreeNodeMeta</c> themselves, so there is no catalog to
/// consult. ⚠ <c>Emit</c>'s resolver is REQUIRED on purpose *(<c>R-126</c>)* — an optional one would
/// rebuild the very failure mode <c>Q49</c> fixes.
/// <para>⭐ ONE helper for both fixtures in this file — 📌 <c>R-13</c>, even for a one-liner.
/// ⚠ Retained after <c>CE-337</c>: <c>Emit</c> still takes the resolver, and the rails still pass it.</para>
/// </summary>
internal static class NoSubtreeCatalog
{
    public static (string Name, string BlackboardTypeName)? Resolve(Guid _) => null;
}
