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
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Blueprints.Tests.Host;

/// <summary>
/// BP-23a — copy / cut / paste / duplicate on the canvas.
///
/// <para>
/// All four ids were declared in <c>CommandCatalog</c> with <b>zero</b> handler registrations
/// repo-wide, and the canvas menu's Paste entry was hard-disabled
/// (<c>ImGui.MenuItem("Paste", "Ctrl+V", false, false)</c>). Probably the single most-felt gap.
/// </para>
///
/// <para>
/// ⚠ <b>The trap the audit flagged:</b> paste must not be built on <c>GraphCommand.AddNode</c>.
/// That path rebuilds a node from its kind and re-applies only the properties
/// <c>ApplyInitialProperties</c> knows — 8 node kinds of 50 — so the other 42 would paste
/// stripped of their configuration. <c>NodeConfiguration_SurvivesPaste_ForKindsTheSinkCannotBuild</c>
/// is the test that pins that.
/// </para>
/// </summary>
public sealed class ClipboardCommandTests
{
    private sealed class FakeClipboard : IClipboard
    {
        private string? _text;
        public string? GetText() => _text;
        public void SetText(string text) => _text = text;
    }

    private sealed record Sut(
        EditorCommandsImpl Commands, GraphView View, Graph Graph, FakeClipboard Clipboard);

    private static Sut MakeSut()
    {
        var asset = BlueprintAssetBuilder.Instance("ClipAsset")
            .WithGraph("EventGraph", GraphKind.Event, _ => { })
            .Build();

        var graph      = asset.Graphs[0];
        var typeSystem = new BlueprintTypeSystem(NullPinDefaultValueEditorRegistry.Instance);
        var registry   = new NodeKindRegistry();
        var model      = new BlueprintGraphModel(asset, graph);
        var catalog    = new BlueprintNodeCatalog(registry) { Asset = asset };
        var validator  = new BlueprintLinkValidator(model, typeSystem);
        var history    = new CommandHistory();
        var editSvc    = new EditService { Context = new EditServiceContext(history, _ => { }) };

        var sink = new BlueprintCommandSink(
            asset, graph, model, catalog, validator, history, editSvc, markDirty: _ => { });

        var host = new StubHostServices(catalog, typeSystem, validator, sink);
        var view = new GraphView(model, host.CommandSink, host.LinkValidator,
            host.TypeSystem, host.NodeCatalog, host);

        var clipboard = new FakeClipboard();
        var commands  = new EditorCommandsImpl();

        // DeleteSelection comes from NodeEdit's own registrar; Cut delegates to it.
        NodeEditor.UI.Action.BuiltinCommandHandlers.RegisterAll(commands, view, findBar: null);
        // BP-24 changed the signature to a resolver so paste follows the canvas across graph
        // switches; a single-graph harness just returns its one graph.
        BlueprintDocumentFactory.RegisterClipboardCommands(commands, view, () => graph, clipboard);

        return new Sut(commands, view, graph, clipboard);
    }

    /// <summary>Adds a node to the graph with materialised pins, and returns it.</summary>
    private static T Place<T>(Sut sut, T node, float x = 100, float y = 100) where T : Node
    {
        node.Id = Guid.NewGuid();
        node.EditorMetadata = new NodeMetadata { X = x, Y = y };
        node.Pins = NodePinSchema.GetCanonicalPins(node, new NodeKindRegistry()).ToList();
        foreach (var pin in node.Pins) pin.Id = Guid.NewGuid();
        sut.Graph.Nodes.Add(node);
        ((BlueprintGraphModel)sut.View.Model).RebuildAndNotify();
        return node;
    }

    private static void Select(Sut sut, params Node[] nodes)
        => sut.View.Selection.ReplaceWith(
            nodes.Select(n => SelectionEntry.OfNode(new NodeId(n.Id))).ToArray());

    private static void Run(Sut sut, string commandId, Vector2? canvasPos = null)
        => sut.Commands.Invoke(commandId,
            new EditorCommandContext(ScreenPos: null, CanvasPos: canvasPos, Args: null));

    // ── Registration (the bug itself) ─────────────────────────────────────────

