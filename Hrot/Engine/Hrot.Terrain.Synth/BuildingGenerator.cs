using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Hrot.Terrain.InputSet;

namespace Hrot.Tools.TerrainSynth;

/// <summary>Switches for the realism options applied on top of the clean set.</summary>
public sealed record BuildingOptions
{
    /// <summary>Jitter the doors and windows like a real input set (position, height, a few flipped headings, a few loose doors).</summary>
    public bool Noise { get; init; }

    /// <summary>Turn about 10 % of the houses into rows of 3 to 6 that share party walls.</summary>
    public bool Terraces { get; init; }

    /// <summary>Write a material (brick, concrete, stone, wood) on about 30 % of the buildings.</summary>
    public bool Materials { get; init; }

    /// <summary>Points where nothing may be built (water); null for none.</summary>
    public Func<double, double, bool>? Blocked { get; init; }
}

/// <summary>
/// Places unique buildings along the residential streets of the villages: footprint (rect / L / U), storeys,
/// doors and windows on every wall, plus loose door / window points that belong to no footprint.
/// </summary>
public static class BuildingGenerator
{
    public static (IReadOnlyList<Building> Buildings, IReadOnlyList<Opening> Unassigned) Generate(SynthProfile p, Rng rng, HeightField h, RoadNetwork r)
        => Generate(p, rng, h, r, new BuildingOptions());

    // The clean set is generated first from its own stream; every option is applied afterwards from a stream of its own,
    // so switching an option off leaves the clean geometry untouched.
    public static (IReadOnlyList<Building> Buildings, IReadOnlyList<Opening> Unassigned) Generate(SynthProfile p, Rng rng, HeightField h, RoadNetwork r, BuildingOptions options)
    {
        var g = new Generator(p, rng.Fork("buildings"), h, r, options.Blocked);
        g.Run();
        if (options.Terraces) g.Terraces(rng.Fork("terraces"));
        IReadOnlyList<Building> buildings = g.Buildings;
        if (options.Noise) buildings = OpeningNoise.Apply(buildings, rng.Fork("noise"));
        if (options.Materials) buildings = AssignMaterials(buildings, rng.Fork("materials"));
        return (buildings, g.Unassigned);
    }

    // about 30 % of the buildings get a material: brick 50 %, concrete 25 %, stone 15 %, wood 10 % of those
    private static IReadOnlyList<Building> AssignMaterials(IReadOnlyList<Building> buildings, Rng rng)
    {
        var result = new List<Building>(buildings.Count);
        foreach (var b in buildings)
        {
            double has = rng.NextDouble(), pick = rng.NextDouble();
            string? material = null;
            if (has < 0.30)
                material = pick < 0.50 ? "brick" : pick < 0.75 ? "concrete" : pick < 0.90 ? "stone" : "wood";
            result.Add(b with { Material = material });
        }
        return result;
    }

    private readonly record struct Pt(double X, double Y);

    private sealed class Wall
    {
        public Pt A, B;
        public double Len, Dx, Dy, Heading;
        public bool Shared;
    }

    private sealed class Placed
    {
        public List<Pt> Ring = new();
        public double MinX, MinY, MaxX, MaxY;
        public bool Dead;
    }

    // how a house was laid out, kept for the terrace pass
    private sealed class Meta
    {
        public bool Rect, Row;
        public double Ox, Oy, Tx, Ty, Nx, Ny, Depth;
    }

    private sealed record RowPreset(int Storeys, double Ftf, bool Pitched);

    private sealed class StreetEdge
    {
        public Pt A, B;
        public double Len, Half;
    }

    private sealed class Generator
    {
        private const double CellSize = 40.0;

        private readonly SynthProfile _p;
        private Rng _rng;
        private readonly HeightField _h;
        private readonly RoadNetwork _r;
        private readonly List<Building> _buildings = new();
        private readonly List<Opening> _unassigned = new();
        private readonly List<Placed> _placed = new();
        private readonly List<Meta> _meta = new();
        private readonly List<int> _unassignedOwner = new();
        private readonly Dictionary<long, List<int>> _cells = new();
        private readonly double[] _storeyShares;
        private readonly double _sigma;

        public IReadOnlyList<Building> Buildings => _buildings;
        public IReadOnlyList<Opening> Unassigned => _unassigned;

        private readonly Func<double, double, bool>? _blocked;

