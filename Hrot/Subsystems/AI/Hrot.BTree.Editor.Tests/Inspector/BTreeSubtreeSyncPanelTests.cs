using System;
using System.Collections.Generic;
using Fbt;
using FluentAssertions;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared.Blackboard;
using Xunit;

namespace Hrot.BTree.Editor.Tests.Inspector;

/// <summary>
/// Tests for the subtree sync panel capabilities of BehaviorTreeAsset:
/// GetSubtreeNodeInfo, GetSyncBindings, SetSyncBinding, ClearSyncBindings (1e-01).
/// </summary>
public sealed class BTreeSubtreeSyncPanelTests
{
    // ---- Helpers ----

    private static BehaviorTreeBlob EmptyBlob() =>
        new BehaviorTreeBlob
        {
            TreeName        = "T",
            Nodes           = Array.Empty<NodeDefinition>(),
            MethodNames     = Array.Empty<string>(),
            FloatParams     = Array.Empty<float>(),
            IntParams       = Array.Empty<int>(),
            SubtreeAssetIds = Array.Empty<string>(),
        };

    private static BehaviorTreeAsset MakeAsset(string name = "Host") =>
        new BehaviorTreeAsset(Guid.NewGuid(), name, $"/{name}.cs", true, "BB", "Ctx", EmptyBlob());

    private static BTreeEditorNode MakeSubtreeNode(bool isResolved = false, Guid subtreeAssetId = default)
    {
        var payload = new BTreeSubtreePayload
        {
            SubtreeName    = "MySub",
            IsResolved     = isResolved,
            SubtreeAssetId = isResolved ? subtreeAssetId : Guid.Empty,
        };
        return new BTreeEditorNode
        {
            VisualId        = Guid.NewGuid(),
            KernelType      = NodeType.Subtree,
            KernelBlobIndex = -1,
            Subtree         = payload,
        };
    }

    private static BTreeEditorNode MakeActionNode() =>
        new BTreeEditorNode
        {
            VisualId        = Guid.NewGuid(),
            KernelType      = NodeType.Action,
            KernelBlobIndex = -1,
            Action          = new BTreeActionPayload { MethodFqn = "Ns.C.M" },
        };

    // ---- GetSubtreeNodeInfo ----

    [Fact]
    public void GetSubtreeNodeInfo_ReturnsNull_ForUnknownGuid()
    {
        var asset = MakeAsset();
        asset.GetSubtreeNodeInfo(Guid.NewGuid()).Should().BeNull();
    }

    [Fact]
    public void GetSubtreeNodeInfo_ReturnsNull_ForNonSubtreeNode()
    {
        var asset  = MakeAsset();
        var action = MakeActionNode();
        asset.AddNode(action);

        asset.GetSubtreeNodeInfo(action.VisualId).Should().BeNull();
    }

    [Fact]
    public void GetSubtreeNodeInfo_ReturnsIsResolved_False_ForUnresolvedSubtreeNode()
    {
        var asset = MakeAsset();
        var node  = MakeSubtreeNode(isResolved: false);
        asset.AddNode(node);

        var info = asset.GetSubtreeNodeInfo(node.VisualId);
        info.Should().NotBeNull();
        info!.IsResolved.Should().BeFalse();
    }

    [Fact]
    public void GetSubtreeNodeInfo_ReturnsIsResolved_True_ForResolvedSubtreeNode()
    {
        var subId = Guid.NewGuid();
        var asset = MakeAsset();
        var node  = MakeSubtreeNode(isResolved: true, subtreeAssetId: subId);
        asset.AddNode(node);

        var info = asset.GetSubtreeNodeInfo(node.VisualId);
        info.Should().NotBeNull();
        info!.IsResolved.Should().BeTrue();
        info.SubtreeAssetId.Should().Be(subId);
    }

    // ---- GetSyncBindings ----

    [Fact]
    public void GetSyncBindings_ReturnsEmpty_ForNewNode()
    {
        var asset = MakeAsset();
        var node  = MakeSubtreeNode(isResolved: true, subtreeAssetId: Guid.NewGuid());
        asset.AddNode(node);

        asset.GetSyncBindings(node.VisualId).Should().BeEmpty();
    }

    // ---- SetSyncBinding ----

    [Fact]
    public void SetSyncBinding_AddsNewBinding()
    {
        var asset   = MakeAsset();
        var node    = MakeSubtreeNode(isResolved: true, subtreeAssetId: Guid.NewGuid());
        asset.AddNode(node);
        var binding = new SubtreeSyncBinding("speed", "MasterSpeed", true, false);

        asset.SetSyncBinding(node.VisualId, binding);

        var bindings = asset.GetSyncBindings(node.VisualId);
        bindings.Should().HaveCount(1);
        bindings[0].FieldName.Should().Be("speed");
        bindings[0].MasterVariableName.Should().Be("MasterSpeed");
        bindings[0].SyncIn.Should().BeTrue();
        bindings[0].SyncOut.Should().BeFalse();
    }

    [Fact]
    public void SetSyncBinding_UpsertsByFieldName()
    {
        var asset   = MakeAsset();
        var node    = MakeSubtreeNode(isResolved: true, subtreeAssetId: Guid.NewGuid());
        asset.AddNode(node);
        asset.SetSyncBinding(node.VisualId, new SubtreeSyncBinding("speed", "A", true, false));

        // Overwrite with updated binding for same field
        asset.SetSyncBinding(node.VisualId, new SubtreeSyncBinding("speed", "B", false, true));

        var bindings = asset.GetSyncBindings(node.VisualId);
        bindings.Should().HaveCount(1);
        bindings[0].MasterVariableName.Should().Be("B");
        bindings[0].SyncIn.Should().BeFalse();
        bindings[0].SyncOut.Should().BeTrue();
    }

    [Fact]
    public void SetSyncBinding_FiresChanged()
    {
        var asset   = MakeAsset();
        var node    = MakeSubtreeNode(isResolved: true, subtreeAssetId: Guid.NewGuid());
        asset.AddNode(node);
        int changed = 0;
        asset.Changed += () => changed++;

        asset.SetSyncBinding(node.VisualId, new SubtreeSyncBinding("speed", "A", true, false));

        changed.Should().BeGreaterThan(0);
    }

    // ---- ClearSyncBindings ----

    [Fact]
    public void ClearSyncBindings_RemovesAllBindingsForNode()
    {
        var asset = MakeAsset();
        var node  = MakeSubtreeNode(isResolved: true, subtreeAssetId: Guid.NewGuid());
        asset.AddNode(node);
        asset.SetSyncBinding(node.VisualId, new SubtreeSyncBinding("a", "A", true, false));
        asset.SetSyncBinding(node.VisualId, new SubtreeSyncBinding("b", "B", false, true));

        asset.ClearSyncBindings(node.VisualId);

        asset.GetSyncBindings(node.VisualId).Should().BeEmpty();
    }

    [Fact]
    public void ClearSyncBindings_IsNoOp_WhenNoBindingsExist()
    {
        var asset = MakeAsset();
        var node  = MakeSubtreeNode(isResolved: true, subtreeAssetId: Guid.NewGuid());
        asset.AddNode(node);
        int changed = 0;
        asset.Changed += () => changed++;

        asset.ClearSyncBindings(node.VisualId);

        changed.Should().Be(0);
    }
}
