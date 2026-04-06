using FDP.Toolkit.Vis2D;
using Hrot.Map.Common.Config;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.Models;

namespace Hrot.Editor.Adapters
{
    /// <summary>
    /// Implements <see cref="IMapConfigController"/> for the offline editor.
    ///
    /// <para>
    /// Reads and writes the injected <see cref="MapViewConfig"/> POCO and the
    /// <see cref="MapCanvas.ActiveLayerMask"/> directly for entity-layer visibility,
    /// avoiding any dependency on <c>Hrot.IG</c> or DDS layer-config topics.
    /// </para>
    ///
    /// No DDS or CycloneDDS references.
    /// </summary>
    public sealed class EditorMapConfigAdapter : IMapConfigController
    {
        private readonly MapViewConfig _config;
        private readonly MapCanvas?    _canvas;

        /// <param name="config">
        /// The mutable in-process map layer configuration shared between this adapter
        /// and the rendering pipeline.
        /// </param>
        /// <param name="canvas">
        /// Optional canvas whose <see cref="MapCanvas.ActiveLayerMask"/> is read and
        /// written when applying entity-layer visibility changes.
        /// May be <c>null</c> in headless mode (layer mask changes are ignored).
        /// </param>
        public EditorMapConfigAdapter(MapViewConfig config, MapCanvas? canvas = null)
        {
            _config = config;
            _canvas = canvas;
        }

        /// <inheritdoc/>
        public MapLayerState GetCurrentConfig()
        {
            uint mask = _canvas?.ActiveLayerMask ?? 0xFFFFFFFF;
            return new MapLayerState(
                Satellite:        _config.ShowSatelliteLayer,
                GroundUnits:      (mask & MapLayerBits.GroundUnitsBit)      != 0,
                AirUnits:         (mask & MapLayerBits.AirUnitsBit)         != 0,
                Vehicles:         (mask & MapLayerBits.VehiclesBit)         != 0,
                TacticalGraphics: (mask & MapLayerBits.TacticalGraphicsBit) != 0,
                RoadGraphs:       (mask & MapLayerBits.RoadGraphsBit)       != 0,
                Grid:             _config.ShowGrid);
        }

        /// <inheritdoc/>
        public void ApplyConfig(MapLayerState config)
        {
            _config.ShowSatelliteLayer = config.Satellite;
            _config.ShowGrid           = config.Grid;

            if (_canvas != null)
            {
                uint mask = _canvas.ActiveLayerMask;
                mask = config.GroundUnits      ? mask |  MapLayerBits.GroundUnitsBit      : mask & ~MapLayerBits.GroundUnitsBit;
                mask = config.AirUnits         ? mask |  MapLayerBits.AirUnitsBit         : mask & ~MapLayerBits.AirUnitsBit;
                mask = config.Vehicles         ? mask |  MapLayerBits.VehiclesBit         : mask & ~MapLayerBits.VehiclesBit;
                mask = config.TacticalGraphics ? mask |  MapLayerBits.TacticalGraphicsBit : mask & ~MapLayerBits.TacticalGraphicsBit;
                mask = config.RoadGraphs       ? mask |  MapLayerBits.RoadGraphsBit       : mask & ~MapLayerBits.RoadGraphsBit;
                _canvas.ActiveLayerMask = mask;
            }
        }
    }
}
