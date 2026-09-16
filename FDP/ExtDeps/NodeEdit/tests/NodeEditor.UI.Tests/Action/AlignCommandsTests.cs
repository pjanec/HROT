using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using NodeEditor.Core;
using NodeEditor.Core.Action;
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.Primitives;
using NodeEditor.UI.Action;
using Xunit;

namespace NodeEditor.UI.Tests.Action;

/// <summary>
/// BP-13 — align / distribute / straighten.
///
/// <para>
/// <c>CommandCatalog</c> declared nine alignment ids with zero implementations anywhere in the
/// editor. Every one is a batch move, so each reduces to <c>CommandBuilder.MoveNodes</c> and is a
/// single undoable step.
/// </para>
/// </summary>
public sealed class AlignCommandsTests
{
    // ── Test model ────────────────────────────────────────────────────────────

    private sealed class MutableNode : INodeModel
    {
        public NodeId       Id       { get; init; } = IdGenerator.NewNodeId();
        public NodeKindKey  Kind     => new("stub");
        public string       Title    => "Stub";
        public string?      Subtitle => null;
        public NodeCategory Category => NodeCategory.Function;
        public Vector2      Position { get; set; }
        public Vector2?     SizeOverride { get; init; }
        public NodeState    State    => NodeState.Normal;
        public string?      StatusTooltip     => null;
        public bool         IsCollapsed       => false;
        public bool         ShowAdvancedPins  => false;
        public NodeId?      ParentContainerId => null;
        public IReadOnlyList<IPinModel> Pins { get; init; } = Array.Empty<IPinModel>();
    }

    private sealed class StubPin : IPinModel
    {
        public PinId        Id          { get; init; } = IdGenerator.NewPinId();
        public NodeId       OwnerNodeId { get; init; }
        public string       Label       => "P";
        public PinDirection Direction   { get; init; }
        public PinKind      Kind        => PinKind.Exec;
        public TypeKey?     Type        => null;
        public PinShape     Shape       => PinShape.Circle;
        public bool         IsAdvanced  => false;
        public bool         IsOptional  => false;
        public string?      Tooltip     => null;
        public IPinDefaultValue? Default => null;
        public int          LinkCount   => 0;
    }

    private sealed class StubLink : ILinkModel
    {
        public LinkId Id      { get; init; } = IdGenerator.NewLinkId();
        public PinId  FromPin { get; init; }
        public PinId  ToPin   { get; init; }
        public LinkStyle Style => LinkStyle.Solid;
        public IReadOnlyList<Vector2> Waypoints => Array.Empty<Vector2>();
    }

    private sealed class Model : IGraphModel
    {
        private readonly List<INodeModel> _nodes = new();
        private readonly List<ILinkModel> _links = new();
        private readonly Dictionary<PinId, IPinModel> _pins = new();

        public GraphId Id => GraphId.Empty;
        public string DisplayName => "test";
        public GraphKindDescriptor Kind => new("test", "Test", false, false);
        public IReadOnlyCollection<INodeModel>    Nodes    => _nodes;
        public IReadOnlyCollection<ILinkModel>    Links    => _links;
        public IReadOnlyCollection<ICommentModel> Comments => Array.Empty<ICommentModel>();

        public INodeModel? FindNode(NodeId id) => _nodes.FirstOrDefault(n => n.Id == id);
        public IPinModel?  FindPin(PinId id)   => _pins.TryGetValue(id, out var p) ? p : null;
        public ILinkModel? FindLink(LinkId id) => _links.FirstOrDefault(l => l.Id == id);
        public event Action<GraphChangeNotification>? Changed { add { } remove { } }

        public MutableNode Add(float x, float y, Vector2? size = null)
        {
            var node = new MutableNode { Position = new Vector2(x, y), SizeOverride = size };
            _nodes.Add(node);
            return node;
        }

        public MutableNode AddWithPins(float x, float y, out StubPin outPin, out StubPin inPin)
        {
            var id  = IdGenerator.NewNodeId();
            var o   = new StubPin { OwnerNodeId = id, Direction = PinDirection.Output };
            var i   = new StubPin { OwnerNodeId = id, Direction = PinDirection.Input };
            var node = new MutableNode { Id = id, Position = new Vector2(x, y), Pins = new IPinModel[] { o, i } };
            _nodes.Add(node);
            _pins[o.Id] = o;
            _pins[i.Id] = i;
            outPin = o; inPin = i;
            return node;
        }

