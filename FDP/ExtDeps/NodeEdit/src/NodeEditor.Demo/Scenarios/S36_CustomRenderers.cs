using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;
using NodeEditor.Core.Canvas;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.Demo.FakeBlueprint;
using NodeEditor.Primitives;

namespace NodeEditor.Demo.Scenarios;

/// <summary>
/// S36: Custom Canvas Renderers
/// Demonstrates all four CanvasRenderPass injection points using four
/// minimal renderers:
///   - BeforeContent: tints two rectangular regions of the canvas.
///   - AfterWires:    labels midpoints of all links.
///   - AfterNodes:    draws warning lines between two marker positions.
///   - TopMost:       shows a cursor-following debug tooltip.
/// </summary>
public sealed class S36_CustomRenderers : Scenario
{
    public override string Name        => "36 -- Custom Canvas Renderers";
    public override string Description => "Four-pass custom renderer demo: region tints, wire labels, warning lines, mouse tooltip.";

    public override void SetupHost(FakeHostServices host)
    {
        host.CustomRenderers.Add(new RegionTintRenderer());
        host.CustomRenderers.Add(new WireLabelRenderer());
        host.CustomRenderers.Add(new WarningLineRenderer());
        host.CustomRenderers.Add(new TooltipRenderer());
    }

    public override void Build(GraphView view, FakeGraphModel graph, FakeCommandSink sink, FakeNodeCatalog catalog)
    {
        // Add a few nodes so there are wires and nodes to draw over.
        var n1 = AddNode(graph, catalog, "Event.BeginPlay", new Vector2(50f, 80f));
        var n2 = AddNode(graph, catalog, "Util.Print",      new Vector2(250f, 80f));
        var n3 = AddNode(graph, catalog, "Flow.Delay",      new Vector2(450f, 80f));
        var n4 = AddNode(graph, catalog, "Util.Print",      new Vector2(250f, 220f));

        LinkNodes(graph, n1, 0, n2, 0);
        LinkNodes(graph, n2, 0, n3, 0);
        LinkNodes(graph, n1, 0, n4, 0);
    }

    // ── BeforeContent: faint rectangular region tints ────────────────────────

    private sealed class RegionTintRenderer : ICustomCanvasRenderer
    {
        public string          Id   => "s36.region_tints";
        public CanvasRenderPass Pass => CanvasRenderPass.BeforeContent;

        public void Render(ICanvasRenderContext ctx)
        {
            // Draw two faint colored rectangles in graph space.
            DrawRect(ctx, new Vector2(-20f, 20f), new Vector2(220f, 200f), new Vector4(0.2f, 0.5f, 1.0f, 0.08f));
            DrawRect(ctx, new Vector2(230f, 20f), new Vector2(560f, 200f), new Vector4(0.8f, 0.3f, 0.1f, 0.08f));
        }

        private static void DrawRect(ICanvasRenderContext ctx, Vector2 graphMin, Vector2 graphMax, Vector4 color)
        {
            var screenMin = ctx.Viewport.GraphToScreen(graphMin);
            var screenMax = ctx.Viewport.GraphToScreen(graphMax);
            ctx.DrawList.AddRectFilled(screenMin, screenMax, ImGui.GetColorU32(color));
        }
    }

    // ── AfterWires: text labels at link midpoints ─────────────────────────────

    private sealed class WireLabelRenderer : ICustomCanvasRenderer
    {
        public string          Id   => "s36.wire_labels";
        public CanvasRenderPass Pass => CanvasRenderPass.AfterWires;

        public void Render(ICanvasRenderContext ctx)
        {
            if (ctx.IsLowZoom) return;

            int i = 0;
            foreach (var link in ctx.Graph.Links)
            {
                if (!ctx.VisibleLinks.Contains(link.Id)) continue;

                var fromPin = ctx.Graph.FindPin(link.FromPin);
                var toPin   = ctx.Graph.FindPin(link.ToPin);
                if (fromPin == null || toPin == null) continue;

                var fromNode = ctx.Graph.FindNode(fromPin.OwnerNodeId);
                var toNode   = ctx.Graph.FindNode(toPin.OwnerNodeId);
                if (fromNode == null || toNode == null) continue;

                // Approximate midpoint in graph space.
                var mid = (fromNode.Position + toNode.Position) * 0.5f + new Vector2(80f, 0f);
                var screenPos = ctx.Viewport.GraphToScreen(mid);

                var label = $"Link {i + 1}";
                ctx.DrawList.AddText(screenPos, ImGui.GetColorU32(new Vector4(1f, 1f, 0.4f, 0.85f)), label);
                i++;
            }
        }
    }

    // ── AfterNodes: warning lines between fixed positions ────────────────────

    private sealed class WarningLineRenderer : ICustomCanvasRenderer
    {
        public string          Id   => "s36.warning_lines";
        public CanvasRenderPass Pass => CanvasRenderPass.AfterNodes;

        // Two fixed graph-space pairs to connect with a warning line.
        private static readonly (Vector2 A, Vector2 B)[] Pairs =
        {
            (new Vector2(150f, 100f), new Vector2(350f, 260f)),
        };

        public void Render(ICanvasRenderContext ctx)
        {
            var color = ImGui.GetColorU32(new Vector4(1f, 0.85f, 0.1f, 0.8f));
            foreach (var (a, b) in Pairs)
            {
                var sa = ctx.Viewport.GraphToScreen(a);
                var sb = ctx.Viewport.GraphToScreen(b);
                ctx.DrawList.AddLine(sa, sb, color, 1.5f * ctx.Zoom);

                // Small diamond glyph at each end.
                float r = 4f * ctx.Zoom;
                DrawDiamond(ctx.DrawList, sa, r, color);
                DrawDiamond(ctx.DrawList, sb, r, color);
            }
        }

        private static void DrawDiamond(ImDrawListPtr dl, Vector2 center, float r, uint color)
        {
            dl.AddQuad(
                center + new Vector2(0f, -r),
                center + new Vector2(r,  0f),
                center + new Vector2(0f,  r),
                center + new Vector2(-r, 0f),
                color, 1f);
        }
    }

    // ── TopMost: cursor-following debug tooltip ───────────────────────────────

    private sealed class TooltipRenderer : ICustomCanvasRenderer
    {
        public string          Id   => "s36.cursor_tooltip";
        public CanvasRenderPass Pass => CanvasRenderPass.TopMost;

        public void Render(ICanvasRenderContext ctx)
        {
            if (ctx.IsLowZoom) return;

            var screenMouse = ImGui.GetMousePos();
            var graphMouse  = ctx.Viewport.ScreenToGraph(screenMouse);

            // Only draw when the mouse is actually over the canvas area.
            if (!ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem |
                                       ImGuiHoveredFlags.AllowWhenBlockedByPopup))
                return;

            var tip     = $"Graph ({graphMouse.X:F0}, {graphMouse.Y:F0})  zoom {ctx.Zoom:F2}";
            var offset  = new Vector2(14f, -18f);
            var pos     = screenMouse + offset;
            var bgColor = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.65f));
            var fgColor = ImGui.GetColorU32(new Vector4(0.9f, 0.9f, 0.9f, 1f));

            // Measure text to draw a background rectangle.
            var textSize = ImGui.CalcTextSize(tip);
            var pad      = new Vector2(4f, 2f);
            ctx.DrawList.AddRectFilled(pos - pad, pos + textSize + pad, bgColor, 3f);
            ctx.DrawList.AddText(pos, fgColor, tip);
        }
    }
}
