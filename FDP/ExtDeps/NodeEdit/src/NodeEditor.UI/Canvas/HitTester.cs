using System;
using System.Collections.Generic;
using System.Numerics;
using NodeEditor.Core.Canvas;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.Layout;
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
    // Screen-space half-width of a wire's clickable band. Widened from 6px so the "active" hover/select
    // zone is easier to find (editor punch-list #13) without being so broad it steals clicks from
    // nearby wires/pins.
    private const float WireHitDistancePx = 9f;
    private const int   WireSampleCount   = 24;

    /// <summary>CE-1000: width (screen px) of the band just inside a node's border that starts a link.</summary>
    internal const float NodeEdgeBandPx = 8f;

    /// <summary>CE-1004: half-height (screen px) of the clickable line between two bands.</summary>
    internal const float RegionDividerHitPx = 4f;
    /// <summary>CE-1004: size (px at zoom 1) of the container's bottom-right resize grip.</summary>
    internal const float ContainerGripPx = 12f;

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
    internal const int ZLayerNodeEdge          = 67;   // CE-1000: link-start band (node-to-node routing only)
    internal const int ZLayerContainerResize   = 68;   // CE-1004: region divider + container corner grip
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
        Dictionary<LinkId, LinkPath> linkPaths,
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
            if (!linkPaths.TryGetValue(link.Id, out var path)) continue;

            // CE-1000: the clickable band is sampled from the SAME path the wire renderer draws.
            if (path.DistanceTo(mouse, WireSampleCount) <= WireHitDistancePx)
                SubmitHit(new HoverInfo { Kind = HoverKind.Link, Link = link.Id }, ZLayerWire, wireIndex, 1);
        }

        // 3. Custom AfterWires
        SubmitCustomHits(view.Host.CustomCanvasRenderers, CanvasRenderPass.AfterWires, mouseGraph, hitCtx, ZLayerAfterWires, 0, SubmitHit);

        // 4. Unified Nodes, Pins, Attachments, Containers
        int nodeIndex = 0;
        float pinHitRadius = MathF.Max(10f, 7.5f * view.Viewport.Zoom);
        float containerHeaderHtPx = view.Host.Theme.NodeHeaderHeight * view.Viewport.Zoom;
        float collapseArrowWidthPx = 18f * view.Viewport.Zoom;
        bool nodeToNode = view.Model.Kind.Routing == LinkRouting.NodeToNode;

        foreach (var node in view.Model.Nodes)
        {
            nodeIndex++;
            bool isForeground = view.Selection.Contains(SelectionEntry.OfNode(node.Id))
                || view.Interaction.DragOverridePositions.ContainsKey(node.Id);

            // Critical architecture: Ties the node's visual Z-order directly to its hit-test sub-layer.
            int nodeSubLayer = isForeground ? nodeIndex + 100000 : nodeIndex;

            var bounds = spatialIndex.GetBounds(node.Id);

            // CE-1000: in node-to-node routing a band just inside the node's border starts a link. It sits above the
            // node body / container header (so it wins there) and below pins, wires and attachments.
            if (nodeToNode && nodeScreenRects.TryGetValue(node.Id, out var edgeRect)
                && IsInBorderBand(mouse, edgeRect, NodeEdgeBandPx))
            {
                SubmitHit(new HoverInfo { Kind = HoverKind.NodeEdge, Node = node.Id }, ZLayerNodeEdge, nodeSubLayer, 1);
            }

            if (node.AsContainer() is { } container)
            {
                if (nodeScreenRects.TryGetValue(node.Id, out var containerScreenRect))
                {
                    // ⭐ CE-1004 (R1): the corner grip resizes the container; the line between two bands resizes the
                    //    upper (left) band. Both win over the header / interior and the link-start border band.
                    if (!container.IsCollapsed)
                    {
                        float grip = MathF.Max(8f, ContainerGripPx * view.Viewport.Zoom);
                        var cornerRect = new RectF(containerScreenRect.Max - new Vector2(grip, grip), new Vector2(grip, grip));
                        if (cornerRect.Contains(mouse))
                            SubmitHit(new HoverInfo { Kind = HoverKind.Container, Node = node.Id,
                                ContainerZone = ContainerHoverZone.ResizeEdge, ContainerEdge = ContainerResizeEdge.Corner },
                                ZLayerContainerResize, nodeSubLayer, 1);

                        if (container.Regions.Count > 1 && containerScreenRect.Contains(mouse))
                        {
                            var strips = RegionLayoutComputer.Compute(
                                container, view.Model, id => spatialIndex.GetBounds(id)?.Size,
                                containerScreenRect, containerHeaderHtPx, 1f, view.Viewport.Zoom,
                                preferredOverride: i => view.Interaction.RegionSizePreview(node.Id, i));
                            bool horiz = container.RegionOrientation == RegionLayoutOrientation.HorizontalStack;
                            for (int i = 0; i < strips.Count - 1; i++)
                            {
                                var end = strips[i].Min + strips[i].Size;
                                float d = horiz ? MathF.Abs(mouse.X - end.X) : MathF.Abs(mouse.Y - end.Y);
                                if (d <= RegionDividerHitPx)
                                {
                                    SubmitHit(new HoverInfo { Kind = HoverKind.Container, Node = node.Id,
                                        ContainerZone = ContainerHoverZone.RegionDivider, ContainerRegionIndex = i },
                                        ZLayerContainerResize, nodeSubLayer, 2);
                                    break;
                                }
                            }
                        }
                    }

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

                // Pins — CE-1000: not in node-to-node graphs, where the border band starts links and pins are
                // only the link's identity (an invisible pin must never be the thing a user has to aim at).
                foreach (var pin in nodeToNode ? Array.Empty<IPinModel>() : node.Pins)
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

    /// <summary>True when <paramref name="p"/> is inside <paramref name="r"/> and within <paramref name="band"/> of its border.</summary>
    internal static bool IsInBorderBand(Vector2 p, RectF r, float band)
    {
        if (!r.Contains(p)) return false;
        float d = MathF.Min(MathF.Min(p.X - r.Min.X, r.Max.X - p.X), MathF.Min(p.Y - r.Min.Y, r.Max.Y - p.Y));
        return d <= band;
    }

    /// <summary>Kept as the name the wire-tangent rails use; the geometry now lives in <see cref="LinkPathBuilder"/>.</summary>
    internal static (Vector2 c1, Vector2 c2) WireTangents(Vector2 a, Vector2 b, PinOrientation orientation = PinOrientation.Horizontal, float zoom = 1f)
        => LinkPathBuilder.PinWireTangents(a, b, orientation, zoom);
}
