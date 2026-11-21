using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using NodeEditor.Core;
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.Primitives;
using NodeEditor.UI.Canvas;
using Xunit;

namespace NodeEditor.UI.Tests.Canvas;

/// <summary>
/// CE-1000 — starting a link from a whole node in a <see cref="LinkRouting.NodeToNode"/> graph
/// (docs/blueprints/DESIGN_Hsm_Canvas_Authoring.md §4b): drag connects, a click selects, the source is never its own
/// target until the cursor has left it.
/// </summary>
public sealed class NodeToNodeWireTests
{
    private sealed class Pin : IPinModel
    {
        public Pin(PinId id, NodeId owner, PinDirection dir) { Id = id; OwnerNodeId = owner; Direction = dir; }
        public PinId Id { get; }
        public NodeId OwnerNodeId { get; }
        public string Label => "";
        public PinDirection Direction { get; }
        public PinKind Kind => PinKind.Data;
        public TypeKey? Type => null;
        public PinShape Shape => PinShape.None;
        public bool IsAdvanced => false;
        public bool IsOptional => true;
        public string? Tooltip => null;
        public IPinDefaultValue? Default => null;
    }

    private sealed class State : INodeModel
    {
        public State(NodeId id)
        {
            Id = id;
            Out = new Pin(IdGenerator.NewPinId(), id, PinDirection.Output);
            In = new Pin(IdGenerator.NewPinId(), id, PinDirection.Input);
        }
        public Pin Out { get; }
        public Pin In { get; }
        public NodeId Id { get; }
        public NodeKindKey Kind => new("state");
        public string Title => "S";
        public string? Subtitle => null;
        public NodeCategory Category => NodeCategory.Function;
        public Vector2 Position => Vector2.Zero;
        public Vector2? SizeOverride => null;
        NodeState INodeModel.State => NodeState.Normal;
        public string? StatusTooltip => null;
        public bool IsCollapsed => false;
        public bool ShowAdvancedPins => false;
        public IReadOnlyList<IPinModel> Pins => new IPinModel[] { Out, In };
    }

    private sealed class Graph : IGraphModel
    {
        public readonly Dictionary<NodeId, State> States = new();
        public GraphId Id => GraphId.Empty;
        public string DisplayName => "sm";
        public GraphKindDescriptor Kind { get; } =
            new("sm", "SM", false, false) { Routing = LinkRouting.NodeToNode, LinkDisplayName = "Transition" };
        public IReadOnlyCollection<INodeModel> Nodes => States.Values;
        public IReadOnlyCollection<ILinkModel> Links => Array.Empty<ILinkModel>();
        public IReadOnlyCollection<ICommentModel> Comments => Array.Empty<ICommentModel>();
        public event Action<GraphChangeNotification>? Changed { add { } remove { } }
        public INodeModel? FindNode(NodeId id) => States.TryGetValue(id, out var s) ? s : null;
        public IPinModel? FindPin(PinId id) =>
            States.Values.SelectMany(s => s.Pins).FirstOrDefault(p => p.Id == id);
        public ILinkModel? FindLink(LinkId id) => null;
        public State Add() { var s = new State(IdGenerator.NewNodeId()); States[s.Id] = s; return s; }

        // Like HSM: pin ids derive from the node id, so a node that does not exist yet still has a known pin.
        public PinId? NodeLinkPin(NodeId node, PinDirection direction) =>
            FindNode(node)?.Pins.FirstOrDefault(p => p.Direction == direction)?.Id
            ?? new PinId(direction == PinDirection.Input ? Derive(node.Value, 2) : Derive(node.Value, 1));

        public static Guid Derive(Guid g, byte flip) { var b = g.ToByteArray(); b[15] ^= flip; return new Guid(b); }
    }

    /// <summary>A picker that immediately picks the first entry its source offers.</summary>
    private sealed class AutoPickRegistry : IPickerRegistry
    {
        private readonly Dictionary<string, object> _sources = new();
        public string? OpenedKey;
        public void Register<TItem>(string key, IPickerSource<TItem> source) => _sources[key] = source;
        public IPickerSource<TItem>? Get<TItem>(string key) => _sources.TryGetValue(key, out var s) ? s as IPickerSource<TItem> : null;
        public void Open(string key, Vector2 at, System.Action<object> onPick, System.Action? onCancel = null,
                         IReadOnlyDictionary<string, object?>? context = null)
        {
            OpenedKey = key;
            var src = Get<NodeCatalogEntry>(key);
            var first = src?.Query("", context).FirstOrDefault();
            if (first != null) onPick(first); else onCancel?.Invoke();
        }
        public void DrawFrame() { }
    }

