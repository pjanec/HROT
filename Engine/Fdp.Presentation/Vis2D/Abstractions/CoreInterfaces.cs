using System.Numerics;
using Fdp.Kernel;
using ModuleHost.Core.Abstractions;
using Raylib_cs;

namespace FDP.Toolkit.Vis2D.Abstractions;

/// <summary>
/// Context passed to rendering layers and tools.
/// </summary>
public struct RenderContext
{
    public Camera2D Camera;
    public float Zoom => Camera.Zoom;
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
}

/// <summary>
/// Adapter interface for rendering entities.
/// Decouples map rendering from specific component types.
/// </summary>
public interface IVisualizerAdapter
{
    /// <summary>
    /// Extract world position from entity. Returns null to hide/cull the entity.
    /// </summary>
    Vector2? GetPosition(ISimulationView view, Entity entity);

    /// <summary>
    /// Draw the entity. Called inside Raylib BeginMode2D.
    /// </summary>
    void Render(ISimulationView view, Entity entity, Vector2 position, RenderContext ctx, bool isSelected, bool isHovered);

    /// <summary>
    /// Helper to determine picking radius (for mouse clicks).
    /// </summary>
    float GetHitRadius(ISimulationView view, Entity entity);
    
    /// <summary>
    /// (Optional) Text to display when hovering. Return null for no tooltip.
    /// </summary>
    string? GetHoverLabel(ISimulationView view, Entity entity) => null;
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
    bool HandleInput(Vector2 worldPos, MouseButton button, bool isPressed);

    /// <summary>
    /// Pick the top-most entity at the given world position.
    /// Used for visual aggregation and selection.
    /// </summary>
    Entity? PickEntity(Vector2 worldPos);
}


