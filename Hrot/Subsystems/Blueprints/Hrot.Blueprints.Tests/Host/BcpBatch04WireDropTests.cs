using System.Numerics;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor;
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
/// BCP-BATCH-04 Task 1 tests: the wire-drop auto-connect must honor
/// <c>AddNode.InitialProperties["PinIds"]</c>.
/// <para>
/// These reproduce the exact command sequence NodeEdit's <c>CanvasInput</c> wire-drop
/// create-path emits: pre-generate a <see cref="List{PinId}"/> sized
/// <c>entry.Inputs.Count + entry.Outputs.Count</c> (inputs-then-outputs order, matching
/// <c>BlueprintNodeCatalog.DescriptorToEntry</c>), ship it as <c>InitialProperties["PinIds"]</c>
/// on the <see cref="GraphCommand.AddNode"/>, then form the auto-connect
/// <see cref="GraphCommand.AddLink"/> referencing <c>pinIds[targetIdx]</c>, and execute both as a
/// single <see cref="GraphCommand.Batch"/>.
/// </para>
/// <para>
/// The assertions prove a <b>real connection</b>, not just "a link was added":
/// the link is in <c>graph.Links</c>, BOTH endpoints resolve in the model
/// (<c>FindPin != null</c>), the resolved target pin is owned by the NEW node, and the
/// model link wires source-out → new-node-in.
/// </para>
/// All tests are headless (no ImGui context).
/// </summary>
public sealed class BcpBatch04WireDropTests
{
    private static (BlueprintAsset asset, Graph graph) MakeAssetWithGraph()
    {
        var asset = BlueprintAssetBuilder.Instance("Batch04Asset")
            .WithGraph("EventGraph", GraphKind.Event, _ => { })
            .Build();
        return (asset, asset.Graphs[0]);
    }

    private static (BlueprintCommandSink sink, BlueprintGraphModel model, BlueprintNodeCatalog catalog)
        MakeSink(BlueprintAsset asset, Graph graph)
    {
        // Use the production palette registry so kind ids ("EventEntry", "ChannelCommand",
        // "GetVariable", "SetVariable") resolve to real typed nodes — exactly as production wires.
        var registry   = BlueprintEditorBootstrap.CreatePaletteRegistry();
        var model      = new BlueprintGraphModel(asset, graph, registry);
        var catalog    = new BlueprintNodeCatalog(registry);
        var typeSystem = new BlueprintTypeSystem(NullPinDefaultValueEditorRegistry.Instance);
        var validator  = new BlueprintLinkValidator(model, typeSystem);
        var history    = new CommandHistory();
        var editSvc    = new EditService { Context = new EditServiceContext(history, _ => { }) };
        var sink = new BlueprintCommandSink(
            asset, graph, model, catalog, validator, history, editSvc, _ => { });
        return (sink, model, catalog);
    }

    /// <summary>
    /// Builds the inputs-then-outputs PinId list the canvas would pre-generate for
    /// <paramref name="kindId"/>, exactly the way CanvasInput does:
    /// <c>entry.Inputs.Count + entry.Outputs.Count</c> fresh GUIDs, inputs first.
    /// Returns the catalog entry too so the test can locate the compatible target index.
    /// </summary>
    private static (List<PinId> pinIds, NodeCatalogEntry entry) PreGeneratePinIds(
        BlueprintNodeCatalog catalog, string kindId)
    {
        var entry = catalog.All.Single(e => e.Kind.Id == kindId);
        int total = entry.Inputs.Count + entry.Outputs.Count;
        var pinIds = new List<PinId>(total);
        for (int i = 0; i < total; i++)
            pinIds.Add(new PinId(Guid.NewGuid()));
        return (pinIds, entry);
    }

    // ── EXEC wire-drop ─────────────────────────────────────────────────────────

