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

    /// <summary>⭐ CE-4388 — the map scale up to which the LANES are drawn (and the light dots); coarser, the edge centrelines (m per pixel).</summary>
    public const float LanesMaxMetresPerPixel = 0.5f;

    /// <summary>⭐ CE-4388 — the edge centrelines' simplification, in screen pixels (converted at the map's scale).</summary>
    public const float TolerancePixels = 2f;

    /// <summary>⭐ CE-4388 — the road segments the last draw emitted (a rail; telemetry).</summary>
    public int LastLinesDrawn { get; private set; }

    public void Draw(ISimulationView view, IDebugDrawBuilder draw)
    {
        if (view is not EntityRepository world) return;
        if (!draw.IsLayerActive(RoadLayer)) return;   // ⭐ CE-1203 — a hidden layer costs no work
        var net = TrafficNetCache.Of(world);
        if (net is null) return;

        // ⭐ The symbol (TrafficRoadPlan), never the meshes.
        var plan = net.Roads.Plan;
        // ⭐ CE-4388 (docs/DESIGN_Map_View_Filter.md §7) — with the map's view: the roads IN VIEW, as lanes close in, else the edge centrelines simplified to
        //   about two pixels — every road at every zoom, within the budget (⛔ SUPERSEDED: the whole net or nothing — a net over the budget, real-1's 1 480
        //   roads, drew NO road in 2-D). Without a view (a headless host, a rail) the plan's whole-net detail, as before.
        var mapView = Fdp.Toolkit.Diagnostics.Gizmos.GizmoViews.Of(draw);
        bool viewed = mapView is { Active: true } && plan.HasPolylines;
        if (viewed && mapView!.Is3D) return;   // a 2-D-only layer: nothing to emit while the map shows 3-D
        bool lights;
        if (viewed)
        {
            float mpp = mapView!.MetresPerPixel;
            bool close = mpp > 0f && mpp <= LanesMaxMetresPerPixel;
            var set = close ? plan.LanePolylines : plan.EdgeLevelFor(TolerancePixels * mpp);
            if (!close)
            {
                // ⭐ CE-6020 — a view that does not fit the budget at its scale's level steps to a COARSER level (fewer points per street) before anything is left out;
                //   past the coarsest, the plan's order (fastest first) decides what is left out
                int level = plan.EdgeLevelIndexFor(TolerancePixels * mpp);
                while (level < plan.EdgeLevels.Length - 1 && TrafficRoadPlan.SegmentsInView(plan.EdgeLevels[level], mapView) > TrafficRoadPlan.Budget) level++;
                set = plan.EdgeLevels[level];
            }
            int used = 0;
            var pts = set.Points;
            foreach (var pl in set.Lines)
            {
                if (!mapView.Overlaps(pl.Min, pl.Max)) continue;
                for (int k = 1; k < pl.Count && used < TrafficRoadPlan.Budget; k++, used++)
                    draw.DrawLine(new Vector3(pts[pl.Start + k - 1], 0f), new Vector3(pts[pl.Start + k], 0f), Centreline, CentrelinePx, SizeMode.ScreenPixels, Target, RoadLayer);
                if (used >= TrafficRoadPlan.Budget) break;
            }
            LastLinesDrawn = used;
            lights = close;
        }
        else
        {
            foreach (var l in plan.Lines)
                draw.DrawLine(new Vector3(l.A, 0f), new Vector3(l.B, 0f), Centreline, CentrelinePx, SizeMode.ScreenPixels, Target, RoadLayer);
            LastLinesDrawn = plan.Lines.Count;
            lights = plan.DrawsLights;
        }

        var frame = TrafficFrame.Of(world);
        if (frame is null) return;
        if (lights)
            foreach (var s in net.Roads.Signals)
            {
                if (viewed && !mapView!.Overlaps(new Vector2(s.Head.X, s.Head.Y), new Vector2(s.Head.X, s.Head.Y))) continue;
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
