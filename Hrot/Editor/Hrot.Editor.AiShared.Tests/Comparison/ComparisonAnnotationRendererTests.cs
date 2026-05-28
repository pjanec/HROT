using System;
using System.Collections.Generic;
using System.Numerics;
using Hrot.Editor.AiShared.Comparison;
using Hrot.Editor.AiShared.Comparison.Rendering;
using ImGuiNET;
using NodeEditor.Core.Canvas;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.Primitives;

namespace Hrot.Editor.AiShared.Tests.Comparison;

// ---- Stubs ------------------------------------------------------------------

file sealed class StubNodeAR : INodeModel
{
    public NodeId Id { get; }
    public NodeKindKey Kind => new("stub");
    public string Title { get; }
    public string? Subtitle { get; }
    public NodeCategory Category => NodeCategory.Function;
    public Vector2 Position { get; set; }
    public Vector2? SizeOverride => null;
    public NodeState State => NodeState.Normal;
    public string? StatusTooltip => null;
    public bool IsCollapsed => false;
    public bool ShowAdvancedPins => false;
    public IReadOnlyList<IPinModel> Pins => Array.Empty<IPinModel>();

    public StubNodeAR(NodeId id, string title = "stub", string? subtitle = null)
    {
        Id = id;
        Title = title;
        Subtitle = subtitle;
    }
}

file sealed class StubGraphAR : IGraphModel
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

    public void Add(INodeModel node) => _nodes[node.Id] = node;

    public INodeModel?  FindNode(NodeId id) => _nodes.GetValueOrDefault(id);
    public IPinModel?   FindPin(PinId id)   => null;
    public ILinkModel?  FindLink(LinkId id) => null;
}

file sealed class StubRenderCtxAR : ICanvasRenderContext
{
    public ImDrawListPtr DrawList => default;  // null; renderers must guard against this
    public ViewportState Viewport { get; } = new ViewportState();
    public CanvasRenderPass Pass => CanvasRenderPass.AfterNodes;
    public IEditorTheme Theme => null!;
    public IGraphModel Graph { get; }
    public SelectionState Selection { get; } = new SelectionState();
    public IReadOnlySet<NodeId> VisibleNodes { get; } = new HashSet<NodeId>();
    public IReadOnlySet<LinkId> VisibleLinks { get; } = new HashSet<LinkId>();
    public float Zoom => 1f;
    public bool IsLowZoom => false;
    public IDebugSession? DebugSession => null;
    public IDictionary<string, object?> FrameScratch { get; } = new Dictionary<string, object?>();

    public Vector2 CanvasToScreen(Vector2 p) => p;
    public Vector2 ScreenToCanvas(Vector2 p) => p;
    public RectF CanvasToScreen(RectF r) => r;

    public StubRenderCtxAR(StubGraphAR graph) => Graph = graph;
}

// ---- Helpers ----------------------------------------------------------------

file static class AR_TestHelper
{
    public static ComparisonResponse MakeResponse(params ComparisonChange[] changes) =>
        new ComparisonResponse(null, "Summary.", changes, Array.Empty<string>());

    public static ComparisonChange MakeChange(
        string kind,
        string? elementId,
        string severity = "behavior",
        string? oldValue = null,
        string? newValue = null) =>
        new ComparisonChange(kind, elementId, "desc", null, oldValue, newValue, severity, "detail");

    public static ComparisonSessionState MakeSession(
        Guid assetId,
        ComparisonResponse response,
        bool enableCosmetic = false)
    {
        var session = new ComparisonSessionState(assetId, response);
        if (enableCosmetic)
            session.ToggleSeverity("cosmetic"); // cosmetic is off by default; toggle on
        return session;
    }
}

// ---- Tests ------------------------------------------------------------------

public sealed class ComparisonAnnotationRendererTests
{
    // ---- C-21: Severity filter applied --------------------------------------

