using System.Numerics;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Editor.GraphEditor;
using Hrot.Blueprints.Editor.Host;
using Hrot.Blueprints.Editor.NodeDrawers;
using Hrot.Blueprints.Tests.Builders;
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Xunit;

// ChangeParentMove is defined in NodeEditor.Core.Commands, same namespace as GraphCommand.

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

    // ── AddLink: CA-07c wire-bake (TryBakeCollectionConsumer) ─────────────────

    private const string CollectionComponentFqn = "Hrot.AI.Behaviors.BpCollectionDemo";
    private const string CollectionCountFqn     = "Hrot.AI.Behaviors.Brains.BpCollectionDemoOps.Count";
    private const string CollectionItemFqn      = "Hrot.AI.Behaviors.Brains.BpCollectionDemoOps.Item";

    /// <summary>
    /// Builds a fully pin-authored <c>GetComponent&lt;BpCollectionDemo&gt;</c> node with a single
    /// baked collection decl ("Values", element System.Int32) -- mirrors
    /// <c>ComponentCollectionConsumerLoweringTests.BuildGetComponentCollectionNode</c>.
    /// </summary>
    private static (GetComponentNode Node, Pin ValuesOut) AddGetComponentCollectionNode(Graph graph)
    {
        var valuesOut = new Pin { Id = Guid.NewGuid(), Name = "Values", Direction = "Out", IsExec = false,
            TypeRef = new BlueprintTypeRef { TypeId = "System.Int32", IsArray = true } };
        var foundOut = new Pin { Id = Guid.NewGuid(), Name = "Found", Direction = "Out", IsExec = false,
            TypeRef = new BlueprintTypeRef { TypeId = "System.Boolean" } };
        var node = new GetComponentNode
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = CollectionComponentFqn,
            Fields = new List<ComponentFieldDecl>
            {
                new()
                {
                    Name             = "Values",
                    IsCollection     = true,
                    ElementTypeId    = "System.Int32",
                    CountAccessorFqn = CollectionCountFqn,
                    ItemAccessorFqn  = CollectionItemFqn,
                },
            },
        };
        node.Pins.AddRange(new[] { valuesOut, foundOut });
        graph.Nodes.Add(node);
        return (node, valuesOut);
    }

    [Fact]
    public void CommandSink_AddLink_GetComponentCollectionIntoComponentForEach_BakesAllFourProps()
    {
        var (asset, graph) = MakeAssetWithGraph();
        var (_, valuesOut) = AddGetComponentCollectionNode(graph);

        var collectionIn = new Pin { Id = Guid.NewGuid(), Name = "Collection", Direction = "In", IsExec = false,
            TypeRef = new BlueprintTypeRef { TypeId = "System.Object", IsArray = true } };
        var forEach = new ComponentForEachNode { Id = Guid.NewGuid() };
        forEach.Pins.Add(collectionIn);
        graph.Nodes.Add(forEach);

        var (sink, _, _, _, _, _) = MakeSut(asset, graph);

        var result = sink.Apply(new GraphCommand.AddLink(
            new LinkId(Guid.NewGuid()), new PinId(valuesOut.Id), new PinId(collectionIn.Id)));

        Assert.True(result.Success);
        var baked = (ComponentForEachNode)graph.Nodes.Single(n => n.Id == forEach.Id);
        Assert.Equal(CollectionComponentFqn, baked.ComponentTypeFqn);
        Assert.Equal(CollectionCountFqn,     baked.CountAccessorFqn);
        Assert.Equal(CollectionItemFqn,      baked.ItemAccessorFqn);
        Assert.Equal("System.Int32",         baked.ElementTypeFqn);
    }

    [Fact]
    public void CommandSink_AddLink_GetComponentCollectionIntoComponentItemGet_BakesThreeProps_NoCountAccessor()
    {
        var (asset, graph) = MakeAssetWithGraph();
        var (_, valuesOut) = AddGetComponentCollectionNode(graph);

        var collectionIn = new Pin { Id = Guid.NewGuid(), Name = "Collection", Direction = "In", IsExec = false,
            TypeRef = new BlueprintTypeRef { TypeId = "System.Object", IsArray = true } };
        var itemGet = new ComponentItemGetNode { Id = Guid.NewGuid() };
        itemGet.Pins.Add(collectionIn);
        graph.Nodes.Add(itemGet);

        var (sink, _, _, _, _, _) = MakeSut(asset, graph);

        var result = sink.Apply(new GraphCommand.AddLink(
            new LinkId(Guid.NewGuid()), new PinId(valuesOut.Id), new PinId(collectionIn.Id)));

        Assert.True(result.Success);
        var baked = (ComponentItemGetNode)graph.Nodes.Single(n => n.Id == itemGet.Id);
        Assert.Equal(CollectionComponentFqn, baked.ComponentTypeFqn);
        Assert.Equal(CollectionItemFqn,      baked.ItemAccessorFqn);
        Assert.Equal("System.Int32",         baked.ElementTypeFqn);
    }

    [Fact]
    public void CommandSink_AddLink_GetComponentCollectionIntoComponentItemCount_BakesTwoProps_NoItemAccessorOrElementType()
    {
        var (asset, graph) = MakeAssetWithGraph();
        var (_, valuesOut) = AddGetComponentCollectionNode(graph);

        var collectionIn = new Pin { Id = Guid.NewGuid(), Name = "Collection", Direction = "In", IsExec = false,
            TypeRef = new BlueprintTypeRef { TypeId = "System.Object", IsArray = true } };
        var itemCount = new ComponentItemCountNode { Id = Guid.NewGuid() };
        itemCount.Pins.Add(collectionIn);
        graph.Nodes.Add(itemCount);

        var (sink, _, _, _, _, _) = MakeSut(asset, graph);

        var result = sink.Apply(new GraphCommand.AddLink(
            new LinkId(Guid.NewGuid()), new PinId(valuesOut.Id), new PinId(collectionIn.Id)));

        Assert.True(result.Success);
        var baked = (ComponentItemCountNode)graph.Nodes.Single(n => n.Id == itemCount.Id);
        Assert.Equal(CollectionComponentFqn, baked.ComponentTypeFqn);
        Assert.Equal(CollectionCountFqn,     baked.CountAccessorFqn);
    }

    [Fact]
    public void CommandSink_AddLink_GetComponentCollectionIntoComponentContains_BakesAllFourProps()
    {
        // CA-07d-1: Contains is a SEARCH node (loop + compare), so it bakes all four props like
        // ForEach, not the two/three-prop subset ItemCount/ItemGet get.
        var (asset, graph) = MakeAssetWithGraph();
        var (_, valuesOut) = AddGetComponentCollectionNode(graph);

        var collectionIn = new Pin { Id = Guid.NewGuid(), Name = "Collection", Direction = "In", IsExec = false,
            TypeRef = new BlueprintTypeRef { TypeId = "System.Object", IsArray = true } };
        var contains = new ComponentContainsNode { Id = Guid.NewGuid() };
        contains.Pins.Add(collectionIn);
        graph.Nodes.Add(contains);

        var (sink, _, _, _, _, _) = MakeSut(asset, graph);

        var result = sink.Apply(new GraphCommand.AddLink(
            new LinkId(Guid.NewGuid()), new PinId(valuesOut.Id), new PinId(collectionIn.Id)));

        Assert.True(result.Success);
        var baked = (ComponentContainsNode)graph.Nodes.Single(n => n.Id == contains.Id);
        Assert.Equal(CollectionComponentFqn, baked.ComponentTypeFqn);
        Assert.Equal(CollectionCountFqn,     baked.CountAccessorFqn);
        Assert.Equal(CollectionItemFqn,      baked.ItemAccessorFqn);
        Assert.Equal("System.Int32",         baked.ElementTypeFqn);
    }

    [Fact]
    public void CommandSink_AddLink_GetComponentCollectionIntoComponentFind_BakesAllFourProps()
    {
        // CA-07d-1: Find is a SEARCH node (loop + compare), so it bakes all four props like ForEach.
        var (asset, graph) = MakeAssetWithGraph();
        var (_, valuesOut) = AddGetComponentCollectionNode(graph);

        var collectionIn = new Pin { Id = Guid.NewGuid(), Name = "Collection", Direction = "In", IsExec = false,
            TypeRef = new BlueprintTypeRef { TypeId = "System.Object", IsArray = true } };
        var find = new ComponentFindNode { Id = Guid.NewGuid() };
        find.Pins.Add(collectionIn);
        graph.Nodes.Add(find);

        var (sink, _, _, _, _, _) = MakeSut(asset, graph);

        var result = sink.Apply(new GraphCommand.AddLink(
            new LinkId(Guid.NewGuid()), new PinId(valuesOut.Id), new PinId(collectionIn.Id)));

        Assert.True(result.Success);
        var baked = (ComponentFindNode)graph.Nodes.Single(n => n.Id == find.Id);
        Assert.Equal(CollectionComponentFqn, baked.ComponentTypeFqn);
        Assert.Equal(CollectionCountFqn,     baked.CountAccessorFqn);
        Assert.Equal(CollectionItemFqn,      baked.ItemAccessorFqn);
        Assert.Equal("System.Int32",         baked.ElementTypeFqn);
    }

    // ── AddLink: FC-1 (Q#20) collection-WRITE wire-bake + the two writability gates ──

    private const string WritableComponentFqn = "Hrot.AI.Behaviors.BpFixedListDemo";
    private const string WritableOpsFqn       = "Hrot.AI.Behaviors.Brains.BpFixedListDemoOps";

    /// <summary>GetComponent producer over the FC-0 `[BlueprintWritable]` + `[InlineArray]` demo ("Items" collection).</summary>
    private static (GetComponentNode Node, Pin ItemsOut) AddWritableCollectionSource(Graph graph)
    {
        var itemsOut = new Pin { Id = Guid.NewGuid(), Name = "Items", Direction = "Out", IsExec = false,
            TypeRef = new BlueprintTypeRef { TypeId = "System.Int32", IsArray = true } };
        var node = new GetComponentNode
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = WritableComponentFqn,
            Fields = new List<ComponentFieldDecl>
            {
                new()
                {
                    Name             = "Items",
                    IsCollection     = true,
                    ElementTypeId    = "System.Int32",
                    CountAccessorFqn = WritableOpsFqn + ".Count",
                    ItemAccessorFqn  = WritableOpsFqn + ".Item",
                },
            },
        };
        node.Pins.Add(itemsOut);
        graph.Nodes.Add(node);
        return (node, itemsOut);
    }

    private static (CollectionWriteNode Node, Pin CollectionIn) AddCollectionWriteNode(
        Graph graph, CollectionWriteOp op)
    {
        var collectionIn = new Pin { Id = Guid.NewGuid(), Name = "Collection", Direction = "In", IsExec = false,
            TypeRef = new BlueprintTypeRef { TypeId = "System.Object", IsArray = true } };
        var node = new CollectionWriteNode { Id = Guid.NewGuid(), Op = op };
        node.Pins.Add(collectionIn);
        graph.Nodes.Add(node);
        return (node, collectionIn);
    }

    [Fact]
    public void CommandSink_AddLink_WritableCollectionIntoCollectionWrite_BakesOpAccessor()
    {
        var (asset, graph) = MakeAssetWithGraph();
        var (_, itemsOut)  = AddWritableCollectionSource(graph);
        var (write, collectionIn) = AddCollectionWriteNode(graph, CollectionWriteOp.SetAt);

        var (sink, _, _, _, _, _) = MakeSut(asset, graph);
        var result = sink.Apply(new GraphCommand.AddLink(
            new LinkId(Guid.NewGuid()), new PinId(itemsOut.Id), new PinId(collectionIn.Id)));

        Assert.True(result.Success);
        Assert.Equal(WritableComponentFqn,        write.ComponentTypeFqn);
        Assert.Equal(WritableOpsFqn + ".SetAt",   write.WriteAccessorFqn);
        Assert.Equal("System.Int32",              write.ElementTypeFqn);
        Assert.Equal(CollectionKind.CuratedStatic, write.CollectionKind);
    }

    [Fact]
    public void CommandSink_AddLink_NonWritableComponentIntoCollectionWrite_RefusesBake()
    {
        // Gate 1 (Q#20-A): BpCollectionDemo SHIPS write accessors (the FC-0 fixed-buffer reference
        // set) but is deliberately NOT [BlueprintWritable] -- the wire must land, the bake must not.
        var (asset, graph) = MakeAssetWithGraph();
        var (_, valuesOut) = AddGetComponentCollectionNode(graph);   // BpCollectionDemo producer
        var (write, collectionIn) = AddCollectionWriteNode(graph, CollectionWriteOp.Add);

        var (sink, _, _, _, _, _) = MakeSut(asset, graph);
        var result = sink.Apply(new GraphCommand.AddLink(
            new LinkId(Guid.NewGuid()), new PinId(valuesOut.Id), new PinId(collectionIn.Id)));

        Assert.True(result.Success);                       // the LINK itself is legal
        Assert.Equal("", write.ComponentTypeFqn);          // ... but nothing baked
        Assert.Equal("", write.WriteAccessorFqn);
    }

    [Fact]
    public void CommandSink_AddLink_ManagedMemberCollectionIntoCollectionWrite_RefusesBake()
    {
        // Gate 0 (Q#20-C): a ManagedMember decl is never element-writable -- refuse before either
        // writability gate is even consulted.
        var (asset, graph) = MakeAssetWithGraph();
        var membersOut = new Pin { Id = Guid.NewGuid(), Name = "MemberIds", Direction = "Out", IsExec = false,
            TypeRef = new BlueprintTypeRef { TypeId = "System.Int32", IsArray = true } };
        var producer = new GetComponentNode
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = "Hrot.AI.Behaviors.BpManagedCollectionDemo",
            IsManaged        = true,
            Fields = new List<ComponentFieldDecl>
            {
                new()
                {
                    Name                = "MemberIds",
                    IsCollection        = true,
                    ElementTypeId       = "System.Int32",
                    CollectionKind      = CollectionKind.ManagedMember,
                    CollectionFieldName = "MemberIds",
                },
            },
        };
        producer.Pins.Add(membersOut);
        graph.Nodes.Add(producer);
        var (write, collectionIn) = AddCollectionWriteNode(graph, CollectionWriteOp.Add);

        var (sink, _, _, _, _, _) = MakeSut(asset, graph);
        var result = sink.Apply(new GraphCommand.AddLink(
            new LinkId(Guid.NewGuid()), new PinId(membersOut.Id), new PinId(collectionIn.Id)));

        Assert.True(result.Success);
        Assert.Equal("", write.ComponentTypeFqn);
        Assert.Equal("", write.WriteAccessorFqn);
        Assert.Equal(CollectionKind.CuratedStatic, write.CollectionKind);   // untouched default
    }

    // ── AddLink: FC-2/LV-2 list-variable wire-bake (TryBakeCollectionConsumer) ────

    [Fact]
    public void CommandSink_AddLink_ListVariableIntoForEach_BakesKindAndVariableName()
    {
        var (asset, graph) = MakeAssetWithGraph();
        asset.Variables.Add(new VariableDecl
        {
            Id = Guid.NewGuid(), Name = "MyList",
            Type = new BlueprintTypeRef { TypeId = "System.Int32", Capacity = 4 },
        });

        var gvOut = new Pin { Id = Guid.NewGuid(), Name = "Value", Direction = "Out", IsExec = false,
            TypeRef = new BlueprintTypeRef { TypeId = "System.Int32", IsArray = true } };
        var gv = new GetVariableNode { Id = Guid.NewGuid(), VariableId = asset.Variables[0].Id.ToString() };
        gv.Pins.Add(gvOut);
        graph.Nodes.Add(gv);

        var collectionIn = new Pin { Id = Guid.NewGuid(), Name = "Collection", Direction = "In", IsExec = false,
            TypeRef = new BlueprintTypeRef { TypeId = "System.Object", IsArray = true } };
        var forEach = new ComponentForEachNode { Id = Guid.NewGuid() };
        forEach.Pins.Add(collectionIn);
        graph.Nodes.Add(forEach);

        var (sink, _, _, _, _, _) = MakeSut(asset, graph);
        var result = sink.Apply(new GraphCommand.AddLink(
            new LinkId(Guid.NewGuid()), new PinId(gvOut.Id), new PinId(collectionIn.Id)));

        Assert.True(result.Success);
        Assert.Equal(CollectionKind.BlackboardFixedList, forEach.CollectionKind);
        Assert.Equal("MyList",       forEach.CollectionFieldName);
        Assert.Equal("System.Int32", forEach.ElementTypeFqn);
        Assert.Equal("",             forEach.ComponentTypeFqn);   // no entity/component for a list source
        Assert.Equal("",             forEach.CountAccessorFqn);
        Assert.Equal("",             forEach.ItemAccessorFqn);
    }

    [Fact]
    public void CommandSink_AddLink_ScalarVariableIntoForEach_DoesNotBake()
    {
        var (asset, graph) = MakeAssetWithGraph();
        asset.Variables.Add(new VariableDecl
        {
            Id = Guid.NewGuid(), Name = "Scalar",
            Type = new BlueprintTypeRef { TypeId = "System.Int32" },   // Capacity 0 => not a list
        });

        var gvOut = new Pin { Id = Guid.NewGuid(), Name = "Value", Direction = "Out", IsExec = false,
            TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" } };
        var gv = new GetVariableNode { Id = Guid.NewGuid(), VariableId = asset.Variables[0].Id.ToString() };
        gv.Pins.Add(gvOut);
        graph.Nodes.Add(gv);

        var collectionIn = new Pin { Id = Guid.NewGuid(), Name = "Collection", Direction = "In", IsExec = false,
            TypeRef = new BlueprintTypeRef { TypeId = "System.Object", IsArray = true } };
        var forEach = new ComponentForEachNode { Id = Guid.NewGuid() };
        forEach.Pins.Add(collectionIn);
        graph.Nodes.Add(forEach);

        var (sink, _, _, _, _, _) = MakeSut(asset, graph);
        sink.Apply(new GraphCommand.AddLink(
            new LinkId(Guid.NewGuid()), new PinId(gvOut.Id), new PinId(collectionIn.Id)));

        Assert.Equal(CollectionKind.CuratedStatic, forEach.CollectionKind);   // untouched
        Assert.Null(forEach.CollectionFieldName);
    }

    // ── AddLink: CA-07d-2 managed-member wire-bake (TryBakeCollectionConsumer) ────

    private const string ManagedCollectionComponentFqn = "Hrot.AI.Behaviors.BpManagedCollectionDemo";
    private const string ManagedCollectionFieldName     = "MemberIds";

    /// <summary>
    /// Builds a fully pin-authored MANAGED <c>GetComponent&lt;BpManagedCollectionDemo&gt;</c> node
    /// with a single baked MANAGED collection decl ("MemberIds", element System.Int32, empty
    /// accessor FQNs) -- the managed counterpart of <see cref="AddGetComponentCollectionNode"/>.
    /// </summary>
    private static (GetComponentNode Node, Pin MemberIdsOut) AddGetComponentManagedCollectionNode(Graph graph)
    {
        var memberIdsOut = new Pin { Id = Guid.NewGuid(), Name = "MemberIds", Direction = "Out", IsExec = false,
            TypeRef = new BlueprintTypeRef { TypeId = "System.Int32", IsArray = true } };
        var node = new GetComponentNode
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = ManagedCollectionComponentFqn,
            IsManaged        = true,
            Fields = new List<ComponentFieldDecl>
            {
                new()
                {
                    Name                = ManagedCollectionFieldName,
                    IsCollection        = true,
                    ElementTypeId       = "System.Int32",
                    CountAccessorFqn    = "",
                    ItemAccessorFqn     = "",
                    CollectionKind      = CollectionKind.ManagedMember,
                    CollectionFieldName = ManagedCollectionFieldName,
                },
            },
        };
        node.Pins.Add(memberIdsOut);
        graph.Nodes.Add(node);
        return (node, memberIdsOut);
    }

    [Fact]
    public void CommandSink_AddLink_GetComponentManagedCollectionIntoComponentContains_BakesKindAndFieldName_AccessorFqnsEmpty()
    {
        var (asset, graph) = MakeAssetWithGraph();
        var (_, memberIdsOut) = AddGetComponentManagedCollectionNode(graph);

        var collectionIn = new Pin { Id = Guid.NewGuid(), Name = "Collection", Direction = "In", IsExec = false,
            TypeRef = new BlueprintTypeRef { TypeId = "System.Object", IsArray = true } };
        var contains = new ComponentContainsNode { Id = Guid.NewGuid() };
        contains.Pins.Add(collectionIn);
        graph.Nodes.Add(contains);

        var (sink, _, _, _, _, _) = MakeSut(asset, graph);

        var result = sink.Apply(new GraphCommand.AddLink(
            new LinkId(Guid.NewGuid()), new PinId(memberIdsOut.Id), new PinId(collectionIn.Id)));

        Assert.True(result.Success);
        var baked = (ComponentContainsNode)graph.Nodes.Single(n => n.Id == contains.Id);
        Assert.Equal(ManagedCollectionComponentFqn, baked.ComponentTypeFqn);
        Assert.Equal(CollectionKind.ManagedMember,  baked.CollectionKind);
        Assert.Equal(ManagedCollectionFieldName,    baked.CollectionFieldName);
        Assert.Equal("System.Int32",                baked.ElementTypeFqn);
        Assert.Equal("",                            baked.CountAccessorFqn);
        Assert.Equal("",                            baked.ItemAccessorFqn);
    }

    [Fact]
    public void CommandSink_AddLink_GetComponentManagedCollectionIntoComponentItemCount_BakesElementTypeFqn_ManagedOnly()
    {
        // ComponentItemCountNode's ElementTypeFqn is baked ONLY for the managed case (the compiler
        // needs it to type the native IReadOnlyList<TElement> local) -- curated Count never sets it
        // (see CommandSink_AddLink_GetComponentCollectionIntoComponentItemCount_BakesTwoProps_NoItemAccessorOrElementType).
        var (asset, graph) = MakeAssetWithGraph();
        var (_, memberIdsOut) = AddGetComponentManagedCollectionNode(graph);

        var collectionIn = new Pin { Id = Guid.NewGuid(), Name = "Collection", Direction = "In", IsExec = false,
            TypeRef = new BlueprintTypeRef { TypeId = "System.Object", IsArray = true } };
        var itemCount = new ComponentItemCountNode { Id = Guid.NewGuid() };
        itemCount.Pins.Add(collectionIn);
        graph.Nodes.Add(itemCount);

        var (sink, _, _, _, _, _) = MakeSut(asset, graph);

        var result = sink.Apply(new GraphCommand.AddLink(
            new LinkId(Guid.NewGuid()), new PinId(memberIdsOut.Id), new PinId(collectionIn.Id)));

        Assert.True(result.Success);
        var baked = (ComponentItemCountNode)graph.Nodes.Single(n => n.Id == itemCount.Id);
        Assert.Equal(ManagedCollectionComponentFqn, baked.ComponentTypeFqn);
        Assert.Equal(CollectionKind.ManagedMember,  baked.CollectionKind);
        Assert.Equal(ManagedCollectionFieldName,    baked.CollectionFieldName);
        Assert.Equal("System.Int32",                baked.ElementTypeFqn);
        Assert.Equal("",                            baked.CountAccessorFqn);
    }

    [Fact]
    public void CommandSink_AddLink_NonGetComponentSourceIntoCollectionPin_DoesNotBake_LinkStillAdded()
    {
        var (asset, graph) = MakeAssetWithGraph();

        // A plain FunctionCall array output -- NOT a GetComponent collection pin.
        var srcOut = new Pin { Id = Guid.NewGuid(), Name = "Result", Direction = "Out", IsExec = false,
            TypeRef = new BlueprintTypeRef { TypeId = "System.Int32", IsArray = true } };
        var srcNode = new FunctionCallNode { Id = Guid.NewGuid(), MethodName = "Src" };
        srcNode.Pins.Add(srcOut);
        graph.Nodes.Add(srcNode);

        var collectionIn = new Pin { Id = Guid.NewGuid(), Name = "Collection", Direction = "In", IsExec = false,
            TypeRef = new BlueprintTypeRef { TypeId = "System.Object", IsArray = true } };
        var itemCount = new ComponentItemCountNode { Id = Guid.NewGuid() };
        itemCount.Pins.Add(collectionIn);
        graph.Nodes.Add(itemCount);

        var (sink, _, _, _, _, _) = MakeSut(asset, graph);

        var result = sink.Apply(new GraphCommand.AddLink(
            new LinkId(Guid.NewGuid()), new PinId(srcOut.Id), new PinId(collectionIn.Id)));

        // Detection fails (source isn't GetComponentNode) -- the wire still goes through untouched
        // (validator/BP2066 handle the semantic mismatch, not this hook), but nothing gets baked.
        Assert.True(result.Success);
        Assert.Single(graph.Links);
        var untouched = (ComponentItemCountNode)graph.Nodes.Single(n => n.Id == itemCount.Id);
        Assert.Equal("", untouched.ComponentTypeFqn);
        Assert.Equal("", untouched.CountAccessorFqn);
    }

    [Fact]
    public void CommandSink_AddLink_GetComponentScalarFieldIntoCollectionPin_IsRejected_ArrayArity()
    {
        // A GetComponent SCALAR field (Int32, not an array) wired into a "Collection" (array) data-IN
        // pin is now REJECTED by the array-arity rule in BlueprintLinkValidator (a scalar cannot feed
        // a collection pin) — the wire never lands, so the consumer can't be left wired-but-unbaked
        // (which previously produced a hard BP2066 at compile). CA-07c robustness.
        var (asset, graph) = MakeAssetWithGraph();

        // A GetComponent node with only a SCALAR field (no collection decl at all).
        var healthOut = new Pin { Id = Guid.NewGuid(), Name = "Health", Direction = "Out", IsExec = false,
            TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" } };
        var gcn = new GetComponentNode
        {
            Id = Guid.NewGuid(),
            ComponentTypeFqn = "Hrot.AI.Behaviors.BpComponentDemo",
            Fields = new List<ComponentFieldDecl> { new() { Name = "Health", TypeId = "System.Int32" } },
        };
        gcn.Pins.Add(healthOut);
        graph.Nodes.Add(gcn);

        var collectionIn = new Pin { Id = Guid.NewGuid(), Name = "Collection", Direction = "In", IsExec = false,
            TypeRef = new BlueprintTypeRef { TypeId = "System.Object", IsArray = true } };
        var itemCount = new ComponentItemCountNode { Id = Guid.NewGuid() };
        itemCount.Pins.Add(collectionIn);
        graph.Nodes.Add(itemCount);

        var (sink, _, _, _, _, _) = MakeSut(asset, graph);

        var result = sink.Apply(new GraphCommand.AddLink(
            new LinkId(Guid.NewGuid()), new PinId(healthOut.Id), new PinId(collectionIn.Id)));

        // Rejected (scalar → array), no link added, node left untouched.
        Assert.False(result.Success);
        Assert.Empty(graph.Links);
        var untouched = (ComponentItemCountNode)graph.Nodes.Single(n => n.Id == itemCount.Id);
        Assert.Equal("", untouched.ComponentTypeFqn);
    }

    [Fact]
    public void CommandSink_AddLink_IntoOrdinaryDataPinNamedCollection_OnNonConsumerNode_DoesNotThrow()
    {
        // Guards the detection switch: a pin coincidentally named "Collection" on some OTHER node
        // kind must not crash the bake hook (only ComponentForEach/ItemGet/ItemCount are handled).
        var (asset, graph) = MakeAssetWithGraph();
        var (_, valuesOut) = AddGetComponentCollectionNode(graph);

        var collectionIn = new Pin { Id = Guid.NewGuid(), Name = "Collection", Direction = "In", IsExec = false,
            TypeRef = new BlueprintTypeRef { TypeId = "System.Object", IsArray = true } };
        var otherNode = new FunctionCallNode { Id = Guid.NewGuid(), MethodName = "NotAConsumer" };
        otherNode.Pins.Add(collectionIn);
        graph.Nodes.Add(otherNode);

        var (sink, _, _, _, _, _) = MakeSut(asset, graph);

        var result = sink.Apply(new GraphCommand.AddLink(
            new LinkId(Guid.NewGuid()), new PinId(valuesOut.Id), new PinId(collectionIn.Id)));

        Assert.True(result.Success);
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

    // ── ChangeParentMultiple (BCP-BATCH-01-FIX BUG 1) ────────────────────────

    /// <summary>
    /// ChangeParentMultiple (the command the canvas issues for every node drop, BPF-029)
    /// must persist NewLocalPosition to the asset so the node does not jump back.
    /// </summary>
    [Fact]
    public void CommandSink_ChangeParentMultiple_PersistsPosition()
    {
        var (asset, graph) = MakeAssetWithGraph();
        var node = new FunctionCallNode { Id = Guid.NewGuid(),
            EditorMetadata = new NodeMetadata { X = 0f, Y = 0f } };
        graph.Nodes.Add(node);
        var (sink, model, _, _, _, _) = MakeSut(asset, graph);

        var newPos = new Vector2(99f, 111f);
        var result = sink.Apply(new GraphCommand.ChangeParentMultiple(
            new[] { new ChangeParentMove(new NodeId(node.Id), null, null, newPos) }));

        Assert.True(result.Success);
        // Asset metadata updated.
        Assert.Equal(99f,  node.EditorMetadata.X, precision: 2);
        Assert.Equal(111f, node.EditorMetadata.Y, precision: 2);
        // Model reflects the new position (reads live from metadata).
        var modelNode = model.FindNode(new NodeId(node.Id));
        Assert.NotNull(modelNode);
        Assert.Equal(99f,  modelNode!.Position.X, precision: 2);
        Assert.Equal(111f, modelNode.Position.Y,  precision: 2);
    }

    /// <summary>
    /// ChangeParentMultiple fires NodesMoved (not Wholesale) — no full rebuild.
    /// </summary>
    [Fact]
    public void CommandSink_ChangeParentMultiple_FiresNodesMoved_NotWholesale()
    {
        var (asset, graph) = MakeAssetWithGraph();
        var node = new FunctionCallNode { Id = Guid.NewGuid(),
            EditorMetadata = new NodeMetadata { X = 0f, Y = 0f } };
        graph.Nodes.Add(node);
        var (sink, model, _, _, _, _) = MakeSut(asset, graph);

        int wholesaleCount  = 0;
        int nodesMovedCount = 0;
        model.Changed += n =>
        {
            if (n.Kind == GraphChangeKind.Wholesale)  wholesaleCount++;
            if (n.Kind == GraphChangeKind.NodesMoved) nodesMovedCount++;
        };

        sink.Apply(new GraphCommand.ChangeParentMultiple(
            new[] { new ChangeParentMove(new NodeId(node.Id), null, null, new Vector2(50f, 75f)) }));

        Assert.Equal(0, wholesaleCount);
        Assert.Equal(1, nodesMovedCount);
    }

    /// <summary>
    /// After ChangeParentMultiple, FindNode returns the same INodeModel instance
    /// (no rebuild replaced it) and its Position matches NewLocalPosition.
    /// </summary>
    [Fact]
    public void CommandSink_ChangeParentMultiple_SameInstanceIdentityPreserved()
    {
        var (asset, graph) = MakeAssetWithGraph();
        var node = new FunctionCallNode { Id = Guid.NewGuid(),
            EditorMetadata = new NodeMetadata { X = 0f, Y = 0f } };
        graph.Nodes.Add(node);
        var (sink, model, _, _, _, _) = MakeSut(asset, graph);

        var instanceBefore = model.FindNode(new NodeId(node.Id));
        Assert.NotNull(instanceBefore);

        sink.Apply(new GraphCommand.ChangeParentMultiple(
            new[] { new ChangeParentMove(new NodeId(node.Id), null, null, new Vector2(22f, 33f)) }));

        var instanceAfter = model.FindNode(new NodeId(node.Id));
        Assert.Same(instanceBefore, instanceAfter);
        Assert.Equal(22f, instanceAfter!.Position.X, precision: 2);
        Assert.Equal(33f, instanceAfter.Position.Y,  precision: 2);
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

    // ── BF-UX1 FIX B: ChannelCommandNode pin preservation (channel catalog) ────

    /// <summary>
    /// Helper: builds a sink with a full palette registry + channel catalog so
    /// ChannelCommandNode resolves its param data-IN pins via NodePinSchema.
    /// </summary>
    private static (BlueprintCommandSink sink,
                    BlueprintGraphModel  model,
                    List<BlueprintAsset> dirtyLog)
        MakeSutWithChannelCatalog(BlueprintAsset? asset = null, Graph? graph = null)
    {
        if (asset == null)
            (asset, graph) = MakeAssetWithGraph();
        else if (graph == null)
            throw new ArgumentNullException(nameof(graph));

        var typeSystem      = new BlueprintTypeSystem(NullPinDefaultValueEditorRegistry.Instance);
        var channelCatalog  = BuiltInChannelCommandCatalog.Instance;
        // AN4: pass catalog so per-action ChannelCommand entries are registered.
        var kindRegistry    = BlueprintEditorBootstrap.CreatePaletteRegistry(channelCatalog);
        var model           = new BlueprintGraphModel(asset, graph!, kindRegistry,
                                 channelCommands: channelCatalog);
        var catalog         = new BlueprintNodeCatalog(kindRegistry);
        var validator    = new BlueprintLinkValidator(model, typeSystem);
        var history      = new CommandHistory();
        var dirtyLog     = new List<BlueprintAsset>();
        var editService  = new EditService
        {
            Context = new EditServiceContext(history, a => dirtyLog.Add(a))
        };

        var sink = new BlueprintCommandSink(
            asset, graph!, model, catalog, validator, history, editService,
            markDirty:       a => dirtyLog.Add(a),
            channelCommands: channelCatalog);

        return (sink, model, dirtyLog);
    }

    /// <summary>
    /// BF-UX1 FIX B: A ChannelCommandNode created via the add-node path (ApplyAddNode →
    /// CreateAssetNode → ApplyPinIds) must retain its parameter data-IN pins, not collapse
    /// to exec-only.  This was broken because ApplyPinIds called GetCanonicalPins without
    /// the channel catalog.
    ///
    /// ChannelCommandPins matches by LastSegment(ChannelTypeFqn) == node.ChannelType, so
    /// we pass the short segment "LocomotionChannel" as the ChannelType.
    /// We also supply a PinIds list (required for ApplyPinIds to do any work) sized to the
    /// expected pin count (execIn + execOut + at least 1 data pin = 3+).
    /// </summary>
    [Fact]
    public void CommandSink_AddChannelCommandNode_RetainsParamPins_WithChannelCatalog()
    {
        var (asset, graph) = MakeAssetWithGraph();
        var (sink, model, _) = MakeSutWithChannelCatalog(asset, graph);

        // Supply enough PinIds for the canonical pins: at minimum execIn + execOut + data pins.
        // We provide 8 ids — more than needed; ApplyPinIds stamps min(supplied, canonical).
        var pinIds = Enumerable.Range(0, 8).Select(_ => new PinId(Guid.NewGuid())).ToList();

        // AN4: use the per-action kind id (ChannelType+ActionId baked by CreateInstance).
        // Props only carry PinIds (baking via CreateInstance; no need for ChannelType/ActionId props).
        var props = new Dictionary<string, object?>
        {
            ["PinIds"] = (IReadOnlyList<PinId>)pinIds,
        };

        var result = sink.Apply(new GraphCommand.AddNode(
            new NodeId(Guid.NewGuid()),
            new NodeKindKey("ChannelCommand:LocomotionChannel:MoveTo"),
            Vector2.Zero,
            props));

        Assert.True(result.Success);
        var node = graph.Nodes.Last() as ChannelCommandNode;
        Assert.NotNull(node);

        // AN4: ChannelType + ActionId are baked by CreateInstance.
        Assert.Equal("LocomotionChannel", node!.ChannelType);
        Assert.Equal("MoveTo",            node.ActionId);

        // ChannelCommandNode must have more than 2 exec-only pins; MoveTo
        // projects execIn, execOut + the MoveTo param data-IN pins (≥ 3 total).
        Assert.True(node.Pins.Count > 2,
            $"Expected >2 pins (exec + param data-IN) but got {node.Pins.Count}.");
    }

    /// <summary>
    /// BF-UX1 FIX B: After SetPinDefault + RebuildAndNotify, the model node for a
    /// ChannelCommandNode still exposes the param pins (not just exec-only).
    /// Regression guard: the bug caused RebuildAndNotify to short-circuit at
    /// NodePinSchema pass-0 with only the exec pins stamped by the collapsed ApplyPinIds.
    /// </summary>
    [Fact]
    public void CommandSink_ChannelCommandNode_AfterRebuild_RetainsParamPins()
    {
        var (asset, graph) = MakeAssetWithGraph();
        var (sink, model, _) = MakeSutWithChannelCatalog(asset, graph);

        // Populate ChannelType/ActionId so NodePinSchema can resolve the MoveTo param type.
        // ChannelCommandPins matches by LastSegment(ChannelTypeFqn) == node.ChannelType.
        var ccNode = new ChannelCommandNode
        {
            Id          = Guid.NewGuid(),
            ChannelType = "LocomotionChannel",
            ActionId    = "MoveTo",
        };
        graph.Nodes.Add(ccNode);
        model.RebuildAndNotify();

        var modelNode = model.FindNode(new NodeId(ccNode.Id));
        Assert.NotNull(modelNode);

        // The model node should expose more than 2 pins (exec-only would be exactly 2).
        Assert.True(modelNode!.Pins.Count > 2,
            $"Model node should expose param data-IN pins. Got {modelNode.Pins.Count}.");
    }
}
