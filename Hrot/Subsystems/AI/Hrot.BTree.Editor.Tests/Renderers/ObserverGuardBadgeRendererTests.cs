using System;
using System.Collections.Generic;
using System.Numerics;
using FluentAssertions;
using Hrot.BTree.Editor.Host;
using Hrot.BTree.Editor.Renderers;
using ImGuiNET;
using NodeEditor.Core.Canvas;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.BTree.Editor.Tests.Renderers;

public sealed class ObserverGuardBadgeRendererTests
{
    // ---- Stubs --------------------------------------------------------------

    private sealed class StubNodeKind
    {
        internal readonly NodeKindKey Key;
        internal StubNodeKind(string id) => Key = new NodeKindKey(id);
    }

    private sealed class StubNode : INodeModel
    {
        public NodeId Id { get; }
        public NodeKindKey Kind { get; }
        public string Title => string.Empty;
        public string? Subtitle => null;
        public NodeCategory Category => NodeCategory.Function;
        public Vector2 Position { get; set; }
        public Vector2? SizeOverride => null;
        public NodeState State => NodeState.Normal;
        public string? StatusTooltip => null;
        public bool IsCollapsed { get; set; }
        public bool ShowAdvancedPins => false;
        public IReadOnlyList<IPinModel> Pins => Array.Empty<IPinModel>();

        public StubNode(NodeId id, NodeKindKey kind, Vector2 pos = default)
        {
            Id = id; Kind = kind; Position = pos;
        }
    }

    private sealed class StubPin : IPinModel
    {
        public PinId Id { get; }
        public NodeId OwnerNodeId { get; }
        public string Label => string.Empty;
        public PinDirection Direction { get; }
        public PinKind Kind => PinKind.Exec;
        public TypeKey? Type => null;
        public PinShape Shape => PinShape.Circle;
        public bool IsAdvanced => false;
        public bool IsOptional => false;
        public string? Tooltip => null;
        public IPinDefaultValue? Default => null;

        public StubPin(PinId id, NodeId owner, PinDirection dir)
        {
            Id = id; OwnerNodeId = owner; Direction = dir;
        }
    }

    private sealed class StubLink : ILinkModel
    {
        public LinkId Id { get; }
        public PinId FromPin { get; }
        public PinId ToPin { get; }
        public LinkStyle Style => LinkStyle.Solid;
        public System.Collections.Generic.IReadOnlyList<Vector2> Waypoints => Array.Empty<Vector2>();

        public StubLink(LinkId id, PinId from, PinId to)
        {
            Id = id; FromPin = from; ToPin = to;
        }
    }

    private sealed class StubGraph : IGraphModel
    {
        private readonly Dictionary<NodeId, INodeModel> _nodes = new();
        private readonly Dictionary<PinId,  IPinModel>  _pins  = new();
        private readonly Dictionary<LinkId, ILinkModel> _links = new();

        public GraphId Id => GraphId.NewId();
        public string DisplayName => "stub";
        public GraphKindDescriptor Kind => new("stub", "stub", false, false);
        public IReadOnlyCollection<INodeModel>    Nodes    => _nodes.Values;
        public IReadOnlyCollection<ILinkModel>    Links    => _links.Values;
        public IReadOnlyCollection<ICommentModel> Comments => Array.Empty<ICommentModel>();

#pragma warning disable CS0067
        public event Action<GraphChangeNotification>? Changed;
#pragma warning restore CS0067

        public void Add(INodeModel n) => _nodes[n.Id] = n;
        public void Add(IPinModel p)  => _pins[p.Id]  = p;
        public void Add(ILinkModel l) => _links[l.Id] = l;

        public INodeModel?  FindNode(NodeId id) => _nodes.GetValueOrDefault(id);
        public IPinModel?   FindPin(PinId id)   => _pins.GetValueOrDefault(id);
        public ILinkModel?  FindLink(LinkId id) => _links.GetValueOrDefault(id);
    }

    private sealed class StubRenderCtx : ICanvasRenderContext
    {
        private readonly IReadOnlySet<LinkId> _visibleLinks;

