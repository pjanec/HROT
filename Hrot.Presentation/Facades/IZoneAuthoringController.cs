namespace Hrot.UI.Common.Facades;

/// <summary>
/// Port interface for zone authoring operations.
/// Provides commands for configuring road networks and obstacle placement within named zones.
/// </summary>
public interface IZoneAuthoringController
{
    /// <summary>
    /// Sets the road network asset path for the specified zone.
    /// </summary>
    /// <param name="activeZoneName">The name of the zone to configure.</param>
    /// <param name="assetPath">The file path to the road network asset (e.g. a JSON graph file).</param>
    void SetRoadNetworkPath(string activeZoneName, string assetPath);

    /// <summary>
    /// Activates obstacle placement mode for the specified zone, allowing the operator to
    /// click the map to place circular obstacles of the given radius.
    /// </summary>
    /// <param name="activeZoneName">The name of the zone in which obstacles are placed.</param>
    /// <param name="radius">The radius of the obstacle in metres.</param>
    void StartObstaclePlacementMode(string activeZoneName, float radius);
}
