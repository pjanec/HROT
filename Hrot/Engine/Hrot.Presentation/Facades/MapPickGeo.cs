using Hrot.Core.Mission;

namespace Hrot.UI.Common.Facades;

/// <summary>
/// ⭐ <c>CE-4073</c> — the ONE conversion of a picked LOCAL position into the geodetic <see cref="GeoPoint"/> a location pick returns.
///
/// <para>A mission's location field is geodetic by authoring convention (<c>PickableGeoPoint</c>, the mission DTOs), so a pick is one of the
/// few places a local ENU position must become latitude/longitude — through the host's <see cref="Fdp.Modules.Geographic.IGeographicTransform"/>.
/// ⭐ Shared by <see cref="CanvasMapPickAdapter"/> (a host that owns the canvas) and <c>ExConLogic</c> (the console, whose click arrives as local
/// ENU metres from the map host) — ⛔ never two copies of the conversion.</para>
///
/// <para>⚠ With no transform (a test, a host with no geography) the local X/Y is returned in the Latitude/Longitude fields, Altitude 0 —
/// the adapter's long-standing fallback. ⛔ Only meaningful with a transform; stated so nobody reads it as a conversion.</para>
/// </summary>
public static class MapPickGeo
{
    /// <summary>The local position (east <paramref name="x"/>, north <paramref name="y"/>, up <paramref name="z"/> metres) as a geodetic point.</summary>
    public static GeoPoint ToGeoPoint(Fdp.Modules.Geographic.IGeographicTransform? geo, double x, double y, double z = 0.0)
    {
        if (geo is null) return new GeoPoint(x, y, 0);
        var (lat, lon, alt) = geo.ToGeodetic(new System.Numerics.Vector3((float)x, (float)y, (float)z));
        return new GeoPoint(lat, lon, alt);
    }
}
