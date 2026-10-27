using System.Collections.Generic;
using System.Numerics;
using System;
using Raylib_cs;
using Fdp.Core;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis2D.Components;
using Fdp.Toolkit.Vis2D.Input;

namespace Fdp.Toolkit.Vis2D
{
    public class MapCanvas : IResourceProvider
    {
        public MapCamera Camera { get; set; } = new MapCamera();
        public Vis2DInputMap InputMap { get; set; } = Vis2DInputMap.Default;
        public uint ActiveLayerMask { get; set; } = 0xFFFFFFFF;
        public IInputProvider Input => _input;

        /// <summary>
        /// Debug primitive builder injected into every <see cref="RenderContext"/> during
        /// <see cref="Draw"/>. Set by the hosting application after creating the canvas and
        /// before the first draw call. May be null until set.
        /// </summary>
        public Fdp.Toolkit.Diagnostics.Gizmos.IDebugDrawBuilder? DrawBuffer { get; set; }
        
        private readonly IInputProvider _input;

        public MapCanvas(IInputProvider? input = null)
        {
             _input = input ?? new Fdp.Toolkit.Vis2D.Defaults.RaylibInputProvider();
        }

        // Resources
        private readonly Dictionary<Type, object> _resources = new();

        public void AddResource<T>(T resource) where T : class
        {
            _resources[typeof(T)] = resource;
        }

        public T? Get<T>() where T : class
        {
            if (_resources.TryGetValue(typeof(T), out var res))
                return res as T;
            return null;
        }

        public bool Has<T>() where T : class
        {
            return _resources.ContainsKey(typeof(T));
        }

        /// <summary>
        /// Set when the right mouse button has been held and dragged beyond a small
        /// threshold while the camera was panning.  Prevents the pan-release from
        /// being misinterpreted as a right-click (e.g. opening a context menu).
        /// Cleared on every right-button release, after the click check.
        /// </summary>
        private bool _rightButtonDragged = false;
        private const float RightDragThresholdSq = 25f; // 5 px, matches DRAG_THRESHOLD in StandardInteractionTool

        /// <summary>
        /// <c>true</c> when a layer consumed one or more key presses during the
        /// last <see cref="Update"/> call.  Use this in the hosting application to gate
        /// camera or application-level keyboard handling so that layers that capture
        /// specific keys (e.g. ESC) do not inadvertently trigger host-level actions.
        /// Reset to <c>false</c> at the start of every <see cref="ProcessInputPipeline"/> call.
        /// </summary>
        public bool KeyboardConsumedByTool { get; private set; }

        public IReadOnlyList<IMapLayer> Layers => _layers;
        private readonly List<IMapLayer> _layers = new();

        public void AddLayer(IMapLayer layer)
        {
            if (!_layers.Contains(layer))
                _layers.Add(layer);
        }

        public void RemoveLayer(IMapLayer layer)
        {
            _layers.Remove(layer);
        }

        public Entity? PickTopmostEntity(Vector2 worldPos)
        {
            // Iterate reverse (Top -> Bottom)
            for (int i = _layers.Count - 1; i >= 0; i--)
            {
                var layer = _layers[i];
                if (IsLayerVisible(layer))
                {
                    var entity = layer.PickEntity(worldPos);
                    if (entity.HasValue) return entity;
                }
            }
            return null;
        }

        public void Update(float dt)
        {
            // Update Camera Interpolation
            Camera.Update(dt);

            // Handle Input Routing
            ProcessInputPipeline();

            // Update Layers
            foreach (var layer in _layers)
            {
                layer.Update(dt);
            }
        }

        /// <summary>⭐ CE-1033 S1 — in 3-D, the visible layers that have no 3-D path (drawn as a dead edge in the design's module
        /// diagram; reported so their absence is visible). Updated each 3-D frame.</summary>
        public int LayersWithout3D { get; private set; }

        public void Draw()
        {
            // ⭐ CE-1033 S5b (2-D: entity cards) / S3 (M13, 3-D: labels) — the screen-space pass over the finished map
            var ctx = Camera.Is3D ? DrawLayers3D(sceneOnly: false) : DrawLayers2D(sceneOnly: false);
            DrawOverlays(ctx, sceneOnly: false);
        }

