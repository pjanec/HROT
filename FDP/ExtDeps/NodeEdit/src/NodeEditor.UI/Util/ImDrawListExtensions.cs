using System.Numerics;
using ImGuiNET;

namespace NodeEditor.UI.Util;

/// <summary>
/// Extension helpers for <see cref="ImDrawListPtr"/> that keep canvas-rendering
/// code expressive without repeating low-level math.
/// </summary>
public static class ImDrawListExtensions
{
    /// <summary>
    /// Draw a cubic bezier and place a filled triangle arrowhead at its midpoint.
    /// Used for execution-flow wires.
    /// </summary>
    public static void AddBezierWithArrow(
        this ImDrawListPtr dl,
        Vector2 p1, Vector2 c1, Vector2 c2, Vector2 p2,
        uint color, float thickness, float arrowSize, int segments = 0)
    {
        dl.AddBezierCubic(p1, c1, c2, p2, color, thickness, segments);

        var mid = BezierPoint(p1, c1, c2, p2, 0.5f);
        var tan = Vector2.Normalize(BezierTangent(p1, c1, c2, p2, 0.5f));
        var nor = new Vector2(-tan.Y, tan.X);

        var tip = mid + tan * arrowSize;
        var b1 = mid - tan * (arrowSize * 0.5f) + nor * (arrowSize * 0.5f);
        var b2 = mid - tan * (arrowSize * 0.5f) - nor * (arrowSize * 0.5f);
        dl.AddTriangleFilled(b1, b2, tip, color);
    }

    /// <summary>
    /// Draw a circle filled with <paramref name="fillColor"/> then outlined
    /// with <paramref name="outlineColor"/>. One call vs two.
    /// </summary>
    public static void AddCircleFilledOutline(
        this ImDrawListPtr dl,
        Vector2 center, float radius,
        uint fillColor, uint outlineColor, float outlineThickness = 1.5f)
    {
        dl.AddCircleFilled(center, radius, fillColor);
        dl.AddCircle(center, radius, outlineColor, 0, outlineThickness);
    }

    // ── private math ──────────────────────────────────────────────────────────

    private static Vector2 BezierPoint(Vector2 p1, Vector2 c1, Vector2 c2, Vector2 p2, float t)
    {
        float u = 1f - t;
        return u * u * u * p1
             + 3f * u * u * t * c1
             + 3f * u * t * t * c2
             + t * t * t * p2;
    }

    private static Vector2 BezierTangent(Vector2 p1, Vector2 c1, Vector2 c2, Vector2 p2, float t)
    {
        float u = 1f - t;
        return 3f * u * u * (c1 - p1)
             + 6f * u * t * (c2 - c1)
             + 3f * t * t * (p2 - c2);
    }
}
