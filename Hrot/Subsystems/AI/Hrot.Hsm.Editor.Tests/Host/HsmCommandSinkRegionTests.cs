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

public sealed class HsmCommandSinkRegionTests
{
    // ---- Helpers ------------------------------------------------------------

    // Build a minimal HsmAsset that has one parallel StateNode with a known StableId.
    // The sink can locate the state via FindStateByStableId.
    private static (HsmAsset asset, HsmCommandSink sink, StateNode parallelState) BuildTestAsset()
    {
        var stateId  = Guid.NewGuid();
        var pState   = new StateNode("Parallel") { StableId = stateId, IsParallel = true };
        var rootNode = new StateNode("__root__");
        pState.Parent = rootNode;
        rootNode.Children.Add(pState);

        var asset = new HsmAsset(
            Guid.NewGuid(), "Test", "", false, "",
            new HsmDefinitionBlob(), new MachineMetadata(),
            rootNode,
            new List<StateNode> { pState },
            new List<TransitionNode>(),
            new List<GlobalTransitionNode>(),
            new List<RegionNode>(),
            new List<EventDefinition>());

        var sink = new HsmCommandSink(asset);
        return (asset, sink, pState);
    }

    // ---- Region tests -------------------------------------------------------

    [Fact]
    public void AddRegion_adds_region_to_state_RegionNodes()
    {
        var (asset, sink, pState) = BuildTestAsset();

        sink.Apply(new GraphCommand.AddRegion(
            new NodeId(pState.StableId), 0, "Alpha", 5));

        pState.RegionNodes.Count.Should().Be(1);
        pState.RegionNodes[0].Name.Should().Be("Alpha");
        pState.RegionNodes[0].Priority.Should().Be(5);
        pState.RegionNodes[0].RegionIndex.Should().Be(0);
    }

    [Fact]
    public void AddRegion_increments_AllRegions()
    {
        var (asset, sink, pState) = BuildTestAsset();
        int initialCount = asset.AllRegions.Count;

        sink.Apply(new GraphCommand.AddRegion(
            new NodeId(pState.StableId), 0, "Beta", 0));

        asset.AllRegions.Count.Should().Be(initialCount + 1);
    }

    [Fact]
    public void RemoveRegion_removes_region_from_state()
    {
        var (asset, sink, pState) = BuildTestAsset();
        sink.Apply(new GraphCommand.AddRegion(new NodeId(pState.StableId), 0, "R1", 0));
        pState.RegionNodes.Count.Should().Be(1);

        sink.Apply(new GraphCommand.RemoveRegion(
            new NodeId(pState.StableId), 0, ChildRedistributionPolicy.MoveToParent));

        pState.RegionNodes.Count.Should().Be(0);
        asset.AllRegions.Count.Should().Be(0);
    }

    [Fact]
    public void ReorderRegions_changes_region_order()
    {
        var (asset, sink, pState) = BuildTestAsset();
        sink.Apply(new GraphCommand.AddRegion(new NodeId(pState.StableId), 0, "First",  0));
        sink.Apply(new GraphCommand.AddRegion(new NodeId(pState.StableId), 1, "Second", 0));
        pState.RegionNodes.Count.Should().Be(2);

        // Swap: new index 0 comes from old index 1, new index 1 from old index 0.
        sink.Apply(new GraphCommand.ReorderRegions(
            new NodeId(pState.StableId), new List<int> { 1, 0 }));

        pState.RegionNodes[0].Name.Should().Be("Second");
        pState.RegionNodes[1].Name.Should().Be("First");
        pState.RegionNodes[0].RegionIndex.Should().Be(0);
        pState.RegionNodes[1].RegionIndex.Should().Be(1);
    }

    // ---- Attachment tests ---------------------------------------------------

    [Fact]
    public void AddAttachment_makes_attachment_findable_by_node()
    {
        var (asset, sink, pState) = BuildTestAsset();
        var hostId = new NodeId(pState.StableId);
        var attId  = AttachmentId.NewId();

        sink.Apply(new GraphCommand.AddAttachment(
            attId, hostId, AttachmentCategory.Decorator,
            "X", "Watch", null, 0, null));

        var result = asset.GetAttachmentsForNode(hostId);
        result.Count.Should().Be(1);
        result[0].Id.Should().Be(attId);
        result[0].Label.Should().Be("Watch");
        result[0].Glyph.Should().Be("X");
    }

