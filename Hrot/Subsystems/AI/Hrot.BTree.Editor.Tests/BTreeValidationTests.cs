using System;
using System.Collections.Generic;
using FluentAssertions;
using Fbt;
using Hrot.BTree.Editor.Model;
using Hrot.BTree.Editor.Validation;
using Xunit;

namespace Hrot.BTree.Editor.Tests;

/// <summary>
/// Tests for BT-S1-16: BTree validation rules.
/// BTH §11.
/// </summary>
public sealed class BTreeValidationTests
{
    // ---- Helpers ------------------------------------------------------------

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

    private static IReadOnlyList<BTreeDiagnostic> Validate(BehaviorTreeAsset asset) =>
        new BTreeValidator().Validate(asset);

    // ---- Tests --------------------------------------------------------------

    [Fact]
    public void Validate_valid_tree_returns_no_diagnostics()
    {
        var asset = MakeAsset();
        var root   = MakeNode(NodeType.Root);
        var seq    = MakeNode(NodeType.Sequence);
        var action = MakeNode(NodeType.Action);
        action.Action = new BTreeActionPayload { MethodFqn = "Ns.Class.Method" };

        root.ChildVisualIds.Add(seq.VisualId);
        seq.ChildVisualIds.Add(action.VisualId);

        asset.AddNode(root);
        asset.AddNode(seq);
        asset.AddNode(action);

        Validate(asset).Should().BeEmpty();
    }

    [Fact]
    public void Validate_empty_sequence_returns_warning()
    {
        var asset = MakeAsset();
        var root = MakeNode(NodeType.Root);
        var seq  = MakeNode(NodeType.Sequence); // no children

        root.ChildVisualIds.Add(seq.VisualId);
        asset.AddNode(root);
        asset.AddNode(seq);

        var diagnostics = Validate(asset);

        diagnostics.Should().ContainSingle(d =>
            d.Code == BTreeDiagnosticCode.EmptyComposite &&
            d.Severity == BTreeDiagnosticSeverity.Warning &&
            d.VisualId == seq.VisualId);
    }

    [Fact]
    public void Validate_empty_selector_returns_warning()
    {
        var asset = MakeAsset();
        var root = MakeNode(NodeType.Root);
        var sel  = MakeNode(NodeType.Selector);

        root.ChildVisualIds.Add(sel.VisualId);
        asset.AddNode(root);
        asset.AddNode(sel);

        Validate(asset).Should().ContainSingle(d =>
            d.Code == BTreeDiagnosticCode.EmptyComposite &&
            d.VisualId == sel.VisualId);
    }

    [Fact]
    public void Validate_action_with_empty_fqn_returns_error()
    {
        var asset  = MakeAsset();
        var root   = MakeNode(NodeType.Root);
        var seq    = MakeNode(NodeType.Sequence);
        var action = MakeNode(NodeType.Action);
        action.Action = new BTreeActionPayload { MethodFqn = "" };

        root.ChildVisualIds.Add(seq.VisualId);
        seq.ChildVisualIds.Add(action.VisualId);

        asset.AddNode(root);
        asset.AddNode(seq);
        asset.AddNode(action);

        Validate(asset).Should().ContainSingle(d =>
            d.Code == BTreeDiagnosticCode.UnboundActionMethod &&
            d.Severity == BTreeDiagnosticSeverity.Error &&
            d.VisualId == action.VisualId);
    }

    [Fact]
    public void Validate_condition_with_empty_fqn_returns_error()
    {
        var asset     = MakeAsset();
        var root      = MakeNode(NodeType.Root);
        var seq       = MakeNode(NodeType.Sequence);
        var condition = MakeNode(NodeType.Condition);
        condition.Condition = new BTreeConditionPayload { MethodFqn = "" };

        root.ChildVisualIds.Add(seq.VisualId);
        seq.ChildVisualIds.Add(condition.VisualId);

        asset.AddNode(root);
        asset.AddNode(seq);
        asset.AddNode(condition);

        Validate(asset).Should().ContainSingle(d =>
            d.Code == BTreeDiagnosticCode.UnboundConditionMethod &&
            d.Severity == BTreeDiagnosticSeverity.Error);
    }

