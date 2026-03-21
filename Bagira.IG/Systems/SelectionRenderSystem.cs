using System.Numerics;
using Bagira.IG.Adapters;
using Bagira.IG.Components;
using Fdp.Kernel;
using FDP.Toolkit.Vis2D.Abstractions;
using FDP.Toolkit.Vis2D.Components;
using ModuleHost.Core.Abstractions;
using Raylib_cs;

namespace Bagira.IG.Systems;

/// <summary>
/// PostRender-equivalent overlay layer that draws selection rings for every entity
/// whose <see cref="SelectionState.IsSelected"/> flag is <c>true</c>.
///
/// Implemented as an <see cref="IMapLayer"/> so that draw calls execute inside
/// the <c>MapCanvas.Draw()</c> scope (within <c>BeginMode2D</c>), where coordinates
/// and radii are automatically scaled by the camera zoom.
///
/// Visual contract (§CODE-STANDARDS §1 — all sizes from constants):
/// <list type="bullet">
///   <item>
///     Primary selection (<see cref="SelectionState.IsPrimarySelection"/> = <c>true</c>):
///     filled green circle (alpha <see cref="SelectionRenderConstants.PrimaryFillAlpha"/>)
///     and green outline.
///   </item>
///   <item>
///     Secondary selection: yellow outline only (no fill).
///   </item>
/// </list>
///
/// Zero allocations in <see cref="Draw"/> (§CODE-STANDARDS §4):
/// all ECS iteration is plain <c>foreach</c> over the pre-built query.
/// </summary>
public class SelectionRenderSystem : IMapLayer
{
    /// <inheritdoc/>
    public string Name => SelectionRenderConstants.LayerName;

    /// <inheritdoc/>
    /// <remarks>
    /// Returns <c>-1</c> so the layer is always drawn regardless of the canvas
    /// layer-visibility mask.
    /// </remarks>
    public int LayerBitIndex => SelectionRenderConstants.AlwaysVisibleLayerBitIndex;

    private readonly ISimulationView _view;
    private readonly EntityQuery     _query;

    /// <param name="view">
    /// Simulation view used to read <see cref="SelectionState"/> and
    /// <see cref="SimTransform"/> components.
    /// </param>
    /// <param name="query">
    /// Pre-built query returning entities to check for selection state.
    /// Should be <c>With&lt;SelectionState, SimTransform&gt;</c>.
    /// </param>
    public SelectionRenderSystem(ISimulationView view, EntityQuery query)
    {
        _view  = view;
        _query = query;
    }

    /// <inheritdoc/>
    public void Update(float dt) { /* No per-frame state — rendering only. */ }

    /// <inheritdoc/>
    /// <remarks>
    /// Called inside <c>MapCanvas.Draw()</c> → <c>Camera.BeginMode()</c>.
    /// Coordinates are in world space; the camera applies zoom automatically.
    /// </remarks>
    public void Draw(RenderContext ctx)
    {
        foreach (var entity in _query)
        {
            if (!_view.HasComponent<SelectionState>(entity))
                continue;

            ref readonly var sel = ref _view.GetComponentRO<SelectionState>(entity);
            if (!sel.IsSelected)
                continue;

            // Layer visibility: skip selection rings for entities on hidden layers.
            if (_view.HasComponent<MapDisplayComponent>(entity))
            {
                uint em = _view.GetComponentRO<MapDisplayComponent>(entity).LayerMask;
                if ((em & ctx.VisibleLayersMask) == 0) continue;
            }

            if (!_view.HasComponent<SimTransform>(entity))
                continue;

            ref readonly var transform = ref _view.GetComponentRO<SimTransform>(entity);
            var pos = new Vector2(transform.Position.X, transform.Position.Y);

            TestHook_RingDrawCount++;

            if (!TestHook_SkipRaylibCalls)
            {
                if (sel.IsPrimarySelection)
                {
                    // Filled green circle (semi-transparent) + green outline.
                    var fill = new Color(
                        SelectionRenderConstants.PrimaryFillR,
                        SelectionRenderConstants.PrimaryFillG,
                        SelectionRenderConstants.PrimaryFillB,
                        SelectionRenderConstants.PrimaryFillAlpha);

                    Raylib.DrawCircle(
                        (int)pos.X, (int)pos.Y,
                        SstVisualizerAdapterConstants.SelectionRadiusPx,
                        fill);

                    Raylib.DrawCircleLines(
                        (int)pos.X, (int)pos.Y,
                        SstVisualizerAdapterConstants.SelectionRadiusPx,
                        Color.Green);
                }
                else
                {
                    // Yellow outline only for secondary selections.
                    Raylib.DrawCircleLines(
                        (int)pos.X, (int)pos.Y,
                        SstVisualizerAdapterConstants.SelectionRadiusPx,
                        Color.Yellow);
                }
            }
        }
    }

    /// <summary>Suppresses Raylib calls in tests. Increment only <see cref="TestHook_RingDrawCount"/>.</summary>
    internal bool TestHook_SkipRaylibCalls;

    /// <summary>Counts selection ring draws reached. Reset between test assertions.</summary>
    internal int TestHook_RingDrawCount;

    /// <inheritdoc/>
    /// <remarks>Selection rings do not consume mouse input.</remarks>
    public bool HandleInput(Vector2 worldPos, MouseButton button, bool isPressed) => false;

    /// <inheritdoc/>
    /// <remarks>Selection rings are overlays; they are not pick targets.</remarks>
    public Entity? PickEntity(Vector2 worldPos) => null;
}
