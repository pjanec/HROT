using System;
using System.Collections.Generic;
using System.Numerics;
using Raylib_cs;
using Fdp.Kernel;
using FDP.Toolkit.Vis2D.Abstractions;
using FDP.Toolkit.Vis2D.Input;
using ModuleHost.Core.Abstractions;

namespace FDP.Toolkit.Vis2D.Tools
{
    /// <summary>
    /// The standard "Default" tool that handles:
    /// 1. Clicking to select.
    /// 2. Dragging entities (delegates to EntityDragTool).
    /// 3. Box selection (delegates to BoxSelectionTool).
    /// 4. Global hotkeys (Delete).
    /// 5. Global Actions (Shift+Right Click Waypoints).
    /// </summary>
    public class StandardInteractionTool : IMapTool
    {
        public string Name => "Interaction";

        // Configuration
        private const float DRAG_THRESHOLD = 5.0f;

        // Dependencies
        private readonly ISimulationView _view;
        private readonly EntityQuery _query;
        private readonly IVisualizerAdapter _adapter;
        
        // Callbacks / Action Handlers
        // Generic Events (Decoupled)
        public event Action<Vector2, MouseButton, bool, bool, Entity>? OnWorldClick; // Pos, Button, Shift, Ctrl, HitEntity
        public event Action<Entity, bool>? OnEntitySelectRequest; // Entity, AugmentSelection
        public event Action<List<Entity>>? OnRegionSelected;     // Result of Box Select
        public event Action<Entity, Vector2>? OnEntityMoved;     // Result of Drag Operation

        // State
        private bool _isActionMouseDown; // Was 'Left', now generic based on map
        private Vector2 _mouseDownPos;
        private Entity _potentialTarget = Entity.Null;
        
        private bool _shiftHeld => _canvas?.Input.IsKeyDown(_canvas.InputMap.MultiSelectMod) == true;
        private bool _ctrlHeld => _canvas?.Input.IsKeyDown(_canvas.InputMap.BoxSelectMod) == true;
        
        private MapCanvas _canvas;

        public StandardInteractionTool(
            ISimulationView view,
            EntityQuery query,
            IVisualizerAdapter adapter)
        {
            _view = view;
            _query = query;
            _adapter = adapter;
        }

        public void OnEnter(MapCanvas canvas)
        {
            _canvas = canvas;
            ResetState();
        }

        public void OnExit()
        {
            ResetState();
        }

        private void ResetState()
        {
            _isActionMouseDown = false;
            _potentialTarget = Entity.Null;
        }

        public void Update(float dt)
        {
            if (_isActionMouseDown && _canvas?.Input.IsMouseButtonDown(_canvas.InputMap.SelectButton) == false)
            {
                ResetState();
            }
            // No global key handling in generic tool
        }

        public void Draw(RenderContext ctx)
        {
            // Nothing to draw
        }

        public bool HandleClick(Vector2 worldPos, MouseButton button)
        {
            var selectBtn = _canvas?.InputMap.SelectButton ?? MouseButton.Left;
            Entity hit = Entity.Null;

            // 1. Selection Logic (Only if Select Button)
            if (button == selectBtn)
            {
                // Detect hit via Visual Picking (Topmost Layer)
                hit = FindEntityAt(worldPos);

                // Notify Selection Request
                bool augment = _ctrlHeld || _shiftHeld;
                
                // If we hit something, request select.
                // If we hit nothing, request deselect all (unless augment?).
                if (_view.IsAlive(hit))
                {
                    OnEntitySelectRequest?.Invoke(hit, augment);
                }
                else
                {
                   // If valid click on empty space with no modifiers, usually implies deselect.
                   if (!augment)
                       OnEntitySelectRequest?.Invoke(Entity.Null, false);
                }
            }
            else
            {
                // For other buttons (e.g. Right Click), we still want to know what was under cursor
                hit = FindEntityAt(worldPos);
            }
            
            // Notify Generic Click (Used for Right-Click Context, Move commands etc)
            OnWorldClick?.Invoke(worldPos, button, _shiftHeld, _ctrlHeld, hit);
            
            return true; // Consumed
        }

        public bool HandleDrag(Vector2 worldPos, Vector2 delta)
        {
            if (_isActionMouseDown)
            {
                float dist = Vector2.Distance(worldPos, _mouseDownPos);
                if (dist > DRAG_THRESHOLD)
                {
                    // Threshold passed -> Decide Action
                    if (_view.IsAlive(_potentialTarget))
                    {
                        // Dragging on an entity -> Entity Drag
                        var startPos = _adapter.GetPosition(_view, _potentialTarget) ?? _mouseDownPos;
                        var tool = new EntityDragTool(
                            _potentialTarget, 
                            startPos, 
                            () => _canvas.PopTool()
                        );
                        tool.OnEntityMoved += (e, p) => OnEntityMoved?.Invoke(e, p);
                        _canvas.PushTool(tool);

                        ResetState();
                        return true;
                    }
                    else
                    {
                        // Dragging on empty space -> Box Select
                        var tool = new BoxSelectionTool(
                            _mouseDownPos,
                            _view,
                            _query,
                            _adapter,
                            (list) => { OnRegionSelected?.Invoke(list); _canvas.PopTool(); },
                            () => _canvas.PopTool()
                        );
                        _canvas.PushTool(tool);

                        ResetState();
                        return true;
                    }
                }
            }
            
            return false;
        }

        public bool HandleHover(Vector2 worldPos)
        {
            // Track Mouse Down for Drag Initiation
            if (_canvas?.Input.IsMouseButtonPressed(_canvas.InputMap.SelectButton) == true)
            {
                _isActionMouseDown = true;
                _mouseDownPos = worldPos;
                _potentialTarget = FindEntityAt(worldPos);
            }
            return false;
        }

        private Entity FindEntityAt(Vector2 pos)
        {
            return _canvas?.PickTopmostEntity(pos) ?? Entity.Null;
        }
    }
}
