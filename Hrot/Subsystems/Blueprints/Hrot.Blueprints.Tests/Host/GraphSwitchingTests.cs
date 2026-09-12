using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
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
using Xunit;

namespace Hrot.Blueprints.Tests.Host;

/// <summary>
/// BP-24 — canvas graph switching (architect Q23, package A2 + C2 + D1).
///
/// <para>
/// The per-document stack is built once and holds fixed references; a switch retargets the graph
/// model and command sink <b>in place</b>. These tests pin the three load-bearing consequences:
/// mutations follow the canvas (the sink writes to the graph being looked at), the undo stack
/// survives a switch and <b>auto-switches back</b> to the graph an entry was recorded in before
/// replaying it, and per-graph viewport/selection are saved and restored.
/// </para>
/// </summary>
public sealed class GraphSwitchingTests
{
    private sealed record Sut(
        BlueprintAsset Asset, Graph GraphA, Graph GraphB,
        BlueprintGraphModel Model, BlueprintCommandSink Sink,
        GraphView View, BlueprintGraphSwitcher Switcher);

    /// <summary>Two-graph asset (Function "Main" first, Event "OnThing" second), canvas on A.</summary>
    private static Sut MakeSut()
    {
        BlueprintGraphViewMemory.Reset();

        var asset = BlueprintAssetBuilder.Instance("SwitchAsset")
            .WithGraph("Main",    GraphKind.Function, _ => { })
            .WithGraph("OnThing", GraphKind.Event,    _ => { })
            .Build();

        var graphA     = asset.Graphs[0];
        var graphB     = asset.Graphs[1];
        var typeSystem = new BlueprintTypeSystem(NullPinDefaultValueEditorRegistry.Instance);
        var model      = new BlueprintGraphModel(asset, graphA);
        var catalog    = new BlueprintNodeCatalog(new NodeKindRegistry()) { Asset = asset };
        var validator  = new BlueprintLinkValidator(model, typeSystem);
        var history    = new CommandHistory();
        var editSvc    = new EditService { Context = new EditServiceContext(history, _ => { }) };

        var sink = new BlueprintCommandSink(
            asset, graphA, model, catalog, validator, history, editSvc, markDirty: _ => { });

        var host = new StubHostServices(catalog, typeSystem, validator, sink);
        var view = new GraphView(model, host.CommandSink, host.LinkValidator,
            host.TypeSystem, host.NodeCatalog, host);

        var switcher = new BlueprintGraphSwitcher(asset, model, sink, view);

        return new Sut(asset, graphA, graphB, model, sink, view, switcher);
    }

    /// <summary>Adds one node to the current graph through the undoable canvas path.
    /// (The kind is registry-unknown, so the sink builds its generic fallback — these tests care
    /// about which <em>graph</em> receives the node, not which subtype.)</summary>
    private static NodeId AddNodeVia(GraphView view, Vector2 at)
    {
        var cb = new CommandBuilder(view.Model);
        var (fwd, inv) = cb.AddNode(new NodeKindKey("Test.AnyNode"), at);
        view.Execute(fwd, inv, "Add Node");
        return view.Model.Nodes.Single().Id;
    }

    // ── Retarget basics ───────────────────────────────────────────────────────

    [Fact]
    public void SwitchTo_ProjectsTheTargetGraph_AndChangesTheModelId()
    {
        var sut = MakeSut();
        sut.GraphB.Nodes.Add(new EventEntryNode { Id = Guid.NewGuid(), EventTypeId = "" });

        var idBefore = sut.Model.Id;

        Assert.True(sut.Switcher.SwitchTo(sut.GraphB.Id));

        Assert.Equal(sut.GraphB, sut.Model.CurrentGraph);
        Assert.Single(sut.Model.Nodes);                 // GraphB's node, projected
        Assert.NotEqual(idBefore, sut.Model.Id);        // derived id follows the graph
        Assert.Equal("OnThing", sut.Model.DisplayName);
    }

    [Fact]
    public void SwitchTo_AnUnknownGraph_ReturnsFalse_AndStays()
    {
        var sut = MakeSut();
        Assert.False(sut.Switcher.SwitchTo(Guid.NewGuid()));
        Assert.Equal(sut.GraphA, sut.Model.CurrentGraph);
    }

