using System;
using System.Numerics;
using Fdp.Modules.Geographic;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Abstractions;
using Hrot.Core.Mission;
using Raylib_cs;

namespace Hrot.Editor.Tools
{
    /// <summary>
    /// Single-click tool that fires <see cref="OnLocationPicked"/> with the world-space
    /// click position converted to WGS-84 geodetic coordinates via
    /// <see cref="IGeographicTransform"/> and pops itself immediately.
    /// Supports cancellation via ESC or right-click.
    /// </summary>
    public sealed class LocationPickerTool : IMapTool
    {
        /// <inheritdoc/>
        public string Name => "LocationPicker";

        /// <summary>Fired when the operator left-clicks; provides the picked geographic position.</summary>
        public Action<GeoPoint>? OnLocationPicked;

        /// <summary>Fired when the operator cancels picking (ESC or right-click).</summary>
        public Action? OnCancelled;

        private MapCanvas? _canvas;
        private readonly IGeographicTransform _geoTransform;

        /// <param name="geoTransform">Used to convert flat-map Cartesian X/Y to WGS-84 lat/lon.</param>
        public LocationPickerTool(IGeographicTransform geoTransform)
        {
            _geoTransform = geoTransform ?? throw new ArgumentNullException(nameof(geoTransform));
        }

        /// <inheritdoc/>
        public void OnEnter(MapCanvas canvas) => _canvas = canvas;

        /// <inheritdoc/>
        public void OnExit() => _canvas = null;

        /// <inheritdoc/>
        public void Update(float dt) { }

        /// <inheritdoc/>
        public void Draw(RenderContext ctx) { }

        /// <inheritdoc/>
        public bool HandleClick(Vector2 worldPos, MouseButton button)
        {
            if (button == MouseButton.Left)
            {
                // Convert flat-map Cartesian X/Y to WGS-84 geodetic coordinates.
                var (lat, lon, alt) = _geoTransform.ToGeodetic(new Vector3(worldPos.X, worldPos.Y, 0f));
                var geo = new GeoPoint { Latitude = lat, Longitude = lon, Altitude = alt };
                OnLocationPicked?.Invoke(geo);
                _canvas?.PopTool();
                return true;
            }
            if (button == MouseButton.Right)
            {
                OnCancelled?.Invoke();
                _canvas?.PopTool();
                return true;
            }
            return false;
        }

        /// <inheritdoc/>
        public bool HandleDrag(Vector2 worldPos, Vector2 delta) => false;

        /// <inheritdoc/>
        public bool HandleHover(Vector2 worldPos) => false;

        /// <inheritdoc/>
        public bool HandleKeyPressed(KeyboardKey key)
        {
            if (key == KeyboardKey.Escape)
            {
                OnCancelled?.Invoke();
                _canvas?.PopTool();
                return true;
            }
            return false;
        }
    }
}
