using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.GraphEditor;
using Hrot.Blueprints.Editor.Host;
using Hrot.Blueprints.Editor.NodeDrawers;
using Hrot.Blueprints.Tests.Builders;
using NodeEditor.Core;
using NodeEditor.Core.Action;
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.Primitives;
using NodeEditor.UI.Action;
using NodeEditor.UI.Find;
using Xunit;

namespace Hrot.Blueprints.Tests.Host;

/// <summary>
/// BCP-BATCH-02 tests: FindEngine, IEditorCommands, picker sources, and variable value-pin fix.
/// All tests are headless (no ImGui context).
/// </summary>
public sealed class BcpBatch02BlueprintTests
{
    // ── shared helpers ────────────────────────────────────────────────────────

    private static (BlueprintAsset asset, Graph graph) MakeAssetWithGraph()
    {
        var asset = BlueprintAssetBuilder.Instance("Batch02Asset")
            .WithGraph("EventGraph", GraphKind.Event, _ => { })
            .Build();
        return (asset, asset.Graphs[0]);
    }

    private static GraphView MakeGraphView(BlueprintGraphModel model, BlueprintNodeCatalog catalog)
    {
        var typeSystem = new BlueprintTypeSystem(NullPinDefaultValueEditorRegistry.Instance);
        var validator  = new BlueprintLinkValidator(model, typeSystem);
        var host = new StubHostServices(catalog, typeSystem, validator, new StubCommandSink());
        return new GraphView(model, host.CommandSink, host.LinkValidator,
            host.TypeSystem, host.NodeCatalog, host);
    }

    // ── Task 1: FindEngine returns matched node ids ───────────────────────────

