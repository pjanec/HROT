using System;
using System.Numerics;
using Raylib_cs;
using Fdp.Kernel;
using FDP.Toolkit.Vis2D.Abstractions;

namespace FDP.Toolkit.Vis2D.Tools;

/// <summary>
/// A modal tool to move an entity by dragging.
/// </summary>
public class EntityDragTool : IMapTool
{
    public string Name => "Drag Entity";
    
    // Callbacks provided by higher-level code (App/Example)
    // Decoupled from repository/simulation logic.
    private readonly Entity _target;
    public event Action<Entity, Vector2>? OnEntityMoved; // Replaces direct Action injection
    private readonly Action _onComplete;
    
    // Internal State
    private Vector2 _currentPos;
    private Vector2 _startPos;
    private bool _isActive;
    private MapCanvas _canvas;

    public EntityDragTool(Entity target, Vector2 startPos, Action onComplete)
    {
        _target = target;
        _startPos = startPos;
        _currentPos = startPos;
        _onComplete = onComplete;
        _isActive = true;
    }

    public void OnEnter(MapCanvas canvas)
    {
        _canvas = canvas;
        // Typically initialized active
    }

    public void OnExit()
    {
        // Cleanup if forcibly exited
        if (_isActive)
        {
            _onComplete?.Invoke();
            _isActive = false;
        }
    }

    public void Update(float dt)
    {
        // Check if mouse released to finish Drag
        if (_canvas?.Input.IsMouseButtonReleased(MouseButton.Left) == true)
        {
            Finish();
        }
    }

    public void Draw(RenderContext ctx)
    {
        // Draw drag line from start to current
        Raylib.DrawLineEx(_startPos, _currentPos, 2.0f / ctx.Zoom, Color.Yellow);
        
        // Draw target reticle at current pos
        float radius = 10.0f / ctx.Zoom;
        Raylib.DrawCircleLines((int)_currentPos.X, (int)_currentPos.Y, radius, Color.Yellow);
        Raylib.DrawCircle((int)_currentPos.X, (int)_currentPos.Y, 2.0f / ctx.Zoom, Color.Yellow);
    }

    public bool HandleClick(Vector2 worldPos, MouseButton button)
    {
        // Consume all clicks while dragging to prevent other interactions
        if (_isActive) return true; 
        
        return false;
    }

    public bool HandleDrag(Vector2 worldPos, Vector2 delta)
    {
        if (_isActive && _canvas?.Input.IsMouseButtonDown(MouseButton.Left) == true)
        {
            _currentPos = worldPos;
            
            // Invoke callback to update simulation
            OnEntityMoved?.Invoke(_target, _currentPos);
            
            return true;
        }
        return false;
    }

    public bool HandleHover(Vector2 worldPos)
    {
        // While dragging, we update position on hover too? Usually Drag implies Button Down.
        // But if we support "pick and place" (click to pick, move, click to place), then Hover is relevant.
        // Current requirement: "Drag" implies holding button.
        
        _currentPos = worldPos; // Update visual position for reticle even if not "dragging" (e.g. if button released logic is handled in Update)
        return false; 
    }

    private void Finish()
    {
        if (_isActive)
        {
            _isActive = false;
            _onComplete?.Invoke();
        }
    }
}
