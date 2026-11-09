using System.Globalization;

namespace Hrot.Tools.TerrainSynth;

public enum RoadClass { Motorway, Trunk, Primary, Secondary, Tertiary, Unclassified, Residential, Rail }

public sealed record RoadNode(string Id, double X, double Y);

public sealed record RoadEdge(string Id, string From, string To, RoadClass Class, int Lanes, double SpeedMs, double WidthM);

public sealed record Village(double X, double Y, double Radius);

/// <summary>
/// Synthetic road layout: a jittered street grid per village, villages joined by connector roads, and long
/// through-roads. Every class is trimmed to its profile km. A segment is one edge, except the motorway, which is a
/// pair of opposite one-way edges per segment (LengthKm counts the centreline once).
/// </summary>
public sealed class RoadNetwork
{
    private const double CellSize = 50.0;
    private const double MaxHalfWidth = 7.5;

    private readonly List<RoadNode> _nodes = new();
    private readonly List<RoadEdge> _edges = new();
    private readonly List<Village> _villages = new();
    private readonly Dictionary<string, RoadNode> _nodeById = new();
    private readonly SynthProfile _p;

    // spatial hash of the segments, for DistanceToRoad
    private readonly Dictionary<long, List<int>> _cells = new();
    private readonly List<(double X1, double Y1, double X2, double Y2, double Half)> _segs = new();

    public IReadOnlyList<RoadNode> Nodes => _nodes;
    public IReadOnlyList<RoadEdge> Edges => _edges;
    public IReadOnlyList<Village> Villages => _villages;

    private RoadNetwork(SynthProfile p) { _p = p; }

    // lanes, speed (m/s), width (m)
    private static (int Lanes, double Speed, double Width) Spec(RoadClass c) => c switch
    {
        RoadClass.Motorway => (2, 36, 15),
        RoadClass.Trunk => (2, 28, 10),
        RoadClass.Primary => (2, 25, 8),
        RoadClass.Secondary => (2, 22, 7),
        RoadClass.Tertiary => (2, 19, 6.5),
        RoadClass.Unclassified => (1, 14, 5),
        RoadClass.Residential => (1, 14, 5),
        _ => (1, 33, 4),
    };

    private static string ClassKey(RoadClass c) => c.ToString().ToLowerInvariant();

    private double Target(RoadClass c) => _p.RoadKm.TryGetValue(ClassKey(c), out var km) ? km * 1000.0 : 0.0;

    private static double R2(double v) => Math.Round(v, 2);

    private bool InNodata(double x, double y)
    {
        foreach (var r in _p.NodataRects)
            if (x >= r.MinX - 5 && x <= r.MaxX + 5 && y >= r.MinY - 5 && y <= r.MaxY + 5) return true;
        return false;
    }

    private bool InBounds(double x, double y) => x >= _p.MinX && x <= _p.MaxX && y >= _p.MinY && y <= _p.MaxY;

    public static RoadNetwork Generate(SynthProfile p, Rng rng)
    {
        var net = new RoadNetwork(p);
        net.Build(rng);
        net.BuildHash();
        return net;
    }

    public double LengthKm(RoadClass c)
    {
        double m = 0;
        foreach (var e in _edges)
        {
            if (e.Class != c) continue;
            var a = _nodeById[e.From];
            var b = _nodeById[e.To];
            m += Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
        }
        if (c == RoadClass.Motorway) m /= 2;
        return m / 1000.0;
    }

    /// <summary>Distance from the point to the nearest road centreline minus half that road's width (negative on the carriageway).</summary>
    public double DistanceToRoad(double x, double y)
    {
        if (_segs.Count == 0) return double.PositiveInfinity;
        int cx = (int)Math.Floor((x - _p.MinX) / CellSize);
        int cy = (int)Math.Floor((y - _p.MinY) / CellSize);
        int maxRing = (int)(Math.Max(_p.Width, _p.Height) / CellSize) + 3 + (int)(Math.Max(Math.Abs(x - _p.MinX), Math.Abs(y - _p.MinY)) / CellSize);
        double best = double.PositiveInfinity;
        for (int ring = 0; ring <= maxRing; ring++)
        {
            for (int ix = cx - ring; ix <= cx + ring; ix++)
            {
                for (int iy = cy - ring; iy <= cy + ring; iy++)
                {
                    if (Math.Max(Math.Abs(ix - cx), Math.Abs(iy - cy)) != ring) continue;
                    if (!_cells.TryGetValue(Key(ix, iy), out var list)) continue;
                    foreach (int si in list)
                    {
                        var s = _segs[si];
                        double d = PointSegmentDistance(x, y, s.X1, s.Y1, s.X2, s.Y2) - s.Half;
                        if (d < best) best = d;
                    }
                }
            }
            // cells beyond this ring are at least ring * CellSize away from the point
            if (best <= ring * CellSize - MaxHalfWidth) break;
        }
        return best;
    }