    /// <summary>
    /// EXEC wire-drop: an existing EventEntry node's exec-OUT pin is dragged onto empty canvas,
    /// the user picks "ChannelCommand", and the canvas emits AddNode({PinIds:[...]}) + AddLink to
    /// the new node's exec-IN PinId (pinIds[0]) as a Batch.
    /// After Apply the link must be present AND resolve AND own the target pin on the new node.
    /// </summary>
    [Fact]
    public void WireDrop_Exec_EventEntryToChannelCommand_ConnectsToNewNode()
    {
        var (asset, graph) = MakeAssetWithGraph();

        // Existing source node with a REAL authored exec-out pin (stable GUID).
        var source = new EventEntryNode { Id = Guid.NewGuid(), EventTypeId = "OnStart" };
        var sourceOutPinId = Guid.NewGuid();
        source.Pins.Add(new Pin
        {
            Id = sourceOutPinId, Name = "Out", Direction = "Out", IsExec = true,
            TypeRef = new BlueprintTypeRef(),
        });
        graph.Nodes.Add(source);

        var (sink, model, catalog) = MakeSink(asset, graph);

        // 1. Pre-generate PinIds for the picked kind (ChannelCommand: exec In + exec Out → 2 pins).
        var (pinIds, entry) = PreGeneratePinIds(catalog, "ChannelCommand");
        Assert.Equal(1, entry.Inputs.Count);   // exec "In"
        Assert.Equal(1, entry.Outputs.Count);  // exec "Out"

        // The source is an exec-OUT, so the auto-connect target is the new node's exec-IN.
        // CanvasInput walks entry.Inputs first → index 0 is the exec-IN.
        int targetIdx = 0;
        var targetPinId = pinIds[targetIdx];

        var newNodeId = new NodeId(Guid.NewGuid());
        var addNode = new GraphCommand.AddNode(
            newNodeId,
            new NodeKindKey("ChannelCommand"),
            new Vector2(380, 220),
            new Dictionary<string, object?> { ["PinIds"] = pinIds });

        // source exec-out → new node exec-in.
        var addLink = new GraphCommand.AddLink(
            new LinkId(Guid.NewGuid()),
            new PinId(sourceOutPinId),
            targetPinId);

        var result = sink.Apply(new GraphCommand.Batch(
            "Add Node", new GraphCommand[] { addNode, addLink }));

        Assert.True(result.Success, result.Message);

        // The new node is a real ChannelCommandNode (not a fallback FunctionCallNode).
        var newAssetNode = graph.Nodes.OfType<ChannelCommandNode>().Single();
        Assert.Empty(graph.Nodes.OfType<FunctionCallNode>());
        var newNodeModelId = new NodeId(newAssetNode.Id);

        // ── REAL connection assertions ──────────────────────────────────────
        // 1. The link is in the asset graph wiring source-out → the supplied target GUID.
        var assetLink = Assert.Single(graph.Links);
        Assert.Equal(sourceOutPinId,        assetLink.FromPinId);
        Assert.Equal(targetPinId.Value,     assetLink.ToPinId);
        Assert.Equal(newAssetNode.Id,       assetLink.ToNodeId);

        // 2. Both endpoints resolve in the model (FindPin != null) — the whole point of PinIds.
        var fromPin = model.FindPin(new PinId(sourceOutPinId));
        var toPin   = model.FindPin(targetPinId);
        Assert.NotNull(fromPin);
        Assert.NotNull(toPin);

        // 3. The resolved target pin is owned by the NEW node and is its exec-IN pin.
        Assert.Equal(newNodeModelId,     toPin!.OwnerNodeId);
        Assert.Equal(PinKind.Exec,       toPin.Kind);
        Assert.Equal(PinDirection.Input, toPin.Direction);

        // 4. The model link resolves and wires source-out → new-node-in.
        var linkId = BlueprintGraphModel.MakeLinkId(sourceOutPinId, targetPinId.Value);
        var link   = model.FindLink(linkId);
        Assert.NotNull(link);
        Assert.Equal(new PinId(sourceOutPinId), link!.FromPin);
        Assert.Equal(targetPinId,               link.ToPin);
    }

    // ── DATA wire-drop ─────────────────────────────────────────────────────────

