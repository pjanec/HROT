using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Vis2D.Abstractions;

namespace Fdp.Toolkit.Vis2D.Tools;

/// <summary>
/// Tool for drawing a sequence of points (a path or trajectory).
/// </summary>
public class PointSequenceTool : IMapTool
{
    public string Name => "Draw Path";

    private readonly Action<Vector2[]> _onFinish;
    private readonly List<Vector2> _points = new();
    private Vector2 _currentMousePos;
    private MapCanvas? _canvas;

    // Optional: Limit max points?
    private const int MAX_POINTS = 100;

    public PointSequenceTool(Action<Vector2[]> onFinish)
    {
        _onFinish = onFinish;
    }

    public void OnEnter(MapCanvas canvas)
    {
        _canvas = canvas;
        _points.Clear();
    }

    public void OnExit()
    {
        // Cancel operation if tool switched abruptly — discard partial path.
        _canvas = null;
        _points.Clear();
    }

    public void Update(float dt)
    {
        // Logic handled in HandleClick/Hover
    }

    public void Draw(RenderContext ctx)
    {
        // Raylib Color.Blue = R:0, G:121, B:241. SkyBlue = R:102, G:191, B:255.
        var blue    = new Rgba32(0,   121, 241, 255);
        var skyBlue = new Rgba32(102, 191, 255, 255);

        // Draw captured points
        if (_points.Count > 0)
        {
            // Draw lines connecting points
            for (int i = 0; i < _points.Count - 1; i++)
            {
                ctx.DrawBuilder?.DrawLine(
                    new Vector3(_points[i].X,     _points[i].Y,     0f),
                    new Vector3(_points[i + 1].X, _points[i + 1].Y, 0f),
                    blue, 2.0f / ctx.Zoom);
            }

            // Draw each point as a small circle
            foreach (var p in _points)
            {
                ctx.DrawBuilder?.DrawSphere(new Vector3(p.X, p.Y, 0f), 4.0f / ctx.Zoom, blue);
            }

            // Draw "elastic" line from last point to current mouse cursor
            ctx.DrawBuilder?.DrawLine(
                new Vector3(_points[^1].X,       _points[^1].Y,       0f),
                new Vector3(_currentMousePos.X,  _currentMousePos.Y,  0f),
                skyBlue, 1.0f / ctx.Zoom);
        }

        // Draw cursor indicator at mouse pos
        ctx.DrawBuilder?.DrawSphere(
            new Vector3(_currentMousePos.X, _currentMousePos.Y, 0f),
            5.0f / ctx.Zoom, blue);
    }

    public bool HandleClick(Vector2 worldPos, MapMouseButton button)
    {
        if (button == MapMouseButton.Left)
        {
            // Add point
            if (_points.Count < MAX_POINTS)
            {
                _points.Add(worldPos);
            }
            return true; // Consume click
        }
        else if (button == MapMouseButton.Right)
        {
            // Finish
            Finish();
            return true; // Consume click
        }
        return false;
    }

    public bool HandleDrag(Vector2 worldPos, Vector2 delta)
    {
        return false;
    }

    public bool HandleHover(Vector2 worldPos)
    {
        _currentMousePos = worldPos;
        return true; 
    }

    /// <summary>
    /// Cancels the point-sequence session on ESC and pops the tool without invoking
    /// the finish callback.  The accumulated points are discarded by <see cref="OnExit"/>.
    /// </summary>
    public bool HandleKeyPressed(MapKeyboardKey key)
    {
        if (key == MapKeyboardKey.Escape)
        {
            _canvas?.PopTool();
            return true;
        }
        return false;
    }

    private void Finish()
    {
        if (_points.Count > 0)
        {
            _onFinish?.Invoke(_points.ToArray());
        }
        else
        {
            _onFinish?.Invoke(Array.Empty<Vector2>());
        }
        
        // Note: The tool itself doesn't switch "off". The callback consumer (App) should switch back to Default tool.
        // Or we could have `Action<IMapTool> requestSwitch` dependency?
        // Better: Consumer handles flow.
    }
}
