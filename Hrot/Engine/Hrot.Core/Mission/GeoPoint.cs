using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hrot.Core.Mission;

/// <summary>Geographic position in geodetic coordinates.</summary>
[JsonConverter(typeof(GeoPointArrayConverter))]
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

public sealed class GeoPointArrayConverter : JsonConverter<GeoPoint>
{
    public override GeoPoint Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        reader.Read(); double latitude = reader.GetDouble();
        reader.Read(); double longitude = reader.GetDouble();
        reader.Read(); double altitude = reader.GetDouble();
        reader.Read(); // EndArray
        return new GeoPoint(latitude, longitude, altitude);
    }

    public override void Write(Utf8JsonWriter writer, GeoPoint value, JsonSerializerOptions options)
    {
        string latitude = value.Latitude.ToString("G17", CultureInfo.InvariantCulture);
        string longitude = value.Longitude.ToString("G17", CultureInfo.InvariantCulture);
        string altitude = value.Altitude.ToString("G17", CultureInfo.InvariantCulture);
        writer.WriteRawValue($"[{latitude}, {longitude}, {altitude}]");
    }
}
