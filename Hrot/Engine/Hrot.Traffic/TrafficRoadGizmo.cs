using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Sim.Scene;

namespace Hrot.Traffic;

/// <summary>
/// ⭐ AQ93 R-4024 / V7 — the SUMO roads on the 2-D MAP: asphalt, junctions, crosswalks and the ambient ground from
/// <see cref="TrafficNetCache.Roads"/>, the traffic lights in their live state and the cars that are not entities (an entity car is
/// drawn by the entity layer), from <see cref="TrafficFrame"/>. A global stateless projector on the shared gizmo seam, so every host
/// whose world holds a SUMO net draws it.
/// </summary>
/// <remarks>
/// ⭐ Map-2D only (<see cref="PipelineTarget.Map2D"/>): the 3-D map has its own mesh layer. ⭐ On <see cref="RoadLayer"/> 0 with the
/// terrain, emitted before entity gizmos. ⚠ Lane markings are left out in 2-D (sub-pixel at map zooms).
/// </remarks>
[GizmoProjector]
public sealed class TrafficRoadGizmo : IGlobalStatelessGizmo
{
    public const byte RoadLayer = 0;

    private static readonly Rgba32 GroundFill    = new(196, 204, 180, 120);
    private static readonly Rgba32 AsphaltFill   = new(72, 74, 78, 230);
    private static readonly Rgba32 JunctionFill  = new(84, 86, 90, 230);
    private static readonly Rgba32 CrosswalkFill = new(235, 235, 235, 230);
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

        foreach (var mesh in Ordered(net.Roads.Meshes))
        {
            var fill = mesh.Kind switch
            {
                SceneKind.AmbientGround => GroundFill,
                SceneKind.Junction => JunctionFill,
                SceneKind.Crosswalk => CrosswalkFill,
                SceneKind.Asphalt => AsphaltFill,
                _ => default,
            };
            if (fill.A == 0 || (mesh.Kind == SceneKind.AmbientGround && !ShowAmbientGround)) continue;
            var v = mesh.Vertices;
            var ix = mesh.Indices;
            for (int t = 0; t + 2 < ix.Length; t += 3)
                draw.DrawFilledTriangle(Flat(v[ix[t]]), Flat(v[ix[t + 1]]), Flat(v[ix[t + 2]]), fill, RoadLayer, PipelineTarget.Map2D);
        }

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
            if (frame.IsEntity(car.Handle)) continue;
            draw.DrawBox2D(new Vector2(car.X, car.Y), new Vector2(car.Length * 0.5f, car.Width * 0.5f), CarFill,
                car.YawRad * 180f / MathF.PI, 0f, SizeMode.WorldMeters, PipelineTarget.Map2D, RoadLayer, CarFill);
        }
    }

    // The ground first, then the road surfaces over it, the paint last.
    private static IEnumerable<TrafficRoadMesh> Ordered(IReadOnlyList<TrafficRoadMesh> meshes)
        => meshes.OrderBy(m => m.Kind switch
        {
            SceneKind.AmbientGround => 0, SceneKind.Junction => 1, SceneKind.Asphalt => 2, _ => 3,
        });

    private static Vector2 Flat(Vector3 v) => new(v.X, v.Y);
}
