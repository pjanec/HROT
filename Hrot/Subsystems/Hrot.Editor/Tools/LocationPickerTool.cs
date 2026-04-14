using System;
using System.Numerics;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Abstractions;
using Hrot.Core.Mission;
using Raylib_cs;

namespace Hrot.Editor.Tools
{
    /// <summary>
    /// Single-click tool that fires <see cref="OnLocationPicked"/> with the world-space
    /// click position (expressed as a <see cref="GeoPoint"/> with X→Longitude, Y→Latitude)
    /// and pops itself immediately.  Supports cancellation via ESC or right-click.
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
                var geo = new GeoPoint { Latitude = worldPos.Y, Longitude = worldPos.X, Altitude = 0.0 };
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
