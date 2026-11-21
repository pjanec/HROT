using Hrot.Terrain.InputSet;
using System.Globalization;
using System.Text;

namespace Hrot.Tools.TerrainSynth;

/// <summary>Options of the extras mode.</summary>
public sealed record TreeOptions
{
    /// <summary>Points where nothing may stand (water); null for none.</summary>
    public Func<double, double, bool>? Blocked { get; init; }

    /// <summary>Return only the trees outside the woods and describe the woods as polygons.</summary>
    public bool WoodsAsPolygons { get; init; }
}

public sealed record TreeResult(IReadOnlyList<(double X, double Y, double Z)> Trees, IReadOnlyList<AreaFeature> Woods, WoodsMask? Mask);

/// <summary>
/// Woods (a jittered grid inside a low-frequency noise mask) plus lone trees: rows along minor roads and scattered field trees.
/// </summary>
public static class TreeGenerator
{
    public static IReadOnlyList<(double X, double Y, double Z)> Generate(SynthProfile p, Rng rng, HeightField h, RoadNetwork r, IReadOnlyList<Building> b)
        => new Generator(p, rng, h, r, b, new TreeOptions()).Run().Trees;

    /// <summary>Like Generate, with the optional extras: water exclusion and the woods as polygons instead of trees.</summary>
    public static TreeResult GenerateDetailed(SynthProfile p, Rng rng, HeightField h, RoadNetwork r, IReadOnlyList<Building> b, TreeOptions options)
        => new Generator(p, rng, h, r, b, options).Run();

    public static void WriteCsv(string path, IReadOnlyList<(double X, double Y, double Z)> t) => TreesCsv.Write(path, t);

    private sealed class Generator
    {
        private const double MaskWavelength = 900.0;
        private const double LatticeStep = 25.0;
        private const double HashCell = 20.0;

        private readonly SynthProfile _p;
        private readonly Rng _rng;
        private readonly HeightField _h;
        private readonly RoadNetwork _r;
        private readonly IReadOnlyList<Building> _b;
        private readonly Dictionary<long, List<int>> _footprintCells = new();
        private readonly TreeOptions _o;
        private readonly Func<double, double, bool>? _blocked;
        private ulong _maskSeed;
        private double _threshold;

        public Generator(SynthProfile p, Rng rng, HeightField h, RoadNetwork r, IReadOnlyList<Building> b, TreeOptions o)
        {
            _p = p; _rng = rng; _h = h; _r = r; _b = b; _o = o; _blocked = o.Blocked;
        }

        private static double R2(double v) => Math.Round(v, 2);

        private static long Key(int x, int y) => ((long)x << 32) ^ (uint)y;

        public TreeResult Run()
        {
            IndexFootprints();
            _maskSeed = _rng.Fork("woods").NextU64();
            double woodsHa = CalibrateMask();

            // woods: density from the target count, corrected once for the trees the rejections remove
            double woodTarget = _p.TreeCount * (1.0 - _p.LoneShare);
            double density = Math.Clamp(woodTarget / Math.Max(woodsHa, 1e-6), 45.0, 400.0);
            var woods = WoodsPass(density);
            if (woods.Count > 0 && Math.Abs(woods.Count / woodTarget - 1.0) > 0.02)
            {
                density = Math.Clamp(density * woodTarget / woods.Count, 45.0, 400.0);
                woods = WoodsPass(density);
            }

            var loneTrees = new List<(double X, double Y, double Z)>();
            int lone = (int)Math.Round(woods.Count * _p.LoneShare / (1.0 - _p.LoneShare));
            int rows = AddRows(loneTrees, (int)(0.6 * lone));
            AddScattered(loneTrees, lone - rows);

            if (!_o.WoodsAsPolygons)
            {
                var all = new List<(double X, double Y, double Z)>(woods);
                all.AddRange(loneTrees);
                return new TreeResult(all, Array.Empty<AreaFeature>(), null);
            }

            // the woods become polygons; the lone trees stay points and must lie outside every polygon
            var mask = BuildRaster();
            var polygons = WoodsPolygons.Trace(mask, 2.5, 40);
            var features = polygons.Select(r => new AreaFeature(AreaRings.Close(r), null, Math.Round(density, 1))).ToList();
            var boxes = polygons.Select(r => (MinX: r.Min(v => v.X), MaxX: r.Max(v => v.X), MinY: r.Min(v => v.Y), MaxY: r.Max(v => v.Y))).ToList();
            var outside = new List<(double X, double Y, double Z)>();
            foreach (var t in loneTrees)
            {
                bool inside = false;
                for (int i = 0; i < polygons.Count && !inside; i++)
                    inside = t.X >= boxes[i].MinX && t.X <= boxes[i].MaxX && t.Y >= boxes[i].MinY && t.Y <= boxes[i].MaxY && Geo.Inside(polygons[i], t.X, t.Y);
                if (!inside) outside.Add(t);
            }
            return new TreeResult(outside, features, mask);
        }

