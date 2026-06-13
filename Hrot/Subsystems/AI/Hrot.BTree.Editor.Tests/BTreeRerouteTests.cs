using System;
using System.Collections.Generic;
using System.Numerics;
using FluentAssertions;
using Fbt;
using Hrot.BTree.Editor.Host;
using Hrot.BTree.Editor.Model;
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.BTree.Editor.Tests;

/// <summary>
/// Tests for wire reroute support in BTree (RR-03):
/// - BTreeParentChildLink.ChildVisualIdFromLinkId inverse-XOR round-trip
/// - BTreeCommandSink.InsertReroute / MoveReroute / RemoveReroute correctness
/// - Out-of-range and unknown-link no-ops
/// - BTreeGraphModel.FindLink.Waypoints returns the child's waypoints
/// </summary>
public sealed class BTreeRerouteTests
{
    // ---- Helpers ------------------------------------------------------------

    private static BehaviorTreeBlob EmptyBlob() => new BehaviorTreeBlob
    {
        TreeName        = "test",
        Nodes           = Array.Empty<NodeDefinition>(),
        MethodNames     = Array.Empty<string>(),
        FloatParams     = Array.Empty<float>(),
        IntParams       = Array.Empty<int>(),
        SubtreeAssetIds = Array.Empty<string>(),
    };

    private static BehaviorTreeAsset MakeAsset() =>
        new BehaviorTreeAsset(Guid.NewGuid(), "TestTree", "/TestTree.cs", true,
            "BB", "Ctx", EmptyBlob());

    // ---- Stubs --------------------------------------------------------------

    private sealed class StubPin : IPinModel
    {
        public PinId Id { get; }
        public NodeId OwnerNodeId { get; }
        public string Label => string.Empty;
        public PinDirection Direction { get; }
        public PinKind Kind => PinKind.Exec;
        public TypeKey? Type => null;
        public PinShape Shape => PinShape.Circle;
        public bool IsAdvanced => false;
        public bool IsOptional => false;
        public string? Tooltip => null;
        public IPinDefaultValue? Default => null;

        public StubPin(PinId id, NodeId owner, PinDirection dir)
        { Id = id; OwnerNodeId = owner; Direction = dir; }
    }

    private sealed class StubGraph : IGraphModel
    {
        private readonly Dictionary<PinId, StubPin> _pins = new();

        public GraphId Id => GraphId.NewId();
        public string DisplayName => "test";
        public GraphKindDescriptor Kind => new("test", "test", false, false);
        public IReadOnlyCollection<INodeModel>    Nodes    => Array.Empty<INodeModel>();
        public IReadOnlyCollection<ILinkModel>    Links    => Array.Empty<ILinkModel>();
        public IReadOnlyCollection<ICommentModel> Comments => Array.Empty<ICommentModel>();

#pragma warning disable CS0067
        public event Action<GraphChangeNotification>? Changed;
#pragma warning restore CS0067

        public void RegisterPins(NodeId nodeId, out PinId outputPin, out PinId inputPin)
        {
            outputPin = new PinId(Guid.NewGuid());
            inputPin  = new PinId(Guid.NewGuid());
            _pins[outputPin] = new StubPin(outputPin, nodeId, PinDirection.Output);
            _pins[inputPin]  = new StubPin(inputPin,  nodeId, PinDirection.Input);
        }

        public INodeModel?  FindNode(NodeId id) => null;
        public IPinModel?   FindPin(PinId id)   => _pins.TryGetValue(id, out var p) ? p : null;
        public ILinkModel?  FindLink(LinkId id) => null;
    }

    /// <summary>
    /// Builds an asset with one parent (Sequence) and one child (Action) connected,
    /// then returns the child node and its canonical LinkId.
    /// LinkId = XorGuid(child.VisualId, LinkIdXorKey); recovered by ChildVisualIdFromLinkId.
    /// </summary>
    private static (BehaviorTreeAsset asset, BTreeCommandSink sink, BTreeEditorNode child, LinkId linkId)
        BuildWithChild()
    {
        var asset = MakeAsset();
        var graph = new StubGraph();
        var sink  = new BTreeCommandSink(asset, graph);

        var parentId = NodeId.NewId();
        var childId  = NodeId.NewId();

        sink.Apply(new GraphCommand.AddNode(parentId, new NodeKindKey(BTreeKinds.Sequence), Vector2.Zero, null));
        sink.Apply(new GraphCommand.AddNode(childId,  new NodeKindKey(BTreeKinds.Action),   Vector2.Zero, null));

        graph.RegisterPins(parentId, out _, out var parentIn);
        graph.RegisterPins(childId,  out var childOut, out _);
        sink.Apply(new GraphCommand.AddLink(new LinkId(Guid.NewGuid()), childOut, parentIn));

        var child = asset.FindNode(childId.Value)!;

        // Compute the canonical LinkId: XorGuid(child.VisualId, key).
        // Since ChildVisualIdFromLinkId is self-inverse (XOR), applying it to childVisualId
        // gives the linkId (same operation both ways).
        var linkId = new LinkId(BTreeParentChildLink.ChildVisualIdFromLinkId(new LinkId(child.VisualId)));

        return (asset, sink, child, linkId);
    }