    private sealed class FixedSource : IPickerSource<NodeCatalogEntry>
    {
        private readonly NodeCatalogEntry _entry;
        public FixedSource(NodeCatalogEntry e) { _entry = e; }
        public string Title => "t"; public string EmptyResultText => "";
        public PickerLayout PreferredLayout => PickerLayout.Wide;
        public PickerSelectionMode SelectionMode => PickerSelectionMode.Single;
        public QueryCost Cost => QueryCost.Cheap; public bool IsAsync => false;
        public bool AllowsDragOut => false; public bool AllowsDragIn => false; public bool AllowArbitraryTextInput => false;
        public IReadOnlyList<NodeCatalogEntry> Query(string t, IReadOnlyDictionary<string, object?>? c) => new[] { _entry };
        public System.Threading.Tasks.Task<IReadOnlyList<NodeCatalogEntry>> QueryAsync(string t, IReadOnlyDictionary<string, object?>? c, System.Threading.CancellationToken ct)
            => System.Threading.Tasks.Task.FromResult(Query(t, c));
        public void RenderItem(NodeCatalogEntry i, bool s, bool k, IPickerRenderContext ctx) { }
        public void RenderPreview(NodeCatalogEntry i, IPickerRenderContext ctx) { }
        public bool IsPreviewExpensive(NodeCatalogEntry i) => false;
        public string GetSearchableText(NodeCatalogEntry i) => i.DisplayName;
        public string GetItemKey(NodeCatalogEntry i) => i.Kind.Id;
        public bool CanAcceptDrop(object payload) => false;
        public string? GetCategory(NodeCatalogEntry i) => null;
        public string? GetIconKey(NodeCatalogEntry i) => null;
        public string? GetDescription(NodeCatalogEntry i) => null;
    }

    private sealed class Sink : IGraphCommandSink
    {
        public readonly List<GraphCommand> Log = new();
        public GraphCommandResult Apply(GraphCommand cmd) { Log.Add(cmd); return new GraphCommandResult(true, null); }
    }

    /// <summary>One frame of scripted input.</summary>
    private sealed class Input : IInputSource
    {
        public Vector2 MousePosition { get; set; }
        public Vector2 MouseDelta => Vector2.Zero;
        public float WheelDelta => 0f;
        public KeyModifiers Modifiers { get; set; }
        public ReadOnlySpan<char> TextThisFrame => ReadOnlySpan<char>.Empty;
        public bool Released { get; set; }
        public bool Pressed { get; set; }
        public bool IsMouseDown(MouseButton b) => false;
        public MouseButton Button { get; set; } = MouseButton.Left;   // the button Pressed / Released mean
        public bool IsMousePressed(MouseButton b) => b == Button && Pressed;
        public bool IsMouseReleased(MouseButton b) => b == Button && Released;
        public bool IsMouseDoubleClicked(MouseButton b) => false;
        public bool IsKeyDown(EditorKey k) => false;
        public EditorKey? Key { get; set; }   // the key "pressed" this frame
        public bool IsKeyPressed(EditorKey k, bool r = false) => Key == k;
        public bool IsKeyReleased(EditorKey k) => false;
    }

    private sealed class Validator : ILinkValidator
    {
        public LinkValidationResult Validate(PinId f, PinId t) => new(LinkValidity.Valid, null, false, null);
    }

    private sealed class Types : ITypeSystem
    {
        public bool TryGetTypeInfo(TypeKey k, out TypeDisplayInfo i) { i = default!; return false; }
        public Vector4 GetPinColor(TypeKey k) => default;
        public PinShape GetPinShape(TypeKey k, ContainerKind c) => default;
        public IPinDefaultValueEditor? GetDefaultEditor(TypeKey k) => null;
        public bool AreCompatible(TypeKey f, TypeKey t) => false;
        public bool IsImplicitCast(TypeKey f, TypeKey t) => false;
    }

