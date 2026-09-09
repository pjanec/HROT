using System.Numerics;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.GraphEditor;
using Hrot.Blueprints.Editor.Host;
using Hrot.Blueprints.Editor.NodeDrawers;
using NodeEditor.Core;
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Blueprints.Tests.Host;

/// <summary>
/// BP-84 — delete a node, Ctrl+Z, and the node must come back as the SAME KIND with its pins.
///
/// <para>
/// The gesture is not <c>DeleteNodeCommand.Undo</c>. The canvas records the delete on the
/// editor's <c>UndoStack</c> as a forward/inverse PAIR (<c>EditCommands.BuildDeleteSelection</c>):
/// forward <c>RemoveNodes</c>, inverse <c>AddNode(n.Id, n.Kind, n.Position, {PinIds})</c>. So on
/// undo the node is <b>reconstructed from its kind string</b>, and the kind string the view model
/// hands over is <c>BlueprintNodeModel.Kind = node.GetType().Name</c> — i.e. "GetVariableNode".
/// </para>
///
/// <para>
/// These tests drive that exact pair through the sink.
/// </para>
/// </summary>
public sealed class UndoRestoresNodeWithPinsTests
{
    private static (BlueprintAsset asset, Graph graph, Guid varId) MakeAssetWithGetVariable()
    {
        var asset = new BlueprintAsset { AssetId = Guid.NewGuid(), Name = "A" };
        var graph = new Graph { Id = Guid.NewGuid(), Name = "EventGraph", Kind = GraphKind.Event };
        asset.Graphs.Add(graph);

        var varId = Guid.NewGuid();
        asset.Variables.Add(new VariableDecl
        {
            Id   = varId,
            Name = "Health",
            Type = new BlueprintTypeRef { TypeId = "System.Single" },
        });

        graph.Nodes.Add(new GetVariableNode { Id = Guid.NewGuid(), VariableId = $"var:{varId}" });
        return (asset, graph, varId);
    }

    private static (BlueprintCommandSink sink, BlueprintGraphModel model) MakeSink(
        BlueprintAsset asset, Graph graph)
    {
        var registry   = new NodeKindRegistry();
        var model      = new BlueprintGraphModel(asset, graph, registry);
        var catalog    = new BlueprintNodeCatalog(registry);
        var typeSystem = new BlueprintTypeSystem(NullPinDefaultValueEditorRegistry.Instance);
        var validator  = new BlueprintLinkValidator(model, typeSystem);
        var history    = new CommandHistory();
        var editSvc    = new EditService { Context = new EditServiceContext(history, _ => { }) };
        var sink = new BlueprintCommandSink(
            asset, graph, model, catalog, validator, history, editSvc, _ => { });
        return (sink, model);
    }

    /// <summary>
    /// Documents the kind string the canvas actually puts in the inverse command. If this ever
    /// changes, the reconstruction mapping below must change with it.
    /// </summary>
    [Fact]
    public void ViewModel_ExposesGetVariableNode_AsTypeName()
    {
        var (asset, graph, _) = MakeAssetWithGetVariable();
        var (_, model) = MakeSink(asset, graph);

        var nodeModel = model.Nodes.Single();

        Assert.Equal("GetVariableNode", nodeModel.Kind.Id);
    }