        public Generator(SynthProfile p, Rng rng, HeightField h, RoadNetwork r, Func<double, double, bool>? blocked)
        {
            _p = p; _rng = rng; _h = h; _r = r; _blocked = blocked;
            double sum = p.StoreyShares.Sum();
            _storeyShares = p.StoreyShares.Select(s => s / sum).ToArray();
            _sigma = (Math.Log(p.FootprintAreaP90) - Math.Log(p.FootprintAreaP10)) / (2 * 1.2815515655446004);
        }

        private static double R2(double v) => Math.Round(v, 2);

        private double Gauss()
        {
            double u1 = 1.0 - _rng.NextDouble(), u2 = _rng.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }

        private int Binomial(int trials, double p)
        {
            int k = 0;
            for (int i = 0; i < trials; i++) if (_rng.NextDouble() < p) k++;
            return k;
        }

        // ---- placement ----

        public void Run()
        {
            var villages = _r.Villages;
            var nodes = _r.Nodes.ToDictionary(n => n.Id);
            var perVillage = villages.Select(_ => new List<StreetEdge>()).ToList();
            foreach (var e in _r.Edges)
            {
                if (e.Class != RoadClass.Residential) continue;
                var a = nodes[e.From]; var b = nodes[e.To];
                double len = Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
                if (len < 12) continue;
                double mx = (a.X + b.X) / 2, my = (a.Y + b.Y) / 2;
                int best = -1;
                double bd = double.PositiveInfinity;
                for (int i = 0; i < villages.Count; i++)
                {
                    double d = Math.Sqrt((villages[i].X - mx) * (villages[i].X - mx) + (villages[i].Y - my) * (villages[i].Y - my));
                    if (d < bd) { bd = d; best = i; }
                }
                if (best < 0 || bd > villages[best].Radius * 1.1) continue;
                perVillage[best].Add(new StreetEdge { A = new Pt(a.X, a.Y), B = new Pt(b.X, b.Y), Len = len, Half = e.WidthM / 2 });
            }

            var weights = perVillage.Select(l => l.Sum(e => e.Len)).ToArray();
            double totalWeight = weights.Sum();
            if (totalWeight <= 0) return;
            var cumulative = new double[weights.Length];
            double acc = 0;
            for (int i = 0; i < weights.Length; i++) { acc += weights[i]; cumulative[i] = acc; }
            var edgeCum = perVillage.Select(l =>
            {
                var c = new double[l.Count];
                double a2 = 0;
                for (int i = 0; i < l.Count; i++) { a2 += l[i].Len; c[i] = a2; }
                return c;
            }).ToList();

            int want = _p.BuildingCount;
            int attempts = 0, maxAttempts = Math.Max(2000, 80 * want);
            while (_buildings.Count < want && attempts++ < maxAttempts)
            {
                // villages are drawn by street length; a village that is full just fails its attempts and the rest spill over
                int vi = Pick(cumulative, _rng.Range(0, totalWeight));
                var edges = perVillage[vi];
                int ei = Pick(edgeCum[vi], _rng.Range(0, weights[vi]));
                TryPlace(edges[ei]);
            }
        }

        private static int Pick(double[] cumulative, double v)
        {
            int lo = 0, hi = cumulative.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (cumulative[mid] > v) hi = mid; else lo = mid + 1;
            }
            return lo;
        }

        private void TryPlace(StreetEdge e)
        {
            double tx = (e.B.X - e.A.X) / e.Len, ty = (e.B.Y - e.A.Y) / e.Len;
            double side = _rng.NextDouble() < 0.5 ? 1 : -1;
            double nx = -ty * side, ny = tx * side;
            double s = _rng.Range(0, e.Len);
            double setback = _rng.Range(4, 8);
            double ox = e.A.X + tx * s + nx * (e.Half + setback);
            double oy = e.A.Y + ty * s + ny * (e.Half + setback);

            var local = Shape(out _);
            var ring = new List<Pt>();
            foreach (var (lx, ly) in local)
                ring.Add(new Pt(R2(ox + lx * tx + ly * nx), R2(oy + lx * ty + ly * ny)));
            if (SignedArea(ring) < 0) ring.Reverse();

            // front facade: the wall lying on the street side (local y = 0)
            var front = new Pt(R2(ox + local[0].X * tx + local[0].Y * nx), R2(oy + local[0].X * ty + local[0].Y * ny));
            var front2 = new Pt(R2(ox + local[1].X * tx + local[1].Y * nx), R2(oy + local[1].X * ty + local[1].Y * ny));

            if (!Admissible(ring)) return;
            if (BlockedRing(ring)) return;
            var walls = MakeWalls(ring);
            int frontIdx = walls.FindIndex(w => (w.A == front && w.B == front2) || (w.A == front2 && w.B == front));
            if (frontIdx < 0) return;
            var fw = walls[frontIdx];
            var door = new Pt(R2((fw.A.X + fw.B.X) / 2), R2((fw.A.Y + fw.B.Y) / 2));
            if (_h.IsNoData(door.X, door.Y)) return;
            float ground = _h.Sample(door.X, door.Y);
            if (float.IsNaN(ground)) return;
            if (OverlapsPlaced(ring)) return;
            if (TooCloseToRoad(walls)) return;

            var meta = new Meta { Rect = local.Count == 4, Ox = ox, Oy = oy, Tx = tx, Ty = ty, Nx = nx, Ny = ny, Depth = local.Max(q => q.Y) };
            Accept(ring, walls, frontIdx, door, R2(ground), meta);
        }

