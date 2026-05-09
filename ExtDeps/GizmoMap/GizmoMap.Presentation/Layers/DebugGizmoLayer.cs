using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
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
        ///
        /// When an <see cref="DebugPrimitiveShape.InputCaptureBinding"/> with exclusive mode
        /// is present in the frame, all raw HW events are routed to the capturing token and
        /// normal spatial hit-testing is suppressed.
        ///
        /// Hit-testing iterates the primitive buffer in reverse so the last-submitted (topmost)
        /// Box2D wins. <see cref="DebugPrimitive.DebugLayer"/> is NOT used as a Z-order key.
        /// </summary>
        /// <param name="camera">Current camera used to convert screen pixels to world space.</param>
        /// <param name="onInteraction">
        /// Optional callback invoked with the pick token, event kind, world position, actionId,
        /// and stateFlags. For non-RawInput events actionId=0 and stateFlags=0.
        /// For RawInput: actionId=(int)MapMouseButton or (int)MapKeyboardKey;
        /// stateFlags bit7=1 mouse/0 keyboard, bit0=1 pressed/0 released.
        /// </param>
        public void HandleInput(
            Camera2D camera,
            Action<GizmoPickToken, GizmoInteractionEventKind, Vector3, int, byte>? onInteraction = null)
        {
            var screenPos = Raylib.GetMousePosition();
            var worldPos  = Raylib.GetScreenToWorld2D(screenPos, camera);

            var frame = _buffer.GetFrame();

            // ---- Scan for exclusive InputCaptureBinding --------------------------------
            // When found, route all raw HW events to the capturing token and skip
            // normal spatial hit-testing. The gizmo declares intent; the terminal obeys.
            for (int i = 0; i < frame.Length; i++)
            {
                ref readonly var prim = ref frame[i];
                if (prim.Shape != DebugPrimitiveShape.InputCaptureBinding) continue;
                if (prim.ConditionMask != 1u) continue; // 0 = shared, skip

                var captureToken = new GizmoPickToken
                {
                    AnchorId     = prim.InspNetworkId,
                    SubElementId = prim.SubElementId,
                };
                var worldPos3 = new Vector3(worldPos.X, worldPos.Y, 0f);

                // Mouse move -> DragUpdate so the gizmo can recompute heading/position.
                var delta = Raylib.GetMouseDelta();
                if (delta.X != 0 || delta.Y != 0)
                    onInteraction?.Invoke(captureToken, GizmoInteractionEventKind.DragUpdate, worldPos3, 0, 0);

                // Mouse button press/release -> RawInput (bit7=1 mouse; bit0=1 pressed).
                if (Raylib.IsMouseButtonPressed(MouseButton.Left))
                    onInteraction?.Invoke(captureToken, GizmoInteractionEventKind.RawInput,
                        worldPos3, (int)MapMouseButton.Left, 0x81);
                else if (Raylib.IsMouseButtonReleased(MouseButton.Left))
                    onInteraction?.Invoke(captureToken, GizmoInteractionEventKind.RawInput,
                        worldPos3, (int)MapMouseButton.Left, 0x80);

                if (Raylib.IsMouseButtonPressed(MouseButton.Right))
                    onInteraction?.Invoke(captureToken, GizmoInteractionEventKind.RawInput,
                        worldPos3, (int)MapMouseButton.Right, 0x81);
                else if (Raylib.IsMouseButtonReleased(MouseButton.Right))
                    onInteraction?.Invoke(captureToken, GizmoInteractionEventKind.RawInput,
                        worldPos3, (int)MapMouseButton.Right, 0x80);

                // Keyboard Escape -> RawInput (bit7=0 keyboard; bit0=1 pressed).
                if (Raylib.IsKeyPressed(KeyboardKey.Escape))
                    onInteraction?.Invoke(captureToken, GizmoInteractionEventKind.RawInput,
                        worldPos3, (int)MapKeyboardKey.Escape, 0x01);

                // Exclusive capture active: suppress all normal hit-testing this frame.
                return;
            }

            // ---- Build menu bindings dictionary from ContextMenuBinding meta-primitives ---
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

            // ---- Try to start a new interaction on left press (reverse iteration) -------
            // Iterating in reverse ensures the last-submitted (topmost) Box2D wins the
            // hit-test. DebugLayer is a visibility mask and is NOT used as a depth key.
            if (_activeTool == null && Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                for (int i = frame.Length - 1; i >= 0; i--)
                {
                    ref readonly var prim = ref frame[i];
                    if (prim.Shape != DebugPrimitiveShape.Box2D) continue;
                    if (prim.Space != CoordinateSpace.World) continue;
                    if (prim.SubElementId == 0) continue;

                    float dx = Math.Abs(worldPos.X - prim.BoxCenterX);
                    float dy = Math.Abs(worldPos.Y - prim.BoxCenterY);
                    if (dx <= prim.BoxExtentX && dy <= prim.BoxExtentY)
                    {
                        // BoxAnchorId routes to the owning manager slot; fall back to SubElementId
                        // for legacy unmanaged Box2D primitives (e.g. the interactive drag box).
                        var token = new GizmoPickToken
                        {
                            AnchorId     = prim.BoxAnchorId != 0 ? prim.BoxAnchorId : (long)prim.SubElementId,
                            SubElementId = prim.SubElementId,
                        };
                        _activeTool = new GizmoInteractionProxyTool(
                            token, worldPos, onInteraction, onExit: () => _activeTool = null);
                        _activeTool.HandlePress(worldPos, MouseButton.Left);
                        break;
                    }
                }
            }

            // ---- Right-click: show context menu for the topmost hit Box2D ---------------
            if (_activeTool == null && Raylib.IsMouseButtonReleased(MouseButton.Right))
            {
                for (int i = frame.Length - 1; i >= 0; i--)
                {
                    ref readonly var prim = ref frame[i];
                    if (prim.Shape != DebugPrimitiveShape.Box2D) continue;
                    if (prim.Space != CoordinateSpace.World) continue;
                    if (prim.SubElementId == 0) continue;

                    float dx = Math.Abs(worldPos.X - prim.BoxCenterX);
                    float dy = Math.Abs(worldPos.Y - prim.BoxCenterY);
                    if (dx <= prim.BoxExtentX && dy <= prim.BoxExtentY)
                    {
                        // Use BoxAnchorId for managed handles; fall back to SubElementId for
                        // unmanaged Box2D primitives whose context-menu binding key equals SubElementId.
                        long entityId = prim.BoxAnchorId != 0 ? prim.BoxAnchorId : (long)prim.SubElementId;
                        Console.WriteLine($"[Debug] Hit Box2D. entityId: {entityId}");
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

            // ---- Drive the active drag tool with subsequent mouse state ----------------
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

