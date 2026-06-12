using System;
using System.Collections.Generic;
using FluentAssertions;
using Fhsm.Compiler;
using Fhsm.Kernel.Data;
using Hrot.Hsm.Editor.Host;
using Hrot.Hsm.Editor.Model;
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Hsm.Editor.Tests.Host;

public sealed class HsmCommandSinkCreateStateTests
{
    // ---- Helpers ------------------------------------------------------------

    // Build a minimal HsmAsset with an empty state list — just the synthetic root.
    // The sink can then create and register new states via AddNode commands.
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

    // ---- AddNode (Simple) ---------------------------------------------------

    [Fact]
    public void AddNode_Simple_creates_state_under_root()
    {
        var (asset, sink) = BuildTestAsset();
        var assignedId = Guid.NewGuid();

        var result = sink.Apply(new GraphCommand.AddNode(
            new NodeId(assignedId),
            new NodeKindKey(HsmKinds.Simple),
            new System.Numerics.Vector2(100, 200),
            null));

        result.Success.Should().BeTrue();
        asset.AllStates.Should().HaveCount(1);

        var state = asset.FindStateByStableId(assignedId);
        state.Should().NotBeNull();
        state!.Parent.Should().BeSameAs(asset.RootState);
        state.Kind.Id.Should().Be(HsmKinds.Simple);
        state.Position.Should().Be(new System.Numerics.Vector2(100, 200));
    }

    // ---- AddNode (Parallel) -------------------------------------------------

    [Fact]
    public void AddNode_Parallel_sets_IsParallel_flag()
    {
        var (asset, sink) = BuildTestAsset();
        var assignedId = Guid.NewGuid();

        sink.Apply(new GraphCommand.AddNode(
            new NodeId(assignedId),
            new NodeKindKey(HsmKinds.Parallel),
            System.Numerics.Vector2.Zero,
            null));

        var state = asset.FindStateByStableId(assignedId);
        state.Should().NotBeNull();
        state!.IsParallel.Should().BeTrue();
        state.IsFinal.Should().BeFalse();
        state.IsHistory.Should().BeFalse();
        state.IsDeepHistory.Should().BeFalse();
        state.Kind.Id.Should().Be(HsmKinds.Parallel);
    }

    // ---- AddNode (Final) ----------------------------------------------------

    [Fact]
    public void AddNode_Final_sets_IsFinal_flag()
    {
        var (asset, sink) = BuildTestAsset();
        var assignedId = Guid.NewGuid();

        sink.Apply(new GraphCommand.AddNode(
            new NodeId(assignedId),
            new NodeKindKey(HsmKinds.Final),
            System.Numerics.Vector2.Zero,
            null));

        var state = asset.FindStateByStableId(assignedId);
        state.Should().NotBeNull();
        state!.IsFinal.Should().BeTrue();
        state.IsParallel.Should().BeFalse();
        state.IsHistory.Should().BeFalse();
        state.IsDeepHistory.Should().BeFalse();
    }

    // ---- AddNode (History / DeepHistory) ------------------------------------

    [Fact]
    public void AddNode_History_sets_IsHistory_flag()
    {
        var (asset, sink) = BuildTestAsset();
        var assignedId = Guid.NewGuid();

        sink.Apply(new GraphCommand.AddNode(
            new NodeId(assignedId),
            new NodeKindKey(HsmKinds.History),
            System.Numerics.Vector2.Zero,
            null));

        var state = asset.FindStateByStableId(assignedId);
        state.Should().NotBeNull();
        state!.IsHistory.Should().BeTrue();
        state.IsDeepHistory.Should().BeFalse();
        state.IsFinal.Should().BeFalse();
        state.Kind.Id.Should().Be(HsmKinds.History);
    }

    [Fact]
    public void AddNode_DeepHistory_sets_IsDeepHistory_flag()
    {
        var (asset, sink) = BuildTestAsset();
        var assignedId = Guid.NewGuid();

        sink.Apply(new GraphCommand.AddNode(
            new NodeId(assignedId),
            new NodeKindKey(HsmKinds.DeepHistory),
            System.Numerics.Vector2.Zero,
            null));

        var state = asset.FindStateByStableId(assignedId);
        state.Should().NotBeNull();
        state!.IsDeepHistory.Should().BeTrue();
        state.IsHistory.Should().BeFalse();
        state.IsFinal.Should().BeFalse();
        state.Kind.Id.Should().Be(HsmKinds.DeepHistory);
    }

