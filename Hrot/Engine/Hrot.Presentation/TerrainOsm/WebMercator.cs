using System;
using Hrot.Terrain.Osm;

namespace Hrot.UI.Common.TerrainOsm;

/// <summary>
/// ⭐ CE-4375 (OSM §6b) — the web-Mercator maths of the wizard's own map: lat/lon ↔ WORLD pixels at a zoom (256 px tiles, the XYZ scheme OSM serves),
/// and the ground size of a box. Pure — the map widget and its rails share it. ⛔ No XYZ / Mercator code existed in the repo (INVENTORY, §6b).
/// </summary>
public static class WebMercator
{
    public const int TileSize = 256;
    public const double MaxLatitude = 85.05112878;
    private const double EarthRadiusM = 6378137.0;

    /// <summary>The world-pixel position of <paramref name="lat"/>, <paramref name="lon"/> at <paramref name="zoom"/> (fractional zooms scale continuously).</summary>
    public static (double X, double Y) ToPixel(double lat, double lon, double zoom)
    {
        lat = Math.Clamp(lat, -MaxLatitude, MaxLatitude);
        double scale = TileSize * Math.Pow(2, zoom);
        double x = (lon + 180.0) / 360.0 * scale;
        double s = Math.Sin(lat * Math.PI / 180.0);
        double y = (0.5 - Math.Log((1 + s) / (1 - s)) / (4 * Math.PI)) * scale;
        return (x, y);
    }

    /// <summary>The lat/lon of a world-pixel position at <paramref name="zoom"/>.</summary>
    public static (double Lat, double Lon) ToGeo(double x, double y, double zoom)
    {
        double scale = TileSize * Math.Pow(2, zoom);
        double lon = x / scale * 360.0 - 180.0;
        double n = Math.PI - 2.0 * Math.PI * y / scale;
        double lat = 180.0 / Math.PI * Math.Atan(Math.Sinh(n));
        return (lat, lon);
    }

    /// <summary>The ground width and height (m) of <paramref name="box"/> — east-west at its middle latitude, north-south along a meridian.</summary>
    public static (double WidthM, double HeightM) SizeOf(GeoBox box)
    {
        double midLat = (box.South + box.North) * 0.5 * Math.PI / 180.0;
        double w = Math.Abs(box.East - box.West) * Math.PI / 180.0 * EarthRadiusM * Math.Cos(midLat);
        double h = Math.Abs(box.North - box.South) * Math.PI / 180.0 * EarthRadiusM;
        return (w, h);
    }

    /// <summary>A box centred on <paramref name="lat"/>, <paramref name="lon"/> of the given ground size (m).</summary>
    public static GeoBox BoxAround(double lat, double lon, double widthM, double heightM)
    {
        double dLat = heightM / EarthRadiusM * 180.0 / Math.PI * 0.5;
        double dLon = widthM / (EarthRadiusM * Math.Cos(lat * Math.PI / 180.0)) * 180.0 / Math.PI * 0.5;
        return new GeoBox(lat - dLat, lon - dLon, lat + dLat, lon + dLon);
    }

    /// <summary>The UTM zone of a longitude (no Norway / Svalbard exceptions — the box is ≤ 6 km and the label is informative).</summary>
    public static int UtmZone(double lon) => Math.Clamp((int)Math.Floor((lon + 180.0) / 6.0) + 1, 1, 60);

    /// <summary>"4.8 × 3.5 km" — the size chip's text.</summary>
    public static string SizeText(double widthM, double heightM)
        => (widthM / 1000.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " × "
         + (heightM / 1000.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " km";
}
