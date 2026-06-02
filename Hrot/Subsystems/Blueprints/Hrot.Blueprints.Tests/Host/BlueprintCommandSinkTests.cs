using System.Numerics;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.GraphEditor;
using Hrot.Blueprints.Editor.Host;
using Hrot.Blueprints.Editor.NodeDrawers;
using Hrot.Blueprints.Tests.Builders;
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Blueprints.Tests.Host;

/// <summary>
/// Behavioral tests for <see cref="BlueprintCommandSink"/> (AIE-044).
/// All tests are headless (no ImGui).
/// </summary>
public sealed class BlueprintCommandSinkTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private static (BlueprintAsset asset, Graph graph) MakeAssetWithGraph()
    {
        var asset = BlueprintAssetBuilder.Instance("SinkTestAsset")
            .WithGraph("Main", GraphKind.Event, _ => { })
            .Build();
        return (asset, asset.Graphs[0]);
    }

    private static (BlueprintCommandSink sink,
                    BlueprintGraphModel  model,
                    BlueprintNodeCatalog catalog,
                    CommandHistory       history,
                    EditService          editService,
                    List<BlueprintAsset> dirtyLog)
        MakeSut(BlueprintAsset? asset = null, Graph? graph = null)
    {
        if (asset == null)
        {
            (asset, graph) = MakeAssetWithGraph();
        }
        else if (graph == null)
        {
            throw new ArgumentNullException(nameof(graph));
        }

        var typeSystem    = new BlueprintTypeSystem(NullPinDefaultValueEditorRegistry.Instance);
        var model         = new BlueprintGraphModel(asset, graph!);
        var catalog       = new BlueprintNodeCatalog(new NodeKindRegistry());
        var validator     = new BlueprintLinkValidator(model, typeSystem);
        var history       = new CommandHistory();
        var dirtyLog      = new List<BlueprintAsset>();
        var editService   = new EditService
        {
            Context = new EditServiceContext(history, a => dirtyLog.Add(a))
        };

        var sink = new BlueprintCommandSink(
            asset, graph!, model, catalog, validator, history, editService,
            markDirty: a => dirtyLog.Add(a));

        return (sink, model, catalog, history, editService, dirtyLog);
    }

    // Helper: create a pair of connected nodes in the asset.
    private static (Guid n1Id, Guid n1OutPinId, Guid n2Id, Guid n2InPinId)
        AddTwoConnectedNodes(BlueprintAsset asset, Graph graph)
    {
        var n1 = new FunctionCallNode { Id = Guid.NewGuid(), MethodName = "A" };
        var outPin = new Pin { Id = Guid.NewGuid(), Name = "Out", Direction = "Out", IsExec = false,
            TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" } };
        n1.Pins.Add(outPin);
        graph.Nodes.Add(n1);

        var n2 = new FunctionCallNode { Id = Guid.NewGuid(), MethodName = "B" };
        var inPin  = new Pin { Id = Guid.NewGuid(), Name = "In",  Direction = "In",  IsExec = false,
            TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" } };
        n2.Pins.Add(inPin);
        graph.Nodes.Add(n2);

        graph.Links.Add(new Link
        {
            FromNodeId = n1.Id, FromPinId = outPin.Id,
            ToNodeId   = n2.Id, ToPinId   = inPin.Id,
        });

        return (n1.Id, outPin.Id, n2.Id, inPin.Id);
    }

    // ── AddNode ───────────────────────────────────────────────────────────────

    [Fact]
    public void CommandSink_AddNode_AddsToAssetGraph()
    {
        var (asset, graph)              = MakeAssetWithGraph();
        var (sink, model, _, _, _, _)   = MakeSut(asset, graph);
        var initialCount = graph.Nodes.Count;

        var result = sink.Apply(new GraphCommand.AddNode(
            new NodeId(Guid.NewGuid()),
            new NodeKindKey("FunctionCallNode"),
            new Vector2(100, 200),
            null));

        // command succeeded
        Assert.True(result.Success);
        // node added to asset graph
        Assert.Equal(initialCount + 1, graph.Nodes.Count);
    }

    [Fact]
    public void CommandSink_AddNode_PositionStoredInEditorMetadata()
    {
        var (asset, graph)            = MakeAssetWithGraph();
        var (sink, model, _, _, _, _) = MakeSut(asset, graph);

        sink.Apply(new GraphCommand.AddNode(
            new NodeId(Guid.NewGuid()),
            new NodeKindKey("FunctionCallNode"),
            new Vector2(77f, 88f),
            null));

        var added = graph.Nodes.Last();
        Assert.Equal(77f, added.EditorMetadata.X, precision: 2);
        Assert.Equal(88f, added.EditorMetadata.Y, precision: 2);
    }

    [Fact]
    public void CommandSink_AddNode_ModelReflectsNewNode()
    {
        var (asset, graph)            = MakeAssetWithGraph();
        var (sink, model, _, _, _, _) = MakeSut(asset, graph);
        var before = model.Nodes.Count;

        sink.Apply(new GraphCommand.AddNode(
            new NodeId(Guid.NewGuid()),
            new NodeKindKey("FunctionCallNode"),
            Vector2.Zero,
            null));

        // model rebuilt — node count increased
        Assert.Equal(before + 1, model.Nodes.Count);
    }

    // ── RemoveNodes ──────────────────────────────────────────────────────────

    [Fact]
    public void CommandSink_RemoveNodes_Removes()
    {
        var (asset, graph)            = MakeAssetWithGraph();
        var node = new FunctionCallNode { Id = Guid.NewGuid(), MethodName = "R" };
        graph.Nodes.Add(node);
        var (sink, model, _, _, _, _) = MakeSut(asset, graph);

        var result = sink.Apply(new GraphCommand.RemoveNodes(
            new[] { new NodeId(node.Id) }));

        Assert.True(result.Success);
        Assert.DoesNotContain(graph.Nodes, n => n.Id == node.Id);
        Assert.DoesNotContain(model.Nodes, n => n.Id == new NodeId(node.Id));
    }

    [Fact]
    public void CommandSink_RemoveNodes_AlsoRemovesIncidentLinks()
    {
        var (asset, graph) = MakeAssetWithGraph();
        var (n1Id, _, n2Id, _) = AddTwoConnectedNodes(asset, graph);
        var (sink, model, _, _, _, _) = MakeSut(asset, graph);
        Assert.Single(graph.Links);

        sink.Apply(new GraphCommand.RemoveNodes(new[] { new NodeId(n1Id) }));

        Assert.Empty(graph.Links);
    }

    // ── AddLink ──────────────────────────────────────────────────────────────

    [Fact]
    public void CommandSink_AddLink_ConnectsPins_OnGraphLinks()
    {
        var (asset, graph) = MakeAssetWithGraph();

        // Create two nodes with typed data pins, no pre-existing link.
        var n1 = new FunctionCallNode { Id = Guid.NewGuid(), MethodName = "Src" };
        var outPin = new Pin { Id = Guid.NewGuid(), Name = "Result", Direction = "Out", IsExec = false,
            TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" } };
        n1.Pins.Add(outPin);
        graph.Nodes.Add(n1);

        var n2 = new FunctionCallNode { Id = Guid.NewGuid(), MethodName = "Dst" };
        var inPin = new Pin { Id = Guid.NewGuid(), Name = "Value", Direction = "In", IsExec = false,
            TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" } };
        n2.Pins.Add(inPin);
        graph.Nodes.Add(n2);

        var (sink, model, _, _, _, _) = MakeSut(asset, graph);

        var result = sink.Apply(new GraphCommand.AddLink(
            new LinkId(Guid.NewGuid()),
            new PinId(outPin.Id),
            new PinId(inPin.Id)));

        Assert.True(result.Success);
        Assert.Single(graph.Links);
        Assert.Equal(outPin.Id, graph.Links[0].FromPinId);
        Assert.Equal(inPin.Id,  graph.Links[0].ToPinId);
    }

    [Fact]
    public void CommandSink_AddLink_SingleDataInput_ReplacesExisting()
    {
        var (asset, graph) = MakeAssetWithGraph();

        // n1 out -> n2 in (pre-existing)
        var n1 = new FunctionCallNode { Id = Guid.NewGuid() };
        var out1 = new Pin { Id = Guid.NewGuid(), Name = "V", Direction = "Out", IsExec = false,
            TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" } };
        n1.Pins.Add(out1);
        graph.Nodes.Add(n1);

        var n2 = new FunctionCallNode { Id = Guid.NewGuid() };
        var in2 = new Pin { Id = Guid.NewGuid(), Name = "In", Direction = "In", IsExec = false,
            TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" } };
        n2.Pins.Add(in2);
        graph.Nodes.Add(n2);

        // n3 out — new source that will replace n1→n2
        var n3 = new FunctionCallNode { Id = Guid.NewGuid() };
        var out3 = new Pin { Id = Guid.NewGuid(), Name = "W", Direction = "Out", IsExec = false,
            TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" } };
        n3.Pins.Add(out3);
        graph.Nodes.Add(n3);

        // Pre-wire n1→n2
        graph.Links.Add(new Link { FromNodeId = n1.Id, FromPinId = out1.Id,
                                   ToNodeId   = n2.Id, ToPinId   = in2.Id });

        var (sink, model, _, _, _, _) = MakeSut(asset, graph);

        // Connect n3→n2 (should replace n1→n2)
        var result = sink.Apply(new GraphCommand.AddLink(
            new LinkId(Guid.NewGuid()),
            new PinId(out3.Id),
            new PinId(in2.Id)));

        Assert.True(result.Success);
        // Still exactly one link to in2
        var linksToIn2 = graph.Links.Where(l => l.ToPinId == in2.Id).ToList();
        Assert.Single(linksToIn2);
        Assert.Equal(out3.Id, linksToIn2[0].FromPinId);
    }

    // ── MoveNodes ────────────────────────────────────────────────────────────

    [Fact]
    public void CommandSink_MoveNodes_UpdatesPositions()
    {
        var (asset, graph) = MakeAssetWithGraph();
        var node = new FunctionCallNode { Id = Guid.NewGuid(),
            EditorMetadata = new NodeMetadata { X = 0, Y = 0 } };
        graph.Nodes.Add(node);
        var (sink, model, _, _, _, _) = MakeSut(asset, graph);

        var result = sink.Apply(new GraphCommand.MoveNodes(
            new[] { new NodeMove(new NodeId(node.Id), new Vector2(55f, 66f)) }));

        Assert.True(result.Success);
        Assert.Equal(55f, node.EditorMetadata.X, precision: 2);
        Assert.Equal(66f, node.EditorMetadata.Y, precision: 2);

        // Also verify the model reflects the new position.
        var modelNode = model.FindNode(new NodeId(node.Id));
        Assert.NotNull(modelNode);
        Assert.Equal(55f, modelNode!.Position.X, precision: 2);
        Assert.Equal(66f, modelNode!.Position.Y, precision: 2);
    }

    /// <summary>
    /// BCP-B: After MoveNodes the SAME INodeModel instance is returned by FindNode
    /// (no full rebuild occurred).  This verifies identity preservation — the canvas
    /// can safely hold references to node models across drag frames.
    /// </summary>
    [Fact]
    public void CommandSink_MoveNodes_SameInstanceIdentityPreserved()
    {
        var (asset, graph) = MakeAssetWithGraph();
        var node = new FunctionCallNode { Id = Guid.NewGuid(),
            EditorMetadata = new NodeMetadata { X = 0, Y = 0 } };
        graph.Nodes.Add(node);
        var (sink, model, _, _, _, _) = MakeSut(asset, graph);

        // Capture the reference BEFORE the move.
        var instanceBefore = model.FindNode(new NodeId(node.Id));
        Assert.NotNull(instanceBefore);

        sink.Apply(new GraphCommand.MoveNodes(
            new[] { new NodeMove(new NodeId(node.Id), new Vector2(100f, 200f)) }));

        // FindNode must return the SAME object reference (no rebuild replaced it).
        var instanceAfter = model.FindNode(new NodeId(node.Id));
        Assert.NotNull(instanceAfter);
        Assert.Same(instanceBefore, instanceAfter);

        // And the position must have been updated in place.
        Assert.Equal(100f, instanceAfter!.Position.X, precision: 2);
        Assert.Equal(200f, instanceAfter.Position.Y, precision: 2);
    }

    /// <summary>
    /// BCP-B: MoveNodes fires NodesMoved (not Wholesale) and does NOT trigger a Rebuild —
    /// verified by counting Changed notifications of each kind.
    /// </summary>
    [Fact]
    public void CommandSink_MoveNodes_FiresNodesMoved_NotWholesale()
    {
        var (asset, graph) = MakeAssetWithGraph();
        var node = new FunctionCallNode { Id = Guid.NewGuid(),
            EditorMetadata = new NodeMetadata { X = 0, Y = 0 } };
        graph.Nodes.Add(node);
        var (sink, model, _, _, _, _) = MakeSut(asset, graph);

        int wholesaleCount = 0;
        int nodesMovedCount = 0;
        model.Changed += n =>
        {
            if (n.Kind == GraphChangeKind.Wholesale) wholesaleCount++;
            if (n.Kind == GraphChangeKind.NodesMoved) nodesMovedCount++;
        };

        sink.Apply(new GraphCommand.MoveNodes(
            new[] { new NodeMove(new NodeId(node.Id), new Vector2(50f, 75f)) }));

        Assert.Equal(0, wholesaleCount);
        Assert.Equal(1, nodesMovedCount);
    }

    // ── SetNodeProperty ──────────────────────────────────────────────────────

    [Fact]
    public void CommandSink_SetProperty_UpdatesNode()
    {
        var (asset, graph) = MakeAssetWithGraph();
        var node = new FunctionCallNode { Id = Guid.NewGuid(), MethodName = "Old" };
        graph.Nodes.Add(node);
        var (sink, _, _, _, _, _) = MakeSut(asset, graph);

        var result = sink.Apply(new GraphCommand.SetNodeProperty(
            new NodeId(node.Id), "MethodName", "New"));

        Assert.True(result.Success);
        Assert.Equal("New", node.MethodName);
    }

    [Fact]
    public void CommandSink_SetProperty_Comment_UpdatesEditorMetadata()
    {
        var (asset, graph) = MakeAssetWithGraph();
        var node = new FunctionCallNode { Id = Guid.NewGuid() };
        graph.Nodes.Add(node);
        var (sink, _, _, _, _, _) = MakeSut(asset, graph);

        sink.Apply(new GraphCommand.SetNodeProperty(new NodeId(node.Id), "Comment", "hello"));

        Assert.Equal("hello", node.EditorMetadata.Comment);
    }

    // ── MarksDirty ───────────────────────────────────────────────────────────

    [Fact]
    public void CommandSink_MarksDirty_AfterMutation()
    {
        var (asset, graph)              = MakeAssetWithGraph();
        var (sink, _, _, _, _, dirtyLog) = MakeSut(asset, graph);
        var beforeCount = dirtyLog.Count;

        sink.Apply(new GraphCommand.AddNode(
            new NodeId(Guid.NewGuid()),
            new NodeKindKey("FunctionCallNode"),
            Vector2.Zero,
            null));

        Assert.True(dirtyLog.Count > beforeCount, "Asset should have been marked dirty.");
        Assert.Contains(asset, dirtyLog);
    }

    // ── Batch ────────────────────────────────────────────────────────────────

    [Fact]
    public void CommandSink_Batch_AppliesAll()
    {
        var (asset, graph)            = MakeAssetWithGraph();
        var (sink, model, _, _, _, _) = MakeSut(asset, graph);
        var before = graph.Nodes.Count;

        var result = sink.Apply(new GraphCommand.Batch("Add two nodes", new GraphCommand[]
        {
            new GraphCommand.AddNode(new NodeId(Guid.NewGuid()), new NodeKindKey("FunctionCallNode"), Vector2.Zero, null),
            new GraphCommand.AddNode(new NodeId(Guid.NewGuid()), new NodeKindKey("FunctionCallNode"), new Vector2(100,0), null),
        }));

        Assert.True(result.Success);
        Assert.Equal(before + 2, graph.Nodes.Count);
    }

    [Fact]
    public void CommandSink_Batch_StopsOnFirstFailure()
    {
        var (asset, graph)            = MakeAssetWithGraph();
        var (sink, model, _, _, _, _) = MakeSut(asset, graph);
        var before = graph.Nodes.Count;

        // First command: add a node (succeeds).
        // Second command: add a link between non-existent pins (will fail with "Pin not found").
        // The batch should report failure after the link attempt fails; the already-added node
        // from command 1 remains (batch does not roll back completed commands).
        var nodeId = new NodeId(Guid.NewGuid());
        var result = sink.Apply(new GraphCommand.Batch("Fail batch", new GraphCommand[]
        {
            new GraphCommand.AddNode(nodeId, new NodeKindKey("FunctionCallNode"), Vector2.Zero, null),
            // Link with non-existent pin ids — fails during validation.
            new GraphCommand.AddLink(
                new LinkId(Guid.NewGuid()),
                new PinId(Guid.NewGuid()),   // does not exist in model
                new PinId(Guid.NewGuid())),  // does not exist in model
        }));

        Assert.False(result.Success);
    }

    // ── Undo ─────────────────────────────────────────────────────────────────

    [Fact]
    public void CommandSink_AddNode_Undo_RemovesNode()
    {
        var (asset, graph)                  = MakeAssetWithGraph();
        var (sink, model, _, history, _, _) = MakeSut(asset, graph);
        var before = graph.Nodes.Count;

        sink.Apply(new GraphCommand.AddNode(
            new NodeId(Guid.NewGuid()),
            new NodeKindKey("FunctionCallNode"),
            Vector2.Zero, null));

        Assert.Equal(before + 1, graph.Nodes.Count);

        history.Undo();

        // The AddNodeCommand's Undo() removes the node from the graph.
        Assert.Equal(before, graph.Nodes.Count);
    }
}
