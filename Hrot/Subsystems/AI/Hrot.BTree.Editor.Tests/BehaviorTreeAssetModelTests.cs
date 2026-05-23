using System;
using System.Collections.Generic;
using FluentAssertions;
using Fbt;
using Hrot.BTree.Editor.Model;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.BTree.Editor.Tests;

public sealed class BehaviorTreeAssetModelTests
{
    private static BehaviorTreeBlob EmptyBlob() =>
        new BehaviorTreeBlob
        {
            TreeName = "test",
            Nodes    = Array.Empty<NodeDefinition>(),
            MethodNames = Array.Empty<string>(),
            FloatParams = Array.Empty<float>(),
            IntParams   = Array.Empty<int>(),
            SubtreeAssetIds = Array.Empty<string>(),
        };

    private static BehaviorTreeAsset MakeAsset() =>
        new BehaviorTreeAsset(
            Guid.NewGuid(),
            "TestTree",
            "/trees/TestTree.cs",
            true,
            "MyBlackboard",
            "MyContext",
            EmptyBlob());

    // ── BT-S1-01: Asset properties ────────────────────────────────────────────

    [Fact]
    public void AssetKind_is_BTree()
    {
        var asset = MakeAsset();
        asset.Kind.Should().Be(Hrot.Editor.AiShared.AssetKind.BTree);
    }

    [Fact]
    public void IsDirty_is_false_initially()
    {
        var asset = MakeAsset();
        asset.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void MarkDirty_sets_dirty_and_raises_event()
    {
        var asset  = MakeAsset();
        var raised = false;
        asset.Changed += () => raised = true;

        asset.MarkDirty();

        asset.IsDirty.Should().BeTrue();
        raised.Should().BeTrue();
    }

    [Fact]
    public void ClearDirty_resets_dirty_flag()
    {
        var asset = MakeAsset();
        asset.MarkDirty();
        asset.ClearDirty();
        asset.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void IsLeaf_is_true_for_leaf_nodes()
    {
        new BTreeEditorNode { KernelType = NodeType.Action    }.IsLeaf.Should().BeTrue();
        new BTreeEditorNode { KernelType = NodeType.Condition }.IsLeaf.Should().BeTrue();
        new BTreeEditorNode { KernelType = NodeType.Wait      }.IsLeaf.Should().BeTrue();
        new BTreeEditorNode { KernelType = NodeType.Subtree   }.IsLeaf.Should().BeTrue();
    }

    [Fact]
    public void IsLeaf_is_false_for_composites()
    {
        new BTreeEditorNode { KernelType = NodeType.Sequence }.IsLeaf.Should().BeFalse();
        new BTreeEditorNode { KernelType = NodeType.Selector }.IsLeaf.Should().BeFalse();
        new BTreeEditorNode { KernelType = NodeType.Root     }.IsLeaf.Should().BeFalse();
    }

    // ── BT-S1-03: Lookup tables ───────────────────────────────────────────────

    [Fact]
    public void FindNode_returns_added_node()
    {
        var asset = MakeAsset();
        var id    = Guid.NewGuid();
        var node  = new BTreeEditorNode { VisualId = id, KernelBlobIndex = 0, KernelType = NodeType.Sequence };

        asset.AddNode(node);

        asset.FindNode(id).Should().BeSameAs(node);
    }

    [Fact]
    public void FindNode_returns_null_for_missing()
    {
        var asset = MakeAsset();
        asset.FindNode(Guid.NewGuid()).Should().BeNull();
    }

    [Fact]
    public void FindBlobIndex_returns_index_for_known_node()
    {
        var asset = MakeAsset();
        var id    = Guid.NewGuid();
        asset.AddNode(new BTreeEditorNode { VisualId = id, KernelBlobIndex = 7, KernelType = NodeType.Action });

        asset.FindBlobIndex(id).Should().Be(7);
    }

    [Fact]
    public void FindBlobIndex_returns_minus_one_for_unknown()
    {
        var asset = MakeAsset();
        asset.FindBlobIndex(Guid.NewGuid()).Should().Be(-1);
    }

    [Fact]
    public void FindPill_returns_added_pill()
    {
        var asset     = MakeAsset();
        var pillId    = Guid.NewGuid();
        var hostId    = Guid.NewGuid();
        var pill      = new BTreeEditorPill { VisualId = pillId, HostNodeVisualId = hostId, DecoratorType = NodeType.Inverter };

        asset.AddPill(pill);

        asset.FindPill(pillId).Should().BeSameAs(pill);
    }

    [Fact]
    public void RemoveNode_removes_from_collection_and_lookups()
    {
        var asset = MakeAsset();
        var id    = Guid.NewGuid();
        asset.AddNode(new BTreeEditorNode { VisualId = id, KernelBlobIndex = 2, KernelType = NodeType.Sequence });

        var removed = asset.RemoveNode(id);

        removed.Should().BeTrue();
        asset.FindNode(id).Should().BeNull();
        asset.Nodes.Should().BeEmpty();
    }

    [Fact]
    public void ReplaceAll_rebuilds_lookups()
    {
        var asset  = MakeAsset();
        var idA    = Guid.NewGuid();
        var idB    = Guid.NewGuid();
        var nodes  = new List<BTreeEditorNode>
        {
            new() { VisualId = idA, KernelBlobIndex = 0, KernelType = NodeType.Root },
            new() { VisualId = idB, KernelBlobIndex = 1, KernelType = NodeType.Action },
        };

        asset.ReplaceAll(nodes, new List<BTreeEditorPill>(), EmptyBlob());

        asset.Nodes.Should().HaveCount(2);
        asset.FindNode(idA).Should().NotBeNull();
        asset.FindNode(idB).Should().NotBeNull();
        asset.FindBlobIndex(idA).Should().Be(0);
        asset.FindBlobIndex(idB).Should().Be(1);
    }
}
