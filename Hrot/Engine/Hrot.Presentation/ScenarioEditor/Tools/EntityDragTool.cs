using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Abstractions;

namespace Hrot.ScenarioEditor.Tools;

/// <summary>
/// Stateful map tool for entity drag-and-drop repositioning.
/// Captures the entity ECS position on entry, renders a ghost preview via
/// <see cref="IDebugDrawBuilder"/> during the drag, and fires
/// <see cref="OnEntityMoved"/> with the final world position on commit.
/// Cancels (restores original position) on ESC or right-click.
/// No Raylib dependency: all rendering goes through the DrawBuilder pipeline.
/// </summary>
public sealed class EntityDragTool : IMapTool
{
    /// <inheritdoc/>
    public string Name => "EntityDrag";

    // ---- State ---------------------------------------------------------------

    private MapCanvas? _canvas;
    private Entity     _entity;
    private Vector2    _originPos;
    private Vector2    _currentPos;
    private bool       _dragging;

    // ---- Events --------------------------------------------------------------

    /// <summary>Fired when the operator releases the mouse and commits the move.</summary>
    public event Action<Entity, Vector2>? OnEntityMoved;

    /// <summary>Fired when the operator cancels the operation (ESC or right-click).</summary>
    public event Action<Entity>? OnCancelled;

    // ---- Constructor ---------------------------------------------------------

    /// <summary>
    /// Creates a drag tool bound to the given entity at its current world position.
    /// </summary>
    /// <param name="entity">Entity being dragged.</param>
    /// <param name="entityWorldPos">Current world-space position of the entity (map XY).</param>
    public EntityDragTool(Entity entity, Vector2 entityWorldPos)
    {
        _entity    = entity;
        _originPos = entityWorldPos;
    }

    // ---- IMapTool lifecycle --------------------------------------------------

    /// <inheritdoc/>
    public void OnEnter(MapCanvas canvas)
    {
        _canvas     = canvas;
        _currentPos = _originPos;
        _dragging   = false;
    }

    /// <inheritdoc/>
    public void OnExit()
    {
        _canvas   = null;
        _dragging = false;
    }

    /// <inheritdoc/>
    public void Update(float dt) { }

    // ---- Input ---------------------------------------------------------------

    /// <inheritdoc/>
    public bool HandleHover(Vector2 worldPos)
    {
        _currentPos = worldPos;
        return true;
    }

    /// <inheritdoc/>
    public bool HandleDrag(Vector2 worldPos, Vector2 delta)
    {
        _currentPos = worldPos;
        _dragging   = true;
        return true;
    }

    /// <inheritdoc/>
    public bool HandleClick(Vector2 worldPos, MapMouseButton button)
    {
        if (button == MapMouseButton.Left)
        {
            _currentPos = worldPos;
            Commit();
            return true;
        }
        if (button == MapMouseButton.Right)
        {
            Cancel();
            return true;
        }
        return false;
    }

    /// <inheritdoc/>
    public bool HandleKeyPressed(MapKeyboardKey key)
    {
        if (key == MapKeyboardKey.Escape)
        {
            Cancel();
            return true;
        }
        return false;
    }

    // ---- Rendering -----------------------------------------------------------

    /// <inheritdoc/>
    public void Draw(RenderContext ctx)
    {
        // Ghost preview: a sphere at the candidate position connected by a line to the origin.
        var ghostColor  = new Fdp.Toolkit.Diagnostics.Gizmos.Rgba32(0, 200, 255, 200); // cyan ghost
        var lineColor   = new Fdp.Toolkit.Diagnostics.Gizmos.Rgba32(0, 200, 255, 128);

        float zoom   = ctx.Zoom > 0 ? ctx.Zoom : 1f;
        float radius = 12f / zoom;

        ctx.DrawBuilder?.DrawSphere(
            new Vector3(_currentPos.X, _currentPos.Y, 0f), radius, ghostColor);

        if (_dragging)
        {
            ctx.DrawBuilder?.DrawLine(
                new Vector3(_originPos.X,  _originPos.Y,  0f),
                new Vector3(_currentPos.X, _currentPos.Y, 0f),
                lineColor, 1.5f / zoom);
        }
    }

    // ---- Helpers -------------------------------------------------------------

    private void Commit()
    {
        var entity = _entity;
        var pos    = _currentPos;
        _canvas?.PopTool();
        OnEntityMoved?.Invoke(entity, pos);
    }

    private void Cancel()
    {
        var entity = _entity;
        _canvas?.PopTool();
        OnCancelled?.Invoke(entity);
    }
}