        public void Link(StubPin from, StubPin to)
            => _links.Add(new StubLink { FromPin = from.Id, ToPin = to.Id });

        /// <summary>Applies a MoveNodes command, which is all these commands emit.</summary>
        public void ApplyMove(GraphCommand.MoveNodes move)
        {
            foreach (var m in move.Moves)
                if (FindNode(m.Node) is MutableNode node) node.Position = m.NewPosition;
        }
    }

    private sealed class Sink : IGraphCommandSink
    {
        private readonly Model _model;
        public Sink(Model model) => _model = model;

        public GraphCommandResult Apply(GraphCommand command)
        {
            if (command is GraphCommand.MoveNodes move) _model.ApplyMove(move);
            return new GraphCommandResult(true, null);
        }
    }

    private static (EditorCommandsImpl Commands, GraphView View, Model Model) MakeSut()
    {
        var model = new Model();
        var sink  = new Sink(model);
        var view  = new GraphView(model, sink, new StubValidator(), new StubTypeSystem(),
                                  new StubCatalog(), new StubHost(sink));
        var commands = new EditorCommandsImpl();
        AlignCommands.Register(commands, view);
        return (commands, view, model);
    }

    private static void Select(GraphView view, params INodeModel[] nodes)
        => view.Selection.ReplaceWith(nodes.Select(n => SelectionEntry.OfNode(n.Id)).ToArray());

    private static void Run(EditorCommandsImpl commands, string id) => commands.Invoke(id);

    // ── Registration (the bug itself) ─────────────────────────────────────────

    [Theory]
    [InlineData("editor.align-left")]
    [InlineData("editor.align-right")]
    [InlineData("editor.align-top")]
    [InlineData("editor.align-bottom")]
    [InlineData("editor.align-center-h")]
    [InlineData("editor.align-center-v")]
    [InlineData("editor.distribute-h")]
    [InlineData("editor.distribute-v")]
    [InlineData("editor.straighten-connection")]
    public void EveryDeclaredAlignmentCommand_IsRegistered(string commandId)
    {
        Assert.NotNull(MakeSut().Commands.Get(commandId));
    }

    // ── Align ─────────────────────────────────────────────────────────────────

    [Fact]
    public void AlignLeft_MovesEveryNodeToTheLeftmostEdge()
    {
        var (commands, view, model) = MakeSut();
        var a = model.Add(100, 10);
        var b = model.Add(250, 60);
        Select(view, a, b);

        Run(commands, CommandCatalog.AlignLeft);

        Assert.Equal(100f, a.Position.X);
        Assert.Equal(100f, b.Position.X);
        Assert.Equal(60f,  b.Position.Y);   // the other axis is untouched
    }

    /// <summary>
    /// Align-right must use the node's <b>right edge</b>. Aligning by the top-left corner would
    /// leave nodes of different widths visibly ragged — the opposite of the point.
    /// </summary>
    [Fact]
    public void AlignRight_UsesNodeWidth_NotOrigin()
    {
        var (commands, view, model) = MakeSut();
        var wide   = model.Add(100, 0, new Vector2(200, 50));
        var narrow = model.Add(120, 60, new Vector2(80, 50));
        Select(view, wide, narrow);

        Run(commands, CommandCatalog.AlignRight);

        Assert.Equal(300f, wide.Position.X + 200f);
        Assert.Equal(300f, narrow.Position.X + 80f);
    }

    [Fact]
    public void AlignTop_AndAlignBottom_WorkOnTheOtherAxis()
    {
        var (commands, view, model) = MakeSut();
        var a = model.Add(0, 40,  new Vector2(100, 40));
        var b = model.Add(200, 300, new Vector2(100, 80));
        Select(view, a, b);

        Run(commands, CommandCatalog.AlignTop);
        Assert.Equal(40f, a.Position.Y);
        Assert.Equal(40f, b.Position.Y);

        b.Position = new Vector2(200, 300);
        Select(view, a, b);
        Run(commands, CommandCatalog.AlignBottom);
        Assert.Equal(380f, a.Position.Y + 40f);
        Assert.Equal(380f, b.Position.Y + 80f);
    }

