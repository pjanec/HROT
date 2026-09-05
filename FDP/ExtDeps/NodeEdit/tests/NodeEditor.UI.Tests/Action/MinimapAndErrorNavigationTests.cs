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
using NodeEditor.UI.Canvas;
using Xunit;

namespace NodeEditor.UI.Tests.Action;

/// <summary>
/// BP-19 (minimap geometry) and BP-20 (jump to next/previous problem).
///
/// <para>
/// Both were declared-but-never-registered: <c>editor.toggle-minimap</c>, <c>editor.next-error</c>
/// and <c>editor.prev-error</c> existed in <c>CommandCatalog</c> with no implementation. The
/// ingredients were already present — <c>ViewportState</c> supplies the transform maths, and
/// <c>NodeState.Error</c>/<c>Warning</c> are set by the host and painted by the canvas.
/// </para>
///
/// <para>
/// The minimap's drawing needs an ImGui context; its <b>geometry</b> does not, and that is where the
/// bugs would live, so that is what is tested here.
/// </para>
/// </summary>
public sealed class MinimapAndErrorNavigationTests
{
    private sealed class StubNode : INodeModel
    {
        public NodeId       Id       { get; init; } = IdGenerator.NewNodeId();
        public NodeKindKey  Kind     => new("stub");
        public string       Title    => "Stub";
        public string?      Subtitle => null;
        public NodeCategory Category => NodeCategory.Function;
        public Vector2      Position { get; init; }
        public Vector2?     SizeOverride { get; init; }
        public NodeState    State    { get; init; } = NodeState.Normal;
        public string?      StatusTooltip     => null;
        public bool         IsCollapsed       => false;
        public bool         ShowAdvancedPins  => false;
        public NodeId?      ParentContainerId => null;
        public IReadOnlyList<IPinModel> Pins => Array.Empty<IPinModel>();
    }

    private sealed class Model : IGraphModel
    {
        private readonly List<INodeModel> _nodes = new();
        public GraphId Id => GraphId.Empty;
        public string DisplayName => "test";
        public GraphKindDescriptor Kind => new("test", "Test", false, false);
        public IReadOnlyCollection<INodeModel>    Nodes    => _nodes;
        public IReadOnlyCollection<ILinkModel>    Links    => Array.Empty<ILinkModel>();
        public IReadOnlyCollection<ICommentModel> Comments => Array.Empty<ICommentModel>();
        public INodeModel? FindNode(NodeId id) => _nodes.FirstOrDefault(n => n.Id == id);
        public IPinModel?  FindPin(PinId id)   => null;
        public ILinkModel? FindLink(LinkId id) => null;
        public event Action<GraphChangeNotification>? Changed { add { } remove { } }

        public StubNode Add(float x, float y, NodeState state = NodeState.Normal, Vector2? size = null)
        {
            var n = new StubNode { Position = new Vector2(x, y), State = state, SizeOverride = size };
            _nodes.Add(n);
            return n;
        }
    }

    private static (EditorCommandsImpl Commands, GraphView View, Model Model) MakeSut()
    {
        var model = new Model();
        var sink  = new StubSink();
        var view  = new GraphView(model, sink, new StubValidator(), new StubTypeSystem(),
                                  new StubCatalog(), new StubHost(sink));
        view.Viewport.CanvasScreenOrigin = Vector2.Zero;
        view.Viewport.CanvasScreenSize   = new Vector2(800, 600);

        var commands = new EditorCommandsImpl();
        ViewCommands.Register(commands, view);
        ErrorNavigationCommands.Register(commands, view);
        return (commands, view, model);
    }

    // ── BP-19: minimap ────────────────────────────────────────────────────────

    [Fact]
    public void ToggleMinimap_IsRegistered_AndFlipsTheFlag()
    {
        var (commands, view, _) = MakeSut();

        Assert.NotNull(commands.Get(CommandCatalog.ToggleMinimap));
        Assert.False(view.Viewport.ShowMinimap);

        commands.Invoke(CommandCatalog.ToggleMinimap);
        Assert.True(view.Viewport.ShowMinimap);

        commands.Invoke(CommandCatalog.ToggleMinimap);
        Assert.False(view.Viewport.ShowMinimap);
    }

    /// <summary>Bounds must include each node's drawn size, not just its origin.</summary>
    [Fact]
    public void GraphBounds_CoverEveryNodesFullExtent()
    {
        var (_, view, model) = MakeSut();
        model.Add(100, 100, size: new Vector2(200, 50));
        model.Add(-40, 300, size: new Vector2(80, 40));

        var bounds = MinimapRenderer.GraphBounds(view);

        Assert.Equal(-40f, bounds.Min.X);
        Assert.Equal(100f, bounds.Min.Y);
        Assert.Equal(340f, bounds.Size.X);   // -40 … 300
        Assert.Equal(240f, bounds.Size.Y);   // 100 … 340
    }

    [Fact]
    public void GraphBounds_OfAnEmptyGraph_IsDegenerateRatherThanInfinite()
    {
        var (_, view, _) = MakeSut();

        var bounds = MinimapRenderer.GraphBounds(view);

        Assert.Equal(Vector2.Zero, bounds.Min);
        Assert.Equal(Vector2.Zero, bounds.Size);
    }

