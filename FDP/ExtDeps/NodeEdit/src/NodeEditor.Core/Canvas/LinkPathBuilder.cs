using System;
using System.Collections.Generic;
using System.Numerics;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace NodeEditor.Core.Canvas;

/// <summary>
/// ⭐ CE-1000 — the ONE place link geometry is computed. Replaces the curve maths that lived separately in the wire
/// renderer, the hit-tester, the pending-wire preview and host renderers.
/// <para>Two routings (<see cref="LinkRouting"/>):
/// <b>PinWires</b> — the Blueprint/BTree S-curve between pin points (tangents unchanged from the former
/// <c>HitTester.WireTangents</c>);
/// <b>NodeToNode</b> — a state-machine arrow from node border to node border on a gentle arc that bends to the
/// LEFT of travel, so <c>A→B</c> and <c>B→A</c> separate by construction.</para>
/// All inputs and outputs are in one space (the canvas passes screen space and the zoom).
/// 📄 docs/blueprints/DESIGN_Hsm_Canvas_Authoring.md §6.
/// </summary>
public static class LinkPathBuilder
{
    // ── Pin wires (Blueprint / BTree) ─────────────────────────────────────────

    /// <summary>The control points of a pin-to-pin wire segment.</summary>
    public static (Vector2 c1, Vector2 c2) PinWireTangents(
        Vector2 a, Vector2 b, PinOrientation orientation = PinOrientation.Horizontal, float zoom = 1f)
    {
        // The minimum-tangent floor is expressed in GRAPH units and multiplied by zoom, so the
        // curve keeps its shape as you zoom (a & b are already screen positions). A fixed screen
        // floor would make short/zoomed-out wires bulge disproportionately (shape change).
        float floor = 50f * zoom;

        if (orientation == PinOrientation.Vertical)
        {
            // Pins face along Y: the From/output pin (a) is on the node's top edge
            // and faces up; the To/input pin (b) is on the bottom edge and faces
            // down. Tangents leave/enter vertically so the spline doesn't sprout
            // sideways like a horizontal (Blueprint) wire.
            float dy = MathF.Abs(b.Y - a.Y);
            float tangentV = MathF.Max(floor, dy * 0.5f);
            return (a - new Vector2(0f, tangentV), b + new Vector2(0f, tangentV));
        }

        float dx = MathF.Abs(b.X - a.X);
        float tangent = MathF.Max(floor, dx * 0.5f);
        return (a + new Vector2(tangent, 0f), b - new Vector2(tangent, 0f));
    }

    /// <summary>A pin-to-pin wire through optional waypoints (one segment per hop).</summary>
    public static LinkPath PinWire(
        Vector2 a, Vector2 b, IReadOnlyList<Vector2>? waypoints = null,
        PinOrientation orientation = PinOrientation.Horizontal, float zoom = 1f)
    {
        var segs = new List<BezierSegment>((waypoints?.Count ?? 0) + 1);
        var prev = a;
        if (waypoints != null)
        {
            foreach (var wp in waypoints)
            {
                segs.Add(PinSegment(prev, wp, orientation, zoom));
                prev = wp;
            }
        }
        segs.Add(PinSegment(prev, b, orientation, zoom));
        return new LinkPath(segs);
    }

    private static BezierSegment PinSegment(Vector2 a, Vector2 b, PinOrientation orientation, float zoom)
    {
        var (c1, c2) = PinWireTangents(a, b, orientation, zoom);
        return new BezierSegment(a, c1, c2, b);
    }

    // ── Node-to-node arrows (state machines) ──────────────────────────────────

    /// <summary>Endpoint offset (px at zoom 1) of lane 0 from the centre line, along the left normal.</summary>
    public const float LaneEndpointOffset = 6f;
    /// <summary>Extra endpoint offset per additional parallel link in the same direction.</summary>
    public const float LaneEndpointStep = 10f;
    /// <summary>Minimum arc bend (px at zoom 1).</summary>
    public const float MinBend = 18f;
    /// <summary>Arc bend as a fraction of the chord length.</summary>
    public const float BendPerLength = 0.12f;
    /// <summary>Extra bend per additional parallel link in the same direction.</summary>
    public const float BendLaneStep = 14f;

    /// <summary>
    /// An arrow from <paramref name="from"/>'s border to <paramref name="to"/>'s border.
    /// <paramref name="lane"/> is the index among links with the same source AND target (0 for the first).
    /// Same rect ⇒ a self-loop. One rect containing the other (a composite and its child) ⇒ the arrow
    /// starts/ends on the container's nearest edge.
    /// </summary>
    public static LinkPath NodeToNode(RectF from, RectF to, int lane = 0, float zoom = 1f,
                                      IReadOnlyList<Vector2>? waypoints = null)
    {
        if (from == to) return SelfLoop(from, lane, zoom);

        if (waypoints is { Count: > 0 })
            return ThroughWaypoints(from, to, waypoints, zoom);

        var ca = from.Center;
        var cb = to.Center;
        Vector2 p0, p3;

        if (from.FullyContains(to))
        {
            p0 = NearestBorderPoint(from, cb);
            p3 = BorderPoint(to, cb, SafeNormalize(p0 - cb));
        }
        else if (to.FullyContains(from))
        {
            p3 = NearestBorderPoint(to, ca);
            p0 = BorderPoint(from, ca, SafeNormalize(p3 - ca));
        }
        else
        {
            var dir = SafeNormalize(cb - ca);
            var left = LeftNormal(dir);
            float off = (LaneEndpointOffset + LaneEndpointStep * lane) * zoom;
            p0 = BorderPoint(from, ClampInto(from, ca + left * off), dir);
            p3 = BorderPoint(to, ClampInto(to, cb + left * off), -dir);
        }

        return new LinkPath(new[] { Arc(p0, p3, lane, zoom) }, hasArrowhead: true);
    }

