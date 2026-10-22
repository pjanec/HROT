using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Vis2D.Components;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Presentation.Map;

/// <summary>
/// PostSimulation system that assigns each map entity's <see cref="MapDisplayComponent"/>
/// bitmask by evaluating the predicates in <see cref="MapLayerRegistry.All"/>.
///
/// <para>⭐⭐ <b>CE-4044 — a full pass EVERY frame, writing only on change.</b> The mask is a pure function of the
/// entity's state at this frame, so the world is the same whatever the machine's speed and however many frames are
/// drawn between two steps. ⛔ SUPERSEDED: a time-sliced iterator with a 1 ms WALL-CLOCK budget per frame and a 3 s
/// STOPWATCH rescan — 📐 measured by the scenario verifier (coordinator, <c>bt-grenade-posture</c>, two fresh
/// processes): the golden DIFFERED at t = 2 s, <c>MapDisplayComponent</c> present on three units in one run and absent
/// in the other, the second on a loaded machine. The predicates are cheap (a DIS-type check, a <c>HasComponent</c>),
/// so the budget bought nothing that was worth a non-reproducible world.
/// 📄 docs/blueprints/batches/NOTE_Coordinator_To_Backend_Scenario_Determinism.md §2 ·
/// docs/blueprints/DESIGN_Paused_And_Stepped_Execution.md §7e.</para>
///
/// <para><b>Hot-path contract:</b> no heap allocations in <see cref="Execute"/> —
/// all iteration is over value-type ECS data and the delegate registry is a pre-built
/// <c>IReadOnlyList</c>.</para>
/// </summary>
[UpdateInPhase(SystemPhase.PostSimulation)]
[TickMode(TickPolicy.EveryFrame)]
public class MapLayerAssignmentSystem : IEcsModuleSystem
{
    private readonly IReadOnlyList<MapLayerDefinition> _layers;
    private EntityQuery? _query;

    /// <summary>
    /// Constructs the system with an optional custom layer registry.
    /// </summary>
    /// <param name="layers">
    /// Layer definition list.  Defaults to <see cref="MapLayerRegistry.All"/> when
    /// <see langword="null"/>, allowing tests to inject a minimal custom set.
    /// </param>
    public MapLayerAssignmentSystem(IReadOnlyList<MapLayerDefinition>? layers = null)
        => _layers = layers ?? MapLayerRegistry.All;

    /// <inheritdoc/>
    public void Execute(ISimulationView view, float deltaTime)
    {
        var repo = (EntityRepository)view;
        _query ??= view
            .Query()
            .With<SimTransform>()
            .WithLifecycle(EntityLifecycle.All)
            .Build();

        foreach (var entity in _query)
        {
            var disType = repo.GetDisType(entity);

            // Evaluate each layer predicate and accumulate bits.
            uint mask = 0;
            for (int i = 0; i < _layers.Count; i++)
            {
                var layer = _layers[i];
                if (layer.IsMember(entity, disType, view))
                    mask |= layer.BitMask;
            }

            // Fallback for entities with no matching layer (e.g. unclassified debug
            // entities): make them fully visible so they are never silently hidden.
            if (mask == 0)
                mask = 0xFFFF_FFFF;

            // Write component — add on first encounter, update only when changed
            // to avoid unnecessary dirty-tracking overhead.
            if (repo.HasComponent<MapDisplayComponent>(entity))
            {
                ref readonly var current =
                    ref view.GetComponentRO<MapDisplayComponent>(entity);
                if (current.LayerMask != mask)
                    repo.SetComponent(entity, new MapDisplayComponent { LayerMask = mask });
            }
            else
            {
                repo.AddComponent(entity, new MapDisplayComponent { LayerMask = mask });
            }
        }
    }
}