    private static long Key(int ix, int iy) => ((long)ix << 32) ^ (uint)iy;

    private static double PointSegmentDistance(double px, double py, double ax, double ay, double bx, double by)
    {
        double dx = bx - ax, dy = by - ay;
        double l2 = dx * dx + dy * dy;
        double t = l2 <= 0 ? 0 : Math.Clamp(((px - ax) * dx + (py - ay) * dy) / l2, 0, 1);
        double qx = ax + t * dx - px, qy = ay + t * dy - py;
        return Math.Sqrt(qx * qx + qy * qy);
    }

    private void BuildHash()
    {
        foreach (var e in _edges)
        {
            var a = _nodeById[e.From];
            var b = _nodeById[e.To];
            int si = _segs.Count;
            _segs.Add((a.X, a.Y, b.X, b.Y, Spec(e.Class).Width / 2));
            int x0 = (int)Math.Floor((Math.Min(a.X, b.X) - _p.MinX) / CellSize), x1 = (int)Math.Floor((Math.Max(a.X, b.X) - _p.MinX) / CellSize);
            int y0 = (int)Math.Floor((Math.Min(a.Y, b.Y) - _p.MinY) / CellSize), y1 = (int)Math.Floor((Math.Max(a.Y, b.Y) - _p.MinY) / CellSize);
            for (int ix = x0; ix <= x1; ix++)
                for (int iy = y0; iy <= y1; iy++)
                {
                    long k = Key(ix, iy);
                    if (!_cells.TryGetValue(k, out var list)) _cells[k] = list = new List<int>();
                    list.Add(si);
                }
        }
    }

    // ---- construction ----

    private string AddNode(double x, double y)
    {
        var n = new RoadNode("n" + _nodes.Count.ToString(CultureInfo.InvariantCulture), R2(x), R2(y));
        _nodes.Add(n);
        _nodeById[n.Id] = n;
        return n.Id;
    }

    private void AddEdge(string from, string to, RoadClass c)
    {
        var (lanes, speed, width) = Spec(c);
        _edges.Add(new RoadEdge("e" + _edges.Count.ToString(CultureInfo.InvariantCulture), from, to, c, lanes, speed, width));
        if (c == RoadClass.Motorway)
            _edges.Add(new RoadEdge("e" + _edges.Count.ToString(CultureInfo.InvariantCulture), to, from, c, lanes, speed, width));
    }

    private double Dist(string a, string b)
    {
        var na = _nodeById[a];
        var nb = _nodeById[b];
        return Math.Sqrt((na.X - nb.X) * (na.X - nb.X) + (na.Y - nb.Y) * (na.Y - nb.Y));
    }

    private void Build(Rng rng)
    {
        PlaceVillages(rng.Fork("roads.villages"));
        var villageNodes = BuildStreets(rng.Fork("roads.streets"));
        BuildConnectors(rng.Fork("roads.connectors"), villageNodes);
        foreach (var c in new[] { RoadClass.Motorway, RoadClass.Trunk, RoadClass.Primary, RoadClass.Secondary, RoadClass.Rail })
            BuildThrough(c, rng.Fork("roads.through." + ClassKey(c)));
    }

    // ---- villages ----

    private void PlaceVillages(Rng rng)
    {
        int count = Math.Max(1, _p.BuildingCount / 60);
        double maxR = 0.35 * Math.Min(_p.Width, _p.Height);
        for (int i = 0; i < count; i++)
        {
            double r = Math.Min(rng.Range(150, 350), maxR);
            bool placed = false;
            while (!placed && r >= 40)
            {
                for (int t = 0; t < 400 && !placed; t++)
                {
                    double x = rng.Range(_p.MinX + r + 20, _p.MaxX - r - 20);
                    double y = rng.Range(_p.MinY + r + 20, _p.MaxY - r - 20);
                    if (!VillageFits(x, y, r)) continue;
                    _villages.Add(new Village(R2(x), R2(y), R2(r)));
                    placed = true;
                }
                if (!placed) r *= 0.85;
            }
        }
    }

