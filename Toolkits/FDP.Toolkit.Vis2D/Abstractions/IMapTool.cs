using System.Numerics;
using Raylib_cs;

namespace FDP.Toolkit.Vis2D.Abstractions;

/// <summary>
/// Map tool interface for different interaction modes.
/// Uses State Pattern for tool switching.
/// </summary>
public interface IMapTool
{
    string Name { get; }

    // Lifecycle
    void OnEnter(MapCanvas canvas);
    void OnExit();

    // Execution
    void Update(float dt);
    
    /// <summary>
    /// Draw tool-specific overlays (gizmos, edit handles, selection boxes).
    /// Drawn AFTER all map layers.
    /// </summary>
    void Draw(RenderContext ctx);

    // Input (return true if consumed)
    bool HandleClick(Vector2 worldPos, MouseButton button);
    bool HandleDrag(Vector2 worldPos, Vector2 delta);
    bool HandleHover(Vector2 worldPos);
}
