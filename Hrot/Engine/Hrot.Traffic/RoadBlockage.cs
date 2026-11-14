using System.Numerics;
using CarKinem.Road;
using Fdp.Core;
using Fdp.Toolkit.Terrain;

namespace Hrot.Traffic;

/// <summary>
/// ⭐⭐ <c>CE-4590</c> (P-2; 📄 docs/DESIGN_Ground_Patches_And_Road_Blockage.md §3.2, §4 G-10, §9.2 V-18…V-20, V-24) — the geometry of ROAD BLOCKAGE: which
/// static blockers lie on a road and whether one closes it. A road is CLOSED where, at one station along it, every point across its full width lies inside a
/// blocker; it is PARTLY covered where any point does. Measured against each net in its OWN geometry — our road graph's segments, SUMO's lanes — so no edge-id
/// mapping between the two is needed (V-18's need, removed). Pure; called only when the blocker set changes.
/// </summary>
public static class RoadBlockage
{
    /// <summary>The spacing (m) of the stations along a road and of the points across it.</summary>
    public const float Step = 0.5f;

    /// <summary>The footprints (terrain-local, counter-clockwise) of every ACTIVE static obstacle of <paramref name="world"/> — a roadblock, a wreck, sandbags.</summary>
    public static void Blockers(EntityRepository world, List<Vector2[]> into)
    {
        into.Clear();
        if (!world.IsComponentTypeRegistered<StaticObstacle>() || !world.IsComponentTypeRegistered<ObstacleShape>() || !world.IsComponentTypeRegistered<SimTransform>()) return;
        var library = TerrainMaterialLibrary.Shared;
        library.TryGet(TerrainMaterialLibrary.DefaultMaterial, out var material);
        foreach (var e in world.Query().With<StaticObstacle>().With<ObstacleShape>().With<SimTransform>().Build())
        {
            ref readonly var t = ref world.GetComponentRO<SimTransform>(e);
            ref readonly var shape = ref world.GetComponentRO<ObstacleShape>(e);
            into.Add(TerrainObstacles.PrismOf(t.Position, TerrainObstacles.Yaw(t.Rotation), shape, material!).Footprint);
        }
    }

    /// <summary>
    /// The road along <paramref name="at"/>(s), s in [0, 1] over <paramref name="length"/> m, <paramref name="width"/> m wide: 2 = closed (a station fully
    /// covered), 1 = partly covered, 0 = clear.
    /// </summary>
    public static int Covered(Func<float, (Vector2 P, Vector2 Dir)> at, float length, float width, IReadOnlyList<Vector2[]> blockers)
    {
        if (blockers.Count == 0 || length <= 0f || width <= 0f) return 0;
        // a quick reject: no blocker's box near the road's box
        var (rmin, rmax) = Box(at, length, width);
        bool near = false;
        foreach (var b in blockers) { var (bmin, bmax) = Box(b); if (bmin.X <= rmax.X && bmax.X >= rmin.X && bmin.Y <= rmax.Y && bmax.Y >= rmin.Y) { near = true; break; } }
        if (!near) return 0;
        int stations = Math.Max(1, (int)MathF.Ceiling(length / Step));
        int across = Math.Max(1, (int)MathF.Ceiling(width / Step));
        int result = 0;
        for (int k = 0; k <= stations; k++)
        {
            var (p, dir) = at((float)k / stations);
            var side = new Vector2(-dir.Y, dir.X);
            int inside = 0;
            for (int j = 0; j < across; j++)
            {
                float off = -width * 0.5f + width * (j + 0.5f) / across;
                if (InsideAny(p + side * off, blockers)) inside++;
            }
            if (inside == across) return 2;
            if (inside > 0) result = 1;
        }
        return result;
    }

    /// <summary>The segments of <paramref name="blob"/> a blocker CLOSES (every lane covered at one station) — indices in <paramref name="blob"/>.</summary>
    public static void ClosedSegments(in RoadNetworkBlob blob, IReadOnlyList<Vector2[]> blockers, HashSet<int> into)
    {
        into.Clear();
        if (blockers.Count == 0 || !blob.Segments.IsCreated) return;
        for (int i = 0; i < blob.Segments.Length; i++)
        {
            var s = blob.Segments[i];
            float width = s.LaneWidth * Math.Max(1, s.LaneCount);
            if (Covered(t =>
                {
                    var p = RoadGraphNavigator.EvaluateHermite(t, s.P0, s.T0, s.P1, s.T1);
                    var d = RoadGraphNavigator.EvaluateHermiteTangent(t, s.P0, s.T0, s.P1, s.T1);
                    return (p, d.LengthSquared() > 1e-9f ? Vector2.Normalize(d) : Vector2.UnitX);
                }, MathF.Max(s.Length, Vector2.Distance(s.P0, s.P1)), width, blockers) == 2)
                into.Add(i);
        }
    }

