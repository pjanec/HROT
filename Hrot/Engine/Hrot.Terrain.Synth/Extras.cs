using Hrot.Terrain.InputSet;

namespace Hrot.Tools.TerrainSynth;

/// <summary>The synth's helpers on <see cref="AreaFeature"/> rings (closed = first point repeated last).</summary>
internal static class AreaRings
{
    internal static IReadOnlyList<(double X, double Y)> Close(IReadOnlyList<(double X, double Y)> open)
    {
        var closed = open.ToList();
        closed.Add(open[0]);
        return closed;
    }

    internal static List<(double X, double Y)> OpenRing(this AreaFeature f) => f.Ring.Take(f.Ring.Count - 1).ToList();
}

/// <summary>The woods mask as a raster of square cells; polygons are traced from it.</summary>
public sealed class WoodsMask
{
    public bool[,] Cells { get; }
    public double Cell { get; }
    public double MinX { get; }
    public double MinY { get; }

    public WoodsMask(bool[,] cells, double cell, double minX, double minY)
    {
        Cells = cells; Cell = cell; MinX = minX; MinY = minY;
    }

    /// <summary>True when a woods cell lies within the given number of cells of the point.</summary>
    public bool NearWoods(double x, double y, int cells)
    {
        int ci = (int)Math.Floor((x - MinX) / Cell), cj = (int)Math.Floor((y - MinY) / Cell);
        for (int i = ci - cells; i <= ci + cells; i++)
            for (int j = cj - cells; j <= cj + cells; j++)
                if (i >= 0 && j >= 0 && i < Cells.GetLength(0) && j < Cells.GetLength(1) && Cells[i, j]) return true;
        return false;
    }
}

/// <summary>Water polygons plus a conservative raster of "too close to water" for the placement exclusions.</summary>
public sealed class WaterField
{
    private const double Cell = 5.0;
    private const double Margin = 8.0;

    private readonly SynthProfile _p;
    private readonly bool[,] _near;

    public IReadOnlyList<AreaFeature> Features { get; }

    public WaterField(SynthProfile p, IReadOnlyList<AreaFeature> features)
    {
        _p = p;
        Features = features;
        int cols = (int)Math.Ceiling(p.Width / Cell), rows = (int)Math.Ceiling(p.Height / Cell);
        _near = new bool[cols, rows];
        foreach (var f in features)
        {
            var ring = f.OpenRing();
            int i0 = Math.Max(0, (int)Math.Floor((ring.Min(v => v.X) - Margin - p.MinX) / Cell));
            int i1 = Math.Min(cols - 1, (int)Math.Floor((ring.Max(v => v.X) + Margin - p.MinX) / Cell));
            int j0 = Math.Max(0, (int)Math.Floor((ring.Min(v => v.Y) - Margin - p.MinY) / Cell));
            int j1 = Math.Min(rows - 1, (int)Math.Floor((ring.Max(v => v.Y) + Margin - p.MinY) / Cell));
            for (int i = i0; i <= i1; i++)
                for (int j = j0; j <= j1; j++)
                {
                    double x = p.MinX + (i + 0.5) * Cell, y = p.MinY + (j + 0.5) * Cell;
                    if (Geo.Inside(ring, x, y) || Geo.OutlineDistance(ring, x, y) <= Margin) _near[i, j] = true;
                }
        }
    }

    /// <summary>True when the point is on water or within a few metres of it (nothing is placed there).</summary>
    public bool Near(double x, double y)
    {
        int i = (int)Math.Floor((x - _p.MinX) / Cell), j = (int)Math.Floor((y - _p.MinY) / Cell);
        return i >= 0 && j >= 0 && i < _near.GetLength(0) && j < _near.GetLength(1) && _near[i, j];
    }
}

/// <summary>The optional extras: one pond, one river strip and a few scrub patches.</summary>
public static class ExtrasGenerator
{
    private static double R2(double v) => Math.Round(v, 2);

    private static bool TouchesNodata(SynthProfile p, IReadOnlyList<(double X, double Y)> ring, double margin)
    {
        double minX = ring.Min(v => v.X), maxX = ring.Max(v => v.X), minY = ring.Min(v => v.Y), maxY = ring.Max(v => v.Y);
        foreach (var r in p.NodataRects)
            if (maxX >= r.MinX - margin && minX <= r.MaxX + margin && maxY >= r.MinY - margin && minY <= r.MaxY + margin) return true;
        return false;
    }

