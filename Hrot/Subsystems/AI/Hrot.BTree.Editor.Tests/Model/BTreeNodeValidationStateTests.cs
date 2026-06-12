using System;
using FluentAssertions;
using Fbt;
using Hrot.BTree.Editor.Model;
using Hrot.BTree.Editor.Validation;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.BTree.Editor.Tests.Model;

/// <summary>
/// Tests that <see cref="BTreeGraphModel"/> projects per-node validation diagnostics
/// onto <see cref="INodeModel.State"/> and <see cref="INodeModel.StatusTooltip"/>.
/// BATCH-05 / TASK-BT-05.
/// </summary>
public sealed class BTreeNodeValidationStateTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

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

    private static BehaviorTreeAsset MakeAsset() =>
        new BehaviorTreeAsset(Guid.NewGuid(), "T", "/T.cs", true, "BB", "Ctx", EmptyBlob());

    private static BTreeEditorNode MakeNode(NodeType type) =>
        new BTreeEditorNode { VisualId = Guid.NewGuid(), KernelType = type };

    /// <summary>
    /// Builds a <see cref="BTreeGraphModel"/> over <paramref name="asset"/>
    /// and returns the <see cref="INodeModel"/> for the given visual ID.
    /// </summary>
    private static INodeModel? GetNodeModel(BehaviorTreeAsset asset, Guid visualId)
    {
        var graph = new BTreeGraphModel(asset);
        return graph.FindNode(new NodeId(visualId));
    }

    // ── tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void NodeState_EmptyComposite_HasWarningFlagAndTooltip()
    {
        // Root → empty Sequence → Sequence node should have Warning.
        var asset = MakeAsset();
        var root  = MakeNode(NodeType.Root);
        var seq   = MakeNode(NodeType.Sequence); // no children → EmptyComposite

        root.ChildVisualIds.Add(seq.VisualId);
        asset.AddNode(root);
        asset.AddNode(seq);

        // Confirm the validator flags this as EmptyComposite/Warning.
        var diags = new BTreeValidator().Validate(asset);
        diags.Should().ContainSingle(d =>
            d.Code == BTreeDiagnosticCode.EmptyComposite &&
            d.Severity == BTreeDiagnosticSeverity.Warning &&
            d.VisualId == seq.VisualId);

        var model = GetNodeModel(asset, seq.VisualId);
        model.Should().NotBeNull();
        (model!.State & NodeState.Warning).Should().NotBe(0,
            "empty composite should project Warning flag onto the canvas node");
        model.StatusTooltip.Should().NotBeNullOrEmpty(
            "a diagnostic tooltip should be present for the empty composite");
    }

    [Fact]
    public void NodeState_UnboundAction_HasErrorFlagAndTooltip()
    {
        // Root → Sequence → Action (with empty MethodFqn) → Action node should have Error.
        var asset  = MakeAsset();
        var root   = MakeNode(NodeType.Root);
        var seq    = MakeNode(NodeType.Sequence);
        var action = MakeNode(NodeType.Action);
        action.Action = new BTreeActionPayload { MethodFqn = "" }; // unbound

        root.ChildVisualIds.Add(seq.VisualId);
        seq.ChildVisualIds.Add(action.VisualId);
        asset.AddNode(root);
        asset.AddNode(seq);
        asset.AddNode(action);

        // Confirm the validator flags this as UnboundActionMethod/Error.
        var diags = new BTreeValidator().Validate(asset);
        diags.Should().ContainSingle(d =>
            d.Code == BTreeDiagnosticCode.UnboundActionMethod &&
            d.Severity == BTreeDiagnosticSeverity.Error &&
            d.VisualId == action.VisualId);

        var model = GetNodeModel(asset, action.VisualId);
        model.Should().NotBeNull();
        (model!.State & NodeState.Error).Should().NotBe(0,
            "unbound action should project Error flag onto the canvas node");
        model.StatusTooltip.Should().NotBeNullOrEmpty(
            "a diagnostic tooltip should be present for the unbound action");
    }

    [Fact]
    public void NodeState_ValidNode_IsNormalNoTooltip()
    {
        // Root → Sequence → Action (with a valid MethodFqn) → Action node should be Normal.
        var asset  = MakeAsset();
        var root   = MakeNode(NodeType.Root);
        var seq    = MakeNode(NodeType.Sequence);
        var action = MakeNode(NodeType.Action);
        action.Action = new BTreeActionPayload { MethodFqn = "Ns.C.ValidMethod" };

        root.ChildVisualIds.Add(seq.VisualId);
        seq.ChildVisualIds.Add(action.VisualId);
        asset.AddNode(root);
        asset.AddNode(seq);
        asset.AddNode(action);

        // Confirm the validator produces no diagnostics for this valid tree.
        var diags = new BTreeValidator().Validate(asset);
        diags.Should().BeEmpty(
            "a valid tree with bound action should have no diagnostics");

        var model = GetNodeModel(asset, action.VisualId);
        model.Should().NotBeNull();
        model!.State.Should().Be(NodeState.Normal,
            "a validly bound action should project Normal state");
        model.StatusTooltip.Should().BeNull(
            "a valid node should have no tooltip");
    }

    [Fact]
    public void NodeState_RecomputesOnChanged()
    {
        // Root → empty Sequence → Sequence has Warning.
        // After adding a child to the Sequence and firing Changed, it clears.
        var asset = MakeAsset();
        var root  = MakeNode(NodeType.Root);
        var seq   = MakeNode(NodeType.Sequence); // no children → EmptyComposite

        root.ChildVisualIds.Add(seq.VisualId);
        asset.AddNode(root);
        asset.AddNode(seq);

        var graph = new BTreeGraphModel(asset);
        var seqModel = graph.FindNode(new NodeId(seq.VisualId));
        seqModel.Should().NotBeNull();

        // Initially empty composite → Warning.
        (seqModel!.State & NodeState.Warning).Should().NotBe(0,
            "empty composite should initially project Warning");
        seqModel.StatusTooltip.Should().NotBeNullOrEmpty();

        // Add a valid child to the Sequence — the empty-composite diagnostic should clear.
        var action = MakeNode(NodeType.Action);
        action.Action = new BTreeActionPayload { MethodFqn = "Ns.C.ValidMethod" };
        seq.ChildVisualIds.Add(action.VisualId);
        asset.AddNode(action);
        asset.MarkDirty(); // fires Changed → triggers OnAssetChanged → BuildCaches

        // Re-read from the same graph model (it was rebuilt on Changed).
        seqModel = graph.FindNode(new NodeId(seq.VisualId));
        seqModel.Should().NotBeNull();
        seqModel!.State.Should().Be(NodeState.Normal,
            "after adding a child, the sequence should no longer be flagged as empty");
        seqModel.StatusTooltip.Should().BeNull(
            "after the diagnostic clears, the tooltip should be null");
    }
}
