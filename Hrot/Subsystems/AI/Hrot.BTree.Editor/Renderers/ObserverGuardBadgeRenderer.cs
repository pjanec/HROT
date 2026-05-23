using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;
using Hrot.BTree.Editor.Host;
using NodeEditor.Core.Canvas;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.BTree.Editor.Renderers;

// Custom canvas renderer for the BTree editor.
// Draws "OBSERVES" badges on links from ObserverSelector nodes to their guard children.
// Runs at the AfterWires pass so badges appear on top of link wires but below nodes.
public sealed class ObserverGuardBadgeRenderer : ICustomCanvasRenderer
{
    // Badge background color (semi-transparent blue-gray).
    private static readonly Vector4 BadgeBgColor = new(0.15f, 0.45f, 0.75f, 0.75f);
    // Badge text color (white).
    private static readonly Vector4 BadgeTextColor = new(1f, 1f, 1f, 1f);

    public string Id => "btree.observer_guard_badges";
    public CanvasRenderPass Pass => CanvasRenderPass.AfterWires;

    public void Render(ICanvasRenderContext ctx)
    {
        if (ctx.IsLowZoom) return;

        foreach (var linkId in ctx.VisibleLinks)
        {
            var link = ctx.Graph.FindLink(linkId);
            if (link is null) continue;

            // From pin -> child node; To pin -> parent node (BTree reversed convention)
            var fromPin = ctx.Graph.FindPin(link.FromPin);
            var toPin   = ctx.Graph.FindPin(link.ToPin);
            if (fromPin is null || toPin is null) continue;

            var childNode  = ctx.Graph.FindNode(fromPin.OwnerNodeId);
            var parentNode = ctx.Graph.FindNode(toPin.OwnerNodeId);
            if (childNode is null || parentNode is null) continue;

            // Only badge links where parent is ObserverSelector and child is Condition.
            if (parentNode.Kind.Id != BTreeKinds.ObserverSelector) continue;
            if (childNode.Kind.Id  != BTreeKinds.Condition) continue;

            // Compute badge position: 30% along parent->child in graph space.
            var graphPos  = parentNode.Position + 0.3f * (childNode.Position - parentNode.Position);
            var screenPos = ctx.Viewport.GraphToScreen(graphPos);

            DrawBadge(ctx.DrawList, screenPos, ctx.Zoom);
        }
    }

    private static void DrawBadge(ImDrawListPtr dl, Vector2 screenPos, float zoom)
    {
        const string label = "OBSERVES";
        float fontSize = 10f * zoom;
        if (fontSize < 7f) return;   // too small to be legible

        var textSize = ImGui.CalcTextSize(label);
        float padX   = 4f * zoom;
        float padY   = 2f * zoom;
        var bgMin    = screenPos - new Vector2(padX, padY);
        var bgMax    = screenPos + textSize + new Vector2(padX, padY);

        dl.AddRectFilled(bgMin, bgMax, ImGui.GetColorU32(BadgeBgColor), 3f * zoom);
        dl.AddText(screenPos, ImGui.GetColorU32(BadgeTextColor), label);
    }
}