    private sealed class Catalog : INodeCatalog
    {
        public IReadOnlyList<NodeCatalogEntry> All => Array.Empty<NodeCatalogEntry>();
        public IReadOnlyList<NodeCategoryDescriptor> Categories => Array.Empty<NodeCategoryDescriptor>();
        public IReadOnlyList<NodeCatalogEntry> Query(NodeSearchQuery q) => Array.Empty<NodeCatalogEntry>();
        public IReadOnlyList<NodeCatalogEntry> QueryForPinContext(PinContextQuery q) => Array.Empty<NodeCatalogEntry>();
    }

    private sealed class Host : IEditorHostServices
    {
        private readonly Sink _sink;
        public Host(Sink sink) { _sink = sink; }
        public IPickerRegistry? PickerRegistry { get; set; }
        public bool ReadOnly { get; set; }
        public bool IsReadOnly => ReadOnly;
        public INodeCatalog NodeCatalog => throw new NotImplementedException();
        public ITypeSystem TypeSystem => throw new NotImplementedException();
        public ILinkValidator LinkValidator => throw new NotImplementedException();
        public IGraphCommandSink CommandSink => _sink;
        public IPickerRegistry Pickers => PickerRegistry ?? throw new NotImplementedException();
        public IClipboard Clipboard => throw new NotImplementedException();
        public IIconProvider Icons => throw new NotImplementedException();
        public IDiagnosticsSink? Diagnostics => null;
        public IDebugSession? Debug => null;
        public IInputSource Input => new Input();
        public IEditorTheme Theme => throw new NotImplementedException();
    }

    private static (Graph g, Sink sink, GraphView view) Make(IPickerRegistry? pickers = null, bool readOnly = false)
    {
        var g = new Graph();
        var sink = new Sink();
        var view = new GraphView(g, sink, new Validator(), new Types(), new Catalog(),
            new Host(sink) { PickerRegistry = pickers, ReadOnly = readOnly });
        return (g, sink, view);
    }

    private static void Frame(GraphView view, Input input, HoverInfo hover)
    {
        view.Interaction.Hover = hover;
        CanvasInput.HandlePendingWire(view, input);
    }

    private static HoverInfo Over(NodeId n) => new() { Kind = HoverKind.Node, Node = n };

    [Fact]
    public void CE1000_DragFromABorder_ToAnotherState_AddsOneLink_SourceOutToTargetIn()
    {
        var (g, sink, view) = Make();
        var a = g.Add(); var b = g.Add();
        var input = new Input { MousePosition = new Vector2(10, 10) };

        Assert.True(CanvasInput.BeginNodeWire(view, input, a.Id, addToSelectionOnClick: false, sticky: false));
        input.MousePosition = new Vector2(300, 10);                 // well past the drag threshold
        Frame(view, input, Over(b.Id));
        Assert.Equal(b.Id, view.Interaction.PendingWire!.CandidateNode);
        input.Released = true;
        Frame(view, input, Over(b.Id));

        var add = Assert.Single(sink.Log.OfType<GraphCommand.AddLink>());
        Assert.Equal(a.Out.Id, add.From);
        Assert.Equal(b.In.Id, add.To);
        Assert.Equal(InteractionMode.Idle, view.Interaction.Mode);
    }

    [Fact]
    public void CE1000_PressAndReleaseWithoutDragging_SelectsTheNode_AndAddsNoLink()
    {
        var (g, sink, view) = Make();
        var a = g.Add();
        var input = new Input { MousePosition = new Vector2(10, 10) };

        CanvasInput.BeginNodeWire(view, input, a.Id, addToSelectionOnClick: false, sticky: false);
        input.Released = true;
        Frame(view, input, new HoverInfo { Kind = HoverKind.NodeEdge, Node = a.Id });

        Assert.Empty(sink.Log.OfType<GraphCommand.AddLink>());
        Assert.Contains(SelectionEntry.OfNode(a.Id), view.Selection.Items);
    }

    [Fact]
    public void CE1000_ReleasedBackOnTheSourceBeforeLeavingIt_IsNotASelfTransition()
    {
        var (g, sink, view) = Make();
        var a = g.Add();
        var input = new Input { MousePosition = new Vector2(10, 10) };

        CanvasInput.BeginNodeWire(view, input, a.Id, addToSelectionOnClick: false, sticky: false);
        input.MousePosition = new Vector2(60, 10);                  // dragged, but still inside A
        Frame(view, input, Over(a.Id));
        input.Released = true;
        Frame(view, input, Over(a.Id));

        Assert.Empty(sink.Log.OfType<GraphCommand.AddLink>());
    }

