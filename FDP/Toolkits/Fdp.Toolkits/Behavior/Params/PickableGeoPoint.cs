using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fdp.Toolkit.Behavior.Params;

/// <summary>
/// Minimal geographic coordinate used by behavior-parameter DTOs to represent a
/// single map-pickable world location.
///
/// <para>Kept intentionally lightweight (two doubles) so that <c>Fdp.Toolkits</c>
/// remains independent of higher-level layers such as <c>Hrot.Core</c>.  The
/// presentation layer converts between this struct and <c>Hrot.Core.Mission.GeoPoint</c>
/// when consuming async pick results from <see cref="Hrot.UI.Common.Facades.IMapPickService"/>.</para>
/// </summary>
[JsonConverter(typeof(PickableGeoPointArrayConverter))]
public struct PickableGeoPoint
{
    /// <summary>Latitude in degrees.</summary>
    public double Latitude;

    /// <summary>Longitude in degrees.</summary>
    public double Longitude;

    /// <summary>Initialises a <see cref="PickableGeoPoint"/> with the given coordinates.</summary>
    public PickableGeoPoint(double lat, double lon)
    {
        Latitude  = lat;
        Longitude = lon;
    }
}


/// <summary>
/// Serializes/deserializes <see cref="PickableGeoPoint"/> as a compact single-line JSON array
/// <c>[latitude, longitude]</c>.
/// </summary>
internal sealed class PickableGeoPointArrayConverter : JsonConverter<PickableGeoPoint>
{
    public override PickableGeoPoint Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        reader.Read(); double latitude = reader.GetDouble();
        reader.Read(); double longitude = reader.GetDouble();
        reader.Read(); // EndArray
        return new PickableGeoPoint(latitude, longitude);
    }

    public override void Write(Utf8JsonWriter writer, PickableGeoPoint value, JsonSerializerOptions options)
    {
        string latitude = value.Latitude.ToString("G17", CultureInfo.InvariantCulture);
        string longitude = value.Longitude.ToString("G17", CultureInfo.InvariantCulture);
        writer.WriteRawValue($"[{latitude}, {longitude}]");
    }
}
