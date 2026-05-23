using System.Numerics;
using ImGuiNET;
using Hrot.Hsm.Editor.Model;
using NodeEditor.Core.Canvas;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Hsm.Editor.Renderers;

// Renders visual glyphs for history pseudo-states (H, H*) and final states (F).
// Draws a circle with a letter at the center of each such state node.
// Runs in AfterNodes pass so it overlays the node body.
public sealed class HsmHistoryGlyphsRenderer : ICustomCanvasRenderer
{
    // Default node size used for center computation when no size override exists.
    private static readonly Vector2 DefaultNodeSize = new(120f, 40f);

    private readonly HsmAsset _asset;

    public string Id => "hsm.history_glyphs";
    public CanvasRenderPass Pass => CanvasRenderPass.AfterNodes;

    public HsmHistoryGlyphsRenderer(HsmAsset asset)
    {
        _asset = asset;
    }

    public void Render(ICanvasRenderContext ctx)
    {
        foreach (var state in _asset.AllStates)
        {
            if (state == _asset.RootState) continue;
            if (!state.IsHistory && !state.IsDeepHistory && !state.IsFinal) continue;

            string label;
            if (state.IsDeepHistory)
                label = "H*";
            else if (state.IsHistory)
                label = "H";
            else
                label = "F";

            var size = state.SizeOverride ?? DefaultNodeSize;
            var center = ctx.Viewport.GraphToScreen(state.Position + size * 0.5f);
            float radius = 12f * ctx.Zoom;

            // Filled circle background.
            var fillColor = new Vector4(0.1f, 0.1f, 0.15f, 0.70f);
            ctx.DrawList.AddCircleFilled(center, radius, ImGui.GetColorU32(fillColor));

            // Circle outline.
            var outlineColor = ctx.Theme.TextDefault;
            float outlineThickness = 2f * ctx.Zoom;
            ctx.DrawList.AddCircle(center, radius, ImGui.GetColorU32(outlineColor), 16, outlineThickness);

            // Label text, offset to approximate centering.
            var textOffset = new Vector2(-label.Length * 3.5f, -6f) * ctx.Zoom;
            ctx.DrawList.AddText(center + textOffset, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 1f)), label);

            // Selection highlight.
            if (ctx.Selection.Nodes.Contains(new NodeId(state.StableId)))
            {
                var selColor = new Vector4(0.4f, 0.8f, 1.0f, 1.0f);
                ctx.DrawList.AddCircle(center, radius, ImGui.GetColorU32(selColor), 16, 3f * ctx.Zoom);
            }
        }
    }
}
