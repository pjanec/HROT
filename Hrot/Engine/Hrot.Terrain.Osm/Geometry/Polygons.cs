using Clipper2Lib;

namespace Hrot.Terrain.Osm;

/// <summary>A polygon with courtyards: a counter-clockwise outer ring and clockwise holes (open rings — the last point is NOT repeated). <see cref="OuterIds"/> are the OSM node ids of the outer ring's points when known.</summary>
public sealed class PolyRegion
{
    public PathD Outer { get; set; }
    public List<PathD> Holes { get; } = new();
    public long[]? OuterIds { get; set; }
    public PolyRegion(PathD outer, long[]? ids = null) { Outer = outer; OuterIds = ids; }
}

/// <summary>⭐ E-3 / E-4: every polygon operation the rules need, over Clipper2 (clip, union, offset, simplify) and one hand-written piece (hole splitting).</summary>
public static class Polygons
{
    public const int Precision = 3;   // Clipper works in millimetres

    public static double SignedArea(IReadOnlyList<PointD> p)
    {
        double a = 0;
        for (int i = 0, n = p.Count; i < n; i++) { var q = p[(i + 1) % n]; a += p[i].x * q.y - q.x * p[i].y; }
        return a / 2;
    }

    public static double Area(IReadOnlyList<PointD> p) => Math.Abs(SignedArea(p));

    public static PathD Ccw(PathD p) { if (SignedArea(p) < 0) p.Reverse(); return p; }
    public static PathD Cw(PathD p) { if (SignedArea(p) > 0) p.Reverse(); return p; }

    public static bool Contains(IReadOnlyList<PointD> ring, double x, double y)
    {
        bool inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            if ((ring[i].y > y) != (ring[j].y > y) && x < (ring[j].x - ring[i].x) * (y - ring[i].y) / (ring[j].y - ring[i].y) + ring[i].x) inside = !inside;
        return inside;
    }

    public static (double MinX, double MinY, double MaxX, double MaxY) Bbox(IReadOnlyList<PointD> p)
    {
        double a = double.MaxValue, b = double.MaxValue, c = double.MinValue, d = double.MinValue;
        foreach (var q in p) { if (q.x < a) a = q.x; if (q.x > c) c = q.x; if (q.y < b) b = q.y; if (q.y > d) d = q.y; }
        return (a, b, c, d);
    }

    public static PathD Rect(double minX, double minY, double maxX, double maxY) =>
        new() { new PointD(minX, minY), new PointD(maxX, minY), new PointD(maxX, maxY), new PointD(minX, maxY) };

    /// <summary>Removes repeated points (and the closing duplicate) — and the ids with them.</summary>
    public static (PathD Ring, long[]? Ids) Clean(IEnumerable<PointD> pts, long[]? ids = null)
    {
        var ring = new PathD();
        var outIds = ids == null ? null : new List<long>();
        int i = 0;
        foreach (var p in pts)
        {
            if (ring.Count == 0 || Math.Abs(ring[^1].x - p.x) > 1e-9 || Math.Abs(ring[^1].y - p.y) > 1e-9) { ring.Add(p); outIds?.Add(ids![i]); }
            i++;
        }
        while (ring.Count > 1 && Math.Abs(ring[0].x - ring[^1].x) < 1e-9 && Math.Abs(ring[0].y - ring[^1].y) < 1e-9) { ring.RemoveAt(ring.Count - 1); outIds?.RemoveAt(outIds.Count - 1); }
        return (ring, outIds?.ToArray());
    }

    /// <summary>The regions of a Clipper result tree (outer rings with their holes; islands inside holes become regions of their own).</summary>
    private static void Collect(PolyPathD node, List<PolyRegion> into)
    {
        for (int i = 0; i < node.Count; i++)
        {
            var child = (PolyPathD)node[i];
            if (child.IsHole) { Collect(child, into); continue; }
            var region = new PolyRegion(Ccw(new PathD(child.Polygon!)));
            for (int h = 0; h < child.Count; h++)
            {
                var hole = (PolyPathD)child[h];
                region.Holes.Add(Cw(new PathD(hole.Polygon!)));
                Collect(hole, into);
            }
            into.Add(region);
        }
    }

    private static PathsD AsPaths(PolyRegion r)
    {
        var ps = new PathsD { r.Outer };
        ps.AddRange(r.Holes);
        return ps;
    }

    /// <summary>The region clipped to a rectangle; the result may be several regions, or none.</summary>
    public static List<PolyRegion> ClipToRect(PolyRegion r, double minX, double minY, double maxX, double maxY)
    {
        var tree = new PolyTreeD();
        var c = new ClipperD(Precision);
        c.AddSubject(AsPaths(r));
        c.AddClip(new PathsD { Rect(minX, minY, maxX, maxY) });
        c.Execute(ClipType.Intersection, FillRule.EvenOdd, tree);
        var list = new List<PolyRegion>();
        Collect(tree, list);
        return list;
    }

