using System;
using System.Numerics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using GizmoMap.Network;
using Raylib_cs;

namespace GizmoMap.Presentation
{
    /// <summary>
    /// Interaction tool that arms on press, publishes drag-update events, and
    /// commits or cancels on release/right-click/ESC.
    ///
    /// Adapted from Fdp.Presentation GizmoInteractionProxyTool with the following differences:
    /// - Uses <see cref="GizmoPickToken"/> (network-stable IDs) instead of ECS PickToken.
    /// - Uses a callback delegate instead of FdpEventBus.
    /// - Uses an optional exit callback instead of MapCanvas.PopTool().
    /// - Callback carries actionId and stateFlags so the same delegate handles RawInput.
    ///   For Started/DragUpdate/Commit/Cancel, actionId=0 and stateFlags=0.
    /// </summary>
    public sealed class GizmoInteractionProxyTool
    {
        private readonly GizmoPickToken _token;
        private readonly Action<GizmoPickToken, GizmoInteractionEventKind, Vector3, int, byte>? _onInteraction;
        private readonly Action? _onExit;
        private readonly CoordinateSpace _space;
        private bool _dragActive;

        public GizmoInteractionProxyTool(
            GizmoPickToken token,
            Vector2 initialWorldPos,
            Action<GizmoPickToken, GizmoInteractionEventKind, Vector3, int, byte>? onInteraction = null,
            Action? onExit = null,
            CoordinateSpace space = CoordinateSpace.World)
        {
            _token         = token;
            _onInteraction = onInteraction;
            _onExit        = onExit;
            _space         = space;

            // Fire Started event immediately on construction (mirrors OnEnter behaviour).
            _onInteraction?.Invoke(_token, GizmoInteractionEventKind.Started,
                new Vector3(initialWorldPos.X, initialWorldPos.Y, 0f), 0, 0);
        }

        public bool HandlePress(Vector2 worldPos, MouseButton button)
        {
            if (button == MouseButton.Left)
            {
                _dragActive = true;
                return true;
            }
            return false;
        }

        public bool HandleDrag(Vector2 worldPos, Vector2 delta)
        {
            if (!_dragActive) return false;
            _onInteraction?.Invoke(
                _token,
                GizmoInteractionEventKind.DragUpdate,
                new Vector3(worldPos.X, worldPos.Y, 0f), 0, 0);
            return true;
        }

        public bool HandleClick(Vector2 worldPos, MouseButton button)
        {
            if (button == MouseButton.Left)
            {
                if (_dragActive)
                {
                    _dragActive = false;
                    _onInteraction?.Invoke(
                        _token,
                        GizmoInteractionEventKind.Commit,
                        new Vector3(worldPos.X, worldPos.Y, 0f), 0, 0);
                    _onExit?.Invoke();
                    return true;
                }
                else
                {
                    // Click-away (no prior press): cancel without committing.
                    _onInteraction?.Invoke(_token, GizmoInteractionEventKind.Cancel, Vector3.Zero, 0, 0);
                    _onExit?.Invoke();
                    return false;
                }
            }

            // Right button = cancel.
            if (button == MouseButton.Right)
            {
                _dragActive = false;
                _onInteraction?.Invoke(_token, GizmoInteractionEventKind.Cancel, Vector3.Zero, 0, 0);
                _onExit?.Invoke();
                return true;
            }

            return false;
        }

        public bool HandleKeyPressed(KeyboardKey key)
        {
            if (key == KeyboardKey.Escape)
            {
                _dragActive = false;
                _onInteraction?.Invoke(_token, GizmoInteractionEventKind.Cancel, Vector3.Zero, 0, 0);
                _onExit?.Invoke();
                return true;
            }
            return false;
        }
    }
}
