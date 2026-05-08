using System;
using System.Numerics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Vis2D.Abstractions;

namespace Fdp.Toolkit.Vis2D.Tools;

/// <summary>
/// A map tool that lets the operator click any point on the canvas to return a
/// world-space location.
///
/// <para><b>Workflow:</b>
/// <list type="number">
///   <item>Caller pushes this tool onto the <see cref="MapCanvas"/> stack.</item>
///   <item>The canvas cursor is replaced by a crosshair to signal pick mode.</item>
///   <item>Left-click fires <see cref="OnLocationPicked"/> with the world
///         position and pops the tool.</item>
///   <item>Right-click or <c>Escape</c> fires <see cref="OnCancelled"/> and
///         pops the tool without a result.</item>
/// </list>
/// </para>
///
/// <para>No allocations on the 60 FPS hover / draw hot path.</para>
/// </summary>
public sealed class LocationPickerTool : IMapTool
{
    /// <inheritdoc/>
    public string Name => "LocationPicker";

    // ── State ─────────────────────────────────────────────────────────────────

    private MapCanvas? _canvas;
    private Vector2    _mouseWorldPos;

    // ── Visual constants ──────────────────────────────────────────────────────

    private const float CrosshairHalfSize  = 14f;
    private const float CrosshairThickness = 1.5f;
    private const float CrosshairGapRadius = 5f;

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fired when the operator left-clicks the map canvas to confirm a location.
    /// The tool pops itself immediately before firing the event.
    /// The argument is the world-space position of the click.
    /// </summary>
    public event Action<Vector2>? OnLocationPicked;

    /// <summary>
    /// Fired when the operator cancels the pick (right-click or <c>Escape</c>).
    /// The tool pops itself immediately before firing the event.
    /// </summary>
    public event Action? OnCancelled;

    // ── IMapTool lifecycle ────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void OnEnter(MapCanvas canvas) => _canvas = canvas;

    /// <inheritdoc/>
    public void OnExit() => _canvas = null;

    /// <inheritdoc/>
    public void Update(float dt) { }

    // ── Input ─────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public bool HandleHover(Vector2 worldPos)
    {
        _mouseWorldPos = worldPos;
        return false;
    }

    /// <inheritdoc/>
    public bool HandleClick(Vector2 worldPos, MapMouseButton button)
    {
        if (button == MapMouseButton.Left)
        {
            _canvas?.PopTool();
            OnLocationPicked?.Invoke(worldPos);
            return true;
        }

        if (button == MapMouseButton.Right)
        {
            _canvas?.PopTool();
            OnCancelled?.Invoke();
            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public bool HandleDrag(Vector2 worldPos, Vector2 delta) => false;

    /// <inheritdoc/>
    public bool HandleKeyPressed(MapKeyboardKey key)
    {
        if (key == MapKeyboardKey.Escape)
        {
            _canvas?.PopTool();
            OnCancelled?.Invoke();
            return true;
        }
        return false;
    }

    // ── Rendering ─────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    /// <summary>
    /// Draws a cyan crosshair cursor at the current mouse world position.
    /// All draw calls are allocation-free.
    /// </summary>
    public void Draw(RenderContext ctx)
    {
        float zoom  = ctx.Zoom > 0 ? ctx.Zoom : 1f;
        float size  = CrosshairHalfSize  / zoom;
        float thick = CrosshairThickness / zoom;
        float gap   = CrosshairGapRadius / zoom;

        // Sky-blue crosshair (Raylib Color.SkyBlue = R:102, G:191, B:255).
        var drawColor = new Rgba32(102, 191, 255, 255);
        var pos = _mouseWorldPos;

        // Horizontal arms.
        ctx.DrawBuilder?.DrawLine(new Vector3(pos.X - size, pos.Y, 0f), new Vector3(pos.X - gap, pos.Y, 0f), drawColor, thick);
        ctx.DrawBuilder?.DrawLine(new Vector3(pos.X + gap,  pos.Y, 0f), new Vector3(pos.X + size, pos.Y, 0f), drawColor, thick);

        // Vertical arms.
        ctx.DrawBuilder?.DrawLine(new Vector3(pos.X, pos.Y - size, 0f), new Vector3(pos.X, pos.Y - gap, 0f), drawColor, thick);
        ctx.DrawBuilder?.DrawLine(new Vector3(pos.X, pos.Y + gap,  0f), new Vector3(pos.X, pos.Y + size, 0f), drawColor, thick);

        // Centre circle.
        ctx.DrawBuilder?.DrawSphere(new Vector3(pos.X, pos.Y, 0f), gap, drawColor);
    }
}