    [Fact]
    public void AlignCenter_CentresOnTheSelectionBounds()
    {
        var (commands, view, model) = MakeSut();
        var a = model.Add(0,   0, new Vector2(100, 40));
        var b = model.Add(200, 0, new Vector2(100, 40));
        Select(view, a, b);

        Run(commands, CommandCatalog.AlignCenterH);

        // Bounds are 0..300, so both centres land on 150.
        Assert.Equal(150f, a.Position.X + 50f);
        Assert.Equal(150f, b.Position.X + 50f);
    }

    // ── Distribute ────────────────────────────────────────────────────────────

    /// <summary>
    /// Gaps are equalised between node <b>edges</b>: distributing by origin leaves wide nodes
    /// crowding their neighbours.
    /// </summary>
    [Fact]
    public void DistributeH_EqualisesTheGapsBetweenEdges()
    {
        var (commands, view, model) = MakeSut();
        var a = model.Add(0,   0, new Vector2(100, 40));
        var b = model.Add(110, 0, new Vector2(100, 40));   // deliberately crowded
        var c = model.Add(500, 0, new Vector2(100, 40));
        Select(view, a, b, c);

        Run(commands, CommandCatalog.DistributeH);

        // Span 0..600, 300 occupied, so two gaps of 150.
        Assert.Equal(0f,   a.Position.X);
        Assert.Equal(250f, b.Position.X);
        Assert.Equal(500f, c.Position.X);
    }

    /// <summary>The extremes hold still, so distributing an already-even row changes nothing.</summary>
    [Fact]
    public void Distribute_IsIdempotent()
    {
        var (commands, view, model) = MakeSut();
        var a = model.Add(0,   0, new Vector2(100, 40));
        var b = model.Add(110, 0, new Vector2(100, 40));
        var c = model.Add(500, 0, new Vector2(100, 40));
        Select(view, a, b, c);

        Run(commands, CommandCatalog.DistributeH);
        var afterFirst = b.Position;
        Run(commands, CommandCatalog.DistributeH);

        Assert.Equal(afterFirst, b.Position);
    }

    [Fact]
    public void DistributeV_NeedsThreeNodes()
    {
        var (commands, view, model) = MakeSut();
        var a = model.Add(0, 0);
        var b = model.Add(0, 500);
        Select(view, a, b);

        Assert.False(commands.Get(CommandCatalog.DistributeV)!.IsEnabled());

        Select(view, a, b, model.Add(0, 100));
        Assert.True(commands.Get(CommandCatalog.DistributeV)!.IsEnabled());
    }

    // ── Straighten ────────────────────────────────────────────────────────────

    /// <summary>
    /// Anchored on the first selected node rather than averaging, so the designer keeps control of
    /// where the row lands.
    /// </summary>
    [Fact]
    public void Straighten_PullsConnectedNodesOntoTheAnchorsRow()
    {
        var (commands, view, model) = MakeSut();
        var a = model.AddWithPins(0,   100, out var aOut, out _);
        var b = model.AddWithPins(200, 340, out var bOut, out var bIn);
        var c = model.AddWithPins(400, 20,  out _,        out var cIn);
        model.Link(aOut, bIn);
        model.Link(bOut, cIn);
        Select(view, a, b, c);

        Run(commands, CommandCatalog.StraightenConn);

        Assert.Equal(100f, a.Position.Y);
        Assert.Equal(100f, b.Position.Y);
        Assert.Equal(100f, c.Position.Y);
        Assert.Equal(200f, b.Position.X);   // horizontal layout preserved
    }

