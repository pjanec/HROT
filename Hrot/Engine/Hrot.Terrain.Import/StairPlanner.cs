using Clipper2Lib;

namespace Hrot.Terrain.Import;

/// <summary>
/// ⭐ I-g (R-4186: one ramp per storey): per storey above the ground a 1 m-wide ramp along the INSIDE of a wall, run = 1.7 × the storey height;
/// the floor above is the footprint minus the stairwell — a notch against the wall, never a hole (Clipper2). The stairs of successive storeys
/// take disjoint places (another wall, or the other end of the same wall) so a ramp never starts in the notch above the one below; when no place
/// fits, the plan is null and the building becomes SOLID (counted).
/// ⭐ CE-4379: when no wall takes the straight flight, a SWITCH-BACK (U) stair is tried — two runs side by side, each half the rise at the
/// straight ramp's slope, with a landing slab at mid-height — in a ≈ 2.2 × (0.85 × storey height + 1 m) well against a wall corner. The straight
/// flight is always preferred; the switch-back only needs a wall of (0.85 × storey height + 1 m) + margins. Both runs enter and leave on the same
/// side (the well's open end), so the floor above has ONE notch, as for the straight flight.
/// </summary>
internal static class StairPlanner
{
    public const double Width = 1.0, RunPerHeight = 1.7, WallInset = 0.15, EndMargin = 0.4, Landing = 0.6;
    /// <summary>the switch-back: the gap between its two runs, and the depth of the landing slab between them (m)</summary>
    public const double RunGap = 0.2, LandingDepth = 1.0;

    /// <param name="FromRise">the run's start / end height above the floor of <c>FromStorey</c>; null = the straight flight (floor → the next floor)</param>
    public sealed record Run((double X, double Y) From, (double X, double Y) To, double? FromRise = null, double? ToRise = null);

    /// <summary>One flight from <c>FromStorey</c> to the next: one run, or (⭐ CE-4379) a switch-back — two runs and a landing slab at <c>LandingRise</c>.</summary>
    public sealed record Stair(int FromStorey, IReadOnlyList<Run> Runs, IReadOnlyList<(double X, double Y)>? Landing = null, double LandingRise = 0)
    {
        public bool SwitchBack => Landing != null;
        public (double X, double Y) From => Runs[0].From;
        public (double X, double Y) To => Runs[^1].To;
    }

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
            double h = storeyHeights[k];
            // the straight flight on any wall first; only when none fits, the switch-back — a building that fitted before is planned as before
            var chosen = TryStraight(footprint, edges, h, taken) ?? TrySwitchBack(footprint, edges, h, taken);
            if (chosen == null) return null;
            var (stair, expanded, notch) = chosen.Value;
            var rings = Subtract(footprint, notch);
            if (rings == null) return null;
            taken.Add(expanded);
            stairs.Add(stair with { FromStorey = k });
            floors[k + 1] = rings;
        }
        return new Plan(stairs, floors!);
    }

    private delegate PointD Frame(double t, double l);

    private static Frame WallFrame(PathD footprint, int e, double len)
    {
        int n = footprint.Count;
        var a = footprint[e]; var b = footprint[(e + 1) % n];
        double dx = (b.x - a.x) / len, dy = (b.y - a.y) / len, nx = -dy, ny = dx;   // inward normal of a counter-clockwise ring
        return (t, l) => new PointD(a.x + dx * t + nx * l, a.y + dy * t + ny * l);
    }

    private static (Stair, PathD Expanded, PathD Notch)? TryStraight(PathD footprint, List<(int E, double L)> edges, double h, List<PathD> taken)
    {
        double run = RunPerHeight * h;
        foreach (var (e, len) in edges)
        {
            if (len < run + EndMargin + Landing) continue;
            var P = WallFrame(footprint, e, len);
            foreach (bool fromStart in new[] { true, false })
            {
                double t0 = fromStart ? EndMargin : len - EndMargin - run, t1 = t0 + run;
                var stair = Rect(P, t0, t1, WallInset, WallInset + Width);
                if (!Inside(stair, footprint)) continue;
                var expanded = Rect(P, t0 - Landing, t1 + Landing, -0.2, WallInset + Width + 0.3);
                if (taken.Any(o => Overlap(o, expanded))) continue;
                var c = WallInset + Width / 2;
                var s = fromStart ? (P(t0, c), P(t1, c)) : (P(t1, c), P(t0, c));
                var notch = Rect(P, t0, t1, -0.2, WallInset + Width + 0.15);   // open to the wall: a notch, not a hole
                return (new Stair(0, new[] { new Run((s.Item1.x, s.Item1.y), (s.Item2.x, s.Item2.y)) }), expanded, notch);
            }
        }
        return null;
    }

    /// <summary>
    /// ⭐ CE-4379 — the U. In well coordinates (v along the wall, u inward from it): run A on the wall-side lane climbs v 0 → r to h/2, the landing
    /// slab covers v r → r + 1 m across both lanes, run B on the far lane returns v r → 0 to h. r = 0.85 h = the straight ramp's slope over half the
    /// rise. The open end (v = 0) is at least <see cref="Landing"/> from the wall's end; the closed end (the landing) sits <see cref="EndMargin"/> from the
    /// corner. The notch in the floor above spans the whole well and reaches the wall.
    /// </summary>
    private static (Stair, PathD Expanded, PathD Notch)? TrySwitchBack(PathD footprint, List<(int E, double L)> edges, double h, List<PathD> taken)
    {
        double r = RunPerHeight * h / 2, wellLen = r + LandingDepth, wellW = 2 * Width + RunGap;
        foreach (var (e, len) in edges)
        {
            if (len < wellLen + EndMargin + Landing) continue;
            var P = WallFrame(footprint, e, len);
            foreach (bool cornerAtStart in new[] { true, false })
            {
                // v runs away from the corner: cornerAtStart ⇒ the landing end is at t = EndMargin and v grows toward −t
                double tv0 = cornerAtStart ? EndMargin + wellLen : len - EndMargin - wellLen;
                double sgn = cornerAtStart ? -1 : 1;
                PointD Q(double v, double u) => P(tv0 + sgn * v, WallInset + u);
                PathD Box(double v0, double v1, double u0, double u1) => new() { Q(v0, u0), Q(v1, u0), Q(v1, u1), Q(v0, u1) };
                var well = Box(0, wellLen, 0, wellW);
                if (!Inside(well, footprint)) continue;
                var expanded = Box(-Landing, wellLen + Landing, -0.2 - WallInset, wellW + 0.3);
                if (taken.Any(o => Overlap(o, expanded))) continue;
                double ua = Width / 2, ub = Width + RunGap + Width / 2;
                (double, double) Pt(double v, double u) { var q = Q(v, u); return (q.x, q.y); }
                var landing = Box(r, wellLen, 0, wellW).Select(q => (q.x, q.y)).ToList();
                var runs = new[]
                {
                    new Run(Pt(0, ua), Pt(r, ua), 0, h / 2),
                    new Run(Pt(r, ub), Pt(0, ub), h / 2, h),
                };
                var notch = Box(0, wellLen, -0.2 - WallInset, wellW + 0.15);
                return (new Stair(0, runs, landing, h / 2), expanded, notch);
            }
        }
        return null;
    }

    private static PathD Rect(Frame P, double t0, double t1, double l0, double l1) =>
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