    // a smooth irregular blob around (cx, cy), counter-clockwise
    private static List<(double X, double Y)> Blob(Rng rng, double cx, double cy, double radius, int n, double spread)
    {
        var f = new double[n];
        for (int i = 0; i < n; i++) f[i] = rng.Range(1 - spread, 1 + spread);
        var s = new double[n];
        for (int i = 0; i < n; i++) s[i] = (f[(i + n - 1) % n] + 2 * f[i] + f[(i + 1) % n]) / 4;
        var ring = new List<(double X, double Y)>();
        for (int i = 0; i < n; i++)
        {
            double a = 2 * Math.PI * i / n;
            ring.Add((cx + radius * s[i] * Math.Cos(a), cy + radius * s[i] * Math.Sin(a)));
        }
        return ring;
    }

    public static WaterField Water(SynthProfile p, Rng rng, RoadNetwork r) => Water(p, rng, r, null);

    /// <summary>⭐ CE-4428 F-2 — <paramref name="mode"/> "none" (an empty field), "ponds" (the pond only), "river" (the river only) or null (both, as always).</summary>
    public static WaterField Water(SynthProfile p, Rng rng, RoadNetwork r, string? mode)
    {
        if (mode == "none") return new WaterField(p, new List<AreaFeature>());
        var river = mode == "ponds" ? null : MakeRiver(p, rng.Fork("extras.river"));
        var pond = mode == "river" ? null : MakePond(p, rng.Fork("extras.pond"), r, river);
        var features = new List<AreaFeature>();
        if (pond != null) features.Add(new AreaFeature(AreaRings.Close(pond), "pond"));
        if (river != null) features.Add(new AreaFeature(AreaRings.Close(river), "river"));
        return new WaterField(p, features);
    }

    private static List<(double X, double Y)>? MakeRiver(SynthProfile p, Rng rng)
    {
        double cx = (p.MinX + p.MaxX) / 2;
        for (int attempt = 0; attempt < 300; attempt++)
        {
            double x = rng.Range(p.MinX + 0.15 * p.Width, p.MinX + 0.55 * p.Width);
            double y = p.MinY;
            double w0 = rng.Range(18, 27), phase = rng.Range(0, 2 * Math.PI);
            var centre = new List<(double X, double Y)> { (x, y) };
            double theta = 0;
            while (y < p.MaxY && centre.Count < 2000)
            {
                theta = Math.Clamp(theta * 0.9 + rng.Range(-0.12, 0.12), -0.4, 0.4);
                double nx = x + 40 * Math.Sin(theta), ny = y + 40 * Math.Cos(theta);
                if (nx < p.MinX + 0.08 * p.Width || nx > p.MaxX - 0.08 * p.Width)
                {
                    theta = nx < cx ? 0.25 : -0.25;
                    nx = x + 40 * Math.Sin(theta);
                    ny = y + 40 * Math.Cos(theta);
                }
                if (ny >= p.MaxY)
                {
                    double t = (p.MaxY - y) / (ny - y);
                    nx = x + (nx - x) * t;
                    ny = p.MaxY;
                }
                centre.Add((nx, ny));
                x = nx; y = ny;
            }

            var left = new List<(double X, double Y)>();
            var right = new List<(double X, double Y)>();
            for (int i = 0; i < centre.Count; i++)
            {
                var a = centre[Math.Max(0, i - 1)]; var b = centre[Math.Min(centre.Count - 1, i + 1)];
                double tx = b.X - a.X, ty = b.Y - a.Y, len = Math.Sqrt(tx * tx + ty * ty);
                tx /= len; ty /= len;
                double half = Math.Clamp(w0 + 3 * Math.Sin(i * 0.17 + phase), 15, 30) / 2;
                // the end cross-sections lie on the bounds: keep them inside
                left.Add((R2(Math.Clamp(centre[i].X - ty * half, p.MinX, p.MaxX)), R2(Math.Clamp(centre[i].Y + tx * half, p.MinY, p.MaxY))));
                right.Add((R2(Math.Clamp(centre[i].X + ty * half, p.MinX, p.MaxX)), R2(Math.Clamp(centre[i].Y - tx * half, p.MinY, p.MaxY))));
            }
            var ring = new List<(double X, double Y)>(right);
            for (int i = left.Count - 1; i >= 0; i--) ring.Add(left[i]);
            if (Geo.SignedArea(ring) < 0) ring.Reverse();
            if (ring.Any(v => v.X < p.MinX || v.X > p.MaxX || v.Y < p.MinY || v.Y > p.MaxY)) continue;
            if (TouchesNodata(p, ring, 30)) continue;
            if (Geo.SelfIntersects(ring)) continue;
            return ring;
        }
        return null;
    }

