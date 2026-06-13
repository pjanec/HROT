using System;
using System.Collections.Generic;
using System.Numerics;
using FluentAssertions;
using Fhsm.Compiler;
using Fhsm.Kernel.Data;
using Hrot.Hsm.Editor.Host;
using Hrot.Hsm.Editor.Model;
using NodeEditor.Core.Commands;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Hsm.Editor.Tests.Host;

public sealed class HsmCommandSinkRerouteTests
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

    /// <summary>
    /// Registers two simple states and one transition between them via the sink.
    /// Returns the transition and the LinkId (VisualId).
    /// </summary>
    private static (TransitionNode transition, Guid linkId) RegisterTransition(
        HsmAsset asset, HsmCommandSink sink)
    {
        var aId = Guid.NewGuid();
        var bId = Guid.NewGuid();

        sink.Apply(new GraphCommand.AddNode(
            new NodeId(aId),
            new NodeKindKey(HsmKinds.Simple),
            Vector2.Zero,
            null));
        sink.Apply(new GraphCommand.AddNode(
            new NodeId(bId),
            new NodeKindKey(HsmKinds.Simple),
            new Vector2(200, 0),
            null));

        var a = asset.FindStateByStableId(aId)!;
        var b = asset.FindStateByStableId(bId)!;

        var linkId = Guid.NewGuid();
        sink.Apply(new GraphCommand.AddLink(
            new LinkId(linkId),
            new PinId(a.HiddenOutputPinId),
            new PinId(b.HiddenInputPinId)));

        var transition = asset.FindTransitionByVisualId(linkId)!;
        transition.Should().NotBeNull("setup: transition must exist");
        return (transition, linkId);
    }

    // ---- InsertReroute ------------------------------------------------------

    [Fact]
    public void InsertReroute_appends_waypoint_to_transition()
    {
        var (asset, sink) = BuildTestAsset();
        var (t, linkId) = RegisterTransition(asset, sink);

        var pt = new Vector2(100, 50);
        var result = sink.Apply(new GraphCommand.InsertReroute(new LinkId(linkId), pt));

        result.Success.Should().BeTrue();
        t.Waypoints.Should().ContainSingle().Which.Should().Be(pt);
    }

    [Fact]
    public void InsertReroute_multiple_calls_appends_in_order()
    {
        var (asset, sink) = BuildTestAsset();
        var (t, linkId) = RegisterTransition(asset, sink);

        var p0 = new Vector2(10, 20);
        var p1 = new Vector2(30, 40);
        sink.Apply(new GraphCommand.InsertReroute(new LinkId(linkId), p0));
        sink.Apply(new GraphCommand.InsertReroute(new LinkId(linkId), p1));

        t.Waypoints.Should().HaveCount(2);
        t.Waypoints[0].Should().Be(p0);
        t.Waypoints[1].Should().Be(p1);
    }

    [Fact]
    public void InsertReroute_unknown_linkId_is_noop()
    {
        var (asset, sink) = BuildTestAsset();
        var (t, _) = RegisterTransition(asset, sink);

        var before = t.Waypoints.Count;
        var result = sink.Apply(new GraphCommand.InsertReroute(new LinkId(Guid.NewGuid()), new Vector2(1, 2)));

        result.Success.Should().BeTrue();
        t.Waypoints.Should().HaveCount(before);
    }

    // ---- MoveReroute --------------------------------------------------------

    [Fact]
    public void MoveReroute_updates_existing_waypoint()
    {
        var (asset, sink) = BuildTestAsset();
        var (t, linkId) = RegisterTransition(asset, sink);

        sink.Apply(new GraphCommand.InsertReroute(new LinkId(linkId), new Vector2(10, 10)));

        var newPos = new Vector2(99, 88);
        var result = sink.Apply(new GraphCommand.MoveReroute(new LinkId(linkId), 0, newPos));

        result.Success.Should().BeTrue();
        t.Waypoints[0].Should().Be(newPos);
    }

    [Fact]
    public void MoveReroute_out_of_range_index_is_noop()
    {
        var (asset, sink) = BuildTestAsset();
        var (t, linkId) = RegisterTransition(asset, sink);

        // One waypoint at index 0; index 1 is out of range.
        sink.Apply(new GraphCommand.InsertReroute(new LinkId(linkId), new Vector2(5, 5)));
        var original = t.Waypoints[0];

        var result = sink.Apply(new GraphCommand.MoveReroute(new LinkId(linkId), 1, new Vector2(99, 99)));

        result.Success.Should().BeTrue();
        t.Waypoints.Should().HaveCount(1);
        t.Waypoints[0].Should().Be(original);
    }

    [Fact]
    public void MoveReroute_negative_index_is_noop()
    {
        var (asset, sink) = BuildTestAsset();
        var (t, linkId) = RegisterTransition(asset, sink);

        sink.Apply(new GraphCommand.InsertReroute(new LinkId(linkId), new Vector2(5, 5)));
        var original = t.Waypoints[0];

        var result = sink.Apply(new GraphCommand.MoveReroute(new LinkId(linkId), -1, new Vector2(99, 99)));

        result.Success.Should().BeTrue();
        t.Waypoints[0].Should().Be(original);
    }

    [Fact]
    public void MoveReroute_unknown_linkId_is_noop()
    {
        var (asset, sink) = BuildTestAsset();
        var (t, linkId) = RegisterTransition(asset, sink);

        sink.Apply(new GraphCommand.InsertReroute(new LinkId(linkId), new Vector2(5, 5)));
        var original = t.Waypoints[0];

        var result = sink.Apply(new GraphCommand.MoveReroute(new LinkId(Guid.NewGuid()), 0, new Vector2(99, 99)));

        result.Success.Should().BeTrue();
        t.Waypoints[0].Should().Be(original);
    }

    // ---- RemoveReroute ------------------------------------------------------

    [Fact]
    public void RemoveReroute_removes_waypoint_at_index()
    {
        var (asset, sink) = BuildTestAsset();
        var (t, linkId) = RegisterTransition(asset, sink);

        sink.Apply(new GraphCommand.InsertReroute(new LinkId(linkId), new Vector2(1, 1)));
        sink.Apply(new GraphCommand.InsertReroute(new LinkId(linkId), new Vector2(2, 2)));

        var result = sink.Apply(new GraphCommand.RemoveReroute(new LinkId(linkId), 0));

        result.Success.Should().BeTrue();
        t.Waypoints.Should().ContainSingle().Which.Should().Be(new Vector2(2, 2));
    }

    [Fact]
    public void RemoveReroute_out_of_range_index_is_noop()
    {
        var (asset, sink) = BuildTestAsset();
        var (t, linkId) = RegisterTransition(asset, sink);

        sink.Apply(new GraphCommand.InsertReroute(new LinkId(linkId), new Vector2(1, 1)));

        // Index 1 does not exist (only index 0).
        var result = sink.Apply(new GraphCommand.RemoveReroute(new LinkId(linkId), 1));

        result.Success.Should().BeTrue();
        t.Waypoints.Should().HaveCount(1);
    }

    [Fact]
    public void RemoveReroute_negative_index_is_noop()
    {
        var (asset, sink) = BuildTestAsset();
        var (t, linkId) = RegisterTransition(asset, sink);

        sink.Apply(new GraphCommand.InsertReroute(new LinkId(linkId), new Vector2(1, 1)));

        var result = sink.Apply(new GraphCommand.RemoveReroute(new LinkId(linkId), -1));

        result.Success.Should().BeTrue();
        t.Waypoints.Should().HaveCount(1);
    }

    [Fact]
    public void RemoveReroute_unknown_linkId_is_noop()
    {
        var (asset, sink) = BuildTestAsset();
        var (t, linkId) = RegisterTransition(asset, sink);

        sink.Apply(new GraphCommand.InsertReroute(new LinkId(linkId), new Vector2(1, 1)));

        var result = sink.Apply(new GraphCommand.RemoveReroute(new LinkId(Guid.NewGuid()), 0));

        result.Success.Should().BeTrue();
        t.Waypoints.Should().HaveCount(1);
    }
}