    [Theory]
    [InlineData("editor.copy")]
    [InlineData("editor.cut")]
    [InlineData("editor.paste")]
    [InlineData("editor.duplicate")]
    public void EachClipboardCommand_IsRegistered(string commandId)
    {
        Assert.NotNull(MakeSut().Commands.Get(commandId));
    }

    /// <summary>Paste is disabled until there is something of ours on the clipboard.</summary>
    [Fact]
    public void Paste_IsDisabled_UntilSomethingIsCopied()
    {
        var sut = MakeSut();
        var descriptor = sut.Commands.Get(NodeEditor.Core.CommandCatalog.Paste)!;

        Assert.False(descriptor.IsEnabled());

        Select(sut, Place(sut, new BranchNode()));
        Run(sut, NodeEditor.Core.CommandCatalog.Copy);

        Assert.True(descriptor.IsEnabled());
    }

    // ── Copy / paste ──────────────────────────────────────────────────────────

    [Fact]
    public void CopyThenPaste_AddsAnIndependentNode()
    {
        var sut  = MakeSut();
        var node = Place(sut, new BranchNode());
        Select(sut, node);

        Run(sut, NodeEditor.Core.CommandCatalog.Copy);
        Run(sut, NodeEditor.Core.CommandCatalog.Paste);

        Assert.Equal(2, sut.Graph.Nodes.Count);
        var pasted = sut.Graph.Nodes.Single(n => n.Id != node.Id);
        Assert.IsType<BranchNode>(pasted);

        // Independent: no shared node id, and no shared pin ids either.
        var originalPins = node.Pins.Select(p => p.Id).ToHashSet();
        Assert.DoesNotContain(pasted.Pins, p => originalPins.Contains(p.Id));
    }

    /// <summary>
    /// The copy must be visibly distinct from its source, or the designer cannot tell that
    /// anything happened.
    /// </summary>
    [Fact]
    public void APasteWithNoTarget_IsOffsetFromTheOriginal()
    {
        var sut  = MakeSut();
        var node = Place(sut, new BranchNode(), x: 100, y: 100);
        Select(sut, node);

        Run(sut, NodeEditor.Core.CommandCatalog.Copy);
        Run(sut, NodeEditor.Core.CommandCatalog.Paste);

        var pasted = sut.Graph.Nodes.Single(n => n.Id != node.Id);
        Assert.NotEqual(100f, pasted.EditorMetadata.X);
        Assert.NotEqual(100f, pasted.EditorMetadata.Y);
    }

    /// <summary>Paste-at-cursor puts the fragment's top-left corner where the menu was opened.</summary>
    [Fact]
    public void APasteWithATarget_LandsThere()
    {
        var sut = MakeSut();
        var a   = Place(sut, new BranchNode(),   x: 100, y: 100);
        var b   = Place(sut, new SequenceNode(), x: 180, y: 140);
        Select(sut, a, b);

        Run(sut, NodeEditor.Core.CommandCatalog.Copy);
        Run(sut, NodeEditor.Core.CommandCatalog.Paste, new Vector2(500, 300));

        var pasted = sut.Graph.Nodes.Where(n => n.Id != a.Id && n.Id != b.Id).ToList();
        Assert.Equal(2, pasted.Count);

        // Top-left corner at the target; the fragment keeps its internal layout.
        Assert.Equal(500f, pasted.Min(n => n.EditorMetadata.X));
        Assert.Equal(300f, pasted.Min(n => n.EditorMetadata.Y));
        Assert.Equal(80f,  pasted.Max(n => n.EditorMetadata.X) - pasted.Min(n => n.EditorMetadata.X));
    }

    /// <summary>
    /// <b>The trap the audit called out.</b> <c>ApplyInitialProperties</c> knows 8 node kinds of
    /// 50, so a paste routed through <c>AddNode</c> would silently strip the configuration of the
    /// rest. <c>CompareNode.Operator</c> and <c>CastNode.TargetTypeId</c> are two it does not know.
    /// </summary>
    [Fact]
    public void NodeConfiguration_SurvivesPaste_ForKindsTheSinkCannotBuild()
    {
        var sut     = MakeSut();
        var compare = Place(sut, new CompareNode { Operator = ComparisonOperator.GreaterThan });
        var cast    = Place(sut, new CastNode { TargetTypeId = "Fdp.Core.Entity" });
        Select(sut, compare, cast);

        Run(sut, NodeEditor.Core.CommandCatalog.Copy);
        Run(sut, NodeEditor.Core.CommandCatalog.Paste);

        var pastedCompare = sut.Graph.Nodes.OfType<CompareNode>().Single(n => n.Id != compare.Id);
        var pastedCast    = sut.Graph.Nodes.OfType<CastNode>().Single(n => n.Id != cast.Id);

        Assert.Equal(ComparisonOperator.GreaterThan, pastedCompare.Operator);
        Assert.Equal("Fdp.Core.Entity", pastedCast.TargetTypeId);
    }