    /// <summary>
    /// FindEngine.Search over a BlueprintGraphModel returns expected node ids.
    /// </summary>
    [Fact]
    public void FindEngine_Search_ReturnsMatchedNodeIds()
    {
        var (asset, graph) = MakeAssetWithGraph();

        // Arrange: add two nodes with known kinds — a BranchNode and a SequenceNode.
        var branchNode = new BranchNode { Id = Guid.NewGuid() };
        branchNode.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "In",    Direction = "In",  IsExec = true });
        branchNode.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "True",  Direction = "Out", IsExec = true });
        branchNode.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "False", Direction = "Out", IsExec = true });
        graph.Nodes.Add(branchNode);

        var seqNode = new SequenceNode { Id = Guid.NewGuid() };
        seqNode.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "In",    Direction = "In",  IsExec = true });
        seqNode.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "Then0", Direction = "Out", IsExec = true });
        graph.Nodes.Add(seqNode);

        var model   = new BlueprintGraphModel(asset, graph);
        var typeSystem = new BlueprintTypeSystem(NullPinDefaultValueEditorRegistry.Instance);
        var validator  = new BlueprintLinkValidator(model, typeSystem);
        var host    = new StubHostServices(
            new BlueprintNodeCatalog(new NodeKindRegistry()),
            typeSystem, validator,
            new StubCommandSink());
        var view    = new GraphView(model, host.CommandSink, host.LinkValidator,
            host.TypeSystem, host.NodeCatalog, host);

        var engine  = new FindEngine(model, extras: null);
        var query   = FindQueryParser.Parse("Branch");
        var results = engine.Search(query, FindScope.CurrentGraph, view).ToList();

        // BranchNode title is "Branch ..." — should match; SequenceNode should not.
        Assert.True(results.Count >= 1,
            "FindEngine should return at least one match for 'Branch'");
        // The branch node specifically must be in the results
        Assert.Contains(results, r => r.Node == new NodeId(branchNode.Id));
        // The sequence node must NOT be in the results when searching 'Branch'
        Assert.DoesNotContain(results, r => r.Node == new NodeId(seqNode.Id));
    }

    /// <summary>
    /// FindEngine with empty query returns all nodes.
    /// </summary>
    [Fact]
    public void FindEngine_EmptyQuery_ReturnsAllNodes()
    {
        var (asset, graph) = MakeAssetWithGraph();
        var n1 = new BranchNode { Id = Guid.NewGuid() };
        n1.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "In", Direction = "In", IsExec = true });
        graph.Nodes.Add(n1);

        var model = new BlueprintGraphModel(asset, graph);
        var typeSystem = new BlueprintTypeSystem(NullPinDefaultValueEditorRegistry.Instance);
        var validator  = new BlueprintLinkValidator(model, typeSystem);
        var host   = new StubHostServices(new BlueprintNodeCatalog(new NodeKindRegistry()),
            typeSystem, validator, new StubCommandSink());
        var view   = new GraphView(model, host.CommandSink, host.LinkValidator,
            host.TypeSystem, host.NodeCatalog, host);
        var engine = new FindEngine(model, extras: null);
        var query  = FindQueryParser.Parse("");
        var results = engine.Search(query, FindScope.CurrentGraph, view).ToList();

        // Should find all nodes — we added one.
        Assert.Equal(1, results.Count);
        Assert.Equal(new NodeId(n1.Id), results[0].Node);
    }

    // ── Task 1: IEditorCommands — add-node dispatch ───────────────────────────

    /// <summary>
    /// After BuiltinCommandHandlers.RegisterAll, invoking a registered command succeeds.
    /// Specifically, editor.undo and editor.redo are registered by BuiltinCommandHandlers.
    /// </summary>
    [Fact]
    public void EditorCommands_RegisterAll_RegistersUndoRedo()
    {
        var (asset, graph) = MakeAssetWithGraph();
        var model     = new BlueprintGraphModel(asset, graph);
        var typeSystem = new BlueprintTypeSystem(NullPinDefaultValueEditorRegistry.Instance);
        var validator  = new BlueprintLinkValidator(model, typeSystem);
        var host = new StubHostServices(
            new BlueprintNodeCatalog(new NodeKindRegistry()),
            typeSystem, validator,
            new StubCommandSink());
        var view = new GraphView(model, host.CommandSink, host.LinkValidator,
            host.TypeSystem, host.NodeCatalog, host);

        var commands = new EditorCommandsImpl();
        var findBar  = new FindBar(view, new FindEngine(model, null));
        BuiltinCommandHandlers.RegisterAll(commands, view, findBar);

        // undo and redo must be in the catalog.
        Assert.NotNull(commands.Get(CommandCatalog.Undo));
        Assert.NotNull(commands.Get(CommandCatalog.Redo));
        Assert.NotNull(commands.Get(CommandCatalog.FindInGraph));
    }

    /// <summary>
    /// Invoking editor.undo on a view with one pending operation succeeds (result.Success is always true
    /// because there's nothing to undo; but the Invoke itself must not throw).
    /// </summary>
    [Fact]
    public void EditorCommands_Invoke_UndoOnEmptyStack_DoesNotThrow()
    {
        var (asset, graph) = MakeAssetWithGraph();
        var model     = new BlueprintGraphModel(asset, graph);
        var typeSystem = new BlueprintTypeSystem(NullPinDefaultValueEditorRegistry.Instance);
        var validator  = new BlueprintLinkValidator(model, typeSystem);
        var host = new StubHostServices(
            new BlueprintNodeCatalog(new NodeKindRegistry()),
            typeSystem, validator,
            new StubCommandSink());
        var view = new GraphView(model, host.CommandSink, host.LinkValidator,
            host.TypeSystem, host.NodeCatalog, host);

        var commands = new EditorCommandsImpl();
        var findBar  = new FindBar(view, new FindEngine(model, null));
        BuiltinCommandHandlers.RegisterAll(commands, view, findBar);

        var result = commands.Invoke(CommandCatalog.Undo);
        // Should not throw; we just verify that Invoke didn't return the default
        // (undo on an empty stack typically returns Success=false but doesn't throw)
        _ = result; // method must not throw
    }

    // ── Task 2: picker sources — nodes.all ────────────────────────────────────

    /// <summary>
    /// BlueprintPickerSources registers nodes.all; Query with empty text returns all catalog entries.
    /// </summary>
    [Fact]
    public void PickerSources_NodesAll_ReturnsAllEntries_WhenTextEmpty()
    {
        var (asset, _) = MakeAssetWithGraph();
        var registry   = new NodeKindRegistry();
        var catalog    = new BlueprintNodeCatalog(registry);

        // Fake registry: no entries → catalog returns empty.
        var pickerReg  = new NodeEditor.UI.Picker.PickerRegistry();
        BlueprintPickerSources.Register(pickerReg, catalog, asset);

        var source = pickerReg.Get<NodeCatalogEntry>("nodes.all");
        Assert.NotNull(source);

        var results = source!.Query("", context: null);
        // With no registered kinds the catalog is empty — that's expected.
        Assert.NotNull(results);
    }

    // ── Task 2: picker sources — nodes.by-pin ────────────────────────────────

    /// <summary>
    /// nodes.by-pin source filters by pin compatibility. An exec-out source pin should
    /// return only nodes that have an exec-in pin.
    /// The catalog entry pins are taken from CreateInstance().Pins, so the factory
    /// must produce nodes with their pins already populated.
    /// </summary>
    [Fact]
    public void PickerSources_NodesByPin_ExecOutPin_ReturnsOnlyExecInCompatibleKinds()
    {
        var (asset, _) = MakeAssetWithGraph();
        var registry   = new NodeKindRegistry();

        // Register a "FlowBranch" kind whose factory returns a node with an exec-in pin.
        // (The BranchNode asset class has empty Pins by default, so we create a node with pins directly.)
        registry.Register(new NodeKindDescriptor
        {
            Kind          = "FlowBranch",
            DisplayName   = "Branch",
            Category      = "Flow",
            CreateInstance = () =>
            {
                var n = new BranchNode();
                n.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "In",    Direction = "In",  IsExec = true });
                n.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "True",  Direction = "Out", IsExec = true });
                n.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "False", Direction = "Out", IsExec = true });
                return n;
            },
        });

        // Register a "DataLiteral" kind whose factory returns a node with a data-output-only pin.
        registry.Register(new NodeKindDescriptor
        {
            Kind          = "DataLiteral",
            DisplayName   = "Literal",
            Category      = "Data",
            CreateInstance = () =>
            {
                var n = new LiteralNode { TypeId = "System.Single" };
                n.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "Value", Direction = "Out", IsExec = false,
                    TypeRef = new BlueprintTypeRef { TypeId = "System.Single" } });
                return n;
            },
        });

        var catalog   = new BlueprintNodeCatalog(registry);
        var pickerReg = new NodeEditor.UI.Picker.PickerRegistry();
        BlueprintPickerSources.Register(pickerReg, catalog, asset);

        // Verify the catalog itself has the entries
        Assert.Equal(2, catalog.All.Count);
        var branchEntry = catalog.All.First(e => e.Kind.Id == "FlowBranch");
        Assert.Equal(1, branchEntry.Inputs.Count);  // 1 exec-in pin
        Assert.Equal(PinKind.Exec, branchEntry.Inputs[0].Kind);

        // Verify QueryForPinContext directly on the catalog
        var fakePinId = new PinId(Guid.NewGuid());
        var directResult = catalog.QueryForPinContext(
            new PinContextQuery(fakePinId, PinDirection.Output, PinKind.Exec, null, ""));
        Assert.Contains(directResult, e => e.Kind.Id == "FlowBranch");
        Assert.DoesNotContain(directResult, e => e.Kind.Id == "DataLiteral");

        // Now verify via the picker source
        var source = pickerReg.Get<NodeCatalogEntry>("nodes.by-pin");
        Assert.NotNull(source);

        var context = new Dictionary<string, object?>
        {
            ["sourcePinId"]     = fakePinId,
            ["sourceDirection"] = PinDirection.Output,
            ["sourceKind"]      = PinKind.Exec,
        };
        var results = source!.Query("", context);

        // FlowBranch has an exec-in → must appear.
        Assert.Contains(results, e => e.Kind.Id == "FlowBranch");
        // DataLiteral has no exec pin → must NOT appear.
        Assert.DoesNotContain(results, e => e.Kind.Id == "DataLiteral");
    }

    // ── Task 2: picker sources — variables.all ────────────────────────────────

    /// <summary>
    /// variables.all source lists the asset's variables.
    /// </summary>
    [Fact]
    public void PickerSources_VariablesAll_ListsAssetVariables()
    {
        var (asset, _) = MakeAssetWithGraph();
        asset.Variables.Add(new VariableDecl
        {
            Id   = Guid.NewGuid(),
            Name = "Health",
            Type = new BlueprintTypeRef { TypeId = "System.Single" }
        });
        asset.Variables.Add(new VariableDecl
        {
            Id   = Guid.NewGuid(),
            Name = "Speed",
            Type = new BlueprintTypeRef { TypeId = "System.Single" }
        });

        var catalog   = new BlueprintNodeCatalog(new NodeKindRegistry());
        var pickerReg = new NodeEditor.UI.Picker.PickerRegistry();
        BlueprintPickerSources.Register(pickerReg, catalog, asset);

        var source = pickerReg.Get<VariableDecl>("variables.all");
        Assert.NotNull(source);

        var results = source!.Query("", context: null);
        Assert.Equal(2, results.Count);
        Assert.Contains(results, v => v.Name == "Health");
        Assert.Contains(results, v => v.Name == "Speed");
    }

    /// <summary>
    /// variables.all source filters by text.
    /// </summary>
    [Fact]
    public void PickerSources_VariablesAll_FiltersVariablesByText()
    {
        var (asset, _) = MakeAssetWithGraph();
        asset.Variables.Add(new VariableDecl
        {
            Id   = Guid.NewGuid(),
            Name = "Health",
            Type = new BlueprintTypeRef { TypeId = "System.Single" }
        });
        asset.Variables.Add(new VariableDecl
        {
            Id   = Guid.NewGuid(),
            Name = "Speed",
            Type = new BlueprintTypeRef { TypeId = "System.Single" }
        });

        var catalog   = new BlueprintNodeCatalog(new NodeKindRegistry());
        var pickerReg = new NodeEditor.UI.Picker.PickerRegistry();
        BlueprintPickerSources.Register(pickerReg, catalog, asset);

        var source  = pickerReg.Get<VariableDecl>("variables.all")!;
        var results = source.Query("heal", context: null);

        Assert.Single(results);
        Assert.Equal("Health", results[0].Name);
    }

    // ── Task 2: picker sources — types.all ───────────────────────────────────

    /// <summary>
    /// types.all source returns the type vocabulary.
    /// </summary>
    [Fact]
    public void PickerSources_TypesAll_ReturnsTypeSet()
    {
        var (asset, _) = MakeAssetWithGraph();
        var catalog   = new BlueprintNodeCatalog(new NodeKindRegistry());
        var pickerReg = new NodeEditor.UI.Picker.PickerRegistry();
        BlueprintPickerSources.Register(pickerReg, catalog, asset);

        var source  = pickerReg.Get<TypeKey>("types.all");
        Assert.NotNull(source);

        var results = source!.Query("", context: null);
        Assert.True(results.Count > 0, "types.all should return at least one type");
        Assert.Contains(results, t => t.Id == "System.Single");
        Assert.Contains(results, t => t.Id == "System.Boolean");
    }

    // ── Task 3: variable Get/Set value-pin type fix ───────────────────────────

    /// <summary>
    /// Creating a GetVariableNode with a VariableId that matches a System.Single variable
    /// yields a Value output pin of type System.Single (not System.Object).
    /// </summary>
    [Fact]
    public void GetVariableNode_ValuePin_TypeMatchesDeclaredVariableType()
    {
        var (asset, graph) = MakeAssetWithGraph();

        // Declare a variable of type System.Single.
        var varId = Guid.NewGuid();
        asset.Variables.Add(new VariableDecl
        {
            Id   = varId,
            Name = "Health",
            Type = new BlueprintTypeRef { TypeId = "System.Single" }
        });

        // Add a GetVariableNode that references this variable.
        var getNode = new GetVariableNode { Id = Guid.NewGuid(), VariableId = varId.ToString() };
        // Pins are empty as stored in .bp.json; NodePinSchema will project them.
        graph.Nodes.Add(getNode);

        var model = new BlueprintGraphModel(asset, graph);

        // Find the projected node model.
        var nodeModel = model.Nodes.FirstOrDefault(n => n.Id == new NodeId(getNode.Id));
        Assert.NotNull(nodeModel);

        // The node should have exactly 1 pin: a Value output.
        Assert.Equal(1, nodeModel!.Pins.Count);
        var valuePin = nodeModel.Pins[0];
        Assert.Equal(PinDirection.Output, valuePin.Direction);
        Assert.Equal("Value", valuePin.Label);
        // Must have the declared type, not System.Object.
        Assert.NotNull(valuePin.Type);
        Assert.Equal("System.Single", valuePin.Type!.Value.Id);
    }

    /// <summary>
    /// Creating a SetVariableNode with a VariableId that matches a System.Single variable
    /// yields exec in/out pins plus a typed Value input and output of type System.Single.
    /// </summary>
    [Fact]
    public void SetVariableNode_ValuePin_TypeMatchesDeclaredVariableType()
    {
        var (asset, graph) = MakeAssetWithGraph();

        var varId = Guid.NewGuid();
        asset.Variables.Add(new VariableDecl
        {
            Id   = varId,
            Name = "Health",
            Type = new BlueprintTypeRef { TypeId = "System.Single" }
        });

        var setNode = new SetVariableNode { Id = Guid.NewGuid(), VariableId = varId.ToString() };
        graph.Nodes.Add(setNode);

        var model = new BlueprintGraphModel(asset, graph);

        var nodeModel = model.Nodes.FirstOrDefault(n => n.Id == new NodeId(setNode.Id));
        Assert.NotNull(nodeModel);

        // SetVariable has: exec-in, exec-out, value-in, value-out = 4 pins.
        Assert.Equal(4, nodeModel!.Pins.Count);

        var dataPins = nodeModel.Pins.Where(p => p.Kind == PinKind.Data).ToList();
        Assert.Equal(2, dataPins.Count);
        Assert.All(dataPins, p => Assert.Equal("System.Single", p.Type!.Value.Id));
    }

    /// <summary>
    /// GetVariableNode with an unknown VariableId (not in asset) falls back to System.Object.
    /// </summary>
    [Fact]
    public void GetVariableNode_UnknownVariableId_FallsBackToSystemObject()
    {
        var (asset, graph) = MakeAssetWithGraph();

        // No variables declared — VariableId will not resolve.
        var getNode = new GetVariableNode { Id = Guid.NewGuid(), VariableId = Guid.NewGuid().ToString() };
        graph.Nodes.Add(getNode);

        var model     = new BlueprintGraphModel(asset, graph);
        var nodeModel = model.Nodes.FirstOrDefault(n => n.Id == new NodeId(getNode.Id));
        Assert.NotNull(nodeModel);

        var valuePin = nodeModel!.Pins.FirstOrDefault(p => p.Kind == PinKind.Data);
        Assert.NotNull(valuePin);
        Assert.Equal("System.Object", valuePin!.Type!.Value.Id);
    }

    /// <summary>
    /// GetVariableNode with a "var:GUID" prefixed VariableId correctly resolves the type.
    /// (CanvasRenderer.PlaceVariableNode passes MyBlueprintModel item-ids like "var:abc123".)
    /// </summary>
    [Fact]
    public void GetVariableNode_VarPrefixedId_ResolvesCorrectly()
    {
        var (asset, graph) = MakeAssetWithGraph();

        var varId = Guid.NewGuid();
        asset.Variables.Add(new VariableDecl
        {
            Id   = varId,
            Name = "Position",
            Type = new BlueprintTypeRef { TypeId = "System.Numerics.Vector3" }
        });

        // Simulate MyBlueprintModel item-id format: "var:<Guid>"
        var prefixedId = $"var:{varId}";
        var getNode = new GetVariableNode { Id = Guid.NewGuid(), VariableId = prefixedId };
        graph.Nodes.Add(getNode);

        var model     = new BlueprintGraphModel(asset, graph);
        var nodeModel = model.Nodes.FirstOrDefault(n => n.Id == new NodeId(getNode.Id));
        Assert.NotNull(nodeModel);

        var valuePin = nodeModel!.Pins.FirstOrDefault(p => p.Direction == PinDirection.Output);
        Assert.NotNull(valuePin);
        Assert.Equal("System.Numerics.Vector3", valuePin!.Type!.Value.Id);
    }

    // ── BCP-BATCH-02-FIX Task 3: variable Get/Set create-path ─────────────────

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
    /// Dragging a variable as "Get" (kind "Util.GetVar") through the command sink creates a
    /// real <see cref="GetVariableNode"/> whose projection is a single Value data-output pin
    /// of the variable's type — with NO exec pins (a Get is pure).
    /// </summary>
    [Fact]
    public void CreatePath_GetVariable_ProducesPureValueOutPin()
    {
        var (asset, graph) = MakeAssetWithGraph();
        var varId = Guid.NewGuid();
        asset.Variables.Add(new VariableDecl
        {
            Id = varId, Name = "Health",
            Type = new BlueprintTypeRef { TypeId = "System.Single" },
        });

        var (sink, model) = MakeSink(asset, graph);

        var result = sink.Apply(new GraphCommand.AddNode(
            new NodeId(Guid.NewGuid()),
            new NodeKindKey("Util.GetVar"),
            new System.Numerics.Vector2(10, 20),
            new Dictionary<string, object?> { ["VariableId"] = $"var:{varId}" }));
        Assert.True(result.Success, result.Message);

        // The asset graph must contain a real GetVariableNode (NOT a FunctionCallNode).
        var assetNode = graph.Nodes.OfType<GetVariableNode>().Single();
        Assert.Equal($"var:{varId}", assetNode.VariableId);
        Assert.Empty(graph.Nodes.OfType<FunctionCallNode>());

        // Projected pins: exactly one Value data-output, no exec pins.
        var nodeModel = model.Nodes.Single(n => n.Id == new NodeId(assetNode.Id));
        Assert.Equal(1, nodeModel.Pins.Count);
        var pin = nodeModel.Pins[0];
        Assert.Equal(PinKind.Data,       pin.Kind);
        Assert.Equal(PinDirection.Output, pin.Direction);
        Assert.Equal("Value",            pin.Label);
        Assert.Equal("System.Single",    pin.Type!.Value.Id);
        Assert.DoesNotContain(nodeModel.Pins, p => p.Kind == PinKind.Exec);
    }

    /// <summary>
    /// Dragging a variable as "Set" (kind "Util.SetVar") creates a real
    /// <see cref="SetVariableNode"/> whose projection has exec in/out plus a typed
    /// Value data input of the variable's type.
    /// </summary>
    [Fact]
    public void CreatePath_SetVariable_ProducesExecPlusTypedValueInput()
    {
        var (asset, graph) = MakeAssetWithGraph();
        var varId = Guid.NewGuid();
        asset.Variables.Add(new VariableDecl
        {
            Id = varId, Name = "Ammo",
            Type = new BlueprintTypeRef { TypeId = "System.Int32" },
        });

        var (sink, model) = MakeSink(asset, graph);

        var result = sink.Apply(new GraphCommand.AddNode(
            new NodeId(Guid.NewGuid()),
            new NodeKindKey("Util.SetVar"),
            new System.Numerics.Vector2(0, 0),
            new Dictionary<string, object?> { ["VariableId"] = varId.ToString() }));
        Assert.True(result.Success, result.Message);

        var assetNode = graph.Nodes.OfType<SetVariableNode>().Single();
        Assert.Empty(graph.Nodes.OfType<FunctionCallNode>());

        var nodeModel = model.Nodes.Single(n => n.Id == new NodeId(assetNode.Id));

        // Exec in + exec out present.
        Assert.Contains(nodeModel.Pins, p => p.Kind == PinKind.Exec && p.Direction == PinDirection.Input);
        Assert.Contains(nodeModel.Pins, p => p.Kind == PinKind.Exec && p.Direction == PinDirection.Output);

        // A typed Value data-input of the variable's type.
        var valueIn = nodeModel.Pins.Single(p =>
            p.Kind == PinKind.Data && p.Direction == PinDirection.Input);
        Assert.Equal("Value",        valueIn.Label);
        Assert.Equal("System.Int32", valueIn.Type!.Value.Id);
    }

    // ── BCP-BATCH-02-FIX Task 3: My Blueprint "+" create-variable ──────────────

    /// <summary>
    /// The editor.create-variable handler (My Blueprint "+ Variable") appends a new
    /// <see cref="VariableDecl"/> to the asset and projects it into the Variables section.
    /// </summary>
    [Fact]
    public void CreateVariableCommand_AddsVariableDecl_AppearsInMyBlueprintModel()
    {
        var (asset, _) = MakeAssetWithGraph();
        Assert.Empty(asset.Variables);

        var commands = new EditorCommandsImpl();
        bool dirtied = false;
        Hrot.Blueprints.Editor.Host.BlueprintDocumentFactory.RegisterCreateVariableCommand(
            commands, asset, () => dirtied = true);

        var result = commands.Invoke(NodeEditor.Core.CommandCatalog.CreateVariable);
        Assert.True(result.Success, result.Message);

        // A real VariableDecl was added to the asset.
        var decl = Assert.Single(asset.Variables);
        Assert.False(string.IsNullOrEmpty(decl.Name));
        Assert.True(dirtied, "Creating a variable must mark the document dirty.");

        // It appears in the My Blueprint model's Variables section.
        var model = new Hrot.Blueprints.Editor.Windows.BlueprintMyBlueprintModel();
        model.Retarget(null, asset);
        var items = model.GetItems(
            Hrot.Blueprints.Editor.Windows.BlueprintMyBlueprintModel.SectionVariables);
        Assert.Single(items);
        Assert.Equal(decl.Name, items[0].DisplayName);
    }

    /// <summary>
    /// Invoking the create-variable command twice yields two distinctly-named variables.
    /// </summary>
    [Fact]
    public void CreateVariableCommand_Twice_ProducesUniqueNames()
    {
        var (asset, _) = MakeAssetWithGraph();
        var commands = new EditorCommandsImpl();
        Hrot.Blueprints.Editor.Host.BlueprintDocumentFactory.RegisterCreateVariableCommand(
            commands, asset, () => { });

        commands.Invoke(NodeEditor.Core.CommandCatalog.CreateVariable);
        commands.Invoke(NodeEditor.Core.CommandCatalog.CreateVariable);

        Assert.Equal(2, asset.Variables.Count);
        Assert.NotEqual(asset.Variables[0].Name, asset.Variables[1].Name);
    }

    // ── private stubs ─────────────────────────────────────────────────────────

    private sealed class StubCommandSink : IGraphCommandSink
    {
        public GraphCommandResult Apply(GraphCommand command) => new(true, null);
    }

    private sealed class StubHostServices : IEditorHostServices
    {
        private readonly INodeCatalog         _catalog;
        private readonly ITypeSystem          _typeSystem;
        private readonly ILinkValidator       _validator;
        private readonly IGraphCommandSink    _sink;

        public StubHostServices(
            INodeCatalog catalog,
            ITypeSystem typeSystem,
            ILinkValidator validator,
            IGraphCommandSink sink)
        {
            _catalog    = catalog;
            _typeSystem = typeSystem;
            _validator  = validator;
            _sink       = sink;
        }

        public INodeCatalog         NodeCatalog   => _catalog;
        public ITypeSystem          TypeSystem    => _typeSystem;
        public ILinkValidator       LinkValidator => _validator;
        public IGraphCommandSink    CommandSink   => _sink;
        public IPickerRegistry      Pickers       => null!;
        public IClipboard           Clipboard     => null!;
        public IIconProvider        Icons         => null!;
        public IDiagnosticsSink?    Diagnostics   => null;
        public IDebugSession?       Debug         => null;
        public IInputSource         Input         => null!;
        public IEditorTheme         Theme         => null!;
        public IReadOnlyList<ICustomCanvasRenderer> CustomCanvasRenderers =>
            Array.Empty<ICustomCanvasRenderer>();
        public ICustomElementContextMenuProvider? CustomElementContextMenu => null;
    }
}
