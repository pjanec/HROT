using System.Collections.Generic;
using System.Numerics;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;

namespace Fdp.Toolkit.Vis2D.Gizmos;

/// <summary>What a drawing / editing gizmo is shaping: an area (a filled outline) or a route (a polyline). Decides the colour.</summary>
public enum ShapeKind : byte { Area, Route }

/// <summary>The three segments of a shape being drawn: those already placed, the next one to the cursor, and (areas) the one that would close it.</summary>
public enum SegmentKind : byte { Placed, Next, Closing }

/// <summary>A handle's state. (The sheet's "hovered" waits for the editors to learn the cursor's pixel radius — see the design's P7-B AS-BUILT.)</summary>
public enum HandleState : byte { Idle, Grabbed }

/// <summary>
/// ⭐ B-17 P7 ② ③ (📄 docs/UX/DESIGN_Map_Interaction.md "P7 — the build"; sheet <c>map-gizmo-look-1.svg</c> rows ② ③) — THE one look of drawing and editing a shape or a route. Every drawing gizmo
/// (<see cref="PointSequenceGizmo"/>, <see cref="AreaShapeGizmo"/>) and every editor (<c>VertexEditGizmo</c>, <c>RouteWaypointGizmo</c>) draws through it, so none keeps its own colours:
/// area teal, route green, a grabbed handle amber. Points and handles are SCREEN-PIXEL squares (constant size at every zoom, in 2-D and — draped — in 3-D). Allocation-free.
/// <para>⭐ <b>Pick size ≠ drawn size.</b> The pick test uses a primitive's drawn extents (+ 5 px slack), and the editors' handles were 16 px squares. A handle is therefore TWO primitives: an
/// invisible (alpha 0) Box2D of the old pick size carrying the ids, and the small drawn square with no id — the hit area stays as it was while the picture shrinks to the sheet's 9 px.</para>
/// <para>🔒 The colours are the toolbar icons' (area teal, route green) as constants — the icon colour table lives in the editor assembly, which this one cannot reference.</para>
/// </summary>
public static class GizmoEditStyle
{
    /// <summary>Area / drawn shape: teal.</summary>
    public static readonly Rgba32 Teal = new(0x3E, 0xC6, 0xB0, 255);
    /// <summary>Route: green.</summary>
    public static readonly Rgba32 Green = new(0x5B, 0xD0, 0x6A, 255);
    /// <summary>A grabbed handle: amber.</summary>
    public static readonly Rgba32 Amber = new(0xF2, 0xC0, 0x33, 255);
    /// <summary>The fill of an unfinished point and of the centre diamond.</summary>
    public static readonly Rgba32 Dark = new(0x10, 0x30, 0x2B, 255);
    private static readonly Rgba32 White = new(255, 255, 255, 255);
    private static readonly Rgba32 Black = new(0, 0, 0, 255);
    private static readonly Rgba32 Invisible = new(0, 0, 0, 0);

    /// <summary>The pick half-extent (screen px) of a handle: what the editors emitted before P7 (so hit areas are unchanged).</summary>
    public const float PickHalfPx = 8f;
    /// <summary>Half extents (screen px) of the drawn squares: a point 4, the first point 5, a handle 4.5.</summary>
    public const float PointHalfPx = 4f, FirstHalfPx = 5f, HandleHalfPx = 4.5f;
    /// <summary>The fill alpha of a closed area.</summary>
    public const byte FillAlpha = 30;

    /// <summary>The colour of <paramref name="kind"/>.</summary>
    public static Rgba32 KindColour(ShapeKind kind) => kind == ShapeKind.Area ? Teal : Green;

    /// <summary>A drawn point: a small screen-pixel square. The FIRST point is larger with a WHITE fill — it is the closing target; the others are dark with the kind's outline.</summary>
    public static void Point(IDebugDrawBuilder draw, Vector2 at, ShapeKind kind, bool first)
    {
        var c = KindColour(kind);
        float h = first ? FirstHalfPx : PointHalfPx;
        draw.DrawBox2D(at, new Vector2(h, h), c, 0f, 1.5f, SizeMode.ScreenPixels, fillColor: first ? White : Dark);
    }

    /// <summary>A segment: placed = solid 2 px, the next to the cursor = dashed 1.6 px, the closing one (cursor → first point, areas) = dotted 1.2 px.</summary>
    public static void Segment(IDebugDrawBuilder draw, Vector3 a, Vector3 b, ShapeKind kind, SegmentKind segment)
    {
        var c = KindColour(kind);
        switch (segment)
        {
            case SegmentKind.Placed: draw.DrawLine(a, b, c, 2f, SizeMode.ScreenPixels); break;
            case SegmentKind.Next: draw.DrawLine(a, b, c, 1.6f, SizeMode.ScreenPixels, style: LineStyle.Dashed); break;
            default: draw.DrawLine(a, b, c, 1.2f, SizeMode.ScreenPixels, style: LineStyle.Dotted); break;
        }
    }