    private static List<(double X, double Y)>? MakePond(SynthProfile p, Rng rng, RoadNetwork r, List<(double X, double Y)>? river)
    {
        for (int attempt = 0; attempt < 3000; attempt++)
        {
            double area = rng.Range(2000, 8000), r0 = Math.Sqrt(area / Math.PI);
            double margin = r0 * 1.4 + 25;
            if (p.Width < 2 * margin || p.Height < 2 * margin) return null;
            double cx = rng.Range(p.MinX + margin, p.MaxX - margin), cy = rng.Range(p.MinY + margin, p.MaxY - margin);
            var blob = Blob(rng, cx, cy, r0, 20, 0.22);
            double scale = Math.Sqrt(area / Geo.SignedArea(blob));
            var ring = blob.Select(v => (X: R2(cx + (v.X - cx) * scale), Y: R2(cy + (v.Y - cy) * scale))).ToList();
            double maxR = ring.Max(v => Math.Sqrt((v.X - cx) * (v.X - cx) + (v.Y - cy) * (v.Y - cy)));

            if (r.Villages.Any(v => Math.Sqrt((v.X - cx) * (v.X - cx) + (v.Y - cy) * (v.Y - cy)) < v.Radius + maxR + 30)) continue;
            if (TouchesNodata(p, ring, 30)) continue;
            bool onRoad = r.DistanceToRoad(cx, cy) < 8;
            for (int i = 0; i < ring.Count && !onRoad; i++)
            {
                var a = ring[i]; var b = ring[(i + 1) % ring.Count];
                onRoad = r.DistanceToRoad(a.X, a.Y) < 8 || r.DistanceToRoad((a.X + b.X) / 2, (a.Y + b.Y) / 2) < 8;
            }
            if (onRoad) continue;
            if (river != null)
            {
                if (Geo.BoxesOverlap(ring, river, 25) && (ring.Any(v => Geo.Inside(river, v.X, v.Y) || Geo.OutlineDistance(river, v.X, v.Y) < 25) || Geo.Inside(ring, river[0].X, river[0].Y)
                    || river.Any(v => Geo.OutlineDistance(ring, v.X, v.Y) < 25 || Geo.Inside(ring, v.X, v.Y)))) continue;
            }
            return ring;
        }
        return null;
    }

    /// <summary>3 to 8 scrub patches outside woods, villages, water, buildings and nodata.</summary>
    public static IReadOnlyList<AreaFeature> Scrub(SynthProfile p, Rng rng, RoadNetwork r, WaterField water, WoodsMask? woods, IReadOnlyList<Building> buildings)
    {
        var footprints = buildings.Select(b => b.Footprint.Take(b.Footprint.Count - 1).ToList()).ToList();
        int want = rng.Int(3, 9);
        var placed = new List<(double X, double Y, double R, AreaFeature F)>();
        for (int k = 0; k < want; k++)
        {
            for (int attempt = 0; attempt < 800; attempt++)
            {
                double radius = rng.Range(25, 70);
                double margin = radius * 1.3 + 20;
                if (p.Width < 2 * margin || p.Height < 2 * margin) break;
                double cx = rng.Range(p.MinX + margin, p.MaxX - margin), cy = rng.Range(p.MinY + margin, p.MaxY - margin);
                var blob = Blob(rng, cx, cy, radius, rng.Int(12, 19), 0.25);
                var ring = blob.Select(v => (X: R2(v.X), Y: R2(v.Y))).ToList();
                if (Ok(p, r, water, woods, footprints, placed.Select(q => (q.X, q.Y, q.R)).ToList(), ring, cx, cy, radius))
                {
                    placed.Add((cx, cy, radius, new AreaFeature(AreaRings.Close(ring), "scrub")));
                    break;
                }
            }
        }
        return placed.Select(q => q.F).ToList();
    }