    [Fact]
    public void CE1000_LeavingTheSource_ThenComingBack_IsASelfTransition()
    {
        var (g, sink, view) = Make();
        var a = g.Add();
        var input = new Input { MousePosition = new Vector2(10, 10) };

        CanvasInput.BeginNodeWire(view, input, a.Id, addToSelectionOnClick: false, sticky: false);
        input.MousePosition = new Vector2(400, 10);
        Frame(view, input, HoverInfo.None);                         // out over empty canvas
        Frame(view, input, Over(a.Id));                             // and back
        input.Released = true;
        Frame(view, input, Over(a.Id));

        var add = Assert.Single(sink.Log.OfType<GraphCommand.AddLink>());
        Assert.Equal(a.Out.Id, add.From);
        Assert.Equal(a.In.Id, add.To);
    }

    [Fact]
    public void CE1001_DroppedOnEmptyCanvas_OpensTheScopedPicker_AndAddsStatePlusLink_InOneBatch()
    {
        var shared = new AutoPickRegistry();
        var scoped = new ScopedPickerRegistry(shared, "doc:x");
        var entry = new NodeCatalogEntry(new NodeKindKey("state.simple"), "Simple State", null, "States",
            Array.Empty<string>(), null, false, false, false, Array.Empty<PinSignature>(), Array.Empty<PinSignature>());
        scoped.Register("nodes.by-pin", new FixedSource(entry));
        var (g, sink, view) = Make(scoped);
        var a = g.Add();
        var input = new Input { MousePosition = new Vector2(10, 10) };

        CanvasInput.BeginNodeWire(view, input, a.Id, addToSelectionOnClick: false, sticky: false);
        input.MousePosition = new Vector2(500, 300);
        Frame(view, input, HoverInfo.None);
        input.Released = true;
        Frame(view, input, HoverInfo.None);

        Assert.Equal("doc:x/nodes.by-pin", shared.OpenedKey);
        var batch = Assert.Single(sink.Log.OfType<GraphCommand.Batch>());
        var addNode = Assert.Single(batch.Commands.OfType<GraphCommand.AddNode>());
        var addLink = Assert.Single(batch.Commands.OfType<GraphCommand.AddLink>());
        Assert.Equal(a.Out.Id, addLink.From);
        Assert.Equal(g.NodeLinkPin(addNode.AssignedId, PinDirection.Input), addLink.To);
    }

    [Fact]
    public void CE1001_AStickyWire_FinishesOnALeftClick_OverATarget()
    {
        var (g, sink, view) = Make();
        var a = g.Add(); var b = g.Add();
        var input = new Input { MousePosition = new Vector2(10, 10) };

        CanvasInput.BeginNodeWire(view, input, a.Id, addToSelectionOnClick: false, sticky: true);
        input.MousePosition = new Vector2(300, 10);
        Frame(view, input, Over(b.Id));                             // follows, nothing pressed
        Assert.Empty(sink.Log);
        input.Pressed = true;
        Frame(view, input, Over(b.Id));

        var add = Assert.Single(sink.Log.OfType<GraphCommand.AddLink>());
        Assert.Equal(b.In.Id, add.To);
    }

    [Fact]
    public void CE1001_ScopedPickerRegistry_KeepsTwoDocumentsApart_AndFallsThroughForUnknownKeys()
    {
        var shared = new AutoPickRegistry();
        var e1 = new NodeCatalogEntry(new NodeKindKey("one"), "One", null, "c", Array.Empty<string>(), null, false, false, false,
            Array.Empty<PinSignature>(), Array.Empty<PinSignature>());
        var e2 = e1 with { Kind = new NodeKindKey("two") };
        var doc1 = new ScopedPickerRegistry(shared, "doc:1");
        var doc2 = new ScopedPickerRegistry(shared, "doc:2");
        doc1.Register("nodes.all", new FixedSource(e1));
        doc2.Register("nodes.all", new FixedSource(e2));
        shared.Register("assets.any", new FixedSource(e1));

        Assert.Equal("one", doc1.Get<NodeCatalogEntry>("nodes.all")!.Query("", null)[0].Kind.Id);
        Assert.Equal("two", doc2.Get<NodeCatalogEntry>("nodes.all")!.Query("", null)[0].Kind.Id);
        Assert.NotNull(doc1.Get<NodeCatalogEntry>("assets.any"));
    }

