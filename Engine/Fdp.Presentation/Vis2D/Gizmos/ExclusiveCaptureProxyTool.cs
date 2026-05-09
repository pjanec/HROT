using System.Numerics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Vis2D.Abstractions;
using GizmoMouseButton = Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapMouseButton;
using GizmoKeyboardKey = Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapKeyboardKey;

namespace Fdp.Toolkit.Vis2D.Gizmos
{
    // Canvas tool that captures all mouse and keyboard input and forwards it
    // directly to an IEntityStatefulGizmo with RequiresExclusiveFocus = true.
    //
    // Lifecycle:
    //   Push onto MapCanvas when the gizmo is activated (e.g. "Rotate" context menu).
    //   The tool pops itself on left-release (commit), right-release (cancel), or Escape.
    //   The gizmo's onRemove action must deactivate the gizmo in DataDrivenGizmoSystem.
    //
    // Input routing:
    //   HandleHover    -> gizmo.OnDragUpdate (mouse move without button held)
    //   HandleDrag     -> gizmo.OnDragUpdate (mouse move with button held)
    //   HandleClick(L) -> gizmo.OnMouseEvent(Left, released) then PopTool
    //   HandleClick(R) -> gizmo.OnMouseEvent(Right, pressed) then PopTool
    //   HandleKeyPressed(Esc) -> gizmo.OnKeyEvent(Escape, pressed) then PopTool
    //
    // Note: HandleDrag returns false so _isDraggingInteraction stays false in the
    // canvas, ensuring HandleClick fires on every mouse release.
    public sealed class ExclusiveCaptureProxyTool : IMapTool
    {
        public string Name => "ExclusiveCapture";

        private readonly IEntityStatefulGizmo _gizmo;
        private MapCanvas? _canvas;

        public ExclusiveCaptureProxyTool(IEntityStatefulGizmo gizmo)
        {
            _gizmo = gizmo;
        }

        public void OnEnter(MapCanvas canvas) => _canvas = canvas;
        public void OnExit() => _canvas = null;
        public void Update(float dt) { }
        public void Draw(RenderContext ctx) { }

        public bool HandleHover(Vector2 worldPos)
        {
            _gizmo.OnDragUpdate(new Vector3(worldPos.X, worldPos.Y, 0f));
            return false;
        }

        public bool HandleDrag(Vector2 worldPos, Vector2 delta)
        {
            // Keep the gizmo updated while a button is held.
            // Return false so the canvas does not set _isDraggingInteraction = true,
            // which would suppress the subsequent HandleClick call.
            _gizmo.OnDragUpdate(new Vector3(worldPos.X, worldPos.Y, 0f));
            return false;
        }

        public bool HandlePress(Vector2 worldPos, MapMouseButton button) => false;

        public bool HandleClick(Vector2 worldPos, MapMouseButton button)
        {
            var pos = new Vector3(worldPos.X, worldPos.Y, 0f);
            if (button == MapMouseButton.Left)
            {
                // isPressed=false signals a release commit to the gizmo.
                _gizmo.OnMouseEvent((GizmoMouseButton)(int)button, isPressed: false, pos);
                _canvas?.PopTool();
                return true;
            }
            if (button == MapMouseButton.Right)
            {
                // isPressed=true matches EntityRotatorGizmo's "Right && isPressed" cancel check.
                _gizmo.OnMouseEvent((GizmoMouseButton)(int)button, isPressed: true, pos);
                _canvas?.PopTool();
                return true;
            }
            return false;
        }

        public bool HandleKeyPressed(MapKeyboardKey key)
        {
            _gizmo.OnKeyEvent((GizmoKeyboardKey)(int)key, isPressed: true);
            if (key == MapKeyboardKey.Escape)
            {
                _canvas?.PopTool();
                return true;
            }
            return false;
        }
    }
}
