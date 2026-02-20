using System;
using System.Collections.Generic;
using System.Numerics;
using Raylib_cs;
using FDP.Toolkit.Vis2D.Abstractions;
using Fdp.Kernel;
using ModuleHost.Core.Abstractions;

namespace FDP.Toolkit.Vis2D.Tools
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
        private readonly IVisualizerAdapter _adapter;
        
        private Vector2 _startPos;
        private Vector2 _currentPos;
        private bool _isActive;

        public BoxSelectionTool(
            Vector2 startPos,
            ISimulationView view,
            EntityQuery query,
            IVisualizerAdapter adapter,
            Action<List<Entity>> onSelectionComplete,
            Action onCancel)
        {
            _startPos = startPos;
            _currentPos = startPos;
            _view = view;
            _query = query;
            _adapter = adapter;
            _onSelectionComplete = onSelectionComplete;
            _onCancel = onCancel;
            _isActive = true;
        }

        public void OnEnter(MapCanvas canvas)
        {
            _isActive = true;
        }

        public void OnExit()
        {
            _isActive = false;
        }

        public void Update(float dt)
        {
            if (!_isActive) return;

            // Update current position to mouse
            // Wait, HandleDrag/Hover does this. But we might need active polling if mouse goes off screen?
            // Rely on IMapTool callbacks for position updates.
            
            if (Raylib.IsMouseButtonReleased(MouseButton.Left))
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

        public bool HandleClick(Vector2 worldPos, MouseButton button)
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
            
            // Query
            foreach (var entity in _query)
            {
                var pos = _adapter.GetPosition(_view, entity);
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