    private static bool Ok(SynthProfile p, RoadNetwork r, WaterField water, WoodsMask? woods, List<List<(double X, double Y)>> footprints,
        List<(double X, double Y, double R)> others, List<(double X, double Y)> ring, double cx, double cy, double radius)
    {
        double maxR = ring.Max(v => Math.Sqrt((v.X - cx) * (v.X - cx) + (v.Y - cy) * (v.Y - cy)));
        if (r.Villages.Any(v => Math.Sqrt((v.X - cx) * (v.X - cx) + (v.Y - cy) * (v.Y - cy)) < v.Radius + maxR + 20)) return false;
        if (others.Any(o => Math.Sqrt((o.X - cx) * (o.X - cx) + (o.Y - cy) * (o.Y - cy)) < o.R * 1.3 + maxR + 10)) return false;
        if (TouchesNodata(p, ring, 10)) return false;

        // the outline, the middle and a ring halfway out
        var samples = new List<(double X, double Y)>(ring) { (cx, cy) };
        foreach (var v in ring) samples.Add((cx + (v.X - cx) * 0.5, cy + (v.Y - cy) * 0.5));
        foreach (var (x, y) in samples)
        {
            if (woods != null && woods.NearWoods(x, y, 3)) return false;
            if (water.Near(x, y)) return false;
            if (r.DistanceToRoad(x, y) < 4) return false;
        }
        foreach (var f in footprints)
        {
            if (!Geo.BoxesOverlap(ring, f, 8)) continue;
            if (ring.Any(v => Geo.Inside(f, v.X, v.Y) || Geo.OutlineDistance(f, v.X, v.Y) < 5)) return false;
            if (f.Any(v => Geo.Inside(ring, v.X, v.Y) || Geo.OutlineDistance(ring, v.X, v.Y) < 5)) return false;
        }
        return true;
    }
}

/// <summary>Traces the woods raster into counter-clockwise polygons without holes.</summary>
public static class WoodsPolygons
{
    private static readonly int[] Dx = { 1, 0, -1, 0 };
    private static readonly int[] Dy = { 0, 1, 0, -1 };

    private static long K(int x, int y) => ((long)x << 32) | (uint)y;

    /// <summary>Components smaller than minCells are dropped; outlines are simplified to about eps metres.</summary>
    public static IReadOnlyList<List<(double X, double Y)>> Trace(WoodsMask mask, double eps, int minCells)
    {
        int cols = mask.Cells.GetLength(0), rows = mask.Cells.GetLength(1);
        var seen = new bool[cols, rows];
        var result = new List<List<(double X, double Y)>>();
        for (int j = 0; j < rows; j++)
            for (int i = 0; i < cols; i++)
            {
                if (!mask.Cells[i, j] || seen[i, j]) continue;
                var comp = new List<(int X, int Y)>();
                var stack = new Stack<(int X, int Y)>();
                stack.Push((i, j));
                seen[i, j] = true;
                while (stack.Count > 0)
                {
                    var c = stack.Pop();
                    comp.Add(c);
                    for (int d = 0; d < 4; d++)
                    {
                        int nx = c.X + Dx[d], ny = c.Y + Dy[d];
                        if (nx < 0 || ny < 0 || nx >= cols || ny >= rows || !mask.Cells[nx, ny] || seen[nx, ny]) continue;
                        seen[nx, ny] = true;
                        stack.Push((nx, ny));
                    }
                }
                if (comp.Count >= minCells) Process(comp, mask, eps, result);
            }
        return result;
    }

    private static List<List<(int X, int Y)>> Components(IEnumerable<(int X, int Y)> cells)
    {
        var set = new HashSet<long>(cells.Select(c => K(c.X, c.Y)));
        var ordered = cells.OrderBy(c => c.Y).ThenBy(c => c.X).ToList();
        var seen = new HashSet<long>();
        var comps = new List<List<(int X, int Y)>>();
        foreach (var start in ordered)
        {
            if (!seen.Add(K(start.X, start.Y))) continue;
            var comp = new List<(int X, int Y)>();
            var stack = new Stack<(int X, int Y)>();
            stack.Push(start);
            while (stack.Count > 0)
            {
                var c = stack.Pop();
                comp.Add(c);
                for (int d = 0; d < 4; d++)
                {
                    int nx = c.X + Dx[d], ny = c.Y + Dy[d];
                    long key = K(nx, ny);
                    if (set.Contains(key) && seen.Add(key)) stack.Push((nx, ny));
                }
            }
            comps.Add(comp);
        }
        return comps;
    }