        /// <summary>
        /// ⭐ <c>CE-3289</c> (docs/DESIGN_Map_3D_Mode.md §6ab) — the WORLD alone, as a film frame shows it: the visible
        /// <see cref="ISceneLayer"/>s through the SAME layer passes <see cref="Draw"/> runs (no tool, cursor, HUD or panel). Into whatever
        /// target is bound (a render texture).
        /// <para>⭐ <c>CE-1379</c> / <c>CE-1380</c> (docs/DESIGN_Director_Timeline_Editor.md §11t "as built") — in BOTH modes (⛔ was: 3-D only, so a
        /// 2-D film frame took the whole screen pass, cursor readout and HUD included), with the scene layers' screen-space pass (the cards and
        /// labels the film's layer set turns on), and <see cref="RenderContext.Film"/> set so the gizmo layer draws its film layers only.</para>
        /// </summary>
        public void DrawScene()
        {
            var ctx = Camera.Is3D ? DrawLayers3D(sceneOnly: true) : DrawLayers2D(sceneOnly: true);
            DrawOverlays(ctx, sceneOnly: true);
        }

        /// <summary>The screen-space pass both <see cref="Draw"/> and <see cref="DrawScene"/> run (in 3-D only the layers with a 3-D path).</summary>
        private void DrawOverlays(RenderContext ctx, bool sceneOnly)
        {
            foreach (var layer in _layers)
            {
                if (!IsLayerVisible(layer) || (sceneOnly && layer is not ISceneLayer)) continue;
                if (Camera.Is3D && !layer.Has3D) continue;
                layer.DrawOverlay(ctx);
            }
        }

        private RenderContext NewContext(bool film) => new()
        {
            Zoom              = Camera.Zoom,
            MouseWorldPos     = Camera.ScreenToWorld(GetMousePosition()),
            DeltaTime         = GetFrameTime(),
            VisibleLayersMask = ActiveLayerMask,
            Resources         = this,
            DrawBuilder       = DrawBuffer,
            Film              = film,
        };

        /// <summary>The 2-D layer pass (0 → N, bottom to top) both <see cref="Draw"/> and <see cref="DrawScene"/> run (one loop).</summary>
        private RenderContext DrawLayers2D(bool sceneOnly)
        {
            Camera.BeginMode();
            var ctx = NewContext(sceneOnly);
            foreach (var layer in _layers)
                if (IsLayerVisible(layer) && (!sceneOnly || layer is ISceneLayer)) layer.Draw(ctx);
            Camera.EndMode();
            return ctx;
        }

        /// <summary>The 3-D layer pass both <see cref="Draw"/> and <see cref="DrawScene"/> run (one loop).</summary>
        private RenderContext DrawLayers3D(bool sceneOnly)
        {
            Camera.BeginMode();
            var ctx = NewContext(sceneOnly);
            int without = 0;
            foreach (var layer in _layers)
            {
                if (!IsLayerVisible(layer)) continue;
                if (sceneOnly && layer is not ISceneLayer) continue;
                if (layer.Has3D) layer.Draw3D(ctx); else without++;
            }
            if (!sceneOnly) LayersWithout3D = without;
            Camera.EndMode();
            return ctx;
        }

        private bool IsLayerVisible(IMapLayer layer)
        {
            if (layer.LayerBitIndex < 0) return true; // Always visible
            if (layer.LayerBitIndex >= 32) return false; // Out of range

            uint mask = 1u << layer.LayerBitIndex;
            return (ActiveLayerMask & mask) != 0;
        }

