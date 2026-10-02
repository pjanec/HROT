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

        /// <summary>
        /// ⭐⭐⭐ <b>TRUE, and it is not optional for an exclusive-focus gizmo.</b> 📄
        /// <c>docs/designs/gizmos-1/gizmo-input-focus-design.md</c> §5.1 · <c>§6.2c</c> · <c>CE-259k</c>.
        ///
        /// <para>🔴 <b>The defect this closes, measured <c>2026-09-09</c> by running the editor:</b> this gizmo
        /// implements its ENTIRE behaviour in <see cref="OnMouseEvent"/> / <see cref="OnKeyEvent"/> — the RAW
        /// handlers — while <c>OnInteractionStarted</c> / <c>OnCommit</c> are empty stubs. With
        /// <c>WantsRawInput</c> left at its <see langword="false"/> default the terminal is told the opposite,
        /// and the gizmo is DEAF ON BOTH PATHS: <c>DebugGizmoLayer.cs:126</c> withholds raw events, and
        /// <c>:428</c> suppresses spatial hit-testing for everything not anchored to the capture token because
        /// <c>RequiresExclusiveFocus</c> is set. ⇒ it DRAWS and ignores every click, and Escape too.</para>
        ///
        /// <para>🔒 <b>Design basis:</b> §5.1 defines <c>InputCaptureBinding</c> as <i>"a meta-primitive
        /// declaring that the bound token wants raw hardware events streamed to it"</i>, with ONE flag —
        /// <c>ConditionMask: 1 = Exclusive, 0 = Shared</c>. ⛔ There is no <c>wantsRawInput</c> bit in the
        /// design at all; emitting the binding WAS the request. The second bit is an as-built divergence, and
        /// reconciling it is <c>CE-259l</c> — until then every exclusive gizmo must say this explicitly.</para>
        /// </summary>
        public bool WantsRawInput => true;

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
