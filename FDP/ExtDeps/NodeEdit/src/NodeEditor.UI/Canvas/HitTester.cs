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
/// Priority (highest first, 15-step hierarchy): reroutes, pins, wires, TopMost custom,
/// attachments, AfterNodes custom, container chevrons, container headers, comment headers,
/// AfterWires custom, node bodies, BeforeContent custom, container interiors, comment bodies.
/// </summary>
internal sealed class HitTester
{
    // How close (screen px) the cursor has to be to a reroute dot to "hit" it.
    private const float RerouteHitRadiusPx = 8f;
    // Wire hit: samples along bezier curve.
    private const float WireHitDistancePx = 6f;
    private const int   WireSampleCount   = 24;

    // Z-layer constants for the 15-step hit-test priority hierarchy (highest wins).
    // Higher value = higher priority. Empty canvas is the implicit baseline (never submitted).
    internal const int ZLayerCommentBody       = 2;
    internal const int ZLayerContainerInterior = 3;
    internal const int ZLayerBeforeContent     = 4;
    internal const int ZLayerNodeBody          = 5;
    internal const int ZLayerAfterWires        = 6;
    internal const int ZLayerCommentHeader     = 7;
    internal const int ZLayerContainerHeader   = 8;
    internal const int ZLayerContainerChevron  = 9;
    internal const int ZLayerAfterNodes        = 10;
    internal const int ZLayerAttachment        = 11;
    internal const int ZLayerTopMost           = 12;
    internal const int ZLayerWire              = 13;
    internal const int ZLayerPin               = 14;
    internal const int ZLayerReroute           = 15;

    /// <summary>Run hit-testing and store the result into <see cref="InteractionState.Hover"/>.</summary>
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
            var resizeRect = new RectF(
                comment.Position + comment.Size - new Vector2(12f, 12f),
                new Vector2(12f, 12f));

            if (resizeRect.Contains(mouseGraph))
                SubmitHit(new HoverInfo { Kind = HoverKind.Comment, Comment = comment.Id, CommentZone = CommentHoverZone.ResizeHandle }, ZLayerCommentHeader, subLayer, 1);
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

