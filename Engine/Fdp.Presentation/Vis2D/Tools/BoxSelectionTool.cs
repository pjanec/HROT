using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis2D.Components;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Raylib_cs;

namespace Fdp.Toolkit.Vis2D.Tools
{
    /// <summary>
    /// Tool for selecting multiple entities via a rectangle (marquee selection).
    /// </summary>
    public class BoxSelectionTool : IMapTool
    {
        public string Name => "Box Selection";
        
        private readonly Action<List<Entity>> _onSelectionComplete; // Returns selected entities
        private readonly Action _onCancel;
        private readonly ISimulationView _view;
        private readonly EntityQuery _query;
        private readonly Func<Entity, Vector2?> _getEntityPosition;
        private MapCanvas? _canvas;
        
        private Vector2 _startPos;
        private Vector2 _currentPos;
        private bool _isActive;

        public BoxSelectionTool(
            Vector2 startPos,
            ISimulationView view,
            EntityQuery query,
            Func<Entity, Vector2?>? getEntityPosition,
            Action<List<Entity>> onSelectionComplete,
            Action onCancel)
        {
            _startPos = startPos;
            _currentPos = startPos;
            _view = view;
            _query = query;
            _getEntityPosition = getEntityPosition ?? (e =>
                view.HasComponent<SimTransform>(e)
                    ? new Vector2(view.GetComponentRO<SimTransform>(e).Position.X,
                                  view.GetComponentRO<SimTransform>(e).Position.Y)
                    : (Vector2?)null);
            _onSelectionComplete = onSelectionComplete;
            _onCancel            = onCancel;
            _isActive            = true;
        }

        public void OnEnter(MapCanvas canvas)
        {
            _canvas = canvas;
            _isActive = true;
        }

        public void OnExit()
        {
            _canvas = null;
            _isActive = false;
        }

        public void Update(float dt)
        {
            if (!_isActive) return;

            // Update current position to mouse
            // Wait, HandleDrag/Hover does this. But we might need active polling if mouse goes off screen?
            // Rely on IMapTool callbacks for position updates.
            
            bool released = _canvas is not null
                ? _canvas.Input.IsMouseButtonReleased(MapMouseButton.Left)
                : false;
            if (released)
            {
                FinishSelection();
            }
        }

        public void Draw(RenderContext ctx)
        {
            // Draw Selection Box
            // Calculate Min/Max for drawing
            var min = Vector2.Min(_startPos, _currentPos);
            var max = Vector2.Max(_startPos, _currentPos);
            var size = max - min;
            
            // Draw semi-transparent fill
            Raylib.DrawRectangleV(min, size, new Color(0, 120, 255, 50));
            
            // Draw border (affected by Zoom? Usually we want consistent line width in screen space, 
            // but DrawRectangleLinesEx uses world units for thickness if we are in camera mode.
            // 2.0f / ctx.Zoom gives constant screen thickness)
            Raylib.DrawRectangleLinesEx(new Rectangle(min.X, min.Y, size.X, size.Y), 2.0f / ctx.Zoom, new Color(0, 120, 255, 200));
        }

        public bool HandleClick(Vector2 worldPos, MapMouseButton button)
        {
            return true; // Consume all clicks while selecting
        }

        public bool HandleDrag(Vector2 worldPos, Vector2 delta)
        {
            _currentPos = worldPos;
            return true;
        }

        public bool HandleHover(Vector2 worldPos)
        {
            _currentPos = worldPos;
            return true;
        }
        
        private void FinishSelection()
        {
            var selected = new List<Entity>();
            
            // Normalize Rect
            var min = Vector2.Min(_startPos, _currentPos);
            var max = Vector2.Max(_startPos, _currentPos);

            // Respect the canvas active layer mask so hidden-layer entities are not selectable.
            uint activeMask = _canvas?.ActiveLayerMask ?? 0xFFFFFFFF;

            // Query
            foreach (var entity in _query)
            {
                // Layer visibility check: skip entities whose layer mask is fully hidden.
                if (_view.HasComponent<MapDisplayComponent>(entity))
                {
                    uint em = _view.GetComponentRO<MapDisplayComponent>(entity).LayerMask;
                    if ((em & activeMask) == 0) continue;
                }

                var pos = _getEntityPosition(entity);
                if (!pos.HasValue) continue;
                
                // Simple Point-in-Rect check
                if (pos.Value.X >= min.X && pos.Value.X <= max.X &&
                    pos.Value.Y >= min.Y && pos.Value.Y <= max.Y)
                {
                    selected.Add(entity);
                }
            }
            
            _onSelectionComplete?.Invoke(selected);
        }
    }
}