    private bool VillageFits(double x, double y, double r)
    {
        if (x - r < _p.MinX || x + r > _p.MaxX || y - r < _p.MinY || y + r > _p.MaxY) return false;
        foreach (var rect in _p.NodataRects)
        {
            double dx = Math.Max(Math.Max(rect.MinX - x, x - rect.MaxX), 0);
            double dy = Math.Max(Math.Max(rect.MinY - y, y - rect.MaxY), 0);
            if (Math.Sqrt(dx * dx + dy * dy) < r + 20) return false;
        }
        foreach (var v in _villages)
        {
            double d = Math.Sqrt((v.X - x) * (v.X - x) + (v.Y - y) * (v.Y - y));
            if (d < v.Radius + r + 60) return false;
        }
        return true;
    }

    // ---- village street grids ----

    private readonly record struct GridSeg(int Village, long KeyA, long KeyB, double Ax, double Ay, double Bx, double By)
    {
        public double Length => Math.Sqrt((Ax - Bx) * (Ax - Bx) + (Ay - By) * (Ay - By));
    }

    private List<GridSeg> GridSegments(double spacing, Rng rng)
    {
        var all = new List<GridSeg>();
        for (int vi = 0; vi < _villages.Count; vi++)
        {
            var v = _villages[vi];
            var r = rng.Fork("village" + vi.ToString(CultureInfo.InvariantCulture));
            double ang = r.Range(0, Math.PI / 2);
            double ca = Math.Cos(ang), sa = Math.Sin(ang);
            int n = (int)Math.Ceiling(v.Radius / spacing) + 1;
            var pts = new Dictionary<long, (double X, double Y)>();
            for (int i = -n; i <= n; i++)
                for (int j = -n; j <= n; j++)
                {
                    double lx = i * spacing + r.Range(-0.15, 0.15) * spacing;
                    double ly = j * spacing + r.Range(-0.15, 0.15) * spacing;
                    if (lx * lx + ly * ly > v.Radius * v.Radius) continue;
                    pts[LatticeKey(i, j)] = (R2(v.X + ca * lx - sa * ly), R2(v.Y + sa * lx + ca * ly));
                }
            for (int i = -n; i <= n; i++)
                for (int j = -n; j <= n; j++)
                {
                    if (!pts.TryGetValue(LatticeKey(i, j), out var a)) continue;
                    foreach (var (di, dj) in new[] { (1, 0), (0, 1) })
                    {
                        if (!pts.TryGetValue(LatticeKey(i + di, j + dj), out var b)) continue;
                        if (r.NextDouble() < 0.08) continue;
                        all.Add(new GridSeg(vi, LatticeKey(i, j), LatticeKey(i + di, j + dj), a.X, a.Y, b.X, b.Y));
                    }
                }
        }
        return all;
    }

    private static long LatticeKey(int i, int j) => ((long)(i + 1000) << 20) | (uint)(j + 1000);

    // returns, per village, its street nodes
    private List<List<string>> BuildStreets(Rng rng)
    {
        var result = _villages.Select(_ => new List<string>()).ToList();
        double target = Target(RoadClass.Residential);
        if (target <= 0 || _villages.Count == 0) return result;

        double sumR2 = _villages.Sum(v => v.Radius * v.Radius);
        double s = Math.Max(15.0, 2 * Math.PI * sumR2 / target);
        double bestS = s, bestErr = double.PositiveInfinity;
        for (int it = 0; it < 14; it++)
        {
            double len = GridSegments(s, rng).Sum(g => g.Length);
            double ratio = len > 0 ? len / (target * 1.12) : 4.0;
            double err = Math.Abs(ratio - 1);
            if (err < bestErr) { bestErr = err; bestS = s; }
            if (err < 0.02) break;
            s = Math.Clamp(s * Math.Pow(ratio, 0.85), 15.0, 2000.0);
        }

        var segs = GridSegments(bestS, rng);
        // the grid is built slightly above the target; random segments are dropped (dead ends) to land on it
        double total = segs.Sum(g => g.Length);
        var drop = rng.Fork("trim");
        while (total > target && segs.Count > 0)
        {
            int k = drop.Int(0, segs.Count);
            total -= segs[k].Length;
            segs.RemoveAt(k);
        }

        var ids = new Dictionary<(int, long), string>();
        string Node(int vi, long key, double x, double y)
        {
            if (!ids.TryGetValue((vi, key), out var id))
            {
                id = AddNode(x, y);
                ids[(vi, key)] = id;
                result[vi].Add(id);
            }
            return id;
        }
        foreach (var g in segs)
        {
            string a = Node(g.Village, g.KeyA, g.Ax, g.Ay);
            string b = Node(g.Village, g.KeyB, g.Bx, g.By);
            AddEdge(a, b, RoadClass.Residential);
        }
        return result;
    }

