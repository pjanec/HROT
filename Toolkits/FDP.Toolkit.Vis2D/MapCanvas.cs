using System.Collections.Generic;
using System.Numerics;
using System;
using Raylib_cs;
using Fdp.Kernel;
using FDP.Toolkit.Vis2D.Abstractions;
using FDP.Toolkit.Vis2D.Components;
using FDP.Toolkit.Vis2D.Input;

namespace FDP.Toolkit.Vis2D
{
    public class MapCanvas : IResourceProvider
    {
        public MapCamera Camera { get; set; } = new MapCamera();
        public Vis2DInputMap InputMap { get; set; } = Vis2DInputMap.Default;
        public uint ActiveLayerMask { get; set; } = 0xFFFFFFFF;
        public IInputProvider Input => _input;
        
        private readonly IInputProvider _input;

        public MapCanvas(IInputProvider? input = null)
        {
             _input = input ?? new FDP.Toolkit.Vis2D.Defaults.RaylibInputProvider();
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

        // Backing field for ActiveTool to allow private set
        public IMapTool? ActiveTool
        {
             get => _toolStack.Count > 0 ? _toolStack.Peek() : null;
             // Set is removed, use SwitchTool/PushTool
        }

        private readonly Stack<IMapTool> _toolStack = new();
        private bool _isSwitching = false;
        
        // Input state tracking to separate Click from Drag
        private bool _isDraggingInteraction = false;

        /// <summary>
        /// <c>true</c> when the active tool consumed one or more key presses during the
        /// last <see cref="Update"/> call.  Use this in the hosting application to gate
        /// camera or application-level keyboard handling so that tools that capture
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

        /// <summary>
        /// Clears the tool stack and sets the new tool as the base.
        /// Use this for major mode switches.
        /// </summary>
        public void SwitchTool(IMapTool? tool)
        {
            if (_isSwitching) return; // Prevent recursion loops
            
            // Check if we are already effective
            // if (ActiveTool == tool) return; // Hard to check with stack clearing semantics

            _isSwitching = true;
            try
            {
                // Exit all tools in stack from top to bottom
                while (_toolStack.Count > 0)
                {
                    var t = _toolStack.Pop();
                    t.OnExit();
                }
                
                if (tool != null)
                {
                    _toolStack.Push(tool);
                    tool.OnEnter(this);
                }
            }
            finally
            {
                _isSwitching = false;
            }
        }
        
        /// <summary>
        /// Pushes a new tool onto the stack (e.g. starting a sub-task).
        /// The previous tool is suspended (OnExit *is* called?).
        /// Convention: OnExit is usually called when losing focus.
        /// </summary>
        public void PushTool(IMapTool tool)
        {
             if (_isSwitching) return;
             _isSwitching = true;
             try 
             {
                 var current = ActiveTool;
                 // We choose NOT to call OnExit on the suspended tool? 
                 // Or we DO call OnExit, and OnEnter when it returns?
                 // Standard state machine: Exit old, Enter new.
                 if (current != null) current.OnExit();
                 
                 _toolStack.Push(tool);
                 tool.OnEnter(this);
             }
             finally { _isSwitching = false; }
        }
        
        /// <summary>
        /// Pops the current tool and returns to the previous one.
        /// </summary>
        public void PopTool()
        {
            if (_isSwitching) return;
            if (_toolStack.Count == 0) return;
            
            _isSwitching = true;
            try
            {
                var current = _toolStack.Pop();
                current.OnExit();
                
                var prev = ActiveTool;
                if (prev != null) prev.OnEnter(this);
            }
            finally { _isSwitching = false; }
        }

        public void ResetTool()
        {
            SwitchTool(null);
        }

        public void Update(float dt)
        {
            // Update Camera Interpolation
            Camera.Update(dt);

            // Update Layers
            foreach (var layer in _layers)
            {
                layer.Update(dt);
            }

            // Update Tool
            if (ActiveTool != null)
                ActiveTool.Update(dt);
            
            // Handle Input Routing
            ProcessInputPipeline();
        }

        public void Draw()
        {
            Camera.BeginMode();

            var ctx = new RenderContext
            {
                Camera = Camera.InnerCamera,
                MouseWorldPos = Camera.ScreenToWorld(GetMousePosition()),
                DeltaTime = GetFrameTime(),
                VisibleLayersMask = ActiveLayerMask,
                Resources = this
            };

            // Draw Layers (0 -> N) - Bottom to Top
            foreach (var layer in _layers)
            {
                // Verify visibility
                if (IsLayerVisible(layer))
                {
                    layer.Draw(ctx);
                }
            }
            
            // Draw Tool Overlay (Topmost)
            if (ActiveTool != null)
            {
                ActiveTool.Draw(ctx);
            }

            Camera.EndMode();
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
            
            bool leftPressed = _input.IsMouseButtonPressed(MouseButton.Left);
            bool rightPressed = _input.IsMouseButtonPressed(MouseButton.Right);
            bool leftDown = _input.IsMouseButtonDown(MouseButton.Left);
            bool rightDown = _input.IsMouseButtonDown(MouseButton.Right);
            bool leftReleased = _input.IsMouseButtonReleased(MouseButton.Left);
            bool rightReleased = _input.IsMouseButtonReleased(MouseButton.Right);

            Vector2 delta = _input.MouseDelta;
            Vector2 deltaWorld = delta * (1.0f / Camera.Zoom);

            bool consumed = false;

            // 0. Keyboard routing to the active tool.
            // Drain the Raylib key-press queue and route each key to the active tool
            // before handling mouse input so that a Cancel (ESC) takes effect this frame.
            if (!_input.IsKeyboardCaptured && ActiveTool != null)
            {
                int rawKey;
                while ((rawKey = _input.GetKeyPressed()) != 0)
                {
                    if (ActiveTool.HandleKeyPressed((KeyboardKey)rawKey))
                        KeyboardConsumedByTool = true;
                }
            }

            // 1. Tool Priority
            if (ActiveTool != null)
            {
                // Hover
                ActiveTool.HandleHover(mouseWorld);

                // Drag
                if (leftDown || rightDown)
                {
                    if (ActiveTool.HandleDrag(mouseWorld, deltaWorld))
                    {
                        consumed = true;
                        _isDraggingInteraction = true;
                    }
                }

                // Click (Release)
                if (!_isDraggingInteraction)
                {
                    if (leftReleased) 
                    {
                        if (ActiveTool.HandleClick(mouseWorld, MouseButton.Left)) consumed = true;
                    }
                    if (rightReleased && !consumed)
                    {
                        if (ActiveTool.HandleClick(mouseWorld, MouseButton.Right)) consumed = true;
                    }
                }

                // Reset Drag State
                if (leftReleased || rightReleased)
                {
                    _isDraggingInteraction = false;
                }
            }

            // 2. Camera Priority
            if (!consumed)
            {
                if (Camera.HandleInput(_input)) consumed = true;
            }

            // 3. Layer Priority (Reverse)
            if (!consumed)
            {
                for (int i = _layers.Count - 1; i >= 0; i--)
                {
                    var layer = _layers[i];
                    if (!IsLayerVisible(layer)) continue;

                    // Support acting on Pressed
                    if (leftPressed)
                    {
                        if (layer.HandleInput(mouseWorld, MouseButton.Left, true)) return;
                    }
                    if (rightPressed)
                    {
                        if (layer.HandleInput(mouseWorld, MouseButton.Right, true)) return;
                    }
                }
            }
        }

        // Virtual for testing
        protected virtual Vector2 GetMousePosition() => _input.MousePosition;
        protected virtual float GetFrameTime() => Raylib.GetFrameTime();
        protected virtual bool IsMouseButtonPressed(MouseButton button) => _input.IsMouseButtonPressed(button);
        protected virtual bool IsMouseButtonDown(MouseButton button) => _input.IsMouseButtonDown(button);
        protected virtual Vector2 GetMouseDelta() => _input.MouseDelta;
        protected virtual bool IsMouseCaptured() => _input.IsMouseCaptured;
    }
}