    /// <summary>
    /// The whole defect, end to end: delete then undo, exactly as the canvas issues it.
    /// </summary>
    [Fact]
    public void DeleteThenUndo_RestoresGetVariableNode_WithItsValuePin()
    {
        var (asset, graph, varId) = MakeAssetWithGetVariable();
        var (sink, model) = MakeSink(asset, graph);

        var nodeModel = model.Nodes.Single();
        var nodeId    = nodeModel.Id;
        var kind      = nodeModel.Kind;                       // "GetVariableNode"
        var pinIds    = nodeModel.Pins.Select(p => p.Id).ToList();
        var position  = nodeModel.Position;

        // forward — GraphCommand.RemoveNodes
        Assert.True(sink.Apply(new GraphCommand.RemoveNodes(new[] { nodeId })).Success);
        Assert.Empty(graph.Nodes);

        // inverse — exactly what EditCommands.BuildDeleteSelection records
        var inverse = new GraphCommand.AddNode(
            nodeId, kind, position,
            new Dictionary<string, object?> { ["PinIds"] = pinIds });
        Assert.True(sink.Apply(inverse).Success);

        // It must come back as a GetVariableNode — not a generic FunctionCallNode.
        Assert.Empty(graph.Nodes.OfType<FunctionCallNode>());
        var restored = Assert.Single(graph.Nodes.OfType<GetVariableNode>());

        // ...still bound to its variable...
        Assert.Equal($"var:{varId}", restored.VariableId);

        // ...and projecting its typed Value out-pin, so it can be rewired.
        var restoredModel = model.Nodes.Single(n => n.Id == new NodeId(restored.Id));
        var valuePin = Assert.Single(restoredModel.Pins);
        Assert.Equal("Value", valuePin.Label);
        Assert.Equal(PinDirection.Output, valuePin.Direction);
        Assert.Equal("System.Single", valuePin.Type!.Value.Id);
    }

    /// <summary>
    /// Same gesture for a Set node: it must come back with its exec in/out AND typed data-in.
    /// </summary>
    [Fact]
    public void DeleteThenUndo_RestoresSetVariableNode_WithItsPins()
    {
        var asset = new BlueprintAsset { AssetId = Guid.NewGuid(), Name = "A" };
        var graph = new Graph { Id = Guid.NewGuid(), Name = "EventGraph", Kind = GraphKind.Event };
        asset.Graphs.Add(graph);
        var varId = Guid.NewGuid();
        asset.Variables.Add(new VariableDecl
        {
            Id = varId, Name = "Health",
            Type = new BlueprintTypeRef { TypeId = "System.Single" },
        });
        graph.Nodes.Add(new SetVariableNode { Id = Guid.NewGuid(), VariableId = $"var:{varId}" });

        var (sink, model) = MakeSink(asset, graph);
        var nodeModel = model.Nodes.Single();

        Assert.True(sink.Apply(new GraphCommand.RemoveNodes(new[] { nodeModel.Id })).Success);
        Assert.True(sink.Apply(new GraphCommand.AddNode(
            nodeModel.Id, nodeModel.Kind, nodeModel.Position,
            new Dictionary<string, object?>
            {
                ["PinIds"] = nodeModel.Pins.Select(p => p.Id).ToList()
            })).Success);

        Assert.Empty(graph.Nodes.OfType<FunctionCallNode>());
        var restored = Assert.Single(graph.Nodes.OfType<SetVariableNode>());
        Assert.Equal($"var:{varId}", restored.VariableId);

        var restoredModel = model.Nodes.Single(n => n.Id == new NodeId(restored.Id));
        Assert.Contains(restoredModel.Pins, p => p.Label == "Value" && p.Direction == PinDirection.Input);
    }

    /// <summary>
    /// Deleting one of two sibling Get nodes must not disturb the other — the experiment that
    /// ruled out node ordering as the cause.
    /// </summary>
    [Fact]
    public void DeleteThenUndo_LeavesSiblingNodeIntact()
    {
        var (asset, graph, varId) = MakeAssetWithGetVariable();
        graph.Nodes.Add(new GetVariableNode { Id = Guid.NewGuid(), VariableId = $"var:{varId}" });

        var (sink, model) = MakeSink(asset, graph);
        var target  = model.Nodes.First();
        var sibling = model.Nodes.Last();
        var siblingId = sibling.Id;

        Assert.True(sink.Apply(new GraphCommand.RemoveNodes(new[] { target.Id })).Success);
        Assert.True(sink.Apply(new GraphCommand.AddNode(
            target.Id, target.Kind, target.Position,
            new Dictionary<string, object?>
            {
                ["PinIds"] = target.Pins.Select(p => p.Id).ToList()
            })).Success);

        Assert.Equal(2, graph.Nodes.OfType<GetVariableNode>().Count());
        var siblingModel = model.Nodes.Single(n => n.Id == siblingId);
        Assert.Single(siblingModel.Pins);
        Assert.Equal("System.Single", siblingModel.Pins[0].Type!.Value.Id);
    }
}