        // local-frame outline: x along the street, y away from it; vertex 0 -> 1 is the front facade. CCW in the local frame.
        private List<(double X, double Y)> Shape(out string kind)
        {
            double area = Math.Clamp(Math.Exp(Math.Log(_p.FootprintAreaP50) + _sigma * Gauss()), 30, 900);
            double aspect = _rng.Range(0.8, 1.8);
            double u = _rng.NextDouble();
            bool mirror = _rng.NextDouble() < 0.5;
            double a = _rng.Range(0.3, 0.5), b = _rng.Range(0.3, 0.5);
            double uw = _rng.Range(0.3, 0.45), ud = _rng.Range(0.4, 0.6);

            kind = "rect";
            double fraction = 1.0;
            if (u >= _p.RectShare)
            {
                if (_rng.NextDouble() < 0.5) { kind = "L"; fraction = 1 - a * b; }
                else { kind = "U"; fraction = 1 - uw * ud; }
            }
            double bbox = area / fraction;
            double w = Math.Clamp(Math.Sqrt(bbox * aspect), 6, 35);
            double d = Math.Clamp(bbox / w, 5, 22);

            if (kind == "L")
            {
                double nw = a * w, nd = b * d;
                if (nw >= 2.5 && nd >= 2.5 && w - nw >= 2.5 && d - nd >= 2.5)
                    return Mirror(new List<(double, double)> { (-w / 2, 0), (w / 2, 0), (w / 2, d - nd), (w / 2 - nw, d - nd), (w / 2 - nw, d), (-w / 2, d) }, mirror);
            }
            else if (kind == "U")
            {
                double nw = uw * w, nd = ud * d, arm = (w - nw) / 2;
                if (nw >= 2.5 && nd >= 2.5 && arm >= 2.5 && d - nd >= 2.5)
                    return Mirror(new List<(double, double)> { (-w / 2, 0), (w / 2, 0), (w / 2, d), (w / 2 - arm, d), (w / 2 - arm, d - nd), (-w / 2 + arm, d - nd), (-w / 2 + arm, d), (-w / 2, d) }, mirror);
            }
            kind = "rect";
            return new List<(double, double)> { (-w / 2, 0), (w / 2, 0), (w / 2, d), (-w / 2, d) };
        }

        // a mirrored outline must keep vertex 0 -> 1 as the front wall and stay CCW, so it is reversed after the flip
        private static List<(double X, double Y)> Mirror(List<(double X, double Y)> v, bool mirror)
        {
            if (!mirror) return v;
            var m = v.Select(q => (-q.X, q.Y)).ToList();
            // flipping x reverses the winding; reverse the order and start again at the front wall (old vertex 1 -> 0)
            m.Reverse();
            // after reversing, the front wall is the pair (n-2, n-1); rotate so it is vertex 0 -> 1
            int n = m.Count;
            var rot = new List<(double X, double Y)>();
            for (int i = 0; i < n; i++) rot.Add(m[(i + n - 2) % n]);
            return rot;
        }

        private static double SignedArea(List<Pt> v)
        {
            double a = 0;
            for (int i = 0; i < v.Count; i++)
            {
                var p = v[i]; var q = v[(i + 1) % v.Count];
                a += p.X * q.Y - q.X * p.Y;
            }
            return a / 2;
        }

