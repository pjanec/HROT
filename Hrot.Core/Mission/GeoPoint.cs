namespace Hrot.Core.Mission;

/// <summary>Geographic position in geodetic coordinates.</summary>
public struct GeoPoint
{
    /// <summary>Latitude in degrees.</summary>
    public double Latitude;

    /// <summary>Longitude in degrees.</summary>
    public double Longitude;

    /// <summary>Altitude in meters above reference ellipsoid.</summary>
    public double Altitude;

    public GeoPoint(double lat, double lon, double alt = 0)
    {
        Latitude  = lat;
        Longitude = lon;
        Altitude  = alt;
    }
}
