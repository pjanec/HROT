using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Vis2D.Abstractions;

/// <summary>
/// Context passed to rendering layers and tools.
/// </summary>
public struct RenderContext
{
    public float Zoom;
    public Vector2 MouseWorldPos;
    public float DeltaTime;

    /// <summary>
    /// The mask of layers currently enabled by the user (32-bit bitmask).
    /// </summary>
    public uint VisibleLayersMask;

    /// <summary>
    /// Access to global resources.
    /// </summary>
    public IResourceProvider Resources;

    /// <summary>
    /// Debug primitive builder injected by <see cref="MapCanvas.Draw"/>.
    /// Tools use this to emit backend-neutral draw primitives instead of calling Raylib directly.
    /// May be null in headless test contexts.
    /// </summary>
    public Fdp.Toolkit.Diagnostics.Gizmos.IDebugDrawBuilder? DrawBuilder;
}

/// <summary>
/// Map layer interface for composable rendering.
/// </summary>
public interface IMapLayer
{
    /// <summary>
    /// Name for the UI "Layer Control" panel.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Which bit in the mask does this layer represent?
    /// (0 to 31). Return -1 if it's an "Always On" background layer.
    /// </summary>
    int LayerBitIndex { get; }

    /// <summary>
    /// Update logic (animations, etc).
    /// </summary>
    void Update(float dt);

    /// <summary>
    /// Draw content. Check ctx.VisibleLayersMask if you need custom filtering logic.
    /// </summary>
    void Draw(RenderContext ctx);

    /// <summary>
    /// Handle mouse clicks.
    /// Return true if the input was consumed (blocking layers below).
    /// </summary>
    bool HandleInput(Vector2 worldPos, MapMouseButton button, bool isPressed);

    /// <summary>
    /// Pick the top-most entity at the given world position.
    /// Used for visual aggregation and selection.
    /// </summary>
    Entity? PickEntity(Vector2 worldPos);

    /// <summary>
    /// Called every frame with the current mouse world position.
    /// Used by layers that need to track cursor position for drag-update events.
    /// Default: no-op.
    /// </summary>
    void HandleHover(Vector2 mouseWorldPos) { }

    /// <summary>
    /// Called when the mouse moves with a button held down.
    /// Return true to consume the drag (prevents camera panning).
    /// Default: not consumed.
    /// </summary>
    bool HandleDrag(Vector2 worldPos, Vector2 delta) => false;

    /// <summary>
    /// Called for each key pressed this frame, after ImGui keyboard capture check.
    /// Return true to mark the key as consumed.
    /// Default: not consumed.
    /// </summary>
    bool HandleKeyInput(MapKeyboardKey key) => false;
}