    [Fact]
    public void Validate_repeater_pill_with_zero_count_returns_warning()
    {
        var asset  = MakeAsset();
        var root   = MakeNode(NodeType.Root);
        var seq    = MakeNode(NodeType.Sequence);
        var action = MakeNode(NodeType.Action);
        action.Action = new BTreeActionPayload { MethodFqn = "Ns.C.M" };

        root.ChildVisualIds.Add(seq.VisualId);
        seq.ChildVisualIds.Add(action.VisualId);

        var pill = new BTreeEditorPill
        {
            VisualId         = Guid.NewGuid(),
            HostNodeVisualId = seq.VisualId,
            DecoratorType    = NodeType.Repeater,
            IntParam         = 0,   // invalid
            StackIndex       = 0,
        };

        asset.AddNode(root);
        asset.AddNode(seq);
        asset.AddNode(action);
        asset.AddPill(pill);

        Validate(asset).Should().ContainSingle(d =>
            d.Code == BTreeDiagnosticCode.RepeaterCountInvalid &&
            d.Severity == BTreeDiagnosticSeverity.Warning &&
            d.VisualId == pill.VisualId);
    }

    [Fact]
    public void Validate_wait_with_zero_duration_returns_warning()
    {
        var asset = MakeAsset();
        var root  = MakeNode(NodeType.Root);
        var seq   = MakeNode(NodeType.Sequence);
        var wait  = MakeNode(NodeType.Wait);
        wait.Wait = new BTreeWaitPayload { Duration = 0f };

        root.ChildVisualIds.Add(seq.VisualId);
        seq.ChildVisualIds.Add(wait.VisualId);

        asset.AddNode(root);
        asset.AddNode(seq);
        asset.AddNode(wait);

        Validate(asset).Should().ContainSingle(d =>
            d.Code == BTreeDiagnosticCode.WaitDurationInvalid &&
            d.Severity == BTreeDiagnosticSeverity.Warning &&
            d.VisualId == wait.VisualId);
    }

    [Fact]
    public void Validate_unresolved_subtree_returns_error()
    {
        var asset   = MakeAsset();
        var root    = MakeNode(NodeType.Root);
        var seq     = MakeNode(NodeType.Sequence);
        var subtree = MakeNode(NodeType.Subtree);
        subtree.Subtree = new BTreeSubtreePayload
        {
            SubtreeName = "MissingTree",
            IsResolved  = false,
        };

        root.ChildVisualIds.Add(seq.VisualId);
        seq.ChildVisualIds.Add(subtree.VisualId);

        asset.AddNode(root);
        asset.AddNode(seq);
        asset.AddNode(subtree);

        Validate(asset).Should().ContainSingle(d =>
            d.Code == BTreeDiagnosticCode.UnresolvedSubtree &&
            d.Severity == BTreeDiagnosticSeverity.Error &&
            d.VisualId == subtree.VisualId);
    }

    [Fact]
    public void Validate_orphaned_node_returns_warning()
    {
        var asset   = MakeAsset();
        var root    = MakeNode(NodeType.Root);
        var orphan  = MakeNode(NodeType.Action);
        orphan.Action = new BTreeActionPayload { MethodFqn = "Ns.C.M" };

        // Root has no children; orphan is not linked.
        asset.AddNode(root);
        asset.AddNode(orphan);

        Validate(asset).Should().ContainSingle(d =>
            d.Code == BTreeDiagnosticCode.OrphanedNode &&
            d.Severity == BTreeDiagnosticSeverity.Warning &&
            d.VisualId == orphan.VisualId);
    }

    [Fact]
    public void Validate_cycle_detected_returns_error()
    {
        var asset = MakeAsset();
        var root  = MakeNode(NodeType.Root);
        var seqA  = MakeNode(NodeType.Sequence);
        var seqB  = MakeNode(NodeType.Sequence);

        root.ChildVisualIds.Add(seqA.VisualId);
        seqA.ChildVisualIds.Add(seqB.VisualId);
        seqB.ChildVisualIds.Add(seqA.VisualId); // cycle: B -> A -> B

        asset.AddNode(root);
        asset.AddNode(seqA);
        asset.AddNode(seqB);

        Validate(asset).Should().ContainSingle(d =>
            d.Code == BTreeDiagnosticCode.CycleDetected &&
            d.Severity == BTreeDiagnosticSeverity.Error);
    }

    [Fact]
    public void Validate_depth_exceeded_returns_warning()
    {
        // Build a chain of 10 sequences (depth 10 > MaxAllowedDepth=8).
        var asset = MakeAsset();
        var root  = MakeNode(NodeType.Root);
        asset.AddNode(root);

        var current = root;
        for (int i = 0; i < 10; i++)
        {
            var seq = MakeNode(NodeType.Sequence);
            asset.AddNode(seq);
            current.ChildVisualIds.Add(seq.VisualId);
            current = seq;
        }

        Validate(asset).Should().Contain(d =>
            d.Code == BTreeDiagnosticCode.StackDepthExceeded &&
            d.Severity == BTreeDiagnosticSeverity.Warning);
    }

