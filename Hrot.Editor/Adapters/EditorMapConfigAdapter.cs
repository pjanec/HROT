using Hrot.Map.Common.Config;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.Models;

namespace Hrot.Editor.Adapters
{
    /// <summary>
    /// Implements <see cref="IMapConfigController"/> for the offline editor.
    ///
    /// <para>
    /// Reads and writes the injected <see cref="MapViewConfig"/> POCO directly,
    /// avoiding any dependency on <c>Hrot.IG</c> or DDS layer-config topics.
    /// </para>
    ///
    /// No DDS or CycloneDDS references.
    /// </summary>
    public sealed class EditorMapConfigAdapter : IMapConfigController
    {
        private readonly MapViewConfig _config;

        /// <param name="config">
        /// The mutable in-process map layer configuration shared between this adapter
        /// and the rendering pipeline.
        /// </param>
        public EditorMapConfigAdapter(MapViewConfig config)
        {
            _config = config;
        }

        /// <inheritdoc/>
        public MapLayerState GetCurrentConfig()
        {
            return new MapLayerState(
                Satellite:   _config.ShowSatelliteLayer,
                GroundUnits: _config.ShowGroundUnits,
                AirUnits:    _config.ShowAirUnits,
                Grid:        _config.ShowGrid);
        }

        /// <inheritdoc/>
        public void ApplyConfig(MapLayerState config)
        {
            _config.ShowSatelliteLayer = config.Satellite;
            _config.ShowGroundUnits    = config.GroundUnits;
            _config.ShowAirUnits       = config.AirUnits;
            _config.ShowGrid           = config.Grid;
        }
    }
}