    [Fact]
    public void VisibleRect_TracksThePanAndZoom()
    {
        var (_, view, _) = MakeSut();

        var atRest = MinimapRenderer.VisibleRect(view);
        Assert.Equal(800f, atRest.Size.X);
        Assert.Equal(600f, atRest.Size.Y);

        view.Viewport.SetZoom(2f);
        var zoomed = MinimapRenderer.VisibleRect(view);
        Assert.Equal(400f, zoomed.Size.X);   // half the graph-space width at 2×
    }

    /// <summary>
    /// The viewport rectangle must stay inside the minimap even when the user has panned away from
    /// every node, which is exactly when a minimap earns its keep.
    /// </summary>
    [Fact]
    public void TheMappedRegion_AlwaysContainsTheVisibleRect()
    {
        var (_, view, model) = MakeSut();
        model.Add(0, 0, size: new Vector2(100, 40));
        view.Viewport.Pan(new Vector2(-5000, -5000));

        var union   = MinimapRenderer.Union(MinimapRenderer.GraphBounds(view),
                                            MinimapRenderer.VisibleRect(view));
        var visible = MinimapRenderer.VisibleRect(view);

        Assert.True(union.Min.X <= visible.Min.X);
        Assert.True(union.Min.Y <= visible.Min.Y);
        Assert.True(union.Min.X + union.Size.X >= visible.Min.X + visible.Size.X);
        Assert.True(union.Min.Y + union.Size.Y >= visible.Min.Y + visible.Size.Y);
    }

    // ── BP-20: next / previous issue ──────────────────────────────────────────

    [Theory]
    [InlineData("editor.next-error")]
    [InlineData("editor.prev-error")]
    public void ErrorNavigation_IsRegistered(string commandId)
    {
        Assert.NotNull(MakeSut().Commands.Get(commandId));
    }

    [Fact]
    public void WithNoProblems_TheCommandsAreDisabled()
    {
        var (commands, _, model) = MakeSut();
        model.Add(0, 0);

        Assert.False(commands.Get(CommandCatalog.NextError)!.IsEnabled());
    }

    [Fact]
    public void NextIssue_SelectsAndCentresTheFirstProblem()
    {
        var (commands, view, model) = MakeSut();
        model.Add(0, 0);
        var bad = model.Add(2000, 1500, NodeState.Error, new Vector2(160, 64));

        commands.Invoke(CommandCatalog.NextError);

        Assert.Equal(new[] { bad.Id }, view.Selection.Nodes.ToArray());
        // Centred: the node's middle now sits at the canvas centre.
        var centre = view.Viewport.ScreenToGraph(new Vector2(400, 300));
        Assert.Equal(2080f, centre.X, 0);
        Assert.Equal(1532f, centre.Y, 0);
    }

    /// <summary>
    /// Errors before warnings: the first press on a broken graph must land on something that
    /// actually stops the build.
    /// </summary>
    [Fact]
    public void Errors_AreVisitedBeforeWarnings()
    {
        var (commands, view, model) = MakeSut();
        model.Add(0, 0, NodeState.Warning);
        var error = model.Add(500, 0, NodeState.Error);

        commands.Invoke(CommandCatalog.NextError);

        Assert.Equal(new[] { error.Id }, view.Selection.Nodes.ToArray());
    }

    [Fact]
    public void RepeatedPresses_CycleAndWrap()
    {
        var (commands, view, model) = MakeSut();
        var a = model.Add(0,   0, NodeState.Error);
        var b = model.Add(500, 0, NodeState.Error);

        commands.Invoke(CommandCatalog.NextError);
        Assert.Equal(a.Id, view.Selection.Nodes.Single());

        commands.Invoke(CommandCatalog.NextError);
        Assert.Equal(b.Id, view.Selection.Nodes.Single());

        commands.Invoke(CommandCatalog.NextError);
        Assert.Equal(a.Id, view.Selection.Nodes.Single());
    }

    [Fact]
    public void PreviousIssue_WalksTheOtherWay()
    {
        var (commands, view, model) = MakeSut();
        var a = model.Add(0,   0, NodeState.Error);
        var b = model.Add(500, 0, NodeState.Error);

        commands.Invoke(CommandCatalog.PrevError);
        Assert.Equal(b.Id, view.Selection.Nodes.Single());

        commands.Invoke(CommandCatalog.PrevError);
        Assert.Equal(a.Id, view.Selection.Nodes.Single());
    }

    /// <summary>
    /// Anchoring on the selection rather than a stored cursor keeps the sequence right after the
    /// user clicks elsewhere — a stored index would resume from a stale position.
    /// </summary>
    [Fact]
    public void TheSequenceFollowsTheSelection_NotAStoredCursor()
    {
        var (commands, view, model) = MakeSut();
        var a = model.Add(0,    0, NodeState.Error);
        var b = model.Add(500,  0, NodeState.Error);
        var c = model.Add(1000, 0, NodeState.Error);

        view.Selection.ReplaceWith(SelectionEntry.OfNode(b.Id));
        commands.Invoke(CommandCatalog.NextError);

        Assert.Equal(c.Id, view.Selection.Nodes.Single());
        Assert.NotEqual(a.Id, view.Selection.Nodes.Single());
    }

    // ── Stubs ─────────────────────────────────────────────────────────────────

    private sealed class StubSink : IGraphCommandSink
    {
        public GraphCommandResult Apply(GraphCommand command) => new(true, null);
    }

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
