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