    /// <summary>
    /// The point of retargeting the sink: after a switch, an editor mutation lands on the graph
    /// the designer is looking at — not the one the document happened to open on.
    /// </summary>
    [Fact]
    public void MutationsAfterASwitch_LandOnTheCurrentGraph()
    {
        var sut = MakeSut();
        sut.Switcher.SwitchTo(sut.GraphB.Id);

        AddNodeVia(sut.View, new Vector2(10, 10));

        Assert.Empty(sut.GraphA.Nodes);
        Assert.Single(sut.GraphB.Nodes);
    }

    // ── Undo across switches (the Q23-A sub-decision) ─────────────────────────

    [Fact]
    public void UndoStack_SurvivesASwitch()
    {
        var sut = MakeSut();
        AddNodeVia(sut.View, new Vector2(10, 10));

        sut.Switcher.SwitchTo(sut.GraphB.Id);

        Assert.True(sut.View.Undo.CanUndo);
    }

    /// <summary>
    /// The wrong-graph-mutation hazard, pinned: an entry recorded in A, undone while looking at
    /// B, must first switch the canvas back to A — then remove A's node. Without the context
    /// hooks the sink (now pointing at B) would try to remove the node from B.
    /// </summary>
    [Fact]
    public void Undo_AutoSwitchesToTheEntrysGraph_AndReversesThere()
    {
        var sut = MakeSut();
        AddNodeVia(sut.View, new Vector2(10, 10));
        Assert.Single(sut.GraphA.Nodes);

        sut.Switcher.SwitchTo(sut.GraphB.Id);
        sut.View.UndoLast();

        Assert.Equal(sut.GraphA.Id, sut.Switcher.CurrentGraphId);   // canvas followed the undo
        Assert.Empty(sut.GraphA.Nodes);                             // and the node is gone
        Assert.Empty(sut.GraphB.Nodes);                             // B untouched
    }

    [Fact]
    public void Redo_AutoSwitchesToo()
    {
        var sut = MakeSut();
        AddNodeVia(sut.View, new Vector2(10, 10));
        sut.View.UndoLast();

        sut.Switcher.SwitchTo(sut.GraphB.Id);
        sut.View.RedoLast();

        Assert.Equal(sut.GraphA.Id, sut.Switcher.CurrentGraphId);
        Assert.Single(sut.GraphA.Nodes);
    }

    // ── Per-graph view state (Q23-C's companion) ──────────────────────────────

    [Fact]
    public void ViewportAndSelection_AreRestoredPerGraph()
    {
        var sut = MakeSut();
        var nodeId = AddNodeVia(sut.View, new Vector2(10, 10));

        sut.View.Viewport.PanGraph = new Vector2(500, 300);
        sut.View.Viewport.SetZoom(2f);
        sut.View.Selection.ReplaceWith(SelectionEntry.OfNode(nodeId));

        sut.Switcher.SwitchTo(sut.GraphB.Id);
        Assert.Empty(sut.View.Selection.Items);          // B has its own (empty) selection
        sut.View.Viewport.PanGraph = new Vector2(-50, -50);

        sut.Switcher.SwitchTo(sut.GraphA.Id);

        Assert.Equal(new Vector2(500, 300), sut.View.Viewport.PanGraph);
        Assert.Equal(2f, sut.View.Viewport.Zoom);
        Assert.Equal(nodeId, Assert.Single(sut.View.Selection.Nodes.ToList()));
    }

    /// <summary>The outgoing camera lands in GraphMetadata, so it rides along with a real save.</summary>
    [Fact]
    public void SwitchingAway_WritesTheViewportToGraphMetadata()
    {
        var sut = MakeSut();
        sut.View.Viewport.PanGraph = new Vector2(77, 88);
        sut.View.Viewport.SetZoom(0.5f);

        sut.Switcher.SwitchTo(sut.GraphB.Id);

        Assert.Equal(77f,  sut.GraphA.EditorMetadata.ViewportX);
        Assert.Equal(88f,  sut.GraphA.EditorMetadata.ViewportY);
        Assert.Equal(0.5f, sut.GraphA.EditorMetadata.ViewportZoom);
    }