    /// <summary>
    /// An editable point's handle: the invisible PICKABLE box (the ids the editors always emitted — <paramref name="anchorId"/>, <paramref name="subElementId"/>) plus the drawn square: idle = white fill with the
    /// kind outline, grabbed = amber fill with a black outline.
    /// </summary>
    public static void Handle(IDebugDrawBuilder draw, Vector2 at, ShapeKind kind, HandleState state, long anchorId, ushort subElementId)
    {
        EmitPick(draw, at, anchorId, subElementId);
        HandleSquare(draw, at, kind, state);
    }

    /// <summary>The drawn square of a handle alone (idle = white with the kind outline, grabbed = amber with a black one) — for a gizmo that hit-tests itself and has no pick ids to carry.</summary>
    public static void HandleSquare(IDebugDrawBuilder draw, Vector2 at, ShapeKind kind, HandleState state)
    {
        bool grabbed = state == HandleState.Grabbed;
        draw.DrawBox2D(at, new Vector2(HandleHalfPx, HandleHalfPx), grabbed ? Black : KindColour(kind), 0f, 1.5f, SizeMode.ScreenPixels, fillColor: grabbed ? Amber : White);
    }

    /// <summary>The centre handle (moves the whole shape): a DIAMOND — a box turned 45° — dark fill with a white outline; the same ids as the pick box beneath it.</summary>
    public static void Centre(IDebugDrawBuilder draw, Vector2 at, long anchorId, ushort subElementId, HandleState state = HandleState.Idle)
    {
        EmitPick(draw, at, anchorId, subElementId);
        CentreMark(draw, at, state);
    }

    /// <summary>The drawn diamond of the centre handle alone.</summary>
    public static void CentreMark(IDebugDrawBuilder draw, Vector2 at, HandleState state = HandleState.Idle)
        => draw.DrawBox2D(at, new Vector2(5f, 5f), state == HandleState.Grabbed ? Amber : White, 45f, 1.5f, SizeMode.ScreenPixels, fillColor: Dark);

    /// <summary>A point that stands above the ground (a roof, a level): a dashed line from it down to z = 0 and a small dot at the raised end. In 2-D it degenerates to a dot; the pickable square stays on the ground.</summary>
    public static void Stem(IDebugDrawBuilder draw, Vector2 at, float height, ShapeKind kind)
    {
        if (height <= 0.01f) return;
        var c = KindColour(kind);
        draw.DrawLine(new Vector3(at.X, at.Y, height), new Vector3(at.X, at.Y, 0f), c, 1f, SizeMode.ScreenPixels, style: LineStyle.Dashed);
        draw.DrawSphere(new Vector3(at.X, at.Y, height), 3f, c, sizeMode: SizeMode.ScreenPixels);
    }

    /// <summary>
    /// A closed area's fill: a triangle fan from the first point at alpha <see cref="FillAlpha"/> — ONLY when the polygon is convex (a fan of a concave one would cover outside it); a concave area is left unfilled.
    /// Returns whether it filled.
    /// </summary>
    public static bool FillConvex(IDebugDrawBuilder draw, IReadOnlyList<Vector2> pts, ShapeKind kind)
    {
        int n = pts.Count;
        if (n < 3 || !IsConvex(pts)) return false;
        var c = KindColour(kind);
        var fill = new Rgba32(c.R, c.G, c.B, FillAlpha);
        for (int i = 1; i < n - 1; i++) draw.DrawFilledTriangle(pts[0], pts[i], pts[i + 1], fill);
        return true;
    }

    /// <summary>All turns go the same way (collinear points allowed).</summary>
    public static bool IsConvex(IReadOnlyList<Vector2> pts)
    {
        int n = pts.Count; float sign = 0f;
        for (int i = 0; i < n; i++)
        {
            var a = pts[i]; var b = pts[(i + 1) % n]; var c = pts[(i + 2) % n];
            float cross = (b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X);
            if (cross == 0f) continue;
            if (sign == 0f) sign = cross;
            else if ((cross > 0f) != (sign > 0f)) return false;
        }
        return true;
    }

    private static void EmitPick(IDebugDrawBuilder draw, Vector2 at, long anchorId, ushort subElementId)
    {
        var prim = default(DebugPrimitive);
        prim.Shape        = DebugPrimitiveShape.Box2D;
        prim.Space        = CoordinateSpace.World;
        prim.TargetView   = PipelineTarget.Map2D;
        prim.BoxCenterX   = at.X;
        prim.BoxCenterY   = at.Y;
        prim.BoxExtentX   = PickHalfPx;
        prim.BoxExtentY   = PickHalfPx;
        prim.Color        = Invisible;   // alpha 0: nothing is drawn, but the hit test (which reads extents and ids only) still finds it
        prim.SubElementId = subElementId;
        prim.BoxAnchorId  = anchorId;
        draw.EmitRaw(in prim);
    }
}
