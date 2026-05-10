using System;
using System.Numerics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;

namespace GizmoMap.Example
{
    // Exclusive-focus gizmo that lets the operator rotate a target entity by moving
    // the mouse. Triggered by a host action (e.g. pressing R over an entity); removed
    // when the operator releases Left (commit) or presses Right/Escape (cancel).
    //
    // Design (gizmo-input-focus-design.md section 10.2 and 11):
    // - RequiresExclusiveFocus = true => the GizmoInteractionManager emits
    //   InputCaptureBinding(Exclusive=true) and the terminal streams all raw HW to it.
    // - The gizmo does NOT emit the capture binding itself.
    // - No ECS, no ISimulationView. Entity state is passed at construction time.
    // - Self-removal: calls onRemove (provided by the host) from within OnMouseEvent /
    //   OnKeyEvent. The host is responsible for calling manager.RemoveTool after dispatch.
    public sealed class EntityRotatorGizmo : IStatefulGizmo
    {
        private readonly Vector2 _entityPos;
        private Vector3 _currentCursorPos;
        private float _currentYawRad;
        private readonly Action<float> _onCommit;
        private readonly Action _onRemove;
        private bool _active = true;

        public bool RequiresExclusiveFocus => true;
        public bool IsFocused { get; private set; }
        public void SetFocus(bool isFocused) => IsFocused = isFocused;

        // entityPos      - fixed world position of the target entity.
        // initialYawRad  - starting heading in radians.
        // onCommit       - called with the final yaw when the operator releases Left.
        // onRemove       - called when the gizmo wants to exit (commit or cancel).
        public EntityRotatorGizmo(
            Vector2 entityPos,
            float initialYawRad,
            Action<float> onCommit,
            Action onRemove)
        {
            _entityPos     = entityPos;
            _currentCursorPos = new Vector3(entityPos.X, entityPos.Y, 0f);
            _currentYawRad = initialYawRad;
            _onCommit      = onCommit;
            _onRemove      = onRemove;
        }

        // Draws a yellow arrow from the entity center toward the current heading.
        // The arrow length is 50 world units.
        public void UpdateAndDraw(float deltaTime, IGizmoDrawBuilder draw)
        {
            if (!_active) return;
            var from = new Vector3(_entityPos.X, _entityPos.Y, 0f);
            draw.DrawLine(from, _currentCursorPos, Rgba32.Yellow, thickness: 2f, sizeMode: SizeMode.ScreenPixels);

            float compassDeg = ((90f - _currentYawRad * (180f / MathF.PI)) % 360f + 360f) % 360f;
            string label = $"{compassDeg:F0}deg";
            float midX = (from.X + _currentCursorPos.X) * 0.5f;
            float midY = (from.Y + _currentCursorPos.Y) * 0.5f;
            draw.DrawTextLong(midX, midY + 15f, label, Rgba32.White);
        }

        // DragUpdate fires while the mouse moves with exclusive capture held.
        // Recompute the yaw from the current cursor world position.
        public void OnDragUpdate(Vector3 worldPos)
        {
            _currentCursorPos = worldPos;
            float dx = worldPos.X - _entityPos.X;
            float dy = worldPos.Y - _entityPos.Y;
            if (MathF.Abs(dx) > 0.001f || MathF.Abs(dy) > 0.001f)
                _currentYawRad = MathF.Atan2(dy, dx);
        }

        // Left released: commit the new heading and exit.
        // Right pressed: cancel without writing back and exit.
        public void OnMouseEvent(MapMouseButton button, bool isPressed, Vector3 worldPos)
        {
            if (!_active) return;
            _currentCursorPos = worldPos;
            if (button == MapMouseButton.Left && !isPressed)
            {
                _onCommit(_currentYawRad);
                _active = false;
                _onRemove();
            }
            else if (button == MapMouseButton.Right && isPressed)
            {
                _active = false;
                _onRemove();
            }
        }

        // Escape pressed: cancel.
        public void OnKeyEvent(MapKeyboardKey key, bool isPressed)
        {
            if (!_active) return;
            if (key == MapKeyboardKey.Escape && isPressed)
            {
                _active = false;
                _onRemove();
            }
        }

        // These are not used for the exclusive-capture rotator but must be implemented.
        public void OnInteractionStarted(GizmoPickToken token, Vector3 worldPos)
        {
            _currentCursorPos = worldPos;
        }
        public void OnCommit(Vector3 worldPos)  { }
        public void OnCancel()                  { }
        public void OnMenuAction(int actionId)  { }

        public void Dispose() { }
    }
}