    // ---- polylines ----

    // Emits a polyline as edges of the class until `remaining` metres are used; stops at a nodata rectangle.
    // Optional end node ids let a connector land on existing street nodes. Returns the metres emitted.
    private double Emit(RoadClass c, List<(double X, double Y)> pts, double remaining, string? startId = null, string? endId = null)
    {
        double used = 0;
        if (pts.Count < 2 || remaining < 1.0) return 0;
        string prev = startId ?? AddNode(pts[0].X, pts[0].Y);
        double px = _nodeById[prev].X, py = _nodeById[prev].Y;
        for (int k = 1; k < pts.Count; k++)
        {
            double x = R2(pts[k].X), y = R2(pts[k].Y);
            if (!InBounds(x, y) || InNodata(x, y)) break;
            double len = Math.Sqrt((x - px) * (x - px) + (y - py) * (y - py));
            if (len < 0.5) continue;
            bool last = k == pts.Count - 1;
            if (len > remaining - used)
            {
                double left = remaining - used;
                if (left < 1.0) break;
                x = R2(px + (x - px) * left / len);
                y = R2(py + (y - py) * left / len);
                len = left;
                string cut = AddNode(x, y);
                AddEdge(prev, cut, c);
                used += len;
                break;
            }
            string id = last && endId != null ? endId : AddNode(x, y);
            AddEdge(prev, id, c);
            used += len;
            prev = id;
            px = x; py = y;
        }
        return used;
    }

    // ---- through roads ----

    private List<(double X, double Y)> WanderPath(RoadClass c, int index, Rng rng, double phase)
    {
        double step = c switch
        {
            RoadClass.Motorway or RoadClass.Rail => 300,
            RoadClass.Trunk => 250,
            RoadClass.Primary => 200,
            _ => 180,
        };
        bool alongX = index % 2 == 0;
        bool reverse = (index / 2) % 2 == 1;
        double frac = 0.12 + 0.76 * ((phase + (index / 2) * 0.6180339887498949 + rng.NextDouble() * 0.05) % 1.0);
        double x, y, baseAng;
        if (alongX)
        {
            y = _p.MinY + frac * _p.Height;
            x = reverse ? _p.MaxX : _p.MinX;
            baseAng = reverse ? Math.PI : 0;
        }
        else
        {
            x = _p.MinX + frac * _p.Width;
            y = reverse ? _p.MaxY : _p.MinY;
            baseAng = reverse ? -Math.PI / 2 : Math.PI / 2;
        }
        var pts = new List<(double, double)> { (x, y) };
        double dev = 0;
        for (int k = 0; k < 2000; k++)
        {
            dev = Math.Clamp(dev * 0.85 + rng.Range(-0.3, 0.3), -0.6, 0.6);
            double ang = baseAng + dev;
            double nx = x + step * Math.Cos(ang), ny = y + step * Math.Sin(ang);
            if (!InBounds(nx, ny))
            {
                double t = 1.0;
                double dx = nx - x, dy = ny - y;
                if (nx < _p.MinX) t = Math.Min(t, (_p.MinX - x) / dx);
                if (nx > _p.MaxX) t = Math.Min(t, (_p.MaxX - x) / dx);
                if (ny < _p.MinY) t = Math.Min(t, (_p.MinY - y) / dy);
                if (ny > _p.MaxY) t = Math.Min(t, (_p.MaxY - y) / dy);
                nx = Math.Clamp(x + dx * t, _p.MinX, _p.MaxX);
                ny = Math.Clamp(y + dy * t, _p.MinY, _p.MaxY);
                pts.Add((nx, ny));
                break;
            }
            pts.Add((nx, ny));
            x = nx; y = ny;
        }
        return pts;
    }

