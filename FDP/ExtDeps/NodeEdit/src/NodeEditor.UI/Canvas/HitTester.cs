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
    // How close (screen px) the cursor has to be to a reroute dot to "hit" it.
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
        var mouseGraph = view.Viewport.ScreenToGraph(mouse);

        bool hasBestHit = false;
        var bestHit = HoverInfo.None;
        int bestZLayer = -1;
        int bestSubLayer = -1;
        int bestPriority = int.MaxValue;

        void SubmitHit(HoverInfo hit, int zLayer, int subLayer, int priority)
        {
            if (zLayer > bestZLayer
                || (zLayer == bestZLayer && subLayer > bestSubLayer)
                || (zLayer == bestZLayer && subLayer == bestSubLayer && priority < bestPriority))
            {
                hasBestHit = true;
                bestHit = hit;
                bestZLayer = zLayer;
                bestSubLayer = subLayer;
                bestPriority = priority;
            }
        }

        // 1. Comments
        foreach (var comment in view.Model.Comments)
        {
            int subLayer = comment.ZOrder;
            float headerHt = 20f;
            var headerRect = new RectF(comment.Position, new Vector2(comment.Size.X, headerHt));
            var bodyRect   = new RectF(
                comment.Position + new Vector2(0f, headerHt),
                new Vector2(comment.Size.X, comment.Size.Y - headerHt));
            var resizeRect = new RectF(
                comment.Position + comment.Size - new Vector2(12f, 12f),
                new Vector2(12f, 12f));

            if (resizeRect.Contains(mouseGraph))
                SubmitHit(new HoverInfo { Kind = HoverKind.Comment, Comment = comment.Id, CommentZone = CommentHoverZone.ResizeHandle }, 4, subLayer, 1);
            else if (headerRect.Contains(mouseGraph))
                SubmitHit(new HoverInfo { Kind = HoverKind.Comment, Comment = comment.Id, CommentZone = CommentHoverZone.Header }, 4, subLayer, 2);
            else if (bodyRect.Contains(mouseGraph))
                SubmitHit(new HoverInfo { Kind = HoverKind.Comment, Comment = comment.Id, CommentZone = CommentHoverZone.Body }, 0, subLayer, 1);
        }

        // 2. Wires
        int wireIndex = 0;
        foreach (var link in view.Model.Links)
        {
            wireIndex++;
            if (!pinPositions.TryGetValue(link.FromPin, out var a)) continue;
            if (!pinPositions.TryGetValue(link.ToPin, out var b)) continue;

            if (HitsWire(mouse, a, b, link, view.Viewport))
                SubmitHit(new HoverInfo { Kind = HoverKind.Link, Link = link.Id }, 1, wireIndex, 1);
        }

        // 3. Nodes and Pins (same sub-layer uses model draw order).
        int nodeIndex = 0;
        float pinHitRadius = MathF.Max(10f, 7.5f * view.Viewport.Zoom);
        foreach (var node in view.Model.Nodes)
        {
            nodeIndex++;
            bool isForeground = view.Selection.Contains(SelectionEntry.OfNode(node.Id))
                             || view.Interaction.DragOverridePositions.ContainsKey(node.Id);
            int zLayer = isForeground ? 3 : 2;

            var bounds = spatialIndex.GetBounds(node.Id);
            if (bounds.HasValue && bounds.Value.Contains(mouseGraph))
                SubmitHit(new HoverInfo { Kind = HoverKind.Node, Node = node.Id }, zLayer, nodeIndex, 2);

            foreach (var pin in node.Pins)
            {
                if (!pinPositions.TryGetValue(pin.Id, out var screenPos)) continue;
                if (Vector2.Distance(mouse, screenPos) <= pinHitRadius)
                    SubmitHit(new HoverInfo { Kind = HoverKind.Pin, Pin = pin.Id }, zLayer, nodeIndex, 1);
            }
        }

        // 4. Reroutes (topmost interaction layer).
        int rerouteIndex = 0;
        foreach (var link in view.Model.Links)
        {
            rerouteIndex++;
            for (int wi = 0; wi < link.Waypoints.Count; wi++)
            {
                var pt = view.Viewport.GraphToScreen(link.Waypoints[wi]);
                if (Vector2.Distance(mouse, pt) <= RerouteHitRadiusPx)
                {
                    SubmitHit(
                        new HoverInfo
                        {
                            Kind = HoverKind.Reroute,
                            Reroute = new RerouteRef(link.Id, wi),
                        },
                        zLayer: 5,
                        subLayer: rerouteIndex,
                        priority: 1);
                }
            }
        }

        view.Interaction.Hover = hasBestHit ? bestHit : HoverInfo.None;
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
