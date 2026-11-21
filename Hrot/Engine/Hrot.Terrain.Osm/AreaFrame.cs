using Fdp.Modules.Geographic.Transforms;
using Hrot.Terrain.InputSet;

namespace Hrot.Terrain.Osm;

/// <summary>A terrain's frame and its local bounds (whole metres), from <see cref="AreaFrame.FromBox"/>.</summary>
public sealed record AreaBounds(UtmFrame Frame, double MinX, double MinY, double MaxX, double MaxY)
{
    public double Width => MaxX - MinX;
    public double Height => MaxY - MinY;
    public bool Contains(double x, double y) => x >= MinX && x <= MaxX && y >= MinY && y <= MaxY;
}

/// <summary>
/// ⭐ E-8: the frame of an area — the UTM zone of the box CENTRE, <c>e0 / n0</c> = the centre rounded to 100 m, local bounds = the INNER
/// rectangle of the four projected corners (no slivers outside the data), rounded inwards to whole metres so the 1 m height posts fall on them.
/// </summary>
public static class AreaFrame
{
    public const double MaxSideMetres = 6000;
    private const double MetresPerDegree = 111320.0;

    public static string? Validate(GeoBox b)
    {
        if (!(b.South < b.North)) return "the box is inverted: south must be below north";
        if (!(b.West < b.East)) return "the box is inverted: west must be left of east (an area across the antimeridian is not supported)";
        if (b.South < -80 || b.North > 84) return "UTM covers latitudes -80 … 84 only";
        if (b.West < -180 || b.East > 180) return "longitude must be within -180 … 180";
        return null;
    }

    public static AreaBounds FromBox(GeoBox b)
    {
        if (Validate(b) is string bad) throw new ArgumentException(bad, nameof(b));
        double clat = (b.South + b.North) / 2, clon = (b.West + b.East) / 2;
        int zone = Math.Clamp((int)Math.Floor((clon + 180.0) / 6.0) + 1, 1, 60);
        bool south = clat < 0;
        var (ce, cn) = Utm.FromGeodetic(clat, clon, zone, south);
        double e0 = Math.Round(ce / 100.0) * 100.0, n0 = Math.Round(cn / 100.0) * 100.0;
        var frame = new UtmFrame(zone, south, e0, n0);
        var sw = ToLocal(frame, b.South, b.West);
        var se = ToLocal(frame, b.South, b.East);
        var nw = ToLocal(frame, b.North, b.West);
        var ne = ToLocal(frame, b.North, b.East);
        double minX = Math.Ceiling(Math.Max(sw.X, nw.X)), maxX = Math.Floor(Math.Min(se.X, ne.X));
        double minY = Math.Ceiling(Math.Max(sw.Y, se.Y)), maxY = Math.Floor(Math.Min(nw.Y, ne.Y));
        return new AreaBounds(frame, minX, minY, maxX, maxY);
    }

    public static (double X, double Y) ToLocal(UtmFrame f, double latDeg, double lonDeg)
    {
        var (e, n) = Utm.FromGeodetic(latDeg, lonDeg, f.Zone, f.South);
        return (e - f.E0, n - f.N0);
    }

    public static (double Lat, double Lon) ToGeo(UtmFrame f, double x, double y) => Utm.ToGeodetic(x + f.E0, y + f.N0, f.Zone, f.South);

    /// <summary>The size in metres, or the reason the area is refused — no download needed.</summary>
    public static (double WidthM, double HeightM, string? Refusal) Measure(GeoBox b)
    {
        if (Validate(b) is string bad) return (0, 0, bad);
        var a = FromBox(b);
        if (a.Width > MaxSideMetres + 0.5 || a.Height > MaxSideMetres + 0.5)
            return (a.Width, a.Height, $"the area is {a.Width:0} × {a.Height:0} m — the largest side is {MaxSideMetres:0} m; pick a smaller box");
        if (a.Width < 20 || a.Height < 20) return (a.Width, a.Height, $"the area is {a.Width:0} × {a.Height:0} m — too small to be a terrain");
        return (a.Width, a.Height, null);
    }

    /// <summary>The box grown by <paramref name="metres"/> on every side (the download margin, E-1).</summary>
    public static GeoBox WithMargin(GeoBox b, double metres)
    {
        double dLat = metres / MetresPerDegree;
        double worstLat = Math.Min(Math.Max(Math.Abs(b.South), Math.Abs(b.North)), 84.0);
        double dLon = metres / (MetresPerDegree * Math.Cos(worstLat * Math.PI / 180.0));
        return new GeoBox(Math.Max(-80, b.South - dLat), Math.Max(-180, b.West - dLon), Math.Min(84, b.North + dLat), Math.Min(180, b.East + dLon));
    }
}
