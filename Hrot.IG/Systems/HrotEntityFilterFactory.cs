using System;
using System.Linq;
using Fdp.Kernel;
using FDP.Toolkit.Vis2D.Abstractions;
using FDP.Toolkit.Vis2D.Components;

namespace Hrot.IG.Systems;

/// <summary>
/// Hrot-IG implementation of <see cref="IEntityFilterFactory"/>.
///
/// <para>
/// Translates human-readable layer preset strings (e.g. <c>"road_graphs"</c>,
/// <c>"units_ground"</c>) into a single combined <c>uint</c> bitmask by
/// consulting the static <see cref="MapLayerRegistry"/>.  The bitmask
/// computation happens exactly once inside <see cref="CreateFilter"/>; the
/// returned <see cref="LayerMaskFilter.IsMatch"/> executes as an O(1) lookup
/// against <see cref="MapDisplayComponent.LayerMask"/>.
/// </para>
///
/// <para>
/// <b>DIP / clean boundaries:</b> Only <c>Hrot.IG</c> knows about
/// <c>MapLayerRegistry</c> and <c>MapDisplayComponent</c>.
/// <c>FDP.Toolkit.Vis2D</c> only sees the <see cref="IEntityFilter"/> abstraction.
/// </para>
/// </summary>
public sealed class HrotEntityFilterFactory : IEntityFilterFactory
{
    private readonly EntityRepository _world;

    /// <param name="world">
    /// Live ECS entity repository used by <see cref="LayerMaskFilter"/> to read
    /// <see cref="MapDisplayComponent"/> from each candidate entity.
    /// </param>
    public HrotEntityFilterFactory(EntityRepository world)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Unknown preset names are silently ignored so that forward-compatible
    /// command payloads do not break older IG instances.
    /// An empty <paramref name="filterPresets"/> array results in a mask of
    /// <c>0xFFFFFFFF</c> (match all layers).
    /// </remarks>
    public IEntityFilter CreateFilter(string[] filterPresets)
    {
        if (filterPresets == null || filterPresets.Length == 0)
        {
            // No restriction: any entity with a MapDisplayComponent passes.
            return new LayerMaskFilter(_world, 0xFFFFFFFFu);
        }

        uint combinedMask = 0u;
        foreach (var preset in filterPresets)
        {
            // O(1) scan of the small static registry list.
            foreach (var layerDef in MapLayerRegistry.All)
            {
                if (string.Equals(layerDef.Name, preset, StringComparison.OrdinalIgnoreCase))
                {
                    combinedMask |= layerDef.BitMask;
                    break;
                }
            }
        }

        // If no preset matched, fall back to match-all so the picker is still usable.
        if (combinedMask == 0u)
            combinedMask = 0xFFFFFFFFu;

        return new LayerMaskFilter(_world, combinedMask);
    }
}

/// <summary>
/// High-performance entity filter that checks <see cref="MapDisplayComponent.LayerMask"/>
/// against a precompiled bitmask.
///
/// <para>All per-frame calls to <see cref="IsMatch"/> are O(1) and allocation-free:
/// a single ECS component pointer dereference followed by a bitwise <c>&amp;</c>
/// operation.</para>
/// </summary>
public sealed class LayerMaskFilter : IEntityFilter
{
    private readonly EntityRepository _world;
    private readonly uint             _allowedMask;

    internal LayerMaskFilter(EntityRepository world, uint allowedMask)
    {
        _world       = world;
        _allowedMask = allowedMask;
    }

    /// <inheritdoc/>
    public bool IsMatch(Entity entity)
    {
        if (!_world.HasComponent<MapDisplayComponent>(entity))
            return false;

        ref readonly var display = ref _world.GetComponentRO<MapDisplayComponent>(entity);
        return (display.LayerMask & _allowedMask) != 0;
    }
}