        // 5 m cells that are woods, minus a safety strip around buildings and water
        private WoodsMask BuildRaster()
        {
            const double cell = 5.0;
            int cols = (int)Math.Ceiling(_p.Width / cell), rows = (int)Math.Ceiling(_p.Height / cell);
            var cells = new bool[cols, rows];
            for (int j = 0; j < rows; j++)
                for (int i = 0; i < cols; i++)
                    cells[i, j] = InWoods(_p.MinX + (i + 0.5) * cell, _p.MinY + (j + 0.5) * cell);
            foreach (var b in _b)
            {
                double minX = b.Footprint.Min(q => q.X) - 6, maxX = b.Footprint.Max(q => q.X) + 6, minY = b.Footprint.Min(q => q.Y) - 6, maxY = b.Footprint.Max(q => q.Y) + 6;
                int i0 = Math.Max(0, (int)Math.Floor((minX - _p.MinX) / cell)), i1 = Math.Min(cols - 1, (int)Math.Floor((maxX - _p.MinX) / cell));
                int j0 = Math.Max(0, (int)Math.Floor((minY - _p.MinY) / cell)), j1 = Math.Min(rows - 1, (int)Math.Floor((maxY - _p.MinY) / cell));
                for (int i = i0; i <= i1; i++)
                    for (int j = j0; j <= j1; j++)
                        cells[i, j] = false;
            }
            return new WoodsMask(cells, cell, _p.MinX, _p.MinY);
        }

        // ---- woods mask ----

        private double Mask(double x, double y) => Noise.Fbm(_maskSeed, x, y, MaskWavelength, 3);

        private bool InVillage(double x, double y)
        {
            foreach (var v in _r.Villages)
            {
                double d = Math.Sqrt((v.X - x) * (v.X - x) + (v.Y - y) * (v.Y - y));
                if (d < v.Radius + 50.0) return true;
            }
            return false;
        }

        private bool WoodsAllowed(double x, double y) => !InVillage(x, y) && !_h.IsNoData(x, y) && !(_blocked != null && _blocked(x, y));

        private bool InWoods(double x, double y) => WoodsAllowed(x, y) && Mask(x, y) >= _threshold;

        // Picks the threshold so that the woods cover the wanted share of the whole area (sampled on a 25 m lattice).
        // Returns the woods area in hectares.
        private double CalibrateMask()
        {
            int cols = (int)Math.Ceiling(_p.Width / LatticeStep), rows = (int)Math.Ceiling(_p.Height / LatticeStep);
            var values = new List<double>();
            for (int j = 0; j < rows; j++)
                for (int i = 0; i < cols; i++)
                {
                    double x = _p.MinX + (i + 0.5) * LatticeStep, y = _p.MinY + (j + 0.5) * LatticeStep;
                    if (WoodsAllowed(x, y)) values.Add(Mask(x, y));
                }
            int wanted = (int)Math.Floor(_p.WoodsShare * cols * rows);
            if (values.Count <= wanted || wanted <= 0)
            {
                _threshold = double.NegativeInfinity;
                return values.Count * LatticeStep * LatticeStep / 10000.0;
            }
            values.Sort();
            _threshold = values[values.Count - wanted];
            return wanted * LatticeStep * LatticeStep / 10000.0;
        }

        private List<(double X, double Y, double Z)> WoodsPass(double perHa)
        {
            var rng = _rng.Fork("woods.grid");
            double cell = 100.0 / Math.Sqrt(perHa);
            int cols = (int)Math.Ceiling(_p.Width / cell), rows = (int)Math.Ceiling(_p.Height / cell);
            var list = new List<(double, double, double)>();
            for (int j = 0; j < rows; j++)
                for (int i = 0; i < cols; i++)
                {
                    // two draws per cell whatever happens, so the sequence does not depend on the mask
                    double jx = rng.Range(-0.45, 0.45), jy = rng.Range(-0.45, 0.45);
                    double x = R2(_p.MinX + (i + 0.5 + jx) * cell), y = R2(_p.MinY + (j + 0.5 + jy) * cell);
                    if (!InWoods(x, y)) continue;
                    if (Ground(x, y) is double z) list.Add((x, y, z));
                }
            return list;
        }

        // ---- lone trees ----

