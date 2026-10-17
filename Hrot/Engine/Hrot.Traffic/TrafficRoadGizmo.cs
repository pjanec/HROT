using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Sim.Scene;

namespace Hrot.Traffic;

/// <summary>
/// ⭐ AQ93 R-4024 / V7 — the SUMO roads on the 2-D MAP: lanes, sidewalks, junctions and the ambient ground from
/// <see cref="TrafficRoadScene.Plan"/>, the traffic lights in their live state and the cars that are not entities (an entity car is
/// drawn by the entity layer), from <see cref="TrafficFrame"/>. A global stateless projector on the shared gizmo seam, so every host
/// whose world holds a SUMO net draws it.
/// </summary>
/// <remarks>
/// ⭐ Map-2D only (<see cref="PipelineTarget.Map2D"/>): the 3-D map has its own mesh layer. ⭐ On <see cref="RoadLayer"/> 0 with the
/// terrain, emitted before entity gizmos. ⚠ Lane markings and crosswalks are left out in 2-D (paint, sub-pixel at map zooms).
/// ⭐ Sized for the map buffer: <see cref="TrafficRoadPlan"/> keeps demo-city at 3 731 primitives (139 ground rows, 1 062 lane strips)
/// (rail <c>TheRoadLayerFitsTheMapBufferTests</c>).
/// </remarks>
[GizmoProjector]
public sealed class TrafficRoadGizmo : IGlobalStatelessGizmo
{
    public const byte RoadLayer = 0;

    private static readonly Rgba32 GroundFill    = new(196, 204, 180, 120);
    private static readonly Rgba32 AsphaltFill   = new(72, 74, 78, 230);
    private static readonly Rgba32 JunctionFill  = new(84, 86, 90, 230);
    private static readonly Rgba32 SidewalkFill  = new(170, 168, 160, 230);
    private static readonly Rgba32 CarFill       = new(70, 120, 190, 240);
    private static readonly Rgba32 Red    = new(220, 40, 40, 255);
    private static readonly Rgba32 Yellow = new(240, 200, 40, 255);
    private static readonly Rgba32 Green  = new(40, 200, 70, 255);

    /// <summary>Draw the ambient ground (the background floor beyond our terrain). On by default.</summary>
    public static bool ShowAmbientGround { get; set; } = true;

    public void Draw(ISimulationView view, IDebugDrawBuilder draw)
    {
        if (view is not EntityRepository world) return;
        var net = TrafficNetCache.Of(world);
        if (net is null) return;

        // ⭐ The 2-D plan, not the 3-D meshes (TrafficRoadPlan: the meshes were ~51.8k triangles a frame and starved the buffer).
        var plan = net.Roads.Plan;
        if (ShowAmbientGround)
            foreach (var q in plan.Ground)
            {
                draw.DrawFilledTriangle(q.P00, q.P10, q.P11, GroundFill, RoadLayer, PipelineTarget.Map2D);
                draw.DrawFilledTriangle(q.P00, q.P11, q.P01, GroundFill, RoadLayer, PipelineTarget.Map2D);
            }
        foreach (var mesh in net.Roads.Meshes)
        {
            if (mesh.Kind != SceneKind.Junction) continue;
            var v = mesh.Vertices;
            var ix = mesh.Indices;
            for (int t = 0; t + 2 < ix.Length; t += 3)
                draw.DrawFilledTriangle(Flat(v[ix[t]]), Flat(v[ix[t + 1]]), Flat(v[ix[t + 2]]), JunctionFill, RoadLayer, PipelineTarget.Map2D);
        }
        foreach (var s in plan.Strips)
            draw.DrawLine(new Vector3(s.A, 0f), new Vector3(s.B, 0f), s.Sidewalk ? SidewalkFill : AsphaltFill, s.Width,
                SizeMode.WorldMeters, PipelineTarget.Map2D, RoadLayer);

        var frame = TrafficFrame.Of(world);
        if (frame is null) return;
        foreach (var s in net.Roads.Signals)
        {
            if (!frame.Lights.TryGetValue(s.LaneHandle, out var signal)) continue;
            var colour = TrafficFrame.LampOf(signal) switch { 2 => Green, 1 => Yellow, 0 => Red, _ => default };
            if (colour.A == 0) continue;
            draw.DrawSphere(s.Head, 0.6f, colour, 0f, SizeMode.WorldMeters, PipelineTarget.Map2D, RoadLayer, colour);
        }
        foreach (var car in frame.Cars)
        {
            if (frame.IsEntity(car)) continue;
            draw.DrawBox2D(new Vector2(car.X, car.Y), new Vector2(car.Length * 0.5f, car.Width * 0.5f), CarFill,
                car.YawRad * 180f / MathF.PI, 0f, SizeMode.WorldMeters, PipelineTarget.Map2D, RoadLayer, CarFill);
        }
    }

    private static Vector2 Flat(Vector3 v) => new(v.X, v.Y);
}
