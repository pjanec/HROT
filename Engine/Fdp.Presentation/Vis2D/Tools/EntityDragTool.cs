using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Vis2D.Abstractions;

namespace Fdp.Toolkit.Vis2D.Tools;

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
    private MapCanvas? _canvas;

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
        if (_canvas?.Input.IsMouseButtonReleased(MapMouseButton.Left) == true)
        {
            Finish();
        }
    }

    public void Draw(RenderContext ctx)
    {
        var color = new Rgba32(255, 255, 0, 255);
        float zoom = ctx.Zoom > 0 ? ctx.Zoom : 1f;

        // Draw drag line from start to current position.
        ctx.DrawBuilder?.DrawLine(
            new Vector3(_startPos.X, _startPos.Y, 0f),
            new Vector3(_currentPos.X, _currentPos.Y, 0f),
            color, 2.0f / zoom);

        // Draw target reticle at current position.
        float radius = 10.0f / zoom;
        ctx.DrawBuilder?.DrawSphere(
            new Vector3(_currentPos.X, _currentPos.Y, 0f),
            radius, color);
    }

    public bool HandleClick(Vector2 worldPos, MapMouseButton button)
    {
        // Consume all clicks while dragging to prevent other interactions
        if (_isActive) return true; 
        
        return false;
    }

    public bool HandleDrag(Vector2 worldPos, Vector2 delta)
    {
        if (_isActive && _canvas?.Input.IsMouseButtonDown(MapMouseButton.Left) == true)
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
