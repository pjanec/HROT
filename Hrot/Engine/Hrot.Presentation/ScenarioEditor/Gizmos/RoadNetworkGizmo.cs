using System.Numerics;
using CarKinem.Road;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;

namespace Hrot.ScenarioEditor.Gizmos;

/// <summary>
/// ⭐ <c>CE-3124</c> (R-228) — the loaded terrain's ROAD NETWORK, from the <see cref="ZoneEnvironmentData"/> world singleton that
/// terrain residency writes when a terrain with roads is committed (<c>TerrainResidency</c>, the one source; <c>RoadNetworkHolder</c>
/// is only its thread-safe projection). Each segment's curve as a grey band of its full lane width with a yellow centre line, each node
/// as a small blue dot — the drawing <c>SimHostRoadLayer</c> did with raylib on SimHost alone. Has data = can draw: it draws on
/// every host whose world holds a road network (SimHost, Editor, CGF, IG today; the Replay Browser once it loads the terrain,
/// <c>CE-3118</c>). Read on the main thread, where the singleton is also swapped. Toggled by the <c>Roads</c> layer.
/// 📄 docs/DESIGN_Uniform_Gizmo_Membership.md §10.
/// </summary>
[GizmoProjector]
public sealed class RoadNetworkGizmo : IGlobalStatelessGizmo
{
    public static readonly Rgba32 RoadColor   = new(130, 130, 130, 160);
    public static readonly Rgba32 CentreColor = new(253, 249, 0, 220);
    public static readonly Rgba32 NodeColor   = new(0, 121, 241, 230);

    /// <summary>Pieces a segment's curve is drawn in — a segment is a cubic Hermite (P0,T0 → P1,T1), evaluated by the same <see cref="RoadGraphNavigator.EvaluateHermite"/> the navigator uses;
    /// the old raylib layer drew the chord, which cut every bend.</summary>
    public const int CurveSamples = 8;


    public void Draw(ISimulationView view, IDebugDrawBuilder draw)
    {
        if (view is not EntityRepository repo || !repo.HasSingleton<ZoneEnvironmentData>()) return;
        var network = repo.GetSingleton<ZoneEnvironmentData>().RoadNetwork;
        Draw(network, draw);
    }

    /// <summary>Draws <paramref name="network"/>; nothing when it holds no roads.</summary>
    public static void Draw(in RoadNetworkBlob network, IDebugDrawBuilder draw)
    {
        if (!network.Segments.IsCreated || !network.Nodes.IsCreated) return;
        for (int i = 0; i < network.Segments.Length; i++)
        {
            var seg = network.Segments[i];
            float width = seg.LaneWidth * seg.LaneCount;
            var a = new Vector3(seg.P0, 0f);
            for (int k = 1; k <= CurveSamples; k++)
            {
                var b = new Vector3(RoadGraphNavigator.EvaluateHermite(k / (float)CurveSamples, seg.P0, seg.T0, seg.P1, seg.T1), 0f);
                draw.DrawLine(a, b, RoadColor, width, SizeMode.WorldMeters, layer: DebugTraceLayers.Roads);
                draw.DrawLine(a, b, CentreColor, 1f, SizeMode.ScreenPixels, layer: DebugTraceLayers.Roads);
                a = b;
            }
        }
        for (int i = 0; i < network.Nodes.Length; i++)
            draw.DrawSphere(new Vector3(network.Nodes[i].Position, 0f), 2f, NodeColor, layer: DebugTraceLayers.Roads, fillColor: NodeColor);
    }
}
