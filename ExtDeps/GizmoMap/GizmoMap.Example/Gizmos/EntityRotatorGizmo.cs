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
        private float _currentYawRad;
        private readonly Action<float> _onCommit;
        private readonly Action _onRemove;

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
            _currentYawRad = initialYawRad;
            _onCommit      = onCommit;
            _onRemove      = onRemove;
        }

        // Draws a yellow arrow from the entity center toward the current heading.
        // The arrow length is 50 world units.
        public void UpdateAndDraw(float deltaTime, IGizmoDrawBuilder draw)
        {
            var from = new Vector3(_entityPos.X, _entityPos.Y, 0f);
            var tip  = new Vector3(
                _entityPos.X + MathF.Cos(_currentYawRad) * 50f,
                _entityPos.Y + MathF.Sin(_currentYawRad) * 50f,
                0f);
            draw.DrawArrow(from, tip, Rgba32.Yellow, headSize: 6f);
        }

        // DragUpdate fires while the mouse moves with exclusive capture held.
        // Recompute the yaw from the current cursor world position.
        public void OnDragUpdate(Vector3 worldPos)
        {
            float dx = worldPos.X - _entityPos.X;
            float dy = worldPos.Y - _entityPos.Y;
            if (MathF.Abs(dx) > 0.001f || MathF.Abs(dy) > 0.001f)
                _currentYawRad = MathF.Atan2(dy, dx);
        }

        // Left released: commit the new heading and exit.
        // Right pressed: cancel without writing back and exit.
        public void OnMouseEvent(MapMouseButton button, bool isPressed, Vector3 worldPos)
        {
            if (button == MapMouseButton.Left && !isPressed)
            {
                _onCommit(_currentYawRad);
                _onRemove();
            }
            else if (button == MapMouseButton.Right && isPressed)
            {
                _onRemove();
            }
        }

        // Escape pressed: cancel.
        public void OnKeyEvent(MapKeyboardKey key, bool isPressed)
        {
            if (key == MapKeyboardKey.Escape && isPressed)
                _onRemove();
        }

        // These are not used for the exclusive-capture rotator but must be implemented.
        public void OnInteractionStarted(GizmoPickToken token, Vector3 worldPos) { }
        public void OnCommit(Vector3 worldPos)  { }
        public void OnCancel()                  { }
        public void OnMenuAction(int actionId)  { }

        public void Dispose() { }
    }
}
