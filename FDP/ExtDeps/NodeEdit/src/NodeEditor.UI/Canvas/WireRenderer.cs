using System.Numerics;
using ImGuiNET;
using NodeEditor.Core;
using NodeEditor.Core.Canvas;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.Primitives;
using NodeEditor.UI.Util;

namespace NodeEditor.UI.Canvas;

/// <summary>
/// Draws all wires (links) and reroute waypoints. Execution wires get a
/// midpoint arrowhead; data wires don't. Selected wires are thicker.
/// Dashed wires use a shortened segment approximation.
/// </summary>
internal sealed class WireRenderer
{
    private const int BezierSegments = 0; // 0 = auto

    /// <summary>Draw all links whose endpoints or waypoints intersect <paramref name="visibleGraphRect"/>.</summary>
    public void DrawAll(
        GraphView view,
        ImDrawListPtr dl,
        Dictionary<PinId, Vector2> pinPositions,
        Dictionary<LinkId, LinkPath> linkPaths,
        HashSet<NodeId> visibleNodes,
        RectF visibleGraphRect)
    {
        var theme = view.Host.Theme;
        var selection = view.Selection;
        bool alt = (view.Host.Input.Modifiers & KeyModifiers.Alt) != 0;

        foreach (var link in view.Model.Links)
        {
            // Cull: skip the link unless at least one endpoint node is visible
            // or a waypoint falls inside the visible rect.
            var fromPin = view.Model.FindPin(link.FromPin);
            var toPin   = view.Model.FindPin(link.ToPin);

            bool endpointVisible =
                (fromPin != null && visibleNodes.Contains(fromPin.OwnerNodeId)) ||
                (toPin   != null && visibleNodes.Contains(toPin.OwnerNodeId));

            if (!endpointVisible)
            {
                bool waypointVisible = link.Waypoints.Any(wp => visibleGraphRect.Contains(wp));
                if (!waypointVisible) continue;
            }

            if (!linkPaths.TryGetValue(link.Id, out var path)) continue;

            // Skip hidden links; they are drawn by custom renderers (e.g. HSM internal transitions).
            if (link.Style == LinkStyle.Hidden) continue;

            bool isExec = fromPin?.Kind == PinKind.Exec;

            var wireColor = isExec
                ? DefaultTypeColors.ExecColor
                : fromPin?.Type.HasValue == true
                    ? view.TypeSystem.GetPinColor(fromPin.Type!.Value)
                    : DefaultTypeColors.GetColor(TypeKey.Empty);

            bool selected = selection.Contains(SelectionEntry.OfLink(link.Id));
            bool hovered  = view.Interaction.Hover is { Kind: HoverKind.Link } h && h.Link == link.Id;
            bool hoveredPinAttached = view.Interaction.Hover is { Kind: HoverKind.Pin } hp
                                   && (hp.Pin == link.FromPin || hp.Pin == link.ToPin);
            bool pendingDelete = alt && (hovered || hoveredPinAttached);

            // Scale wire thickness with zoom so wires get thin (not chunky) when zoomed out and
            // match node scale when zoomed in. Clamp to a visible minimum so they never vanish.
            float thickness = isExec ? theme.WireThicknessExec : theme.WireThicknessData;
            thickness = MathF.Max(0.75f, thickness * view.Viewport.Zoom);
            if (selected || hovered) thickness *= 1.6f;

            uint color = ImGui.GetColorU32(wireColor);
            if (pendingDelete)
                color = ImGui.GetColorU32(new Vector4(1f, 0.1f, 0.1f, 1f));
            else if (selected)
                color = ImGui.GetColorU32(theme.SelectionAccent);

            DrawPath(dl, path, color, thickness, isExec, view.Viewport.Zoom);
        }

        // Reroute dots
        DrawRerouteDots(view, dl, pinPositions, visibleNodes, visibleGraphRect);
    }