    private void BuildThrough(RoadClass c, Rng rng)
    {
        double remaining = Target(c);
        double phase = rng.NextDouble();
        for (int i = 0; i < 400 && remaining >= 1.0; i++)
        {
            var path = WanderPath(c, i, rng, phase);
            remaining -= Emit(c, path, remaining);
        }
    }

    // ---- connectors between villages ----

    private void BuildConnectors(Rng rng, List<List<string>> villageNodes)
    {
        var classes = new[] { RoadClass.Unclassified, RoadClass.Tertiary };
        var remaining = classes.ToDictionary(c => c, Target);
        if (_villages.Count < 2 || remaining.Values.All(v => v < 1.0)) { FillWander(classes, remaining, rng); return; }

        // Prim's minimum spanning tree over village centres, then the remaining pairs by distance
        int n = _villages.Count;
        double D(int a, int b) => Math.Sqrt(Math.Pow(_villages[a].X - _villages[b].X, 2) + Math.Pow(_villages[a].Y - _villages[b].Y, 2));
        var inTree = new bool[n];
        inTree[0] = true;
        var links = new List<(int A, int B)>();
        var used = new HashSet<(int, int)>();
        for (int k = 1; k < n; k++)
        {
            (int A, int B) best = (-1, -1);
            double bd = double.PositiveInfinity;
            for (int a = 0; a < n; a++)
            {
                if (!inTree[a]) continue;
                for (int b = 0; b < n; b++)
                {
                    if (inTree[b]) continue;
                    double d = D(a, b);
                    if (d < bd) { bd = d; best = (a, b); }
                }
            }
            inTree[best.B] = true;
            links.Add(best);
            used.Add((Math.Min(best.A, best.B), Math.Max(best.A, best.B)));
        }
        var extra = new List<(int A, int B, double D)>();
        for (int a = 0; a < n; a++)
            for (int b = a + 1; b < n; b++)
                if (!used.Contains((a, b))) extra.Add((a, b, D(a, b)));
        extra.Sort((l, r) => l.D != r.D ? l.D.CompareTo(r.D) : l.A != r.A ? l.A.CompareTo(r.A) : l.B.CompareTo(r.B));
        links.AddRange(extra.Select(e => (e.A, e.B)));

        foreach (var (a, b) in links)
        {
            var cls = classes.OrderByDescending(c => remaining[c]).First();
            if (remaining[cls] < 1.0) break;
            if (villageNodes[a].Count == 0 || villageNodes[b].Count == 0) continue;
            string na = Nearest(villageNodes[a], _villages[b].X, _villages[b].Y);
            string nb = Nearest(villageNodes[b], _nodeById[na].X, _nodeById[na].Y);
            var pts = LinkPath(_nodeById[na], _nodeById[nb], rng);
            remaining[cls] -= Emit(cls, pts, remaining[cls], na, nb);
        }
        FillWander(classes, remaining, rng);
    }

    private void FillWander(RoadClass[] classes, Dictionary<RoadClass, double> remaining, Rng rng)
    {
        foreach (var c in classes)
        {
            double phase = rng.NextDouble();
            for (int i = 0; i < 400 && remaining[c] >= 1.0; i++)
                remaining[c] -= Emit(c, WanderPath(c, i, rng, phase), remaining[c]);
        }
    }

    private string Nearest(List<string> ids, double x, double y)
    {
        string best = ids[0];
        double bd = double.PositiveInfinity;
        foreach (var id in ids)
        {
            var n = _nodeById[id];
            double d = (n.X - x) * (n.X - x) + (n.Y - y) * (n.Y - y);
            if (d < bd) { bd = d; best = id; }
        }
        return best;
    }

    private static List<(double X, double Y)> LinkPath(RoadNode a, RoadNode b, Rng rng)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        int pieces = Math.Max(1, (int)Math.Round(len / 200.0));
        double amp = rng.Range(-0.08, 0.08) * len;
        double nx = len > 0 ? -dy / len : 0, ny = len > 0 ? dx / len : 0;
        var pts = new List<(double, double)>();
        for (int k = 0; k <= pieces; k++)
        {
            double t = (double)k / pieces;
            double off = Math.Sin(Math.PI * t) * amp;
            pts.Add((a.X + dx * t + nx * off, a.Y + dy * t + ny * off));
        }
        // the end points are the existing nodes exactly
        pts[0] = (a.X, a.Y);
        pts[^1] = (b.X, b.Y);
        return pts;
    }
}
