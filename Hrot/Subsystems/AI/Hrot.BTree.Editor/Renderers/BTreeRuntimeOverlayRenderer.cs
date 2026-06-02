using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using Fbt;
using ImGuiNET;
using Hrot.BTree.Editor.Debug;
using NodeEditor.Core.Canvas;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.BTree.Editor.Renderers;

/// <summary>
/// Custom canvas renderer that draws a runtime overlay when a BTree debug session is active.
/// Renders:
///   - Pulsing gold outline on the currently running node.
///   - Dimmed gold outlines on stack-ancestry nodes.
///   - Status glyphs (OK/X/~) on recently executed nodes.
/// Registered at the AfterNodes pass so overlays appear above node bodies.
/// </summary>
public sealed class BTreeRuntimeOverlayRenderer : ICustomCanvasRenderer
{
    // Colors used for overlay drawing.
    private static readonly Vector4 RunningColor  = new(1.00f, 0.85f, 0.00f, 0.90f);  // gold
    private static readonly Vector4 AncestorColor = new(1.00f, 0.85f, 0.00f, 0.45f);  // dim gold
    private static readonly Vector4 SuccessColor  = new(0.20f, 0.80f, 0.20f, 0.80f);  // green
    private static readonly Vector4 FailureColor  = new(0.80f, 0.20f, 0.20f, 0.80f);  // red
    private static readonly Vector4 RunningGlyph  = new(1.00f, 0.80f, 0.00f, 0.80f);  // yellow
    private static readonly Vector4 AsyncBadgeColor = new(0.40f, 0.80f, 1.00f, 0.90f); // cyan-blue clock badge

    // Default node size used when no explicit size override exists.
    private static readonly Vector2 DefaultNodeSize = new(120f, 48f);

    private IBTreeDebugSession? _session;

    public string Id   => "btree.runtime_overlay";
    public CanvasRenderPass Pass => CanvasRenderPass.AfterNodes;

    /// <summary>
    /// False when no debug session is attached — the canvas skips the renderer
    /// entirely so there is no per-frame overhead during authoring.
    /// </summary>
    public bool IsActive => _session != null;

    /// <summary>
    /// Node VisualIds for which an async-pending badge was drawn in the most recent
    /// Render() call.  Reset to empty at the start of each Render().
    /// Used by unit tests that cannot inspect ImGui draw list calls directly.
    /// </summary>
    internal List<Guid> LastRenderedAsyncBadgeNodeIds { get; } = new();

    /// <summary>Attaches or detaches the debug session used for overlay data.</summary>
    public void SetSession(IBTreeDebugSession? session) => _session = session;

    public void Render(ICanvasRenderContext ctx)
    {
        // Reset per-frame async-badge observable.
        LastRenderedAsyncBadgeNodeIds.Clear();

        var snapshot = _session?.GetCurrentStateSnapshot();
        if (snapshot is null) return;

        // 1. Gold outline on the running node.
        if (snapshot.RunningElementId.HasValue)
        {
            var nodeId  = new NodeId(snapshot.RunningElementId.Value);
            var nodeModel = ctx.Graph.FindNode(nodeId);
            if (nodeModel is not null)
                DrawNodeOutline(ctx, nodeModel.Position, RunningColor, 2.5f);
        }

        // 2. Dimmed gold outlines on stack ancestry (innermost = brighter).
        for (int i = 0; i < snapshot.StackPointer; i++)
        {
            var elementId = snapshot.StackElementIds[i];
            if (elementId is null) continue;
            var nodeModel = ctx.Graph.FindNode(new NodeId(elementId.Value));
            if (nodeModel is null) continue;
            float alpha = 0.4f + 0.6f * (i + 1) / snapshot.StackPointer;
            var color = AncestorColor with { W = AncestorColor.W * alpha };
            DrawNodeOutline(ctx, nodeModel.Position, color, 1.5f);
        }

        // 3. Status glyphs on recently executed nodes (skipped at low zoom).
        if (!ctx.IsLowZoom)
        {
            foreach (var executed in _session!.GetRecentNodeHistory(50))
            {
                if (executed.AssetId != snapshot.AssetId) continue;
                var nodeModel = ctx.Graph.FindNode(new NodeId(executed.NodeVisualId));
                if (nodeModel is null) continue;

                (string glyph, Vector4 color) = executed.Status switch
                {
                    NodeStatus.Success => ("OK", SuccessColor),
                    NodeStatus.Failure => ("X",  FailureColor),
                    _                  => ("~",  RunningGlyph),
                };

                // Draw glyph near the top-right corner of the node.
                var glyphPos = ctx.Viewport.GraphToScreen(
                    nodeModel.Position + new Vector2(DefaultNodeSize.X - 18f, 4f));
                ctx.DrawList.AddText(glyphPos, ImGui.GetColorU32(color), glyph);
            }
        }

        // 4. Async-pending clock badges on nodes with pending async operations.
        // Per design SS12.4 step 4: call GetRecentAsyncHistory -> DrawAsyncBadge for
        // each Issued (still-pending) entry that belongs to the currently displayed asset.
        foreach (var evt in _session!.GetRecentAsyncHistory())
        {
            if (evt.Phase != BTreeAsyncPhase.Issued) continue;
            if (evt.AssetId != snapshot.AssetId) continue;
            var asyncNodeModel = ctx.Graph.FindNode(new NodeId(evt.NodeVisualId));
            if (asyncNodeModel is null) continue;
            LastRenderedAsyncBadgeNodeIds.Add(evt.NodeVisualId);
            DrawAsyncBadge(ctx, asyncNodeModel.Position);
        }
    }

    private static void DrawNodeOutline(
        ICanvasRenderContext ctx,
        Vector2 nodeCanvasPos,
        Vector4 color,
        float thickness)
    {
        var dl = ctx.DrawList;
        if (Unsafe.As<ImDrawListPtr, nint>(ref dl) == 0) return;
        var min = ctx.Viewport.GraphToScreen(nodeCanvasPos);
        var max = ctx.Viewport.GraphToScreen(nodeCanvasPos + DefaultNodeSize);
        dl.AddRect(min, max, ImGui.GetColorU32(color),
            rounding: 4f * ctx.Zoom,
            flags: ImDrawFlags.None,
            thickness: thickness * ctx.Zoom);
    }

    // Draws a small cyan-blue clock badge in the bottom-right corner of the node.
    // Guards against a null DrawList so headless tests can assert the observable
    // LastRenderedAsyncBadgeNodeIds without needing a live ImGui context.
    private static void DrawAsyncBadge(ICanvasRenderContext ctx, Vector2 nodeCanvasPos)
    {
        var screenPos = ctx.Viewport.GraphToScreen(
            nodeCanvasPos + new Vector2(DefaultNodeSize.X - 14f, DefaultNodeSize.Y - 14f));
        var dl = ctx.DrawList;
        if (Unsafe.As<ImDrawListPtr, nint>(ref dl) == 0) return;
        dl.AddText(screenPos, ImGui.GetColorU32(AsyncBadgeColor), "o");
    }
}
