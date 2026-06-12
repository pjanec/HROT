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

public sealed class HsmCommandSinkTransitionTests
{
    // ---- Helpers ------------------------------------------------------------

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

    // Registers two simple states A and B via the sink and returns (a, b).
    private static (StateNode a, StateNode b) RegisterTwoSimpleStates(HsmAsset asset, HsmCommandSink sink)
    {
        var aId = Guid.NewGuid();
        var bId = Guid.NewGuid();

        sink.Apply(new GraphCommand.AddNode(
            new NodeId(aId),
            new NodeKindKey(HsmKinds.Simple),
            System.Numerics.Vector2.Zero,
            null));
        sink.Apply(new GraphCommand.AddNode(
            new NodeId(bId),
            new NodeKindKey(HsmKinds.Simple),
            new System.Numerics.Vector2(200, 0),
            null));

        var a = asset.FindStateByStableId(aId)!;
        var b = asset.FindStateByStableId(bId)!;
        a.Should().NotBeNull();
        b.Should().NotBeNull();
        return (a, b);
    }

    // ---- Happy path ---------------------------------------------------------

    [Fact]
    public void AddLink_happy_path_creates_transition()
    {
        var (asset, sink) = BuildTestAsset();
        var (a, b) = RegisterTwoSimpleStates(asset, sink);
        var linkId = Guid.NewGuid();

        var result = sink.Apply(new GraphCommand.AddLink(
            new LinkId(linkId),
            new PinId(a.HiddenOutputPinId),
            new PinId(b.HiddenInputPinId)));

        result.Success.Should().BeTrue();

        var t = asset.FindTransitionByVisualId(linkId);
        t.Should().NotBeNull();
        t!.Source.Should().BeSameAs(a);
        t!.Target.Should().BeSameAs(b);
        t!.VisualId.Should().Be(linkId);
        t!.Kind.Should().Be(TransitionKind.External);
        t!.EventId.Should().Be(0);

        // Present in identity collections.
        asset.AllTransitions.Should().Contain(t);
        a.OutgoingTransitions.Should().Contain(t);
    }

    [Fact]
    public void AddLink_find_by_visual_id_resolves()
    {
        var (asset, sink) = BuildTestAsset();
        var (a, b) = RegisterTwoSimpleStates(asset, sink);
        var linkId = Guid.NewGuid();

        sink.Apply(new GraphCommand.AddLink(
            new LinkId(linkId),
            new PinId(a.HiddenOutputPinId),
            new PinId(b.HiddenInputPinId)));

        var found = asset.FindTransitionByVisualId(linkId);
        found.Should().NotBeNull();
        found!.VisualId.Should().Be(linkId);
    }

    // ---- Graph projection ---------------------------------------------------

    [Fact]
    public void AddLink_projects_into_HsmGraphModel_Links()
    {
        var (asset, sink) = BuildTestAsset();
        var (a, b) = RegisterTwoSimpleStates(asset, sink);
        var graph = new HsmGraphModel(asset);
        var linkId = Guid.NewGuid();

        sink.Apply(new GraphCommand.AddLink(
            new LinkId(linkId),
            new PinId(a.HiddenOutputPinId),
            new PinId(b.HiddenInputPinId)));

        // The graph model should rebuild its link cache on asset.Changed.
        graph.Links.Should().ContainSingle(l => l.Id.Value == linkId);
    }

    // ---- Validator rejection — Final source ---------------------------------

    [Fact]
    public void AddLink_rejects_when_source_is_Final()
    {
        var (asset, sink) = BuildTestAsset();
        var aId = Guid.NewGuid();
        var bId = Guid.NewGuid();

        sink.Apply(new GraphCommand.AddNode(
            new NodeId(aId),
            new NodeKindKey(HsmKinds.Final),
            System.Numerics.Vector2.Zero,
            null));
        sink.Apply(new GraphCommand.AddNode(
            new NodeId(bId),
            new NodeKindKey(HsmKinds.Simple),
            new System.Numerics.Vector2(200, 0),
            null));

        var a = asset.FindStateByStableId(aId)!;
        var b = asset.FindStateByStableId(bId)!;
        a.IsFinal.Should().BeTrue();

        var before = asset.AllTransitions.Count;

        sink.Apply(new GraphCommand.AddLink(
            new LinkId(Guid.NewGuid()),
            new PinId(a.HiddenOutputPinId),
            new PinId(b.HiddenInputPinId)));

        // No transition should have been created.
        asset.AllTransitions.Should().HaveCount(before);
    }

    // ---- Validator rejection — History target -------------------------------

    [Fact]
    public void AddLink_rejects_when_target_is_History()
    {
        var (asset, sink) = BuildTestAsset();
        var aId = Guid.NewGuid();
        var bId = Guid.NewGuid();

        sink.Apply(new GraphCommand.AddNode(
            new NodeId(aId),
            new NodeKindKey(HsmKinds.Simple),
            System.Numerics.Vector2.Zero,
            null));
        sink.Apply(new GraphCommand.AddNode(
            new NodeId(bId),
            new NodeKindKey(HsmKinds.History),
            new System.Numerics.Vector2(200, 0),
            null));

        var a = asset.FindStateByStableId(aId)!;
        var b = asset.FindStateByStableId(bId)!;
        b.IsHistory.Should().BeTrue();

        var before = asset.AllTransitions.Count;

        sink.Apply(new GraphCommand.AddLink(
            new LinkId(Guid.NewGuid()),
            new PinId(a.HiddenOutputPinId),
            new PinId(b.HiddenInputPinId)));

        // No transition should have been created.
        asset.AllTransitions.Should().HaveCount(before);
    }

    // ---- Unresolvable pin ---------------------------------------------------

    [Fact]
    public void AddLink_noops_when_pin_unresolvable()
    {
        var (asset, sink) = BuildTestAsset();
        var aId = Guid.NewGuid();

        sink.Apply(new GraphCommand.AddNode(
            new NodeId(aId),
            new NodeKindKey(HsmKinds.Simple),
            System.Numerics.Vector2.Zero,
            null));

        var a = asset.FindStateByStableId(aId)!;
        var before = asset.AllTransitions.Count;

        // Valid output pin, random input pin.
        sink.Apply(new GraphCommand.AddLink(
            new LinkId(Guid.NewGuid()),
            new PinId(a.HiddenOutputPinId),
            new PinId(Guid.NewGuid())));

        asset.AllTransitions.Should().HaveCount(before);

        // Random output pin, valid input pin.
        sink.Apply(new GraphCommand.AddLink(
            new LinkId(Guid.NewGuid()),
            new PinId(Guid.NewGuid()),
            new PinId(a.HiddenInputPinId)));

        asset.AllTransitions.Should().HaveCount(before);
    }
}
