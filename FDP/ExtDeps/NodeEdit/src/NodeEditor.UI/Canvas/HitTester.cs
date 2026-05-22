using System.Numerics;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.Spatial;
using NodeEditor.Core.View;
using NodeEditor.Primitives;

namespace NodeEditor.UI.Canvas;

/// <summary>
/// Performs per-frame hit-testing against the spatial index and pin positions
/// and updates <see cref="InteractionState.Hover"/>.
/// Priority (highest first): reroutes, pins, wires, comment headers,
/// node bodies, comment bodies, empty canvas.
/// </summary>
internal sealed class HitTester
{
    // How close (screen px) the cursor has to be to a reroute dot or pin to "hit" it.
    private const float PinHitRadiusPx    = 8f;
    private const float RerouteHitRadiusPx = 8f;
    // Wire hit: samples along bezier curve.
    private const float WireHitDistancePx = 6f;
    private const int   WireSampleCount   = 24;

    /// <summary>Run hit-testing and store the result into <see cref="InteractionState.Hover"/>.</summary>
    public void UpdateHover(
        GraphView view,
        SpatialIndex spatialIndex,
        Dictionary<PinId, Vector2> pinPositions)
    {
        var mouse = view.Host.Input.MousePosition;

        // 1. Reroutes
        foreach (var link in view.Model.Links)
        {
            for (int wi = 0; wi < link.Waypoints.Count; wi++)
            {
                var pt = view.Viewport.GraphToScreen(link.Waypoints[wi]);
                if (Vector2.Distance(mouse, pt) <= RerouteHitRadiusPx)
                {
                    view.Interaction.Hover = new HoverInfo
                    {
                        Kind = HoverKind.Reroute,
                        Reroute = new RerouteRef(link.Id, wi),
                    };
                    return;
                }
            }
        }

        // 2. Pins
        foreach (var (pinId, screenPos) in pinPositions)
        {
            if (Vector2.Distance(mouse, screenPos) <= PinHitRadiusPx)
            {
                view.Interaction.Hover = new HoverInfo { Kind = HoverKind.Pin, Pin = pinId };
                return;
            }
        }

        // 3. Wires
        foreach (var link in view.Model.Links)
        {
            if (!pinPositions.TryGetValue(link.FromPin, out var a)) continue;
            if (!pinPositions.TryGetValue(link.ToPin, out var b)) continue;

            if (HitsWire(mouse, a, b, link, view.Viewport))
            {
                view.Interaction.Hover = new HoverInfo { Kind = HoverKind.Link, Link = link.Id };
                return;
            }
        }

        // 4. Comment headers / bodies (check header first for drag priority)
        var mouseGraph = view.Viewport.ScreenToGraph(mouse);
        var comments = view.Model.Comments.ToList();
        comments.Sort((a, b) => b.ZOrder.CompareTo(a.ZOrder));

        foreach (var comment in comments)
        {
            float headerHt = 20f; // approximate header height in graph units
            var headerRect = new RectF(comment.Position, new Vector2(comment.Size.X, headerHt));
            var bodyRect   = new RectF(
                comment.Position + new Vector2(0f, headerHt),
                new Vector2(comment.Size.X, comment.Size.Y - headerHt));
            var resizeRect = new RectF(
                comment.Position + comment.Size - new Vector2(12f, 12f),
                new Vector2(12f, 12f));

            if (resizeRect.Contains(mouseGraph))
            {
                view.Interaction.Hover = new HoverInfo
                {
                    Kind = HoverKind.Comment,
                    Comment = comment.Id,
                    CommentZone = CommentHoverZone.ResizeHandle,
                };
                return;
            }

            if (headerRect.Contains(mouseGraph))
            {
                view.Interaction.Hover = new HoverInfo
                {
                    Kind = HoverKind.Comment,
                    Comment = comment.Id,
                    CommentZone = CommentHoverZone.Header,
                };
                return;
            }

            if (bodyRect.Contains(mouseGraph))
            {
                view.Interaction.Hover = new HoverInfo
                {
                    Kind = HoverKind.Comment,
                    Comment = comment.Id,
                    CommentZone = CommentHoverZone.Body,
                };
                // Don't return — node bodies on top of comments take priority.
            }
        }

        // 5. Node bodies
        foreach (var nodeId in spatialIndex.QueryPoint(mouseGraph))
        {
            view.Interaction.Hover = new HoverInfo { Kind = HoverKind.Node, Node = nodeId };
            return;
        }

        // 6. Empty — keep any comment body hit found above, or clear.
        if (view.Interaction.Hover.Kind != HoverKind.Comment)
            view.Interaction.Hover = HoverInfo.None;
    }

    // ── wire hit ─────────────────────────────────────────────────────────────

    private static bool HitsWire(Vector2 mouse, Vector2 a, Vector2 b,
        ILinkModel link, ViewportState viewport)
    {
        var waypoints = link.Waypoints;
        if (waypoints.Count == 0)
        {
            return BezierHit(mouse, a, b);
        }

        // Walk all segments: a → wp0, wp0 → wp1, …, wpN → b
        var prev = a;
        for (int i = 0; i < waypoints.Count; i++)
        {
            var wpt = viewport.GraphToScreen(waypoints[i]);
            if (BezierHit(mouse, prev, wpt)) return true;
            prev = wpt;
        }
        return BezierHit(mouse, prev, b);
    }

    private static bool BezierHit(Vector2 mouse, Vector2 a, Vector2 b)
    {
        var (c1, c2) = WireTangents(a, b);
        for (int s = 0; s <= WireSampleCount; s++)
        {
            float t = s / (float)WireSampleCount;
            var pt = BezierPoint(a, c1, c2, b, t);
            if (Vector2.DistanceSquared(mouse, pt) <= WireHitDistancePx * WireHitDistancePx)
                return true;
        }
        return false;
    }

    internal static (Vector2 c1, Vector2 c2) WireTangents(Vector2 a, Vector2 b)
    {
        float dx = MathF.Abs(b.X - a.X);
        float tangent = MathF.Max(50f, dx * 0.5f);
        return (a + new Vector2(tangent, 0f), b - new Vector2(tangent, 0f));
    }

    private static Vector2 BezierPoint(Vector2 p1, Vector2 c1, Vector2 c2, Vector2 p2, float t)
    {
        float u = 1f - t;
        return u * u * u * p1
             + 3f * u * u * t * c1
             + 3f * u * t * t * c2
             + t * t * t * p2;
    }
}