        private bool Admissible(List<Pt> ring)
        {
            double minX = ring.Min(q => q.X), maxX = ring.Max(q => q.X), minY = ring.Min(q => q.Y), maxY = ring.Max(q => q.Y);
            if (minX < _p.MinX + 1 || maxX > _p.MaxX - 1 || minY < _p.MinY + 1 || maxY > _p.MaxY - 1) return false;
            foreach (var rect in _p.NodataRects)
                if (maxX >= rect.MinX - 1 && minX <= rect.MaxX + 1 && maxY >= rect.MinY - 1 && minY <= rect.MaxY + 1) return false;
            foreach (var q in ring)
                if (_h.IsNoData(q.X, q.Y)) return false;
            return true;
        }

        // a corner or a point along a wall (every 2 m) on or near water
        private bool BlockedRing(List<Pt> ring)
        {
            if (_blocked == null) return false;
            for (int i = 0; i < ring.Count; i++)
            {
                var a = ring[i]; var b = ring[(i + 1) % ring.Count];
                double len = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
                for (double s = 0; s <= len; s += 2.0)
                    if (_blocked(a.X + (b.X - a.X) * s / len, a.Y + (b.Y - a.Y) * s / len)) return true;
                if (_blocked(b.X, b.Y)) return true;
            }
            return false;
        }

        private static List<Wall> MakeWalls(List<Pt> ring)
        {
            var walls = new List<Wall>();
            for (int i = 0; i < ring.Count; i++)
            {
                var a = ring[i]; var b = ring[(i + 1) % ring.Count];
                double dx = b.X - a.X, dy = b.Y - a.Y, len = Math.Sqrt(dx * dx + dy * dy);
                dx /= len; dy /= len;
                // outward normal of a counter-clockwise ring is to the right of the walking direction
                double nx = dy, ny = -dx;
                double heading = Math.Atan2(nx, ny) * 180.0 / Math.PI;
                if (heading < 0) heading += 360.0;
                heading = Math.Round(heading, 1);
                if (heading >= 360.0) heading = 0.0;
                walls.Add(new Wall { A = a, B = b, Len = len, Dx = dx, Dy = dy, Heading = heading });
            }
            return walls;
        }

        private bool TooCloseToRoad(List<Wall> walls)
        {
            foreach (var w in walls)
            {
                if (_r.DistanceToRoad((w.A.X + w.B.X) / 2, (w.A.Y + w.B.Y) / 2) < 3.0) return true;
                for (double s = 0; s < w.Len; s += 2.0)
                    if (_r.DistanceToRoad(w.A.X + w.Dx * s, w.A.Y + w.Dy * s) < 3.0) return true;
            }
            return false;
        }

        // ---- overlap with earlier buildings (2 m gap) ----

        private static long Key(int x, int y) => ((long)x << 32) ^ (uint)y;

        private IEnumerable<int> Near(double minX, double minY, double maxX, double maxY)
        {
            int x0 = (int)Math.Floor((minX - 2.5) / CellSize), x1 = (int)Math.Floor((maxX + 2.5) / CellSize);
            int y0 = (int)Math.Floor((minY - 2.5) / CellSize), y1 = (int)Math.Floor((maxY + 2.5) / CellSize);
            var seen = new HashSet<int>();
            for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                    if (_cells.TryGetValue(Key(x, y), out var l))
                        foreach (int i in l) if (seen.Add(i)) yield return i;
        }

        private bool OverlapsPlaced(List<Pt> ring)
        {
            double minX = ring.Min(q => q.X), maxX = ring.Max(q => q.X), minY = ring.Min(q => q.Y), maxY = ring.Max(q => q.Y);
            foreach (int i in Near(minX, minY, maxX, maxY))
            {
                var o = _placed[i];
                if (o.Dead) continue;
                if (o.MaxX + 2.0 < minX || o.MinX - 2.0 > maxX || o.MaxY + 2.0 < minY || o.MinY - 2.0 > maxY) continue;
                if (PolygonGap(ring, o.Ring) < 2.0) return true;
            }
            return false;
        }

