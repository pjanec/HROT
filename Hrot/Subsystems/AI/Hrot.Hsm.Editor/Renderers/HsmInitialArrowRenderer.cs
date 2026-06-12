using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;
using Hrot.Hsm.Editor.Model;
using NodeEditor.Core.Canvas;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Hsm.Editor.Renderers;

// Custom canvas renderer that draws initial-state arrows for composite states
// and highlights the LCA composite when a transition link is selected.
// Runs in the AfterNodes pass so arrows appear above node bodies.
public sealed class HsmInitialArrowRenderer : ICustomCanvasRenderer
{
    // Gold highlight for the LCA composite when a transition link is selected.
    private static readonly Vector4 LcaHighlightColor = new(1.00f, 0.85f, 0.00f, 0.55f);
    private static readonly Vector2 DefaultNodeSize = new(120f, 40f);

    // Initial-state marker rendering constants.
    internal const float MarkerGap = 24f;
    private const float MarkerRadius = 5f;
    private const float ArrowThickness = 2f;
    private const float ArrowheadArmLength = 5f;
    private static readonly Vector4 MarkerColor = new(0.75f, 0.75f, 0.75f, 1.0f);

    private readonly HsmAsset _asset;

    public HsmInitialArrowRenderer(HsmAsset asset)
    {
        _asset = asset;
    }

    public string Id => "hsm.initial_state_arrows";
    public CanvasRenderPass Pass => CanvasRenderPass.AfterNodes;
    public bool IsActive { get; set; } = true;

    public void Render(ICanvasRenderContext ctx)
    {
        // Draw initial-state markers for each composite state.
        var markers = CollectInitialMarkers(_asset);
        foreach (var marker in markers)
        {
            var childSize = marker.InitialChild.SizeOverride ?? DefaultNodeSize;
            var (circleCenter, arrowStart, arrowEnd) = ComputeMarkerGeometry(marker.InitialChild.Position, childSize);

            var screenCircle = ctx.Viewport.GraphToScreen(circleCenter);
            var screenStart  = ctx.Viewport.GraphToScreen(arrowStart);
            var screenEnd    = ctx.Viewport.GraphToScreen(arrowEnd);

            uint color = ImGui.GetColorU32(MarkerColor);
            float radius = MarkerRadius * ctx.Zoom;
            float thickness = ArrowThickness * ctx.Zoom;

            // Filled circle floating above the child's top edge.
            ctx.DrawList.AddCircleFilled(screenCircle, radius, color);

            // Arrow line from circle down to child top edge.
            ctx.DrawList.AddLine(screenStart, screenEnd, color, thickness);

            // Arrowhead — two short lines forming a "v" pointing down (tip at screenEnd).
            float headSize = ArrowheadArmLength * ctx.Zoom;
            var leftWing  = new Vector2(screenEnd.X - headSize, screenEnd.Y - headSize);
            var rightWing = new Vector2(screenEnd.X + headSize, screenEnd.Y - headSize);
            ctx.DrawList.AddLine(screenEnd, leftWing,  color, thickness);
            ctx.DrawList.AddLine(screenEnd, rightWing, color, thickness);
        }

        // LCA highlight: when exactly one transition link is selected, outline its LCA.
        foreach (var linkId in ctx.Selection.Links)
        {
            var transition = _asset.FindTransitionByVisualId(linkId.Value);
            if (transition?.Source is null || transition.Target is null) continue;

            var lca = FindLca(_asset, transition.Source, transition.Target);
            // Synthetic root has no visual representation; skip it.
            if (lca == _asset.RootState) continue;

            DrawLcaOutline(ctx, lca);
        }
    }

    private static void DrawLcaOutline(ICanvasRenderContext ctx, StateNode lca)
    {
        var size = lca.SizeOverride ?? DefaultNodeSize;
        var min  = ctx.Viewport.GraphToScreen(lca.Position);
        var max  = ctx.Viewport.GraphToScreen(lca.Position + size);
        ctx.DrawList.AddRect(min, max, ImGui.GetColorU32(LcaHighlightColor),
            rounding: 4f * ctx.Zoom,
            flags: ImDrawFlags.None,
            thickness: 1.5f * ctx.Zoom);
    }

    // Finds the Lowest Common Ancestor (deepest composite that contains both states).
    // Uses root-to-state ancestor path comparison.
    private static StateNode FindLca(HsmAsset asset, StateNode a, StateNode b)
    {
        var aPath = BuildRootToStatePath(a);
        var bPath = BuildRootToStatePath(b);
        StateNode lca = asset.RootState;
        for (int i = 0; i < Math.Min(aPath.Count, bPath.Count); i++)
        {
            if (aPath[i] == bPath[i]) lca = aPath[i];
            else break;
        }
        return lca;
    }

    // Returns the path from the root (inclusive) down to the given state (inclusive).
    private static List<StateNode> BuildRootToStatePath(StateNode state)
    {
        var path = new List<StateNode>();
        var current = (StateNode?)state;
        while (current is not null)
        {
            path.Add(current);
            current = current.Parent;
        }
        path.Reverse();
        return path;
    }

    // ── Initial-marker logic & geometry ────────────────────────────────────

    /// <summary>
    /// Collects initial-child markers from every visible composite and parallel state.
    /// The synthetic root is skipped (it has no visual body).
    /// </summary>
    internal static IReadOnlyList<InitialMarker> CollectInitialMarkers(HsmAsset asset)
    {
        var result = new List<InitialMarker>();
        foreach (var s in asset.AllStates)
        {
            if (s == asset.RootState) continue;

            if (s.IsParallel)
            {
                foreach (var r in s.RegionNodes)
                {
                    if (r.InitialChild is not null)
                        result.Add(new InitialMarker(s, r.InitialChild, r.RegionIndex));
                }
            }
            else if (s.Children.Count > 0)
            {
                var initial = FindInitialChild(s);
                if (initial is not null)
                    result.Add(new InitialMarker(s, initial, -1));
            }
        }
        return result;
    }

    /// <summary>
    /// Returns the first child of a normal composite that is marked <c>IsInitial</c>,
    /// or null when none is marked.
    /// </summary>
    private static StateNode? FindInitialChild(StateNode composite)
    {
        for (int i = 0; i < composite.Children.Count; i++)
        {
            if (composite.Children[i].IsInitial)
                return composite.Children[i];
        }
        return null;
    }

    /// <summary>
    /// Computes graph-space geometry for the initial-state marker:
    /// a filled circle floating <see cref="MarkerGap"/> above the child's top-center
    /// with the arrow pointing down to the child's top edge.
    /// </summary>
    internal static (Vector2 circleCenter, Vector2 arrowStart, Vector2 arrowEnd)
        ComputeMarkerGeometry(Vector2 childPos, Vector2 childSize)
    {
        float cx = childPos.X + childSize.X * 0.5f;
        var arrowEnd     = new Vector2(cx, childPos.Y);
        var circleCenter = new Vector2(cx, childPos.Y - MarkerGap);
        return (circleCenter, circleCenter, arrowEnd);
    }
}

/// <summary>
/// Describes a single initial-child marker to render:
/// which container (composite or parallel), which child, and the region index
/// (-1 for a normal composite).
/// </summary>
internal readonly record struct InitialMarker(StateNode Container, StateNode InitialChild, int RegionIndex);
