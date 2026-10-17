namespace Hrot.Traffic;

/// <summary>
/// ⭐ AQ93 E1 (R-4025) — UTM ⇄ WGS84 on the WGS84 ellipsoid (Snyder, "Map Projections — A Working Manual", USGS PP 1395,
/// §8 transverse Mercator), accurate to well under a millimetre inside a zone. The only projection a SUMO net may carry
/// into HROT (<c>+proj=utm</c>); anything else is refused by <see cref="SumoNetPlacement"/>.
/// </summary>
public static class Utm
{
    private const double A = 6378137.0;
    private const double F = 1.0 / 298.257223563;
    private const double K0 = 0.9996;
    private static readonly double E2 = F * (2 - F);
    private static readonly double Ep2 = E2 / (1 - E2);

    /// <summary>The central meridian of a UTM <paramref name="zone"/>, in degrees.</summary>
    public static double CentralMeridianDeg(int zone) => zone * 6.0 - 183.0;

    /// <summary>UTM easting/northing (metres) in <paramref name="zone"/> → latitude/longitude (degrees).</summary>
    public static (double LatDeg, double LonDeg) ToGeodetic(double easting, double northing, int zone, bool south)
    {
        double x = easting - 500000.0;
        double y = south ? northing - 10000000.0 : northing;
        double m = y / K0;
        double mu = m / (A * (1 - E2 / 4 - 3 * E2 * E2 / 64 - 5 * E2 * E2 * E2 / 256));
        double e1 = (1 - Math.Sqrt(1 - E2)) / (1 + Math.Sqrt(1 - E2));
        double phi1 = mu
            + (3 * e1 / 2 - 27 * Math.Pow(e1, 3) / 32) * Math.Sin(2 * mu)
            + (21 * e1 * e1 / 16 - 55 * Math.Pow(e1, 4) / 32) * Math.Sin(4 * mu)
            + (151 * Math.Pow(e1, 3) / 96) * Math.Sin(6 * mu)
            + (1097 * Math.Pow(e1, 4) / 512) * Math.Sin(8 * mu);
        double sin = Math.Sin(phi1), cos = Math.Cos(phi1), tan = Math.Tan(phi1);
        double n1 = A / Math.Sqrt(1 - E2 * sin * sin);
        double t1 = tan * tan;
        double c1 = Ep2 * cos * cos;
        double r1 = A * (1 - E2) / Math.Pow(1 - E2 * sin * sin, 1.5);
        double d = x / (n1 * K0);
        double lat = phi1 - n1 * tan / r1 * (d * d / 2
            - (5 + 3 * t1 + 10 * c1 - 4 * c1 * c1 - 9 * Ep2) * Math.Pow(d, 4) / 24
            + (61 + 90 * t1 + 298 * c1 + 45 * t1 * t1 - 252 * Ep2 - 3 * c1 * c1) * Math.Pow(d, 6) / 720);
        double lon = (d - (1 + 2 * t1 + c1) * Math.Pow(d, 3) / 6
            + (5 - 2 * c1 + 28 * t1 - 3 * c1 * c1 + 8 * Ep2 + 24 * t1 * t1) * Math.Pow(d, 5) / 120) / cos;
        return (lat * 180.0 / Math.PI, CentralMeridianDeg(zone) + lon * 180.0 / Math.PI);
    }

    /// <summary>Latitude/longitude (degrees) → UTM easting/northing (metres) in <paramref name="zone"/> — for rails.</summary>
    public static (double Easting, double Northing) FromGeodetic(double latDeg, double lonDeg, int zone, bool south)
    {
        double phi = latDeg * Math.PI / 180.0;
        double lam = (lonDeg - CentralMeridianDeg(zone)) * Math.PI / 180.0;
        double sin = Math.Sin(phi), cos = Math.Cos(phi), tan = Math.Tan(phi);
        double n = A / Math.Sqrt(1 - E2 * sin * sin);
        double t = tan * tan;
        double c = Ep2 * cos * cos;
        double a = cos * lam;
        double m = A * ((1 - E2 / 4 - 3 * E2 * E2 / 64 - 5 * E2 * E2 * E2 / 256) * phi
            - (3 * E2 / 8 + 3 * E2 * E2 / 32 + 45 * E2 * E2 * E2 / 1024) * Math.Sin(2 * phi)
            + (15 * E2 * E2 / 256 + 45 * E2 * E2 * E2 / 1024) * Math.Sin(4 * phi)
            - (35 * E2 * E2 * E2 / 3072) * Math.Sin(6 * phi));
        double easting = K0 * n * (a + (1 - t + c) * Math.Pow(a, 3) / 6
            + (5 - 18 * t + t * t + 72 * c - 58 * Ep2) * Math.Pow(a, 5) / 120) + 500000.0;
        double northing = K0 * (m + n * tan * (a * a / 2 + (5 - t + 9 * c + 4 * c * c) * Math.Pow(a, 4) / 24
            + (61 - 58 * t + t * t + 600 * c - 330 * Ep2) * Math.Pow(a, 6) / 720));
        return (easting, south ? northing + 10000000.0 : northing);
    }

    /// <summary>
    /// Reads <c>+proj=utm +zone=N [+south]</c> from a PROJ string. <c>false</c> for any other projection (or none).
    /// </summary>
    public static bool TryParseProj(string proj, out int zone, out bool south)
    {
        zone = 0; south = false;
        if (string.IsNullOrWhiteSpace(proj) || !proj.Contains("+proj=utm", StringComparison.Ordinal)) return false;
        foreach (var token in proj.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.StartsWith("+zone=", StringComparison.Ordinal) && int.TryParse(token.AsSpan(6), out var z)) zone = z;
            else if (token == "+south") south = true;
        }
        return zone is >= 1 and <= 60;
    }
}
