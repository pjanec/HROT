using System;
using System.Collections.Generic;
using System.Numerics;
using Raylib_cs;
using FDP.Toolkit.Vis2D.Abstractions;

namespace FDP.Toolkit.Vis2D.Tools;

/// <summary>
/// Tool for drawing a sequence of points (a path or trajectory).
/// </summary>
public class PointSequenceTool : IMapTool
{
    public string Name => "Draw Path";

    private readonly Action<Vector2[]> _onFinish;
    private readonly List<Vector2> _points = new();
    private Vector2 _currentMousePos;

    // Optional: Limit max points?
    private const int MAX_POINTS = 100;

    public PointSequenceTool(Action<Vector2[]> onFinish)
    {
        _onFinish = onFinish;
    }

    public void OnEnter(MapCanvas canvas)
    {
        _points.Clear();
    }

    public void OnExit()
    {
        // Cancel operation if tool switched abruptly?
        // Or callback with partial? Usually cancel.
        _points.Clear();
    }

    public void Update(float dt)
    {
        // Logic handled in HandleClick/Hover
    }

    public void Draw(RenderContext ctx)
    {
        // Draw captured points
        if (_points.Count > 0)
        {
            // Draw lines connecting points
            for (int i = 0; i < _points.Count - 1; i++)
            {
                Raylib.DrawLineEx(_points[i], _points[i + 1], 2.0f / ctx.Zoom, Color.Blue);
            }

            // Draw each point as a small circle
            foreach (var p in _points)
            {
                Raylib.DrawCircleV(p, 4.0f / ctx.Zoom, Color.Blue);
            }

            // Draw "elastic" line from last point to current mouse cursor
            Raylib.DrawLineEx(_points[^1], _currentMousePos, 1.0f / ctx.Zoom, Color.SkyBlue);
        }
        
        // Draw cursor indicator at mouse pos
        // Use DrawPolyLines to avoid casting to int (which DrawCircleLines requires) and losing precision in World Space
        Raylib.DrawPolyLines(_currentMousePos, 20, 5.0f / ctx.Zoom, 0.0f, Color.Blue);
    }

    public bool HandleClick(Vector2 worldPos, MouseButton button)
    {
        if (button == MouseButton.Left)
        {
            // Add point
            if (_points.Count < MAX_POINTS)
            {
                _points.Add(worldPos);
            }
            return true; // Consume click
        }
        else if (button == MouseButton.Right)
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
