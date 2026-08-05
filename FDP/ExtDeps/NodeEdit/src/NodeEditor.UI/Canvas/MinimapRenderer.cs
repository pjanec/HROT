using System.Numerics;
using ImGuiNET;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.Primitives;

namespace NodeEditor.UI.Canvas;

/// <summary>
/// BP-19 — the overview minimap.
///
/// <para>
/// A corner overlay showing every node as a filled rectangle plus an outline of what the canvas is
/// currently looking at. Clicking or dragging inside it recentres the viewport there, which is the
/// point: on a graph large enough to need a minimap, scrolling back to a node you can see but
/// cannot reach is the actual cost.
/// </para>
///
/// <para>
/// <see cref="ViewportState"/> already supplies all the transform maths; this renderer only needs
/// the second mapping, graph-space → minimap-space, which is a uniform fit of the graph's bounding
/// box into a fixed box. Uniform rather than stretched, so the minimap is a recognisable miniature
/// of the graph rather than a distorted one.
/// </para>
/// </summary>
public sealed class MinimapRenderer
{
    /// <summary>Size of the overlay in screen pixels.</summary>
    public Vector2 Size { get; set; } = new(220f, 150f);

    /// <summary>Gap between the overlay and the canvas edge.</summary>
    public float Margin { get; set; } = 12f;

    /// <summary>Assumed node size when a node has not reported one (mirrors the canvas default).</summary>
    private static readonly Vector2 DefaultNodeSize = new(160f, 64f);

