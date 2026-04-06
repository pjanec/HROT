namespace Hrot.Map.Common.Config;

/// <summary>
/// Bit-mask constants for the five standard entity-render layers.
/// These values must match <c>Hrot.IG.Systems.MapLayerRegistry</c> exactly so that
/// the offline Editor and the live IG use the same bitmask encoding in
/// <c>MapCanvas.ActiveLayerMask</c> and <c>MapDisplayComponent.LayerMask</c>.
/// </summary>
public static class MapLayerBits
{
    /// <summary>Rendering bit for the <c>"units_ground"</c> layer.</summary>
    public const uint GroundUnitsBit = 1u << 0;

    /// <summary>Rendering bit for the <c>"units_air"</c> layer.</summary>
    public const uint AirUnitsBit = 1u << 1;

    /// <summary>Rendering bit for the <c>"vehicles"</c> layer (motorised platforms).</summary>
    public const uint VehiclesBit = 1u << 2;

    /// <summary>Rendering bit for the <c>"tactical_graphics"</c> layer (area overlays).</summary>
    public const uint TacticalGraphicsBit = 1u << 3;

    /// <summary>Rendering bit for the <c>"road_graphs"</c> layer (route entities).</summary>
    public const uint RoadGraphsBit = 1u << 4;
}
