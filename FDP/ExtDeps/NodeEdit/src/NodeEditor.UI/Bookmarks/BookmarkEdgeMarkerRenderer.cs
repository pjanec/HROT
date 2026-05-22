using ImGuiNET;
using NodeEditor.Core.Bookmarks;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.Primitives;
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
        var vp       = view.Viewport;
        var origin   = vp.CanvasScreenOrigin;
        var size     = vp.CanvasScreenSize;
        var dl       = ImGui.GetWindowDrawList();

        for (int slot = 1; slot <= 9; slot++)
        {
            var b = store.GetSlot(slot);
            if (b is null || b.TargetGraph != view.Model.Id) continue;

            var bookmarkScreen = vp.GraphToScreen(b.ViewportPan);
            var canvasRect     = new RectF(origin, size);

            if (canvasRect.Contains(bookmarkScreen)) continue; // on-screen, no marker needed

            var clipped = ClipToEdge(bookmarkScreen, origin, size);
            var dir     = Vector2.Normalize(bookmarkScreen - new Vector2(origin.X + size.X * 0.5f, origin.Y + size.Y * 0.5f));

            var color = ImGui.ColorConvertFloat4ToU32(new Vector4(1, 0.85f, 0.1f, 0.85f));
            DrawArrow(dl, clipped, dir, ArrowSize, color);

            // Hover tooltip
            if (Vector2.Distance(ImGui.GetMousePos(), clipped) < ArrowSize * 2)
                ImGui.SetTooltip($"[{slot}] {b.Label}");

            // Click to jump
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) &&
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
