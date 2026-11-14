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

    /// <summary>
    /// ⭐ <c>CE-1379</c> / <c>CE-1380</c> (docs/DESIGN_Director_Timeline_Editor.md §11t "as built") — true in a FILM's pass
    /// (<see cref="Fdp.Toolkit.Vis2D.MapCanvas.DrawScene"/>, 2-D and 3-D): a scene layer that also carries tool and UI drawing (the gizmo
    /// layer) draws only what a film shows. False on screen.
    /// </summary>
    public bool Film;

    /// <summary>
    /// ⭐ <c>CE-1383</c> (docs/DESIGN_Director_Timeline_Editor.md §11t "CE-1382 / CE-1383 as built") — the size in pixels of the TARGET this pass
    /// draws into: the window's on screen, the render texture's in a film's pass (<see cref="Fdp.Toolkit.Vis2D.MapCanvas.DrawScene"/>) — ONE
    /// source for every layer that culls to what is visible (the 2-D plan's outlines and labels, the grid). Zero when unknown (no window,
    /// a headless rail): a layer then does not cull.
    /// </summary>
    public Vector2 ViewSize;

    /// <summary><see cref="ViewSize"/>, or — for a context built by hand (a rail) with none — the window's size; zero with no window.</summary>
    public readonly Vector2 TargetSize =>
        ViewSize.X > 0f && ViewSize.Y > 0f ? ViewSize
        : Raylib_cs.Raylib.IsWindowReady() ? new Vector2(Raylib_cs.Raylib.GetScreenWidth(), Raylib_cs.Raylib.GetScreenHeight())
        : Vector2.Zero;
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

    /// <summary>
    /// ⭐ CE-1033 S1 (<c>docs/DESIGN_Map_3D_Mode.md</c> M3) — draws the layer in the map's 3-D mode, inside the camera's 3-D pass
    /// (coordinates already converted by <c>Vis3D.HrotToRaylib</c>). Default: nothing — a layer with no 3-D path is simply absent
    /// in 3-D (<see cref="MapCanvas.LayersWithout3D"/> counts them, so the absence is visible, not silent).
    /// </summary>
    void Draw3D(RenderContext ctx) { }

    /// <summary>True when the layer draws something in 3-D (overrides <see cref="Draw3D"/>).</summary>
    bool Has3D => false;

    /// <summary>
    /// ⭐ CE-1033 S3 (M13) / S5b — draws screen-space content AFTER the camera pass, in BOTH modes: labels projected from the 3-D
    /// scene, entity cards above their entities — at a constant pixel size, on top. Called for every visible layer (in 3-D only
    /// those with <see cref="Has3D"/>). Default: nothing.
    /// </summary>
    void DrawOverlay(RenderContext ctx) { }
}

/// <summary>
/// ⭐ <c>CE-3289</c> (<c>docs/DESIGN_Map_3D_Mode.md</c> §6ab, R-3032) — a layer that draws the WORLD itself (the ground, bodies, effects,
/// rounds), not a tool or an overlay on it. A filmed frame (<see cref="MapCanvas.DrawScene"/>) draws only these, so a new overlay stays
/// out of films unless it opts in.
/// <para>⭐ <c>CE-1379</c> / <c>CE-1380</c> (docs/DESIGN_Director_Timeline_Editor.md §11t) — in BOTH map modes now (a 2-D film frame is the
/// same pass), and three more opt in, each still gated by its layer row as on screen: the 2-D terrain plan, the 2-D grid, and the gizmo
/// layer — which in a film's pass (<see cref="RenderContext.Film"/>) draws only its film layers (the rows the Layers panel toggles; never
/// the selection, the always-on helpers or the UI panels).</para>
/// </summary>
public interface ISceneLayer : IMapLayer
{
}
