using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;

namespace Hrot.Traffic;

/// <summary>
/// ⭐ AQ93 R-4024 / V7, §12.4 — the SUMO roads on the 2-D MAP as a SYMBOL: lane centrelines (or edge centrelines, or none, by the
/// net's size) from <see cref="TrafficRoadScene.Plan"/>, a coloured dot per traffic-light head in its live state, a dot per pedestrian (§15 K3), and the cars that are not entities (an entity car is
/// drawn by the entity layer), from <see cref="TrafficFrame"/>. A global stateless projector on the shared gizmo seam, so every host
/// whose world holds a SUMO net draws it.
/// </summary>
/// <remarks>
/// ⭐ 2-D only — <see cref="Target"/> carries <see cref="PipelineTarget.FlatOnly"/>, so the 3-D map, which draws the triangle meshes,
/// never draws this layer. ⭐ On <see cref="RoadLayer"/> — ⭐ CE-1203 its own "Traffic" checkbox (bit 24), no longer the Entities bit. ⭐ Within
/// <see cref="TrafficRoadPlan.Budget"/> (rail <c>TheRoadLayerFitsTheMapBufferTests</c>).
/// </remarks>
[GizmoProjector]
public sealed class TrafficRoadGizmo : IGlobalStatelessGizmo
{
    public const byte RoadLayer = DebugTraceLayers.Traffic;   // ⭐ CE-1203 (§5e FL-1) — was 0, the Entities bit

    /// <summary>⭐ Every primitive here is a 2-D SYMBOL — the 3-D map draws the road meshes, the lamps and the cars itself
    /// (<c>TrafficLayer3D</c>), so its pass never draws these (🔒 user, 2026-10-17).</summary>
    public const PipelineTarget Target = PipelineTarget.Map2D | PipelineTarget.FlatOnly;

    /// <summary>A centreline's width on screen (pixels, whatever the zoom).</summary>
    public const float CentrelinePx = 1.5f;

    /// <summary>A traffic light's dot radius on screen (pixels).</summary>
    public const float LightDotPx = 3f;

    /// <summary>⭐ AQ93 §15 K3 — a pedestrian's dot radius on screen (pixels): a symbol, one primitive each.</summary>
    public const float PedestrianDotPx = 2f;

    private static readonly Rgba32 Centreline = new(90, 92, 96, 220);
    private static readonly Rgba32 CarFill    = new(70, 120, 190, 240);
    private static readonly Rgba32 PedFill    = new(225, 225, 230, 230);
    private static readonly Rgba32 Red    = new(220, 40, 40, 255);
    private static readonly Rgba32 Yellow = new(240, 200, 40, 255);
    private static readonly Rgba32 Green  = new(40, 200, 70, 255);

    public void Draw(ISimulationView view, IDebugDrawBuilder draw)
    {
        if (view is not EntityRepository world) return;
        if (!draw.IsLayerActive(RoadLayer)) return;   // ⭐ CE-1203 — a hidden layer costs no work
        var net = TrafficNetCache.Of(world);
        if (net is null) return;

        // ⭐ The symbol (TrafficRoadPlan), never the meshes: centrelines at the detail the plan chose for this net's size.
        var plan = net.Roads.Plan;
        foreach (var l in plan.Lines)
            draw.DrawLine(new Vector3(l.A, 0f), new Vector3(l.B, 0f), Centreline, CentrelinePx, SizeMode.ScreenPixels, Target, RoadLayer);

        var frame = TrafficFrame.Of(world);
        if (frame is null) return;
        if (plan.DrawsLights)
            foreach (var s in net.Roads.Signals)
            {
                if (!frame.Lights.TryGetValue(s.LaneHandle, out var signal)) continue;
                var colour = TrafficFrame.LampOf(signal) switch { 2 => Green, 1 => Yellow, 0 => Red, _ => default };
                if (colour.A == 0) continue;
                draw.DrawSphere(new Vector3(s.Head.X, s.Head.Y, 0f), LightDotPx, colour, 0f, SizeMode.ScreenPixels, Target, RoadLayer, colour);
            }
        foreach (var car in frame.Cars)
        {
            if (frame.IsEntity(car)) continue;
            draw.DrawBox2D(new Vector2(car.X, car.Y), new Vector2(car.Length * 0.5f, car.Width * 0.5f), CarFill,
                car.YawRad * 180f / MathF.PI, 0f, SizeMode.WorldMeters, Target, RoadLayer, CarFill);
        }
        foreach (var ped in frame.Pedestrians)
        {
            if (frame.IsPedEntity(ped)) continue;
            draw.DrawSphere(new Vector3(ped.X, ped.Y, 0f), PedestrianDotPx, PedFill, 0f, SizeMode.ScreenPixels, Target, RoadLayer, PedFill);
        }
    }
}
