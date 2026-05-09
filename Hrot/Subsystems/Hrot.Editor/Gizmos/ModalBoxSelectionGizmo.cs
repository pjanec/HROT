using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;

namespace Hrot.Editor.Gizmos
{
    /// <summary>
    /// Stateful gizmo that fires <see cref="_onSelectionComplete"/> with a list of entity
    /// indices on left-click, then calls <see cref="_onRemove"/>.
    ///
    /// <para>Current implementation fires with an empty list (placeholder); the full
    /// box-query over the entity spatial index is wired up in a later batch.</para>
    ///
    /// Replaces the deleted <c>ModalBoxSelectionTool</c> (Phase 4 of the gizmo migration).
    /// Exercised via <see cref="Hrot.ScenarioEditor.Gizmos.PlacementCanvasBridge"/> which
    /// forwards canvas events into this gizmo.
    /// </summary>
    public sealed class ModalBoxSelectionGizmo : IEntityStatefulGizmo
    {
        private readonly Action<IReadOnlyList<int>> _onSelectionComplete;
        private readonly Action                     _onRemove;

        /// <inheritdoc/>
        public bool RequiresExclusiveFocus => true;

        /// <inheritdoc/>
        public bool IsFocused { get; private set; }

        /// <inheritdoc/>
        public void SetFocus(bool isFocused) => IsFocused = isFocused;

        /// <param name="onSelectionComplete">
        /// Callback fired with the list of entity indices within the selection bounds on left-click.
        /// </param>
        /// <param name="onRemove">
        /// Callback invoked when the gizmo wants to exit. Typically calls
        /// <see cref="Hrot.ScenarioEditor.Gizmos.PlacementCanvasBridge.RequestPop"/> to pop
        /// the bridge from the canvas.
        /// </param>
        public ModalBoxSelectionGizmo(
            Action<IReadOnlyList<int>> onSelectionComplete,
            Action?                    onRemove = null)
        {
            _onSelectionComplete = onSelectionComplete ?? throw new ArgumentNullException(nameof(onSelectionComplete));
            _onRemove            = onRemove ?? (() => { });
        }

        // IEntityStatefulGizmo -- draw

        /// <inheritdoc/>
        public void UpdateAndDraw(float deltaTime, IDebugDrawBuilder draw) { }

        // IEntityStatefulGizmo -- interaction

        /// <inheritdoc/>
        public void OnDragUpdate(Vector3 worldPos) { }

        /// <inheritdoc/>
        /// <remarks>
        /// Left released: fire <see cref="_onSelectionComplete"/> with empty list, then remove self.
        /// Right pressed: cancel and remove self.
        /// </remarks>
        public void OnMouseEvent(MapMouseButton button, bool isPressed, Vector3 worldPos)
        {
            if (button == MapMouseButton.Left && !isPressed)
            {
                _onSelectionComplete(Array.Empty<int>());
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