    /// <summary>
    /// The SUMO edges a blocker CLOSES: at one fraction of the edge's length EVERY lane is covered across its width. <paramref name="toLocal"/> maps net
    /// coordinates to terrain-local.
    /// </summary>
    public static void ClosedEdges(Sim.Ingest.NetworkModel net, Func<double, double, (double X, double Y)> toLocal, IReadOnlyList<Vector2[]> blockers, List<string> into)
    {
        into.Clear();
        if (blockers.Count == 0) return;
        const int Fractions = 200;
        var all = new bool[Fractions + 1];
        foreach (var edge in net.Edges)
        {
            if (edge.Lanes.Count == 0 || edge.Id.StartsWith(':')) continue;   // an internal (junction) edge
            Array.Fill(all, true);
            bool any = false;
            foreach (var lane in edge.Lanes)
            {
                var pts = lane.Shape.Select(p => { var (x, y) = toLocal(p.X, p.Y); return new Vector2((float)x, (float)y); }).ToArray();
                if (pts.Length < 2) { Array.Fill(all, false); break; }
                float length = 0f; for (int i = 0; i + 1 < pts.Length; i++) length += Vector2.Distance(pts[i], pts[i + 1]);
                var cov = Coverage(pts, length, (float)lane.Width, blockers, Fractions);
                for (int f = 0; f <= Fractions; f++) { all[f] &= cov[f]; any |= cov[f]; }
            }
            if (!any) continue;
            for (int f = 0; f <= Fractions; f++) if (all[f]) { into.Add(edge.Id); break; }
        }
    }

    /// <summary>Per fraction of a lane's length: is it covered across its whole width.</summary>
    private static bool[] Coverage(Vector2[] line, float length, float width, IReadOnlyList<Vector2[]> blockers, int fractions)
    {
        var cov = new bool[fractions + 1];
        int across = Math.Max(1, (int)MathF.Ceiling(width / Step));
        for (int f = 0; f <= fractions; f++)
        {
            var (p, dir) = Along(line, length * f / fractions);
            var side = new Vector2(-dir.Y, dir.X);
            bool full = true;
            for (int j = 0; j < across && full; j++)
                full = InsideAny(p + side * (-width * 0.5f + width * (j + 0.5f) / across), blockers);
            cov[f] = full;
        }
        return cov;
    }

    private static (Vector2 P, Vector2 Dir) Along(Vector2[] line, float s)
    {
        for (int i = 0; i + 1 < line.Length; i++)
        {
            float len = Vector2.Distance(line[i], line[i + 1]);
            if ((s <= len || i + 2 == line.Length) && len > 1e-6f)
                return (line[i] + (line[i + 1] - line[i]) * MathF.Min(1f, s / len), (line[i + 1] - line[i]) / len);
            s -= len;
        }
        return (line[^1], Vector2.UnitX);
    }

    private static bool InsideAny(Vector2 p, IReadOnlyList<Vector2[]> blockers)
    {
        for (int i = 0; i < blockers.Count; i++) if (PolygonMath.Contains(blockers[i], p)) return true;
        return false;
    }

    private static (Vector2 Min, Vector2 Max) Box(Vector2[] poly)
    {
        var min = new Vector2(float.MaxValue); var max = new Vector2(float.MinValue);
        foreach (var p in poly) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
        return (min, max);
    }

    private static (Vector2 Min, Vector2 Max) Box(Func<float, (Vector2 P, Vector2 Dir)> at, float length, float width)
    {
        var min = new Vector2(float.MaxValue); var max = new Vector2(float.MinValue);
        int n = Math.Max(2, (int)MathF.Ceiling(length / 5f));
        for (int k = 0; k <= n; k++) { var (p, _) = at((float)k / n); min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
        return (min - new Vector2(width), max + new Vector2(width));
    }
}
