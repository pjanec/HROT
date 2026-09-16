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
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Blueprints.Tests.Host;

/// <summary>
/// BP-68 — dragging a custom event out of the My Blueprint panel produced an <b>unbound</b> node.
///
/// <para>
/// <c>BlueprintCommandSink.CreateAssetNode</c> mapped every kind it did not find in the
/// <c>NodeKindRegistry</c> to a generic <c>FunctionCallNode</c> — its own comment said so:
/// <i>"Dynamic kind (custom event, callable peer) — create a generic FunctionCallNode"</i>. Three
/// create-paths land there, and all three are asset-scoped kinds the sink is the only thing able to
/// bind:
/// </para>
/// <list type="bullet">
///   <item><c>Event.CallCustom</c> — the My Blueprint drag-to-canvas drop (<c>CanvasRenderer</c>)</item>
///   <item><c>CustomEvent.{Name}</c> — the dynamic palette entry (<c>BlueprintNodeCatalog</c>)</item>
///   <item><c>CallPeer.{guid:N}</c> — the dynamic palette entry for a callable peer</item>
/// </list>
///
/// <para>
/// The result was a node with no drawer, no pins, and nothing bound: BP-07's picker never saw it
/// because it is not a <c>CallCustomEventNode</c> at all. The static "Call Custom Event" palette
/// entry worked, which is why this hid — that kind <i>is</i> in the registry.
/// </para>
/// </summary>
public sealed class DynamicKindNodeCreateTests
{
    private static (GraphView view, Graph graph, BlueprintAsset asset) MakeSut(
        Action<BlueprintAsset>? configure = null)
    {
        var asset = BlueprintAssetBuilder.Instance("DynKindAsset")
            .WithGraph("EventGraph", GraphKind.Event, _ => { })
            .Build();
        configure?.Invoke(asset);

        var graph      = asset.Graphs[0];
        var typeSystem = new BlueprintTypeSystem(NullPinDefaultValueEditorRegistry.Instance);
        var model      = new BlueprintGraphModel(asset, graph);
        var catalog    = new BlueprintNodeCatalog(new NodeKindRegistry()) { Asset = asset };
        var validator  = new BlueprintLinkValidator(model, typeSystem);
        var history    = new CommandHistory();
        var editSvc    = new EditService { Context = new EditServiceContext(history, _ => { }) };

        var sink = new BlueprintCommandSink(
            asset, graph, model, catalog, validator, history, editSvc, markDirty: _ => { });

        var host = new StubHostServices(catalog, typeSystem, validator, sink);
        var view = new GraphView(model, host.CommandSink, host.LinkValidator,
            host.TypeSystem, host.NodeCatalog, host);

        return (view, graph, asset);
    }

    /// <summary>Replays exactly what <c>CanvasRenderer</c>'s drop handler does.</summary>
    private static void Drop(
        GraphView view, string kindId, Dictionary<string, object?> props, string label)
    {
        var cb = new CommandBuilder(view.Model);
        var (fwd, inv) = cb.AddNode(new NodeKindKey(kindId), new Vector2(40, 60), props);
        view.Execute(fwd, inv, label);
    }

    private static CustomEventDecl DeclareEvent(BlueprintAsset asset, string name)
        => BlueprintDocumentFactory.CreateCustomEvent(asset, name)!;

    // ── The reported symptom: drag from My Blueprint ──────────────────────────

    /// <summary>
    /// The drop carries the panel item's id (<c>evt:{guid}</c>) and display name. Both must land on
    /// a real <see cref="CallCustomEventNode"/>, bound to the declaration — otherwise the Details
    /// panel has nothing to draw, which is precisely what was reported.
    /// </summary>
    [Fact]
    public void DraggingACustomEvent_CreatesABoundCallCustomEventNode()
    {
        CustomEventDecl? decl = null;
        var (view, graph, asset) = MakeSut(a => decl = DeclareEvent(a, "OnHit"));

        Drop(view, "Event.CallCustom", new Dictionary<string, object?>
        {
            ["EventId"]   = $"evt:{decl!.Id:D}",
            ["EventName"] = "OnHit",
        }, "Call Custom Event");

        var node = Assert.IsType<CallCustomEventNode>(Assert.Single(graph.Nodes));
        Assert.Equal(decl.Id.ToString("D"), node.EventId);

        // And therefore reachable from the BP-07 drawer, which is the point.
        var session = new CallCustomEventNodeSession(node, asset, new DirectEditService());
        Assert.False(session.IsCurrentEventUnresolvedForTest());
    }

