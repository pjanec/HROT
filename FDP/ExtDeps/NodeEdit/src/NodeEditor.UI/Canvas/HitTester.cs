using System;
using System.Collections.Generic;
using System.Numerics;
using NodeEditor.Core.Canvas;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.Spatial;
using NodeEditor.Core.View;
using NodeEditor.Primitives;

namespace NodeEditor.UI.Canvas;

/// <summary>
/// Performs per-frame hit-testing against the spatial index and pin positions
/// and updates <see cref="InteractionState.Hover"/>.
/// Priority respects the strict visual Z-order of canvas elements.
/// </summary>
internal sealed class HitTester
{
    private const float RerouteHitRadiusPx = 8f;
    private const float WireHitDistancePx = 6f;
    private const int   WireSampleCount   = 24;

    // Visual Z-Layers. Higher value = later paint = wins hit test.
    // Ordering (low to high):
    //   BeforeContent < CommentBody < ContainerInterior < AfterWires < NodeBody
    //   < CommentHeader < ContainerHeader < ContainerChevron < TopMost
    //   < Attachment < Wire < Pin < Reroute
    internal const int ZLayerBeforeContent     = 10;
    internal const int ZLayerCommentBody       = 20;
    internal const int ZLayerContainerInterior = 30;
    internal const int ZLayerAfterWires        = 35;
    internal const int ZLayerNodeBody          = 40;   // Same element group as old ZLayerNodeElement
    internal const int ZLayerNodeElement       = 40;   // Alias kept for internal callsites
    internal const int ZLayerCommentHeader     = 50;
    internal const int ZLayerContainerHeader   = 60;
    internal const int ZLayerContainerChevron  = 65;
    internal const int ZLayerTopMost           = 70;
    internal const int ZLayerAttachment        = 80;
    internal const int ZLayerWire              = 90;
    internal const int ZLayerAfterNodes        = 95;
    internal const int ZLayerPin               = 100;
    internal const int ZLayerReroute           = 110;

    public void UpdateHover(
        GraphView view,
        SpatialIndex spatialIndex,
        Dictionary<PinId, Vector2> pinPositions,
        Dictionary<AttachmentId, RectF> attachmentScreenRects,
        Dictionary<NodeId, RectF> nodeScreenRects,
        IHitTestContext hitCtx)
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

            var min = view.Viewport.GraphToScreen(comment.Position);
            var max = view.Viewport.GraphToScreen(comment.Position + comment.Size);
            var cx = (min.X + max.X) * 0.5f;
            var cy = (min.Y + max.Y) * 0.5f;
            Vector2[] handles =
            {
                new(min.X, min.Y), new(cx,    min.Y), new(max.X, min.Y),
                new(min.X, cy),                       new(max.X, cy),
                new(min.X, max.Y), new(cx,    max.Y), new(max.X, max.Y),
            };

            int hitHandleIndex = -1;
            for (int i = 0; i < handles.Length; i++)
            {
                if (Vector2.Distance(mouse, handles[i]) <= 8f)
                {
                    hitHandleIndex = i;
                    break;
                }
            }

