using System;
using Hrot.IG.Components;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.IG.Systems;

/// <summary>
/// PostSimulation-phase system that evaluates camera-frustum visibility and
/// level-of-detail for every entity carrying <see cref="SimTransform"/>.
///
/// Each frame it writes or updates a <see cref="CullingState"/> component:
/// <list type="bullet">
///   <item>
///     <see cref="CullingState.IsVisible"/> is <c>true</c> when the entity's XY
///     position falls within the active camera viewport provided by
///     <see cref="MapCameraViewport"/>.
///   </item>
///   <item>
///     <see cref="CullingState.LodLevel"/> is derived from the current zoom using
///     the thresholds in <see cref="CullingStateConstants"/>:
///     below <see cref="CullingStateConstants.LodIconOnlyZoomThreshold"/> → LOD 2,
///     below <see cref="CullingStateConstants.LodSimplifiedZoomThreshold"/> → LOD 1,
///     otherwise LOD 0.
///   </item>
/// </list>
///
/// The <c>DebugGizmoLayer</c> reads <see cref="CullingState.IsVisible"/> to skip
/// all draw calls for off-screen entities, eliminating wasted GPU work.
///
/// Design constraints (§CODE-STANDARDS §4):
/// <list type="bullet">
///   <item>Zero allocations in <see cref="Execute"/>.</item>
///   <item>No LINQ — plain <c>foreach</c> over the entity query.</item>
///   <item>All thresholds and LOD levels via <see cref="CullingStateConstants"/> named constants (§CODE-STANDARDS §1).</item>
/// </list>
/// </summary>
[UpdateInPhase(SystemPhase.PostSimulation)]
public class MapCullingSystem : IEcsModuleSystem
{
    private readonly MapCameraViewport _viewport;

    /// <param name="viewport">
    /// Application-owned object updated each frame before the kernel ticks.
    /// </param>
    public MapCullingSystem(MapCameraViewport viewport)
        => _viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));

    /// <inheritdoc/>
    public void Execute(ISimulationView view, float deltaTime)
    {
        var cmd = view.GetCommandBuffer();

        // Capture viewport fields into locals — avoids repeated property reads in the hot loop.
        float minX = _viewport.WorldMinX;
        float maxX = _viewport.WorldMaxX;
        float minY = _viewport.WorldMinY;
        float maxY = _viewport.WorldMaxY;
        float zoom = _viewport.Zoom;

        // CE-131: an UNSET viewport is not a small viewport - it carries NO information, and culling
        // against it is not culling, it is blanking.
        //
        // MapCameraViewport's four bounds are auto-properties with no initializer, so they start at 0f:
        // a degenerate point at the origin, against which every entity tests OUT of view. They are filled
        // from the projected screen corners in IgApplication.Update - inside `if (!_headless)`. So on a
        // headless node, or before the first frame with a real camera, this system marked EVERY entity
        // invisible, every tick.
        //
        // This went unnoticed for a long time because IG's map was in fact drawn by SimHost's and CGF's
        // entity projectors, which ignored culling entirely. UXI-23 S2a merged those away, which is what
        // finally made it visible (docs/UX/UX_Feature_Map_Parity.md 3.9j.5b).
        //
        // Absence means VISIBLE - the same rule EntityPresentationGizmo and CullingStateVisibilityPolicy
        // already follow. A host with a real viewport culls exactly as before.
        bool viewportIsSet = maxX > minX && maxY > minY;

        // Resolve LOD once per frame — all entities share the same zoom level.
        byte lod = zoom < CullingStateConstants.LodIconOnlyZoomThreshold
            ? CullingStateConstants.LodIconOnly
            : zoom < CullingStateConstants.LodSimplifiedZoomThreshold
                ? CullingStateConstants.LodSimplified
                : CullingStateConstants.LodFull;

        var query = view.Query().With<SimTransform>().Build();

        foreach (var entity in query)
        {
            ref readonly var transform = ref view.GetComponentRO<SimTransform>(entity);
            float x = transform.Position.X;
            float y = transform.Position.Y;

            bool inView = !viewportIsSet
                       || (x >= minX && x <= maxX && y >= minY && y <= maxY);

            var state = new CullingState { IsVisible = inView, LodLevel = lod };

            cmd.SetComponent(entity, state);
        }
    }
}
