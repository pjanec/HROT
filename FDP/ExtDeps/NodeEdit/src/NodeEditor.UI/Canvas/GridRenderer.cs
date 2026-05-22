using System.Numerics;
using ImGuiNET;
using NodeEditor.Core.View;

namespace NodeEditor.UI.Canvas;

/// <summary>
/// Draws the editor background grid: minor dots every 16 graph units,
/// major dots every 128 graph units. Dots are skipped below
/// <see cref="ViewportState.LowZoomThreshold"/> to avoid visual noise.
/// </summary>
internal sealed class GridRenderer
{
    private const float MinorGridGu = 16f;
    private const float MajorGridGu = 128f;
    private const float MinZoomForMinor = 0.35f;

    /// <summary>Draw the grid into the provided draw list.</summary>
    public void Draw(GraphView view, ImDrawListPtr dl, Vector2 origin, Vector2 size)
    {
        var theme = view.Host.Theme;
        var viewport = view.Viewport;
        float zoom = viewport.Zoom;

        // Background fill
        dl.AddRectFilled(origin, origin + size, ToU32(theme.BackgroundColor));

        if (zoom < MinZoomForMinor)
        {
            // Only draw major grid at very low zoom
            DrawDots(dl, viewport, origin, size, MajorGridGu, zoom, ToU32(theme.GridMajorColor), 2f);
            return;
        }

        DrawDots(dl, viewport, origin, size, MinorGridGu, zoom, ToU32(theme.GridMinorColor), 1.5f);
        DrawDots(dl, viewport, origin, size, MajorGridGu, zoom, ToU32(theme.GridMajorColor), 2.5f);
    }

    private static void DrawDots(
        ImDrawListPtr dl,
        ViewportState viewport,
        Vector2 origin, Vector2 size,
        float gridGu, float zoom, uint color, float dotRadius)
    {
        // Find the first grid line (in graph space) visible at the canvas origin.
        var graphOrigin = viewport.ScreenToGraph(origin);

        float startX = MathF.Floor(graphOrigin.X / gridGu) * gridGu;
        float startY = MathF.Floor(graphOrigin.Y / gridGu) * gridGu;

        float endX = graphOrigin.X + size.X / zoom + gridGu;
        float endY = graphOrigin.Y + size.Y / zoom + gridGu;

        for (float gx = startX; gx < endX; gx += gridGu)
        {
            for (float gy = startY; gy < endY; gy += gridGu)
            {
                var screen = viewport.GraphToScreen(new Vector2(gx, gy));
                // Clip to canvas
                if (screen.X < origin.X - dotRadius || screen.X > origin.X + size.X + dotRadius) continue;
                if (screen.Y < origin.Y - dotRadius || screen.Y > origin.Y + size.Y + dotRadius) continue;
                dl.AddCircleFilled(screen, dotRadius, color);
            }
        }
    }

    private static uint ToU32(System.Numerics.Vector4 c) => ImGui.GetColorU32(c);
}
