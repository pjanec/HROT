using Hrot.Terrain.InputSet;

namespace Hrot.Terrain.Osm;

/// <summary>
/// ⭐ §5.3 / E-7: the 1 m height grid from the DEM — local metres → UTM → lat / lon → <see cref="IDemSource.HeightAt"/> (bicubic inside the
/// source) — with F-2 road levelling ACROSS the width (only ways at <c>layer</c> 0 without <c>bridge</c> / <c>tunnel</c>, blended over 3 m each
/// side). ⛔ F-1: nothing is flattened under buildings. <see cref="GroundAt"/> is the ONE ground function: the grid, the buildings' baseZ and the
/// trees' z all read it, so they agree.
/// </summary>
public sealed class HeightBuilder
{
    public const double Blend = 3.0, LatticeStep = 50.0;
    private readonly IDemSource _dem;
    private readonly AreaBounds _b;
    private readonly SegmentIndex _roads = new(16);
    private readonly List<double> _halfWidth = new();
    private readonly double _maxReach;
    private readonly double[,] _lat, _lon;
    private readonly int _lx, _ly;

    public HeightBuilder(IDemSource dem, AreaBounds bounds, OsmModel model)
    {
        _dem = dem; _b = bounds;
        // exact lat / lon on a coarse lattice; per post it is interpolated (the mapping is smooth — sub-millimetre error over 50 m)
        _lx = (int)Math.Ceiling(bounds.Width / LatticeStep) + 2;
        _ly = (int)Math.Ceiling(bounds.Height / LatticeStep) + 2;
        _lat = new double[_lx, _ly]; _lon = new double[_lx, _ly];
        for (int i = 0; i < _lx; i++)
            for (int j = 0; j < _ly; j++)
                (_lat[i, j], _lon[i, j]) = AreaFrame.ToGeo(bounds.Frame, bounds.MinX + i * LatticeStep, bounds.MinY + j * LatticeStep);
        foreach (var line in new GeometryAssembler(model).Lines(IsLevelled))
        {
            double half = HalfWidth(line.Tags);
            _maxReach = Math.Max(_maxReach, half + Blend);
            for (int i = 0; i + 1 < line.Points.Count; i++)
            {
                _halfWidth.Add(half);
                _roads.Add(line.Points[i].x, line.Points[i].y, line.Points[i + 1].x, line.Points[i + 1].y, _halfWidth.Count - 1);
            }
        }
    }

    public int LevelledSegments => _roads.Count;

    private static readonly HashSet<string> Levelled = new()
    {
        "motorway", "trunk", "primary", "secondary", "tertiary", "unclassified", "residential", "service", "living_street", "road",
        "motorway_link", "trunk_link", "primary_link", "secondary_link", "tertiary_link",
    };

    /// <summary>F-2: a road is levelled only on the ground — layer 0, no bridge, no tunnel.</summary>
    public static bool IsLevelled(IReadOnlyDictionary<string, string> t)
    {
        if (!t.TryGetValue("highway", out var h) || !Levelled.Contains(h)) return false;
        if (t.TryGetValue("bridge", out var br) && br != "no") return false;
        if (t.TryGetValue("tunnel", out var tu) && tu != "no") return false;
        if (t.TryGetValue("covered", out var cv) && cv == "yes") return false;
        return !(BuildingRules.ParseNumber(OsmTags.Get(t, "layer")) is double l && l != 0);
    }

    private static double HalfWidth(IReadOnlyDictionary<string, string> t)
    {
        if (BuildingRules.ParseNumber(OsmTags.Get(t, "width")) is double w && w >= 1) return w / 2;
        return t["highway"] switch
        {
            "motorway" or "trunk" => 7.0,
            "motorway_link" or "trunk_link" or "primary" => 4.5,
            "secondary" or "primary_link" or "secondary_link" => 4.0,
            "tertiary" or "tertiary_link" or "unclassified" => 3.5,
            "residential" or "living_street" or "road" => 3.0,
            _ => 2.0,
        };
    }

    /// <summary>The DEM alone at a local point.</summary>
    public double DemAt(double x, double y)
    {
        double gx = Math.Clamp((x - _b.MinX) / LatticeStep, 0, _lx - 1.000001), gy = Math.Clamp((y - _b.MinY) / LatticeStep, 0, _ly - 1.000001);
        int i = (int)gx, j = (int)gy;
        double fx = gx - i, fy = gy - j;
        double lat = Bil(_lat, i, j, fx, fy), lon = Bil(_lon, i, j, fx, fy);
        return _dem.HeightAt(lat, lon);
    }

    private static double Bil(double[,] a, int i, int j, double fx, double fy) =>
        a[i, j] * (1 - fx) * (1 - fy) + a[i + 1, j] * fx * (1 - fy) + a[i, j + 1] * (1 - fx) * fy + a[i + 1, j + 1] * fx * fy;

    /// <summary>The ground the terrain stands on at a local point: the DEM, levelled across a nearby ground-level road.</summary>
    public double GroundAt(double x, double y)
    {
        double g = DemAt(x, y);
        if (_roads.Count == 0) return g;
        var hit = _roads.Nearest(x, y, _maxReach);
        if (hit is not SegmentIndex.Hit h) return g;
        double half = _halfWidth[h.Tag];
        if (h.Distance > half + Blend) return g;
        double w = h.Distance <= half ? 1.0 : 1.0 - Smooth((h.Distance - half) / Blend);
        return g + w * (DemAt(h.X, h.Y) - g);
    }

    private static double Smooth(double t) { t = Math.Clamp(t, 0, 1); return t * t * (3 - 2 * t); }

    /// <summary>Streams <c>heights/</c> — posts every metre over the bounds, north row first, with the TRUE source resolution (F-9).</summary>
    public void Write(string heightsDir, IProgress<double>? progress, CancellationToken cancellation)
    {
        int cols = (int)Math.Round(_b.Width) + 1, rows = (int)Math.Round(_b.Height) + 1;
        var info = new HeightsInfo(_b.MinX, _b.MinY, 1.0, cols, rows, _b.Frame) { SourceResolution = _dem.ResolutionMetres };
        var writer = new HeightsWriter(heightsDir, info);
        try
        {
            var row = new float[cols];
            for (int r = 0; r < rows; r++)
            {
                cancellation.ThrowIfCancellationRequested();
                double y = _b.MinY + (rows - 1 - r);
                for (int c = 0; c < cols; c++) row[c] = (float)GroundAt(_b.MinX + c, y);
                writer.WriteRow(row);
                if ((r & 63) == 0) progress?.Report((double)r / rows);
            }
        }
        catch
        {
            try { writer.Dispose(); } catch (InvalidOperationException) { /* "n of N rows written" — the original exception matters */ }
            throw;
        }
        writer.Dispose();
    }
}
