using System.Linq;
using System.Numerics;
using ImGuiNET;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.Primitives;

namespace NodeEditor.UI.Canvas;

/// <summary>
/// Renders reroute waypoint circles for all links that have waypoints.
/// Called after wires + nodes so the reroute circles appear on top.
/// </summary>
internal static class ReroutesRenderer
{
    private const float RerouteRadius         = 6f;
    private const float RerouteRingThickness  = 2f;

    /// <summary>Draw all reroute waypoints for all links.</summary>
    public static void Render(
        IGraphModel       model,
        SelectionState    selection,
        ViewportState     viewport,
        ITypeSystem       typeSystem,
        HashSet<NodeId>   visibleNodes,
        RectF             visibleGraphRect)
    {
        var dl = ImGui.GetWindowDrawList();

        foreach (var link in model.Links)
        {
            if (link.Waypoints.Count == 0) continue;

            var fromPin  = model.FindPin(link.FromPin);
            var toPin    = model.FindPin(link.ToPin);

            bool endpointVisible =
                (fromPin != null && visibleNodes.Contains(fromPin.OwnerNodeId)) ||
                (toPin != null && visibleNodes.Contains(toPin.OwnerNodeId));

            if (!endpointVisible && !link.Waypoints.Any(visibleGraphRect.Contains))
                continue;

            // Determine link color from source pin's type
            var linkColor = fromPin?.Type is not null
                ? typeSystem.GetPinColor(fromPin.Type.Value)
                : new Vector4(0.7f, 0.7f, 0.7f, 1f);

            for (int i = 0; i < link.Waypoints.Count; i++)
            {
                var waypoint   = link.Waypoints[i];
                var screenPos  = viewport.GraphToScreen(waypoint);
                var rerouteRef = new RerouteRef(link.Id, i);
                bool selected  = selection.Contains(SelectionEntry.OfReroute(rerouteRef));

                // Filled inner circle
                dl.AddCircleFilled(screenPos, RerouteRadius * viewport.Zoom,
                    ImGui.GetColorU32(linkColor));

                // Outline ring (brighter when selected)
                var ringColor = selected
                    ? new Vector4(1f, 0.9f, 0.2f, 1f)
                    : new Vector4(1f, 1f, 1f, 0.6f);
                dl.AddCircle(screenPos, RerouteRadius * viewport.Zoom,
                    ImGui.GetColorU32(ringColor), 0, RerouteRingThickness);
            }
        }
    }

    /// <summary>
    /// Hit-test reroutes; returns the first reroute hit within the given screen radius,
    /// or <see langword="null"/> if nothing was hit.
    /// </summary>
    public static RerouteRef? HitTest(
        IGraphModel   model,
        ViewportState viewport,
        Vector2       screenPos,
        float         hitRadiusPx = 10f)
    {
        foreach (var link in model.Links)
        {
            for (int i = 0; i < link.Waypoints.Count; i++)
            {
                var wayscreen = viewport.GraphToScreen(link.Waypoints[i]);
                if (Vector2.Distance(screenPos, wayscreen) <= hitRadiusPx)
                    return new RerouteRef(link.Id, i);
            }
        }
        return null;
    }
}
