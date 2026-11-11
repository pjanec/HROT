namespace Hrot.Tools.TerrainSynth;

/// <summary>Small polygon helpers for the extras. A ring here is an open list of vertices (no repeated first point).</summary>
internal static class Geo
{
    public static double SignedArea(IReadOnlyList<(double X, double Y)> v)
    {
        double a = 0;
        for (int i = 0; i < v.Count; i++)
        {
            var p = v[i]; var q = v[(i + 1) % v.Count];
            a += p.X * q.Y - q.X * p.Y;
        }
        return a / 2;
    }

    public static bool Inside(IReadOnlyList<(double X, double Y)> v, double x, double y)
    {
        bool inside = false;
        for (int i = 0, j = v.Count - 1; i < v.Count; j = i++)
            if ((v[i].Y > y) != (v[j].Y > y) && x < (v[j].X - v[i].X) * (y - v[i].Y) / (v[j].Y - v[i].Y) + v[i].X)
                inside = !inside;
        return inside;
    }

    public static double SegDistance(double px, double py, (double X, double Y) a, (double X, double Y) b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y, l2 = dx * dx + dy * dy;
        double t = l2 <= 0 ? 0 : Math.Clamp(((px - a.X) * dx + (py - a.Y) * dy) / l2, 0, 1);
        double qx = a.X + t * dx - px, qy = a.Y + t * dy - py;
        return Math.Sqrt(qx * qx + qy * qy);
    }

    // distance to the outline (0 is not returned for interior points)
    public static double OutlineDistance(IReadOnlyList<(double X, double Y)> v, double x, double y)
    {
        double best = double.PositiveInfinity;
        for (int i = 0; i < v.Count; i++) best = Math.Min(best, SegDistance(x, y, v[i], v[(i + 1) % v.Count]));
        return best;
    }

    private static bool ProperCross((double X, double Y) a, (double X, double Y) b, (double X, double Y) c, (double X, double Y) d)
    {
        static double O((double X, double Y) p, (double X, double Y) q, (double X, double Y) s) => (q.X - p.X) * (s.Y - p.Y) - (q.Y - p.Y) * (s.X - p.X);
        return O(a, b, c) * O(a, b, d) < 0 && O(c, d, a) * O(c, d, b) < 0;
    }

    public static bool SelfIntersects(IReadOnlyList<(double X, double Y)> v)
    {
        int n = v.Count;
        for (int i = 0; i < n; i++)
            for (int j = i + 2; j < n; j++)
            {
                if (i == 0 && j == n - 1) continue;
                if (ProperCross(v[i], v[(i + 1) % n], v[j], v[(j + 1) % n])) return true;
            }
        return false;
    }

    // Douglas-Peucker on one chain; keeps the first and last point
    private static void Chain(IReadOnlyList<(double X, double Y)> pts, int lo, int hi, double eps, bool[] keep)
    {
        if (hi <= lo + 1) return;
        double best = -1;
        int at = -1;
        for (int i = lo + 1; i < hi; i++)
        {
            double d = SegDistance(pts[i].X, pts[i].Y, pts[lo], pts[hi]);
            if (d > best) { best = d; at = i; }
        }
        if (best <= eps) return;
        keep[at] = true;
        Chain(pts, lo, at, eps, keep);
        Chain(pts, at, hi, eps, keep);
    }

    /// <summary>Douglas-Peucker for a closed ring: split at the vertex farthest from vertex 0 and simplify both halves.</summary>
    public static List<(double X, double Y)> Simplify(IReadOnlyList<(double X, double Y)> ring, double eps)
    {
        int n = ring.Count;
        if (n <= 4) return ring.ToList();
        int far = 0;
        double bd = -1;
        for (int i = 1; i < n; i++)
        {
            double d = (ring[i].X - ring[0].X) * (ring[i].X - ring[0].X) + (ring[i].Y - ring[0].Y) * (ring[i].Y - ring[0].Y);
            if (d > bd) { bd = d; far = i; }
        }
        var pts = new List<(double X, double Y)>(ring) { ring[0] };
        var keep = new bool[n + 1];
        keep[0] = keep[far] = keep[n] = true;
        Chain(pts, 0, far, eps, keep);
        Chain(pts, far, n, eps, keep);
        var result = new List<(double X, double Y)>();
        for (int i = 0; i < n; i++) if (keep[i]) result.Add(ring[i]);
        return result;
    }

    public static bool BoxesOverlap(IReadOnlyList<(double X, double Y)> a, IReadOnlyList<(double X, double Y)> b, double margin = 0)
    {
        return !(a.Max(p => p.X) + margin < b.Min(p => p.X) || b.Max(p => p.X) + margin < a.Min(p => p.X)
              || a.Max(p => p.Y) + margin < b.Min(p => p.Y) || b.Max(p => p.Y) + margin < a.Min(p => p.Y));
    }
}
