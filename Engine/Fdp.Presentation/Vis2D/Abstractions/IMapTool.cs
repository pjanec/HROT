using System.Numerics;
using Raylib_cs;

namespace Fdp.Toolkit.Vis2D.Abstractions;

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

    /// <summary>
    /// Called by <see cref="MapCanvas"/> for each key pressed this frame,
    /// after ImGui keyboard capture has been checked.
    /// Return <c>true</c> to mark the key as consumed so it does not bubble
    /// to other handlers (camera, main loop, etc.).
    /// The default implementation returns <c>false</c> (not consumed).
    /// </summary>
    bool HandleKeyPressed(KeyboardKey key) => false;
}