    [Fact]
    public void RemoveAttachments_removes_attachment()
    {
        var (asset, sink, pState) = BuildTestAsset();
        var hostId = new NodeId(pState.StableId);
        var attId  = AttachmentId.NewId();

        sink.Apply(new GraphCommand.AddAttachment(
            attId, hostId, AttachmentCategory.Decorator,
            null, null, null, 0, null));

        asset.GetAttachmentsForNode(hostId).Count.Should().Be(1);

        sink.Apply(new GraphCommand.RemoveAttachments(new List<AttachmentId> { attId }));

        asset.GetAttachmentsForNode(hostId).Count.Should().Be(0);
    }

    // ---- ChangeParentMultiple (BCP-BATCH-01-FIX BUG 1) ----------------------

    // Helper: build an asset with a simple state (not parallel, no regions).
    private static (HsmAsset asset, HsmCommandSink sink, StateNode state) BuildSimpleStateAsset()
    {
        var stateId = Guid.NewGuid();
        var sState  = new StateNode("Simple") { StableId = stateId };
        var rootNode = new StateNode("__root__");
        sState.Parent = rootNode;
        rootNode.Children.Add(sState);

        var asset = new HsmAsset(
            Guid.NewGuid(), "Test", "", false, "",
            new HsmDefinitionBlob(), new MachineMetadata(),
            rootNode,
            new List<StateNode> { sState },
            new List<TransitionNode>(),
            new List<GlobalTransitionNode>(),
            new List<RegionNode>(),
            new List<EventDefinition>());

        return (asset, new HsmCommandSink(asset), sState);
    }

    /// <summary>
    /// ChangeParentMultiple (the command the canvas issues for every node drop, BPF-029)
    /// must persist NewLocalPosition so the state does not jump back.
    /// </summary>
    [Fact]
    public void ChangeParentMultiple_persists_new_position()
    {
        var (asset, sink, state) = BuildSimpleStateAsset();
        state.Position.Should().Be(System.Numerics.Vector2.Zero);

        var newPos = new System.Numerics.Vector2(77f, 88f);
        var result = sink.Apply(new GraphCommand.ChangeParentMultiple(
            new[] { new ChangeParentMove(new NodeId(state.StableId), null, null, newPos) }));

        result.Success.Should().BeTrue();
        state.Position.Should().Be(newPos);
    }

    /// <summary>
    /// ChangeParentMultiple with multiple states must update all positions.
    /// </summary>
    [Fact]
    public void ChangeParentMultiple_multiple_states_all_positions_updated()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var s1  = new StateNode("S1") { StableId = id1 };
        var s2  = new StateNode("S2") { StableId = id2 };
        var root = new StateNode("__root__");
        s1.Parent = root; root.Children.Add(s1);
        s2.Parent = root; root.Children.Add(s2);

        var asset = new HsmAsset(
            Guid.NewGuid(), "Test", "", false, "",
            new HsmDefinitionBlob(), new MachineMetadata(),
            root,
            new List<StateNode> { s1, s2 },
            new List<TransitionNode>(),
            new List<GlobalTransitionNode>(),
            new List<RegionNode>(),
            new List<EventDefinition>());

        var sink = new HsmCommandSink(asset);
        var pos1 = new System.Numerics.Vector2(10f, 20f);
        var pos2 = new System.Numerics.Vector2(30f, 40f);

        var result = sink.Apply(new GraphCommand.ChangeParentMultiple(
            new[]
            {
                new ChangeParentMove(new NodeId(id1), null, null, pos1),
                new ChangeParentMove(new NodeId(id2), null, null, pos2),
            }));

        result.Success.Should().BeTrue();
        s1.Position.Should().Be(pos1);
        s2.Position.Should().Be(pos2);
    }

    /// <summary>
    /// ChangeParentMultiple marks the asset dirty.
    /// </summary>
    [Fact]
    public void ChangeParentMultiple_marks_asset_dirty()
    {
        var (asset, sink, state) = BuildSimpleStateAsset();
        asset.ClearDirty();
        asset.IsDirty.Should().BeFalse();

        sink.Apply(new GraphCommand.ChangeParentMultiple(
            new[] { new ChangeParentMove(new NodeId(state.StableId), null, null, new System.Numerics.Vector2(5f, 5f)) }));

        asset.IsDirty.Should().BeTrue();
    }
}