    /// <summary>The union of counter-clockwise rings, as regions (outer rings with holes).</summary>
    public static List<PolyRegion> Union(IEnumerable<PathD> ccwRings)
    {
        var tree = new PolyTreeD();
        var c = new ClipperD(Precision);
        var subject = new PathsD();
        foreach (var p in ccwRings) subject.Add(p);
        if (subject.Count == 0) return new List<PolyRegion>();
        c.AddSubject(subject);
        c.Execute(ClipType.Union, FillRule.NonZero, tree);
        var list = new List<PolyRegion>();
        Collect(tree, list);
        return list;
    }

    /// <summary>An open line grown into a polygon <paramref name="width"/> metres wide (round ends) — rivers (F-5).</summary>
    public static List<PolyRegion> Buffer(PathD line, double width)
    {
        var grown = Clipper.InflatePaths(new PathsD { line }, width / 2, JoinType.Round, EndType.Round, 2.0, Precision, 0.1);
        var tree = new PolyTreeD();
        var c = new ClipperD(Precision);
        c.AddSubject(grown);
        c.Execute(ClipType.Union, FillRule.NonZero, tree);
        var list = new List<PolyRegion>();
        Collect(tree, list);
        return list;
    }

    /// <summary>Open lines clipped to a rectangle (a line leaving and re-entering gives two pieces).</summary>
    public static PathsD ClipLines(PathD line, double minX, double minY, double maxX, double maxY) =>
        Clipper.RectClipLines(new RectD(minX, minY, maxX, maxY), new PathsD { line }, Precision);

    public static PathD Simplify(PathD ring, double epsilon)
    {
        var s = Clipper.SimplifyPath(ring, epsilon, true);
        return s.Count >= 3 ? s : ring;
    }

    /// <summary>
    /// ⭐ E-4: hole-free parts. A region with courtyards is cut by a straight line through each hole's centre, parallel to the hole's SHORTER
    /// axis (the cut crosses the hole where it is narrowest), repeated until no hole is left; parts under <paramref name="minArea"/> are dropped.
    /// A hole the cut cannot bisect (a concave one) is filled and counted in <paramref name="filledHoles"/>.
    /// </summary>
    public static List<PathD> SplitHoles(PolyRegion region, double minArea, ref int filledHoles)
    {
        var parts = new List<PathD>();
        var work = new Stack<PolyRegion>();
        work.Push(region);
        int guard = 0;
        while (work.Count > 0)
        {
            var r = work.Pop();
            if (r.Holes.Count == 0)
            {
                if (Area(r.Outer) >= minArea) parts.Add(Ccw(r.Outer));
                continue;
            }
            if (++guard > 400) { filledHoles += r.Holes.Count; if (Area(r.Outer) >= minArea) parts.Add(Ccw(r.Outer)); continue; }
            var hole = r.Holes[0];
            var (hx0, hy0, hx1, hy1) = Bbox(hole);
            bool horizontal = hx1 - hx0 <= hy1 - hy0;   // the shorter axis is x ⇒ a cut parallel to x
            bool cutDone = false;
            foreach (double f in new[] { 0.5, 0.4, 0.6, 0.3, 0.7, 0.2, 0.8 })
            {
                var (ox0, oy0, ox1, oy1) = Bbox(r.Outer);
                double at = horizontal ? hy0 + (hy1 - hy0) * f : hx0 + (hx1 - hx0) * f;
                var lowHalf = horizontal ? Rect(ox0 - 1, oy0 - 1, ox1 + 1, at) : Rect(ox0 - 1, oy0 - 1, at, oy1 + 1);
                var highHalf = horizontal ? Rect(ox0 - 1, at, ox1 + 1, oy1 + 1) : Rect(at, oy0 - 1, ox1 + 1, oy1 + 1);
                var (lb0, lb1, lb2, lb3) = Bbox(lowHalf);
                var (hb0, hb1, hb2, hb3) = Bbox(highHalf);
                var lowHole = ClipToRect(new PolyRegion(hole), lb0, lb1, lb2, lb3);
                var highHole = ClipToRect(new PolyRegion(hole), hb0, hb1, hb2, hb3);
                if (lowHole.Count == 0 || highHole.Count == 0) continue;   // this cut line misses the hole
                foreach (var half in new[] { lowHalf, highHalf })
                {
                    var (a0, a1, a2, a3) = Bbox(half);
                    foreach (var piece in ClipToRect(r, a0, a1, a2, a3)) work.Push(piece);
                }
                cutDone = true;
                break;
            }
            if (!cutDone) { filledHoles++; r.Holes.RemoveAt(0); work.Push(r); }
        }
        return parts;
    }

    /// <summary>Clip to the bounds, split holes, drop small parts — the standard path of woods, scrub and water.</summary>
    public static List<PathD> ToParts(IEnumerable<PolyRegion> regions, AreaBounds bounds, double minArea, ref int filledHoles)
    {
        var parts = new List<PathD>();
        foreach (var r in regions)
            foreach (var clipped in ClipToRect(r, bounds.MinX, bounds.MinY, bounds.MaxX, bounds.MaxY))
                parts.AddRange(SplitHoles(clipped, minArea, ref filledHoles));
        return parts;
    }
}
