using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using FluentAssertions;
using Fbt;
using Hrot.BTree.Editor.Debug;
using Hrot.BTree.Editor.Renderers;
using Hrot.Editor.AiShared.Debug;
using Hrot.Editor.AiShared.Selection;
using ImGuiNET;
using NodeEditor.Core.Canvas;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.BTree.Editor.Tests.Renderers;

// ---- Stubs for overlay renderer tests -------------------------------------

file sealed class StubNodeOvl : INodeModel
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

    public StubNodeOvl(NodeId id, NodeKindKey kind, Vector2 pos = default)
    {
        Id = id; Kind = kind; Position = pos;
    }
}

file sealed class StubGraphOvl : IGraphModel
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

file sealed class StubRenderCtxOvl : ICanvasRenderContext
{
    public ImDrawListPtr DrawList => default;  // null; DrawAsyncBadge must guard against this
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

    public StubRenderCtxOvl(
        StubGraphOvl graph,
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

// Fake session that returns a configurable snapshot and async history.
file sealed class FakeOverlaySession : IBTreeDebugSession
{
    private readonly BehaviorTreeStateSnapshot? _snapshot;
    private readonly IReadOnlyList<BTreeAsyncEvent> _asyncHistory;

    public FakeOverlaySession(
        BehaviorTreeStateSnapshot? snapshot,
        IReadOnlyList<BTreeAsyncEvent>? asyncHistory = null)
    {
        _snapshot     = snapshot;
        _asyncHistory = asyncHistory ?? Array.Empty<BTreeAsyncEvent>();
    }

    public bool IsAttached => true;
    public bool IsPaused   => false;
    public bool IsAnyBreakpointActive => false;
    public Breakpoint? PausedAt      => null;
    public Entity?     PausedOnEntity => null;

    public BehaviorTreeStateSnapshot? GetCurrentStateSnapshot() => _snapshot;

    public IReadOnlyList<BTreeNodeExecuted>    GetRecentNodeHistory(int max = 100) => Array.Empty<BTreeNodeExecuted>();
    public IReadOnlyList<BTreeAsyncEvent>      GetRecentAsyncHistory(int max = 100) => _asyncHistory;
    public IReadOnlyDictionary<Guid, int>?     GetAggregateCounters(Guid assetId) => null;
    public IReadOnlyList<Breakpoint>           GetBreakpoints() => Array.Empty<Breakpoint>();
    public IReadOnlyList<Entity>               GetActiveEntities(Guid assetId) => Array.Empty<Entity>();

    public bool HeatmapModeActive { get; set; }

    public void Detach()                   { }
    public void ResetAggregateCounters()   { }
    public void Continue()                 { }
    public void StepOver()                 { }
    public void StepInto()                 { }
    public void StepOut()                  { }
    public void Pause()                    { }
    public void BeginObservingAsset(Guid assetId, TraceLevel level) { }
    public void EndObservingAsset(Guid assetId) { }

    public BreakpointId SetBreakpoint(Guid assetId, Guid elementId) => default;
    public void ClearBreakpoint(BreakpointId id) { }
    public void ClearAllBreakpoints() { }

    public event Action<BTreeBreakpointHit>? OnBreakpointHit  { add { } remove { } }
    public event Action<BTreeNodeExecuted>?  OnNodeExecuted    { add { } remove { } }
    public event Action<BTreeAsyncEvent>?    OnAsyncIssued     { add { } remove { } }
    public event Action<BTreeAsyncEvent>?    OnAsyncResolved   { add { } remove { } }
    public event Action<BTreeAsyncEvent>?    OnAsyncAborted    { add { } remove { } }
    public event Action?                     OnSessionStateChanged { add { } remove { } }
}

// ---- Helpers ---------------------------------------------------------------

file static class SnapshotHelper
{
    // Creates a minimal snapshot so the renderer does not return early.
    public static BehaviorTreeStateSnapshot Make(Guid assetId, Guid? runningId = null)
        => new BehaviorTreeStateSnapshot(
            new Entity(1, 1),
            assetId,
            RunningNodeIndex: 0,
            RunningElementId: runningId,
            StackPointer: 0,
            NodeIndexStack: Array.Empty<int>(),
            StackElementIds: Array.Empty<Guid?>(),
            LocalRegisters: Array.Empty<int>(),
            AsyncHandles: Array.Empty<ulong>(),
            TreeVersion: 1u);
}

// ---- Tests -----------------------------------------------------------------

/// <summary>
/// FIX2-013: BTreeRuntimeOverlayRenderer must include the async-badge render path
/// (section 4 per design SS12.4): GetRecentAsyncHistory -> DrawAsyncBadge for each
/// Issued event.  Tests drive the production Render() path and assert the observable
/// LastRenderedAsyncBadgeNodeIds.
/// </summary>
public sealed class BTreeRuntimeOverlayRendererTests
{
    private static readonly Guid AssetId = new("aabbccdd-0000-0000-0000-000000000001");

    // ---- Basic wiring -------------------------------------------------------

    [Fact]
    public void Render_WithNoSession_DoesNotThrow()
    {
        var renderer = new BTreeRuntimeOverlayRenderer();
        var graph    = new StubGraphOvl();
        var ctx      = new StubRenderCtxOvl(graph, new HashSet<NodeId>());

        var act = () => renderer.Render(ctx);

        act.Should().NotThrow();
        renderer.LastRenderedAsyncBadgeNodeIds.Should().BeEmpty();
    }

    [Fact]
    public void Render_WithNullSnapshot_DoesNotPopulateBadges()
    {
        var renderer = new BTreeRuntimeOverlayRenderer();
        var session  = new FakeOverlaySession(snapshot: null);
        renderer.SetSession(session);

        var graph = new StubGraphOvl();
        var ctx   = new StubRenderCtxOvl(graph, new HashSet<NodeId>());

        renderer.Render(ctx);

        renderer.LastRenderedAsyncBadgeNodeIds.Should().BeEmpty();
    }

    // ---- FIX2-013: async-badge render path ----------------------------------

    [Fact]
    public void Render_WithIssuedAsyncEvent_DrawsAsyncBadge_ForMatchingNode()
    {
        var nodeVisualId = Guid.NewGuid();
        var nodeId       = new NodeId(nodeVisualId);

        var graph = new StubGraphOvl();
        graph.Add(new StubNodeOvl(nodeId, new NodeKindKey("bt.action")));

        var asyncEvents = new List<BTreeAsyncEvent>
        {
            new(new Entity(1, 1), AssetId, nodeVisualId,
                RequestId: 1, TreeVersion: 1u, BTreeAsyncPhase.Issued, SimulationTime: 0f),
        };

        var snapshot = SnapshotHelper.Make(AssetId, runningId: nodeVisualId);
        var session  = new FakeOverlaySession(snapshot, asyncEvents);

        var renderer = new BTreeRuntimeOverlayRenderer();
        renderer.SetSession(session);
        var ctx = new StubRenderCtxOvl(graph, new HashSet<NodeId> { nodeId });

        // Act -- must go through Render(), not DrawAsyncBadge() directly
        renderer.Render(ctx);

        renderer.LastRenderedAsyncBadgeNodeIds.Should().ContainSingle()
            .Which.Should().Be(nodeVisualId);
    }

    [Fact]
    public void Render_WithResolvedAsyncEvent_DoesNotDrawAsyncBadge()
    {
        var nodeVisualId = Guid.NewGuid();
        var nodeId       = new NodeId(nodeVisualId);

        var graph = new StubGraphOvl();
        graph.Add(new StubNodeOvl(nodeId, new NodeKindKey("bt.action")));

        // Resolved (no longer pending) -- should NOT produce a badge.
        var asyncEvents = new List<BTreeAsyncEvent>
        {
            new(new Entity(1, 1), AssetId, nodeVisualId,
                RequestId: 1, TreeVersion: 1u, BTreeAsyncPhase.Resolved, SimulationTime: 0f),
        };

        var snapshot = SnapshotHelper.Make(AssetId, runningId: nodeVisualId);
        var session  = new FakeOverlaySession(snapshot, asyncEvents);

        var renderer = new BTreeRuntimeOverlayRenderer();
        renderer.SetSession(session);
        var ctx = new StubRenderCtxOvl(graph, new HashSet<NodeId> { nodeId });

        renderer.Render(ctx);

        renderer.LastRenderedAsyncBadgeNodeIds.Should().BeEmpty(
            because: "only Issued (pending) events get async badges");
    }

    [Fact]
    public void Render_WithMixedAsyncEvents_DrawsBadgesOnlyForIssued()
    {
        var issuedId   = Guid.NewGuid();
        var resolvedId = Guid.NewGuid();

        var graph = new StubGraphOvl();
        graph.Add(new StubNodeOvl(new NodeId(issuedId),   new NodeKindKey("bt.action")));
        graph.Add(new StubNodeOvl(new NodeId(resolvedId), new NodeKindKey("bt.action")));

        var entity = new Entity(1, 1);
        var asyncEvents = new List<BTreeAsyncEvent>
        {
            new(entity, AssetId, issuedId,   RequestId: 1, TreeVersion: 1u, BTreeAsyncPhase.Issued,   SimulationTime: 0f),
            new(entity, AssetId, resolvedId, RequestId: 2, TreeVersion: 1u, BTreeAsyncPhase.Resolved, SimulationTime: 0f),
            new(entity, AssetId, Guid.NewGuid(), RequestId: 3, TreeVersion: 1u, BTreeAsyncPhase.Aborted,  SimulationTime: 0f),
        };

        var snapshot = SnapshotHelper.Make(AssetId);
        var session  = new FakeOverlaySession(snapshot, asyncEvents);

        var renderer = new BTreeRuntimeOverlayRenderer();
        renderer.SetSession(session);
        var ctx = new StubRenderCtxOvl(graph, new HashSet<NodeId>
        {
            new NodeId(issuedId),
            new NodeId(resolvedId),
        });

        renderer.Render(ctx);

        renderer.LastRenderedAsyncBadgeNodeIds.Should().ContainSingle()
            .Which.Should().Be(issuedId,
                because: "only the Issued event whose node exists in the graph gets a badge");
    }

    [Fact]
    public void Render_AsyncEventForDifferentAsset_IsFiltered()
    {
        var nodeVisualId  = Guid.NewGuid();
        var otherAssetId  = Guid.NewGuid();

        var graph = new StubGraphOvl();
        graph.Add(new StubNodeOvl(new NodeId(nodeVisualId), new NodeKindKey("bt.action")));

        // Event belongs to a different asset -- must be ignored.
        var asyncEvents = new List<BTreeAsyncEvent>
        {
            new(new Entity(1, 1), otherAssetId, nodeVisualId,
                RequestId: 1, TreeVersion: 1u, BTreeAsyncPhase.Issued, SimulationTime: 0f),
        };

        var snapshot = SnapshotHelper.Make(AssetId);          // snapshot uses AssetId, not otherAssetId
        var session  = new FakeOverlaySession(snapshot, asyncEvents);

        var renderer = new BTreeRuntimeOverlayRenderer();
        renderer.SetSession(session);
        var ctx = new StubRenderCtxOvl(graph, new HashSet<NodeId> { new NodeId(nodeVisualId) });

        renderer.Render(ctx);

        renderer.LastRenderedAsyncBadgeNodeIds.Should().BeEmpty(
            because: "events from other assets are filtered by AssetId comparison");
    }

    [Fact]
    public void Render_ResetsAsyncBadgeList_OnEachCall()
    {
        var nodeVisualId = Guid.NewGuid();
        var nodeId       = new NodeId(nodeVisualId);

        var graph = new StubGraphOvl();
        graph.Add(new StubNodeOvl(nodeId, new NodeKindKey("bt.action")));

        var asyncEvents = new List<BTreeAsyncEvent>
        {
            new(new Entity(1, 1), AssetId, nodeVisualId,
                RequestId: 1, TreeVersion: 1u, BTreeAsyncPhase.Issued, SimulationTime: 0f),
        };

        var snapshot = SnapshotHelper.Make(AssetId);
        var renderer = new BTreeRuntimeOverlayRenderer();
        renderer.SetSession(new FakeOverlaySession(snapshot, asyncEvents));
        var ctx = new StubRenderCtxOvl(graph, new HashSet<NodeId> { nodeId });

        renderer.Render(ctx);
        renderer.LastRenderedAsyncBadgeNodeIds.Should().HaveCount(1);

        // Second render with an empty-snapshot session -- list should be cleared.
        renderer.SetSession(new FakeOverlaySession(snapshot: null));
        renderer.Render(ctx);

        renderer.LastRenderedAsyncBadgeNodeIds.Should().BeEmpty(
            because: "LastRenderedAsyncBadgeNodeIds is reset at the start of each Render() call");
    }
}