    [Fact]
    public void FirstVisit_ReadsAViewportPersistedInTheAsset()
    {
        var sut = MakeSut();
        sut.GraphB.EditorMetadata.ViewportX    = 123f;
        sut.GraphB.EditorMetadata.ViewportY    = 45f;
        sut.GraphB.EditorMetadata.ViewportZoom = 1.5f;

        sut.Switcher.SwitchTo(sut.GraphB.Id);

        Assert.Equal(new Vector2(123, 45), sut.View.Viewport.PanGraph);
        Assert.Equal(1.5f, sut.View.Viewport.Zoom);
    }

    // ── Which graph opens (Q23-C) ─────────────────────────────────────────────

    /// <summary>
    /// The pre-BP-24 rule preferred an Event graph, so an asset whose main graph is a Function
    /// silently opened elsewhere (CustomEventSubscriberDemo opened on OnPing instead of Tick).
    /// First-in-authored-order is the fallback now.
    /// </summary>
    [Fact]
    public void InitialGraph_IsTheFirstInAuthoredOrder_NotTheEventGraph()
    {
        BlueprintGraphViewMemory.Reset();
        var asset = BlueprintAssetBuilder.Instance("OrderAsset")
            .WithGraph("Tick",   GraphKind.Function, _ => { })
            .WithGraph("OnPing", GraphKind.Event,    _ => { })
            .Build();

        Assert.Equal("Tick", BlueprintDocumentFactory.ResolveInitialGraph(asset)!.Name);
    }

    [Fact]
    public void InitialGraph_PrefersTheLastViewedOne()
    {
        var sut = MakeSut();
        sut.Switcher.SwitchTo(sut.GraphB.Id);   // records last-viewed

        Assert.Equal(sut.GraphB, BlueprintDocumentFactory.ResolveInitialGraph(sut.Asset));
    }

    // ── The gesture (Q23-D1): editor.go-to-graph ──────────────────────────────

    private static EditorCommandsImpl RegisterGoTo(Sut sut)
    {
        var commands = new EditorCommandsImpl();
        BlueprintDocumentFactory.RegisterGoToGraphCommand(commands, sut.Asset, sut.Switcher);
        return commands;
    }

    private static EditorCommandResult InvokeGoTo(EditorCommandsImpl commands, string key, object? value)
        => commands.Invoke(CommandCatalog.GoToGraph,
            new EditorCommandContext(null, null, new Dictionary<string, object?> { [key] = value }));

    [Fact]
    public void GoToGraph_SwitchesByPanelItemId()
    {
        var sut = MakeSut();
        var commands = RegisterGoTo(sut);

        InvokeGoTo(commands, "itemId", $"graph:{sut.GraphB.Id}");

        Assert.Equal(sut.GraphB.Id, sut.Switcher.CurrentGraphId);
    }

    [Fact]
    public void GoToGraph_AcceptsAGraphIdArg()
    {
        var sut = MakeSut();
        var commands = RegisterGoTo(sut);

        InvokeGoTo(commands, "graphId", sut.GraphB.Id.ToString());

        Assert.Equal(sut.GraphB.Id, sut.Switcher.CurrentGraphId);
    }

    /// <summary>Double-clicking a custom event lands on its body graph (same-name pairing).</summary>
    [Fact]
    public void GoToGraph_ResolvesACustomEventToItsBodyGraph()
    {
        var sut = MakeSut();
        var decl = BlueprintDocumentFactory.CreateCustomEvent(sut.Asset, "OnScored")!;
        var body = BlueprintDocumentFactory.FindCustomEventBodyGraph(sut.Asset, decl)!;
        var commands = RegisterGoTo(sut);

        InvokeGoTo(commands, "itemId", $"evt:{decl.Id}");

        Assert.Equal(body.Id, sut.Switcher.CurrentGraphId);
    }

    [Fact]
    public void GoToGraph_WithNothingResolvable_StaysPut()
    {
        var sut = MakeSut();
        var commands = RegisterGoTo(sut);

        InvokeGoTo(commands, "itemId", "var:not-a-graph");

        Assert.Equal(sut.GraphA.Id, sut.Switcher.CurrentGraphId);
    }

