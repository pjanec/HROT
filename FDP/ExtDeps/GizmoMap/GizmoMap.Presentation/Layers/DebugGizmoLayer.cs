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

        // ⭐⭐⭐ CE-259n — a raw RELEASE is only delivered when its own PRESS was (see RawButtonGate).
        //   ⛔ Per button, and held across frames: "is a press of THIS button outstanding" is not a
        //   per-frame fact. Without these, a right-click on an ImGui PANEL ended a map gizmo, because the
        //   press was correctly withheld and the release was sent anyway.
        private RawButtonGate _leftRaw;
        private RawButtonGate _rightRaw;
        private const float RightDragThresholdSq = 25f;

        // Main menu aggregator: collects MainMenuBinding primitives each frame.
        private readonly MainMenuAdapter _mainMenuAdapter = new();

        public DebugGizmoLayer(DebugPrimitiveRenderer2D renderer)
        {
            _renderer = renderer;
        }

        /// <summary>
        /// Optional resolver that renders a colored icon for right-click context-menu items
        /// carrying an <c>"icon"</c> key. Injected by the host (which owns the icon vocabulary);
        /// null (default) renders text-only.
        /// </summary>
        public MenuIconResolver? ContextMenuIconResolver
        {
            get => _contextMenuAdapter.IconResolver;
            set => _contextMenuAdapter.IconResolver = value;
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

            // ⭐⭐⭐ S5 (DESIGN_Gizmo_Anchor_Identity.md §6) — THE CAPTURE BINDING IS KEYED BY ONE ID.
            //   ⛔ HISTORY, and it is why the generation is no longer read here. S0 first patched the
            //     filter to compare (value, generation) because the comparison used only the VALUE, so a
            //     click leaked past an exclusive tool to whichever entity's ECS index equalled the active
            //     tool's id -- GlobalGizmoManager keyed its binding by a TOOL id from NewId() (1, 2, 3...)
            //     while an entity pick box routed its ECS AnchorIndex, the same small-integer range.
            //   ⭐ S5 removed the CAUSE instead: identity is now the network id on both sides
            //     (DebugPrimitive.BoxAnchorId / InputCaptureBinding.StructNetworkId), and tool ids are
            //     allocated from a DISJOINT high range (GlobalGizmoManager.ToolAnchorIdBase, §6.1).
            //   ⇒ one id space, one comparison. Re-adding a generation term would reintroduce the
            //     two-domain thinking this step deleted.
            long? exclusiveAnchorId = null;
            bool routeRawInput = false;
            var captureToken = default(GizmoPickToken);
            
            for (int i = 0; i < primitives.Length; i++)
            {
                ref readonly var prim = ref primitives[i];
                if (prim.Shape != DebugPrimitiveShape.InputCaptureBinding) continue;
                if ((prim.ConditionMask & 1u) != 0)
                    exclusiveAnchorId = prim.StructNetworkId;
                if ((prim.ConditionMask & 2u) != 0) routeRawInput = true;
                captureToken = new GizmoPickToken
                {
                    AnchorId     = prim.StructNetworkId,   // ⭐ S5 — IDENTITY: network id (or a tool id)
                    SubElementId = prim.SubElementId,
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
                    
                    var token = MakePickToken(in hit);
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

                        var token = MakePickToken(in hit);
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
                // ---- Modifier Packing (Zero-Allocation Backend Context) ----
                // We explicitly poll modifiers every frame and pack them as bitmasks directly
                // into the event payload. This allows the backend tools to evaluate interaction rules
                // statelessly without maintaining an asynchronous key-state dictionary.
                int modifiers = 0;
                if (Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift))
                    modifiers |= (int)MapKeyboardKey.ShiftMask;
                if (Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl))
                    modifiers |= (int)MapKeyboardKey.CtrlMask;
                if (Raylib.IsKeyDown(KeyboardKey.LeftAlt) || Raylib.IsKeyDown(KeyboardKey.RightAlt))
                    modifiers |= (int)MapKeyboardKey.AltMask;

                // Only send raw PRESSED events if ImGui doesn't want the mouse...
                if (Raylib.IsMouseButtonPressed(MouseButton.Left))
                {
                    if (_leftRaw.OnPress(isMouseCaptured))
                        onInteraction?.Invoke(captureToken, GizmoInteractionEventKind.RawInput,
                            worldPos3, (int)MapMouseButton.Left | modifiers, 0x81);
                }
                // ...and send the release whenever ITS OWN PRESS was delivered — wherever the pointer has
                // since travelled. ⭐ That still prevents the stuck backend input queue the original
                // comment guarded (press on map, release over a panel ⇒ delivered), ⛔ while no longer
                // handing a gizmo a release it never earned (press swallowed by a panel ⇒ suppressed).
                //   🔴 CE-259n: VertexEditGizmo treats a right-RELEASE as "commit and exit", so an
                //   unpaired one destroyed the edit on any panel right-click.
                else if (Raylib.IsMouseButtonReleased(MouseButton.Left))
                {
                    if (_leftRaw.OnRelease())
                        onInteraction?.Invoke(captureToken, GizmoInteractionEventKind.RawInput,
                            worldPos3, (int)MapMouseButton.Left | modifiers, 0x80);
                }

                if (Raylib.IsMouseButtonPressed(MouseButton.Right))
                {
                    if (_rightRaw.OnPress(isMouseCaptured))
                        onInteraction?.Invoke(captureToken, GizmoInteractionEventKind.RawInput,
                            worldPos3, (int)MapMouseButton.Right | modifiers, 0x81);
                }
                else if (Raylib.IsMouseButtonReleased(MouseButton.Right))
                {
                    // ⚠ !contextMenuOpened is PRESERVED: the map's own canvas menu consumes the release.
                    if (_rightRaw.OnRelease() && !contextMenuOpened)
                        onInteraction?.Invoke(captureToken, GizmoInteractionEventKind.RawInput,
                            worldPos3, (int)MapMouseButton.Right | modifiers, 0x80);
                }

                // ---- Generic Input Queue ----
                // Raylib's GetKeyPressed() only queues *printable character presses*.
                // We stream these blindly over the boundary.
                int key;
                while ((key = Raylib.GetKeyPressed()) != 0)
                {
                    if (!isKeyboardCaptured)
                    {
                        onInteraction?.Invoke(captureToken, GizmoInteractionEventKind.RawInput,
                            worldPos3, key | modifiers, 0x01);
                    }
                }

                // =====================================================================
                // HARDWARE ABSTRACTION WORKAROUND: STRUCTURAL KEY POLLING
                // =====================================================================
                // Raylib's GetKeyPressed() completely ignores non-printable structural keys
                // (Escape, Tab, Delete, Enter) and provides absolutely no queue for *release* events.
                //
                // We must explicitly poll IsKeyReleased for these structural keys to guarantee
                // the decoupled backend state machines receive the 0x00 (release) payload.
                // Without this explicit polling, a remote backend tool would permanently hang
                // waiting for a key-up event that the windowing library failed to queue.
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

                    // We also explicitly poll modifier presses/releases so backend tools
                    // that use them as hotkeys (e.g. holding Shift to snap to grid) receive the transitions.
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

        public void DrawStructInspector(Action<long, uint, string>? onStructUpdate = null)
        {
            _renderer.DrawStructInspector(onStructUpdate);
        }

        /// <summary>
        /// ⭐⭐⭐ <b>The UNFILTERED spatial hit-test — <c>CE-259p</c>.</b>
        /// 📄 <c>docs/UX/UX_Feature_Tool_Model.md</c> §4.7g.
        ///
        /// <para>🔴 <b>Why this is public:</b> <c>IMapLayer.PickEntity</c> is implemented as <c>=> null</c>
        /// by <b>every</b> production layer in the repo, so <c>MapCanvas.PickTopmostEntity</c> always
        /// yielded <c>null</c> and <c>EntityPickerGizmo</c> — whose whole pick arm is gated on that
        /// hit-test — could never pick anything. Meanwhile the terminal has had a real, working hit-test
        /// all along: <c>FindTopmostInteractivePrimitive</c>, the one that makes ordinary SELECTION work.
        /// ⇒ ⭐ this exposes the LIVE mechanism instead of adding a second one (seam law).</para>
        ///
        /// <para>⭐⭐ <b>Deliberately UNFILTERED by the capture binding.</b> A picker holds exclusive focus,
        /// and <see cref="HandleInput"/> uses <c>exclusiveAnchorId</c> so that nothing ELSE starts an
        /// interaction underneath it. ⛔ But the picker itself must be able to see what it is pointing at —
        /// that is its entire job. ⇒ *"only the capture holder receives interactions"* and *"the capture
        /// holder may hit-test"* are compatible, and conflating them is what made the picker blind.</para>
        ///
        /// <para>⭐⭐⭐ <b>§6.7, 2026-09-11 — RETURNS THE NETWORK ID.</b> ⛔ It used to return
        /// <c>(int Index, ushort Generation)</c> — the hit primitive's ECS handle, which the caller turned
        /// straight back into an <c>Entity</c>. ⇒ a process-local handle crossing an assembly boundary
        /// that exists to be ECS-free. Now it answers with the anchor's IDENTITY and the caller resolves
        /// it in its own world (📄 <c>docs/DESIGN_Gizmo_Anchor_Identity.md</c> §6.7).</para>
        ///
        /// <para>⚠ Yields a result only for a primitive carrying a real anchor id
        /// (<c>BoxAnchorId != 0</c>). ⛔ A sub-element-only handle yields <see langword="null"/> rather
        /// than a fabricated identity. ⚠ A TOOL id (≥ <c>1&lt;&lt;40</c>, the disjoint range of §6.1) is
        /// returned as-is and simply resolves to no entity — which is the correct answer for it.</para>
        ///
        /// <para>⛔ <b>Returns an id, not an <c>Entity</c>, on purpose:</b> this project is deliberately
        /// decoupled from <c>Fdp.Core</c> (see the type header).</para>
        /// </summary>
        public static long? PickTopmostAnchorId(
            ReadOnlySpan<DebugPrimitive> primitives, Vector2 worldPos, float zoom)
        {
            var best = FindTopmostInteractivePrimitive(primitives, worldPos, zoom, exclusiveAnchorId: null);
            if (!best.HasValue) return null;

            long id = best.Value.BoxAnchorId;
            return id != 0 ? id : (long?)null;
        }

        /// <summary>
        /// ⭐⭐⭐ <b>S5 (DESIGN_Gizmo_Anchor_Identity.md §6) — ONE ID, AND IT IS THE NETWORK ID.</b>
        /// The single place a hit primitive becomes a <see cref="GizmoPickToken"/>.
        ///
        /// <para>⛔ Both call sites in <see cref="HandleInput"/> used to build the token inline as
        /// <c>anchorId = AnchorGeneration != 0 ? AnchorIndex : BoxAnchorId</c> — a PROCESS-LOCAL ECS index
        /// in a field <c>GizmoPickToken.cs:8</c> documents as a *"NetworkId / semantic object id"*. That is
        /// defect <c>D2</c> of the design, and having it written twice is how the left-press arm kept the
        /// old behaviour after the right-click arm was fixed.</para>
        ///
        /// <para>⭐⭐ <b>§6.7, 2026-09-11 — THE ECS PAYLOAD IS GONE.</b> This used to also copy
        /// <c>hit.AnchorIndex</c> and <c>hit.AnchorGeneration</c> into the token so a consumer could
        /// rebuild an <c>Entity</c> with no lookup. ⛔ The token now carries <b>only</b> the network id;
        /// each consumer resolves it in its own world. See <c>GizmoPickToken.cs</c> for why the payload's
        /// justification did not survive measurement.</para>
        ///
        /// <para>⭐ Public so a rail can assert it without a live window — <see cref="HandleInput"/> needs
        /// Raylib. ⛔ A test that RE-IMPLEMENTS this is blind to exactly the bug above.</para>
        /// </summary>
        public static GizmoPickToken MakePickToken(in DebugPrimitive hit) => new GizmoPickToken
        {
            AnchorId     = hit.BoxAnchorId,        // ⭐ IDENTITY: the network id (or a disjoint tool id)
            SubElementId = hit.SubElementId,
            GizmoTypeId  = hit.GizmoTypeId,
        };

        /// <summary>⭐ Test seam: the disjoint tool-anchor-id range (§6.1), without a Fdp.Toolkits reference.</summary>
        public static long ToolCaptureIdForTests(int n) => (1L << 40) + n;

        /// <summary>
        /// ⭐⭐ <b>Test seam for the exclusive-capture filter (S0/S5).</b> Mirrors
        /// <see cref="PickTopmostAnchorId"/> -- which exists for the same reason -- but lets a rail
        /// supply the capture binding that <see cref="HandleInput"/> would have scanned out of the frame.
        ///
        /// <para>⛔ Without this the filter is unreachable from a test: the public entry point hard-codes
        /// <c>exclusiveAnchorId: null</c> and <see cref="HandleInput"/> needs a live window.</para>
        /// </summary>
        public static long? PickTopmostAnchorIdUnderCapture(
            ReadOnlySpan<DebugPrimitive> primitives, Vector2 worldPos, float zoom,
            long? exclusiveAnchorId)
        {
            var best = FindTopmostInteractivePrimitive(primitives, worldPos, zoom, exclusiveAnchorId);
            if (!best.HasValue) return null;

            long id = best.Value.BoxAnchorId;
            return id != 0 ? id : (long?)null;
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

                // ⭐⭐⭐ S5 (DESIGN_Gizmo_Anchor_Identity.md §6) — ONE ID, AND IT IS THE NETWORK ID.
                //   ⛔ This used to multiplex two addressing domains:
                //        anchorId = AnchorGeneration != 0 ? AnchorIndex : BoxAnchorId
                //     ...and then compare only the VALUE, so a TOOL id matched an entity whose ECS index
                //     happened to equal it (S0 patched that by also comparing the generation).
                //   ⭐ Identity is now BoxAnchorId on BOTH sides: every entity primitive stamps its network
                //     id there (EntityPresentationGizmoShared.EmitPickBox, and the tool handles likewise),
                //     and a binding carries the same id in StructNetworkId. Tool ids come from a DISJOINT
                //     range so the single space stays unambiguous (GlobalGizmoManager.ToolAnchorIdBase).
                //   ⇒ S0's generation term is GONE: with one id space it adds nothing and reintroduces the
                //     two-domain thinking this step removes.
                long anchorId = prim.BoxAnchorId;
                if (exclusiveAnchorId.HasValue && anchorId != exclusiveAnchorId.Value) continue;

                float hitRadius = prim.SizeMode == SizeMode.ScreenPixels ? 5f / effZoom : 5f;
                bool hit = false;

                if (prim.Shape == DebugPrimitiveShape.Box2D)
                {
                    // ⭐⭐⭐ CE-259ac — AN ORIENTED-BOX TEST. The renderer has ALWAYS drawn Box2D rotated
                    //   (DebugPrimitiveRenderer2D.cs:296 Raylib.DrawRectanglePro(..., prim.BoxAngleDeg, ...),
                    //   and :139 even composes the anchor's yaw for EntityLocal) while this test compared
                    //   axis-aligned extents. ⇒ 🔴 A ROTATED BOX DREW ROTATED AND PICKED AXIS-ALIGNED:
                    //   draw and pick disagreed, which is a defect in its own right. Latent only because
                    //   no production gizmo had set a non-zero angle yet — and the moment one does, a
                    //   diagonal box's pick area is its bounding square.
                    //   ⭐ It is also what makes A LINE CLICKABLE: a clickable segment IS a thin oriented
                    //     box, so with this the terminal needs no new shape and DebugPrimitive needs no
                    //     new field. See DebugPrimitive.MakePickSegment.
                    //   ⭐ Reduces EXACTLY to the old comparison when BoxAngleDeg == 0.
                    float lx = testPos.X - prim.BoxCenterX;
                    float ly = testPos.Y - prim.BoxCenterY;
                    if (prim.BoxAngleDeg != 0f)
                    {
                        // Rotate the probe INTO box space (i.e. by -angle).
                        float rad = -prim.BoxAngleDeg * (MathF.PI / 180f);
                        float c = MathF.Cos(rad), sn = MathF.Sin(rad);
                        (lx, ly) = (lx * c - ly * sn, lx * sn + ly * c);
                    }
                    hit = Math.Abs(lx) <= (prim.BoxExtentX + hitRadius)
                       && Math.Abs(ly) <= (prim.BoxExtentY + hitRadius);
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
