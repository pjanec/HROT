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
        private Vector2 _rightPressScreenPos;
        private bool _rightWasDragged;
        private const float RightDragThresholdSq = 25f;

        // Main menu aggregator: collects MainMenuBinding primitives each frame.
        private readonly MainMenuAdapter _mainMenuAdapter = new();

        public DebugGizmoLayer(DebugPrimitiveRenderer2D renderer)
        {
            _renderer = renderer;
        }

        public void Render(ReadOnlySpan<DebugPrimitive> primitives, Camera2D camera, float zoom)
        {
            _renderer.Render(primitives, camera, zoom);
        }

        public void ExtractMetaPrimitives(ReadOnlySpan<DebugPrimitive> primitives, StringInternMap internMap)
        {
            foreach (ref readonly var prim in primitives)
            {
                if (prim.Shape == DebugPrimitiveShape.MainMenuBinding)
                {
                    string? json = internMap.TryResolve(prim.StringHash);
                    if (json != null)
                        _mainMenuAdapter.Schedule(json);
                }
                else if (prim.Shape == DebugPrimitiveShape.ContextMenuBinding)
                {
                    // (Optional) Menu hashes can also be cached here if needed by the terminal
                }
            }
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
            var worldPos3 = new Vector3(worldPos.X, worldPos.Y, 0f);
            var delta = Raylib.GetMouseDelta();
            
            // FIX: Respect ImGui hardware capture state
            bool isMouseCaptured = ImGuiNET.ImGui.GetIO().WantCaptureMouse;
            bool isKeyboardCaptured = ImGuiNET.ImGui.GetIO().WantCaptureKeyboard;

            long? exclusiveAnchorId = null;
            bool routeRawInput = false;
            var captureToken = default(GizmoPickToken);
            
            for (int i = 0; i < primitives.Length; i++)
            {
                ref readonly var prim = ref primitives[i];
                if (prim.Shape != DebugPrimitiveShape.InputCaptureBinding) continue;
                if ((prim.ConditionMask & 1u) != 0) exclusiveAnchorId = prim.StructNetworkId;
                if ((prim.ConditionMask & 2u) != 0) routeRawInput = true;
                captureToken = new GizmoPickToken
                {
                    AnchorId = prim.StructNetworkId,
                    SubElementId = prim.SubElementId,
                    StreamId = prim.AnchorGeneration,
                };
                break;
            }

            if (Raylib.IsMouseButtonPressed(MouseButton.Right))
            {
                _rightPressScreenPos = screenPos;
                // If ImGui captured the press, treat it as already dragged so it never triggers a canvas menu upon release
                _rightWasDragged = isMouseCaptured;
            }

            if (Raylib.IsMouseButtonDown(MouseButton.Right) &&
                Vector2.DistanceSquared(_rightPressScreenPos, screenPos) > RightDragThresholdSq)
            {
                _rightWasDragged = true;
            }

            // ---- Build menu bindings dictionary from ContextMenuBinding meta-primitives ---
            var menuBindings = new Dictionary<long, uint>();
            foreach (ref readonly var prim in primitives)
            {
                if (prim.Shape == DebugPrimitiveShape.ContextMenuBinding)
                    menuBindings[prim.StructNetworkId] = prim.StringHash;
            }

            // ---- Try to start a new interaction on left press ----------------------------
            // Gate activation: ignore if ImGui is capturing the mouse
            if (_activeTool == null && !isMouseCaptured && Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                var best = FindTopmostInteractivePrimitive(primitives, worldPos, camera.Zoom, exclusiveAnchorId);
                if (best.HasValue)
                {
                    var hit = best.Value;
                    
                    // We multiplex two distinct addressing domains inside the fixed 64-byte payload.
                    // If AnchorGeneration != 0, the primitive is bound to a live local ECS entity. We route the
                    // local AnchorIndex so the engine can reconstruct the exact ECS memory handle.
                    // If AnchorGeneration == 0, the primitive is a stateless tool handle or remote network object.
                    // We fall back to the 64-bit BoxAnchorId to route the global network ID or tool ID.
                    long anchorId = hit.AnchorGeneration != 0 ? hit.AnchorIndex : hit.BoxAnchorId;
                    
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
                else if (!exclusiveAnchorId.HasValue)
                {
                    // Canvas fallback to allow selection-rect interactions.
                    _activeTool = new GizmoInteractionProxyTool(
                        default, worldPos,
                        onInteraction, onExit: () => _activeTool = null);
                    _activeTool.HandlePress(worldPos, MouseButton.Left);
                }
            }

            // ---- Right-click: show context menu for the topmost hit primitive, or canvas ---
            bool contextMenuOpened = false;
            if (_activeTool == null && Raylib.IsMouseButtonReleased(MouseButton.Right))
            {
                bool suppressMenu = _rightWasDragged;
                _rightWasDragged = false;
                
                // Block context menu if ImGui currently captures the mouse
                if (!suppressMenu && !isMouseCaptured)
                {
                    long hitNetworkId = -1L; // canvas anchor fallback

                    var best = FindTopmostInteractivePrimitive(primitives, worldPos, camera.Zoom, exclusiveAnchorId);
                    if (best.HasValue)
                    {
                        var hit = best.Value;
                        hitNetworkId = hit.BoxAnchorId != 0 ? hit.BoxAnchorId : -1L;
                        
                        long anchorId = hit.AnchorGeneration != 0 ? hit.AnchorIndex : hit.BoxAnchorId;
                        
                        var token = new GizmoPickToken
                        {
                            AnchorId = anchorId,
                            SubElementId = hit.SubElementId,
                            StreamId = hit.AnchorGeneration,
                        };
                        onInteraction?.Invoke(token, GizmoInteractionEventKind.Started, worldPos3, 0, 0);
                    }

                    if (exclusiveAnchorId.HasValue && hitNetworkId != exclusiveAnchorId.Value)
                        hitNetworkId = 0;

                    if (hitNetworkId != 0 && hitNetworkId != -1L && menuBindings.TryGetValue(hitNetworkId, out uint menuHash))
                    {
                        string? json = internMap.TryResolve(menuHash);
                        if (json != null)
                        {
                            _contextMenuAdapter.Schedule(hitNetworkId, json);
                            contextMenuOpened = true;
                        }
                    }
                    else if (hitNetworkId == -1L && menuBindings.TryGetValue(-1L, out uint canvasHash))
                    {
                        string? json = internMap.TryResolve(canvasHash);
                        if (json != null)
                        {
                            _contextMenuAdapter.Schedule(-1L, json);
                            contextMenuOpened = true;
                        }
                    }
                }
            }

            // ---- Drive the active drag tool with subsequent mouse state ----------------
            // If the tool is already active, it receives input updates (releases/drags) even
            // if the mouse strays over ImGui, otherwise drags would get stuck.
            if (_activeTool != null)
            {
                if (Raylib.IsMouseButtonDown(MouseButton.Left))
                {
                    if (delta.X != 0f || delta.Y != 0f)
                        _activeTool.HandleDrag(worldPos, delta);
                }

                if (Raylib.IsMouseButtonReleased(MouseButton.Left))
                    _activeTool.HandleClick(worldPos, MouseButton.Left);

                if (Raylib.IsMouseButtonReleased(MouseButton.Right))
                    _activeTool.HandleClick(worldPos, MouseButton.Right);

                if (!isKeyboardCaptured && Raylib.IsKeyPressed(KeyboardKey.Escape))
                    _activeTool.HandleKeyPressed(KeyboardKey.Escape);
            }

            if ((exclusiveAnchorId.HasValue || routeRawInput) && (delta.X != 0f || delta.Y != 0f))
            {
                onInteraction?.Invoke(captureToken, GizmoInteractionEventKind.DragUpdate, worldPos3, 0, 0);
            }

            if (routeRawInput)
            {
                int modifiers = 0;
                if (Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift))
                    modifiers |= (int)MapKeyboardKey.ShiftMask;
                if (Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl))
                    modifiers |= (int)MapKeyboardKey.CtrlMask;
                if (Raylib.IsKeyDown(KeyboardKey.LeftAlt) || Raylib.IsKeyDown(KeyboardKey.RightAlt))
                    modifiers |= (int)MapKeyboardKey.AltMask;

                // Only send raw PRESSED events if ImGui doesn't want the mouse...
                if (!isMouseCaptured && Raylib.IsMouseButtonPressed(MouseButton.Left))
                    onInteraction?.Invoke(captureToken, GizmoInteractionEventKind.RawInput,
                        worldPos3, (int)MapMouseButton.Left | modifiers, 0x81);
                // ...but ALWAYS send released events to prevent stuck backend input queues.
                else if (Raylib.IsMouseButtonReleased(MouseButton.Left))
                    onInteraction?.Invoke(captureToken, GizmoInteractionEventKind.RawInput,
                        worldPos3, (int)MapMouseButton.Left | modifiers, 0x80);

                if (!isMouseCaptured && Raylib.IsMouseButtonPressed(MouseButton.Right))
                    onInteraction?.Invoke(captureToken, GizmoInteractionEventKind.RawInput,
                        worldPos3, (int)MapMouseButton.Right | modifiers, 0x81);
                else if (!contextMenuOpened && Raylib.IsMouseButtonReleased(MouseButton.Right))
                    onInteraction?.Invoke(captureToken, GizmoInteractionEventKind.RawInput,
                        worldPos3, (int)MapMouseButton.Right | modifiers, 0x80);

                int key;
                while ((key = Raylib.GetKeyPressed()) != 0)
                {
                    if (!isKeyboardCaptured)
                    {
                        onInteraction?.Invoke(captureToken, GizmoInteractionEventKind.RawInput,
                            worldPos3, key | modifiers, 0x01);
                    }
                }

                if (!isKeyboardCaptured)
                {
                    if (Raylib.IsKeyReleased(KeyboardKey.Escape))
                        onInteraction?.Invoke(captureToken, GizmoInteractionEventKind.RawInput,
                            worldPos3, (int)MapKeyboardKey.Escape | modifiers, 0x00);
                    if (Raylib.IsKeyReleased(KeyboardKey.Enter))
                        onInteraction?.Invoke(captureToken, GizmoInteractionEventKind.RawInput,
                            worldPos3, (int)MapKeyboardKey.Enter | modifiers, 0x00);
                    if (Raylib.IsKeyReleased(KeyboardKey.Delete))
                        onInteraction?.Invoke(captureToken, GizmoInteractionEventKind.RawInput,
                            worldPos3, (int)MapKeyboardKey.Delete | modifiers, 0x00);
                    if (Raylib.IsKeyReleased(KeyboardKey.Tab))
                        onInteraction?.Invoke(captureToken, GizmoInteractionEventKind.RawInput,
                            worldPos3, (int)MapKeyboardKey.Tab | modifiers, 0x00);

                    void RouteMod(KeyboardKey rlKey, MapKeyboardKey mapKey)
                    {
                        if (Raylib.IsKeyPressed(rlKey))
                            onInteraction?.Invoke(captureToken, GizmoInteractionEventKind.RawInput,
                                worldPos3, (int)mapKey | modifiers, 0x01);
                        if (Raylib.IsKeyReleased(rlKey))
                            onInteraction?.Invoke(captureToken, GizmoInteractionEventKind.RawInput,
                                worldPos3, (int)mapKey | modifiers, 0x00);
                    }

                    RouteMod(KeyboardKey.LeftShift, MapKeyboardKey.LeftShift);
                    RouteMod(KeyboardKey.RightShift, MapKeyboardKey.RightShift);
                    RouteMod(KeyboardKey.LeftControl, MapKeyboardKey.LeftControl);
                    RouteMod(KeyboardKey.RightControl, MapKeyboardKey.RightControl);
                    RouteMod(KeyboardKey.LeftAlt, MapKeyboardKey.LeftAlt);
                    RouteMod(KeyboardKey.RightAlt, MapKeyboardKey.RightAlt);
                }
            }
        }

        /// <summary>
        /// Returns the aggregated main-menu items collected from <see cref="DebugPrimitiveShape.MainMenuBinding"/>
        /// primitives during the most recent <see cref="ExtractMetaPrimitives"/> call, then clears internal state.
        /// Pass the returned list to <see cref="ImGuiMenuRenderer.DrawMenus"/> inside a
        /// <c>rlImGui.Begin()</c>/<c>rlImGui.End()</c> block to merge gizmo-provided menus
        /// with the host application menu bar.
        /// </summary>
        public IReadOnlyList<ContextMenuItemDto> ConsumeMainMenu() => _mainMenuAdapter.ConsumeItems();

        /// <summary>
        /// Renders gizmo-contributed items inside the ImGui main menu bar.
        /// Opens a <c>BeginMainMenuBar</c>/<c>EndMainMenuBar</c> block only when items are present.
        /// Must be called inside an <c>rlImGui.Begin()</c>/<c>rlImGui.End()</c> block each frame.
        /// </summary>
        /// <param name="onAction">Callback invoked with the clicked action id.</param>
        public void DrawMainMenu(Action<int>? onAction = null)
        {
            var items = ConsumeMainMenu();
            if (items.Count == 0) return;
            if (!ImGuiNET.ImGui.BeginMainMenuBar()) return;
            ImGuiMenuRenderer.DrawMenus(items, onAction);
            ImGuiNET.ImGui.EndMainMenuBar();
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

        public void DrawStructInspector(Action<long, string>? onStructUpdate = null)
        {
            _renderer.DrawStructInspector(onStructUpdate);
        }

        private static DebugPrimitive? FindTopmostInteractivePrimitive(
            ReadOnlySpan<DebugPrimitive> primitives,
            Vector2 testPos,
            float zoom,
            long? exclusiveAnchorId = null)
        {
            DebugPrimitive? best = null;
            float effZoom = zoom > 0f ? zoom : 1f;

            for (int i = primitives.Length - 1; i >= 0; i--)
            {
                ref readonly var prim = ref primitives[i];
                if (prim.Shape == DebugPrimitiveShape.InputCaptureBinding || prim.Shape == DebugPrimitiveShape.ContextMenuBinding) continue;

                if (prim.AnchorIndex == 0 && prim.SubElementId == 0 && prim.BoxAnchorId == 0) continue;

                // We multiplex two distinct addressing domains inside the fixed 64-byte payload.
                // If AnchorGeneration != 0, the primitive is bound to a live local ECS entity. We route the
                // local AnchorIndex so the engine can reconstruct the exact ECS memory handle.
                // If AnchorGeneration == 0, the primitive is a stateless tool handle or remote network object.
                // We fall back to the 64-bit BoxAnchorId to route the global network ID or tool ID.
                long anchorId = prim.AnchorGeneration != 0 ? prim.AnchorIndex : prim.BoxAnchorId;
                if (exclusiveAnchorId.HasValue && anchorId != exclusiveAnchorId.Value) continue;

                float hitRadius = prim.SizeMode == SizeMode.ScreenPixels ? 5f / effZoom : 5f;
                bool hit = false;

                if (prim.Shape == DebugPrimitiveShape.Box2D)
                {
                    float dx = Math.Abs(testPos.X - prim.BoxCenterX);
                    float dy = Math.Abs(testPos.Y - prim.BoxCenterY);
                    hit = dx <= (prim.BoxExtentX + hitRadius) && dy <= (prim.BoxExtentY + hitRadius);
                }
                else if (prim.Shape == DebugPrimitiveShape.Sphere)
                {
                    float dx = testPos.X - prim.BoxCenterX;
                    float dy = testPos.Y - prim.BoxCenterY;
                    float r = prim.SphereRadius + hitRadius;
                    hit = (dx * dx + dy * dy) <= (r * r);
                }

                if (hit && (best == null || prim.DebugLayer > best.Value.DebugLayer))
                    best = prim;
            }

            return best;
        }
    }
}
