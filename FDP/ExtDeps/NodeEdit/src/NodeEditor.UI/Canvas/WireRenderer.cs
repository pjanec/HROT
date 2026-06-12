using System.Numerics;
using ImGuiNET;
using NodeEditor.Core;
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

            if (!pinPositions.TryGetValue(link.FromPin, out var a)) continue;
            if (!pinPositions.TryGetValue(link.ToPin,   out var b)) continue;

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

            float thickness = isExec ? theme.WireThicknessExec : theme.WireThicknessData;
            if (selected || hovered) thickness *= 1.6f;

            uint color = ImGui.GetColorU32(wireColor);
            if (pendingDelete)
                color = ImGui.GetColorU32(new Vector4(1f, 0.1f, 0.1f, 1f));
            else if (selected)
                color = ImGui.GetColorU32(theme.SelectionAccent);

            DrawLinkSegments(dl, view, a, b, link, color, thickness, isExec, view.Model.Kind.Orientation);
        }

        // Reroute dots
        DrawRerouteDots(view, dl, pinPositions, visibleNodes, visibleGraphRect);
    }

    // ── private ───────────────────────────────────────────────────────────────

    private static void DrawLinkSegments(
        ImDrawListPtr dl,
        GraphView view,
        Vector2 a, Vector2 b,
        ILinkModel link,
        uint color, float thickness, bool isExec, PinOrientation orientation)
    {
        var waypoints = link.Waypoints;

        if (waypoints.Count == 0)
        {
            DrawBezierSegment(dl, a, b, color, thickness, isExec, BezierSegments, orientation);
            return;
        }

        var prev = a;
        for (int i = 0; i < waypoints.Count; i++)
        {
            var rr = new RerouteRef(link.Id, i);
            var wpGraph = view.Interaction.RerouteDragOverridePositions.TryGetValue(rr, out var ovr) ? ovr : waypoints[i];
            var wp = view.Viewport.GraphToScreen(wpGraph);
            DrawBezierSegment(dl, prev, wp, color, thickness, false, BezierSegments, orientation);
            prev = wp;
        }
        DrawBezierSegment(dl, prev, b, color, thickness, false, BezierSegments, orientation);
    }

    private static void DrawBezierSegment(
        ImDrawListPtr dl,
        Vector2 a, Vector2 b,
        uint color, float thickness, bool withArrow, int segments,
        PinOrientation orientation = PinOrientation.Horizontal)
    {
        var (c1, c2) = HitTester.WireTangents(a, b, orientation);

        if (withArrow)
            dl.AddBezierWithArrow(a, c1, c2, b, color, thickness, thickness * 2.5f, segments);
        else
            dl.AddBezierCubic(a, c1, c2, b, color, thickness, segments);
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
