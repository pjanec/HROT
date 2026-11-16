using System;
using System.Numerics;

namespace Hrot.Presentation.Map2D;

/// <summary>
/// ⭐ <c>CE-8602</c> (exp/agg-ui, docs/designs/aggregate-units/DESIGN_Stochastic_Unit_UI.md §4 UI-2 / UI-4) — the GEOMETRY of a memberless unit's 2-D footprint
/// (DESIGN_Stochastic_Units_And_Resolution §14 M-3: width × depth × heading, or a column as a segment of its path). Engine plan coordinates (x east, y north);
/// heading in compass degrees (0 = north, clockwise). Pure and allocation-free: the caller owns the output spans, so the gizmo calls it every frame (R-2084).
/// </summary>
public static class UnitFootprint
{
    /// <summary>The unit's forward direction for <paramref name="headingDeg"/> (0 = +y north, 90 = +x east).</summary>
    public static Vector2 Forward(float headingDeg)
    {
        float a = headingDeg * (MathF.PI / 180f);
        return new Vector2(MathF.Sin(a), MathF.Cos(a));
    }

    /// <summary>The deployed footprint's four corners into <paramref name="corners"/> (length ≥ 4), counter-clockwise from the rear-left:
    /// rear-left, rear-right, front-right, front-left. <paramref name="width"/> runs across the heading (the frontage), <paramref name="depth"/> along it.</summary>
    public static void Corners(Vector2 centre, float width, float depth, float headingDeg, Span<Vector2> corners)
    {
        if (corners.Length < 4) throw new ArgumentException("needs 4 corners", nameof(corners));
        var f = Forward(headingDeg); var r = new Vector2(f.Y, -f.X);   // right of the heading
        float hw = MathF.Max(0f, width) * 0.5f, hd = MathF.Max(0f, depth) * 0.5f;
        corners[0] = centre - (r * hw) - (f * hd);
        corners[1] = centre + (r * hw) - (f * hd);
        corners[2] = centre + (r * hw) + (f * hd);
        corners[3] = centre - (r * hw) + (f * hd);
    }

    /// <summary>The heading arrow off the middle of the front edge: from its base to its tip, <paramref name="length"/> long.</summary>
    public static (Vector2 Base, Vector2 Tip) FrontArrow(Vector2 centre, float depth, float headingDeg, float length)
    {
        var f = Forward(headingDeg);
        var b = centre + (f * (MathF.Max(0f, depth) * 0.5f));
        return (b, b + (f * length));
    }

    /// <summary>
    /// A march COLUMN (UI-4): the part of <paramref name="path"/> (a polyline in travel order) from <paramref name="headAlong"/> − <paramref name="depth"/> to
    /// <paramref name="headAlong"/> (metres along the path; clamped to it) into <paramref name="points"/>, tail first, head last. Returns the number of points
    /// written (0 when the path has under two points or the output is too short) — the path's own vertices inside the segment plus the two cut ends.
    /// </summary>
    public static int ColumnSegment(ReadOnlySpan<Vector2> path, float headAlong, float depth, Span<Vector2> points)
    {
        if (path.Length < 2 || points.Length < 2) return 0;
        float total = 0f;
        for (int i = 1; i < path.Length; i++) total += Vector2.Distance(path[i - 1], path[i]);
        float head = Math.Clamp(headAlong, 0f, total), tail = Math.Clamp(head - MathF.Max(0f, depth), 0f, total);
        int n = 0; float at = 0f;
        points[n++] = PointAt(path, tail);
        for (int i = 1; i < path.Length && n < points.Length - 1; i++)
        {
            at += Vector2.Distance(path[i - 1], path[i]);
            if (at > tail && at < head) points[n++] = path[i];
        }
        points[n++] = PointAt(path, head);
        return n;
    }

    /// <summary>The point <paramref name="along"/> metres along <paramref name="path"/> (clamped to its ends).</summary>
    public static Vector2 PointAt(ReadOnlySpan<Vector2> path, float along)
    {
        if (path.Length == 0) return default;
        if (along <= 0f) return path[0];
        float at = 0f;
        for (int i = 1; i < path.Length; i++)
        {
            float seg = Vector2.Distance(path[i - 1], path[i]);
            if (at + seg >= along) return seg <= 0f ? path[i] : Vector2.Lerp(path[i - 1], path[i], (along - at) / seg);
            at += seg;
        }
        return path[^1];
    }
}
