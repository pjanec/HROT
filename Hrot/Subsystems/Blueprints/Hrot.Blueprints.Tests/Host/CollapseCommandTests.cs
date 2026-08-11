using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Transform;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Editor.GraphEditor;
using Hrot.Blueprints.Editor.Host;
using Hrot.Blueprints.Editor.NodeDrawers;
using NodeEditor.Core;
using NodeEditor.Core.Action;
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Blueprints.Tests.Host;

/// <summary>
/// BP-74, Batch 34 — collapse reached from the canvas: the sink cases, the single undo entry, and
/// the refusal surface.
///
/// <para>
/// ⭐⭐ <b>The first test here is the one that would have caught the shipped defect.</b> Before this
/// batch, <c>GraphCommand.CollapseToMacro</c> reached <c>BlueprintCommandSink</c>'s <c>default:</c>
/// arm — <i>"unknown commands are silently accepted (forward-compat)"</i> — and returned
/// <c>GraphCommandResult(true, null)</c>. A caller could dispatch a collapse, be told it succeeded,
/// and find nothing had happened. Asserting on <c>result.Success</c> alone would still pass today
/// <b>and would have passed before the fix</b>, so every test below asserts on the GRAPH.
/// </para>
///
/// <para>
/// ⭐ Headless throughout: <c>ClipboardCommandTests</c> established that registered editor commands
/// can be driven with <c>commands.Invoke(...)</c> without an ImGui context, so only the menu item
/// itself needs eyes.
/// </para>
/// </summary>
public sealed class CollapseCommandTests
{
    // ── fixture ─────────────────────────────────────────────────────────────

    private sealed class CapturingIndicators : IEditorIndicators
    {
        public List<EditorNotification> Notifications { get; } = new();
        public EditorStatusSnapshot Snapshot => default;
        public event Action? Changed { add { } remove { } }
        public void Notify(EditorNotification notification) => Notifications.Add(notification);
    }

    private sealed record Sut(
        EditorCommandsImpl Commands,
        GraphView          View,
        BlueprintAsset     Asset,
        Graph              Graph,
        BlueprintCommandSink Sink,
        CapturingIndicators Indicators,
        Node A, Node B, Node Entry, Node Return);

    private static Pin P(string name, string dir, bool isExec) => new()
    {
        Id = Guid.NewGuid(), Name = name, Direction = dir, IsExec = isExec,
        TypeRef = new BlueprintTypeRef(),
    };

    private static Link W(Node f, Pin fp, Node t, Pin tp) => new()
    {
        FromNodeId = f.Id, FromPinId = fp.Id, ToNodeId = t.Id, ToPinId = tp.Id,
    };

    private static PrintStringNode Body(out Pin execIn, out Pin execOut)
    {
        var n = new PrintStringNode { Id = Guid.NewGuid() };
        execIn = P("In", "In", true); execOut = P("Out", "Out", true);
        n.Pins.AddRange(new[] { execIn, execOut });
        return n;
    }

    /// <summary>Entry → A → B → Return. A and B are the collapsible middle.</summary>
    private static Sut MakeSut()
    {
        var entry = new EventEntryNode { Id = Guid.NewGuid() };
        var eOut  = P("Out", "Out", true); entry.Pins.Add(eOut);

        var a = Body(out var aIn, out var aOut);
        var b = Body(out var bIn, out var bOut);

        var ret   = new ReturnNode { Id = Guid.NewGuid() };
        var retIn = P("In", "In", true); ret.Pins.Add(retIn);

        var graph = new Graph
        {
            Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Function,
            Nodes = { entry, a, b, ret },
            Links = { W(entry, eOut, a, aIn), W(a, aOut, b, bIn), W(b, bOut, ret, retIn) },
        };

        var asset = new BlueprintAsset
        {
            AssetId  = Guid.NewGuid(),
            Name     = "CollapseHostAsset",
            Dispatch = BlueprintDispatchKind.Instance,
            Graphs   = { graph },
            Header   = new Header(),
        };

        var typeSystem = new BlueprintTypeSystem(NullPinDefaultValueEditorRegistry.Instance);
        var registry   = new NodeKindRegistry();
        var model      = new BlueprintGraphModel(asset, graph);
        var catalog    = new BlueprintNodeCatalog(registry) { Asset = asset };
        var validator  = new BlueprintLinkValidator(model, typeSystem);
        var history    = new CommandHistory();
        var editSvc    = new EditService { Context = new EditServiceContext(history, _ => { }) };

        var sink = new BlueprintCommandSink(
            asset, graph, model, catalog, validator, history, editSvc, markDirty: _ => { });

        var hostServices = new StubHostServices(catalog, typeSystem, validator, sink);
        var view = new GraphView(model, hostServices.CommandSink, hostServices.LinkValidator,
            hostServices.TypeSystem, hostServices.NodeCatalog, hostServices);

        var indicators = new CapturingIndicators();
        var commands   = new EditorCommandsImpl();
        BlueprintDocumentFactory.RegisterCollapseCommands(
            commands, view, asset, () => graph, indicators);

        return new Sut(commands, view, asset, graph, sink, indicators, a, b, entry, ret);
    }