    /// <summary>
    /// The panel ships <c>evt:{guid}</c>; the compiler and the drawer both parse a bare GUID. The
    /// prefix must be stripped here rather than tolerated everywhere downstream.
    /// </summary>
    [Fact]
    public void TheItemIdPrefix_IsStripped()
    {
        CustomEventDecl? decl = null;
        var (view, graph, _) = MakeSut(a => decl = DeclareEvent(a, "OnHit"));

        Drop(view, "Event.CallCustom",
            new Dictionary<string, object?> { ["EventId"] = $"evt:{decl!.Id:D}" }, "drop");

        var node = Assert.IsType<CallCustomEventNode>(graph.Nodes[0]);
        Assert.True(Guid.TryParse(node.EventId, out var parsed));
        Assert.Equal(decl.Id, parsed);
    }

    /// <summary>
    /// A drop that carries only the display name still binds: Stage5 and the drawer both accept a
    /// bare name, and the sink resolves it to the declaration's GUID — the canonical form.
    /// </summary>
    [Fact]
    public void ANameOnlyDrop_ResolvesToTheDeclarationGuid()
    {
        CustomEventDecl? decl = null;
        var (view, graph, _) = MakeSut(a => decl = DeclareEvent(a, "OnHit"));

        Drop(view, "Event.CallCustom",
            new Dictionary<string, object?> { ["EventName"] = "OnHit" }, "drop");

        Assert.Equal(decl!.Id.ToString("D"),
            Assert.IsType<CallCustomEventNode>(graph.Nodes[0]).EventId);
    }

    /// <summary>
    /// Undo must remove it, like any other placement (BP-11/BP-65). Driven through
    /// <c>view.UndoLast()</c> — what the Ctrl+Z command handler actually calls — rather than the
    /// stack directly, so the whole command path is covered.
    /// </summary>
    [Fact]
    public void DroppingACustomEvent_IsUndoable()
    {
        var (view, graph, _) = MakeSut(a => DeclareEvent(a, "OnHit"));

        Drop(view, "Event.CallCustom",
            new Dictionary<string, object?> { ["EventName"] = "OnHit" }, "Call Custom Event");
        Assert.Single(graph.Nodes);

        Assert.Equal(1, view.Undo.UndoCount);
        Assert.True(view.Undo.CanUndo);
        view.UndoLast();
        Assert.Empty(graph.Nodes);
    }

    // ── The same bug via the palette's dynamic entries ────────────────────────

    /// <summary>
    /// <c>BlueprintNodeCatalog.MakeCustomEventEntry</c> mints <c>CustomEvent.{Name}</c>, so the
    /// palette's "Call OnHit" row hit the identical fallback. The static "Call Custom Event" entry
    /// worked (it is registry-backed), which is what made this look like a drag-only problem.
    /// </summary>
    [Fact]
    public void ThePaletteCustomEventEntry_AlsoCreatesABoundNode()
    {
        CustomEventDecl? decl = null;
        var (view, graph, _) = MakeSut(a => decl = DeclareEvent(a, "OnHit"));

        Drop(view, "CustomEvent.OnHit", new Dictionary<string, object?>(), "Call OnHit");

        Assert.Equal(decl!.Id.ToString("D"),
            Assert.IsType<CallCustomEventNode>(graph.Nodes[0]).EventId);
    }

    /// <summary>
    /// The third occupant of the same fallback: a callable peer must become a real
    /// <see cref="CallPeerBlueprintNode"/> so BP-08's picker and the typed-pin projection see it.
    /// </summary>
    [Fact]
    public void ThePaletteCallablePeerEntry_CreatesABoundPeerNode()
    {
        var peerId = Guid.NewGuid();
        var (view, graph, _) = MakeSut(a => a.CallablePeers.Add(peerId));

        Drop(view, $"CallPeer.{peerId:N}", new Dictionary<string, object?>(), "Call Peer");

        var node = Assert.IsType<CallPeerBlueprintNode>(graph.Nodes[0]);
        Assert.True(Guid.TryParse(node.PeerBlueprintId, out var parsed));
        Assert.Equal(peerId, parsed);
    }

    // ── Guards ────────────────────────────────────────────────────────────────

