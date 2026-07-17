using System;
using System.Numerics;
using NodeEditor.Primitives;

namespace NodeEditor.Core.View;

/// <summary>
/// Editor viewport: pan (graph-space offset of canvas origin) and zoom (uniform scale).
/// Pure data plus deterministic transforms. No rendering, no input handling.
/// </summary>
public sealed class ViewportState
{
    /// <summary>Graph-space coordinate that maps to the canvas screen origin (top-left of canvas region).</summary>
    public Vector2 PanGraph { get; set; } = Vector2.Zero;

    /// <summary>Uniform scale factor. 1.0 = native. Clamped to [<see cref="MinZoom"/>, <see cref="MaxZoom"/>].</summary>
    public float Zoom { get; private set; } = 1.0f;

    /// <summary>Top-left of the canvas region in screen coordinates (set by the renderer each frame).</summary>
    public Vector2 CanvasScreenOrigin { get; set; } = Vector2.Zero;

    /// <summary>Size of the canvas region in screen pixels (set by the renderer each frame).</summary>
    public Vector2 CanvasScreenSize { get; set; } = Vector2.Zero;

    // Lowered from 0.25 → 0.125 (twice the zoom-out) so very large graphs can be surveyed whole.
    public const float MinZoom = 0.125f;
    public const float MaxZoom = 3.0f;
    // Below this zoom the heavier per-node content (pins + interactive inline editors) is skipped.
    // Node title text is NOT gated by this — it stays drawn down to the zoom limit (crisp ladder +
    // bilinear atlas keep it legible), so nodes remain identifiable when surveying a big graph.
    public const float LowZoomThreshold = 0.3f;

    /// <summary>True when zoom is below the simplified-rendering threshold.</summary>
    public bool IsLowZoom => Zoom < LowZoomThreshold;

    /// <summary>Convert a graph-space point to screen coordinates.</summary>
    public Vector2 GraphToScreen(Vector2 graph)
        => CanvasScreenOrigin + (graph - PanGraph) * Zoom;

    /// <summary>Convert a screen-space point to graph coordinates.</summary>
    public Vector2 ScreenToGraph(Vector2 screen)
        => PanGraph + (screen - CanvasScreenOrigin) / Zoom;

    /// <summary>Apply a pan delta in graph-space units.</summary>
    public void Pan(Vector2 deltaGraph) => PanGraph += deltaGraph;

    /// <summary>Apply a pan delta in screen-space pixels (scaled by current zoom).</summary>
    public void PanScreen(Vector2 deltaScreen) => PanGraph += deltaScreen / Zoom;

    /// <summary>
    /// Zoom by a multiplicative factor centered on the given screen position.
    /// The graph point under <paramref name="anchorScreen"/> stays anchored after the zoom.
    /// </summary>
    public void ZoomAt(Vector2 anchorScreen, float factor)
    {
        var anchorGraphBefore = ScreenToGraph(anchorScreen);
        Zoom = Math.Clamp(Zoom * factor, MinZoom, MaxZoom);
        var anchorGraphAfter = ScreenToGraph(anchorScreen);
        PanGraph += anchorGraphBefore - anchorGraphAfter;
    }

    /// <summary>Explicitly sets the zoom level clamped to bounds (used by camera tweening).</summary>
    public void SetZoom(float zoom)
    {
        Zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
    }

    /// <summary>Reset the viewport to identity (zoom=1, pan=0).</summary>
    public void Reset()
    {
        PanGraph = Vector2.Zero;
        Zoom = 1.0f;
    }

    /// <summary>
    /// Frame a graph-space rect into the canvas (centered, with margin).
    /// Zoom is clamped to <see cref="MaxZoom"/> so framing a single point doesn't max out.
    /// </summary>
    public void FrameRect(RectF rect, float marginPx = 64f)
    {
        if (CanvasScreenSize.X <= 0 || CanvasScreenSize.Y <= 0) return;
        if (rect.Width <= 0 || rect.Height <= 0) return;

        var avail = CanvasScreenSize - new Vector2(marginPx * 2f, marginPx * 2f);
        if (avail.X <= 0 || avail.Y <= 0) return;

        float zx = avail.X / rect.Width;
        float zy = avail.Y / rect.Height;
        Zoom = Math.Clamp(MathF.Min(zx, zy), MinZoom, MaxZoom);

        var rectCenterGraph = rect.Center;
        var canvasCenterScreen = CanvasScreenOrigin + CanvasScreenSize * 0.5f;
        PanGraph = rectCenterGraph - (canvasCenterScreen - CanvasScreenOrigin) / Zoom;
    }
}
