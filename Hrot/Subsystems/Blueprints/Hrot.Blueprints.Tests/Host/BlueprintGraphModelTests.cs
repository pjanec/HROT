using System;
using System.Collections.Generic;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.Host;
using Hrot.Blueprints.Tests.Builders;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Blueprints.Tests.Host;

/// <summary>
/// Tests for <see cref="BlueprintGraphModel"/>.
/// All tests are headless (no ImGui).
/// </summary>
public sealed class BlueprintGraphModelTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>Builds a known BlueprintAsset with 3 nodes connected in a chain.</summary>
    private static (BlueprintAsset asset, Graph graph) BuildLinearGraph()
    {
        var asset = BlueprintAssetBuilder.Instance("TestAsset")
            .WithGraph("EventGraph", GraphKind.Event, g => g
                .Entry()
                .Delay(0f)
                .Return())
            .Build();

        return (asset, asset.Graphs[0]);
    }

    private static BlueprintGraphModel MakeSut(BlueprintAsset asset, Graph graph)
        => new(asset, graph);

    // ── projection: nodes ─────────────────────────────────────────────────────

    [Fact]
    public void ProjectsNodesAndPins_NodeCount_MatchesAsset()
    {
        var (asset, graph) = BuildLinearGraph();
        var sut = MakeSut(asset, graph);

        Assert.Equal(graph.Nodes.Count, sut.Nodes.Count);
    }

    [Fact]
    public void ProjectsNodes_NodeIds_MatchAssetNodeGuids()
    {
        var (asset, graph) = BuildLinearGraph();
        var sut = MakeSut(asset, graph);

        var projectedIds = sut.Nodes.Select(n => n.Id.Value).ToHashSet();
        var assetIds     = graph.Nodes.Select(n => n.Id).ToHashSet();

        Assert.Equal(assetIds, projectedIds);
    }

    [Fact]
    public void ProjectsNodes_ExactThreeNodes()
    {
        var (asset, graph) = BuildLinearGraph();
        var sut = MakeSut(asset, graph);

        // Entry + Delay + Return = 3 nodes
        Assert.Equal(3, sut.Nodes.Count);
    }

    [Fact]
    public void ProjectsNodes_EachNodeHasPins()
    {
        var (asset, graph) = BuildLinearGraph();
        var sut = MakeSut(asset, graph);

        foreach (var node in sut.Nodes)
            Assert.True(node.Pins.Count > 0, $"Node {node.Id} has no pins");
    }

    [Fact]
    public void ProjectsPins_PinIds_MatchAssetPinGuids()
    {
        var (asset, graph) = BuildLinearGraph();
        var sut = MakeSut(asset, graph);

        var assetPinIds     = graph.Nodes.SelectMany(n => n.Pins).Select(p => p.Id).ToHashSet();
        var projectedPinIds = sut.Nodes.SelectMany(n => n.Pins).Select(p => p.Id.Value).ToHashSet();

        Assert.Equal(assetPinIds, projectedPinIds);
    }

    // ── projection: pins (exec vs data) ───────────────────────────────────────

    [Fact]
    public void ExecPins_HaveKindExec()
    {
        var (asset, graph) = BuildLinearGraph();
        var sut = MakeSut(asset, graph);

        var assetExecPinIds = graph.Nodes
            .SelectMany(n => n.Pins)
            .Where(p => p.IsExec)
            .Select(p => p.Id)
            .ToHashSet();

        foreach (var node in sut.Nodes)
        foreach (var pin in node.Pins.Where(p => assetExecPinIds.Contains(p.Id.Value)))
            Assert.Equal(PinKind.Exec, pin.Kind);
    }

    [Fact]
    public void DataPins_HaveKindData()
    {
        var (asset, graph) = BuildLinearGraph();
        var sut = MakeSut(asset, graph);

        var assetDataPinIds = graph.Nodes
            .SelectMany(n => n.Pins)
            .Where(p => !p.IsExec)
            .Select(p => p.Id)
            .ToHashSet();

        foreach (var node in sut.Nodes)
        foreach (var pin in node.Pins.Where(p => assetDataPinIds.Contains(p.Id.Value)))
            Assert.Equal(PinKind.Data, pin.Kind);
    }

    [Fact]
    public void ExecPins_HaveNullType()
    {
        var (asset, graph) = BuildLinearGraph();
        var sut = MakeSut(asset, graph);

        var assetExecPinIds = graph.Nodes
            .SelectMany(n => n.Pins)
            .Where(p => p.IsExec)
            .Select(p => p.Id)
            .ToHashSet();

        foreach (var node in sut.Nodes)
        foreach (var pin in node.Pins.Where(p => assetExecPinIds.Contains(p.Id.Value)))
            Assert.Null(pin.Type);
    }

    // ── projection: links ─────────────────────────────────────────────────────

    [Fact]
    public void ProjectsLinks_LinkCount_MatchesAsset()
    {
        var (asset, graph) = BuildLinearGraph();
        var sut = MakeSut(asset, graph);

        Assert.Equal(graph.Links.Count, sut.Links.Count);
    }

    [Fact]
    public void ProjectsLinks_ExactTwoLinks()
    {
        var (asset, graph) = BuildLinearGraph();
        var sut = MakeSut(asset, graph);

        // Entry→Delay and Delay→Return = 2 exec links
        Assert.Equal(2, sut.Links.Count);
    }

    [Fact]
    public void ProjectsLinks_FromAndToPins_MatchAsset()
    {
        var (asset, graph) = BuildLinearGraph();
        var sut = MakeSut(asset, graph);

        foreach (var assetLink in graph.Links)
        {
            var linkId    = BlueprintGraphModel.MakeLinkId(assetLink.FromPinId, assetLink.ToPinId);
            var linkModel = sut.FindLink(linkId);

            Assert.NotNull(linkModel);
            Assert.Equal(assetLink.FromPinId, linkModel!.FromPin.Value);
            Assert.Equal(assetLink.ToPinId,   linkModel.ToPin.Value);
        }
    }

    // ── FindNode / FindPin / FindLink ─────────────────────────────────────────

    [Fact]
    public void FindNode_ExistingId_ReturnsNode()
    {
        var (asset, graph) = BuildLinearGraph();
        var sut = MakeSut(asset, graph);

        var firstNode = graph.Nodes[0];
        var found     = sut.FindNode(new NodeId(firstNode.Id));

        Assert.NotNull(found);
        Assert.Equal(firstNode.Id, found!.Id.Value);
    }

    [Fact]
    public void FindNode_MissingId_ReturnsNull()
    {
        var (asset, graph) = BuildLinearGraph();
        var sut = MakeSut(asset, graph);

        Assert.Null(sut.FindNode(new NodeId(Guid.NewGuid())));
    }

    [Fact]
    public void FindPin_ExistingId_ReturnsPin()
    {
        var (asset, graph) = BuildLinearGraph();
        var sut = MakeSut(asset, graph);

        var firstPin = graph.Nodes[0].Pins[0];
        var found    = sut.FindPin(new PinId(firstPin.Id));

        Assert.NotNull(found);
        Assert.Equal(firstPin.Id, found!.Id.Value);
    }

    [Fact]
    public void FindPin_MissingId_ReturnsNull()
    {
        var (asset, graph) = BuildLinearGraph();
        var sut = MakeSut(asset, graph);

        Assert.Null(sut.FindPin(new PinId(Guid.NewGuid())));
    }

    [Fact]
    public void FindLink_ExistingId_ReturnsLink()
    {
        var (asset, graph) = BuildLinearGraph();
        var sut = MakeSut(asset, graph);

        var assetLink = graph.Links[0];
        var linkId    = BlueprintGraphModel.MakeLinkId(assetLink.FromPinId, assetLink.ToPinId);

        Assert.NotNull(sut.FindLink(linkId));
    }

    [Fact]
    public void FindLink_MissingId_ReturnsNull()
    {
        var (asset, graph) = BuildLinearGraph();
        var sut = MakeSut(asset, graph);

        Assert.Null(sut.FindLink(LinkId.NewId()));
    }

    // ── Changed notification ──────────────────────────────────────────────────

    [Fact]
    public void FiresChanged_WhenRebuildAndNotifyIsCalled()
    {
        var (asset, graph) = BuildLinearGraph();
        var sut = MakeSut(asset, graph);

        int fired = 0;
        sut.Changed += _ => fired++;

        sut.RebuildAndNotify();

        Assert.Equal(1, fired);
    }

    [Fact]
    public void FiresChanged_WithWholesaleKind()
    {
        var (asset, graph) = BuildLinearGraph();
        var sut = MakeSut(asset, graph);

        GraphChangeKind? capturedKind = null;
        sut.Changed += n => capturedKind = n.Kind;

        sut.RebuildAndNotify();

        Assert.Equal(GraphChangeKind.Wholesale, capturedKind);
    }

    [Fact]
    public void AfterRebuild_NewNodeIsVisible()
    {
        var (asset, graph) = BuildLinearGraph();
        var sut = MakeSut(asset, graph);

        var nodeCountBefore = sut.Nodes.Count;

        // Mutate the asset by adding a new node directly.
        var newNode = new BranchNode
        {
            Id = Guid.NewGuid(),
            Pins =
            {
                new Pin { Id = Guid.NewGuid(), Name = "In",   Direction = "In",  IsExec = true },
                new Pin { Id = Guid.NewGuid(), Name = "True", Direction = "Out", IsExec = true },
            }
        };
        graph.Nodes.Add(newNode);

        // Before rebuild, the projection is stale.
        Assert.Equal(nodeCountBefore, sut.Nodes.Count);

        // After rebuild, the new node is visible.
        sut.Rebuild();
        Assert.Equal(nodeCountBefore + 1, sut.Nodes.Count);
        Assert.NotNull(sut.FindNode(new NodeId(newNode.Id)));
    }

    // ── graph identity ────────────────────────────────────────────────────────

    [Fact]
    public void GraphId_IsDeterministicForSameAssetAndGraph()
    {
        var (asset, graph) = BuildLinearGraph();
        var sut1 = new BlueprintGraphModel(asset, graph);
        var sut2 = new BlueprintGraphModel(asset, graph);

        Assert.Equal(sut1.Id, sut2.Id);
    }

    [Fact]
    public void DisplayName_MatchesGraphName()
    {
        var (asset, graph) = BuildLinearGraph();
        var sut = MakeSut(asset, graph);

        Assert.Equal(graph.Name, sut.DisplayName);
    }

    // ── BUG 2 (BCP-BATCH-01-FIX): partial-connection wire resolution ──────────

    /// <summary>
    /// Builds an in-memory graph where a Branch node has TWO exec-out pins
    /// (ExecOutTrue, ExecOutFalse) but only ONE is connected.
    /// The old positional algorithm would fail to assign the second link's FromPinId
    /// because it was already consumed by the first output pin.
    /// The new link-GUID-driven algorithm must ensure every link endpoint resolves via FindPin.
    /// </summary>
    private static (BlueprintAsset asset, Graph graph) BuildBranchWithPartialConnections()
    {
        var assetId = Guid.NewGuid();

        // Build a Branch node with 3 pins (no pre-stored asset Pins — slow path).
        // We deliberately leave Pins empty so the slow path runs.
        var branchNode = new BranchNode { Id = Guid.NewGuid() };
        // Pins intentionally empty → slow path.

        // A downstream Return node (exec-in only), also no pre-stored pins.
        var returnNode = new ReturnNode { Id = Guid.NewGuid() };

        // The asset link connects branch's TRUE output to return's exec-in.
        var fromPinId = Guid.NewGuid();  // represents ExecOutTrue on BranchNode
        var toPinId   = Guid.NewGuid();  // represents ExecIn on ReturnNode

        var graph = new Graph
        {
            Id    = Guid.NewGuid(),
            Name  = "Test",
            Kind  = GraphKind.Event,
            Nodes = new List<Node> { branchNode, returnNode },
            Links = new List<Link>
            {
                new Link
                {
                    FromNodeId = branchNode.Id,
                    FromPinId  = fromPinId,
                    ToNodeId   = returnNode.Id,
                    ToPinId    = toPinId,
                }
            },
            Inputs  = new(),
            Outputs = new(),
        };

        var asset = new BlueprintAsset
        {
            AssetId = assetId,
            Name    = "PartialBranch",
            Header  = new Header(),
            Graphs  = new List<Graph> { graph },
        };

        return (asset, graph);
    }

    /// <summary>
    /// After Rebuild on an asset where a Branch node has only SOME outputs connected
    /// (Pins list empty — slow path), every graph.Links entry must resolve via FindPin.
    /// This proves the link-GUID-driven binding algorithm works for partial connections.
    /// </summary>
    [Fact]
    public void SlowPath_PartialBranchConnections_AllLinksResolveViaFindPin()
    {
        var (asset, graph) = BuildBranchWithPartialConnections();
        var sut = MakeSut(asset, graph);

        // Every link endpoint must resolve.
        foreach (var assetLink in graph.Links)
        {
            var fromPin = sut.FindPin(new PinId(assetLink.FromPinId));
            var toPin   = sut.FindPin(new PinId(assetLink.ToPinId));

            Assert.NotNull(fromPin);  // was null with old positional algorithm
            Assert.NotNull(toPin);
        }
    }

    /// <summary>
    /// A Sequence node has multiple exec-out pins (one per branch).
    /// If only the SECOND out-pin is connected (first is free), the link's FromPinId
    /// must still resolve.  The new algorithm assigns distinct link GUIDs first, so the
    /// first output gets the first (and only) link GUID, covering the case where a node
    /// has more outputs than links (unconnected pins get deterministic GUIDs).
    /// </summary>
    [Fact]
    public void SlowPath_SequenceNodeSecondOutputConnected_LinkResolves()
    {
        var assetId    = Guid.NewGuid();
        var seqNode    = new SequenceNode { Id = Guid.NewGuid() };
        var returnNode = new ReturnNode   { Id = Guid.NewGuid() };

        // Only one link: from sequence's output (only one link GUID in outLinks).
        var fromPinId = Guid.NewGuid();
        var toPinId   = Guid.NewGuid();

        var graph = new Graph
        {
            Id    = Guid.NewGuid(),
            Name  = "SeqTest",
            Kind  = GraphKind.Event,
            Nodes = new List<Node> { seqNode, returnNode },
            Links = new List<Link>
            {
                new Link { FromNodeId = seqNode.Id, FromPinId = fromPinId,
                           ToNodeId   = returnNode.Id, ToPinId = toPinId }
            },
            Inputs = new(), Outputs = new(),
        };
        var asset = new BlueprintAsset
        {
            AssetId = assetId,
            Name    = "SeqTest",
            Header  = new Header(),
            Graphs  = new List<Graph> { graph },
        };

        var sut = new BlueprintGraphModel(asset, graph);

        // The single link must fully resolve.
        var fromPin = sut.FindPin(new PinId(fromPinId));
        var toPin   = sut.FindPin(new PinId(toPinId));

        Assert.NotNull(fromPin);
        Assert.NotNull(toPin);
    }

    /// <summary>
    /// Fan-out: multiple links sharing the same FromPinId (one output pin → multiple inputs).
    /// FindPin must return the same pin for both links' FromPinId.
    /// </summary>
    [Fact]
    public void SlowPath_FanOut_SameFromPinIdResolvesBothLinks()
    {
        var assetId   = Guid.NewGuid();
        var srcNode   = new FunctionCallNode { Id = Guid.NewGuid(), MethodName = "Src" };
        var dst1Node  = new ReturnNode { Id = Guid.NewGuid() };
        var dst2Node  = new ReturnNode { Id = Guid.NewGuid() };

        var fromPinId = Guid.NewGuid(); // shared by both links (fan-out)
        var toPin1Id  = Guid.NewGuid();
        var toPin2Id  = Guid.NewGuid();

        var graph = new Graph
        {
            Id    = Guid.NewGuid(),
            Name  = "FanOut",
            Kind  = GraphKind.Event,
            Nodes = new List<Node> { srcNode, dst1Node, dst2Node },
            Links = new List<Link>
            {
                new Link { FromNodeId = srcNode.Id, FromPinId = fromPinId,
                           ToNodeId   = dst1Node.Id, ToPinId  = toPin1Id },
                new Link { FromNodeId = srcNode.Id, FromPinId = fromPinId,
                           ToNodeId   = dst2Node.Id, ToPinId  = toPin2Id },
            },
            Inputs = new(), Outputs = new(),
        };
        var asset = new BlueprintAsset
        {
            AssetId = assetId,
            Name    = "FanOut",
            Header  = new Header(),
            Graphs  = new List<Graph> { graph },
        };

        var sut = new BlueprintGraphModel(asset, graph);

        // All three GUIDs must resolve.
        Assert.NotNull(sut.FindPin(new PinId(fromPinId)));
        Assert.NotNull(sut.FindPin(new PinId(toPin1Id)));
        Assert.NotNull(sut.FindPin(new PinId(toPin2Id)));
    }
}
