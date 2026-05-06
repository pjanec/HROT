using System;
using System.Numerics;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Abstractions;
using Raylib_cs;

namespace Hrot.ScenarioEditor.Tools;

/// <summary>Unit system for distance display by <see cref="MeasureTool"/>.</summary>
public enum MeasureDisplayUnits { Meters = 0, Kilometers = 1 }

/// <summary>
/// Map tool that measures the Cartesian distance between two operator-clicked
/// world positions.
///
/// State machine:
/// <list type="number">
///   <item><b>Idle</b> â€” no start point set.  Left-click records the start point.</item>
///   <item><b>Measuring</b> â€” start point set.  Moving the mouse shows a live preview
///         line and distance label.  Left-click records the end point, logs the
///         distance, and pops the tool.  Right-click cancels and pops the tool.</item>
/// </list>
///
/// Distance is calculated as 2-D Euclidean distance (<see cref="Vector2.Distance"/>)
/// in world-space metres, consistent with the FDP right-handed coordinate system
/// where X = east and Y = north (Â§CODE-STANDARDS Â§2).  Z (altitude) is ignored
/// because the canvas operates in a top-down 2-D projection.
///
/// Draw calls are made inside <c>MapCanvas.Draw()</c> â†’ <c>Camera.BeginMode()</c>
/// so all coordinates and thicknesses are in world space and scale with zoom.
///
/// No allocations in the hover / draw hot path (Â§CODE-STANDARDS Â§4).
/// </summary>
public class MeasureTool : IMapTool
{
    /// <inheritdoc/>
    public string Name => MeasureToolConstants.ToolName;

    /// <summary>Unit system for distance display. Default is meters.</summary>
    public MeasureDisplayUnits DisplayUnits { get; set; } = MeasureDisplayUnits.Meters;

    private MapCanvas? _canvas;
    private Vector2?   _startPoint;
    private Vector2    _currentPoint;

    /// <summary>
    /// Distance (metres) of the last completed measurement.
    /// <c>float.NaN</c> when no measurement has been completed yet.
    /// Exposed for unit-test assertions without requiring rendering.
    /// </summary>
    public float LastMeasuredDistanceMeters { get; private set; } = float.NaN;

    /// <summary>
    /// <c>true</c> when a start point has been set and the tool is awaiting the end click.
    /// </summary>
    public bool IsMeasuring => _startPoint.HasValue;

    // â”€â”€ IMapTool lifecycle â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <inheritdoc/>
    public void OnEnter(MapCanvas canvas)
    {
        _canvas    = canvas;
        _startPoint = null;
        LastMeasuredDistanceMeters = float.NaN;
    }

    /// <inheritdoc/>
    public void OnExit()
    {
        _canvas = null;
    }

    /// <inheritdoc/>
    public void Update(float dt) { /* Stateless between frames. */ }

    // â”€â”€ Input handling â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <inheritdoc/>
    public bool HandleClick(Vector2 worldPos, MouseButton button)
    {
        if (button == MouseButton.Left)
        {
            if (!_startPoint.HasValue)
            {
                // First click â€” record start point and begin measuring.
                _startPoint    = worldPos;
                _currentPoint  = worldPos;
            }
            else
            {
                // Second click â€” record end, compute and log distance, finish.
                LastMeasuredDistanceMeters = Vector2.Distance(_startPoint.Value, worldPos);
                Console.WriteLine($"[MeasureTool] Distance: {LastMeasuredDistanceMeters:F2} m");
                _canvas?.PopTool();
            }

            return true;
        }

        if (button == MouseButton.Right)
        {
            // Cancel measurement.
            _canvas?.PopTool();
            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public bool HandleDrag(Vector2 worldPos, Vector2 delta) => false;

    /// <inheritdoc/>
    /// <remarks>Tracks cursor world position for the live preview line.</remarks>
    public bool HandleHover(Vector2 worldPos)
    {
        _currentPoint = worldPos;
        return false;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <see cref="KeyboardKey.Escape"/> cancels and pops the tool regardless of state.
    /// </remarks>
    public bool HandleKeyPressed(KeyboardKey key)
    {
        if (key == KeyboardKey.Escape)
        {
            _canvas?.PopTool();
            return true;
        }
        return false;
    }

    // â”€â”€ Rendering â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <inheritdoc/>
    /// <remarks>
    /// When no start point is set, draws a crosshair cursor at the current mouse
    /// position to indicate the tool is ready for the first click.
    /// When a start point is set, draws a cyan line from the start to the current
    /// cursor position and a distance label at the midpoint.
    /// Called inside <c>MapCanvas.Draw()</c> â†’ <c>Camera.BeginMode()</c>.
    /// </remarks>
    public void Draw(RenderContext ctx)
    {
        if (!_startPoint.HasValue)
        {
            float zoom  = ctx.Zoom > 0 ? ctx.Zoom : 1f;
            float size  = 14f / zoom;
            float gap   = 5f  / zoom;
            float thick = MeasureToolConstants.LineThickness / zoom;
            Color color = MeasureToolConstants.LineColor;
            var   pos   = _currentPoint;

            TestHook_LineDrawCount += 4;
            TestHook_CircleDrawCount++;

            if (!TestHook_SkipRaylibCalls)
            {
                Raylib.DrawLineEx(new Vector2(pos.X - size, pos.Y), new Vector2(pos.X - gap,  pos.Y), thick, color);
                Raylib.DrawLineEx(new Vector2(pos.X + gap,  pos.Y), new Vector2(pos.X + size, pos.Y), thick, color);
                Raylib.DrawLineEx(new Vector2(pos.X, pos.Y - size), new Vector2(pos.X, pos.Y - gap),  thick, color);
                Raylib.DrawLineEx(new Vector2(pos.X, pos.Y + gap),  new Vector2(pos.X, pos.Y + size), thick, color);
                Raylib.DrawCircleLinesV(pos, gap, color);
            }
            return;
        }

        var start = _startPoint.Value;
        var end   = _currentPoint;

        Raylib.DrawLineEx(start, end, MeasureToolConstants.LineThickness, MeasureToolConstants.LineColor);

        float  distance = Vector2.Distance(start, end);
        string label    = DisplayUnits == MeasureDisplayUnits.Kilometers
            ? $"{distance / 1000f:F3} km"
            : $"{distance:F1} m";
        var    midpoint = (start + end) * 0.5f;

        Raylib.DrawText(
            label,
            (int)midpoint.X,
            (int)(midpoint.Y + MeasureToolConstants.LabelOffsetY),
            MeasureToolConstants.LabelFontSize,
            Color.White);
    }

    // â”€â”€ Test hooks â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>When <c>true</c>, crosshair Raylib calls are skipped; counters are still incremented.</summary>
    internal bool TestHook_SkipRaylibCalls;

    /// <summary>Number of line draw commands issued by the crosshair renderer.</summary>
    internal int TestHook_LineDrawCount;

    /// <summary>Number of circle draw commands issued by the crosshair renderer.</summary>
    internal int TestHook_CircleDrawCount;
}