    /// <summary>⭐ CE-1343 — a pan the MIDDLE button started ends on its release and opens no menu (the right button's release would).
    /// ✅ Red-proof: the middle-pan branch dropped ⇒ the middle release never ends the pan.</summary>
    [Fact]
    public void CE1343_AMiddleButtonPan_EndsOnItsRelease_WithNoMenu()
    {
        var (_, _, view) = Make();
        view.Interaction.Mode = InteractionMode.Panning;
        view.Interaction.PanByMiddle = true;
        var input = new Input { Button = MouseButton.Middle };
        CanvasInput.HandlePanning(view, input);
        Assert.Equal(InteractionMode.Panning, view.Interaction.Mode);
        input.Released = true;
        CanvasInput.HandlePanning(view, input);
        Assert.Equal(InteractionMode.Idle, view.Interaction.Mode);
        Assert.False(view.Interaction.PanByMiddle);
        Assert.Null(view.Interaction.ContextMenuScreen);
    }

    // ── CE-1276 — a read-only graph hides the authoring affordances ───────────────────────────────────────

    /// <summary>⭐ <c>CE-1276</c> — no link can be STARTED on a read-only graph (node border, Shift-drag, the menu's "Add …" all go
    /// through <c>BeginNodeWire</c>); a writable graph still starts one. 🔴 Red-proof: the gate in <c>BeginNodeWire</c> made conditional on
    /// a false runtime condition ⇒ the read-only view starts a wire.</summary>
    [Fact]
    public void CE1276_ReadOnlyGraph_StartsNoLink_WritableStillDoes()
    {
        var (gRo, _, ro) = Make(readOnly: true);
        var a = gRo.Add();
        Assert.False(CanvasInput.BeginNodeWire(ro, new Input(), a.Id, addToSelectionOnClick: false, sticky: false));
        Assert.Equal(InteractionMode.Idle, ro.Interaction.Mode);
        Assert.Null(ro.Interaction.PendingWire);

        var (gRw, _, rw) = Make();
        var b = gRw.Add();
        Assert.True(CanvasInput.BeginNodeWire(rw, new Input(), b.Id, addToSelectionOnClick: false, sticky: false));
        Assert.Equal(InteractionMode.PendingWire, rw.Interaction.Mode);
    }

    /// <summary>⭐ <c>CE-1276</c> — pressing on a PIN starts a wire on a writable graph and does nothing on a read-only one.
    /// 🔴 Red-proof: the <c>IsReadOnly</c> break in the <c>HoverKind.Pin</c> case made conditional on a false runtime condition.</summary>
    [Fact]
    public void CE1276_PressingAPin_StartsAWire_OnlyOnAWritableGraph()
    {
        foreach (bool readOnly in new[] { true, false })
        {
            var (g, _, view) = Make(readOnly: readOnly);
            var a = g.Add();
            var input = new Input { Pressed = true, MousePosition = new Vector2(5, 5) };
            view.Interaction.Hover = new HoverInfo { Kind = HoverKind.Pin, Pin = a.Out.Id };
            CanvasInput.HandleIdle(view, canProcess: true, isCanvasDirectlyFocused: true, input);
            Assert.Equal(readOnly ? InteractionMode.Idle : InteractionMode.PendingWire, view.Interaction.Mode);
        }
    }

    /// <summary>⭐ <c>CE-1276</c> — Delete removes nothing from a read-only graph (the command is never sent), and the Tab picker does not
    /// open; on a writable graph both work. 🔴 Red-proof: the gates in <c>DeleteSelectedUndoable</c> / the picker condition made
    /// conditional on a false runtime condition.</summary>
    [Fact]
    public void CE1276_ReadOnlyGraph_DeletesNothing_AndOpensNoAddPicker()
    {
        foreach (bool readOnly in new[] { true, false })
        {
            var reg = new AutoPickRegistry();
            var (g, sink, view) = Make(reg, readOnly);
            var a = g.Add();
            view.Selection.ReplaceWith(SelectionEntry.OfNode(a.Id));
            NodeEditor.UI.Action.EditCommands.DeleteSelectedUndoable(view);
            Assert.Equal(readOnly, sink.Log.Count == 0);

            view.Interaction.Hover = default;
            CanvasInput.HandleIdle(view, canProcess: true, isCanvasDirectlyFocused: true, new Input { Key = EditorKey.Tab });
            Assert.Equal(readOnly ? null : "nodes.all", reg.OpenedKey);
        }
    }