    // ---- DEC-06 Part 3: NestedRepeater / NestedParallel ----------------------

    [Fact]
    public void Validate_two_repeater_pills_on_one_node_returns_nested_repeater_error()
    {
        // Arrange: Root → Sequence with TWO Repeater pills (same-node stacking).
        var asset = MakeAsset();
        var root  = MakeNode(NodeType.Root);
        var seq   = MakeNode(NodeType.Sequence);
        root.ChildVisualIds.Add(seq.VisualId);
        asset.AddNode(root);
        asset.AddNode(seq);

        var pill1 = new BTreeEditorPill
        {
            VisualId         = Guid.NewGuid(),
            HostNodeVisualId = seq.VisualId,
            DecoratorType    = NodeType.Repeater,
            IntParam         = 2,
            StackIndex       = 0,
        };
        var pill2 = new BTreeEditorPill
        {
            VisualId         = Guid.NewGuid(),
            HostNodeVisualId = seq.VisualId,
            DecoratorType    = NodeType.Repeater,
            IntParam         = 3,
            StackIndex       = 1,
        };
        asset.AddPill(pill1);
        asset.AddPill(pill2);

        var diagnostics = Validate(asset);

        diagnostics.Should().Contain(d =>
            d.Code == BTreeDiagnosticCode.NestedRepeater &&
            d.Severity == BTreeDiagnosticSeverity.Error);
    }

    [Fact]
    public void Validate_repeater_pill_under_repeater_pilled_ancestor_returns_nested_repeater_error()
    {
        // Arrange: Root → SeqA (Repeater pill) → SeqB (Repeater pill)
        var asset = MakeAsset();
        var root  = MakeNode(NodeType.Root);
        var seqA  = MakeNode(NodeType.Sequence);
        var seqB  = MakeNode(NodeType.Sequence);
        root.ChildVisualIds.Add(seqA.VisualId);
        seqA.ChildVisualIds.Add(seqB.VisualId);
        asset.AddNode(root);
        asset.AddNode(seqA);
        asset.AddNode(seqB);

        var pillA = new BTreeEditorPill
        {
            VisualId         = Guid.NewGuid(),
            HostNodeVisualId = seqA.VisualId,
            DecoratorType    = NodeType.Repeater,
            IntParam         = 2,
            StackIndex       = 0,
        };
        var pillB = new BTreeEditorPill
        {
            VisualId         = Guid.NewGuid(),
            HostNodeVisualId = seqB.VisualId,
            DecoratorType    = NodeType.Repeater,
            IntParam         = 2,
            StackIndex       = 0,
        };
        asset.AddPill(pillA);
        asset.AddPill(pillB);

        var diagnostics = Validate(asset);

        diagnostics.Should().Contain(d =>
            d.Code == BTreeDiagnosticCode.NestedRepeater &&
            d.Severity == BTreeDiagnosticSeverity.Error);
    }

    [Fact]
    public void Validate_single_repeater_pill_returns_no_nested_repeater_error()
    {
        // Arrange: Root → Sequence with exactly one Repeater pill — valid.
        var asset = MakeAsset();
        var root  = MakeNode(NodeType.Root);
        var seq   = MakeNode(NodeType.Sequence);
        root.ChildVisualIds.Add(seq.VisualId);
        asset.AddNode(root);
        asset.AddNode(seq);

        var pill = new BTreeEditorPill
        {
            VisualId         = Guid.NewGuid(),
            HostNodeVisualId = seq.VisualId,
            DecoratorType    = NodeType.Repeater,
            IntParam         = 2,
            StackIndex       = 0,
        };
        asset.AddPill(pill);

        Validate(asset).Should().NotContain(d => d.Code == BTreeDiagnosticCode.NestedRepeater);
    }

    [Fact]
    public void Validate_parallel_inside_parallel_returns_nested_parallel_error()
    {
        // Arrange: Root → Parallel → Parallel (nested, kernel-illegal).
        var asset    = MakeAsset();
        var root     = MakeNode(NodeType.Root);
        var outerPar = MakeNode(NodeType.Parallel);
        var innerPar = MakeNode(NodeType.Parallel);
        root.ChildVisualIds.Add(outerPar.VisualId);
        outerPar.ChildVisualIds.Add(innerPar.VisualId);
        asset.AddNode(root);
        asset.AddNode(outerPar);
        asset.AddNode(innerPar);

        var diagnostics = Validate(asset);

        diagnostics.Should().Contain(d =>
            d.Code == BTreeDiagnosticCode.NestedParallel &&
            d.Severity == BTreeDiagnosticSeverity.Error &&
            d.VisualId == innerPar.VisualId);
    }
}
