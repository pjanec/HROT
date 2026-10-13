using System;
using System.Collections.Generic;
using System.Linq;
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

    // ── CE-3193: working state is derived from the bindings ──────────────────

    /// <summary>⭐ <c>CE-3193</c> — working state is the variables a binding names as its <c>WorkingStateTargetField</c>;
    /// nothing about it is authored on the variable.</summary>
    [Fact]
    public void WorkingStateVariableNames_are_the_ones_a_binding_names()
    {
        var asset = MakeAsset();
        asset.AddVariable(new Hrot.Editor.AiShared.Blackboard.BlackboardVariableEntry("Cursor", typeof(int), Comment: null));
        asset.AddVariable(new Hrot.Editor.AiShared.Blackboard.BlackboardVariableEntry("Plain", typeof(int), Comment: null));
        asset.WorkingStateVariableNames().Should().BeEmpty("no binding names either yet");
    }

    // ── CE-1061 (U5a, R-3001): naming the params DTO ──────────────────────────

    public struct DtoA { public float Radius; }
    public struct DtoB { public int Rounds; }

    /// <summary>⭐ <c>CE-1061</c> (U5a, acceptance 4 — the half that is built): picking a DTO writes <c>ParamsDtoTypeId</c>, rebuilds the
    /// reserved <c>Params</c> entry over the new type, clears the old DTO's default overrides and marks the asset dirty; re-picking the
    /// same DTO changes nothing.</summary>
    [Fact]
    public void CE1061_PickingAParamsDto_WritesTheTypeId_AndRebuildsTheParamsEntry()
    {
        var asset = MakeAsset();
        asset.IsBlackboardEditorManaged = true;
        asset.ParamsDtoTypeId = typeof(DtoA).FullName;
        asset.ParamsDefaultJson = "{\"radius\":3}";
        asset.SetBlackboardVariables(new[]
        {
            new Hrot.Editor.AiShared.Blackboard.BlackboardVariableEntry("Params", typeof(DtoA), null, DefaultValueJson: "{\"radius\":3}", IsReservedParams: true),
            new Hrot.Editor.AiShared.Blackboard.BlackboardVariableEntry("Counter", typeof(int), null),
        });
        int changed = 0;
        asset.Changed += () => changed++;

        ((Hrot.Editor.AiShared.Blackboard.IParamsDtoAsset)asset).SetParamsDto(typeof(DtoB).FullName!);

        asset.ParamsDtoTypeId.Should().Be(typeof(DtoB).FullName);
        asset.ParamsDefaultJson.Should().BeNull("DtoA's overrides name DtoA's fields");
        var reserved = asset.BlackboardVariables.Single(v => v.IsReservedParams);
        reserved.FieldType.Should().Be(typeof(DtoB));
        reserved.DefaultValueJson.Should().BeNull();
        asset.BlackboardVariables.Select(v => v.Name).Should().Contain("Counter");
        asset.IsDirty.Should().BeTrue();
        changed.Should().BeGreaterThan(0);

        int before = changed;
        asset.SetParamsDto(typeof(DtoB).FullName!);
        changed.Should().Be(before, "the same DTO again is a no-op");
    }
}
