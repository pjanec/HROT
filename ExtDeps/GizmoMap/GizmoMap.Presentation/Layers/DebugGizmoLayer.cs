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
    /// Standalone Raylib rendering component that drives a <see cref="DebugPrimitiveRenderer2D"/>.
    ///
    /// Buffer-agnostic: callers pass the primitive span and intern map on each call so
    /// the same layer instance can be shared across multiple buffer sources (local viewer,
    /// remote gizmo stream, etc.).
    ///
    /// Adapted from Fdp.Presentation DebugGizmoLayer with the following differences:
    /// - No ISimulationView or FdpEventBus parameters.
    /// - No IMapLayer interface (lives in Fdp.Toolkit.Vis2D.Abstractions).
    /// - Constructor takes only renderer; buffer data is passed per-call.
    /// </summary>
    public sealed class DebugGizmoLayer
    {
        private readonly DebugPrimitiveRenderer2D _renderer;

        // Active drag interaction tool driven by mouse input.
        private GizmoInteractionProxyTool? _activeTool;

        // Context menu presenter (ImGui popup).
        private readonly ContextMenuAdapter _contextMenuAdapter = new();

        public DebugGizmoLayer(DebugPrimitiveRenderer2D renderer)
        {
            _renderer = renderer;
        }

        public void Render(ReadOnlySpan<DebugPrimitive> primitives, Camera2D camera, float zoom)
        {
            _renderer.Render(primitives, camera, zoom);
        }

        /// <summary>
        /// Polls Raylib mouse/keyboard state and routes input to the active
        /// <see cref="GizmoInteractionProxyTool"/>, or starts a new one when the
        /// operator left-clicks inside a <see cref="DebugPrimitiveShape.Box2D"/> primitive.
        /// Right-clicking a Box2D with a <see cref="DebugPrimitiveShape.ContextMenuBinding"/>
        /// schedules a context menu popup (rendered via <see cref="DrawContextMenu"/>).
        /// When no entity box is hit, falls back to the canvas anchor (<c>-1L</c>) so the
        /// empty-space menu is resolved through the same pipeline.
        ///
        /// When an <see cref="DebugPrimitiveShape.InputCaptureBinding"/> with exclusive mode
        /// is present in the frame, all raw HW events are routed to the capturing token and
        /// normal spatial hit-testing is suppressed.
        ///
        /// Hit-testing iterates the primitive buffer in reverse so the last-submitted (topmost)
        /// Box2D wins. <see cref="DebugPrimitive.DebugLayer"/> is NOT used as a Z-order key.
        /// </summary>
        /// <param name="primitives">Current frame of debug primitives from the gizmo buffer.</param>
        /// <param name="internMap">Intern map used to resolve string hashes in context-menu bindings.</param>
        /// <param name="camera">Current camera used to convert screen pixels to world space.</param>
        /// <param name="onInteraction">
        /// Optional callback invoked with the pick token, event kind, world position, actionId,
        /// and stateFlags. For non-RawInput events actionId=0 and stateFlags=0.
        /// For RawInput: actionId=(int)MapMouseButton or (int)MapKeyboardKey;
        /// stateFlags bit7=1 mouse/0 keyboard, bit0=1 pressed/0 released.
        /// </param>
        public void HandleInput(
            ReadOnlySpan<DebugPrimitive> primitives,
            StringInternMap internMap,
            Camera2D camera,
            Action<GizmoPickToken, GizmoInteractionEventKind, Vector3, int, byte>? onInteraction = null)
        {
            var screenPos = Raylib.GetMousePosition();
            var worldPos  = Raylib.GetScreenToWorld2D(screenPos, camera);

            // ---- Scan for exclusive InputCaptureBinding --------------------------------
            // When found, route all raw HW events to the capturing token and skip
            // normal spatial hit-testing. The gizmo declares intent; the terminal obeys.
            for (int i = 0; i < primitives.Length; i++)
            {
                ref readonly var prim = ref primitives[i];
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
            foreach (ref readonly var prim in primitives)
            {
                if (prim.Shape == DebugPrimitiveShape.ContextMenuBinding)
                    menuBindings[prim.InspNetworkId] = prim.StringHash;
            }

            // ---- Try to start a new interaction on left press ----------------------------
            if (_activeTool == null && Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                var best = FindTopmostInteractivePrimitive(primitives, worldPos, camera.Zoom);
                if (best.HasValue)
                {
                    var hit = best.Value;
                    long anchorId = hit.BoxAnchorId != 0
                        ? hit.BoxAnchorId
                        : (hit.InspNetworkId != 0 ? hit.InspNetworkId : hit.AnchorIndex);
                    var token = new GizmoPickToken
                    {
                        AnchorId = anchorId,
                        SubElementId = hit.SubElementId,
                        StreamId = hit.AnchorGeneration,
                    };
                    _activeTool = new GizmoInteractionProxyTool(
                        token, worldPos, onInteraction, onExit: () => _activeTool = null, hit.Space);
                    _activeTool.HandlePress(worldPos, MouseButton.Left);
                }
                else
                {
                    // Canvas fallback to allow selection-rect interactions.
                    _activeTool = new GizmoInteractionProxyTool(
                        default, worldPos, onInteraction, onExit: () => _activeTool = null);
                    _activeTool.HandlePress(worldPos, MouseButton.Left);
                }
            }

            // ---- Right-click: show context menu for the topmost hit primitive, or canvas ---
            // Falls back to canvas anchor (-1L) when no entity is under the cursor so
            // that empty-space right-clicks resolve through the same ContextMenuBinding pipeline.
            if (_activeTool == null && Raylib.IsMouseButtonReleased(MouseButton.Right))
            {
                long hitEntityId = -1L; // canvas anchor fallback

                var best = FindTopmostInteractivePrimitive(primitives, worldPos, camera.Zoom);
                if (best.HasValue)
                {
                    var hit = best.Value;
                    hitEntityId = hit.BoxAnchorId != 0
                        ? hit.BoxAnchorId
                        : (hit.InspNetworkId != 0 ? hit.InspNetworkId : hit.AnchorIndex);
                }

                if (hitEntityId != 0 && menuBindings.TryGetValue(hitEntityId, out uint menuHash))
                {
                    string? json = internMap.TryResolve(menuHash);
                    if (json != null)
                        _contextMenuAdapter.Schedule(hitEntityId, json);
                }
                else if (hitEntityId != -1L && menuBindings.TryGetValue(-1L, out uint canvasHash))
                {
                    string? json = internMap.TryResolve(canvasHash);
                    if (json != null)
                        _contextMenuAdapter.Schedule(-1L, json);
                }
            }

            // ---- Drive the active drag tool with subsequent mouse state ----------------
            if (_activeTool != null)
            {
                if (Raylib.IsMouseButtonDown(MouseButton.Left))
                {
                    var delta = Raylib.GetMouseDelta();
                    if (delta.X != 0f || delta.Y != 0f)
                        _activeTool.HandleDrag(worldPos, delta);
                }

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

        private static DebugPrimitive? FindTopmostInteractivePrimitive(
            ReadOnlySpan<DebugPrimitive> primitives,
            Vector2 testPos,
            float zoom)
        {
            DebugPrimitive? best = null;
            float effZoom = zoom > 0f ? zoom : 1f;

            for (int i = primitives.Length - 1; i >= 0; i--)
            {
                ref readonly var prim = ref primitives[i];
                if (prim.Shape == DebugPrimitiveShape.InputCaptureBinding || prim.Shape == DebugPrimitiveShape.ContextMenuBinding) continue;

                bool hasAnchor = prim.InspNetworkId != 0 || prim.AnchorIndex != 0 || prim.SubElementId != 0 || prim.BoxAnchorId != 0;
                if (!hasAnchor) continue;

                float hitRadius = prim.SizeMode == SizeMode.ScreenPixels ? 5f / effZoom : 5f;
                bool hit = false;

                if (prim.Shape == DebugPrimitiveShape.Box2D)
                {
                    float dx = Math.Abs(testPos.X - prim.BoxCenterX);
                    float dy = Math.Abs(testPos.Y - prim.BoxCenterY);
                    hit = dx <= prim.BoxExtentX && dy <= prim.BoxExtentY;
                }
                else if (prim.Shape == DebugPrimitiveShape.Sphere)
                {
                    float distSq = Vector2.DistanceSquared(testPos, new Vector2(prim.SphereCenter.X, prim.SphereCenter.Y));
                    float r = prim.SphereRadius + hitRadius;
                    hit = distSq <= r * r;
                }

                if (hit && (best == null || prim.DebugLayer > best.Value.DebugLayer))
                    best = prim;
            }

            return best;
        }
    }
}