        public ImDrawListPtr DrawList => default;  // null draw list; badges must guard against this
        public ViewportState Viewport { get; } = new ViewportState();
        public CanvasRenderPass Pass => CanvasRenderPass.AfterWires;
        public IEditorTheme Theme => null!;
        public IGraphModel Graph { get; }
        public SelectionState Selection { get; } = new SelectionState();
        public IReadOnlySet<NodeId> VisibleNodes { get; } = new HashSet<NodeId>();
        public IReadOnlySet<LinkId> VisibleLinks => _visibleLinks;
        public float Zoom { get; }
        public bool IsLowZoom { get; }
        public IDebugSession? DebugSession => null;
        public IDictionary<string, object?> FrameScratch { get; } = new Dictionary<string, object?>();

        public Vector2 CanvasToScreen(Vector2 p) => p;
        public Vector2 ScreenToCanvas(Vector2 p) => p;
        public RectF CanvasToScreen(RectF r) => r;

        public StubRenderCtx(StubGraph graph, IReadOnlySet<LinkId> visibleLinks, float zoom = 1f, bool isLowZoom = false)
        {
            Graph = graph;
            _visibleLinks = visibleLinks;
            Zoom = zoom;
            IsLowZoom = isLowZoom;
        }
    }

    // ---- Helpers ------------------------------------------------------------

    // Builds a graph with one link from a fromNode to a toNode.
    // fromKind -> childNode (fromPin is output of fromNode)
    // toKind   -> parentNode (toPin is input of toNode)
    private static (StubGraph graph, LinkId linkId) BuildGraph(string parentKind, string childKind)
    {
        var graph = new StubGraph();

        var parentId = NodeId.NewId();
        var childId  = NodeId.NewId();
        var parent   = new StubNode(parentId, new NodeKindKey(parentKind), new Vector2(0, 0));
        var child    = new StubNode(childId,  new NodeKindKey(childKind),  new Vector2(100, 100));
        graph.Add(parent);
        graph.Add(child);

        // BTree convention: FromPin = output of child, ToPin = input of parent.
        var fromPin = new StubPin(new PinId(Guid.NewGuid()), childId,  PinDirection.Output);
        var toPin   = new StubPin(new PinId(Guid.NewGuid()), parentId, PinDirection.Input);
        graph.Add(fromPin);
        graph.Add(toPin);

        var linkId = LinkId.NewId();
        graph.Add(new StubLink(linkId, fromPin.Id, toPin.Id));

        return (graph, linkId);
    }

    // ---- Tests --------------------------------------------------------------

    [Fact]
    public void Render_with_observer_selector_parent_and_condition_child_emits_one_badge()
    {
        var (graph, linkId) = BuildGraph(BTreeKinds.ObserverSelector, BTreeKinds.Condition);
        var visibleLinks = new HashSet<LinkId> { linkId };
        var ctx      = new StubRenderCtx(graph, visibleLinks);
        var renderer = new ObserverGuardBadgeRenderer();

        renderer.Render(ctx);

        renderer.LastRenderBadgeCount.Should().Be(1);
    }

    [Fact]
    public void Render_with_non_observer_parent_emits_no_badge()
    {
        // Sequence parent, Condition child -> not an observer guard link
        var (graph, linkId) = BuildGraph(BTreeKinds.Sequence, BTreeKinds.Condition);
        var visibleLinks = new HashSet<LinkId> { linkId };
        var ctx      = new StubRenderCtx(graph, visibleLinks);
        var renderer = new ObserverGuardBadgeRenderer();

        renderer.Render(ctx);

        renderer.LastRenderBadgeCount.Should().Be(0);
    }

    [Fact]
    public void Render_suppressed_at_low_zoom()
    {
        var (graph, linkId) = BuildGraph(BTreeKinds.ObserverSelector, BTreeKinds.Condition);
        var visibleLinks = new HashSet<LinkId> { linkId };
        var ctx      = new StubRenderCtx(graph, visibleLinks, zoom: 0.2f, isLowZoom: true);
        var renderer = new ObserverGuardBadgeRenderer();

        renderer.Render(ctx);

        renderer.LastRenderBadgeCount.Should().Be(0);
    }
}
