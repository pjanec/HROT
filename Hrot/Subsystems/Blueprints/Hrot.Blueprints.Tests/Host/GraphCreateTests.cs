using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Editor.GraphEditor;
using Hrot.Blueprints.Editor.Host;
using Hrot.Blueprints.Editor.NodeDrawers;
using Hrot.Blueprints.Editor.Windows;
using Hrot.Blueprints.Tests.Builders;
using NodeEditor.Core;
using NodeEditor.Core.Action;
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Blueprints.Tests.Host;

/// <summary>
/// BP-24 — graph creation (architect Q23-B2): Function graphs from My Blueprint, and the
/// custom-event create finally building both halves of the trio — the declaration <i>and</i> the
/// <c>Kind: Event</c> body graph the compiler emits <c>Event_{Name}</c> from.
///
/// <para>
/// Until this, nothing in the editor ever appended to <see cref="BlueprintAsset.Graphs"/>, which
/// is why BP-12c shipped declaration-only and why calling an editor-declared event was a
/// guaranteed <b>BP1407</b>. The body-pairing assertions here check the exact predicate
/// <c>V_CustomEventHandlers</c> validates: an Event graph whose Name equals the declaration's.
/// </para>
/// </summary>
public sealed class GraphCreateTests
{
    private static BlueprintAsset MakeAsset()
        => BlueprintAssetBuilder.Instance("CreateAsset")
            .WithGraph("Main", GraphKind.Function, _ => { })
            .Build();

    /// <summary>Minimal view whose undo stack the create paths can record onto.</summary>
    private static (GraphView view, BlueprintAsset asset) MakeViewSut()
    {
        var asset      = MakeAsset();
        var graph      = asset.Graphs[0];
        var typeSystem = new BlueprintTypeSystem(NullPinDefaultValueEditorRegistry.Instance);
        var model      = new BlueprintGraphModel(asset, graph);
        var catalog    = new BlueprintNodeCatalog(new NodeKindRegistry()) { Asset = asset };
        var validator  = new BlueprintLinkValidator(model, typeSystem);
        var history    = new CommandHistory();
        var editSvc    = new EditService { Context = new EditServiceContext(history, _ => { }) };
        var sink       = new BlueprintCommandSink(
            asset, graph, model, catalog, validator, history, editSvc, markDirty: _ => { });
        var host       = new StubHostServices(catalog, typeSystem, validator, sink);
        var view       = new GraphView(model, host.CommandSink, host.LinkValidator,
            host.TypeSystem, host.NodeCatalog, host);
        return (view, asset);
    }

    // ── Function graphs ───────────────────────────────────────────────────────

