using System;
using System.Numerics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Fdp.Toolkit.Diagnostics.Gizmos.UndoRedo;
using Fdp.Toolkit.ReplayBrowser.Search;

namespace Fdp.Toolkit.ReplayBrowser
{
    /// <summary>
    /// Stateful gizmo that lets the operator drag a 2D bounding-box on the canvas.
    ///
    /// Left-press to anchor the start corner; drag to preview the box; left-release
    /// to commit the selection and call <paramref name="onComplete"/>.
    /// Right-click or Escape cancels without firing <paramref name="onComplete"/>.
    ///
    /// Registered with GlobalGizmoManager; self-removes via <paramref name="onRemove"/>.
    /// </summary>
    public sealed class BoundingBoxPickerGizmo : IEntityStatefulGizmo
    {
        private static readonly Rgba32 BoxColor = new Rgba32(0, 200, 255, 200);

        private readonly Action<BoundingBox2D> _onComplete;
        private readonly Action _onRemove;

        private Vector3 _startPos;
        private Vector3 _currentPos;
        private bool _isDragging;

        /// <inheritdoc/>
        public bool RequiresExclusiveFocus => true;

        /// <inheritdoc/>
        public bool WantsRawInput => true;

        /// <inheritdoc/>
        public bool IsFocused { get; private set; }

        /// <inheritdoc/>
        public void SetFocus(bool isFocused) => IsFocused = isFocused;

        /// <param name="onComplete">
        /// Invoked once the user releases the left button; receives the committed bounding box.
        /// </param>
        /// <param name="onRemove">
        /// Invoked when the gizmo wants to exit (both commit and cancel paths).
        /// </param>
        public BoundingBoxPickerGizmo(Action<BoundingBox2D> onComplete, Action onRemove)
        {
            _onComplete = onComplete ?? throw new ArgumentNullException(nameof(onComplete));
            _onRemove   = onRemove   ?? throw new ArgumentNullException(nameof(onRemove));
        }

        // ── IEntityStatefulGizmo -- draw ──────────────────────────────────────

        /// <inheritdoc/>
        public void UpdateAndDraw(float deltaTime, IDebugDrawBuilder draw)
        {
            if (!_isDragging)
            {
                var crossColor = new Rgba32(255, 255, 0, 255);
                var pos = _currentPos;
                float hs = 10f, gp = 4f, th = 1.5f;

                draw.DrawLine(new Vector3(pos.X - hs, pos.Y, 0f), new Vector3(pos.X - gp, pos.Y, 0f), crossColor, th);
                draw.DrawLine(new Vector3(pos.X + gp, pos.Y, 0f), new Vector3(pos.X + hs, pos.Y, 0f), crossColor, th);
                draw.DrawLine(new Vector3(pos.X, pos.Y - hs, 0f), new Vector3(pos.X, pos.Y - gp, 0f), crossColor, th);
                draw.DrawLine(new Vector3(pos.X, pos.Y + gp, 0f), new Vector3(pos.X, pos.Y + hs, 0f), crossColor, th);
                draw.DrawSphere(new Vector3(pos.X, pos.Y, 0f), gp, crossColor);
                draw.DrawTextLong(pos.X, pos.Y + 20f, "Click & drag to define area", Rgba32.Yellow);
                return;
            }

            Vector2 start   = new Vector2(_startPos.X,   _startPos.Y);
            Vector2 current = new Vector2(_currentPos.X, _currentPos.Y);

            Vector2 center  = (start + current) * 0.5f;
            Vector2 extents = new Vector2(MathF.Abs(current.X - start.X) * 0.5f,
                                         MathF.Abs(current.Y - start.Y) * 0.5f);

            draw.DrawBox2D(center, extents, BoxColor,
                angleDeg: 0f,
                thickness: 1.5f,
                sizeMode: SizeMode.WorldMeters);
        }

        // ── IEntityStatefulGizmo -- interaction ───────────────────────────────

        /// <inheritdoc/>
        public void OnDragUpdate(Vector3 worldPos) => _currentPos = worldPos;

        /// <inheritdoc/>
        public void OnMouseEvent(MapMouseButton button, bool isPressed, Vector3 worldPos)
        {
            if (button == MapMouseButton.Left && isPressed)
            {
                _startPos   = worldPos;
                _currentPos = worldPos;
                _isDragging = true;
            }
            else if (button == MapMouseButton.Left && !isPressed && _isDragging)
            {
                _isDragging = false;

                float minX = MathF.Min(_startPos.X, _currentPos.X);
                float minY = MathF.Min(_startPos.Y, _currentPos.Y);
                float maxX = MathF.Max(_startPos.X, _currentPos.X);
                float maxY = MathF.Max(_startPos.Y, _currentPos.Y);

                _onComplete(new BoundingBox2D
                {
                    Min = new Vector2(minX, minY),
                    Max = new Vector2(maxX, maxY)
                });
                _onRemove();
            }
            else if (button == MapMouseButton.Right && isPressed)
            {
                _isDragging = false;
                _onRemove();
            }
        }

        /// <inheritdoc/>
        public void OnKeyEvent(MapKeyboardKey key, bool isPressed)
        {
            if (key == MapKeyboardKey.Escape && isPressed)
            {
                _isDragging = false;
                _onRemove();
            }
        }

        // ── No-op interface members ───────────────────────────────────────────

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