    private static void Select(Sut sut, params Node[] nodes)
    {
        sut.View.Selection.Clear();
        foreach (var n in nodes)
            sut.View.Selection.Add(SelectionEntry.OfNode(new NodeId(n.Id)));
    }

    // ────────────────────────────────────────────────────────────────────────
    // The sink — trap #5
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ <b>The regression test for the silent-success arm.</b> The assertions are on the GRAPH,
    /// not on <c>Success</c>: before the fix this command returned success with the graph untouched,
    /// so a <c>Assert.True(result.Success)</c>-shaped test would have been green on the defect.
    /// </summary>
    [Fact]
    public void Sink_CollapseToMacro_ActuallyMutatesTheGraph()
    {
        var sut = MakeSut();
        var before = sut.Graph.Nodes.Count;

        var result = sut.Sink.Apply(new GraphCommand.CollapseToMacro(
            new[] { new NodeId(sut.A.Id), new NodeId(sut.B.Id) }, "Lifted", null));

        Assert.True(result.Success, result.Message);

        // A and B left the host; a call node replaced them.
        Assert.Equal(before - 1, sut.Graph.Nodes.Count);
        Assert.DoesNotContain(sut.Graph.Nodes, n => n.Id == sut.A.Id);
        Assert.DoesNotContain(sut.Graph.Nodes, n => n.Id == sut.B.Id);
        Assert.Single(sut.Graph.Nodes.OfType<MacroCallNode>());

        // …and the macro they moved into exists.
        var macro = Assert.Single(sut.Asset.Graphs.Where(g => g.Kind == GraphKind.Macro));
        Assert.Equal("Lifted", macro.Name);
    }

    [Fact]
    public void Sink_CollapseToFunction_ActuallyMutatesTheGraph()
    {
        var sut = MakeSut();

        var result = sut.Sink.Apply(new GraphCommand.CollapseToFunction(
            new[] { new NodeId(sut.A.Id), new NodeId(sut.B.Id) }, "Lifted", false, null));

        Assert.True(result.Success, result.Message);
        Assert.Single(sut.Graph.Nodes.OfType<FunctionCallNode>());
        Assert.Contains(sut.Asset.Graphs, g => g.Kind == GraphKind.Function && g.Name == "Lifted");
    }

    /// <summary>
    /// ⚠ A refusal must be a <b>failed</b> result carrying a reason. The <c>default:</c> arm's
    /// <c>(true, null)</c> is precisely what this rules out.
    /// </summary>
    [Fact]
    public void Sink_IllegalSelection_FailsAndSaysWhy()
    {
        var sut = MakeSut();

        var result = sut.Sink.Apply(new GraphCommand.CollapseToMacro(
            new[] { new NodeId(sut.Entry.Id), new NodeId(sut.A.Id) }, "Nope", null));

        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
        Assert.Equal(4, sut.Graph.Nodes.Count);                 // untouched
        Assert.Single(sut.Asset.Graphs);                        // nothing created
    }

    // ────────────────────────────────────────────────────────────────────────
    // The command path + undo
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Command_CollapseToMacro_CollapsesThroughTheRegisteredCommand()
    {
        var sut = MakeSut();
        Select(sut, sut.A, sut.B);

        sut.Commands.Invoke(NodeEditor.Core.CommandCatalog.CollapseToMacro);

        Assert.Single(sut.Graph.Nodes.OfType<MacroCallNode>());
        Assert.Single(sut.Asset.Graphs.Where(g => g.Kind == GraphKind.Macro));
    }

    /// <summary>
    /// ⭐⭐ <b>One undo entry, and undo restores IDENTITY — not merely shape.</b> Collapse creates a
    /// graph, removes two nodes and re-ties three links; a per-edit inverse would have to re-create
    /// nodes with their original ids, which <c>GraphFragmentCloner</c> cannot do because it mints
    /// fresh ones. The snapshot inverse restores the original objects, so the canonical description
    /// matches <b>and</b> the node ids are the ones that were there before.
    /// </summary>
    [Fact]
    public void Command_Undo_IsOneEntry_AndRestoresHostAndDropsTheCreatedGraph()
    {
        var sut = MakeSut();
        var beforeShape = CanonicalGraphShape.Describe(sut.Graph);
        var beforeIds   = sut.Graph.Nodes.Select(n => n.Id).OrderBy(x => x).ToList();

        Select(sut, sut.A, sut.B);
        sut.Commands.Invoke(NodeEditor.Core.CommandCatalog.CollapseToMacro);

        Assert.Equal(1, sut.View.Undo.UndoCount);               // ⭐ ONE entry for the whole gesture
        Assert.Equal(2, sut.Asset.Graphs.Count);

        Assert.True(sut.View.Undo.Undo());

        Assert.Equal(beforeShape, CanonicalGraphShape.Describe(sut.Graph));
        Assert.Equal(beforeIds, sut.Graph.Nodes.Select(n => n.Id).OrderBy(x => x).ToList());
        // ⭐ The half-undo defect this hunts: host restored, orphan macro graph left behind.
        Assert.Single(sut.Asset.Graphs);
        Assert.DoesNotContain(sut.Asset.Graphs, g => g.Kind == GraphKind.Macro);
    }

