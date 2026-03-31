using System;
using Hrot.IG.Components;
using Fdp.Kernel;
using ModuleHost.Core.Abstractions;

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
/// <see cref="Hrot.IG.Adapters.NedVisualizerAdapter.GetPosition"/> reads
/// <see cref="CullingState.IsVisible"/> to skip all draw calls for off-screen entities,
/// eliminating wasted GPU work.
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

            bool inView = x >= minX && x <= maxX && y >= minY && y <= maxY;

            var state = new CullingState { IsVisible = inView, LodLevel = lod };

            cmd.SetComponent(entity, state);
        }
    }
}
