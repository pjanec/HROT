using System;
using System.Collections.Generic;
using System.Numerics;
using Fbt;
using FluentAssertions;
using Hrot.BTree.Editor.Host;
using Hrot.BTree.Editor.Model;
using Hrot.BTree.Editor.Renderers;
using Hrot.Editor.AiShared.Selection;
using ImGuiNET;
using NodeEditor.Core.Canvas;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.BTree.Editor.Tests.Renderers;

// ---- Stubs ------------------------------------------------------------------

file sealed class StubNodeVB : INodeModel
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

    public StubNodeVB(NodeId id, NodeKindKey kind, Vector2 pos = default)
    {
        Id = id; Kind = kind; Position = pos;
    }
}

file sealed class StubGraphVB : IGraphModel
{
    private readonly Dictionary<NodeId, INodeModel> _nodes = new();

    public GraphId Id => GraphId.NewId();
    public string DisplayName => "stub";
    public GraphKindDescriptor Kind => new("stub", "stub", false, false);
    public IReadOnlyCollection<INodeModel>    Nodes    => _nodes.Values;
    public IReadOnlyCollection<ILinkModel>    Links    => Array.Empty<ILinkModel>();
    public IReadOnlyCollection<ICommentModel> Comments => Array.Empty<ICommentModel>();

#pragma warning disable CS0067
    public event Action<GraphChangeNotification>? Changed;
#pragma warning restore CS0067

    public void Add(INodeModel n) => _nodes[n.Id] = n;

    public INodeModel?  FindNode(NodeId id) => _nodes.GetValueOrDefault(id);
    public IPinModel?   FindPin(PinId id)   => null;
    public ILinkModel?  FindLink(LinkId id) => null;
}

file sealed class StubRenderCtxVB : ICanvasRenderContext
{
    public ImDrawListPtr DrawList => default;  // null draw list; badges must guard against this
    public ViewportState Viewport { get; } = new ViewportState();
    public CanvasRenderPass Pass => CanvasRenderPass.AfterNodes;
    public IEditorTheme Theme => null!;
    public IGraphModel Graph { get; }
    public SelectionState Selection { get; } = new SelectionState();
    public IReadOnlySet<NodeId> VisibleNodes { get; }
    public IReadOnlySet<LinkId> VisibleLinks { get; } = new HashSet<LinkId>();
    public float Zoom { get; }
    public bool IsLowZoom { get; }
    public IDebugSession? DebugSession => null;
    public IDictionary<string, object?> FrameScratch { get; } = new Dictionary<string, object?>();

    public Vector2 CanvasToScreen(Vector2 p) => p;
    public Vector2 ScreenToCanvas(Vector2 p) => p;
    public RectF CanvasToScreen(RectF r) => r;
    public bool TryGetNodeScreenRect(NodeId id, out RectF screenRect) { screenRect = default; return false; }
    public bool TryGetPinScreenPosition(PinId id, out Vector2 screenPos) { screenPos = default; return false; }

    public StubRenderCtxVB(
        StubGraphVB graph,
        IReadOnlySet<NodeId> visibleNodes,
        float zoom = 1f,
        bool isLowZoom = false)
    {
        Graph        = graph;
        VisibleNodes = visibleNodes;
        Zoom         = zoom;
        IsLowZoom    = isLowZoom;
    }
}

// ---- Helpers ----------------------------------------------------------------

file static class BTreeAssetHelper
{
    public static BehaviorTreeAsset MakeAsset() =>
        new BehaviorTreeAsset(
            Guid.NewGuid(),
            "TestTree",
            "/trees/TestTree.cs",
            true,
            "MyBlackboard",
            "MyContext",
            new BehaviorTreeBlob
            {
                TreeName        = "test",
                Nodes           = Array.Empty<NodeDefinition>(),
                MethodNames     = Array.Empty<string>(),
                FloatParams     = Array.Empty<float>(),
                IntParams       = Array.Empty<int>(),
                SubtreeAssetIds = Array.Empty<string>(),
            });
}

// ---- Tests ------------------------------------------------------------------

public sealed class VariableBindingBadgeRendererTests
{
    // ---- BB-1a-06: renderer metadata ----------------------------------------

    [Fact]
    public void Id_is_btree_variable_binding_badges()
    {
        var renderer = new VariableBindingBadgeRenderer(new EditorSelectionStore());
        renderer.Id.Should().Be("btree.variable_binding_badges");
    }

    [Fact]
    public void Pass_is_AfterNodes()
    {
        var renderer = new VariableBindingBadgeRenderer(new EditorSelectionStore());
        renderer.Pass.Should().Be(CanvasRenderPass.AfterNodes);
    }

    // ---- BB-1a-06: isLowZoom skips rendering --------------------------------

    [Fact]
    public void Render_at_low_zoom_skips_all_badges()
    {
        var store    = new EditorSelectionStore();
        var renderer = new VariableBindingBadgeRenderer(store);
        var graph    = new StubGraphVB();
        var nodeId   = NodeId.NewId();
        graph.Add(new StubNodeVB(nodeId, new NodeKindKey(BTreeKinds.Action)));
        var ctx = new StubRenderCtxVB(graph, new HashSet<NodeId> { nodeId }, isLowZoom: true);

        renderer.Render(ctx);

        renderer.LastRenderBadgeCount.Should().Be(0);
    }

