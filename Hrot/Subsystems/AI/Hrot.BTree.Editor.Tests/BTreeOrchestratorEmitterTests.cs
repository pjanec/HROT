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

        string? result = BTreeOrchestratorEmitter.Emit(asset, NoSubtreeCatalog.Resolve);

        result.Should().BeNull();
    }

    [Fact]
    public void Emit_ContainsOrchestratorMethod_ForAlias()
    {
        var asset = MakeAsset();
        asset.AddVariable(new BlackboardVariableEntry("SharedFire", typeof(ShootBtDto), null));
        asset.AddAlias("SharedFire", Binding("Shoot_BT", typeof(ShootBtDto)));

        string result = BTreeOrchestratorEmitter.Emit(asset, NoSubtreeCatalog.Resolve)!;

        result.Should().NotBeNull();
        result.Should().Contain("[BTreeAction(Name = \"Orchestrate_Shoot_BT\")]");
        result.Should().Contain("Orchestrate_Shoot_BT_Tick");
        result.Should().Contain("ref master.SharedFire");
    }

    [Fact]
    public void Emit_Deduplicates_SameSubTreeTwoBindings()
    {
        // Two separate element bindings from the same sub-tree on the same variable
        // should produce only one method.
        var assetId = Guid.NewGuid();
        var asset = MakeAsset();
        asset.AddVariable(new BlackboardVariableEntry("SharedFire", typeof(ShootBtDto), null));
        asset.AddAlias("SharedFire", Binding("Shoot_BT", typeof(ShootBtDto), assetId, Guid.NewGuid()));
        asset.AddAlias("SharedFire", Binding("Shoot_BT", typeof(ShootBtDto), assetId, Guid.NewGuid()));

        // AddAlias deduplicates by (assetId, elementId), so use two unique elementIds.
        // The emitter must also deduplicate by (varName, subTreeName).

        string result = BTreeOrchestratorEmitter.Emit(asset, NoSubtreeCatalog.Resolve)!;

        // Count occurrences of the method name.
        int count = CountOccurrences(result, "Orchestrate_Shoot_BT_Tick");
        count.Should().Be(1, "deduplication by (varName, subTreeName) must collapse identical sub-tree names");
    }

    [Fact]
    public void Emit_ContainsTwoMethods_ForTwoDistinctSubTrees()
    {
        var asset = MakeAsset();
        asset.AddVariable(new BlackboardVariableEntry("SharedFire", typeof(ShootBtDto), null));
        asset.AddAlias("SharedFire", Binding("Shoot_BT", typeof(ShootBtDto)));
        asset.AddAlias("SharedFire", Binding("Patrol_BT", typeof(PatrolBtDto)));

        string result = BTreeOrchestratorEmitter.Emit(asset, NoSubtreeCatalog.Resolve)!;

        result.Should().Contain("Orchestrate_Shoot_BT_Tick");
        result.Should().Contain("Orchestrate_Patrol_BT_Tick");
    }

    [Fact]
    public void Emit_OutputIsDeterministic()
    {
        var assetId = new Guid("a1b2c3d4-0001-0000-0000-000000000001");
        var asset = new BehaviorTreeAsset(
            assetId, "MasterAI", "/trees/MasterAI.cs", true,
            "Hrot.Game.MasterBlackboard", "Hrot.Game.MasterContext",
            EmptyBlob(), "Hrot.AI.Behaviors.Trees");
        asset.AddVariable(new BlackboardVariableEntry("SharedFire", typeof(ShootBtDto), null));
        var bindingId = new Guid("b1b2c3d4-0001-0000-0000-000000000002");
        asset.AddAlias("SharedFire", Binding("Shoot_BT", typeof(ShootBtDto), bindingId, bindingId));

        string first  = BTreeOrchestratorEmitter.Emit(asset, NoSubtreeCatalog.Resolve)!;
        string second = BTreeOrchestratorEmitter.Emit(asset, NoSubtreeCatalog.Resolve)!;

        first.Should().Be(second, "emitter output must be deterministic for the same input");
    }

    [Fact]
    public void Emit_StartsWithEditorGeneratedMarker()
    {
        var asset = MakeAsset();
        asset.AddVariable(new BlackboardVariableEntry("SharedFire", typeof(ShootBtDto), null));
        asset.AddAlias("SharedFire", Binding("Shoot_BT", typeof(ShootBtDto)));

        string result = BTreeOrchestratorEmitter.Emit(asset, NoSubtreeCatalog.Resolve)!;

        result.Should().StartWith(
            Hrot.Editor.AiShared.Emit.FluentCSharpEmitterBase.EditorGeneratedMarker);
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

    // ---- T1: returns null when neither Approach A aliases nor Approach B sync groups exist ----

    [Fact]
    public void Emit_ReturnsNull_WhenNoAliasesAndNoSyncGroups()
    {
        var asset = MakeAsset();
        asset.AddVariable(new BlackboardVariableEntry("X", typeof(int), null));

        string? result = BTreeOrchestratorEmitter.Emit(asset, NoSubtreeCatalog.Resolve);

        result.Should().BeNull();
    }

    // ---- T2: emits Approach B method when SyncIn binding exists ----

    [Fact]
    public void Emit_ContainsApproachBMethod_WhenSyncInBinding()
    {
        var asset = MakeAsset();
        var nodeId = Guid.NewGuid();
        RegisterGroup(asset, nodeId, "PatrolBT", "PatrolBlackboard", "Game.AI",
            new[] { new SubtreeSyncBinding("Range", "MasterRange", SyncIn: true, SyncOut: false) });

        string result = BTreeOrchestratorEmitter.Emit(asset, NoSubtreeCatalog.Resolve)!;

        result.Should().Contain("Orchestrate_PatrolBT_Tick");
        result.Should().Contain("[BTreeAction(Name = \"Orchestrate_PatrolBT\")]");
    }

    // ---- T3: emits Approach B method when SyncOut binding exists ----

    [Fact]
    public void Emit_ContainsApproachBMethod_WhenSyncOutBinding()
    {
        var asset = MakeAsset();
        var nodeId = Guid.NewGuid();
        RegisterGroup(asset, nodeId, "ScoutBT", "ScoutBlackboard", null,
            new[] { new SubtreeSyncBinding("Found", "MasterFound", SyncIn: false, SyncOut: true) });

        string result = BTreeOrchestratorEmitter.Emit(asset, NoSubtreeCatalog.Resolve)!;

        result.Should().Contain("Orchestrate_ScoutBT_Tick");
    }

    // ---- T4: sync-in assignments appear before Tick call, sync-out assignments after ----

    [Fact]
    public void Emit_SyncInBeforeTick_SyncOutAfterTick()
    {
        var asset = MakeAsset();
        var nodeId = Guid.NewGuid();
        RegisterGroup(asset, nodeId, "CombatBT", "CombatBlackboard", null,
            new[]
            {
                new SubtreeSyncBinding("Ammo",   "MasterAmmo",   SyncIn: true,  SyncOut: false),
                new SubtreeSyncBinding("Kills",  "MasterKills",  SyncIn: false, SyncOut: true),
            });

        string result = BTreeOrchestratorEmitter.Emit(asset, NoSubtreeCatalog.Resolve)!;

        int syncInPos  = result.IndexOf("subDto.Ammo = master.MasterAmmo",   StringComparison.Ordinal);
        int tickPos    = result.IndexOf("GetInterpreter().Tick",              StringComparison.Ordinal);
        int syncOutPos = result.IndexOf("master.MasterKills = subDto.Kills", StringComparison.Ordinal);

        syncInPos.Should().BePositive();
        tickPos.Should().BeGreaterThan(syncInPos);
        syncOutPos.Should().BeGreaterThan(tickPos);
    }

    // ---- T5: sync-in fields are emitted in alphabetical order ----

    [Fact]
    public void Emit_SyncInFields_InAlphaOrder()
    {
        var asset = MakeAsset();
        var nodeId = Guid.NewGuid();
        RegisterGroup(asset, nodeId, "AssaultBT", "AssaultBlackboard", null,
            new[]
            {
                new SubtreeSyncBinding("Zeal",  "MasterZeal",  SyncIn: true, SyncOut: false),
                new SubtreeSyncBinding("Alpha", "MasterAlpha", SyncIn: true, SyncOut: false),
            });

        string result = BTreeOrchestratorEmitter.Emit(asset, NoSubtreeCatalog.Resolve)!;

        int posAlpha = result.IndexOf("subDto.Alpha", StringComparison.Ordinal);
        int posZeal  = result.IndexOf("subDto.Zeal",  StringComparison.Ordinal);

        posAlpha.Should().BePositive();
        posZeal.Should().BeGreaterThan(posAlpha);
    }

    // ---- T6: binding without master var is skipped entirely ----

    [Fact]
    public void Emit_SkipsBinding_WhenNoMasterVar()
    {
        var asset = MakeAsset();
        var nodeId = Guid.NewGuid();
        // All bindings have no MasterVariableName => no effective sync ops.
        RegisterGroup(asset, nodeId, "IdleBT", "IdleBlackboard", null,
            new[]
            {
                new SubtreeSyncBinding("Phase", MasterVariableName: null, SyncIn: true,  SyncOut: false),
                new SubtreeSyncBinding("Tick",  MasterVariableName: null, SyncIn: false, SyncOut: true),
            });

        string? result = BTreeOrchestratorEmitter.Emit(asset, NoSubtreeCatalog.Resolve);

        result.Should().BeNull("no effective sync ops means no method should be emitted");
    }

    // ---- T7: Approach A alias preempts Approach B for the same sub-tree ----

    [Fact]
    public void Emit_ApproachAPreemptsApproachB_WhenSameSubtreeName()
    {
        var asset = MakeAsset();
        // Register Approach A alias for "PatrolBT".
        asset.AddVariable(new BlackboardVariableEntry("PatrolSlot", typeof(int), null));
        asset.AddAlias("PatrolSlot", new BlackboardAliasBinding(
            Guid.NewGuid(), Guid.NewGuid(), "PatrolBT", "/patrol.cs", typeof(int)));
        // Register an Approach B group for the SAME sub-tree name.
        var nodeId = Guid.NewGuid();
        RegisterGroup(asset, nodeId, "PatrolBT", "PatrolBlackboard", null,
            new[] { new SubtreeSyncBinding("Speed", "MasterSpeed", SyncIn: true, SyncOut: false) });

        string result = BTreeOrchestratorEmitter.Emit(asset, NoSubtreeCatalog.Resolve)!;

        // Should have exactly one method for PatrolBT (the Approach A one).
        int count = 0;
        int idx = 0;
        while ((idx = result.IndexOf("Orchestrate_PatrolBT_Tick", idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += "Orchestrate_PatrolBT_Tick".Length;
        }
        count.Should().Be(1, "Approach A must preempt Approach B for the same sub-tree name");
    }
}

/// <summary>
/// ⭐⭐ <b><c>Q49</c>: an EXPLICIT <i>"I cannot resolve"</i>, not a silent default.</b> These rails build
/// their assets by hand and call <c>RecordSubtreeNodeMeta</c> themselves, so there is no catalog to
/// consult — ⛔ and <c>BehaviorTreeAsset.RecomputeSubtreeSyncIdentity</c> leaves an unresolvable node's
/// identity ALONE, which is why every assertion in this file is unchanged by the pull.
/// <para>⚠ <c>Emit</c>'s resolver is REQUIRED on purpose *(<c>R-126</c>)* — an optional one would rebuild
/// the very failure mode <c>Q49</c> fixes: an identity only some callers bother to supply.</para>
/// <para>⭐ ONE helper for both fixtures in this file — 📌 <c>R-13</c>, even for a one-liner.</para>
/// </summary>
internal static class NoSubtreeCatalog
{
    public static (string Name, string BlackboardTypeName)? Resolve(Guid _) => null;
}