    // ---- Implicit promotion -------------------------------------------------

    [Fact]
    public void Reparenting_child_under_simple_state_promotes_to_composite()
    {
        var (asset, sink) = BuildTestAsset();
        var s1Id = Guid.NewGuid();
        var s2Id = Guid.NewGuid();

        // Create two simple states under root.
        sink.Apply(new GraphCommand.AddNode(
            new NodeId(s1Id),
            new NodeKindKey(HsmKinds.Simple),
            new System.Numerics.Vector2(0, 0),
            null));
        sink.Apply(new GraphCommand.AddNode(
            new NodeId(s2Id),
            new NodeKindKey(HsmKinds.Simple),
            new System.Numerics.Vector2(200, 0),
            null));

        var s1 = asset.FindStateByStableId(s1Id);
        var s2 = asset.FindStateByStableId(s2Id);
        s1.Should().NotBeNull();
        s2.Should().NotBeNull();

        // Before reparenting, S1 should not be a container.
        s1!.IsContainer.Should().BeFalse();
        s1.Kind.Id.Should().Be(HsmKinds.Simple);

        // Reparent S2 under S1.
        sink.Apply(new GraphCommand.ChangeParent(
            new NodeId(s2Id),
            new NodeId(s1Id),
            0,
            new System.Numerics.Vector2(50, 50)));

        // After reparenting, S1 should be a composite container.
        s1.IsContainer.Should().BeTrue();
        s1.Kind.Id.Should().Be(HsmKinds.Composite);
        s1.Children.Should().Contain(s2);
        s2!.Parent.Should().BeSameAs(s1);
    }

    // ---- FlatIndex uniqueness -----------------------------------------------

    [Fact]
    public void AddNode_two_states_have_unique_flat_indices()
    {
        var (asset, sink) = BuildTestAsset();
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        sink.Apply(new GraphCommand.AddNode(
            new NodeId(id1),
            new NodeKindKey(HsmKinds.Simple),
            System.Numerics.Vector2.Zero,
            null));
        sink.Apply(new GraphCommand.AddNode(
            new NodeId(id2),
            new NodeKindKey(HsmKinds.Simple),
            System.Numerics.Vector2.Zero,
            null));

        var s1 = asset.FindStateByStableId(id1);
        var s2 = asset.FindStateByStableId(id2);
        s1.Should().NotBeNull();
        s2.Should().NotBeNull();

        // FlatIndex values must differ.
        s1!.FlatIndex.Should().NotBe(s2!.FlatIndex);

        // Both must resolve via FindStateByFlatIndex.
        var viaIndex1 = asset.FindStateByFlatIndex(s1.FlatIndex);
        var viaIndex2 = asset.FindStateByFlatIndex(s2.FlatIndex);
        viaIndex1.Should().BeSameAs(s1);
        viaIndex2.Should().BeSameAs(s2);
    }

    // ---- Count correctness --------------------------------------------------

    [Fact]
    public void AddNode_multiple_states_increments_AllStates_count()
    {
        var (asset, sink) = BuildTestAsset();
        asset.AllStates.Should().BeEmpty();

        sink.Apply(new GraphCommand.AddNode(
            new NodeId(Guid.NewGuid()),
            new NodeKindKey(HsmKinds.Simple),
            System.Numerics.Vector2.Zero,
            null));
        asset.AllStates.Should().HaveCount(1);

        sink.Apply(new GraphCommand.AddNode(
            new NodeId(Guid.NewGuid()),
            new NodeKindKey(HsmKinds.Composite),
            System.Numerics.Vector2.Zero,
            null));
        asset.AllStates.Should().HaveCount(2);

        sink.Apply(new GraphCommand.AddNode(
            new NodeId(Guid.NewGuid()),
            new NodeKindKey(HsmKinds.Parallel),
            System.Numerics.Vector2.Zero,
            null));
        asset.AllStates.Should().HaveCount(3);
    }
}
