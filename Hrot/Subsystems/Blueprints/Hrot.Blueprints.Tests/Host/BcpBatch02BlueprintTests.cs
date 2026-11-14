using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor;
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

    // ── BCP-BATCH-02-FIX2 Task 2: full node palette ───────────────────────────

    /// <summary>
    /// CreatePaletteRegistry registers the full blueprint node vocabulary (>= 25 kinds),
    /// and BlueprintNodeCatalog.Query("") surfaces every one of them grouped by category.
    /// </summary>
    [Fact]
    public void Palette_RegistersFullBlueprintNodeSet_WithCategories()
    {
        var registry = BlueprintEditorBootstrap.CreatePaletteRegistry();

        var kinds = registry.EnumerateAll().ToList();
        Assert.True(kinds.Count >= 25,
            $"Expected >= 25 palette kinds, got {kinds.Count}.");

        // The When/EQS trio must still be present.
        Assert.Contains(kinds, k => k.Kind == "When");
        Assert.Contains(kinds, k => k.Kind == "ReadEqsResult");
        Assert.Contains(kinds, k => k.Kind == "SpawnEqsSensor");

        // Spot-check core kinds + their categories.
        var branch   = kinds.Single(k => k.Kind == "Branch");
        var sequence = kinds.Single(k => k.Kind == "Sequence");
        var funcCall = kinds.Single(k => k.Kind == "FunctionCall");
        var getVar   = kinds.Single(k => k.Kind == "GetVariable");

        Assert.Equal(BlueprintNodePaletteEntries.Categories.FlowControl, branch.Category);
        Assert.Equal(BlueprintNodePaletteEntries.Categories.FlowControl, sequence.Category);
        Assert.Equal(BlueprintNodePaletteEntries.Categories.Function,    funcCall.Category);
        Assert.Equal(BlueprintNodePaletteEntries.Categories.Variables,   getVar.Category);

        // Every registered kind must surface through the catalog's empty query.
        var catalog = new BlueprintNodeCatalog(registry);
        var all     = catalog.Query(new NodeSearchQuery(""));
        Assert.Equal(kinds.Count, all.Count);
        Assert.Contains(all, e => e.Kind.Id == "Branch");
        Assert.Contains(all, e => e.Kind.Id == "Sequence");
        Assert.Contains(all, e => e.Kind.Id == "FunctionCall");
        Assert.Contains(all, e => e.Kind.Id == "GetVariable");
    }

    /// <summary>
    /// Each palette descriptor's CreateInstance returns a real, distinctly-typed Node with a
    /// fresh Id (so the same kind dragged twice yields two independent nodes).
    /// </summary>
    [Fact]
    public void Palette_CreateInstance_ReturnsTypedNodesWithFreshIds()
    {
        var registry = BlueprintEditorBootstrap.CreatePaletteRegistry();

        var branch = registry.TryGet("Branch")!;
        var n1 = branch.CreateInstance();
        var n2 = branch.CreateInstance();
        Assert.IsType<BranchNode>(n1);
        Assert.IsType<BranchNode>(n2);
        Assert.NotEqual(Guid.Empty, n1.Id);
        Assert.NotEqual(n1.Id, n2.Id);

        Assert.IsType<SequenceNode>(registry.TryGet("Sequence")!.CreateInstance());
        Assert.IsType<GetVariableNode>(registry.TryGet("GetVariable")!.CreateInstance());
        Assert.IsType<FunctionCallNode>(registry.TryGet("FunctionCall")!.CreateInstance());

        // Was "AcquireSlot" until BP-09 removed that entry (no Stage5 lowering -- it compiled to a
        // silent no-op). "Compare.Equal" is a BP-04 entry, so this also covers a baked descriptor.
        var compare = Assert.IsType<CompareNode>(registry.TryGet("Compare.Equal")!.CreateInstance());
        Assert.Equal(ComparisonOperator.Equal, compare.Operator);
    }

    // ── BCP-BATCH-02-FIX2 Task 3: variable node title shows NAME, not UUID ─────

    /// <summary>
    /// A Get node for a variable named "Health" projects Title == "Get Health"
    /// (resolved from the asset), not "Get var:&lt;guid&gt;".
    /// </summary>
    [Fact]
    public void VariableNodeTitle_GetNode_ShowsVariableName_NotUuid()
    {
        var (asset, graph) = MakeAssetWithGraph();
        var varId = Guid.NewGuid();
        asset.Variables.Add(new VariableDecl
        {
            Id = varId, Name = "Health",
            Type = new BlueprintTypeRef { TypeId = "System.Single" },
        });

        // Use the My-Blueprint "var:<guid>" item-id form to prove prefix stripping.
        var getNode = new GetVariableNode { Id = Guid.NewGuid(), VariableId = $"var:{varId}" };
        graph.Nodes.Add(getNode);

        var model     = new BlueprintGraphModel(asset, graph);
        var nodeModel = model.Nodes.Single(n => n.Id == new NodeId(getNode.Id));

        Assert.Equal("Get [Health]", nodeModel.Title);
        Assert.DoesNotContain(varId.ToString(), nodeModel.Title);
    }

    /// <summary>A Set node for "Health" projects Title == "Set [Health]".</summary>
    [Fact]
    public void VariableNodeTitle_SetNode_ShowsVariableName()
    {
        var (asset, graph) = MakeAssetWithGraph();
        var varId = Guid.NewGuid();
        asset.Variables.Add(new VariableDecl
        {
            Id = varId, Name = "Health",
            Type = new BlueprintTypeRef { TypeId = "System.Single" },
        });

        var setNode = new SetVariableNode { Id = Guid.NewGuid(), VariableId = varId.ToString() };
        graph.Nodes.Add(setNode);

        var model     = new BlueprintGraphModel(asset, graph);
        var nodeModel = model.Nodes.Single(n => n.Id == new NodeId(setNode.Id));

        Assert.Equal("Set [Health]", nodeModel.Title);
    }

    /// <summary>
    /// When the variable id cannot be resolved (no matching decl), the title falls back to
    /// the raw id string rather than throwing.
    /// </summary>
    [Fact]
    public void VariableNodeTitle_UnknownVariable_FallsBackToId()
    {
        var (asset, graph) = MakeAssetWithGraph();
        var unknownId = Guid.NewGuid().ToString();
        var getNode = new GetVariableNode { Id = Guid.NewGuid(), VariableId = unknownId };
        graph.Nodes.Add(getNode);

        var model     = new BlueprintGraphModel(asset, graph);
        var nodeModel = model.Nodes.Single(n => n.Id == new NodeId(getNode.Id));

        Assert.Equal($"Get [{unknownId}]", nodeModel.Title);
    }

    // ── BCP-BATCH-02-FIX2 Task 5: variable-create with name + type ─────────────

    /// <summary>
    /// The headless create path (CreateVariable, invoked by the modal's confirm) adds a
    /// VariableDecl with the entered name and type to the asset.
    /// </summary>
    [Fact]
    public void CreateVariable_WithNameAndType_AddsMatchingVariableDecl()
    {
        var (asset, _) = MakeAssetWithGraph();
        Assert.Empty(asset.Variables);

        bool dirtied = false;
        var decl = Hrot.Blueprints.Editor.Host.BlueprintDocumentFactory.CreateVariable(
            asset, "Speed", "System.Single", () => dirtied = true);

        var added = Assert.Single(asset.Variables);
        Assert.Same(decl, added);
        Assert.Equal("Speed", added.Name);
        Assert.Equal("System.Single", added.Type!.TypeId);
        Assert.True(dirtied, "Creating a variable must mark the document dirty.");
    }

    /// <summary>
    /// BCP-BATCH-02-FIX3 Task 2: a second create with the same name (case-insensitive) is
    /// REJECTED — no silent numeric suffix, no new VariableDecl, the original is untouched.
    /// </summary>
    [Fact]
    public void CreateVariable_DuplicateName_IsRejected()
    {
        var (asset, _) = MakeAssetWithGraph();

        var a = Hrot.Blueprints.Editor.Host.BlueprintDocumentFactory.CreateVariable(
            asset, "Speed", "System.Single");
        Assert.NotNull(a);
        Assert.Equal("Speed", a!.Name);

        // Exact duplicate → rejected (null), nothing added.
        var dup = Hrot.Blueprints.Editor.Host.BlueprintDocumentFactory.CreateVariable(
            asset, "Speed", "System.Single");
        Assert.Null(dup);
        Assert.Single(asset.Variables);

        // Case-insensitive duplicate → also rejected.
        var dupCase = Hrot.Blueprints.Editor.Host.BlueprintDocumentFactory.CreateVariable(
            asset, "SPEED", "System.Int32");
        Assert.Null(dupCase);
        Assert.Single(asset.Variables);

        // The single surviving variable is the original, unchanged.
        Assert.Equal("Speed", asset.Variables[0].Name);
        Assert.Equal("System.Single", asset.Variables[0].Type!.TypeId);
    }

    /// <summary>
    /// BCP-BATCH-02-FIX3 Task 2: a unique name is accepted and added with the requested
    /// name verbatim (no suffix).
    /// </summary>
    [Fact]
    public void CreateVariable_UniqueName_IsAdded()
    {
        var (asset, _) = MakeAssetWithGraph();

        var a = Hrot.Blueprints.Editor.Host.BlueprintDocumentFactory.CreateVariable(
            asset, "Speed", "System.Single");
        var b = Hrot.Blueprints.Editor.Host.BlueprintDocumentFactory.CreateVariable(
            asset, "Health", "System.Int32");

        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.Equal("Speed",  a!.Name);
        Assert.Equal("Health", b!.Name);
        Assert.Equal(2, asset.Variables.Count);
    }

    /// <summary>
    /// BCP-BATCH-02-FIX3 Task 2: blank/whitespace names are rejected (no fallback rename).
    /// </summary>
    [Fact]
    public void CreateVariable_BlankName_IsRejected()
    {
        var (asset, _) = MakeAssetWithGraph();

        var blank = Hrot.Blueprints.Editor.Host.BlueprintDocumentFactory.CreateVariable(
            asset, "   ", "System.Single");
        Assert.Null(blank);
        Assert.Empty(asset.Variables);
    }

    /// <summary>
    /// BCP-BATCH-02-FIX3 Task 2: IsDuplicateVariableName reports collisions (case-insensitive)
    /// — the predicate the modal uses to gate its Confirm button.
    /// </summary>
    [Fact]
    public void IsDuplicateVariableName_DetectsCaseInsensitiveCollision()
    {
        var (asset, _) = MakeAssetWithGraph();
        asset.Variables.Add(new VariableDecl
        {
            Id = Guid.NewGuid(), Name = "Speed",
            Type = new BlueprintTypeRef { TypeId = "System.Single" },
        });

        Assert.True(Hrot.Blueprints.Editor.Host.BlueprintDocumentFactory
            .IsDuplicateVariableName(asset, "Speed"));
        Assert.True(Hrot.Blueprints.Editor.Host.BlueprintDocumentFactory
            .IsDuplicateVariableName(asset, "speed"));
        Assert.True(Hrot.Blueprints.Editor.Host.BlueprintDocumentFactory
            .IsDuplicateVariableName(asset, "  SPEED  "));
        Assert.False(Hrot.Blueprints.Editor.Host.BlueprintDocumentFactory
            .IsDuplicateVariableName(asset, "Health"));
    }

    /// <summary>
    /// The variable-create modal's confirm callback (the same delegate the modal's Create
    /// button invokes) creates a matching VariableDecl. Draw() is verified to be a safe
    /// no-op without an ImGui context (headless).
    /// </summary>
    [Fact]
    public void VariableCreateModal_ConfirmCallback_CreatesVariable()
    {
        var (asset, _) = MakeAssetWithGraph();

        // The confirm callback is exactly what production wires: route to CreateVariable
        // (FC-2/LV-4: the payload now carries capacity/initialLength; 0 = scalar).
        Hrot.Blueprints.Editor.Windows.VariableCreateModal.ConfirmHandler confirm =
            (name, typeId, capacity, initialLength) =>
                Hrot.Blueprints.Editor.Host.BlueprintDocumentFactory.CreateVariable(
                    asset, name, typeId, null, capacity, initialLength);

        var modal = new Hrot.Blueprints.Editor.Windows.VariableCreateModal(confirm);

        // Draw() must be a safe no-op when there is no ImGui context.
        modal.Open();
        modal.Draw();
        Assert.Empty(asset.Variables); // Draw alone (no confirm) creates nothing.

        // Fire the confirm callback as the Create button would.
        confirm("Speed", "System.Single", 0, 0);

        var decl = Assert.Single(asset.Variables);
        Assert.Equal("Speed", decl.Name);
        Assert.Equal("System.Single", decl.Type!.TypeId);
    }

    // ── BCP-BATCH-02-FIX3 Task 1: wire-drop new node auto-connects ─────────────

    /// <summary>
    /// End-to-end wire-drop auto-connect: an existing node has an exec-OUT pin; the user drops
    /// the wire on empty canvas, picks a kind, and a fresh PINLESS node is created with a brand
    /// new link whose ToPinId is a never-seen GUID on that new node. After Rebuild, the
    /// two-pass slow-path must bind that GUID to the new node's first exec-IN canonical pin so
    /// BOTH link endpoints resolve (FindPin != null) and the resolved To-pin belongs to the new
    /// node — i.e. the wire is drawn connected.
    /// </summary>
    [Fact]
    public void WireDrop_AddPinlessNode_PlusLinkToFreshPin_ResolvesAndConnectsAfterRebuild()
    {
        var (asset, graph) = MakeAssetWithGraph();

        // Source node: an EventEntry with a REAL exec-out pin (authored pins → stable GUID).
        var sourceNode = new EventEntryNode { Id = Guid.NewGuid() };
        var sourceOutPinId = Guid.NewGuid();
        sourceNode.Pins.Add(new Pin
        {
            Id = sourceOutPinId, Name = "Out", Direction = "Out", IsExec = true,
            TypeRef = new BlueprintTypeRef(),
        });
        graph.Nodes.Add(sourceNode);

        // New node from the picker: a BranchNode with EMPTY pins (exactly how the wire-drop
        // create-path adds it — pins are projected by NodePinSchema, never authored).
        var newNode = new BranchNode { Id = Guid.NewGuid() };
        Assert.Empty(newNode.Pins);
        graph.Nodes.Add(newNode);

        // The wire-drop link: from the real source exec-out to a FRESH pin GUID on the new node.
        var freshTargetPinId = Guid.NewGuid();
        graph.Links.Add(new Link
        {
            FromNodeId = sourceNode.Id, FromPinId = sourceOutPinId,
            ToNodeId   = newNode.Id,    ToPinId   = freshTargetPinId,
        });

        var registry = BlueprintEditorBootstrap.CreatePaletteRegistry();
        var model    = new BlueprintGraphModel(asset, graph, registry);
        // Constructor already calls Rebuild(); call again to prove idempotent resolution.
        model.Rebuild();

        // Both endpoints resolve.
        var fromPin = model.FindPin(new PinId(sourceOutPinId));
        var toPin   = model.FindPin(new PinId(freshTargetPinId));
        Assert.NotNull(fromPin);
        Assert.NotNull(toPin);

        // The resolved target pin belongs to the NEW node and is the exec-IN pin (auto-connect target).
        Assert.Equal(new NodeId(newNode.Id), toPin!.OwnerNodeId);
        Assert.Equal(PinKind.Exec,           toPin.Kind);
        Assert.Equal(PinDirection.Input,     toPin.Direction);

        // The link itself resolves to a model link wiring source-out → new-node-in.
        var linkId = BlueprintGraphModel.MakeLinkId(sourceOutPinId, freshTargetPinId);
        var link   = model.FindLink(linkId);
        Assert.NotNull(link);
        Assert.Equal(new PinId(sourceOutPinId),   link!.FromPin);
        Assert.Equal(new PinId(freshTargetPinId), link.ToPin);

        // And the new node is actually present in the projection with its canonical pins.
        var newModel = model.Nodes.Single(n => n.Id == new NodeId(newNode.Id));
        Assert.Contains(newModel.Pins, p => p.Kind == PinKind.Exec && p.Direction == PinDirection.Input);
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