    /// <summary>
    /// Draws the overlay in the canvas's top-right corner and handles clicks on it. Call from
    /// inside the canvas child window, after the graph has been drawn.
    /// </summary>
    public void Draw(GraphView view, ImDrawListPtr dl)
    {
        if (!view.Viewport.ShowMinimap) return;
        if (view.Model.Nodes.Count == 0) return;

        var canvasOrigin = view.Viewport.CanvasScreenOrigin;
        var canvasSize   = view.Viewport.CanvasScreenSize;
        if (canvasSize.X <= 0f || canvasSize.Y <= 0f) return;

        var min = new Vector2(
            canvasOrigin.X + canvasSize.X - Size.X - Margin,
            canvasOrigin.Y + Margin);
        var max = min + Size;

        var theme = view.Host.Theme;
        dl.AddRectFilled(min, max, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.55f)), 4f);
        dl.AddRect(min, max, ImGui.GetColorU32(theme.TextMuted), 4f, ImDrawFlags.None, 1f);

        // The graph bounds are unioned with what the canvas currently shows, so the viewport
        // rectangle stays inside the minimap even when the user pans away from every node.
        var bounds = Union(GraphBounds(view), VisibleRect(view));
        var fit    = Fit(bounds, min, max);

        foreach (var node in view.Model.Nodes)
        {
            var size = node.SizeOverride ?? DefaultNodeSize;
            var a    = fit(node.Position);
            var b    = fit(node.Position + size);

            // Sub-pixel nodes would vanish; a minimap that omits nodes is worse than none.
            if (b.X - a.X < 2f) b.X = a.X + 2f;
            if (b.Y - a.Y < 2f) b.Y = a.Y + 2f;

            var colour = node.State == NodeState.Error
                ? theme.ErrorColor
                : theme.GetCategoryHeaderColor(node.Category);
            dl.AddRectFilled(a, b, ImGui.GetColorU32(colour with { W = 0.9f }), 1f);
        }

        var visible = VisibleRect(view);
        dl.AddRect(fit(visible.Min), fit(visible.Min + visible.Size),
            ImGui.GetColorU32(theme.SelectionAccent), 2f, ImDrawFlags.None, 1.5f);

        HandleClick(view, min, bounds);
    }

    // ── interaction ───────────────────────────────────────────────────────────

    /// <summary>
    /// Recentres the viewport on the clicked point. Held drags keep recentring, so the minimap
    /// works as a scrubber rather than only as a jump target.
    /// </summary>
    private void HandleClick(GraphView view, Vector2 min, RectF bounds)
    {
        var mouse = ImGui.GetMousePos();
        var max   = min + Size;
        bool inside = mouse.X >= min.X && mouse.X <= max.X && mouse.Y >= min.Y && mouse.Y <= max.Y;
        if (!inside) return;

        // Consume the hover so the canvas underneath does not also act on it.
        ImGui.SetNextFrameWantCaptureMouse(true);

        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left)) return;

        var scale = FitScale(bounds);
        if (scale <= 0f) return;

        var offset = FitOffset(bounds, min, scale);
        var graph  = (mouse - offset) / scale;

        CentreOn(view, graph);
    }

    /// <summary>Moves the viewport so <paramref name="graphPoint"/> sits at the canvas centre.</summary>
    private static void CentreOn(GraphView view, Vector2 graphPoint)
    {
        var vp     = view.Viewport;
        var centre = vp.ScreenToGraph(vp.CanvasScreenOrigin + vp.CanvasScreenSize * 0.5f);
        vp.Pan(centre - graphPoint);
    }

    // ── geometry ──────────────────────────────────────────────────────────────

    /// <summary>Bounding box of every node, including its drawn size.</summary>
    internal static RectF GraphBounds(GraphView view)
    {
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;

        foreach (var n in view.Model.Nodes)
        {
            var size = n.SizeOverride ?? DefaultNodeSize;
            if (n.Position.X < minX) minX = n.Position.X;
            if (n.Position.Y < minY) minY = n.Position.Y;
            if (n.Position.X + size.X > maxX) maxX = n.Position.X + size.X;
            if (n.Position.Y + size.Y > maxY) maxY = n.Position.Y + size.Y;
        }

        if (minX == float.MaxValue) return new RectF(Vector2.Zero, Vector2.Zero);
        return new RectF(new Vector2(minX, minY), new Vector2(maxX - minX, maxY - minY));
    }

    /// <summary>The canvas's current view, in graph space.</summary>
    internal static RectF VisibleRect(GraphView view)
    {
        var vp       = view.Viewport;
        var topLeft  = vp.ScreenToGraph(vp.CanvasScreenOrigin);
        var bottomRight = vp.ScreenToGraph(vp.CanvasScreenOrigin + vp.CanvasScreenSize);
        return new RectF(topLeft, bottomRight - topLeft);
    }

    internal static RectF Union(RectF a, RectF b)
    {
        var min = new Vector2(MathF.Min(a.Min.X, b.Min.X), MathF.Min(a.Min.Y, b.Min.Y));
        var max = new Vector2(
            MathF.Max(a.Min.X + a.Size.X, b.Min.X + b.Size.X),
            MathF.Max(a.Min.Y + a.Size.Y, b.Min.Y + b.Size.Y));
        return new RectF(min, max - min);
    }

    /// <summary>
    /// Uniform scale that fits <paramref name="bounds"/> into the overlay with a little padding.
    /// Uniform, not per-axis: a stretched minimap is not recognisable as the graph it maps.
    /// </summary>
    private float FitScale(RectF bounds)
    {
        const float pad = 6f;
        float w = MathF.Max(bounds.Size.X, 1f);
        float h = MathF.Max(bounds.Size.Y, 1f);
        return MathF.Min((Size.X - pad * 2f) / w, (Size.Y - pad * 2f) / h);
    }

    /// <summary>Screen offset that centres the scaled bounds inside the overlay.</summary>
    private Vector2 FitOffset(RectF bounds, Vector2 min, float scale)
    {
        var scaled = bounds.Size * scale;
        var slack  = (Size - scaled) * 0.5f;
        return min + slack - bounds.Min * scale;
    }

    private Func<Vector2, Vector2> Fit(RectF bounds, Vector2 min, Vector2 max)
    {
        float scale   = FitScale(bounds);
        var   offset  = FitOffset(bounds, min, scale);
        return graph => offset + graph * scale;
    }
}