            if (hitHandleIndex >= 0)
                SubmitHit(new HoverInfo { Kind = HoverKind.Comment, Comment = comment.Id, CommentZone = CommentHoverZone.ResizeHandle, CommentResizeHandle = hitHandleIndex }, ZLayerCommentHeader, subLayer, 1);
            else if (headerRect.Contains(mouseGraph))
                SubmitHit(new HoverInfo { Kind = HoverKind.Comment, Comment = comment.Id, CommentZone = CommentHoverZone.Header }, ZLayerCommentHeader, subLayer, 2);
            else if (bodyRect.Contains(mouseGraph))
                SubmitHit(new HoverInfo { Kind = HoverKind.Comment, Comment = comment.Id, CommentZone = CommentHoverZone.Body }, ZLayerCommentBody, subLayer, 1);
        }

        // 2. Wires
        int wireIndex = 0;
        foreach (var link in view.Model.Links)
        {
            wireIndex++;
            if (!pinPositions.TryGetValue(link.FromPin, out var a)) continue;
            if (!pinPositions.TryGetValue(link.ToPin, out var b)) continue;

            if (HitsWire(mouse, a, b, link, view.Viewport, view.Model.Kind.Orientation))
                SubmitHit(new HoverInfo { Kind = HoverKind.Link, Link = link.Id }, ZLayerWire, wireIndex, 1);
        }

        // 3. Custom AfterWires
        SubmitCustomHits(view.Host.CustomCanvasRenderers, CanvasRenderPass.AfterWires, mouseGraph, hitCtx, ZLayerAfterWires, 0, SubmitHit);

        // 4. Unified Nodes, Pins, Attachments, Containers
        int nodeIndex = 0;
        float pinHitRadius = MathF.Max(10f, 7.5f * view.Viewport.Zoom);
        float containerHeaderHtPx = view.Host.Theme.NodeHeaderHeight * view.Viewport.Zoom;
        float collapseArrowWidthPx = 18f * view.Viewport.Zoom;

        foreach (var node in view.Model.Nodes)
        {
            nodeIndex++;
            bool isForeground = view.Selection.Contains(SelectionEntry.OfNode(node.Id))
                || view.Interaction.DragOverridePositions.ContainsKey(node.Id);

            // Critical architecture: Ties the node's visual Z-order directly to its hit-test sub-layer.
            int nodeSubLayer = isForeground ? nodeIndex + 100000 : nodeIndex;

            var bounds = spatialIndex.GetBounds(node.Id);

            if (node.AsContainer() is { } container)
            {
                if (nodeScreenRects.TryGetValue(node.Id, out var containerScreenRect))
                {
                    var headerScreenRect = new RectF(
                        containerScreenRect.Min,
                        new Vector2(containerScreenRect.Size.X, containerHeaderHtPx));

                    if (headerScreenRect.Contains(mouse))
                    {
                        var arrowScreenRect = new RectF(
                            containerScreenRect.Min,
                            new Vector2(collapseArrowWidthPx, containerHeaderHtPx));

                        if (arrowScreenRect.Contains(mouse))
                        {
                            SubmitHit(new HoverInfo { Kind = HoverKind.Container, Node = node.Id, ContainerZone = ContainerHoverZone.CollapseArrow },
                                ZLayerContainerHeader, nodeSubLayer, 1);
                        }
                        else
                        {
                            SubmitHit(new HoverInfo { Kind = HoverKind.Container, Node = node.Id, ContainerZone = ContainerHoverZone.Header },
                                ZLayerContainerHeader, nodeSubLayer, 2);
                        }
                    }
                    else if (bounds.HasValue && bounds.Value.Contains(mouseGraph))
                    {
                        float headerHtGu = view.Host.Theme.NodeHeaderHeight;
                        if (mouseGraph.Y >= bounds.Value.Min.Y + headerHtGu)
                        {
                            SubmitHit(new HoverInfo { Kind = HoverKind.Container, Node = node.Id, ContainerZone = ContainerHoverZone.Interior },
                                ZLayerContainerInterior, nodeSubLayer, 1);
                        }
                    }
                }
            }
            else
            {
                // Attachments
                var attachments = view.Model.GetAttachmentsForNode(node.Id);
                foreach (var att in attachments)
                {
                    if (attachmentScreenRects.TryGetValue(att.Id, out var attRect) && attRect.Contains(mouse))
                    {
                        SubmitHit(new HoverInfo { Kind = HoverKind.Attachment, Attachment = att.Id }, ZLayerNodeElement, nodeSubLayer, 2);
                    }
                }

                // Node Body
                if (bounds.HasValue && bounds.Value.Contains(mouseGraph))
                {
                    SubmitHit(new HoverInfo { Kind = HoverKind.Node, Node = node.Id }, ZLayerNodeElement, nodeSubLayer, 3);
                }

                // Pins
                foreach (var pin in node.Pins)
                {
                    if (!pinPositions.TryGetValue(pin.Id, out var screenPos)) continue;
                    if (Vector2.Distance(mouse, screenPos) <= pinHitRadius)
                    {
                        SubmitHit(new HoverInfo { Kind = HoverKind.Pin, Pin = pin.Id }, ZLayerPin, nodeSubLayer, 1);
                    }
                }
            }
        }

        // 5. Custom AfterNodes
        SubmitCustomHits(view.Host.CustomCanvasRenderers, CanvasRenderPass.AfterNodes, mouseGraph, hitCtx, ZLayerAfterNodes, 0, SubmitHit);

        // 6. Reroutes
        int rerouteIndex = 0;
        foreach (var link in view.Model.Links)
        {
            rerouteIndex++;
            for (int wi = 0; wi < link.Waypoints.Count; wi++)
            {
                var rr = new RerouteRef(link.Id, wi);
                var wpGraph = view.Interaction.RerouteDragOverridePositions.TryGetValue(rr, out var ovr) ? ovr : link.Waypoints[wi];
                var pt = view.Viewport.GraphToScreen(wpGraph);
                if (Vector2.Distance(mouse, pt) <= RerouteHitRadiusPx)
                {
                    SubmitHit(
                        new HoverInfo
                        {
                            Kind = HoverKind.Reroute,
                            Reroute = rr,
                        },
                        ZLayerWire, rerouteIndex, 0);
                }
            }
        }

        // 7. Custom TopMost
        SubmitCustomHits(view.Host.CustomCanvasRenderers, CanvasRenderPass.TopMost, mouseGraph, hitCtx, ZLayerTopMost, 0, SubmitHit);

        // 8. Custom BeforeContent
        SubmitCustomHits(view.Host.CustomCanvasRenderers, CanvasRenderPass.BeforeContent, mouseGraph, hitCtx, ZLayerBeforeContent, 0, SubmitHit);

        view.Interaction.Hover = hasBestHit ? bestHit : HoverInfo.None;
    }

    private static void SubmitCustomHits(
        IReadOnlyList<ICustomCanvasRenderer> renderers,
        CanvasRenderPass pass,
        Vector2 mouseGraph,
        IHitTestContext hitCtx,
        int zLayer,
        int subLayerBase,
        System.Action<HoverInfo, int, int, int> submitHit)
    {
        int count = renderers.Count;
        for (int i = count - 1; i >= 0; i--)
        {
            var renderer = renderers[i];
            if (renderer.Pass != pass || !renderer.IsActive) continue;
            if (renderer is not ICustomCanvasHitTester hitTester) continue;
            var result = hitTester.HitTest(mouseGraph, hitCtx);
            if (result is not null)
            {
                int subLayer = subLayerBase + (count - 1 - i);
                submitHit(
                    new HoverInfo { Kind = HoverKind.CustomElement, CustomElement = new CustomElementRef(renderer.Id, result.Value.ElementKey) },
                    zLayer, subLayer, 1);
            }
        }
    }

    private static bool HitsWire(Vector2 mouse, Vector2 a, Vector2 b, ILinkModel link, ViewportState viewport, PinOrientation orientation)
    {
        var waypoints = link.Waypoints;
        if (waypoints.Count == 0)
        {
            return BezierHit(mouse, a, b, orientation);
        }

        var prev = a;
        for (int i = 0; i < waypoints.Count; i++)
        {
            var wpt = viewport.GraphToScreen(waypoints[i]);
            if (BezierHit(mouse, prev, wpt, orientation)) return true;
            prev = wpt;
        }
        return BezierHit(mouse, prev, b, orientation);
    }

    private static bool BezierHit(Vector2 mouse, Vector2 a, Vector2 b, PinOrientation orientation = PinOrientation.Horizontal)
    {
        var (c1, c2) = WireTangents(a, b, orientation);
        for (int s = 0; s <= WireSampleCount; s++)
        {
            float t = s / (float)WireSampleCount;
            var pt = BezierPoint(a, c1, c2, b, t);
            if (Vector2.DistanceSquared(mouse, pt) <= WireHitDistancePx * WireHitDistancePx)
                return true;
        }
        return false;
    }

    internal static (Vector2 c1, Vector2 c2) WireTangents(Vector2 a, Vector2 b, PinOrientation orientation = PinOrientation.Horizontal)
    {
        if (orientation == PinOrientation.Vertical)
        {
            // Pins face along Y: the From/output pin (a) is on the node's top edge
            // and faces up; the To/input pin (b) is on the bottom edge and faces
            // down. Tangents leave/enter vertically so the spline doesn't sprout
            // sideways like a horizontal (Blueprint) wire.
            float dy = MathF.Abs(b.Y - a.Y);
            float tangentV = MathF.Max(50f, dy * 0.5f);
            return (a - new Vector2(0f, tangentV), b + new Vector2(0f, tangentV));
        }

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