    // ── private ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Draws one link from its CE-1000 <see cref="LinkPath"/> — the same instance the hit-tester and custom renderers
    /// read. A pin wire without waypoints keeps its exec mid-arrow; a node-to-node path gets a target arrowhead aimed
    /// along the curve's end tangent.
    /// </summary>
    internal static void DrawPath(ImDrawListPtr dl, LinkPath path, uint color, float thickness, bool isExec, float zoom)
    {
        bool execArrow = isExec && !path.HasArrowhead && path.Segments.Count == 1;
        foreach (var s in path.Segments)
        {
            if (execArrow)
                dl.AddBezierWithArrow(s.P0, s.C1, s.C2, s.P3, color, thickness, thickness * 2.5f, BezierSegments);
            else
                dl.AddBezierCubic(s.P0, s.C1, s.C2, s.P3, color, thickness, BezierSegments);
        }
        if (path.HasArrowhead)
            DrawArrowhead(dl, path.End, path.Tangent(1f), color, MathF.Max(6f, 9f * zoom), MathF.Max(4f, 5.5f * zoom));
    }

    internal static void DrawArrowhead(ImDrawListPtr dl, Vector2 tip, Vector2 tangent, uint color, float length, float halfWidth)
    {
        float len = tangent.Length();
        if (len < 1e-4f) return;
        var dir = tangent / len;
        var nor = new Vector2(-dir.Y, dir.X);
        var baseC = tip - dir * length;
        dl.AddTriangleFilled(tip, baseC + nor * halfWidth, baseC - nor * halfWidth, color);
    }

    private static void DrawRerouteDots(
        GraphView view, ImDrawListPtr dl,
        Dictionary<PinId, Vector2> pinPositions,
        HashSet<NodeId> visibleNodes,
        RectF visibleGraphRect)
    {
        var theme = view.Host.Theme;
        const float DotRadius = 5f;

        foreach (var link in view.Model.Links)
        {
            if (link.Waypoints.Count == 0) continue;

            // Cull: skip reroute dots for links that are entirely off-screen.
            {
                var fromPin = view.Model.FindPin(link.FromPin);
                var toPin   = view.Model.FindPin(link.ToPin);
                bool endpointVisible =
                    (fromPin != null && visibleNodes.Contains(fromPin.OwnerNodeId)) ||
                    (toPin   != null && visibleNodes.Contains(toPin.OwnerNodeId));
                if (!endpointVisible &&
                    !link.Waypoints.Any(wp => visibleGraphRect.Contains(wp)))
                    continue;
            }
            for (int wi = 0; wi < link.Waypoints.Count; wi++)
            {
                var rr = new RerouteRef(link.Id, wi);
                var wpGraph = view.Interaction.RerouteDragOverridePositions.TryGetValue(rr, out var ovr) ? ovr : link.Waypoints[wi];
                var pt = view.Viewport.GraphToScreen(wpGraph);
                bool sel = view.Selection.Contains(SelectionEntry.OfReroute(rr));
                bool hov = view.Interaction.Hover is { Kind: HoverKind.Reroute } h
                        && h.Reroute == rr;

                var fromPin = view.Model.FindPin(link.FromPin);
                var wireColor = fromPin?.Kind == PinKind.Exec
                    ? DefaultTypeColors.ExecColor
                    : fromPin?.Type.HasValue == true
                        ? view.TypeSystem.GetPinColor(fromPin.Type!.Value)
                        : DefaultTypeColors.GetColor(TypeKey.Empty);

                uint fill    = ImGui.GetColorU32(wireColor);
                uint outline = sel
                    ? ImGui.GetColorU32(theme.SelectionAccent)
                    : hov
                        ? ImGui.GetColorU32(wireColor with { W = 1f })
                        : ImGui.GetColorU32(theme.TextMuted);

                dl.AddCircleFilledOutline(pt, DotRadius, fill, outline, hov ? 2f : 1.5f);
            }
        }
    }
}
