using System;
using System.Collections.Generic;
using System.Linq;
using Fbt;
using FluentAssertions;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared.Blackboard;
using Xunit;

namespace Hrot.BTree.Editor.Tests;

public sealed class BTreeAutoAllocationTests
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

    private static void RegisterSyncGroup(
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

    // ---- T1: returns empty when no sync groups ----

    [Fact]
    public void GetAutoAllocatedVariables_ReturnsEmpty_WhenNoSyncGroups()
    {
        var asset = MakeAsset();

        var allocs = asset.GetAutoAllocatedVariables();

        Assert.Empty(allocs);
    }

    // ---- T2: returns one entry when an Approach B sync group exists ----

    [Fact]
    public void GetAutoAllocatedVariables_ReturnsEntry_WhenApproachBSyncGroupExists()
    {
        var asset = MakeAsset();
        var nodeId = Guid.NewGuid();
        RegisterSyncGroup(asset, nodeId, "PatrolBT", "PatrolBlackboard", null,
            new[] { new SubtreeSyncBinding("Range", "MasterRange", SyncIn: true, SyncOut: false) });

        var allocs = asset.GetAutoAllocatedVariables();

        Assert.Single(allocs);
        allocs[0].Name.Should().Be("PatrolBT_PatrolBlackboard");
    }

    // ---- T3: suppresses entry when Approach A alias covers the same node ----

    [Fact]
    public void GetAutoAllocatedVariables_Suppresses_WhenApproachACoversSameNode()
    {
        var asset = MakeAsset();
        // Approach A: alias with the same RequiringElementId as the sync group's NodeVisualId.
        var nodeId = Guid.NewGuid();
        asset.AddVariable(new BlackboardVariableEntry("PatrolSlot", typeof(int), null));
        asset.AddAlias("PatrolSlot", new BlackboardAliasBinding(
            Guid.NewGuid(), nodeId, "PatrolBT", "/patrol.cs", typeof(int)));
        // Approach B: same nodeId.
        RegisterSyncGroup(asset, nodeId, "PatrolBT", "PatrolBlackboard", null,
            new[] { new SubtreeSyncBinding("Range", "MasterRange", SyncIn: true, SyncOut: false) });

        var allocs = asset.GetAutoAllocatedVariables();

        Assert.Empty(allocs);
    }

    // ---- T4: field name is SubtreeName_DtoTypeName ----

    [Fact]
    public void GetAutoAllocatedVariables_FieldName_IsSubtreeNameUnderscoreDtoTypeName()
    {
        var asset = MakeAsset();
        var nodeId = Guid.NewGuid();
        RegisterSyncGroup(asset, nodeId, "CombatBT", "CombatBlackboard", "Game.AI",
            new[] { new SubtreeSyncBinding("HP", "MasterHP", SyncIn: true, SyncOut: false) });

        var allocs = asset.GetAutoAllocatedVariables();

        Assert.Single(allocs);
        allocs[0].Name.Should().Be("CombatBT_CombatBlackboard");
    }

    // ---- T5: returns empty when sync group has no active sync ops ----

    [Fact]
    public void GetAutoAllocatedVariables_ReturnsEmpty_WhenSyncGroupHasNoActiveSyncOps()
    {
        var asset = MakeAsset();
        var nodeId = Guid.NewGuid();
        // Bindings with SyncIn=false, SyncOut=false, MasterVariableName=null => no effective ops.
        // GetApproachBSyncGroups requires at least one active binding (SyncIn or SyncOut AND
        // MasterVariableName != null); this node has none, so it must not appear in the output.
        RegisterSyncGroup(asset, nodeId, "IdleBT", "IdleBlackboard", null,
            new[]
            {
                new SubtreeSyncBinding("Phase", MasterVariableName: null, SyncIn: false, SyncOut: false),
            });

        var allocs = asset.GetAutoAllocatedVariables();

        Assert.Empty(allocs);
    }
}