            if (HitsWire(mouse, a, b, link, view.Viewport))
                SubmitHit(new HoverInfo { Kind = HoverKind.Link, Link = link.Id }, ZLayerWire, wireIndex, 1);
        }

        // 2a. Custom AfterWires hit-testers: below wires per spec (ZLayerAfterWires < ZLayerWire).
        SubmitCustomHits(view.Host.CustomCanvasRenderers, CanvasRenderPass.AfterWires, mouseGraph, hitCtx, zLayer: ZLayerAfterWires, subLayerBase: 0, SubmitHit);

        // 2b. Attachment pills: above node bodies per spec; highest StackIndex wins when multiple overlap.
        foreach (var (attachId, screenRect) in attachmentScreenRects)
        {
            if (screenRect.Contains(mouse))
            {
                int stackIndex = view.Model.FindAttachment(attachId)?.StackIndex ?? 0;
                SubmitHit(new HoverInfo { Kind = HoverKind.Attachment, Attachment = attachId }, ZLayerAttachment, stackIndex, 1);
            }
        }

        // 2c. Container headers and collapse arrows.
        // Chevrons (ZLayerContainerChevron=9) beat headers (ZLayerContainerHeader=8) per spec.
        float containerHeaderHtPx = view.Host.Theme.NodeHeaderHeight * view.Viewport.Zoom;
        float collapseArrowWidthPx = 18f * view.Viewport.Zoom;
        int containerHeaderIndex = 0;
        foreach (var node in view.Model.Nodes)
        {
            if (node.AsContainer() is not { } container) continue;
            containerHeaderIndex++;
            if (!nodeScreenRects.TryGetValue(node.Id, out var containerScreenRect)) continue;

            var headerScreenRect = new RectF(
                containerScreenRect.Min,
                new Vector2(containerScreenRect.Size.X, containerHeaderHtPx));
            if (!headerScreenRect.Contains(mouse)) continue;

            var arrowScreenRect = new RectF(
                containerScreenRect.Min,
                new Vector2(collapseArrowWidthPx, containerHeaderHtPx));
            if (arrowScreenRect.Contains(mouse))
                SubmitHit(new HoverInfo { Kind = HoverKind.Container, Node = node.Id, ContainerZone = ContainerHoverZone.CollapseArrow },
                    ZLayerContainerChevron, containerHeaderIndex, 0);
            else
                SubmitHit(new HoverInfo { Kind = HoverKind.Container, Node = node.Id, ContainerZone = ContainerHoverZone.Header },
                    ZLayerContainerHeader, containerHeaderIndex, 1);
        }

        // 2d. Custom AfterNodes hit-testers: above container headers per spec.
        SubmitCustomHits(view.Host.CustomCanvasRenderers, CanvasRenderPass.AfterNodes, mouseGraph, hitCtx, zLayer: ZLayerAfterNodes, subLayerBase: 0, SubmitHit);

        // 3. Nodes and Pins (same sub-layer uses model draw order).
        int nodeIndex = 0;
        float pinHitRadius = MathF.Max(10f, 7.5f * view.Viewport.Zoom);
        foreach (var node in view.Model.Nodes)
        {
            nodeIndex++;
            bool isForeground = view.Selection.Contains(SelectionEntry.OfNode(node.Id))
                             || view.Interaction.DragOverridePositions.ContainsKey(node.Id);
            int nodeSubLayer = isForeground ? nodeIndex + 100000 : nodeIndex;

            // Skip containers — their header is handled by section 2c; interior by section 3b.
            if (node.IsContainerNode()) continue;

            var bounds = spatialIndex.GetBounds(node.Id);
            if (bounds.HasValue && bounds.Value.Contains(mouseGraph))
                SubmitHit(new HoverInfo { Kind = HoverKind.Node, Node = node.Id }, ZLayerNodeBody, nodeSubLayer, 2);

            foreach (var pin in node.Pins)
            {
                if (!pinPositions.TryGetValue(pin.Id, out var screenPos)) continue;
                if (Vector2.Distance(mouse, screenPos) <= pinHitRadius)
                    SubmitHit(new HoverInfo { Kind = HoverKind.Pin, Pin = pin.Id }, ZLayerPin, nodeSubLayer, 1);
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
                        zLayer: ZLayerReroute,
                        subLayer: rerouteIndex,
                        priority: 1);
                }
            }
        }

        // 4b. Custom TopMost hit-testers: above wires/attachments/nodes but below pins and reroutes per spec.
        var customRenderers = view.Host.CustomCanvasRenderers;
        SubmitCustomHits(customRenderers, CanvasRenderPass.TopMost, mouseGraph, hitCtx, zLayer: ZLayerTopMost, subLayerBase: 0, SubmitHit);

        // 3b. Container interior (body area below the header).
        // ZLayerContainerInterior(3) is below all node/attachment/wire/comment-header layers per spec.
        int containerInteriorIndex = 0;
        foreach (var node in view.Model.Nodes)
        {
            if (node.AsContainer() is not { } container) continue;
            containerInteriorIndex++;
            var bounds = spatialIndex.GetBounds(node.Id);
            if (!bounds.HasValue) continue;
            if (!bounds.Value.Contains(mouseGraph)) continue;

            // Exclude the header zone so header hit (section 2c) is not double-submitted.
            float headerHtGu = view.Host.Theme.NodeHeaderHeight;
            if (mouseGraph.Y < bounds.Value.Min.Y + headerHtGu) continue;

            SubmitHit(new HoverInfo { Kind = HoverKind.Container, Node = node.Id, ContainerZone = ContainerHoverZone.Interior },
                ZLayerContainerInterior, containerInteriorIndex, 2);
        }

        // 5. Custom BeforeContent hit-testers: lowest custom layer per spec.
        SubmitCustomHits(view.Host.CustomCanvasRenderers, CanvasRenderPass.BeforeContent, mouseGraph, hitCtx, zLayer: ZLayerBeforeContent, subLayerBase: 0, SubmitHit);

        view.Interaction.Hover = hasBestHit ? bestHit : HoverInfo.None;
    }

    // Runs ICustomCanvasHitTester.HitTest for renderers that implement it, in reverse
    // registration order (later-registered renderer wins).
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
                // Reverse index: last-registered gets highest subLayer and wins.
                int subLayer = subLayerBase + (count - 1 - i);
                submitHit(
                    new HoverInfo { Kind = HoverKind.CustomElement, CustomElement = new CustomElementRef(renderer.Id, result.Value.ElementKey) },
                    zLayer, subLayer, 1);
            }
        }
    }

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
