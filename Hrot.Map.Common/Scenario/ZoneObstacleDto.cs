namespace Hrot.Map.Common.Scenario;

/// <summary>
/// Describes a single zone obstacle: a cylinder used for LOS blocking and collision.
/// Coordinates are in the zone's local flat-earth frame (metres).
/// </summary>
public sealed class ZoneObstacleDto
{
    /// <summary>X coordinate of the obstacle centre (metres).</summary>
    public float X { get; set; }

    /// <summary>Y coordinate of the obstacle centre (metres).</summary>
    public float Y { get; set; }

    /// <summary>Cylinder radius (metres).</summary>
    public float Radius { get; set; }
}
