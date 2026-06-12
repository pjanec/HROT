using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fhsm.Compiler;
using Fhsm.Kernel.Data;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Hsm.Editor.Host;
using Hrot.Hsm.Editor.Model;
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Hsm.Editor.Tests.Host;

public sealed class HsmCommandSinkDeleteStateTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private static (HsmAsset asset, HsmCommandSink sink) BuildTestAsset()
    {
        var rootNode = new StateNode("__root__");

        var asset = new HsmAsset(
            Guid.NewGuid(), "Test", "", false, "",
            new HsmDefinitionBlob(), new MachineMetadata(),
            rootNode,
            new List<StateNode>(),
            new List<TransitionNode>(),
            new List<GlobalTransitionNode>(),
            new List<RegionNode>(),
            new List<EventDefinition>());

        var sink = new HsmCommandSink(asset);
        return (asset, sink);
    }

    /// <summary>
    /// Registers a state under a parent via HsmAsset.RegisterState (internal, accessible via InternalsVisibleTo).
    /// </summary>
    private static StateNode RegisterState(HsmAsset asset, StateNode parent, string name, Guid? stableId = null)
    {
        var state = new StateNode(name) { StableId = stableId ?? Guid.NewGuid() };
        asset.RegisterState(state, parent);
        return state;
    }

    /// <summary>
    /// Registers a transition between two states via HsmAsset.RegisterTransition (internal).
    /// </summary>
    private static TransitionNode RegisterTransition(
        HsmAsset asset,
        StateNode source,
        StateNode target,
        string? expressionTargetField = null,
        Guid? visualId = null)
    {
        var t = new TransitionNode
        {
            VisualId = visualId ?? Guid.NewGuid(),
            Source = source,
            Target = target,
            ExpressionTargetField = expressionTargetField,
        };
        asset.RegisterTransition(t);
        return t;
    }

    /// <summary>
    /// Adds an auto-managed blackboard variable with the given name.
    /// </summary>
    private static void AddAutoManagedVar(HsmAsset asset, string name)
    {
        asset.AddVariable(new BlackboardVariableEntry(name, typeof(float), null, IsAutoManaged: true));
    }

    /// <summary>
    /// Adds a non-auto-managed blackboard variable with the given name.
    /// </summary>
    private static void AddSharedVar(HsmAsset asset, string name)
    {
        asset.AddVariable(new BlackboardVariableEntry(name, typeof(float), null, IsAutoManaged: false));
    }

    // ── 1. Delete leaf ────────────────────────────────────────────────────────

    [Fact]
    public void DeleteLeaf_state_removed_from_AllStates_and_maps()
    {
        var (asset, sink) = BuildTestAsset();
        var leaf = RegisterState(asset, asset.RootState, "Leaf");

        asset.AllStates.Should().HaveCount(1);
        asset.FindStateByStableId(leaf.StableId).Should().NotBeNull();

        sink.Apply(new GraphCommand.RemoveNodes(new[] { new NodeId(leaf.StableId) }));

        asset.AllStates.Should().BeEmpty();
        asset.FindStateByStableId(leaf.StableId).Should().BeNull();
        asset.FindStateByFlatIndex(leaf.FlatIndex).Should().BeNull();
    }

    [Fact]
    public void DeleteLeaf_removed_from_parent_Children()
    {
        var (asset, sink) = BuildTestAsset();
        var leaf = RegisterState(asset, asset.RootState, "Leaf");

        asset.RootState.Children.Should().Contain(leaf);

        sink.Apply(new GraphCommand.RemoveNodes(new[] { new NodeId(leaf.StableId) }));

        asset.RootState.Children.Should().BeEmpty();
    }

    // ── 2. Delete composite ───────────────────────────────────────────────────

    [Fact]
    public void DeleteComposite_removes_composite_and_all_descendants()
    {
        var (asset, sink) = BuildTestAsset();
        var composite = RegisterState(asset, asset.RootState, "Composite");
        var child1 = RegisterState(asset, composite, "Child1");
        var child2 = RegisterState(asset, composite, "Child2");
        var grandchild = RegisterState(asset, child2, "Grandchild");

        asset.AllStates.Should().HaveCount(4);

        sink.Apply(new GraphCommand.RemoveNodes(new[] { new NodeId(composite.StableId) }));

        asset.AllStates.Should().BeEmpty();
        asset.FindStateByStableId(composite.StableId).Should().BeNull();
        asset.FindStateByStableId(child1.StableId).Should().BeNull();
        asset.FindStateByStableId(child2.StableId).Should().BeNull();
        asset.FindStateByStableId(grandchild.StableId).Should().BeNull();
    }

    [Fact]
    public void DeleteComposite_children_detached_from_composite()
    {
        var (asset, sink) = BuildTestAsset();
        var composite = RegisterState(asset, asset.RootState, "Composite");
        var child = RegisterState(asset, composite, "Child");

        composite.Children.Should().Contain(child);

        sink.Apply(new GraphCommand.RemoveNodes(new[] { new NodeId(composite.StableId) }));

        // Both should be gone from AllStates.
        asset.AllStates.Should().BeEmpty();
    }

    // ── 3. Incident transitions removed ───────────────────────────────────────

    [Fact]
    public void DeleteTarget_removes_incoming_transition()
    {
        var (asset, sink) = BuildTestAsset();
        var a = RegisterState(asset, asset.RootState, "A");
        var b = RegisterState(asset, asset.RootState, "B");
        var t = RegisterTransition(asset, a, b);

        asset.AllTransitions.Should().HaveCount(1);
        a.OutgoingTransitions.Should().Contain(t);
        asset.FindTransitionByVisualId(t.VisualId).Should().NotBeNull();

        sink.Apply(new GraphCommand.RemoveNodes(new[] { new NodeId(b.StableId) }));

        asset.AllTransitions.Should().BeEmpty();
        asset.FindTransitionByVisualId(t.VisualId).Should().BeNull();
        a.OutgoingTransitions.Should().BeEmpty();
    }

    [Fact]
    public void DeleteSource_removes_outgoing_transition()
    {
        var (asset, sink) = BuildTestAsset();
        var a = RegisterState(asset, asset.RootState, "A");
        var b = RegisterState(asset, asset.RootState, "B");
        var t = RegisterTransition(asset, a, b);

        asset.AllTransitions.Should().HaveCount(1);

        sink.Apply(new GraphCommand.RemoveNodes(new[] { new NodeId(a.StableId) }));

        asset.AllTransitions.Should().BeEmpty();
        asset.FindTransitionByVisualId(t.VisualId).Should().BeNull();
    }

    [Fact]
    public void DeleteSource_target_state_persists()
    {
        var (asset, sink) = BuildTestAsset();
        var a = RegisterState(asset, asset.RootState, "A");
        var b = RegisterState(asset, asset.RootState, "B");
        RegisterTransition(asset, a, b);

        sink.Apply(new GraphCommand.RemoveNodes(new[] { new NodeId(a.StableId) }));

        asset.AllStates.Should().HaveCount(1);
        asset.FindStateByStableId(b.StableId).Should().NotBeNull();
    }

    // ── 4. No dangling references after composite delete ──────────────────────

    [Fact]
    public void DeleteComposite_removes_internal_transition_once()
    {
        var (asset, sink) = BuildTestAsset();
        var composite = RegisterState(asset, asset.RootState, "Composite");
        var childA = RegisterState(asset, composite, "ChildA");
        var childB = RegisterState(asset, composite, "ChildB");
        var t = RegisterTransition(asset, childA, childB);

        asset.AllTransitions.Should().HaveCount(1);

        sink.Apply(new GraphCommand.RemoveNodes(new[] { new NodeId(composite.StableId) }));

        // All states gone.
        asset.AllStates.Should().BeEmpty();
        // Transition gone (should not have caused issues despite childA being
        // unregistered before the outgoing snapshot was examined — the snapshot
        // taken before mutations ensures the transition is collected).
        asset.AllTransitions.Should().BeEmpty();
        asset.FindTransitionByVisualId(t.VisualId).Should().BeNull();
    }

    [Fact]
    public void DeleteComposite_with_transition_to_outside_removes_transition()
    {
        var (asset, sink) = BuildTestAsset();
        var composite = RegisterState(asset, asset.RootState, "Composite");
        var childA = RegisterState(asset, composite, "ChildA");
        var outside = RegisterState(asset, asset.RootState, "Outside");
        var t = RegisterTransition(asset, childA, outside);

        asset.AllTransitions.Should().HaveCount(1);

        sink.Apply(new GraphCommand.RemoveNodes(new[] { new NodeId(composite.StableId) }));

        asset.AllTransitions.Should().BeEmpty();
        asset.FindTransitionByVisualId(t.VisualId).Should().BeNull();
    }

    [Fact]
    public void DeleteComposite_with_transition_from_outside_removes_transition()
    {
        var (asset, sink) = BuildTestAsset();
        var composite = RegisterState(asset, asset.RootState, "Composite");
        var childA = RegisterState(asset, composite, "ChildA");
        var outside = RegisterState(asset, asset.RootState, "Outside");
        var t = RegisterTransition(asset, outside, childA);

        asset.AllTransitions.Should().HaveCount(1);

        sink.Apply(new GraphCommand.RemoveNodes(new[] { new NodeId(composite.StableId) }));

        asset.AllTransitions.Should().BeEmpty();
        asset.FindTransitionByVisualId(t.VisualId).Should().BeNull();
        // Outside state persists.
        asset.FindStateByStableId(outside.StableId).Should().NotBeNull();
        outside.OutgoingTransitions.Should().BeEmpty();
    }

    // ── 5. Auto-managed variable cleanup on endpoint delete ───────────────────

    [Fact]
    public void DeleteEndpoint_with_auto_managed_var_removes_variable()
    {
        var (asset, sink) = BuildTestAsset();
        var a = RegisterState(asset, asset.RootState, "A");
        var b = RegisterState(asset, asset.RootState, "B");
        string varName = "_auto_t1";
        AddAutoManagedVar(asset, varName);
        RegisterTransition(asset, a, b, expressionTargetField: varName);

        asset.BlackboardVariables.Should().Contain(v => v.Name == varName);

        sink.Apply(new GraphCommand.RemoveNodes(new[] { new NodeId(b.StableId) }));

        asset.BlackboardVariables.Should().BeEmpty();
    }

    [Fact]
    public void DeleteEndpoint_with_shared_var_preserves_variable()
    {
        var (asset, sink) = BuildTestAsset();
        var a = RegisterState(asset, asset.RootState, "A");
        var b = RegisterState(asset, asset.RootState, "B");
        string varName = "sharedVar";
        AddSharedVar(asset, varName);
        RegisterTransition(asset, a, b, expressionTargetField: varName);

        asset.BlackboardVariables.Should().Contain(v => v.Name == varName);

        sink.Apply(new GraphCommand.RemoveNodes(new[] { new NodeId(b.StableId) }));

        asset.BlackboardVariables.Should().Contain(v => v.Name == varName);
    }
}