        private int AddRows(List<(double X, double Y, double Z)> all, int target)
        {
            if (target <= 0) return 0;
            var rng = _rng.Fork("lone.rows");
            var nodes = _r.Nodes.ToDictionary(n => n.Id);
            var edges = _r.Edges.Where(e => e.Class == RoadClass.Tertiary || e.Class == RoadClass.Unclassified).ToList();
            for (int i = edges.Count - 1; i > 0; i--)
            {
                int j = rng.Int(0, i + 1);
                (edges[i], edges[j]) = (edges[j], edges[i]);
            }
            int added = 0;
            foreach (var e in edges)
            {
                if (added >= target) break;
                var a = nodes[e.From]; var b = nodes[e.To];
                double len = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
                if (len < 10) continue;
                double tx = (b.X - a.X) / len, ty = (b.Y - a.Y) / len;
                double side = rng.NextDouble() < 0.5 ? 1 : -1;
                double off = e.WidthM / 2 + 4.0;
                for (double s = rng.Range(0, 10); s < len && added < target; s += rng.Range(10, 15))
                {
                    double x = R2(a.X + tx * s - ty * side * off), y = R2(a.Y + ty * s + tx * side * off);
                    if (Ground(x, y) is double z)
                    {
                        all.Add((x, y, z));
                        added++;
                    }
                }
            }
            return added;
        }

        private void AddScattered(List<(double X, double Y, double Z)> all, int count)
        {
            var rng = _rng.Fork("lone.field");
            int added = 0;
            for (int attempt = 0; attempt < 100 * Math.Max(count, 1) && added < count; attempt++)
            {
                double x = R2(rng.Range(_p.MinX, _p.MaxX)), y = R2(rng.Range(_p.MinY, _p.MaxY));
                if (InVillage(x, y) || InWoods(x, y)) continue;
                if (Ground(x, y) is double z)
                {
                    all.Add((x, y, z));
                    added++;
                }
            }
        }

        // ---- rejection ----

        // ground height when a tree may stand here, otherwise null
        private double? Ground(double x, double y)
        {
            if (x < _p.MinX || x > _p.MaxX || y < _p.MinY || y > _p.MaxY) return null;
            if (_h.IsNoData(x, y)) return null;
            if (_r.DistanceToRoad(x, y) < 2.0) return null;
            if (NearFootprint(x, y)) return null;
            if (_blocked != null && _blocked(x, y)) return null;
            float z = _h.Sample(x, y);
            if (float.IsNaN(z)) return null;
            return R2(z);
        }

        private void IndexFootprints()
        {
            for (int i = 0; i < _b.Count; i++)
            {
                var f = _b[i].Footprint;
                double minX = f.Min(q => q.X) - 2.5, maxX = f.Max(q => q.X) + 2.5, minY = f.Min(q => q.Y) - 2.5, maxY = f.Max(q => q.Y) + 2.5;
                for (int cx = (int)Math.Floor(minX / HashCell); cx <= (int)Math.Floor(maxX / HashCell); cx++)
                    for (int cy = (int)Math.Floor(minY / HashCell); cy <= (int)Math.Floor(maxY / HashCell); cy++)
                    {
                        long k = Key(cx, cy);
                        if (!_footprintCells.TryGetValue(k, out var l)) _footprintCells[k] = l = new List<int>();
                        l.Add(i);
                    }
            }
        }

        // inside a footprint or within 2 m of its outline
        private bool NearFootprint(double x, double y)
        {
            if (!_footprintCells.TryGetValue(Key((int)Math.Floor(x / HashCell), (int)Math.Floor(y / HashCell)), out var list)) return false;
            foreach (int i in list)
            {
                var f = _b[i].Footprint;
                bool inside = false;
                for (int a = 0, c = f.Count - 1; a < f.Count; c = a++)
                {
                    if ((f[a].Y > y) != (f[c].Y > y) && x < (f[c].X - f[a].X) * (y - f[a].Y) / (f[c].Y - f[a].Y) + f[a].X) inside = !inside;
                    if (SegDistance(x, y, f[a], f[c]) < 2.0) return true;
                }
                if (inside) return true;
            }
            return false;
        }

        private static double SegDistance(double px, double py, (double X, double Y) a, (double X, double Y) b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y, l2 = dx * dx + dy * dy;
            double t = l2 <= 0 ? 0 : Math.Clamp(((px - a.X) * dx + (py - a.Y) * dy) / l2, 0, 1);
            double qx = a.X + t * dx - px, qy = a.Y + t * dy - py;
            return Math.Sqrt(qx * qx + qy * qy);
        }
    }
}
