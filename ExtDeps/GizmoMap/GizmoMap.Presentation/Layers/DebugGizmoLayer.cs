using System;
using System.Numerics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using GizmoMap.Network;
using Raylib_cs;

namespace GizmoMap.Presentation
{
    /// <summary>
    /// Standalone Raylib rendering component that wires a <see cref="GizmoPrimitiveBuffer"/>
    /// to a <see cref="DebugPrimitiveRenderer2D"/>.
    ///
    /// Adapted from Fdp.Presentation DebugGizmoLayer with the following differences:
    /// - No ISimulationView or FdpEventBus parameters.
    /// - No IMapLayer interface (lives in Fdp.Toolkit.Vis2D.Abstractions).
    /// - Simple constructor taking only buffer and renderer.
    /// </summary>
    public sealed class DebugGizmoLayer
    {
        private readonly GizmoPrimitiveBuffer _buffer;
        private readonly DebugPrimitiveRenderer2D _renderer;

        // Active interaction tool driven by mouse input.
        private GizmoInteractionProxyTool? _activeTool;

        public DebugGizmoLayer(GizmoPrimitiveBuffer buffer, DebugPrimitiveRenderer2D renderer)
        {
            _buffer   = buffer;
            _renderer = renderer;
        }

        public void Render(Camera2D camera, float zoom)
        {
            _renderer.Render(_buffer.GetFrame(), camera, zoom);
        }

        /// <summary>
        /// Polls Raylib mouse/keyboard state and routes input to the active
        /// <see cref="GizmoInteractionProxyTool"/>, or starts a new one when the
        /// operator left-clicks inside a <see cref="DebugPrimitiveShape.Box2D"/> primitive.
        /// </summary>
        /// <param name="camera">Current camera used to convert screen pixels to world space.</param>
        /// <param name="onInteraction">
        /// Optional callback invoked with the pick token, event kind, and world position.
        /// </param>
        public void HandleInput(
            Camera2D camera,
            Action<GizmoPickToken, GizmoInteractionEventKind, Vector3>? onInteraction = null)
        {
            var screenPos = Raylib.GetMousePosition();
            var worldPos  = Raylib.GetScreenToWorld2D(screenPos, camera);

            // Try to start a new interaction on left press when no tool is active.
            if (_activeTool == null && Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                var frame = _buffer.GetFrame();
                for (int i = 0; i < frame.Length; i++)
                {
                    ref readonly var prim = ref frame[i];
                    if (prim.Shape != DebugPrimitiveShape.Box2D) continue;
                    if (prim.Space != CoordinateSpace.World) continue;
                    if (prim.SubElementId == 0) continue;

                    float dx = Math.Abs(worldPos.X - prim.BoxCenterX);
                    float dy = Math.Abs(worldPos.Y - prim.BoxCenterY);
                    if (dx <= prim.BoxExtentX && dy <= prim.BoxExtentY)
                    {
                        var token = new GizmoPickToken
                        {
                            // Use SubElementId as AnchorId when set; fall back to 1 for demo use.
                            AnchorId     = prim.SubElementId != 0 ? prim.SubElementId : 1,
                            SubElementId = prim.SubElementId,
                        };
                        _activeTool = new GizmoInteractionProxyTool(
                            token, onInteraction, onExit: () => _activeTool = null);
                        _activeTool.HandlePress(worldPos, MouseButton.Left);
                        break;
                    }
                }
            }

            // Drive the active tool with subsequent mouse state.
            if (_activeTool != null)
            {
                if (Raylib.IsMouseButtonDown(MouseButton.Left))
                    _activeTool.HandleDrag(worldPos, Raylib.GetMouseDelta());

                if (Raylib.IsMouseButtonReleased(MouseButton.Left))
                    _activeTool.HandleClick(worldPos, MouseButton.Left);

                if (Raylib.IsKeyPressed(KeyboardKey.Escape))
                    _activeTool.HandleKeyPressed(KeyboardKey.Escape);
            }
        }
    }
}
