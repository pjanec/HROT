using ImGuiNET;
using NodeEditor.Core.Bookmarks;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.Primitives;
using System;
using System.Numerics;

namespace NodeEditor.UI.Bookmarks;

/// <summary>
/// Canvas overlay renderer that draws arrows at the canvas edges pointing to
/// off-screen bookmarks in slots 1-9.
/// </summary>
public static class BookmarkEdgeMarkerRenderer
{
    private const float ArrowSize  = 14f;
    private const float ArrowInset = 6f;

    /// <summary>
    /// Render edge-markers for bookmarks that are off-screen.
    /// Call during the canvas overlay phase (after nodes/wires).
    /// </summary>
    public static void Render(GraphView view, BookmarkStore store, IEditorTheme theme)
    {
        var dl = ImGui.GetWindowDrawList();
        var visibleGraphRect = RectF.FromMinMax(
            view.Viewport.ScreenToGraph(view.Viewport.CanvasScreenOrigin),
            view.Viewport.ScreenToGraph(view.Viewport.CanvasScreenOrigin + view.Viewport.CanvasScreenSize));

        foreach (var b in store.All)
        {
            if (b.TargetGraph != view.Model.Id || b.SlotNumber < 1 || b.SlotNumber > 9) continue;

            var bookmarkCenterGraph = b.ViewportPan + (view.Viewport.CanvasScreenSize / b.ViewportZoom) * 0.5f;

            if (visibleGraphRect.Contains(bookmarkCenterGraph)) continue;

            var screenTarget = view.Viewport.GraphToScreen(bookmarkCenterGraph);
            var clipped = ClipToEdge(screenTarget, view.Viewport.CanvasScreenOrigin, view.Viewport.CanvasScreenSize);

            if (Vector2.DistanceSquared(screenTarget, clipped) < 1f) continue;

            var dir = Vector2.Normalize(screenTarget - clipped);
            if (dir.LengthSquared() < 0.5f) dir = new Vector2(0, -1);

            uint color = ImGui.GetColorU32(new Vector4(1f, 0.8f, 0.2f, 0.8f));
            DrawArrow(dl, clipped, dir, ArrowSize, color);

            // Hover tooltip
            if (Vector2.Distance(ImGui.GetMousePos(), clipped) < ArrowSize * 2)
                ImGui.SetTooltip($"[{b.SlotNumber}] {b.Label}");

            // Click to jump
            if (ImGui.IsMouseReleased(ImGuiMouseButton.Left) &&
                Vector2.Distance(ImGui.GetMousePos(), clipped) < ArrowSize * 2)
            {
                view.Interaction.BeginViewportTween(b.ViewportPan, b.ViewportZoom, 180);
            }
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static Vector2 ClipToEdge(Vector2 target, Vector2 origin, Vector2 size)
    {
        var center = origin + size * 0.5f;
        var dir    = target - center;

        if (dir.LengthSquared() < 0.0001f) return center;

        float tx   = dir.X != 0 ? (dir.X > 0 ? (origin.X + size.X - ArrowInset - center.X) / dir.X : (origin.X + ArrowInset - center.X) / dir.X) : float.PositiveInfinity;
        float ty   = dir.Y != 0 ? (dir.Y > 0 ? (origin.Y + size.Y - ArrowInset - center.Y) / dir.Y : (origin.Y + ArrowInset - center.Y) / dir.Y) : float.PositiveInfinity;
        float t    = Math.Min(Math.Abs(tx), Math.Abs(ty));
        return center + dir * t;
    }

    private static void DrawArrow(ImDrawListPtr dl, Vector2 tip, Vector2 dir, float size, uint color)
    {
        var right  = new Vector2(-dir.Y, dir.X);
        var p1     = tip;
        var p2     = tip - dir * size + right * size * 0.5f;
        var p3     = tip - dir * size - right * size * 0.5f;
        dl.AddTriangleFilled(p1, p2, p3, color);
        dl.AddTriangle(p1, p2, p3, ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 0.6f)), 1.5f);
    }
}
