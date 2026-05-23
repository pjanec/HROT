using System;
using System.Collections.Generic;
using FluentAssertions;
using Fbt;
using Hrot.BTree.Editor.Emit;
using Hrot.BTree.Editor.Model;
using Xunit;

namespace Hrot.BTree.Editor.Tests;

/// <summary>
/// Tests for BT-S1-09: decorator pill collapse and round-trip emission.
/// Verifies that BTreeFluentEmitter correctly wraps nodes with their decorator pills.
/// BTH §6.
/// </summary>
public sealed class DecoratorPillCollapseTests
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

    private static BehaviorTreeAsset BuildTree(Action<BehaviorTreeAsset> configure)
    {
        var asset = new BehaviorTreeAsset(
            Guid.NewGuid(), "TestTree", "/T.cs", true,
            "BB", "Ctx", EmptyBlob());
        configure(asset);
        return asset;
    }

    private static string Emit(BehaviorTreeAsset asset) =>
        new BTreeFluentEmitter().Emit(asset);

    // Creates the standard root -> sequence -> action skeleton.
    private static (BTreeEditorNode root, BTreeEditorNode sequence, BTreeEditorNode action)
        AddSkeleton(BehaviorTreeAsset asset)
    {
        var root = new BTreeEditorNode
        {
            VisualId = new Guid("11000000-0000-0000-0000-000000000001"),
            KernelType = NodeType.Root,
        };
        var seq = new BTreeEditorNode
        {
            VisualId = new Guid("22000000-0000-0000-0000-000000000001"),
            KernelType = NodeType.Sequence,
            Action = null,
        };
        var action = new BTreeEditorNode
        {
            VisualId = new Guid("33000000-0000-0000-0000-000000000001"),
            KernelType = NodeType.Action,
            Action = new BTreeActionPayload
            {
                MethodFqn = "Ns.Class.Method",
                DelegateShape = BTreeActionDelegateShape.FourParamFull,
            },
        };

        root.ChildVisualIds.Add(seq.VisualId);
        seq.ChildVisualIds.Add(action.VisualId);

        asset.AddNode(root);
        asset.AddNode(seq);
        asset.AddNode(action);

        return (root, seq, action);
    }

    // ---- Composite node pill tests ------------------------------------------

    [Fact]
    public void Emit_sequence_with_inverter_pill_contains_inverter_call()
    {
        var asset = BuildTree(a =>
        {
            var (_, seq, _) = AddSkeleton(a);
            a.AddPill(new BTreeEditorPill
            {
                VisualId         = Guid.NewGuid(),
                HostNodeVisualId = seq.VisualId,
                DecoratorType    = NodeType.Inverter,
                StackIndex       = 0,
            });
        });

        Emit(asset).Should().Contain(".Inverter(");
    }

    [Fact]
    public void Emit_sequence_with_repeater_pill_contains_count()
    {
        var asset = BuildTree(a =>
        {
            var (_, seq, _) = AddSkeleton(a);
            a.AddPill(new BTreeEditorPill
            {
                VisualId         = Guid.NewGuid(),
                HostNodeVisualId = seq.VisualId,
                DecoratorType    = NodeType.Repeater,
                IntParam         = 5,
                StackIndex       = 0,
            });
        });

        string code = Emit(asset);
        code.Should().Contain(".Repeater(");
        code.Should().Contain("5");
    }

    [Fact]
    public void Emit_sequence_with_cooldown_pill_contains_duration()
    {
        var asset = BuildTree(a =>
        {
            var (_, seq, _) = AddSkeleton(a);
            a.AddPill(new BTreeEditorPill
            {
                VisualId         = Guid.NewGuid(),
                HostNodeVisualId = seq.VisualId,
                DecoratorType    = NodeType.Cooldown,
                FloatParam       = 2.5f,
                StackIndex       = 0,
            });
        });

        string code = Emit(asset);
        code.Should().Contain(".Cooldown(");
        code.Should().Contain("2.5");
    }

    [Fact]
    public void Emit_two_pills_outermost_emitted_first()
    {
        // Repeater StackIndex=0 (innermost), Cooldown StackIndex=1 (outermost).
        // Outermost pill wraps the inner — so Cooldown appears first in output.
        var cooldownPillId = new Guid("aaaaaaaa-0000-0000-0000-000000000001");
        var repeaterPillId = new Guid("bbbbbbbb-0000-0000-0000-000000000001");

        var asset = BuildTree(a =>
        {
            var (_, seq, _) = AddSkeleton(a);
            a.AddPill(new BTreeEditorPill
            {
                VisualId         = repeaterPillId,
                HostNodeVisualId = seq.VisualId,
                DecoratorType    = NodeType.Repeater,
                IntParam         = 3,
                StackIndex       = 0,
            });
            a.AddPill(new BTreeEditorPill
            {
                VisualId         = cooldownPillId,
                HostNodeVisualId = seq.VisualId,
                DecoratorType    = NodeType.Cooldown,
                FloatParam       = 2.0f,
                StackIndex       = 1,
            });
        });

        string code = Emit(asset);
        int cooldownIdx = code.IndexOf(".Cooldown(", StringComparison.Ordinal);
        int repeaterIdx = code.IndexOf(".Repeater(", StringComparison.Ordinal);
        cooldownIdx.Should().BeGreaterThan(-1);
        repeaterIdx.Should().BeGreaterThan(-1);
        cooldownIdx.Should().BeLessThan(repeaterIdx, "outermost pill (Cooldown, StackIndex=1) must appear before innermost pill (Repeater, StackIndex=0)");
    }

    [Fact]
    public void Emit_pill_visual_id_included_in_output()
    {
        var pillId = new Guid("cafecafe-0000-0000-0000-000000000001");
        var asset = BuildTree(a =>
        {
            var (_, seq, _) = AddSkeleton(a);
            a.AddPill(new BTreeEditorPill
            {
                VisualId         = pillId,
                HostNodeVisualId = seq.VisualId,
                DecoratorType    = NodeType.Inverter,
                StackIndex       = 0,
            });
        });

        Emit(asset).Should().Contain(pillId.ToString("D"));
    }

    // ---- Leaf node pill tests -----------------------------------------------

    [Fact]
    public void Emit_action_with_force_success_pill_contains_force_success_call()
    {
        var asset = BuildTree(a =>
        {
            var (_, seq, action) = AddSkeleton(a);
            a.AddPill(new BTreeEditorPill
            {
                VisualId         = Guid.NewGuid(),
                HostNodeVisualId = action.VisualId,
                DecoratorType    = NodeType.ForceSuccess,
                StackIndex       = 0,
            });
        });

        Emit(asset).Should().Contain(".ForceSuccess(");
    }

    [Fact]
    public void Emit_action_with_force_failure_pill_contains_force_failure_call()
    {
        var asset = BuildTree(a =>
        {
            var (_, seq, action) = AddSkeleton(a);
            a.AddPill(new BTreeEditorPill
            {
                VisualId         = Guid.NewGuid(),
                HostNodeVisualId = action.VisualId,
                DecoratorType    = NodeType.ForceFailure,
                StackIndex       = 0,
            });
        });

        Emit(asset).Should().Contain(".ForceFailure(");
    }

    [Fact]
    public void Emit_until_success_pill_contains_until_success_call()
    {
        var asset = BuildTree(a =>
        {
            var (_, seq, action) = AddSkeleton(a);
            a.AddPill(new BTreeEditorPill
            {
                VisualId         = Guid.NewGuid(),
                HostNodeVisualId = action.VisualId,
                DecoratorType    = NodeType.UntilSuccess,
                StackIndex       = 0,
            });
        });

        Emit(asset).Should().Contain(".UntilSuccess(");
    }

    [Fact]
    public void Emit_until_failure_pill_contains_until_failure_call()
    {
        var asset = BuildTree(a =>
        {
            var (_, seq, action) = AddSkeleton(a);
            a.AddPill(new BTreeEditorPill
            {
                VisualId         = Guid.NewGuid(),
                HostNodeVisualId = action.VisualId,
                DecoratorType    = NodeType.UntilFailure,
                StackIndex       = 0,
            });
        });

        Emit(asset).Should().Contain(".UntilFailure(");
    }

    [Fact]
    public void Emit_tree_without_pills_is_unchanged_in_structure()
    {
        // No pills: the emitter should still produce normal output.
        var asset = BuildTree(a => AddSkeleton(a));

        string code = Emit(asset);
        code.Should().Contain(".Sequence(");
        code.Should().Contain(".Action(");
        code.Should().NotContain(".Inverter(");
        code.Should().NotContain(".Repeater(");
    }
}
