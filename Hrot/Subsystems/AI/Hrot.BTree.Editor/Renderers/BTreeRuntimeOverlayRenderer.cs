using System.Numerics;
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

    // Default node size used when no explicit size override exists.
    private static readonly Vector2 DefaultNodeSize = new(120f, 48f);

    private IBTreeDebugSession? _session;

    public string Id   => "btree.runtime_overlay";
    public CanvasRenderPass Pass => CanvasRenderPass.AfterNodes;

    /// <summary>Attaches or detaches the debug session used for overlay data.</summary>
    public void SetSession(IBTreeDebugSession? session) => _session = session;

    public void Render(ICanvasRenderContext ctx)
    {
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
        if (ctx.IsLowZoom) return;
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

    private static void DrawNodeOutline(
        ICanvasRenderContext ctx,
        Vector2 nodeCanvasPos,
        Vector4 color,
        float thickness)
    {
        var min = ctx.Viewport.GraphToScreen(nodeCanvasPos);
        var max = ctx.Viewport.GraphToScreen(nodeCanvasPos + DefaultNodeSize);
        ctx.DrawList.AddRect(min, max, ImGui.GetColorU32(color),
            rounding: 4f * ctx.Zoom,
            flags: ImDrawFlags.None,
            thickness: thickness * ctx.Zoom);
    }
}
