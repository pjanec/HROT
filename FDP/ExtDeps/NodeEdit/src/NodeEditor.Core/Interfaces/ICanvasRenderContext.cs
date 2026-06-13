using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;
using NodeEditor.Core.Canvas;
using NodeEditor.Core.View;
using NodeEditor.Primitives;

namespace NodeEditor.Core.Interfaces;

/// <summary>
/// Per-frame render context supplied to <see cref="ICustomCanvasRenderer.Render"/>.
/// The context is created fresh each frame by the canvas and must not be retained
/// across frames.
/// </summary>
public interface ICanvasRenderContext
{
    /// <summary>
    /// Direct access to ImGui's draw list for this frame.
    /// The draw list is in screen coordinates; use CanvasToScreen
    /// to transform canvas-space positions before drawing.
    /// </summary>
    ImDrawListPtr DrawList { get; }

    /// <summary>The viewport's current pan and zoom.</summary>
    ViewportState Viewport { get; }

    /// <summary>The pass currently being rendered.</summary>
    CanvasRenderPass Pass { get; }

    /// <summary>The active editor theme.</summary>
    IEditorTheme Theme { get; }

    /// <summary>The graph being rendered.</summary>
    IGraphModel Graph { get; }

    /// <summary>
    /// Selection state at the start of this frame.
    /// Renderers may read this to show selection feedback; they must not mutate it.
    /// </summary>
    SelectionState Selection { get; }

    /// <summary>
    /// Node IDs whose bounds intersect the current viewport.
    /// Use for culling: only draw elements associated with visible nodes.
    /// </summary>
    IReadOnlySet<NodeId> VisibleNodes { get; }

    /// <summary>
    /// Link IDs whose endpoints are in or near the current viewport.
    /// Use for culling: only draw decorations for visible links.
    /// </summary>
    IReadOnlySet<LinkId> VisibleLinks { get; }

    /// <summary>Current zoom level (same as <see cref="Viewport"/>.Zoom).</summary>
    float Zoom { get; }

    /// <summary>
    /// True when zoom is below 0.5. Renderers should simplify or omit text
    /// and other fine detail in low-zoom mode.
    /// </summary>
    bool IsLowZoom { get; }

    /// <summary>Optional debug session. Renderers that draw runtime overlays read from this.</summary>
    IDebugSession? DebugSession { get; }

    /// <summary>
    /// Per-frame scratch dictionary for passing data between renderers in the same frame.
    /// Cleared between frames.
    /// </summary>
    IDictionary<string, object?> FrameScratch { get; }

    /// <summary>Transform a canvas-coordinate point to screen-coordinate.</summary>
    Vector2 CanvasToScreen(Vector2 canvasPoint);

    /// <summary>Transform a screen-coordinate point to canvas-coordinate.</summary>
    Vector2 ScreenToCanvas(Vector2 screenPoint);

    /// <summary>Transform a canvas-coordinate rect to a screen-coordinate rect.</summary>
    RectF CanvasToScreen(RectF canvasRect);

    /// <summary>
    /// Screen-space bounding rect of a node as laid out this frame (post pan/zoom,
    /// container position resolved). Returns false if the node was not laid out
    /// (e.g. hidden inside a collapsed parent, or unknown id).
    /// </summary>
    bool TryGetNodeScreenRect(NodeId id, out RectF screenRect);

    /// <summary>
    /// Screen-space attachment point of a pin as laid out this frame.
    /// Returns false if the pin was not laid out.
    /// </summary>
    bool TryGetPinScreenPosition(PinId id, out Vector2 screenPos);
}

/// <summary>
/// Lightweight context passed to <see cref="ICustomCanvasHitTester.HitTest"/>.
/// Does not carry the draw list (hit-test is read-only).
/// </summary>
public interface IHitTestContext
{
    ViewportState Viewport { get; }
    IGraphModel Graph { get; }
    IReadOnlySet<NodeId> VisibleNodes { get; }
    IReadOnlySet<LinkId> VisibleLinks { get; }
    float Zoom { get; }
}
