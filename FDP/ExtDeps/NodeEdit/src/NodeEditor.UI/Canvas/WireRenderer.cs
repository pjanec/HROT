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

    /// <summary>Draw all links from <see cref="GraphView.Model"/>.</summary>
    public void DrawAll(
        GraphView view,
        ImDrawListPtr dl,
        Dictionary<PinId, Vector2> pinPositions)
    {
        var theme = view.Host.Theme;
        var selection = view.Selection;

        foreach (var link in view.Model.Links)
        {
            if (!pinPositions.TryGetValue(link.FromPin, out var a)) continue;
            if (!pinPositions.TryGetValue(link.ToPin,   out var b)) continue;

            var fromPin = view.Model.FindPin(link.FromPin);
            bool isExec = fromPin?.Kind == PinKind.Exec;

            var wireColor = isExec
                ? DefaultTypeColors.ExecColor
                : fromPin?.Type.HasValue == true
                    ? view.TypeSystem.GetPinColor(fromPin.Type!.Value)
                    : DefaultTypeColors.GetColor(TypeKey.Empty);

            bool selected = selection.Contains(SelectionEntry.OfLink(link.Id));
            bool hovered  = view.Interaction.Hover is { Kind: HoverKind.Link } h && h.Link == link.Id;

            float thickness = isExec ? theme.WireThicknessExec : theme.WireThicknessData;
            if (selected || hovered) thickness *= 1.6f;

            uint color = ImGui.GetColorU32(wireColor);
            if (selected) color = ImGui.GetColorU32(theme.SelectionAccent);

            DrawLinkSegments(dl, view, a, b, link, color, thickness, isExec);
        }

        // Reroute dots
        DrawRerouteDots(view, dl, pinPositions);
    }

    // ── private ───────────────────────────────────────────────────────────────

    private static void DrawLinkSegments(
        ImDrawListPtr dl,
        GraphView view,
        Vector2 a, Vector2 b,
        ILinkModel link,
        uint color, float thickness, bool isExec)
    {
        var waypoints = link.Waypoints;

        if (waypoints.Count == 0)
        {
            DrawBezierSegment(dl, a, b, color, thickness, isExec, BezierSegments);
            return;
        }

        var prev = a;
        for (int i = 0; i < waypoints.Count; i++)
        {
            var wp = view.Viewport.GraphToScreen(waypoints[i]);
            DrawBezierSegment(dl, prev, wp, color, thickness, false, BezierSegments);
            prev = wp;
        }
        DrawBezierSegment(dl, prev, b, color, thickness, false, BezierSegments);
    }

    private static void DrawBezierSegment(
        ImDrawListPtr dl,
        Vector2 a, Vector2 b,
        uint color, float thickness, bool withArrow, int segments)
    {
        var (c1, c2) = HitTester.WireTangents(a, b);

        if (withArrow)
            dl.AddBezierWithArrow(a, c1, c2, b, color, thickness, thickness * 2.5f, segments);
        else
            dl.AddBezierCubic(a, c1, c2, b, color, thickness, segments);
    }

    private static void DrawRerouteDots(
        GraphView view, ImDrawListPtr dl,
        Dictionary<PinId, Vector2> pinPositions)
    {
        var theme = view.Host.Theme;
        const float DotRadius = 5f;

        foreach (var link in view.Model.Links)
        {
            for (int wi = 0; wi < link.Waypoints.Count; wi++)
            {
                var pt = view.Viewport.GraphToScreen(link.Waypoints[wi]);

                var rr = new RerouteRef(link.Id, wi);
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
