using Hrot.UI.Common.Models;

namespace Hrot.UI.Common.Facades;

/// <summary>
/// Port interface for reading and applying map layer configuration.
/// Panels use this interface to read the current layer visibility state
/// and submit layer toggle requests.
/// </summary>
public interface IMapConfigController
{
    /// <summary>Returns the current map layer visibility configuration.</summary>
    MapLayerState GetCurrentConfig();

    /// <summary>
    /// Applies the given layer configuration, updating visibility in the rendering pipeline.
    /// </summary>
    /// <param name="config">The new layer state to apply.</param>
    void ApplyConfig(MapLayerState config);
}
