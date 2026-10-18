using System.Numerics;
using Sim.Ingest;

namespace Hrot.Traffic;

/// <summary>One straight piece of a centreline on the 2-D map (terrain-local metres).</summary>
public readonly record struct TrafficLine(Vector2 A, Vector2 B);

/// <summary>How much of the road net the 2-D map draws (<see cref="TrafficRoadPlan.Detail"/>).</summary>
public enum TrafficPlanDetail : byte
{
    /// <summary>Every vehicle lane's centreline, and a dot per traffic-light head.</summary>
    Lanes = 0,
    /// <summary>One centreline per edge (a large net), and a dot per traffic-light head.</summary>
    Edges = 1,
    /// <summary>No road net in 2-D at all (a net too large even by edges) — the 3-D map still draws its meshes.</summary>
    None = 2,
}

/// <summary>
/// ⭐ AQ93 §12.4 — the road net as the 2-D MAP draws it: a SYMBOL, not the surface. 🔒 User, 2026-10-17: <i>"We do not need full
/// triangle road net in 2d, symbolic is more than enough (lane centerlines, traffic light as single colored dot). The triangle one
/// is for 3d render"</i> · <i>"some simplification might be necessary - maybe even like not rendering larger road net in 2d map at
/// all"</i>.
/// </summary>
/// <remarks>
/// <para>📌 Measured 2026-10-17 on demo-city: the 3-D meshes drawn flat were ~51.8k triangles a frame and starved the map's
/// gizmo buffer (CE-4019). This plan is built ONCE with the scene and picks its detail against <see cref="Budget"/>: vehicle-lane
/// centrelines (demo-city: 648 + 372 light heads) → one centreline per edge → nothing.</para>
/// <para>⭐ Sidewalks, junction interiors, crosswalks, markings and the ambient ground are not drawn in 2-D.</para>
/// </remarks>
public sealed class TrafficRoadPlan
{
    /// <summary>The most primitives the 2-D road layer may take (lines + light dots): an eighth of the map buffer (16384), so the
    /// terrain and every entity gizmo always fit beside it.</summary>
    public const int Budget = 2048;

    public static readonly TrafficRoadPlan Empty = new(Array.Empty<TrafficLine>(), TrafficPlanDetail.None);

    public TrafficRoadPlan(IReadOnlyList<TrafficLine> lines, TrafficPlanDetail detail)
    {
        Lines = lines;
        Detail = detail;
    }

    /// <summary>The centrelines to draw (empty when <see cref="Detail"/> is <see cref="TrafficPlanDetail.None"/>).</summary>
    public IReadOnlyList<TrafficLine> Lines { get; }

    public TrafficPlanDetail Detail { get; }

    /// <summary>Draw the traffic-light dots? Not when the road net itself is not drawn.</summary>
    public bool DrawsLights => Detail != TrafficPlanDetail.None;

    /// <summary>The plan for <paramref name="net"/>: the most detail whose lines plus <paramref name="lightHeads"/> fit
    /// <paramref name="budget"/>.</summary>
    public static TrafficRoadPlan For(NetworkModel net, SumoNetPlacement placement, int lightHeads, int budget = Budget)
    {
        var lanes = LaneCentrelines(net, placement);
        if (lanes.Count + lightHeads <= budget) return new TrafficRoadPlan(lanes, TrafficPlanDetail.Lanes);
        var edges = EdgeCentrelines(net, placement);
        if (edges.Count + lightHeads <= budget) return new TrafficRoadPlan(edges, TrafficPlanDetail.Edges);
        return new TrafficRoadPlan(Array.Empty<TrafficLine>(), TrafficPlanDetail.None);
    }

    /// <summary>One line per shape segment of every normal vehicle lane (not internal, not pedestrian-only).</summary>
    public static List<TrafficLine> LaneCentrelines(NetworkModel net, SumoNetPlacement placement)
    {
        var lines = new List<TrafficLine>();
        foreach (var lane in net.LanesByHandle)
        {
            if (lane.EdgeId.StartsWith(':') || !lane.AllowsRoadVehicle) continue;
            AddPolyline(lines, lane.Shape, placement);
        }
        return lines;
    }

    /// <summary>One centreline per normal edge with a vehicle lane: the midline of its outer vehicle lanes when their shapes pair up
    /// point for point, else its middle vehicle lane.</summary>
    public static List<TrafficLine> EdgeCentrelines(NetworkModel net, SumoNetPlacement placement)
    {
        var lines = new List<TrafficLine>();
        foreach (var edge in net.Edges)
        {
            if (edge.Id.StartsWith(':')) continue;
            var vehicle = edge.Lanes.Where(l => l.AllowsRoadVehicle && l.Shape.Count >= 2).ToList();
            if (vehicle.Count == 0) continue;
            var first = vehicle[0].Shape;
            var last = vehicle[^1].Shape;
            if (first.Count == last.Count)
            {
                var mid = new (double X, double Y)[first.Count];
                for (int i = 0; i < mid.Length; i++) mid[i] = ((first[i].X + last[i].X) / 2, (first[i].Y + last[i].Y) / 2);
                AddPolyline(lines, mid, placement);
            }
            else AddPolyline(lines, vehicle[vehicle.Count / 2].Shape, placement);
        }
        return lines;
    }

    private static void AddPolyline(List<TrafficLine> into, IReadOnlyList<(double X, double Y)> shape, SumoNetPlacement placement)
    {
        if (shape.Count < 2) return;
        var a = Local(placement, shape[0]);
        for (int i = 1; i < shape.Count; i++)
        {
            var b = Local(placement, shape[i]);
            into.Add(new TrafficLine(a, b));
            a = b;
        }
    }

    private static Vector2 Local(SumoNetPlacement placement, (double X, double Y) p)
    {
        var (x, y) = placement.ToLocal(p.X, p.Y);
        return new Vector2((float)x, (float)y);
    }
}