    // ---- ChildVisualIdFromLinkId round-trip ---------------------------------

    [Fact]
    public void ChildVisualIdFromLinkId_IsInverseOfLinkIdComputation()
    {
        // XOR is self-inverse: XorGuid(XorGuid(x, k), k) == x.
        // ChildVisualIdFromLinkId(new LinkId(childVisualId)) = linkId.Value
        // ChildVisualIdFromLinkId(linkId) = childVisualId
        var childVisualId = Guid.NewGuid();
        var linkIdValue   = BTreeParentChildLink.ChildVisualIdFromLinkId(new LinkId(childVisualId));
        var recovered     = BTreeParentChildLink.ChildVisualIdFromLinkId(new LinkId(linkIdValue));
        recovered.Should().Be(childVisualId, "XOR is self-inverse");
    }

    // ---- InsertReroute ------------------------------------------------------

    [Fact]
    public void InsertReroute_AppendsWaypointToCorrectChild()
    {
        var (asset, sink, child, linkId) = BuildWithChild();
        var pos = new Vector2(10f, 20f);

        var result = sink.Apply(new GraphCommand.InsertReroute(linkId, pos));

        result.Success.Should().BeTrue();
        child.Waypoints.Should().ContainSingle().Which.Should().Be(pos);
    }

    [Fact]
    public void InsertReroute_MultipleAppends_PreservesOrder()
    {
        var (_, sink, child, linkId) = BuildWithChild();
        var p1 = new Vector2(1f, 2f);
        var p2 = new Vector2(3f, 4f);
        var p3 = new Vector2(5f, 6f);

        sink.Apply(new GraphCommand.InsertReroute(linkId, p1));
        sink.Apply(new GraphCommand.InsertReroute(linkId, p2));
        sink.Apply(new GraphCommand.InsertReroute(linkId, p3));

        child.Waypoints.Should().Equal(p1, p2, p3);
    }

    [Fact]
    public void InsertReroute_UnknownLink_IsNoOp()
    {
        var (asset, sink, child, _) = BuildWithChild();
        var unknownLink = new LinkId(Guid.NewGuid());

        sink.Apply(new GraphCommand.InsertReroute(unknownLink, new Vector2(99f, 99f)));

        child.Waypoints.Should().BeEmpty("unknown link must not mutate any node");
    }

    [Fact]
    public void InsertReroute_MarksAssetDirty()
    {
        var (asset, sink, _, linkId) = BuildWithChild();
        asset.ClearDirty();

        sink.Apply(new GraphCommand.InsertReroute(linkId, new Vector2(1f, 2f)));

        asset.IsDirty.Should().BeTrue();
    }

    // ---- MoveReroute --------------------------------------------------------

    [Fact]
    public void MoveReroute_UpdatesWaypointAtIndex()
    {
        var (_, sink, child, linkId) = BuildWithChild();
        sink.Apply(new GraphCommand.InsertReroute(linkId, new Vector2(1f, 2f)));
        sink.Apply(new GraphCommand.InsertReroute(linkId, new Vector2(3f, 4f)));

        var newPos = new Vector2(99f, 88f);
        sink.Apply(new GraphCommand.MoveReroute(linkId, 0, newPos));

        child.Waypoints[0].Should().Be(newPos);
        child.Waypoints[1].Should().Be(new Vector2(3f, 4f), "index 1 must be unchanged");
    }

    [Fact]
    public void MoveReroute_OutOfRangeIndex_IsNoOp()
    {
        var (_, sink, child, linkId) = BuildWithChild();
        sink.Apply(new GraphCommand.InsertReroute(linkId, new Vector2(1f, 2f)));

        sink.Apply(new GraphCommand.MoveReroute(linkId, 5, new Vector2(99f, 88f)));

        child.Waypoints.Should().ContainSingle()
            .Which.Should().Be(new Vector2(1f, 2f), "out-of-range must not mutate");
    }