    [Fact]
    public void CreateFunctionGraph_AppendsAFunctionGraph_WithAnEntryNode()
    {
        var asset = MakeAsset();

        var graph = BlueprintDocumentFactory.CreateFunctionGraph(asset, "DoThing");

        Assert.NotNull(graph);
        Assert.Contains(graph, asset.Graphs);
        Assert.Equal(GraphKind.Function, graph!.Kind);
        Assert.Equal("DoThing", graph.Name);

        // The explicit entry indicator Stage2_Validate.FindEntryNode looks for — the same shape
        // the shipped Function graphs use (an EventEntryNode with an empty EventTypeId).
        var entry = Assert.IsType<EventEntryNode>(Assert.Single(graph.Nodes));
        Assert.Equal("", entry.EventTypeId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1stFunction")]
    [InlineData("Do Thing")]
    [InlineData("class")]
    public void CreateFunctionGraph_RejectsNonIdentifierNames(string bad)
    {
        var asset = MakeAsset();
        Assert.Null(BlueprintDocumentFactory.CreateFunctionGraph(asset, bad));
        Assert.Single(asset.Graphs);
    }

    [Fact]
    public void CreateFunctionGraph_RejectsANameAnyGraphAlreadyHolds_CaseInsensitively()
    {
        var asset = MakeAsset();
        Assert.Null(BlueprintDocumentFactory.CreateFunctionGraph(asset, "main"));
        Assert.Single(asset.Graphs);
    }

    [Fact]
    public void CreateFunctionGraph_IsOneUndoableEntry_WhenAViewIsSupplied()
    {
        var (view, asset) = MakeViewSut();

        var graph = BlueprintDocumentFactory.CreateFunctionGraph(asset, "DoThing", view: view);

        Assert.NotNull(graph);
        Assert.Equal(2, asset.Graphs.Count);

        view.UndoLast();
        Assert.Single(asset.Graphs);
        Assert.DoesNotContain(graph, asset.Graphs);

        view.RedoLast();
        Assert.Contains(graph, asset.Graphs);
    }

    /// <summary>The quick-add never collides: NewFunction, NewFunction1, …</summary>
    [Fact]
    public void QuickAddCommand_AppendsWithAFreeName_EveryTime()
    {
        var asset    = MakeAsset();
        var commands = new EditorCommandsImpl();
        BlueprintDocumentFactory.RegisterCreateFunctionCommand(commands, asset);

        commands.Invoke(CommandCatalog.CreateFunction);
        commands.Invoke(CommandCatalog.CreateFunction);

        var names = asset.Graphs.Select(g => g.Name).ToList();
        Assert.Contains("NewFunction",  names);
        Assert.Contains("NewFunction1", names);
    }

    [Theory]
    [InlineData("",        "Name cannot be empty.")]
    [InlineData("Do Thing", "not a valid name")]
    [InlineData("Main",     "already exists")]
    public void FunctionModalValidation_ExplainsEachRejection(string name, string expectedFragment)
    {
        var asset = MakeAsset();
        var message = FunctionCreateModal.ValidationMessage(asset, name);
        Assert.NotNull(message);
        Assert.Contains(expectedFragment, message!, StringComparison.OrdinalIgnoreCase);
    }

    // ── Custom events grow their body (the BP-12c missing half) ───────────────

    [Fact]
    public void CreateCustomEvent_AlsoCreatesItsBodyGraph()
    {
        var asset = MakeAsset();

        var decl = BlueprintDocumentFactory.CreateCustomEvent(
            asset, "OnScored", new[] { ("Points", BlueprintTypeSystem.Int32) });

        Assert.NotNull(decl);
        var body = BlueprintDocumentFactory.FindCustomEventBodyGraph(asset, decl!);
        Assert.NotNull(body);
        Assert.Equal(GraphKind.Event, body!.Kind);
        Assert.Equal("OnScored", body.Name);

        // Event_{Name}'s parameter list is emitted from graph Inputs
        // (InstanceEmitter.EmitEventMethod) — the body mirrors the declaration.
        var input = Assert.Single(body.Inputs);
        Assert.Equal("Points", input.Name);
        Assert.Equal(BlueprintTypeSystem.Int32, input.Type.TypeId);

        var entry = Assert.IsType<EventEntryNode>(Assert.Single(body.Nodes));
        Assert.Equal("", entry.EventTypeId);
    }

    /// <summary>A hand-authored body-first asset: the create adopts the graph, no duplicate.</summary>
    [Fact]
    public void CreateCustomEvent_AdoptsAnExistingEventGraphOfTheSameName()
    {
        var asset = MakeAsset();
        var handAuthored = new Graph { Id = Guid.NewGuid(), Name = "OnScored", Kind = GraphKind.Event };
        asset.Graphs.Add(handAuthored);

        var decl = BlueprintDocumentFactory.CreateCustomEvent(asset, "OnScored");

        Assert.NotNull(decl);
        Assert.Equal(2, asset.Graphs.Count);   // Main + the adopted body; nothing new
        Assert.Same(handAuthored, BlueprintDocumentFactory.FindCustomEventBodyGraph(asset, decl!));
    }

    /// <summary>
    /// A non-Event graph already holding the name would make the Event_{Name} pairing ambiguous —
    /// rejected up front, like a duplicate event name.
    /// </summary>
    [Fact]
    public void CreateCustomEvent_RejectsANameAFunctionGraphHolds()
    {
        var asset = MakeAsset();

        Assert.Null(BlueprintDocumentFactory.CreateCustomEvent(asset, "Main"));
        Assert.Empty(asset.CustomEvents);
        Assert.Single(asset.Graphs);
    }

    [Fact]
    public void CreateCustomEvent_UndoRemovesBothTheDeclarationAndTheBody()
    {
        var (view, asset) = MakeViewSut();

        var decl = BlueprintDocumentFactory.CreateCustomEvent(
            asset, "OnScored", parameters: null, markDirty: null, view: view);

        Assert.NotNull(decl);
        Assert.Single(asset.CustomEvents);
        Assert.Equal(2, asset.Graphs.Count);

        view.UndoLast();
        Assert.Empty(asset.CustomEvents);
        Assert.Single(asset.Graphs);

        view.RedoLast();
        Assert.Single(asset.CustomEvents);
        Assert.Equal(2, asset.Graphs.Count);
    }

    [Fact]
    public void QuickAdd_StillNeverCollides_NowThatGraphNamesCountToo()
    {
        var asset = MakeAsset();
        BlueprintDocumentFactory.CreateFunctionGraph(asset, "NewEvent");   // squat the default name

        var decl = BlueprintDocumentFactory.AddCustomEvent(asset);

        Assert.Equal("NewEvent1", decl.Name);
        Assert.NotNull(BlueprintDocumentFactory.FindCustomEventBodyGraph(asset, decl));
    }

    // ── The BP-12b undo gap this closed ───────────────────────────────────────

    /// <summary>
    /// Renaming a custom event also renames its body graph and rewrites name-keyed
    /// CallCustomEvent refs (BP-12b) — but the rename's undo only restored the declaration
    /// lists, silently desyncing the Event_{Name} pairing into a BP1407. The naming snapshot
    /// closes that: undo restores all three mutations together.
    /// </summary>
    [Fact]
    public void UndoingACustomEventRename_RestoresTheBodyGraphAndNameKeyedRefs()
    {
        var (view, asset) = MakeViewSut();
        var decl = BlueprintDocumentFactory.CreateCustomEvent(asset, "OnScored")!;
        var body = BlueprintDocumentFactory.FindCustomEventBodyGraph(asset, decl)!;

        var nameKeyedCall = new CallCustomEventNode { Id = Guid.NewGuid(), EventId = "OnScored" };
        asset.Graphs[0].Nodes.Add(nameKeyedCall);

        var commands = new EditorCommandsImpl();
        BlueprintDocumentFactory.RegisterMyBlueprintItemCommands(commands, asset, view);
        commands.Invoke("editor.rename-item", new EditorCommandContext(null, null,
            new Dictionary<string, object?> { ["itemId"] = $"evt:{decl.Id}", ["newName"] = "OnWon" }));

        Assert.Equal("OnWon", body.Name);
        Assert.Equal("OnWon", nameKeyedCall.EventId);

        view.UndoLast();

        Assert.Equal("OnScored", asset.CustomEvents.Single().Name);
        Assert.Equal("OnScored", body.Name);            // was left as "OnWon" before the fix
        Assert.Equal("OnScored", nameKeyedCall.EventId); // ditto
    }

    // ── My Blueprint sections ─────────────────────────────────────────────────

    [Fact]
    public void FunctionsSection_ListsFunctionGraphs_AndGraphsSectionTheRest()
    {
        var asset = MakeAsset();                                            // "Main" (Function)
        BlueprintDocumentFactory.CreateCustomEvent(asset, "OnScored");      // adds an Event body

        var model = new Hrot.Blueprints.Editor.Windows.BlueprintMyBlueprintModel();
        model.Retarget(null, asset);

        var functions = model.GetItems(BlueprintMyBlueprintModel.SectionFunctions);
        var graphs    = model.GetItems(BlueprintMyBlueprintModel.SectionGraphs);

        Assert.Equal("Main",     Assert.Single(functions).DisplayName);
        Assert.Equal("OnScored", Assert.Single(graphs).DisplayName);
    }

    // ── Test doubles (repo pattern: private per test file) ────────────────────

    private sealed class StubHostServices : IEditorHostServices
    {
        public StubHostServices(INodeCatalog catalog, ITypeSystem typeSystem,
            ILinkValidator validator, IGraphCommandSink sink)
        {
            NodeCatalog = catalog; TypeSystem = typeSystem; LinkValidator = validator; CommandSink = sink;
        }

        public INodeCatalog      NodeCatalog   { get; }
        public ITypeSystem       TypeSystem    { get; }
        public ILinkValidator    LinkValidator { get; }
        public IGraphCommandSink CommandSink   { get; }
        public IPickerRegistry   Pickers       => null!;
        public IClipboard        Clipboard     => null!;
        public IIconProvider     Icons         => null!;
        public IDiagnosticsSink? Diagnostics   => null;
        public IDebugSession?    Debug         => null;
        public IInputSource      Input         => null!;
        public IEditorTheme      Theme         => null!;
        public IReadOnlyList<ICustomCanvasRenderer> CustomCanvasRenderers => Array.Empty<ICustomCanvasRenderer>();
        public ICustomElementContextMenuProvider? CustomElementContextMenu => null;
    }
}