        protected virtual void ProcessInputPipeline()
        {
            KeyboardConsumedByTool = false;

            if (_input.IsMouseCaptured) return;

            Vector2 mouseScreen = _input.MousePosition;
            Vector2 mouseWorld = Camera.ScreenToWorld(mouseScreen);
            
            bool leftPressed = _input.IsMouseButtonPressed(MapMouseButton.Left);
            bool rightPressed = _input.IsMouseButtonPressed(MapMouseButton.Right);
            bool leftDown = _input.IsMouseButtonDown(MapMouseButton.Left);
            bool rightDown = _input.IsMouseButtonDown(MapMouseButton.Right);
            bool leftReleased = _input.IsMouseButtonReleased(MapMouseButton.Left);
            bool rightReleased = _input.IsMouseButtonReleased(MapMouseButton.Right);

            Vector2 delta = _input.MouseDelta;
            Vector2 deltaWorld = Camera.ScreenDeltaToWorld(delta);   // ⭐ CE-1033 S1 — the camera knows its own scale (2-D: /Zoom)

            bool consumed = false;
            Vector2? dragWorld = null;

            // Track right-button drags so that pan-then-release does not fire a click.
            if (rightDown && delta.LengthSquared() > RightDragThresholdSq)
                _rightButtonDragged = true;

            // Save right-drag state before reset so 3.5 can check it correctly.
            bool wasRightDragged = _rightButtonDragged;

            // 0. Keyboard routing to layers (highest index = top priority).
            if (!_input.IsKeyboardCaptured)
            {
                int rawKey;
                while ((rawKey = _input.GetKeyPressed()) != 0)
                {
                    for (int i = _layers.Count - 1; i >= 0; i--)
                    {
                        if (_layers[i].HandleKeyInput((MapKeyboardKey)rawKey))
                        {
                            KeyboardConsumedByTool = true;
                            break;
                        }
                    }
                }
            }

            // 1. Hover and drag to layers (informational; no consume semantics for hover).
            for (int i = _layers.Count - 1; i >= 0; i--)
            {
                var layer = _layers[i];
                if (!IsLayerVisible(layer)) continue;
                layer.HandleHover(mouseWorld);
                if ((leftDown || rightDown) && delta.LengthSquared() > 0f)
                {
                    // ⭐ CE-1033 S2 (P2) — a drag follows the terrain under the cursor, never an entity (2-D: the same point).
                    dragWorld ??= Camera.ScreenToWorldForDrag(mouseScreen);
                    if (layer.HandleDrag(dragWorld.Value, deltaWorld))
                        consumed = true;
                }
            }

            // Reset right-drag flag unconditionally so it never leaks across frames.
            if (rightReleased)
                _rightButtonDragged = false;

            // 2. Camera Priority
            if (!consumed)
            {
                if (Camera.HandleInput(_input)) consumed = true;
            }

            // 3. Layer Priority (Reverse) -- press events only.
            if (!consumed)
            {
                for (int i = _layers.Count - 1; i >= 0; i--)
                {
                    var layer = _layers[i];
                    if (!IsLayerVisible(layer)) continue;

                    if (leftPressed)
                    {
                        if (layer.HandleInput(mouseWorld, MapMouseButton.Left, true)) return;
                    }
                    if (rightPressed)
                    {
                        if (layer.HandleInput(mouseWorld, MapMouseButton.Right, true)) return;
                    }
                }
            }

            // 3.5. Layer release routing (for interaction commit/cancel).
            if (leftReleased || (rightReleased && !wasRightDragged))
            {
                for (int i = _layers.Count - 1; i >= 0; i--)
                {
                    var layer = _layers[i];
                    if (!IsLayerVisible(layer)) continue;

                    if (leftReleased)
                    {
                        if (layer.HandleInput(mouseWorld, MapMouseButton.Left, false)) break;
                    }
                    if (rightReleased && !wasRightDragged)
                    {
                        if (layer.HandleInput(mouseWorld, MapMouseButton.Right, false)) break;
                    }
                }
            }
        }

        // Virtual for testing
        protected virtual Vector2 GetMousePosition() => _input.MousePosition;
        protected virtual float GetFrameTime() => Raylib.GetFrameTime();
        protected virtual bool IsMouseButtonPressed(MapMouseButton button) => _input.IsMouseButtonPressed(button);
        protected virtual bool IsMouseButtonDown(MapMouseButton button) => _input.IsMouseButtonDown(button);
        protected virtual Vector2 GetMouseDelta() => _input.MouseDelta;
        protected virtual bool IsMouseCaptured() => _input.IsMouseCaptured;
    }
}