    // ---- BB-1a-06: non-leaf nodes are skipped --------------------------------

    [Fact]
    public void Render_non_action_non_condition_node_not_counted()
    {
        var store    = new EditorSelectionStore();
        var renderer = new VariableBindingBadgeRenderer(store);
        var graph    = new StubGraphVB();
        var nodeId   = NodeId.NewId();
        graph.Add(new StubNodeVB(nodeId, new NodeKindKey("bt.composite.sequence")));
        var ctx = new StubRenderCtxVB(graph, new HashSet<NodeId> { nodeId });

        renderer.Render(ctx);

        renderer.LastRenderBadgeCount.Should().Be(0);
    }

    // ---- BB-1a-06: action node with binding counts as one badge -------------

    [Fact]
    public void Render_action_node_with_binding_produces_one_badge()
    {
        var store    = new EditorSelectionStore();
        var asset    = BTreeAssetHelper.MakeAsset();
        var visualId = Guid.NewGuid();
        asset.AddNode(new BTreeEditorNode
        {
            VisualId    = visualId,
            KernelType  = NodeType.Action,
            Action      = new BTreeActionPayload { ExpressionTargetField = "myVar" },
        });
        store.ActiveAsset = asset;

        var renderer = new VariableBindingBadgeRenderer(store);
        var graph    = new StubGraphVB();
        var nodeId   = new NodeId(visualId);
        graph.Add(new StubNodeVB(nodeId, new NodeKindKey(BTreeKinds.Action)));
        var ctx = new StubRenderCtxVB(graph, new HashSet<NodeId> { nodeId });

        renderer.Render(ctx);

        renderer.LastRenderBadgeCount.Should().Be(1);
    }

    // ---- BB-1a-06: condition node with binding counts -------------------------

    [Fact]
    public void Render_condition_node_with_binding_produces_one_badge()
    {
        var store    = new EditorSelectionStore();
        var asset    = BTreeAssetHelper.MakeAsset();
        var visualId = Guid.NewGuid();
        asset.AddNode(new BTreeEditorNode
        {
            VisualId    = visualId,
            KernelType  = NodeType.Condition,
            Condition   = new BTreeConditionPayload { ExpressionTargetField = "condVar" },
        });
        store.ActiveAsset = asset;

        var renderer = new VariableBindingBadgeRenderer(store);
        var graph    = new StubGraphVB();
        var nodeId   = new NodeId(visualId);
        graph.Add(new StubNodeVB(nodeId, new NodeKindKey(BTreeKinds.Condition)));
        var ctx = new StubRenderCtxVB(graph, new HashSet<NodeId> { nodeId });

        renderer.Render(ctx);

        renderer.LastRenderBadgeCount.Should().Be(1);
    }

    // ---- BB-1a-06: action node WITHOUT binding still counts (shows unbound badge)

    [Fact]
    public void Render_action_node_without_binding_still_counted()
    {
        var store    = new EditorSelectionStore();
        var asset    = BTreeAssetHelper.MakeAsset();
        var visualId = Guid.NewGuid();
        asset.AddNode(new BTreeEditorNode
        {
            VisualId   = visualId,
            KernelType = NodeType.Action,
            Action     = new BTreeActionPayload { ExpressionTargetField = null },
        });
        store.ActiveAsset = asset;

        var renderer = new VariableBindingBadgeRenderer(store);
        var graph    = new StubGraphVB();
        var nodeId   = new NodeId(visualId);
        graph.Add(new StubNodeVB(nodeId, new NodeKindKey(BTreeKinds.Action)));
        var ctx = new StubRenderCtxVB(graph, new HashSet<NodeId> { nodeId });

        renderer.Render(ctx);

        renderer.LastRenderBadgeCount.Should().Be(1);
    }

    // ---- BB-1a-06: multiple mixed visible nodes -----------------------------

    [Fact]
    public void Render_three_nodes_two_are_action_two_badges()
    {
        var store = new EditorSelectionStore();
        store.ActiveAsset = null;   // no asset; all nodes show as unbound

        var renderer = new VariableBindingBadgeRenderer(store);
        var graph    = new StubGraphVB();

        var actionId1   = NodeId.NewId();
        var actionId2   = NodeId.NewId();
        var sequenceId  = NodeId.NewId();
        graph.Add(new StubNodeVB(actionId1,  new NodeKindKey(BTreeKinds.Action)));
        graph.Add(new StubNodeVB(actionId2,  new NodeKindKey(BTreeKinds.Action)));
        graph.Add(new StubNodeVB(sequenceId, new NodeKindKey("bt.composite.sequence")));

        var ctx = new StubRenderCtxVB(graph, new HashSet<NodeId> { actionId1, actionId2, sequenceId });

        renderer.Render(ctx);

        renderer.LastRenderBadgeCount.Should().Be(2);
    }

    // ---- BB-1a-06: empty VisibleNodes is safe -------------------------------

    [Fact]
    public void Render_no_visible_nodes_zero_badges()
    {
        var renderer = new VariableBindingBadgeRenderer(new EditorSelectionStore());
        var graph    = new StubGraphVB();
        var ctx      = new StubRenderCtxVB(graph, new HashSet<NodeId>());

        renderer.Render(ctx);

        renderer.LastRenderBadgeCount.Should().Be(0);
    }
}
