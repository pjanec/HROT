using System.Numerics;
using Sim.Ingest;

namespace Hrot.Traffic;

/// <summary>One straight piece of a lane on the map: its two ends (terrain-local metres) and its width.</summary>
public readonly record struct TrafficLaneStrip(Vector2 A, Vector2 B, float Width, bool Sidewalk);

/// <summary>A quad of the ambient ground on the map, corners counter-clockwise from above (p00, p10, p11, p01).</summary>
public readonly record struct TrafficGroundQuad(Vector2 P00, Vector2 P10, Vector2 P11, Vector2 P01);

/// <summary>
/// ⭐ AQ93 V7 — the road scene as the 2-D MAP draws it: each lane as a world-width line per shape segment, the ambient ground as
/// row strips. Built once with the scene, so the 2-D layer stays a few thousand primitives.
/// </summary>
/// <remarks>
/// <para>📌 Measured 2026-10-17 on demo-city: drawing the scene's meshes in 2-D took ~51.8k triangles per frame (asphalt 15.6k,
/// ambient ground 27.5k, crosswalks 6.7k, junctions 2.0k) against the map's 4096-primitive gizmo buffer. Ground went in first, so
/// roads, lights, cars and every entity gizmo after them were dropped (48.5k dropped a frame) and the map showed nothing.</para>
/// <para>⭐ The plan: 1 062 lane strips (one per segment of a normal lane; internal lanes are covered by the junction fill), the
/// junction mesh as it is, and the ground cells merged along their rows. ⛔ Crosswalks and lane markings are paint and stay out of 2-D.
/// The 3-D layer keeps drawing the full meshes.</para>
/// </remarks>
public sealed class TrafficRoadPlan
{
    public static readonly TrafficRoadPlan Empty = new(Array.Empty<TrafficLaneStrip>(), Array.Empty<TrafficGroundQuad>());

    public TrafficRoadPlan(IReadOnlyList<TrafficLaneStrip> strips, IReadOnlyList<TrafficGroundQuad> ground)
    {
        Strips = strips;
        Ground = ground;
    }

    public IReadOnlyList<TrafficLaneStrip> Strips { get; }

    public IReadOnlyList<TrafficGroundQuad> Ground { get; }

    /// <summary>The lane strips of every normal lane (an edge whose id does not start with <c>:</c>), placed per vertex.</summary>
    public static IReadOnlyList<TrafficLaneStrip> LaneStrips(NetworkModel net, SumoNetPlacement placement)
    {
        var strips = new List<TrafficLaneStrip>();
        foreach (var lane in net.LanesByHandle)
        {
            if (lane.EdgeId.StartsWith(':') || lane.Shape.Count < 2) continue;
            var a = Local(placement, lane.Shape[0]);
            for (int i = 1; i < lane.Shape.Count; i++)
            {
                var b = Local(placement, lane.Shape[i]);
                strips.Add(new TrafficLaneStrip(a, b, (float)lane.Width, !lane.AllowsRoadVehicle));
                a = b;
            }
        }
        return strips;
    }

    /// <summary>
    /// The ambient ground's cells merged along each row: a cell continues the run when its left edge is the previous cell's right
    /// edge. <paramref name="ground"/> is <c>AmbientGroundBuilder</c>'s layout — four vertices per cell (p00, p10, p11, p01), unshared.
    /// </summary>
    public static IReadOnlyList<TrafficGroundQuad> GroundRows(TrafficRoadMesh? ground)
    {
        var quads = new List<TrafficGroundQuad>();
        if (ground is null || ground.Vertices.Length < 4) return quads;
        var v = ground.Vertices;
        int cells = v.Length / 4;
        int start = 0;
        for (int k = 1; k <= cells; k++)
        {
            bool continues = k < cells
                && Near(v[4 * k], v[4 * (k - 1) + 1]) && Near(v[4 * k + 3], v[4 * (k - 1) + 2]);
            if (continues) continue;
            int last = k - 1;
            quads.Add(new TrafficGroundQuad(Flat(v[4 * start]), Flat(v[4 * last + 1]), Flat(v[4 * last + 2]), Flat(v[4 * start + 3])));
            start = k;
        }
        return quads;
    }

    private static bool Near(Vector3 a, Vector3 b) => MathF.Abs(a.X - b.X) < 1e-2f && MathF.Abs(a.Y - b.Y) < 1e-2f;

    private static Vector2 Flat(Vector3 v) => new(v.X, v.Y);

    private static Vector2 Local(SumoNetPlacement placement, (double X, double Y) p)
    {
        var (x, y) = placement.ToLocal(p.X, p.Y);
        return new Vector2((float)x, (float)y);
    }
}
