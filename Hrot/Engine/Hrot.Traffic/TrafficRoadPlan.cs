using System.Numerics;
using Sim.Ingest;

namespace Hrot.Traffic;

/// <summary>One straight piece of a centreline on the 2-D map (terrain-local metres).</summary>
public readonly record struct TrafficLine(Vector2 A, Vector2 B);

/// <summary>⭐ CE-4388 — one road's polyline in a <see cref="TrafficPolylineSet"/>: its points <c>[Start, Start + Count)</c> and its ground box.</summary>
public readonly record struct TrafficPolyline(int Start, int Count, Vector2 Min, Vector2 Max);

/// <summary>⭐ CE-4388 — a set of road polylines in flat arrays (built once per net; walked every frame with no allocation).</summary>
public sealed class TrafficPolylineSet
{
    public static readonly TrafficPolylineSet Empty = new(Array.Empty<Vector2>(), Array.Empty<TrafficPolyline>());
    public TrafficPolylineSet(Vector2[] points, TrafficPolyline[] lines) { Points = points; Lines = lines; }
    public Vector2[] Points { get; }
    public TrafficPolyline[] Lines { get; }
    /// <summary>The segments in the set (a rail, the budget check).</summary>
    public int Segments { get { int n = 0; foreach (var l in Lines) n += Math.Max(0, l.Count - 1); return n; } }
}

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

    /// <summary>
    /// ⭐ CE-4388 (docs/DESIGN_Map_View_Filter.md §7) — the edge centrelines simplified at these tolerances (m), built once: the map draws the level whose
    /// tolerance is about two pixels at its scale. 📐 Measured on real-1 (1 480 roads): 5 576 segments raw (over the budget, so nothing was drawn),
    /// 2 290 at 2.5 m, 1 933 at 5 m (≈ 2 px at the whole-terrain zoom), 1 734 at 10 m.
    /// </summary>
    public static readonly float[] EdgeTolerances = { 0f, 1f, 2f, 4f, 8f, 16f };

    /// <summary>⭐ CE-4388 — every normal vehicle lane as a polyline (close in).</summary>
    public TrafficPolylineSet LanePolylines { get; private init; } = TrafficPolylineSet.Empty;

    /// <summary>⭐ CE-4388 — the edge centrelines at each of <see cref="EdgeTolerances"/>.</summary>
    public TrafficPolylineSet[] EdgeLevels { get; private init; } = Array.Empty<TrafficPolylineSet>();

    /// <summary>The plan carries polylines (the view-filtered drawing): false for <see cref="Empty"/> and a hand-made plan.</summary>
    public bool HasPolylines => EdgeLevels.Length > 0;

    /// <summary>The coarsest edge level whose tolerance is at most <paramref name="toleranceMetres"/>.</summary>
    public TrafficPolylineSet EdgeLevelFor(float toleranceMetres)
    {
        int best = 0;
        for (int i = 0; i < EdgeTolerances.Length && i < EdgeLevels.Length; i++) if (EdgeTolerances[i] <= toleranceMetres) best = i;
        return EdgeLevels[best];
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
        // ⭐ CE-4388 — the polylines the view-filtered drawing uses, at every level, whatever the whole-net detail below
        var lanePolys = new List<Vector2[]>();
        foreach (var lane in net.LanesByHandle)
            if (!lane.EdgeId.StartsWith(':') && lane.AllowsRoadVehicle && lane.Shape.Count >= 2) lanePolys.Add(Points(lane.Shape, placement));
        var edgePolys = EdgePolylines(net, placement);
        var levels = new TrafficPolylineSet[EdgeTolerances.Length];
        for (int i = 0; i < levels.Length; i++) levels[i] = Set(edgePolys.Select(p => Simplify(p, EdgeTolerances[i])).ToList());
        var lanesSet = Set(lanePolys);
        if (lanes.Count + lightHeads <= budget) return new TrafficRoadPlan(lanes, TrafficPlanDetail.Lanes) { LanePolylines = lanesSet, EdgeLevels = levels };
        var edges = EdgeCentrelines(net, placement);
        if (edges.Count + lightHeads <= budget) return new TrafficRoadPlan(edges, TrafficPlanDetail.Edges) { LanePolylines = lanesSet, EdgeLevels = levels };
        return new TrafficRoadPlan(Array.Empty<TrafficLine>(), TrafficPlanDetail.None) { LanePolylines = lanesSet, EdgeLevels = levels };
    }

    private static Vector2[] Points(IReadOnlyList<(double X, double Y)> shape, SumoNetPlacement placement)
    {
        var a = new Vector2[shape.Count];
        for (int i = 0; i < a.Length; i++) a[i] = Local(placement, shape[i]);
        return a;
    }

    /// <summary>The edge centrelines as polylines (the same choice of midline as <see cref="EdgeCentrelines"/>).</summary>
    private static List<Vector2[]> EdgePolylines(NetworkModel net, SumoNetPlacement placement)
    {
        var polys = new List<Vector2[]>();
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
                polys.Add(Points(mid, placement));
            }
            else polys.Add(Points(vehicle[vehicle.Count / 2].Shape, placement));
        }
        return polys;
    }

    private static TrafficPolylineSet Set(List<Vector2[]> polys)
    {
        var points = new List<Vector2>();
        var lines = new List<TrafficPolyline>(polys.Count);
        foreach (var p in polys)
        {
            if (p.Length < 2) continue;
            var min = p[0]; var max = p[0];
            foreach (var v in p) { min = Vector2.Min(min, v); max = Vector2.Max(max, v); }
            lines.Add(new TrafficPolyline(points.Count, p.Length, min, max));
            points.AddRange(p);
        }
        return new TrafficPolylineSet(points.ToArray(), lines.ToArray());
    }

    /// <summary>Douglas-Peucker: the polyline within <paramref name="tolerance"/> m of the original with the fewest points (0 = unchanged).</summary>
    public static Vector2[] Simplify(Vector2[] p, float tolerance)
    {
        if (tolerance <= 0f || p.Length < 3) return p;
        var keep = new bool[p.Length];
        keep[0] = true; keep[p.Length - 1] = true;
        var stack = new Stack<(int A, int B)>();
        stack.Push((0, p.Length - 1));
        while (stack.Count > 0)
        {
            var (a, b) = stack.Pop();
            var d = p[b] - p[a];
            float len = d.Length();
            int far = -1;
            float worst = tolerance;
            for (int k = a + 1; k < b; k++)
            {
                float dist = len < 1e-6f ? Vector2.Distance(p[k], p[a]) : MathF.Abs(d.Y * (p[k].X - p[a].X) - d.X * (p[k].Y - p[a].Y)) / len;
                if (dist > worst) { worst = dist; far = k; }
            }
            if (far < 0) continue;
            keep[far] = true;
            stack.Push((a, far));
            stack.Push((far, b));
        }
        var result = new List<Vector2>();
        for (int i = 0; i < p.Length; i++) if (keep[i]) result.Add(p[i]);
        return result.ToArray();
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