    [Fact]
    public void SeverityFilter_CosmeticDisabled_OnlyBehaviorChangeAnnotated()
    {
        var assetId = Guid.NewGuid();
        var guidA   = Guid.NewGuid();
        var guidB   = Guid.NewGuid();

        var graph = new StubGraphAR();
        graph.Add(new StubNodeAR(new NodeId(guidA)));
        graph.Add(new StubNodeAR(new NodeId(guidB)));

        var response = AR_TestHelper.MakeResponse(
            AR_TestHelper.MakeChange("node_modified", guidA.ToString(), severity: "cosmetic"),
            AR_TestHelper.MakeChange("node_modified", guidB.ToString(), severity: "behavior"));

        var registry = new ComparisonSessionRegistry();
        // cosmetic is NOT enabled by default
        registry.SetSession(new ComparisonSessionState(assetId, response));

        var renderer = new ComparisonAnnotationRenderer(registry);
        renderer.SetActiveAsset(assetId);
        renderer.Render(new StubRenderCtxAR(graph));

        var annotation = Assert.Single(renderer.LastFrameAnnotations);
        Assert.Equal("behavior", annotation.Severity);
    }

    // ---- C-21: Missing node skipped -----------------------------------------

    [Fact]
    public void MissingNode_ValidGuidNotInGraph_AnnotationSkipped()
    {
        var assetId   = Guid.NewGuid();
        var missingId = Guid.NewGuid().ToString(); // valid GUID not in graph

        var response = AR_TestHelper.MakeResponse(
            AR_TestHelper.MakeChange("node_modified", missingId));

        var registry = new ComparisonSessionRegistry();
        registry.SetSession(new ComparisonSessionState(assetId, response));

        var renderer = new ComparisonAnnotationRenderer(registry);
        renderer.SetActiveAsset(assetId);
        renderer.Render(new StubRenderCtxAR(new StubGraphAR()));

        Assert.Empty(renderer.LastFrameAnnotations);
    }

    // ---- C-21: connection_changed both endpoints exist ----------------------

    [Fact]
    public void ConnectionChanged_BothEndpointsExist_PlacementIsEdgeMidpoint()
    {
        var assetId = Guid.NewGuid();
        var guidA   = Guid.NewGuid();
        var guidB   = Guid.NewGuid();

        var graph = new StubGraphAR();
        graph.Add(new StubNodeAR(new NodeId(guidA)));
        graph.Add(new StubNodeAR(new NodeId(guidB)));

        var elementId = $"{guidA}->{guidB}";
        var response = AR_TestHelper.MakeResponse(
            AR_TestHelper.MakeChange("connection_changed", elementId));

        var registry = new ComparisonSessionRegistry();
        registry.SetSession(new ComparisonSessionState(assetId, response));

        var renderer = new ComparisonAnnotationRenderer(registry);
        renderer.SetActiveAsset(assetId);
        renderer.Render(new StubRenderCtxAR(graph));

        var annotation = Assert.Single(renderer.LastFrameAnnotations);
        Assert.Equal(AnnotationPlacement.EdgeMidpoint, annotation.Placement);
    }

    // ---- C-21: connection_changed one endpoint missing ----------------------

    [Fact]
    public void ConnectionChanged_OneEndpointMissing_PlacementIsSurvivingEndpoint()
    {
        var assetId = Guid.NewGuid();
        var guidA   = Guid.NewGuid();
        var guidB   = Guid.NewGuid(); // NOT in graph

        var graph = new StubGraphAR();
        graph.Add(new StubNodeAR(new NodeId(guidA)));

        var elementId = $"{guidA}->{guidB}";
        var response = AR_TestHelper.MakeResponse(
            AR_TestHelper.MakeChange("connection_changed", elementId));

        var registry = new ComparisonSessionRegistry();
        registry.SetSession(new ComparisonSessionState(assetId, response));

        var renderer = new ComparisonAnnotationRenderer(registry);
        renderer.SetActiveAsset(assetId);
        renderer.Render(new StubRenderCtxAR(graph));

        var annotation = Assert.Single(renderer.LastFrameAnnotations);
        Assert.Equal(AnnotationPlacement.SurvivingEndpoint, annotation.Placement);
    }

