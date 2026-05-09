using System;
using System.Numerics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Hrot.ScenarioEditor.Tools;

namespace Hrot.ScenarioEditor.Gizmos;

/// <summary>Unit system for distance display by <see cref="MeasureGizmo"/>.</summary>
public enum MeasureDisplayUnits { Meters = 0, Kilometers = 1 }

/// <summary>
/// Stateful gizmo that measures the Cartesian distance between two operator-clicked
/// world positions.
///
/// State machine:
/// <list type="number">
///   <item><b>Idle</b> - no start point set. Left-click (release) records the start point.</item>
///   <item><b>Measuring</b> - start point set. Moving the mouse shows a live preview
///         line and distance label. Left-click (release) records the end point, logs the
///         distance, and calls <see cref="_onRemove"/>. Right-press or Escape cancels.</item>
/// </list>
///
/// Managed by <see cref="Fdp.Toolkit.Diagnostics.Gizmos.Systems.GlobalGizmoManager"/>.
/// Replaces <c>MeasureTool</c> (Phase 6 of the gizmo migration).
/// </summary>
public sealed class MeasureGizmo : IEntityStatefulGizmo
{
    private const float CrosshairHalfSize  = 14f;
    private const float CrosshairGapRadius = 5f;
    private const float CrosshairThickness = 1.5f;

    private Vector3? _startPoint;
    private Vector3  _currentPoint;
    private readonly Action _onRemove;

    /// <inheritdoc/>
    public bool RequiresExclusiveFocus => true;

    /// <inheritdoc/>
    public bool IsFocused { get; private set; }

    /// <inheritdoc/>
    public void SetFocus(bool isFocused) => IsFocused = isFocused;

    /// <summary>Unit system for distance display. Default is meters.</summary>
    public MeasureDisplayUnits DisplayUnits { get; set; } = MeasureDisplayUnits.Meters;

    /// <summary>
    /// Distance (metres) of the last completed measurement.
    /// <c>float.NaN</c> when no measurement has been completed yet.
    /// Exposed for unit-test assertions without requiring rendering.
    /// </summary>
    public float LastMeasuredDistanceMeters { get; private set; } = float.NaN;

    /// <summary>
    /// <c>true</c> when a start point has been set and the gizmo is awaiting the end click.
    /// </summary>
    public bool IsMeasuring => _startPoint.HasValue;

    /// <param name="onRemove">
    /// Callback invoked when the gizmo wants to exit (measurement complete or cancelled).
    /// Typically calls <c>GlobalGizmoManager.Unregister</c>.
    /// </param>
    public MeasureGizmo(Action? onRemove = null)
    {
        _onRemove = onRemove ?? (() => { });
    }

    // -- IEntityStatefulGizmo --

    /// <inheritdoc/>
    public void UpdateAndDraw(float deltaTime, IDebugDrawBuilder draw)
    {
        if (!_startPoint.HasValue)
        {
            // Idle state: draw a cyan crosshair at the current cursor position.
            var color = new Rgba32(0, 255, 255, 255);
            var pos   = _currentPoint;

            draw.DrawLine(new Vector3(pos.X - CrosshairHalfSize, pos.Y, 0f), new Vector3(pos.X - CrosshairGapRadius, pos.Y, 0f), color, CrosshairThickness);
            draw.DrawLine(new Vector3(pos.X + CrosshairGapRadius, pos.Y, 0f), new Vector3(pos.X + CrosshairHalfSize, pos.Y, 0f), color, CrosshairThickness);
            draw.DrawLine(new Vector3(pos.X, pos.Y - CrosshairHalfSize, 0f), new Vector3(pos.X, pos.Y - CrosshairGapRadius, 0f), color, CrosshairThickness);
            draw.DrawLine(new Vector3(pos.X, pos.Y + CrosshairGapRadius, 0f), new Vector3(pos.X, pos.Y + CrosshairHalfSize, 0f), color, CrosshairThickness);
            draw.DrawSphere(new Vector3(pos.X, pos.Y, 0f), CrosshairGapRadius, color);
            return;
        }

        // Measuring state: draw line from start to current cursor and label the distance.
        var start = _startPoint.Value;
        var end   = _currentPoint;

        draw.DrawLine(
            new Vector3(start.X, start.Y, 0f),
            new Vector3(end.X,   end.Y,   0f),
            new Rgba32(0, 255, 255, 255),
            MeasureToolConstants.LineThickness);

        float  distance = Vector2.Distance(new Vector2(start.X, start.Y), new Vector2(end.X, end.Y));
        string label    = DisplayUnits == MeasureDisplayUnits.Kilometers
            ? $"{distance / 1000f:F3} km"
            : $"{distance:F1} m";
        float  midX = (start.X + end.X) * 0.5f;
        float  midY = (start.Y + end.Y) * 0.5f;

        draw.DrawTextLong(midX, midY + MeasureToolConstants.LabelOffsetY, label, Rgba32.White);
    }

    /// <inheritdoc/>
    public void OnDragUpdate(Vector3 worldPos)
    {
        _currentPoint = worldPos;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Left released: first click sets start; second click records distance and removes gizmo.
    /// Right pressed: cancel and remove gizmo.
    /// </remarks>
    public void OnMouseEvent(MapMouseButton button, bool isPressed, Vector3 worldPos)
    {
        if (button == MapMouseButton.Left && !isPressed)
        {
            if (!_startPoint.HasValue)
            {
                _startPoint   = worldPos;
                _currentPoint = worldPos;
            }
            else
            {
                LastMeasuredDistanceMeters = Vector2.Distance(
                    new Vector2(_startPoint.Value.X, _startPoint.Value.Y),
                    new Vector2(worldPos.X, worldPos.Y));
                Console.WriteLine($"[MeasureGizmo] Distance: {LastMeasuredDistanceMeters:F2} m");
                _onRemove();
            }
        }
        else if (button == MapMouseButton.Right && isPressed)
        {
            _onRemove();
        }
    }

    /// <inheritdoc/>
    /// <remarks>Escape cancels and removes the gizmo regardless of state.</remarks>
    public void OnKeyEvent(MapKeyboardKey key, bool isPressed)
    {
        if (key == MapKeyboardKey.Escape && isPressed)
            _onRemove();
    }

    // Unused IGizmoInteractionHandler methods
    /// <inheritdoc/>
    public void OnInteractionStarted(GizmoPickToken token, Vector3 worldPos) { }
    /// <inheritdoc/>
    public void OnCommit(Vector3 worldPos) { }
    /// <inheritdoc/>
    public void OnCancel() { }
    /// <inheritdoc/>
    public void OnMenuAction(int actionId) { }

    /// <inheritdoc/>
    public void Dispose() { }
}
