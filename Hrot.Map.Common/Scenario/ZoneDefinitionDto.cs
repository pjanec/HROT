using System.Collections.Generic;

namespace Hrot.Map.Common.Scenario;

/// <summary>
/// Describes a single zone's static environment assets:
/// an optional road-network file and a list of LOS obstacles.
/// </summary>
public sealed class ZoneDefinitionDto
{
    /// <summary>
    /// Relative or absolute path to the road-network binary file.
    /// <see langword="null"/> when the zone has no road network.
    /// </summary>
    public string? RoadNetworkPath { get; set; }

    /// <summary>
    /// Identifier of the terrain database used for ground-clamping.
    /// <see langword="null"/> when the zone uses no terrain database.
    /// </summary>
    public string? TerrainDatabaseId { get; set; }

    /// <summary>
    /// Cylindrical LOS obstacles present in this zone.
    /// <see langword="null"/> when there are no static obstacles.
    /// </summary>
    public List<ZoneObstacleDto>? Obstacles { get; set; }
}