    [Fact]
    public void MoveReroute_NegativeIndex_IsNoOp()
    {
        var (_, sink, child, linkId) = BuildWithChild();
        sink.Apply(new GraphCommand.InsertReroute(linkId, new Vector2(1f, 2f)));

        sink.Apply(new GraphCommand.MoveReroute(linkId, -1, new Vector2(99f, 88f)));

        child.Waypoints.Should().ContainSingle()
            .Which.Should().Be(new Vector2(1f, 2f));
    }

    [Fact]
    public void MoveReroute_UnknownLink_IsNoOp()
    {
        var (_, sink, child, linkId) = BuildWithChild();
        sink.Apply(new GraphCommand.InsertReroute(linkId, new Vector2(1f, 2f)));

        var unknownLink = new LinkId(Guid.NewGuid());
        sink.Apply(new GraphCommand.MoveReroute(unknownLink, 0, new Vector2(99f, 88f)));

        child.Waypoints[0].Should().Be(new Vector2(1f, 2f), "unknown link must not mutate");
    }

    // ---- RemoveReroute ------------------------------------------------------

    [Fact]
    public void RemoveReroute_RemovesWaypointAtIndex()
    {
        var (_, sink, child, linkId) = BuildWithChild();
        sink.Apply(new GraphCommand.InsertReroute(linkId, new Vector2(1f, 2f)));
        sink.Apply(new GraphCommand.InsertReroute(linkId, new Vector2(3f, 4f)));

        sink.Apply(new GraphCommand.RemoveReroute(linkId, 0));

        child.Waypoints.Should().ContainSingle()
            .Which.Should().Be(new Vector2(3f, 4f));
    }

    [Fact]
    public void RemoveReroute_OutOfRangeIndex_IsNoOp()
    {
        var (_, sink, child, linkId) = BuildWithChild();
        sink.Apply(new GraphCommand.InsertReroute(linkId, new Vector2(1f, 2f)));

        sink.Apply(new GraphCommand.RemoveReroute(linkId, 5));

        child.Waypoints.Should().ContainSingle("out-of-range remove must not mutate");
    }

    [Fact]
    public void RemoveReroute_UnknownLink_IsNoOp()
    {
        var (_, sink, child, linkId) = BuildWithChild();
        sink.Apply(new GraphCommand.InsertReroute(linkId, new Vector2(1f, 2f)));

        var unknownLink = new LinkId(Guid.NewGuid());
        sink.Apply(new GraphCommand.RemoveReroute(unknownLink, 0));

        child.Waypoints.Should().ContainSingle("unknown link must not mutate");
    }

    // ---- BTreeGraphModel link projection ------------------------------------

    [Fact]
    public void GraphModel_FindLink_ReturnsChildWaypoints()
    {
        // Build a parent→child tree, add waypoints to the child node,
        // rebuild the graph model, and verify FindLink returns those waypoints.
        var asset = MakeAsset();
        var graph = new StubGraph();
        var sink  = new BTreeCommandSink(asset, graph);

        var parentId = NodeId.NewId();
        var childId  = NodeId.NewId();

        sink.Apply(new GraphCommand.AddNode(parentId, new NodeKindKey(BTreeKinds.Sequence), Vector2.Zero, null));
        sink.Apply(new GraphCommand.AddNode(childId,  new NodeKindKey(BTreeKinds.Action),   Vector2.Zero, null));

        graph.RegisterPins(parentId, out _, out var parentIn);
        graph.RegisterPins(childId,  out var childOut, out _);
        sink.Apply(new GraphCommand.AddLink(new LinkId(Guid.NewGuid()), childOut, parentIn));

        var child = asset.FindNode(childId.Value)!;
        var wp1   = new Vector2(5f, 10f);
        var wp2   = new Vector2(15f, 20f);
        child.Waypoints.Add(wp1);
        child.Waypoints.Add(wp2);

        // Rebuild graph model (projects all links fresh).
        var model  = new BTreeGraphModel(asset);
        var linkId = new LinkId(BTreeParentChildLink.ChildVisualIdFromLinkId(new LinkId(child.VisualId)));
        var link   = model.FindLink(linkId);

        link.Should().NotBeNull("the link must be projected");
        link!.Waypoints.Should().HaveCount(2);
        link.Waypoints[0].Should().Be(wp1);
        link.Waypoints[1].Should().Be(wp2);
    }
}