    // ---- C-21: connection_changed neither endpoint --------------------------

    [Fact]
    public void ConnectionChanged_NeitherEndpointInGraph_AnnotationSkipped()
    {
        var assetId = Guid.NewGuid();
        var guidA   = Guid.NewGuid();
        var guidB   = Guid.NewGuid();

        var graph = new StubGraphAR(); // empty graph

        var elementId = $"{guidA}->{guidB}";
        var response = AR_TestHelper.MakeResponse(
            AR_TestHelper.MakeChange("connection_changed", elementId));

        var registry = new ComparisonSessionRegistry();
        registry.SetSession(new ComparisonSessionState(assetId, response));

        var renderer = new ComparisonAnnotationRenderer(registry);
        renderer.SetActiveAsset(assetId);
        renderer.Render(new StubRenderCtxAR(graph));

        Assert.Empty(renderer.LastFrameAnnotations);
    }

    // ---- C-21: null session => IsActive false --------------------------------

    [Fact]
    public void NullSession_IsActiveFalse()
    {
        var assetId  = Guid.NewGuid();
        var registry = new ComparisonSessionRegistry();

        var renderer = new ComparisonAnnotationRenderer(registry);
        renderer.SetActiveAsset(assetId);

        Assert.False(renderer.IsActive);
    }

    // ---- C-25: variable_renamed badges on matching nodes --------------------

    [Fact]
    public void VariableRenamed_TwoMatchingNodes_TwoAnnotations()
    {
        var assetId = Guid.NewGuid();
        var guidA   = Guid.NewGuid();
        var guidB   = Guid.NewGuid();
        var guidC   = Guid.NewGuid();

        var graph = new StubGraphAR();
        graph.Add(new StubNodeAR(new NodeId(guidA), "AmmoCount"));
        graph.Add(new StubNodeAR(new NodeId(guidB), "read AmmoCount"));
        graph.Add(new StubNodeAR(new NodeId(guidC), "SomeOtherVar")); // no match

        var response = AR_TestHelper.MakeResponse(
            AR_TestHelper.MakeChange("variable_renamed", null, oldValue: "AmmoCount"));

        var registry = new ComparisonSessionRegistry();
        registry.SetSession(new ComparisonSessionState(assetId, response));

        var renderer = new ComparisonAnnotationRenderer(registry);
        renderer.SetActiveAsset(assetId);
        renderer.Render(new StubRenderCtxAR(graph));

        Assert.Equal(2, renderer.LastFrameAnnotations.Count);
        Assert.All(renderer.LastFrameAnnotations, a => Assert.Equal("variable_renamed", a.Kind));
        Assert.All(renderer.LastFrameAnnotations, a => Assert.Equal(AnnotationPlacement.NodeBadge, a.Placement));
    }

    // ---- C-25: variable_renamed no badge on non-matching nodes --------------

    [Fact]
    public void VariableRenamed_NonMatchingNode_NoBadge()
    {
        var assetId = Guid.NewGuid();
        var guidC   = Guid.NewGuid();

        var graph = new StubGraphAR();
        graph.Add(new StubNodeAR(new NodeId(guidC), "SomeOtherVar"));

        var response = AR_TestHelper.MakeResponse(
            AR_TestHelper.MakeChange("variable_renamed", null, oldValue: "AmmoCount"));

        var registry = new ComparisonSessionRegistry();
        registry.SetSession(new ComparisonSessionState(assetId, response));

        var renderer = new ComparisonAnnotationRenderer(registry);
        renderer.SetActiveAsset(assetId);
        renderer.Render(new StubRenderCtxAR(graph));

        Assert.Empty(renderer.LastFrameAnnotations);
    }
}