    // ── Links ─────────────────────────────────────────────────────────────────

    /// <summary>A wire between two copied nodes is copied too — and rebound to the new pins.</summary>
    [Fact]
    public void AnInternalLink_IsCopied_AndRemappedToTheNewPins()
    {
        var sut = MakeSut();
        var (a, b) = PlaceLinkedPair(sut);
        Select(sut, a, b);

        Run(sut, NodeEditor.Core.CommandCatalog.Copy);
        Run(sut, NodeEditor.Core.CommandCatalog.Paste);

        Assert.Equal(2, sut.Graph.Links.Count);

        var pastedIds  = sut.Graph.Nodes.Where(n => n.Id != a.Id && n.Id != b.Id)
                            .Select(n => n.Id).ToHashSet();
        var pastedLink = sut.Graph.Links.Single(l => pastedIds.Contains(l.FromNodeId));

        Assert.Contains(pastedLink.ToNodeId, pastedIds);
        // …and it must not still point at the originals' pins.
        Assert.DoesNotContain(a.Pins, p => p.Id == pastedLink.FromPinId);
        Assert.DoesNotContain(b.Pins, p => p.Id == pastedLink.ToPinId);
    }

    /// <summary>
    /// A wire with only one end in the selection is dropped. Copying it would either dangle or
    /// silently re-attach to whatever holds that id in the destination.
    /// </summary>
    [Fact]
    public void AHalfSelectedLink_IsNotCopied()
    {
        var sut = MakeSut();
        var (a, _) = PlaceLinkedPair(sut);
        Select(sut, a);

        Run(sut, NodeEditor.Core.CommandCatalog.Copy);
        Run(sut, NodeEditor.Core.CommandCatalog.Paste);

        Assert.Single(sut.Graph.Links);   // still just the original
    }

    // ── Cut / duplicate ───────────────────────────────────────────────────────

    [Fact]
    public void Cut_RemovesTheNodes_AndPasteBringsThemBack()
    {
        var sut  = MakeSut();
        var node = Place(sut, new BranchNode());
        Select(sut, node);

        Run(sut, NodeEditor.Core.CommandCatalog.Cut);
        Assert.Empty(sut.Graph.Nodes);

        Run(sut, NodeEditor.Core.CommandCatalog.Paste);
        Assert.IsType<BranchNode>(Assert.Single(sut.Graph.Nodes));
    }

    /// <summary>
    /// Duplicate must not clobber the clipboard — a designer who copied one thing and then
    /// duplicated another would otherwise silently lose the first.
    /// </summary>
    [Fact]
    public void Duplicate_DoesNotTouchTheClipboard()
    {
        var sut     = MakeSut();
        var copied  = Place(sut, new BranchNode());
        Select(sut, copied);
        Run(sut, NodeEditor.Core.CommandCatalog.Copy);
        var afterCopy = sut.Clipboard.GetText();

        var other = Place(sut, new SequenceNode());
        Select(sut, other);
        Run(sut, NodeEditor.Core.CommandCatalog.Duplicate);

        Assert.Equal(afterCopy, sut.Clipboard.GetText());
        Assert.Equal(2, sut.Graph.Nodes.OfType<SequenceNode>().Count());
    }

    /// <summary>
    /// Paste leaves the new nodes selected, so paste-then-drag works and a second Ctrl+V after a
    /// paste duplicates what was just pasted rather than the original.
    /// </summary>
    [Fact]
    public void Paste_SelectsWhatItPasted()
    {
        var sut  = MakeSut();
        var node = Place(sut, new BranchNode());
        Select(sut, node);

        Run(sut, NodeEditor.Core.CommandCatalog.Copy);
        Run(sut, NodeEditor.Core.CommandCatalog.Paste);

        var pasted = sut.Graph.Nodes.Single(n => n.Id != node.Id);
        Assert.Equal(new[] { pasted.Id }, sut.View.Selection.Nodes.Select(n => n.Value).ToArray());
    }