    /// <summary>
    /// ⚠ The denormalised mirror. <c>Pin.LinkedToIds</c> was rewritten by the forward on the very
    /// node objects the inverse restores, so an inverse that only puts the lists back leaves every
    /// surviving node advertising a link to a call node that no longer exists.
    /// </summary>
    [Fact]
    public void Command_Undo_RebuildsTheLinkedToIdMirror()
    {
        var sut = MakeSut();
        Select(sut, sut.A, sut.B);
        sut.Commands.Invoke(NodeEditor.Core.CommandCatalog.CollapseToMacro);
        sut.View.Undo.Undo();

        var liveIds = new HashSet<Guid>(sut.Graph.Nodes.SelectMany(n => n.Pins).Select(p => p.Id));
        foreach (var pin in sut.Graph.Nodes.SelectMany(n => n.Pins))
            foreach (var linked in pin.LinkedToIds)
                Assert.Contains(linked, liveIds);
    }

    [Fact]
    public void Command_Redo_ReturnsToTheCollapsedState()
    {
        var sut = MakeSut();
        Select(sut, sut.A, sut.B);
        sut.Commands.Invoke(NodeEditor.Core.CommandCatalog.CollapseToMacro);

        var collapsedShape = CanonicalGraphShape.Describe(sut.Graph);
        var macroShape     = CanonicalGraphShape.Describe(
            sut.Asset.Graphs.First(g => g.Kind == GraphKind.Macro));

        sut.View.Undo.Undo();
        Assert.True(sut.View.Undo.Redo());

        Assert.Equal(collapsedShape, CanonicalGraphShape.Describe(sut.Graph));
        var macro = Assert.Single(sut.Asset.Graphs.Where(g => g.Kind == GraphKind.Macro));
        Assert.Equal(macroShape, CanonicalGraphShape.Describe(macro));
    }

    // ────────────────────────────────────────────────────────────────────────
    // The refusal surface — Q26-B2
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ <b>Assert the notification, not just the absence of change.</b> A refusal that mutates
    /// nothing and says nothing is indistinguishable from a dead menu item, which is the complaint
    /// behind <c>BP-76</c>.
    /// </summary>
    [Fact]
    public void Command_IllegalSelection_NotifiesNamingTheOffendingNode_AndMutatesNothing()
    {
        var sut = MakeSut();
        Select(sut, sut.Entry, sut.A);

        sut.Commands.Invoke(NodeEditor.Core.CommandCatalog.CollapseToMacro);

        Assert.Equal(4, sut.Graph.Nodes.Count);
        Assert.Single(sut.Asset.Graphs);
        Assert.Equal(0, sut.View.Undo.UndoCount);       // a refusal is not an undo entry

        var notification = Assert.Single(sut.Indicators.Notifications);
        Assert.Equal(NotificationSeverity.Warning, notification.Severity);
        Assert.Equal(CollapseAnalysis.RefusalCodes.BoundaryNodeSelected, notification.Id);
        // ⭐ names the offending node — by TYPE, never by Title (BP-76's mistake)
        Assert.Contains(nameof(EventEntryNode), notification.Body);
        Assert.Contains(sut.Entry.Id.ToString("N")[..8], notification.Body);
    }

    /// <summary>
    /// ⭐⭐ <b>The test that locks Q26-B2 against a future "helpful" <c>isEnabled</c>.</b> The
    /// selection below is illegal to collapse; the command must still be ENABLED, so the designer
    /// can invoke it and be told why. Greying it would be shipping the next <c>BP-76</c>.
    /// </summary>
    [Fact]
    public void Command_IsEnabled_ForAnIllegalButNonEmptySelection()
    {
        var sut = MakeSut();
        Select(sut, sut.Entry);      // a boundary node — always refused

        foreach (var id in new[]
                 {
                     NodeEditor.Core.CommandCatalog.CollapseToMacro,
                     NodeEditor.Core.CommandCatalog.CollapseToFunction,
                 })
        {
            var descriptor = sut.Commands.Get(id);
            Assert.NotNull(descriptor);
            Assert.True(descriptor!.IsEnabled(),
                $"{id} must stay enabled for an illegal selection — Q26-B2 refuses on invoke.");
        }
    }

    /// <summary>Empty selection is the one kind-agnostic disablement the shared menu may apply.</summary>
    [Fact]
    public void Command_IsDisabled_WhenNothingIsSelected()
    {
        var sut = MakeSut();
        sut.View.Selection.Clear();

        Assert.False(sut.Commands.Get(NodeEditor.Core.CommandCatalog.CollapseToMacro)!.IsEnabled());
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
