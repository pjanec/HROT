using System.Numerics;

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
    bool HandleClick(Vector2 worldPos, MapMouseButton button);
    bool HandleDrag(Vector2 worldPos, Vector2 delta);
    bool HandleHover(Vector2 worldPos);

    /// <summary>
    /// Called by <see cref="MapCanvas"/> for each key pressed this frame,
    /// after ImGui keyboard capture has been checked.
    /// Return <c>true</c> to mark the key as consumed so it does not bubble
    /// to other handlers (camera, main loop, etc.).
    /// The default implementation returns <c>false</c> (not consumed).
    /// </summary>
    bool HandleKeyPressed(MapKeyboardKey key) => false;

    /// <summary>
    /// Called when a mouse button is first pressed (down-stroke only).
    /// Invoked before layer routing so the active tool gets first refusal.
    /// Return <c>true</c> to consume the press; <c>false</c> = pass through to layers.
    /// </summary>
    bool HandlePress(Vector2 worldPos, MapMouseButton button) => false;
}