    /// <summary>A self-transition: a loop on the node's top-right corner, larger per lane.</summary>
    public static LinkPath SelfLoop(RectF node, int lane = 0, float zoom = 1f)
    {
        float s = 1f + 0.6f * lane;
        float dx = MathF.Min(node.Width * 0.3f, 30f * zoom) * s;
        float dy = MathF.Min(node.Height * 0.3f, 20f * zoom) * s;
        var p0 = new Vector2(node.Max.X - dx, node.Min.Y);
        var p3 = new Vector2(node.Max.X, node.Min.Y + dy);
        float r = 36f * zoom * s;
        var seg = new BezierSegment(p0, p0 + new Vector2(0f, -r), p3 + new Vector2(r, 0f), p3);
        return new LinkPath(new[] { seg }, isSelfLoop: true, hasArrowhead: true);
    }

    /// <summary>The preview while dragging a new link from <paramref name="from"/> to a free point.</summary>
    public static LinkPath ToPoint(RectF from, Vector2 target, float zoom = 1f)
    {
        Vector2 p0 = from.Contains(target)
            ? NearestBorderPoint(from, target)
            : BorderPoint(from, from.Center, SafeNormalize(target - from.Center));
        return new LinkPath(new[] { Arc(p0, target, 0, zoom) }, hasArrowhead: true);
    }

    private static LinkPath ThroughWaypoints(RectF from, RectF to, IReadOnlyList<Vector2> wps, float zoom)
    {
        var segs = new List<BezierSegment>(wps.Count + 1);
        var p0 = BorderPoint(from, from.Center, SafeNormalize(wps[0] - from.Center));
        var prev = p0;
        foreach (var wp in wps)
        {
            segs.Add(Straight(prev, wp));
            prev = wp;
        }
        var p3 = BorderPoint(to, to.Center, SafeNormalize(prev - to.Center));
        segs.Add(Straight(prev, p3));
        return new LinkPath(segs, hasArrowhead: true);
    }

    private static BezierSegment Arc(Vector2 p0, Vector2 p3, int lane, float zoom)
    {
        var chord = p3 - p0;
        float len = chord.Length();
        if (len < 1e-3f) return Straight(p0, p3);
        var n = LeftNormal(chord / len);
        float bend = MathF.Max(MinBend * zoom, BendPerLength * len) + BendLaneStep * zoom * lane;
        return new BezierSegment(p0, p0 + chord / 3f + n * bend, p0 + chord * (2f / 3f) + n * bend, p3);
    }

    private static BezierSegment Straight(Vector2 a, Vector2 b)
        => new(a, a + (b - a) / 3f, a + (b - a) * (2f / 3f), b);

    // ── geometry helpers ──────────────────────────────────────────────────────

    /// <summary>The left normal of travel in screen space (y down): travelling right, "left" is up.</summary>
    internal static Vector2 LeftNormal(Vector2 dir) => new(dir.Y, -dir.X);

    internal static Vector2 SafeNormalize(Vector2 v)
    {
        float len = v.Length();
        return len < 1e-6f ? new Vector2(1f, 0f) : v / len;
    }

    private static Vector2 ClampInto(RectF r, Vector2 p) => new(
        Math.Clamp(p.X, r.Min.X, r.Max.X),
        Math.Clamp(p.Y, r.Min.Y, r.Max.Y));

    /// <summary>Where a ray from <paramref name="origin"/> (inside <paramref name="r"/>) along <paramref name="dir"/> leaves the rect.</summary>
    internal static Vector2 BorderPoint(RectF r, Vector2 origin, Vector2 dir)
    {
        float tx = dir.X > 1e-6f ? (r.Max.X - origin.X) / dir.X
                 : dir.X < -1e-6f ? (r.Min.X - origin.X) / dir.X
                 : float.PositiveInfinity;
        float ty = dir.Y > 1e-6f ? (r.Max.Y - origin.Y) / dir.Y
                 : dir.Y < -1e-6f ? (r.Min.Y - origin.Y) / dir.Y
                 : float.PositiveInfinity;
        float t = MathF.Max(0f, MathF.Min(tx, ty));
        if (float.IsInfinity(t)) t = 0f;
        return origin + dir * t;
    }

    /// <summary>The point on <paramref name="r"/>'s border closest to <paramref name="p"/> (p inside the rect).</summary>
    internal static Vector2 NearestBorderPoint(RectF r, Vector2 p)
    {
        float dl = p.X - r.Min.X, dr = r.Max.X - p.X, dt = p.Y - r.Min.Y, db = r.Max.Y - p.Y;
        float m = MathF.Min(MathF.Min(dl, dr), MathF.Min(dt, db));
        if (m == dl) return new Vector2(r.Min.X, p.Y);
        if (m == dr) return new Vector2(r.Max.X, p.Y);
        if (m == dt) return new Vector2(p.X, r.Min.Y);
        return new Vector2(p.X, r.Max.Y);
    }
}