    private static void Process(List<(int X, int Y)> cells, WoodsMask mask, double eps, List<List<(double X, double Y)>> output)
    {
        var loops = Loops(cells);
        var holes = loops.Where(l => LatticeArea(l) < 0).ToList();
        if (holes.Count == 0)
        {
            foreach (var l in loops.Where(l => LatticeArea(l) > 0))
            {
                var ring = ToRing(l, mask, eps);
                if (ring != null) output.Add(ring);
            }
            return;
        }
        // a hole: cut the component along a column through the middle of the first hole, each part is handled on its own
        var h = holes[0];
        int cut = (h.Min(v => v.X) + h.Max(v => v.X)) / 2;
        foreach (var part in new[] { cells.Where(c => c.X < cut), cells.Where(c => c.X >= cut) })
            foreach (var comp in Components(part))
                Process(comp, mask, eps, output);
    }

    private static double LatticeArea(List<(int X, int Y)> v)
    {
        double a = 0;
        for (int i = 0; i < v.Count; i++) { var p = v[i]; var q = v[(i + 1) % v.Count]; a += (double)p.X * q.Y - (double)q.X * p.Y; }
        return a / 2;
    }

    // boundary loops with the cells on the left; at a pinch the right turn is taken, which keeps cells 4-connected
    private static List<List<(int X, int Y)>> Loops(List<(int X, int Y)> cells)
    {
        var set = new HashSet<long>(cells.Select(c => K(c.X, c.Y)));
        var outgoing = new SortedDictionary<long, int>();
        void Add(int vx, int vy, int dir)
        {
            long key = K(vx, vy);
            outgoing[key] = outgoing.GetValueOrDefault(key) | (1 << dir);
        }
        foreach (var (i, j) in cells.OrderBy(c => c.Y).ThenBy(c => c.X))
        {
            if (!set.Contains(K(i, j - 1))) Add(i, j, 0);
            if (!set.Contains(K(i + 1, j))) Add(i + 1, j, 1);
            if (!set.Contains(K(i, j + 1))) Add(i + 1, j + 1, 2);
            if (!set.Contains(K(i - 1, j))) Add(i, j + 1, 3);
        }

        var used = new HashSet<(long, int)>();
        var loops = new List<List<(int X, int Y)>>();
        foreach (var kv in outgoing.ToList())
        {
            for (int sd = 0; sd < 4; sd++)
            {
                if ((kv.Value & (1 << sd)) == 0 || used.Contains((kv.Key, sd))) continue;
                int sx = (int)(kv.Key >> 32), sy = (int)(uint)kv.Key;
                var loop = new List<(int X, int Y)>();
                int vx = sx, vy = sy, d = sd;
                do
                {
                    used.Add((K(vx, vy), d));
                    loop.Add((vx, vy));
                    vx += Dx[d]; vy += Dy[d];
                    int mask = outgoing[K(vx, vy)];
                    int next = -1;
                    foreach (int cand in new[] { (d + 3) % 4, d, (d + 1) % 4 })
                        if ((mask & (1 << cand)) != 0) { next = cand; break; }
                    d = next;
                } while (!(vx == sx && vy == sy && d == sd));
                loops.Add(loop);
            }
        }
        return loops;
    }

    private static List<(double X, double Y)>? ToRing(List<(int X, int Y)> loop, WoodsMask mask, double eps)
    {
        var pts = loop.Select(v => (X: mask.MinX + v.X * mask.Cell, Y: mask.MinY + v.Y * mask.Cell)).ToList();
        // drop collinear vertices
        var corners = new List<(double X, double Y)>();
        for (int i = 0; i < pts.Count; i++)
        {
            var a = pts[(i + pts.Count - 1) % pts.Count]; var b = pts[i]; var c = pts[(i + 1) % pts.Count];
            if ((b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X) != 0) corners.Add(b);
        }
        if (corners.Count < 3) return null;
        for (double e = eps; e >= eps / 4 - 1e-9; e /= 2)
        {
            var s = Geo.Simplify(corners, e);
            if (s.Count >= 3 && Geo.SignedArea(s) > 0 && !Geo.SelfIntersects(s)) return s.Select(v => (Math.Round(v.X, 2), Math.Round(v.Y, 2))).ToList();
        }
        return Geo.SelfIntersects(corners) ? null : corners.Select(v => (Math.Round(v.X, 2), Math.Round(v.Y, 2))).ToList();
    }
}
