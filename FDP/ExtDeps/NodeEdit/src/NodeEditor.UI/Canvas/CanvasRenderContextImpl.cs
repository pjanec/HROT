using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;
using NodeEditor.Core.Canvas;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.Primitives;

namespace NodeEditor.UI.Canvas;

/// <summary>
/// Concrete per-frame implementation of <see cref="ICanvasRenderContext"/>.
/// Constructed once and updated each frame before invoking custom renderers.
/// </summary>
internal sealed class CanvasRenderContextImpl : ICanvasRenderContext, IHitTestContext
{
    private readonly Dictionary<string, object?> _frameScratch = new();

    // Updated each frame by CanvasRenderer.
    internal ImDrawListPtr    _drawList;
    internal CanvasRenderPass _pass;
    internal GraphView?       _view;
    internal IReadOnlySet<NodeId> _visibleNodes = new HashSet<NodeId>();
    internal IReadOnlySet<LinkId> _visibleLinks = new HashSet<LinkId>();
    internal CanvasLayout?    _layout;

    // ── ICanvasRenderContext ──────────────────────────────────────────────────

    public ImDrawListPtr DrawList => _drawList;
    public ViewportState Viewport => _view!.Viewport;
    public CanvasRenderPass Pass  => _pass;
    public IEditorTheme Theme     => _view!.Host.Theme;
    public IGraphModel Graph      => _view!.Model;
    public SelectionState Selection => _view!.Selection;
    public IReadOnlySet<NodeId> VisibleNodes => _visibleNodes;
    public IReadOnlySet<LinkId> VisibleLinks => _visibleLinks;
    public float Zoom      => _view!.Viewport.Zoom;
    public bool IsLowZoom  => _view!.Viewport.IsLowZoom;
    public IDebugSession? DebugSession => _view!.Host.Debug;
    public IDictionary<string, object?> FrameScratch => _frameScratch;

    public Vector2 CanvasToScreen(Vector2 canvasPoint) =>
        _view!.Viewport.GraphToScreen(canvasPoint);

    public Vector2 ScreenToCanvas(Vector2 screenPoint) =>
        _view!.Viewport.ScreenToGraph(screenPoint);

    public RectF CanvasToScreen(RectF canvasRect)
    {
        var screenMin = _view!.Viewport.GraphToScreen(canvasRect.Min);
        var screenMax = _view!.Viewport.GraphToScreen(canvasRect.Min + canvasRect.Size);
        return RectF.FromMinMax(screenMin, screenMax);
    }

    public bool TryGetNodeScreenRect(NodeId id, out RectF screenRect)
    {
        if (_layout != null && _layout.NodeScreenRects.TryGetValue(id, out screenRect)) return true;
        screenRect = default; return false;
    }

    public bool TryGetPinScreenPosition(PinId id, out Vector2 screenPos)
    {
        if (_layout != null && _layout.PinScreenPositions.TryGetValue(id, out screenPos)) return true;
        screenPos = default; return false;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Reset scratch state between frames.</summary>
    internal void BeginFrame(
        GraphView view,
        ImDrawListPtr drawList,
        IReadOnlySet<NodeId> visibleNodes,
        IReadOnlySet<LinkId> visibleLinks,
        CanvasLayout layout)
    {
        _view         = view;
        _drawList     = drawList;
        _visibleNodes = visibleNodes;
        _visibleLinks = visibleLinks;
        _layout       = layout;
        _frameScratch.Clear();
    }
}