        private static double PointSeg(Pt p, Pt a, Pt b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y, l2 = dx * dx + dy * dy;
            double t = l2 <= 0 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / l2, 0, 1);
            double qx = a.X + t * dx - p.X, qy = a.Y + t * dy - p.Y;
            return Math.Sqrt(qx * qx + qy * qy);
        }

        private static bool Cross(Pt a, Pt b, Pt c, Pt d)
        {
            static double O(Pt p, Pt q, Pt s) => (q.X - p.X) * (s.Y - p.Y) - (q.Y - p.Y) * (s.X - p.X);
            return O(a, b, c) * O(a, b, d) < 0 && O(c, d, a) * O(c, d, b) < 0;
        }

        private static bool Inside(List<Pt> v, Pt p)
        {
            bool inside = false;
            for (int i = 0, j = v.Count - 1; i < v.Count; j = i++)
                if ((v[i].Y > p.Y) != (v[j].Y > p.Y) && p.X < (v[j].X - v[i].X) * (p.Y - v[i].Y) / (v[j].Y - v[i].Y) + v[i].X)
                    inside = !inside;
            return inside;
        }

        // distance between two polygons; 0 when they cross or one contains the other
        private static double PolygonGap(List<Pt> a, List<Pt> b)
        {
            double best = double.PositiveInfinity;
            for (int i = 0; i < a.Count; i++)
            {
                var a0 = a[i]; var a1 = a[(i + 1) % a.Count];
                for (int j = 0; j < b.Count; j++)
                {
                    var b0 = b[j]; var b1 = b[(j + 1) % b.Count];
                    if (Cross(a0, a1, b0, b1)) return 0;
                    best = Math.Min(best, Math.Min(Math.Min(PointSeg(a0, b0, b1), PointSeg(a1, b0, b1)), Math.Min(PointSeg(b0, a0, a1), PointSeg(b1, a0, a1))));
                }
            }
            if (Inside(a, b[0]) || Inside(b, a[0])) return 0;
            return best;
        }

        // ---- building details ----

        private (int Storeys, double Ftf) DrawFloors()
        {
            double u = _rng.NextDouble(), c = 0;
            int storeys = _storeyShares.Length;
            for (int i = 0; i < _storeyShares.Length; i++)
            {
                c += _storeyShares[i];
                if (u < c) { storeys = i + 1; break; }
            }
            double t = _rng.NextDouble();
            double ftf = Math.Round(Math.Clamp(_p.FloorToFloorMin + (_p.FloorToFloorMax - _p.FloorToFloorMin) * Math.Pow(t, 1.51), _p.FloorToFloorMin, _p.FloorToFloorMax), 2);
            return (storeys, ftf);
        }

        private void Accept(List<Pt> ring, List<Wall> walls, int frontIdx, Pt door, double baseZ, Meta meta, RowPreset? preset = null)
        {
            var (storeys, ftf) = preset != null ? (preset.Storeys, preset.Ftf) : DrawFloors();
            var storeyZ = new List<double>();
            for (int i = 0; i < storeys; i++) storeyZ.Add(R2(baseZ + i * ftf));
            double height = R2(storeys * ftf);

            bool pitched = preset?.Pitched ?? (_rng.NextDouble() < _p.PitchedShare);
            double? ridge = pitched ? R2(baseZ + height + _rng.Range(3, 5)) : null;

            // doors: (wall, storey, distance along the wall)
            var doors = new List<(int Wall, int Storey, double Along)> { (frontIdx, 0, walls[frontIdx].Len / 2) };
            int extra = Binomial(3, Math.Clamp((_p.DoorsPerHouse - 1) / 3.0, 0, 1));
            for (int k = 0; k < extra; k++)
            {
                for (int attempt = 0; attempt < 6; attempt++)
                {
                    int wi = _rng.Int(0, walls.Count);
                    if (walls[wi].Len < 3.0 || walls[wi].Shared) continue;
                    int storey = storeys > 1 && _rng.NextDouble() > 0.85 ? _rng.Int(1, storeys) : 0;
                    double along = _rng.Range(1.0, walls[wi].Len - 1.0);
                    if (doors.Any(d => d.Wall == wi && d.Storey == storey && Math.Abs(d.Along - along) < 1.5)) continue;
                    doors.Add((wi, storey, along));
                    break;
                }
            }

            var openings = new List<Opening>();
            foreach (var d in doors)
            {
                var w = walls[d.Wall];
                openings.Add(new Opening("door", R2(w.A.X + w.Dx * d.Along), R2(w.A.Y + w.Dy * d.Along), storeyZ[d.Storey], w.Heading, d.Storey));
            }
            for (int s = 0; s < storeys; s++)
                for (int wi = 0; wi < walls.Count; wi++)
                {
                    var w = walls[wi];
                    if (w.Shared) continue;
                    for (double along = 1.0; along <= w.Len - 1.0; along += _rng.Range(2.5, 3.0))
                    {
                        double a = along;
                        if (doors.Any(d => d.Wall == wi && d.Storey == s && Math.Abs(d.Along - a) < 1.0)) continue;
                        openings.Add(new Opening("window", R2(w.A.X + w.Dx * along), R2(w.A.Y + w.Dy * along), R2(storeyZ[s] + 1.0), w.Heading, s));
                    }
                }

            // loose openings next to this house
            int loose = Binomial(2, Math.Clamp(_p.UnassignedPerHouse / 2.0, 0, 1));
            for (int k = 0; k < loose; k++)
            {
                int wi = _rng.Int(0, walls.Count);
                var w = walls[wi];
                if (w.Len < 3.0 || w.Shared) continue;
                bool isDoor = _rng.NextDouble() < 0.3;
                int storey = isDoor ? 0 : _rng.Int(0, storeys);
                double along = _rng.Range(1.0, w.Len - 1.0);
                double off = _rng.Range(0.5, 2.0);
                double nx = w.Dy, ny = -w.Dx;
                double x = R2(w.A.X + w.Dx * along + nx * off), y = R2(w.A.Y + w.Dy * along + ny * off);
                if (x < _p.MinX || x > _p.MaxX || y < _p.MinY || y > _p.MaxY) continue;
                if (_blocked != null && _blocked(x, y)) continue;
                _unassigned.Add(new Opening(isDoor ? "door" : "window", x, y, R2(storeyZ[storey] + (isDoor ? 0.0 : 1.0)), w.Heading, storey));
                _unassignedOwner.Add(_buildings.Count);
            }

            string id = MakeId(ring);
            var pl = new Placed { Ring = ring, MinX = ring.Min(q => q.X), MaxX = ring.Max(q => q.X), MinY = ring.Min(q => q.Y), MaxY = ring.Max(q => q.Y) };
            int index = _placed.Count;
            _placed.Add(pl);
            int x0 = (int)Math.Floor(pl.MinX / CellSize), x1 = (int)Math.Floor(pl.MaxX / CellSize);
            int y0 = (int)Math.Floor(pl.MinY / CellSize), y1 = (int)Math.Floor(pl.MaxY / CellSize);
            for (int cx = x0; cx <= x1; cx++)
                for (int cy = y0; cy <= y1; cy++)
                {
                    long k = Key(cx, cy);
                    if (!_cells.TryGetValue(k, out var l)) _cells[k] = l = new List<int>();
                    l.Add(index);
                }

            var footprint = ring.Select(q => (q.X, q.Y)).ToList();
            footprint.Add(footprint[0]);
            _meta.Add(meta);
            _buildings.Add(new Building(id, footprint, baseZ, height, pitched ? "pitched" : "flat", ridge, storeyZ, openings));
        }

        // ---- terraced rows ----

        private void Shuffle<T>(List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _rng.Int(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        // Turns about a tenth of the houses into rows of 3 to 6 that share party walls, then trims plain houses so the total stays.
        public void Terraces(Rng rng)
        {
            _rng = rng;
            int target = _p.BuildingCount;
            double wantRowHouses = 0.10 * target;
            var candidates = Enumerable.Range(0, _buildings.Count).Where(i => _meta[i].Rect).ToList();
            Shuffle(candidates);

            var dead = new HashSet<int>();
            int rowHouses = 0;
            foreach (int ci in candidates)
            {
                if (rowHouses >= wantRowHouses) break;
                rowHouses += TryRow(ci, dead);
            }

            int active = _buildings.Count - dead.Count;
            if (active > target)
            {
                var plain = Enumerable.Range(0, _buildings.Count).Where(i => !dead.Contains(i) && !_meta[i].Row).ToList();
                Shuffle(plain);
                foreach (int i in plain)
                {
                    if (active <= target) break;
                    dead.Add(i);
                    active--;
                }
            }
            Compact(dead);
        }

        private void Compact(HashSet<int> dead)
        {
            var keptBuildings = new List<Building>();
            var remap = new Dictionary<int, int>();
            for (int i = 0; i < _buildings.Count; i++)
            {
                if (dead.Contains(i)) continue;
                remap[i] = keptBuildings.Count;
                keptBuildings.Add(_buildings[i]);
            }
            var keptUnassigned = new List<Opening>();
            var keptOwner = new List<int>();
            for (int i = 0; i < _unassigned.Count; i++)
            {
                if (!remap.TryGetValue(_unassignedOwner[i], out int owner)) continue;
                keptUnassigned.Add(_unassigned[i]);
                keptOwner.Add(owner);
            }
            _buildings.Clear(); _buildings.AddRange(keptBuildings);
            _unassigned.Clear(); _unassigned.AddRange(keptUnassigned);
            _unassignedOwner.Clear(); _unassignedOwner.AddRange(keptOwner);
        }

        // Replaces house ci by a row of units on the same street, front line and depth. Returns the number of units (0 = not possible).
        private int TryRow(int ci, HashSet<int> dead)
        {
            var m = _meta[ci];
            int k = _rng.Int(3, 7);
            var xs = new double[k + 1];
            var widths = new double[k];
            double total = 0;
            for (int i = 0; i < k; i++) { widths[i] = R2(_rng.Range(5.5, 8.5)); total += widths[i]; }
            xs[0] = -total / 2;
            for (int i = 0; i < k; i++) xs[i + 1] = xs[i] + widths[i];

            Pt W(double lx, double ly) => new Pt(R2(m.Ox + lx * m.Tx + ly * m.Nx), R2(m.Oy + lx * m.Ty + ly * m.Ny));
            var bottom = new Pt[k + 1];
            var top = new Pt[k + 1];
            for (int i = 0; i <= k; i++) { bottom[i] = W(xs[i], 0); top[i] = W(xs[i], m.Depth); }

            // the original house is replaced, so it must not count as an obstacle
            _placed[ci].Dead = true;
            var units = new List<(List<Pt> Ring, List<Wall> Walls, int Front, Pt Door, double BaseZ)>();
            bool ok = true;
            for (int i = 0; i < k && ok; i++)
            {
                var ring = new List<Pt> { bottom[i], bottom[i + 1], top[i + 1], top[i] };
                if (SignedArea(ring) < 0) ring.Reverse();
                if (!Admissible(ring) || BlockedRing(ring)) { ok = false; break; }
                var walls = MakeWalls(ring);
                int front = walls.FindIndex(w => (w.A == bottom[i] && w.B == bottom[i + 1]) || (w.A == bottom[i + 1] && w.B == bottom[i]));
                if (front < 0) { ok = false; break; }
                var fw = walls[front];
                var door = new Pt(R2((fw.A.X + fw.B.X) / 2), R2((fw.A.Y + fw.B.Y) / 2));
                if (_h.IsNoData(door.X, door.Y)) { ok = false; break; }
                float ground = _h.Sample(door.X, door.Y);
                if (float.IsNaN(ground)) { ok = false; break; }
                if (OverlapsPlaced(ring) || TooCloseToRoad(walls)) { ok = false; break; }
                foreach (var w in walls)
                {
                    bool left = i > 0 && SameEdge(w, bottom[i], top[i]);
                    bool right = i < k - 1 && SameEdge(w, bottom[i + 1], top[i + 1]);
                    w.Shared = left || right;
                }
                units.Add((ring, walls, front, door, R2(ground)));
            }
            if (!ok)
            {
                _placed[ci].Dead = false;
                return 0;
            }

            var (storeys, ftf) = DrawFloors();
            bool pitched = _rng.NextDouble() < _p.PitchedShare;
            var preset = new RowPreset(storeys, ftf, pitched);
            dead.Add(ci);
            foreach (var u in units)
                Accept(u.Ring, u.Walls, u.Front, u.Door, u.BaseZ, new Meta { Row = true, Ox = m.Ox, Oy = m.Oy, Tx = m.Tx, Ty = m.Ty, Nx = m.Nx, Ny = m.Ny, Depth = m.Depth }, preset);
            return k;
        }

        private static bool SameEdge(Wall w, Pt a, Pt b) => (w.A == a && w.B == b) || (w.A == b && w.B == a);

        private static string MakeId(List<Pt> ring)
        {
            var sb = new StringBuilder();
            foreach (var q in ring)
                sb.Append(Math.Round(q.X, 1).ToString("F1", CultureInfo.InvariantCulture)).Append(',')
                  .Append(Math.Round(q.Y, 1).ToString("F1", CultureInfo.InvariantCulture)).Append(';');
            byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
            return Convert.ToHexString(hash).ToLowerInvariant().Substring(0, 12);
        }
    }
}