    private static List<string> MenuLabels(GraphView view, HoverInfo target)
    {
        var ctx = ImGuiNET.ImGui.CreateContext();
        ImGuiNET.ImGui.SetCurrentContext(ctx);
        var io = ImGuiNET.ImGui.GetIO();
        io.Fonts.AddFontDefault();
        io.Fonts.Build();
        io.DisplaySize = new Vector2(1280, 720);
        io.DeltaTime = 1f / 60f;
        var renderer = new CanvasRenderer();
        view.Interaction.ContextMenuTarget = target;
        var log = new List<string>();
        CanvasRenderer.MenuLog = log;
        try
        {
            for (int frame = 0; frame < 3; frame++)
            {
                ImGuiNET.ImGui.NewFrame();
                ImGuiNET.ImGui.Begin("w");
                if (frame == 0) ImGuiNET.ImGui.OpenPopup("##canvas_ctx");
                if (ImGuiNET.ImGui.BeginPopup("##canvas_ctx"))
                {
                    renderer.DrawContextMenu(view);
                    ImGuiNET.ImGui.EndPopup();
                }
                ImGuiNET.ImGui.End();
                ImGuiNET.ImGui.Render();
            }
        }
        finally
        {
            CanvasRenderer.MenuLog = null;
            ImGuiNET.ImGui.DestroyContext(ctx);
        }
        return log.Distinct().ToList();
    }

    /// <summary>⭐ <c>CE-1276</c> (user: "read-only graph still allow drawing connectors and having 'add' items in context menu") — the
    /// canvas menu of a read-only graph has NO Add / Paste / Comment item on empty canvas, NO Add-link / Rename / Cut / Duplicate / Delete
    /// / Comment on a node, and NOTHING on a pin; it keeps the view items (Frame All, Toggle Breakpoint, Copy). A writable graph still has
    /// all of them. 🔴 Red-proof: the empty-canvas gate made conditional on a false runtime condition ⇒ "Add Node..." appears read-only.</summary>
    [Fact]
    public void CE1276_TheContextMenu_OfAReadOnlyGraph_HasNoAuthoringItems_ButKeepsTheViewOnes()
    {
        var authoringOnCanvas = new[] { "Add Node...", "Add Return Node", "Add Comment", "Paste" };
        var authoringOnNode   = new[] { "Add Transition", "Rename\u2026", "Cut", "Duplicate", "Delete Node", "Add Comment" };

        var (_, _, ro) = Make(readOnly: true);
        var roCanvas = MenuLabels(ro, new HoverInfo { Kind = HoverKind.None });
        Assert.Contains("Frame All", roCanvas);
        Assert.DoesNotContain(roCanvas, l => authoringOnCanvas.Contains(l));

        var (gRo, _, ro2) = Make(readOnly: true);
        var n = gRo.Add();
        var roNode = MenuLabels(ro2, new HoverInfo { Kind = HoverKind.Node, Node = n.Id });
        Assert.Contains("Toggle Breakpoint", roNode);
        Assert.DoesNotContain(roNode, l => authoringOnNode.Contains(l));

        var roPin = MenuLabels(ro2, new HoverInfo { Kind = HoverKind.Pin, Pin = n.Out.Id });
        Assert.Empty(roPin);

        var (_, _, rw) = Make();
        var rwCanvas = MenuLabels(rw, new HoverInfo { Kind = HoverKind.None });
        Assert.All(authoringOnCanvas, l => Assert.Contains(l, rwCanvas));

        var (gRw, _, rw2) = Make();
        var m = gRw.Add();
        var rwNode = MenuLabels(rw2, new HoverInfo { Kind = HoverKind.Node, Node = m.Id });
        Assert.All(authoringOnNode, l => Assert.Contains(l, rwNode));
    }
}