    /// <summary>
    /// DATA wire-drop: an existing GetVariable node's typed data-OUT pin (System.Int32) is dragged
    /// onto empty canvas, the user picks "SetVariable" (whose Value data-IN is System.Int32), and
    /// the canvas emits AddNode({PinIds:[...]}) + AddLink to the new node's Value data-IN PinId.
    /// The data wire must connect to the new node's data-IN pin specifically (not an exec pin).
    /// </summary>
    [Fact]
    public void WireDrop_Data_TypedOutToSetVariableValueIn_ConnectsToNewNode()
    {
        var (asset, graph) = MakeAssetWithGraph();

        // Declare an Int32 variable so SetVariable's Value pins resolve to System.Int32.
        var varId = Guid.NewGuid();
        asset.Variables.Add(new VariableDecl
        {
            Id = varId, Name = "Ammo",
            Type = new BlueprintTypeRef { TypeId = "System.Int32" },
        });

        // Existing source node with a REAL authored typed data-out pin (System.Int32).
        var source = new GetVariableNode { Id = Guid.NewGuid(), VariableId = varId.ToString() };
        var sourceDataOutId = Guid.NewGuid();
        source.Pins.Add(new Pin
        {
            Id = sourceDataOutId, Name = "Value", Direction = "Out", IsExec = false,
            TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" },
        });
        graph.Nodes.Add(source);

        var (sink, model, catalog) = MakeSink(asset, graph);

        // SetVariable canonical pins: exec In, exec Out, data Value-In, data Value-Out.
        // DescriptorToEntry splits by direction (order-preserving):
        //   Inputs  = [exec In, data Value-In]   (indices 0,1)
        //   Outputs = [exec Out, data Value-Out] (indices 2,3)
        var (pinIds, entry) = PreGeneratePinIds(catalog, "SetVariable");
        Assert.Equal(2, entry.Inputs.Count);
        Assert.Equal(2, entry.Outputs.Count);

        // Source is a DATA-out → target is the new node's DATA-in. CanvasInput walks entry.Inputs
        // (inputs first) and binds the first compatible DATA input. The catalog entry is built from
        // a DEFAULT-constructed node, so its Value pin is typed System.Object; what matters for the
        // PinId binding is the POSITION (the Value data-IN is the 2nd input, after the exec-IN), so
        // we select by Data-kind to locate that index.
        int targetIdx = -1;
        for (int i = 0; i < entry.Inputs.Count; i++)
        {
            if (entry.Inputs[i].Kind == PinKind.Data)
            {
                targetIdx = i; // index within the inputs-first PinId list
                break;
            }
        }
        Assert.Equal(1, targetIdx); // the Value data-IN, after the exec-IN
        var targetPinId = pinIds[targetIdx];

        var newNodeId = new NodeId(Guid.NewGuid());
        var addNode = new GraphCommand.AddNode(
            newNodeId,
            new NodeKindKey("SetVariable"),
            new Vector2(400, 220),
            new Dictionary<string, object?>
            {
                ["PinIds"]     = pinIds,
                ["VariableId"] = varId.ToString(),
            });

        // source data-out → new node Value data-in.
        var addLink = new GraphCommand.AddLink(
            new LinkId(Guid.NewGuid()),
            new PinId(sourceDataOutId),
            targetPinId);

        var result = sink.Apply(new GraphCommand.Batch(
            "Add Node", new GraphCommand[] { addNode, addLink }));

        Assert.True(result.Success, result.Message);

        var newAssetNode = graph.Nodes.OfType<SetVariableNode>().Single();
        Assert.Empty(graph.Nodes.OfType<FunctionCallNode>());
        var newNodeModelId = new NodeId(newAssetNode.Id);

        // ── REAL connection assertions ──────────────────────────────────────
        var assetLink = Assert.Single(graph.Links);
        Assert.Equal(sourceDataOutId,    assetLink.FromPinId);
        Assert.Equal(targetPinId.Value,  assetLink.ToPinId);
        Assert.Equal(newAssetNode.Id,    assetLink.ToNodeId);

        var fromPin = model.FindPin(new PinId(sourceDataOutId));
        var toPin   = model.FindPin(targetPinId);
        Assert.NotNull(fromPin);
        Assert.NotNull(toPin);

        // The resolved target pin is owned by the new node and is its Value DATA-IN pin.
        Assert.Equal(newNodeModelId,      toPin!.OwnerNodeId);
        Assert.Equal(PinKind.Data,        toPin.Kind);
        Assert.Equal(PinDirection.Input,  toPin.Direction);
        Assert.Equal("Value",             toPin.Label);
        Assert.Equal("System.Int32",      toPin.Type!.Value.Id);

        var linkId = BlueprintGraphModel.MakeLinkId(sourceDataOutId, targetPinId.Value);
        var link   = model.FindLink(linkId);
        Assert.NotNull(link);
        Assert.Equal(new PinId(sourceDataOutId), link!.FromPin);
        Assert.Equal(targetPinId,                link.ToPin);
    }

    // ── Regression guard: without PinIds the wire-drop link is REJECTED ──────────

    /// <summary>
    /// Control test that pins the root cause: if the canvas did NOT pass PinIds, the new node's
    /// pins would carry fresh (synthesized) GUIDs that differ from the link's target GUID, so the
    /// AddLink would fail to resolve the pin and the batch would be rejected. This proves the
    /// PinIds payload is load-bearing for auto-connect (and that the fix is what makes it pass).
    /// </summary>
    [Fact]
    public void WireDrop_WithoutPinIds_LinkToFreshPin_IsRejected()
    {
        var (asset, graph) = MakeAssetWithGraph();

        var source = new EventEntryNode { Id = Guid.NewGuid(), EventTypeId = "OnStart" };
        var sourceOutPinId = Guid.NewGuid();
        source.Pins.Add(new Pin
        {
            Id = sourceOutPinId, Name = "Out", Direction = "Out", IsExec = true,
            TypeRef = new BlueprintTypeRef(),
        });
        graph.Nodes.Add(source);

        var (sink, model, _) = MakeSink(asset, graph);

        var newNodeId = new NodeId(Guid.NewGuid());
        // No PinIds in the props → the new node's exec-in pin gets a synthesized GUID.
        var addNode = new GraphCommand.AddNode(
            newNodeId, new NodeKindKey("ChannelCommand"), new Vector2(380, 220), null);

        // A link to a GUID the new node will NOT own → must fail to resolve.
        var phantomPinId = new PinId(Guid.NewGuid());
        var addLink = new GraphCommand.AddLink(
            new LinkId(Guid.NewGuid()), new PinId(sourceOutPinId), phantomPinId);

        var result = sink.Apply(new GraphCommand.Batch(
            "Add Node", new GraphCommand[] { addNode, addLink }));

        Assert.False(result.Success);
        // The node was added (batch does not roll back), but NO link connected.
        Assert.Single(graph.Nodes.OfType<ChannelCommandNode>());
        Assert.Empty(graph.Links);
        Assert.Null(model.FindPin(phantomPinId));
    }
}
