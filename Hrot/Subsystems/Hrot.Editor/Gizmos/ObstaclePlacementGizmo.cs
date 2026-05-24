using System;
using System.Numerics;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;

namespace Hrot.Editor.Gizmos
{
    /// <summary>
    /// Stateful gizmo that records a single left-click world position and fires
    /// <see cref="_onObstaclePlaced"/> with that position before calling <see cref="_onRemove"/>.
    ///
    /// Replaces the deleted <c>ObstaclePlacementTool</c> (Phase 3 of the gizmo migration).
    /// Exercised via <see cref="Fdp.Toolkit.Diagnostics.Gizmos.Systems.GlobalGizmoManager"/> which
    /// forwards canvas events into this gizmo.
    /// </summary>
    public sealed class ObstaclePlacementGizmo : IEntityStatefulGizmo
    {
        private readonly float           _radius;
        private readonly Action<Vector2> _onObstaclePlaced;
        private readonly Action          _onRemove;

        private Vector3 _cursorWorld;

        /// <inheritdoc/>
        public bool RequiresExclusiveFocus => true;

        /// <inheritdoc/>
        public bool IsFocused { get; private set; }

        /// <inheritdoc/>
        public void SetFocus(bool isFocused) => IsFocused = isFocused;

        /// <param name="radius">Preview radius indicator drawn at the cursor.</param>
        /// <param name="onObstaclePlaced">
        /// Callback fired with the clicked world position (as <see cref="Vector2"/>) when the
        /// operator left-clicks.
        /// </param>
        /// <param name="onRemove">
        /// Callback invoked when the gizmo wants to exit. Typically calls
        /// <c>GlobalGizmoManager.Unregister</c> to remove the gizmo from the manager.
        /// </param>
        public ObstaclePlacementGizmo(
            float           radius,
            Action<Vector2> onObstaclePlaced,
            Action?         onRemove = null)
        {
            _radius           = radius;
            _onObstaclePlaced = onObstaclePlaced ?? throw new ArgumentNullException(nameof(onObstaclePlaced));
            _onRemove         = onRemove ?? (() => { });
        }

        // IEntityStatefulGizmo — draw

        /// <inheritdoc/>
        /// <remarks>Draws a red sphere at the current cursor world position with <see cref="_radius"/>.</remarks>
        public void UpdateAndDraw(ISimulationView view, float deltaTime, IDebugDrawBuilder draw)
        {
            draw.DrawSphere(_cursorWorld, _radius, Rgba32.Red);
        }

        // IEntityStatefulGizmo — interaction

        /// <inheritdoc/>
        public void OnDragUpdate(Vector3 worldPos)
        {
            _cursorWorld = worldPos;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Left released: fire <see cref="_onObstaclePlaced"/> then remove self.
        /// Right pressed: cancel and remove self.
        /// </remarks>
        public void OnMouseEvent(MapMouseButton button, bool isPressed, Vector3 worldPos)
        {
            if (button == MapMouseButton.Left && !isPressed)
            {
                _onObstaclePlaced(new Vector2(worldPos.X, worldPos.Y));
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

        // Unused IEntityStatefulGizmo methods — empty body
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
