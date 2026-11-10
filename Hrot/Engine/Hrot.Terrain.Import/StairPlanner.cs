using Clipper2Lib;

namespace Hrot.Terrain.Import;

/// <summary>
/// ⭐ I-g (R-4186: one ramp per storey): per storey above the ground a 1 m-wide ramp along the INSIDE of a wall, run = 1.7 × the storey height;
/// the floor above is the footprint minus the stairwell — a notch against the wall, never a hole (Clipper2). The stairs of successive storeys
/// take disjoint places (another wall, or the other end of the same wall) so a ramp never starts in the notch above the one below; when no place
/// fits, the plan is null and the building becomes SOLID (counted).
/// </summary>
internal static class StairPlanner
{
    public const double Width = 1.0, RunPerHeight = 1.7, WallInset = 0.15, EndMargin = 0.4, Landing = 0.6;

    public sealed record Stair(int FromStorey, (double X, double Y) From, (double X, double Y) To);

    /// <param name="Floors">for storey k ≥ 1 (index k): the rings of its floor slab (the footprint minus the stairwell of stair k−1); index 0 is empty</param>
    public sealed record Plan(IReadOnlyList<Stair> Stairs, IReadOnlyList<IReadOnlyList<(double X, double Y)>>[] Floors);

    public static Plan? TryPlan(IReadOnlyList<(double X, double Y)> ring, IReadOnlyList<double> storeyHeights)
    {
        int storeys = storeyHeights.Count;
        var stairs = new List<Stair>();
        var floors = new IReadOnlyList<(double, double)>[storeys][];
        floors[0] = Array.Empty<IReadOnlyList<(double, double)>>();
        if (storeys <= 1) return new Plan(stairs, floors!);

        var footprint = new PathD(ring.Select(p => new PointD(p.X, p.Y)));
        if (Signed(footprint) < 0) footprint.Reverse();
        int n = footprint.Count;
        var edges = Enumerable.Range(0, n)
            .Select(e => (E: e, L: Dist(footprint[e], footprint[(e + 1) % n])))
            .OrderByDescending(x => x.L).ThenBy(x => x.E).ToList();

        var taken = new List<PathD>();   // the expanded stairwells already chosen
        for (int k = 0; k + 1 < storeys; k++)
        {
            double run = RunPerHeight * storeyHeights[k];
            bool found = false;
            foreach (var (e, len) in edges)
            {
                if (len < run + EndMargin + Landing) continue;
                var a = footprint[e]; var b = footprint[(e + 1) % n];
                double dx = (b.x - a.x) / len, dy = (b.y - a.y) / len, nx = -dy, ny = dx;   // inward normal of a counter-clockwise ring
                PointD P(double t, double l) => new(a.x + dx * t + nx * l, a.y + dy * t + ny * l);
                foreach (bool fromStart in new[] { true, false })
                {
                    double t0 = fromStart ? EndMargin : len - EndMargin - run, t1 = t0 + run;
                    var stair = Rect(P, t0, t1, WallInset, WallInset + Width);
                    if (!Inside(stair, footprint)) continue;
                    var expanded = Rect(P, t0 - Landing, t1 + Landing, -0.2, WallInset + Width + 0.3);
                    if (taken.Any(o => Overlap(o, expanded))) continue;
                    taken.Add(expanded);
                    var c = WallInset + Width / 2;
                    var s = fromStart ? (P(t0, c), P(t1, c)) : (P(t1, c), P(t0, c));
                    stairs.Add(new Stair(k, (s.Item1.x, s.Item1.y), (s.Item2.x, s.Item2.y)));
                    var notch = Rect(P, t0, t1, -0.2, WallInset + Width + 0.15);   // open to the wall: a notch, not a hole
                    var rings = Subtract(footprint, notch);
                    if (rings == null) return null;
                    floors[k + 1] = rings;
                    found = true;
                    break;
                }
                if (found) break;
            }
            if (!found) return null;
        }
        return new Plan(stairs, floors!);
    }

    private static PathD Rect(Func<double, double, PointD> P, double t0, double t1, double l0, double l1) =>
        new() { P(t0, l0), P(t1, l0), P(t1, l1), P(t0, l1) };

    private static double Signed(IReadOnlyList<PointD> p)
    {
        double a = 0;
        for (int i = 0, n = p.Count; i < n; i++) { var q = p[(i + 1) % n]; a += p[i].x * q.y - q.x * p[i].y; }
        return a / 2;
    }

    private static double Dist(PointD a, PointD b) => Math.Sqrt((b.x - a.x) * (b.x - a.x) + (b.y - a.y) * (b.y - a.y));

    private static double AreaOf(PathsD ps) { double s = 0; foreach (var p in ps) s += Math.Abs(Clipper.Area(p)); return s; }

    private static bool Inside(PathD rect, PathD footprint)
    {
        var inter = Clipper.Intersect(new PathsD { rect }, new PathsD { footprint }, FillRule.NonZero, 3);
        return AreaOf(inter) >= Math.Abs(Clipper.Area(rect)) - 1e-3;
    }

    private static bool Overlap(PathD a, PathD b) =>
        AreaOf(Clipper.Intersect(new PathsD { a }, new PathsD { b }, FillRule.NonZero, 3)) > 1e-3;

    /// <summary>The footprint minus the notch as hole-free rings, or null when Clipper leaves a hole.</summary>
    private static IReadOnlyList<(double X, double Y)>[]? Subtract(PathD footprint, PathD notch)
    {
        var tree = new PolyTreeD();
        var c = new ClipperD(3);
        c.AddSubject(new PathsD { footprint });
        c.AddClip(new PathsD { notch });
        c.Execute(ClipType.Difference, FillRule.NonZero, tree);
        var result = new List<IReadOnlyList<(double, double)>>();
        for (int i = 0; i < tree.Count; i++)
        {
            var outer = (PolyPathD)tree[i];
            if (outer.Count > 0) return null;   // a hole
            var ring = outer.Polygon!;
            if (ring.Count < 3 || Math.Abs(Clipper.Area(ring)) < 0.5) continue;
            var pts = ring.Select(p => (p.x, p.y)).ToList();
            if (Signed(ring) < 0) pts.Reverse();
            result.Add(pts);
        }
        return result.Count == 0 ? null : result.ToArray();
    }
}
