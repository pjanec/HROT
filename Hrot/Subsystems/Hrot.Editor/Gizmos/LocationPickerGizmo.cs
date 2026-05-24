using System;
using System.Numerics;
using Fdp.Modules.Geographic;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Hrot.Core.Mission;

namespace Hrot.Editor.Gizmos
{
    /// <summary>
    /// Stateful gizmo that fires <see cref="_onLocationPicked"/> with the world-space
    /// click position converted to WGS-84 geodetic coordinates via
    /// <see cref="IGeographicTransform"/> and calls <see cref="_onRemove"/> immediately.
    /// Supports cancellation via ESC or right-click.
    ///
    /// Replaces the deleted <c>LocationPickerTool</c> (Phase 4 of the gizmo migration).
    /// Exercised via <see cref="Fdp.Toolkit.Diagnostics.Gizmos.Systems.GlobalGizmoManager"/> which
    /// forwards canvas events into this gizmo.
    /// </summary>
    public sealed class LocationPickerGizmo : IEntityStatefulGizmo
    {
        private const float CrosshairHalfSize  = 14f;
        private const float CrosshairThickness = 1.5f;
        private const float CrosshairGapRadius = 5f;

        private readonly IGeographicTransform _geoTransform;
        private readonly Action<GeoPoint>     _onLocationPicked;
        private readonly Action               _onRemove;

        private Vector3 _cursorWorld;

        /// <inheritdoc/>
        public bool RequiresExclusiveFocus => true;

        /// <inheritdoc/>
        public bool WantsRawInput => true; 

        /// <inheritdoc/>
        public bool IsFocused { get; private set; }

        /// <inheritdoc/>
        public void SetFocus(bool isFocused) => IsFocused = isFocused;

        /// <param name="geoTransform">Used to convert flat-map Cartesian X/Y to WGS-84 lat/lon.</param>
        /// <param name="onLocationPicked">Callback fired with the picked geographic position on left-click.</param>
        /// <param name="onRemove">
        /// Callback invoked when the gizmo wants to exit. Typically calls
        /// <c>GlobalGizmoManager.Unregister</c> to remove the gizmo from the manager.
        /// </param>
        public LocationPickerGizmo(
            IGeographicTransform geoTransform,
            Action<GeoPoint>     onLocationPicked,
            Action?              onRemove = null)
        {
            _geoTransform     = geoTransform     ?? throw new ArgumentNullException(nameof(geoTransform));
            _onLocationPicked = onLocationPicked ?? throw new ArgumentNullException(nameof(onLocationPicked));
            _onRemove         = onRemove ?? (() => { });
        }

        // IEntityStatefulGizmo -- draw

        /// <inheritdoc/>
        /// <remarks>Draws a sky-blue crosshair at the current cursor world position.</remarks>
        public void UpdateAndDraw(ISimulationView view, float deltaTime, IDebugDrawBuilder draw)
        {
            var drawColor = new Rgba32(102, 191, 255, 255);
            var pos = _cursorWorld;

            draw.DrawLine(new Vector3(pos.X - CrosshairHalfSize, pos.Y, 0f), new Vector3(pos.X - CrosshairGapRadius, pos.Y, 0f), drawColor, CrosshairThickness);
            draw.DrawLine(new Vector3(pos.X + CrosshairGapRadius, pos.Y, 0f), new Vector3(pos.X + CrosshairHalfSize, pos.Y, 0f), drawColor, CrosshairThickness);
            draw.DrawLine(new Vector3(pos.X, pos.Y - CrosshairHalfSize, 0f), new Vector3(pos.X, pos.Y - CrosshairGapRadius, 0f), drawColor, CrosshairThickness);
            draw.DrawLine(new Vector3(pos.X, pos.Y + CrosshairGapRadius, 0f), new Vector3(pos.X, pos.Y + CrosshairHalfSize, 0f), drawColor, CrosshairThickness);
            draw.DrawSphere(new Vector3(pos.X, pos.Y, 0f), CrosshairGapRadius, drawColor);
        }

        // IEntityStatefulGizmo -- interaction

        /// <inheritdoc/>
        public void OnDragUpdate(Vector3 worldPos)
        {
            _cursorWorld = worldPos;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Left released: geo-convert position, fire callback, then remove self.
        /// Right pressed: cancel and remove self.
        /// </remarks>
        public void OnMouseEvent(MapMouseButton button, bool isPressed, Vector3 worldPos)
        {
            if (button == MapMouseButton.Left && !isPressed)
            {
                var (lat, lon, alt) = _geoTransform.ToGeodetic(worldPos);
                var geo = new GeoPoint { Latitude = lat, Longitude = lon, Altitude = alt };
                _onLocationPicked(geo);
                _onRemove();
            }
            else if (button == MapMouseButton.Right && isPressed)
            {
                _onRemove();
            }
        }

        /// <inheritdoc/>
        public void OnKeyEvent(MapKeyboardKey key, bool isPressed)
        {
            if (key == MapKeyboardKey.Escape && isPressed)
                _onRemove();
        }

        // Unused IEntityStatefulGizmo methods -- empty body
        /// <inheritdoc/>
        public void OnInteractionStarted(GizmoPickToken token, Vector3 worldPos) { }
        /// <inheritdoc/>
        public void OnCommit(Vector3 worldPos) { }
        /// <inheritdoc/>
        public void OnCancel() { }
        /// <inheritdoc/>
        public void OnMenuAction(int actionId) { }

        /// <inheritdoc/>
        public void Dispose() { }
    }
}