    // ── Undo ──────────────────────────────────────────────────────────────────

    [Fact]
    public void APaste_IsOneUndoEntry_WhateverItsSize()
    {
        var sut = MakeSut();
        var (a, b) = PlaceLinkedPair(sut);
        Select(sut, a, b);

        Run(sut, NodeEditor.Core.CommandCatalog.Copy);
        Run(sut, NodeEditor.Core.CommandCatalog.Paste);
        Assert.Equal(4, sut.Graph.Nodes.Count);

        Assert.Equal(1, sut.View.Undo.UndoCount);
        sut.View.UndoLast();

        Assert.Equal(2, sut.Graph.Nodes.Count);
        Assert.Single(sut.Graph.Links);
    }

    [Fact]
    public void RedoingAPaste_RestoresIt()
    {
        var sut  = MakeSut();
        var node = Place(sut, new BranchNode());
        Select(sut, node);

        Run(sut, NodeEditor.Core.CommandCatalog.Copy);
        Run(sut, NodeEditor.Core.CommandCatalog.Paste);
        sut.View.UndoLast();
        sut.View.RedoLast();

        Assert.Equal(2, sut.Graph.Nodes.Count);
    }

    // ── Guards ────────────────────────────────────────────────────────────────

    /// <summary>Ordinary clipboard text must never be interpreted as a node graph.</summary>
    [Theory]
    [InlineData("just some text a user copied")]
    [InlineData("{\"Nodes\":[]}")]
    [InlineData("")]
    public void ForeignClipboardText_PastesNothing(string text)
    {
        var sut = MakeSut();
        sut.Clipboard.SetText(text);

        Run(sut, NodeEditor.Core.CommandCatalog.Paste);

        Assert.Empty(sut.Graph.Nodes);
        Assert.Equal(0, sut.View.Undo.UndoCount);
    }

    [Fact]
    public void CopyingAnEmptySelection_LeavesTheClipboardAlone()
    {
        var sut = MakeSut();

        Run(sut, NodeEditor.Core.CommandCatalog.Copy);

        Assert.Null(sut.Clipboard.GetText());
    }

    /// <summary>
    /// Pasting the same clipboard entry twice must give two independent fragments, not two views
    /// of one object graph — the second paste would otherwise re-id the first's nodes.
    /// </summary>
    [Fact]
    public void PastingTwice_ProducesTwoIndependentCopies()
    {
        var sut  = MakeSut();
        var node = Place(sut, new BranchNode());
        Select(sut, node);

        Run(sut, NodeEditor.Core.CommandCatalog.Copy);
        Run(sut, NodeEditor.Core.CommandCatalog.Paste);
        Run(sut, NodeEditor.Core.CommandCatalog.Paste);

        Assert.Equal(3, sut.Graph.Nodes.Count);
        Assert.Equal(3, sut.Graph.Nodes.Select(n => n.Id).Distinct().Count());
        Assert.Equal(
            sut.Graph.Nodes.Sum(n => n.Pins.Count),
            sut.Graph.Nodes.SelectMany(n => n.Pins).Select(p => p.Id).Distinct().Count());
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>Two exec-chained nodes plus the link between them.</summary>
    private static (Node A, Node B) PlaceLinkedPair(Sut sut)
    {
        var a = Place<Node>(sut, new BranchNode(),   x: 100, y: 100);
        var b = Place<Node>(sut, new SequenceNode(), x: 300, y: 100);

        var from = a.Pins.First(p => p.IsExec && p.Direction == "Out");
        var to   = b.Pins.First(p => p.IsExec && p.Direction == "In");

        sut.Graph.Links.Add(new Link
        {
            FromNodeId = a.Id, FromPinId = from.Id,
            ToNodeId   = b.Id, ToPinId   = to.Id,
        });
        ((BlueprintGraphModel)sut.View.Model).RebuildAndNotify();
        return (a, b);
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