    /// <summary>
    /// An unknown custom-event name must still produce a <see cref="CallCustomEventNode"/> — the
    /// designer can then pick the right event in Details. Falling back to a FunctionCallNode would
    /// leave them with a node no drawer can fix.
    /// </summary>
    [Fact]
    public void AnUnresolvableCustomEvent_StillCreatesTheRightNodeType()
    {
        var (view, graph, _) = MakeSut();

        Drop(view, "CustomEvent.Missing", new Dictionary<string, object?>(), "drop");

        Assert.IsType<CallCustomEventNode>(graph.Nodes[0]);
    }

    /// <summary>
    /// A genuinely unknown dynamic kind keeps the old FunctionCallNode fallback — this change
    /// narrows that fallback, it does not remove it.
    /// </summary>
    [Fact]
    public void AnUnrelatedUnknownKind_StillFallsBackToFunctionCall()
    {
        var (view, graph, _) = MakeSut();

        Drop(view, "Totally.Unknown", new Dictionary<string, object?>(), "drop");

        Assert.Equal("Totally.Unknown",
            Assert.IsType<FunctionCallNode>(graph.Nodes[0]).MethodName);
    }

    // ── Node header (BP-68 follow-up) ─────────────────────────────────────────

    /// <summary>
    /// The header showed the raw <c>EventId</c>, so a correctly-bound node read
    /// <c>"Call 3f2a…"</c>. It must resolve to the declared name, like Get/SetVariable do.
    /// </summary>
    [Fact]
    public void TheHeader_ShowsTheEventName_NotItsGuid()
    {
        var asset = BlueprintAssetBuilder.Instance("TitleAsset")
            .WithGraph("EventGraph", GraphKind.Event, _ => { }).Build();
        var decl  = DeclareEvent(asset, "OnHit");

        var node = new CallCustomEventNode { EventId = decl.Id.ToString("D") };

        Assert.Equal("Call OnHit", Title(node, asset));
    }

    /// <summary>Hand-authored assets store the bare name; Stage5 accepts it, so the header must too.</summary>
    [Fact]
    public void TheHeader_AcceptsABareName()
    {
        var asset = BlueprintAssetBuilder.Instance("TitleAsset")
            .WithGraph("EventGraph", GraphKind.Event, _ => { }).Build();
        DeclareEvent(asset, "OnHit");

        Assert.Equal("Call OnHit", Title(new CallCustomEventNode { EventId = "OnHit" }, asset));
    }

    /// <summary>
    /// A dangling id stays visible rather than being prettied away — the designer needs to see that
    /// this node points at nothing. An unset one reads as the node kind, not "Call ".
    /// </summary>
    [Fact]
    public void TheHeader_KeepsADanglingId_AndNamesTheKindWhenUnset()
    {
        var asset = BlueprintAssetBuilder.Instance("TitleAsset")
            .WithGraph("EventGraph", GraphKind.Event, _ => { }).Build();
        var orphan = Guid.NewGuid().ToString("D");

        Assert.Equal($"Call {orphan}", Title(new CallCustomEventNode { EventId = orphan }, asset));
        Assert.Equal("Call Custom Event", Title(new CallCustomEventNode(), asset));
    }

    /// <summary>A freshly dropped peer node has no function yet; the header must not trail a colon.</summary>
    [Fact]
    public void ThePeerHeader_ReadsCleanlyBeforeAFunctionIsChosen()
    {
        var asset = BlueprintAssetBuilder.Instance("TitleAsset")
            .WithGraph("EventGraph", GraphKind.Event, _ => { }).Build();

        Assert.Equal("Call Peer", Title(new CallPeerBlueprintNode(), asset));
        Assert.Equal("Call Peer: Fire",
            Title(new CallPeerBlueprintNode { FunctionRef = "Fire" }, asset));
    }

    private static string Title(Node node, BlueprintAsset asset)
        => new BlueprintNodeModel(node, Array.Empty<IPinModel>(), asset).Title;

    // ── Test doubles ──────────────────────────────────────────────────────────

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

    /// <summary>Applies edits straight through; these tests are about node creation.</summary>
    private sealed class DirectEditService : IEditService
    {
        public void MarkDirty(BlueprintAsset asset) { }
        public void RecordPropertyEdit(BlueprintAsset asset, string description, Action apply, Action undo)
            => apply();
        public void NotifyStructureChanged(BlueprintAsset asset) { }
    }
}
