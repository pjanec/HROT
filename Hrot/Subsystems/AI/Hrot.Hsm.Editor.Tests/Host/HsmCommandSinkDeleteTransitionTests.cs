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

public sealed class HsmCommandSinkDeleteTransitionTests
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

    // ── 1. RemoveLinks removes transition fully ──────────────────────────────

    [Fact]
    public void RemoveLinks_removes_transition_from_AllTransitions()
    {
        var (asset, sink) = BuildTestAsset();
        var a = RegisterState(asset, asset.RootState, "A");
        var b = RegisterState(asset, asset.RootState, "B");
        var t = RegisterTransition(asset, a, b);

        asset.AllTransitions.Should().HaveCount(1);

        sink.Apply(new GraphCommand.RemoveLinks(new[] { new LinkId(t.VisualId) }));

        asset.AllTransitions.Should().BeEmpty();
    }

    [Fact]
    public void RemoveLinks_transition_unresolvable_by_visual_id()
    {
        var (asset, sink) = BuildTestAsset();
        var a = RegisterState(asset, asset.RootState, "A");
        var b = RegisterState(asset, asset.RootState, "B");
        var t = RegisterTransition(asset, a, b);

        asset.FindTransitionByVisualId(t.VisualId).Should().NotBeNull();

        sink.Apply(new GraphCommand.RemoveLinks(new[] { new LinkId(t.VisualId) }));

        asset.FindTransitionByVisualId(t.VisualId).Should().BeNull();
    }

    [Fact]
    public void RemoveLinks_transition_removed_from_source_OutgoingTransitions()
    {
        var (asset, sink) = BuildTestAsset();
        var a = RegisterState(asset, asset.RootState, "A");
        var b = RegisterState(asset, asset.RootState, "B");
        var t = RegisterTransition(asset, a, b);

        a.OutgoingTransitions.Should().Contain(t);

        sink.Apply(new GraphCommand.RemoveLinks(new[] { new LinkId(t.VisualId) }));

        a.OutgoingTransitions.Should().BeEmpty();
    }

    // ── 2. States survive ────────────────────────────────────────────────────

    [Fact]
    public void RemoveLinks_source_and_target_states_survive()
    {
        var (asset, sink) = BuildTestAsset();
        var a = RegisterState(asset, asset.RootState, "A");
        var b = RegisterState(asset, asset.RootState, "B");
        var t = RegisterTransition(asset, a, b);

        sink.Apply(new GraphCommand.RemoveLinks(new[] { new LinkId(t.VisualId) }));

        asset.AllStates.Should().HaveCount(2);
        asset.FindStateByStableId(a.StableId).Should().NotBeNull();
        asset.FindStateByStableId(b.StableId).Should().NotBeNull();
    }

    // ── 3. BB1 var cleanup ───────────────────────────────────────────────────

    [Fact]
    public void RemoveLinks_with_auto_managed_var_removes_variable()
    {
        var (asset, sink) = BuildTestAsset();
        var a = RegisterState(asset, asset.RootState, "A");
        var b = RegisterState(asset, asset.RootState, "B");
        string varName = "_auto_t1";
        AddAutoManagedVar(asset, varName);
        var t = RegisterTransition(asset, a, b, expressionTargetField: varName);

        asset.BlackboardVariables.Should().Contain(v => v.Name == varName);

        sink.Apply(new GraphCommand.RemoveLinks(new[] { new LinkId(t.VisualId) }));

        asset.BlackboardVariables.Should().BeEmpty();
    }

    [Fact]
    public void RemoveLinks_with_shared_var_preserves_variable()
    {
        var (asset, sink) = BuildTestAsset();
        var a = RegisterState(asset, asset.RootState, "A");
        var b = RegisterState(asset, asset.RootState, "B");
        string varName = "sharedVar";
        AddSharedVar(asset, varName);
        var t = RegisterTransition(asset, a, b, expressionTargetField: varName);

        asset.BlackboardVariables.Should().Contain(v => v.Name == varName);

        sink.Apply(new GraphCommand.RemoveLinks(new[] { new LinkId(t.VisualId) }));

        asset.BlackboardVariables.Should().Contain(v => v.Name == varName);
    }

    // ── 4. RemoveLinks unknown id ────────────────────────────────────────────

    [Fact]
    public void RemoveLinks_unknown_id_no_throw()
    {
        var (asset, sink) = BuildTestAsset();
        var a = RegisterState(asset, asset.RootState, "A");
        var b = RegisterState(asset, asset.RootState, "B");

        sink.Apply(new GraphCommand.RemoveLinks(new[] { new LinkId(Guid.NewGuid()) }));

        asset.AllStates.Should().HaveCount(2);
        asset.AllTransitions.Should().BeEmpty();
    }

    // ── 5. SetContainerCollapsed ─────────────────────────────────────────────

    [Fact]
    public void SetContainerCollapsed_sets_IsCollapsed_to_true()
    {
        var (asset, sink) = BuildTestAsset();
        var state = RegisterState(asset, asset.RootState, "Container");

        state.IsCollapsed.Should().BeFalse();

        sink.Apply(new GraphCommand.SetContainerCollapsed(
            new NodeId(state.StableId), IsCollapsed: true));

        state.IsCollapsed.Should().BeTrue();
    }

    [Fact]
    public void SetContainerCollapsed_sets_IsCollapsed_to_false()
    {
        var (asset, sink) = BuildTestAsset();
        var state = RegisterState(asset, asset.RootState, "Container");
        state.IsCollapsed = true;

        state.IsCollapsed.Should().BeTrue();

        sink.Apply(new GraphCommand.SetContainerCollapsed(
            new NodeId(state.StableId), IsCollapsed: false));

        state.IsCollapsed.Should().BeFalse();
    }
}