    // ── Bookmarks (BP-16's cross-graph jump, live at last) ────────────────────

    /// <summary>
    /// <c>Bookmark.TargetGraph</c> stores the view-level id (the deterministic hash the model
    /// exposes), and <c>BookmarkCommands</c> hands exactly that to its navigate delegate.
    /// </summary>
    [Fact]
    public void SwitchToViewId_MapsTheBookmarkFormBackToTheAssetGraph()
    {
        var sut = MakeSut();
        sut.Switcher.SwitchTo(sut.GraphB.Id);
        var viewIdOfB = sut.Model.Id;             // what a bookmark set on B would store
        sut.Switcher.SwitchTo(sut.GraphA.Id);

        Assert.True(sut.Switcher.SwitchToViewId(viewIdOfB));
        Assert.Equal(sut.GraphB.Id, sut.Switcher.CurrentGraphId);
    }

    // ── Clipboard follows the canvas (the fifth capture-site fix) ─────────────

    [Fact]
    public void Paste_LandsOnTheGraphBeingLookedAt_AfterASwitch()
    {
        var sut = MakeSut();
        var clipboard = new FakeClipboard();
        var commands  = new EditorCommandsImpl();
        NodeEditor.UI.Action.BuiltinCommandHandlers.RegisterAll(commands, sut.View, findBar: null);
        BlueprintDocumentFactory.RegisterClipboardCommands(
            commands, sut.View, () => sut.Switcher.CurrentGraph, clipboard);

        var nodeId = AddNodeVia(sut.View, new Vector2(10, 10));
        sut.View.Selection.ReplaceWith(SelectionEntry.OfNode(nodeId));
        commands.Invoke(CommandCatalog.Copy);

        sut.Switcher.SwitchTo(sut.GraphB.Id);
        commands.Invoke(CommandCatalog.Paste);

        Assert.Single(sut.GraphA.Nodes);   // the original
        Assert.Single(sut.GraphB.Nodes);   // the paste — on the graph the designer sees
    }

    // ── Test doubles (repo pattern: private per test file) ────────────────────

    // ── BP-72: a graph-scoped window follows the switch, end to end ──────────

    /// <summary>
    /// <b>The seam test (trap #9).</b> BP-72's window logic and BP-24's switcher were each already
    /// correct in isolation; what was missing was the wire between them, so the Graph Signature
    /// window edited a graph the designer was not looking at. This drives a REAL
    /// <see cref="BlueprintGraphSwitcher"/> through the same provider the composition root passes
    /// (<c>AiCanvasContext.CurrentGraphId</c>) and asserts the window's resolved edit target moves
    /// with it — the two halves used together, not merely each asserted alone.
    /// </summary>
    [Fact]
    public void GraphSignatureWindow_FollowsARealCanvasSwitch()
    {
        var sut = MakeSut();

        // Exactly what BlueprintDocumentFactory installs on the canvas context.
        Func<Guid> currentGraphId = () => sut.Switcher.CurrentGraphId;

        var window = new Hrot.Blueprints.Editor.Windows.GraphSignatureWindow(
            new Hrot.Blueprints.Editor.EditorSelectionStore(),
            new Hrot.Blueprints.Editor.DirtyTracker());
        window.Retarget(sut.Asset, currentGraphId);

        // Canvas is on graph A (Function "Main").
        window.ResolveEditModels()!.Value.Inputs.AddParameter("fromA", "System.Int32");

        // Switch the canvas to graph B (Event "OnThing") — Event graphs are editable since BP-72.
        Assert.True(sut.Switcher.SwitchTo(sut.GraphB.Id));
        window.ResolveEditModels()!.Value.Inputs.AddParameter("fromB", "System.Single");

        Assert.Single(sut.GraphA.Inputs);
        Assert.Equal("fromA", sut.GraphA.Inputs[0].Name);
        Assert.Single(sut.GraphB.Inputs);
        Assert.Equal("fromB", sut.GraphB.Inputs[0].Name);
    }

    private sealed class FakeClipboard : IClipboard
    {
        private string? _text;
        public string? GetText() => _text;
        public void SetText(string text) => _text = text;
    }

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
