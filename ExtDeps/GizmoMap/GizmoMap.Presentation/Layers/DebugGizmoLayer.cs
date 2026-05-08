using System;
using System.Collections.Generic;
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

        // Active drag interaction tool driven by mouse input.
        private GizmoInteractionProxyTool? _activeTool;

        // Context menu presenter (ImGui popup).
        private readonly ContextMenuAdapter _contextMenuAdapter = new();

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
        /// Right-clicking a Box2D with a <see cref="DebugPrimitiveShape.ContextMenuBinding"/>
        /// schedules a context menu popup (rendered via <see cref="DrawContextMenu"/>).
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

            var frame = _buffer.GetFrame();

            // Build menu bindings dictionary from ContextMenuBinding meta-primitives.
            var menuBindings = new Dictionary<long, uint>();
            foreach (ref readonly var prim in frame)
            {
                if (prim.Shape == DebugPrimitiveShape.ContextMenuBinding)
                    menuBindings[prim.InspNetworkId] = prim.StringHash;
            }
            if (Raylib.IsMouseButtonReleased(MouseButton.Right))
            {
                Console.WriteLine($"[Debug] Right-click detected. Menu bindings count: {menuBindings.Count}");
            }

            // Try to start a new interaction on left press when no tool is active.
            if (_activeTool == null && Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
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

            // Right-click: show context menu for the hit Box2D if a binding exists.
            if (_activeTool == null && Raylib.IsMouseButtonReleased(MouseButton.Right))
            {
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
                        long entityId = prim.SubElementId; // SubElementId used as entity binding key
                        Console.WriteLine($"[Debug] Hit Box2D. SubElementId: {entityId}");
                        if (menuBindings.TryGetValue(entityId, out uint menuHash))
                        {
                            Console.WriteLine($"[Debug] Found binding hash: {menuHash}");
                            string? json = _buffer.InternMap.TryResolve(menuHash);
                            if (json != null)
                            {
                                Console.WriteLine($"[Debug] JSON resolved successfully. Length: {json.Length}");
                                _contextMenuAdapter.Schedule(entityId, json);
                            }
                            else
                            {
                                Console.WriteLine("[Debug] ERROR: TryResolve returned null. InternMap sync failed.");
                            }
                        }
                        else
                        {
                            Console.WriteLine("[Debug] ERROR: No menu binding found for this SubElementId.");
                        }
                        break;
                    }
                }
            }

            // Drive the active drag tool with subsequent mouse state.
            if (_activeTool != null)
            {
                if (Raylib.IsMouseButtonDown(MouseButton.Left))
                    _activeTool.HandleDrag(worldPos, Raylib.GetMouseDelta());

                if (Raylib.IsMouseButtonReleased(MouseButton.Left))
                    _activeTool.HandleClick(worldPos, MouseButton.Left);

                if (Raylib.IsMouseButtonReleased(MouseButton.Right))
                    _activeTool.HandleClick(worldPos, MouseButton.Right);

                if (Raylib.IsKeyPressed(KeyboardKey.Escape))
                    _activeTool.HandleKeyPressed(KeyboardKey.Escape);
            }
        }

        /// <summary>
        /// Renders any pending context menu popup via ImGui.
        /// Must be called inside an <c>rlImGui.Begin()</c>/<c>rlImGui.End()</c> block each frame.
        /// </summary>
        /// <param name="onMenuAction">
        /// Callback invoked with the pick token and clicked action id when the operator selects a menu item.
        /// </param>
        public void DrawContextMenu(Action<GizmoPickToken, int>? onMenuAction = null)
        {
            _contextMenuAdapter.DrawScheduled((anchorId, actionId) =>
                onMenuAction?.Invoke(new GizmoPickToken { AnchorId = anchorId }, actionId));
        }
    }
}