    /// <summary>
    /// A node connected only to something outside the selection says nothing about where the
    /// selection should sit, so it is left alone.
    /// </summary>
    [Fact]
    public void Straighten_IgnoresUnconnectedNodesInTheSelection()
    {
        var (commands, view, model) = MakeSut();
        var a = model.AddWithPins(0,   100, out var aOut, out _);
        var b = model.AddWithPins(200, 340, out _, out var bIn);
        var loner = model.Add(400, 999);
        model.Link(aOut, bIn);
        Select(view, a, b, loner);

        Run(commands, CommandCatalog.StraightenConn);

        Assert.Equal(100f, b.Position.Y);
        Assert.Equal(999f, loner.Position.Y);
    }

    // ── Undo ──────────────────────────────────────────────────────────────────

    [Fact]
    public void AnAlignment_IsOneUndoEntry()
    {
        var (commands, view, model) = MakeSut();
        var a = model.Add(100, 0);
        var b = model.Add(250, 0);
        var c = model.Add(400, 0);
        Select(view, a, b, c);

        Run(commands, CommandCatalog.AlignLeft);
        Assert.Equal(1, view.Undo.UndoCount);

        view.UndoLast();
        Assert.Equal(100f, a.Position.X);
        Assert.Equal(250f, b.Position.X);
        Assert.Equal(400f, c.Position.X);
    }

    /// <summary>
    /// Aligning an already-aligned selection must not cost the designer a Ctrl+Z that appears to
    /// do nothing.
    /// </summary>
    [Fact]
    public void AnAlignmentThatChangesNothing_RecordsNothing()
    {
        var (commands, view, model) = MakeSut();
        var a = model.Add(100, 0);
        var b = model.Add(100, 60);
        Select(view, a, b);

        Run(commands, CommandCatalog.AlignLeft);

        Assert.Equal(0, view.Undo.UndoCount);
    }

    [Fact]
    public void WithFewerThanTwoNodes_AlignmentIsDisabled_AndDoesNothing()
    {
        var (commands, view, model) = MakeSut();
        var a = model.Add(100, 0);
        Select(view, a);

        Assert.False(commands.Get(CommandCatalog.AlignLeft)!.IsEnabled());
        Run(commands, CommandCatalog.AlignLeft);
        Assert.Equal(0, view.Undo.UndoCount);
    }

    // ── Stubs ─────────────────────────────────────────────────────────────────

    private sealed class StubValidator : ILinkValidator
    {
        public LinkValidationResult Validate(PinId from, PinId to)
            => new(LinkValidity.Valid, null, false, null);
    }

    private sealed class StubTypeSystem : ITypeSystem
    {
        public bool TryGetTypeInfo(TypeKey key, out TypeDisplayInfo info) { info = default!; return false; }
        public Vector4 GetPinColor(TypeKey key) => default;
        public PinShape GetPinShape(TypeKey key, ContainerKind container) => default;
        public IPinDefaultValueEditor? GetDefaultEditor(TypeKey key) => null;
        public bool AreCompatible(TypeKey from, TypeKey to) => false;
        public bool IsImplicitCast(TypeKey from, TypeKey to) => false;
    }

    private sealed class StubCatalog : INodeCatalog
    {
        public IReadOnlyList<NodeCatalogEntry> All => Array.Empty<NodeCatalogEntry>();
        public IReadOnlyList<NodeCategoryDescriptor> Categories => Array.Empty<NodeCategoryDescriptor>();
        public IReadOnlyList<NodeCatalogEntry> Query(NodeSearchQuery q) => Array.Empty<NodeCatalogEntry>();
        public IReadOnlyList<NodeCatalogEntry> QueryForPinContext(PinContextQuery q) => Array.Empty<NodeCatalogEntry>();
    }

    private sealed class StubHost : IEditorHostServices
    {
        private readonly IGraphCommandSink _sink;
        public StubHost(IGraphCommandSink sink) => _sink = sink;
        public INodeCatalog NodeCatalog => new StubCatalog();
        public ITypeSystem TypeSystem => new StubTypeSystem();
        public ILinkValidator LinkValidator => new StubValidator();
        public IGraphCommandSink CommandSink => _sink;
        public IPickerRegistry Pickers => null!;
        public IClipboard Clipboard => null!;
        public IIconProvider Icons => null!;
        public IDiagnosticsSink? Diagnostics => null;
        public IDebugSession? Debug => null;
        public IInputSource Input => null!;
        public IEditorTheme Theme => null!;
    }
}
