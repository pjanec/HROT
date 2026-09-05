using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Vis2D.Components;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Presentation.Map;

/// <summary>
/// PostSimulation system that assigns each map entity's <see cref="MapDisplayComponent"/>
/// bitmask by evaluating the predicates in <see cref="MapLayerRegistry.All"/>.
///
/// <para><b>Execution strategy:</b> a time-sliced iterator (<see cref="IteratorState"/>)
/// spreads the evaluation workload across multiple frames.  Once the full entity set is
/// scanned, the system waits <see cref="RescanIntervalSeconds"/> before starting a new
/// pass.  This ensures component changes (e.g. an entity acquiring
/// <c>MapOverlayStyle</c>) are reflected within a few seconds without burning an
/// excessive amount of per-frame CPU budget.</para>
///
/// <para><b>Hot-path contract:</b> no heap allocations in <see cref="Execute"/> —
/// all iteration is over value-type ECS data and the delegate registry is a pre-built
/// <c>IReadOnlyList</c>.</para>
/// </summary>
[UpdateInPhase(SystemPhase.PostSimulation)]
public class MapLayerAssignmentSystem : IEcsModuleSystem
{
    // ── Configuration ─────────────────────────────────────────────────────────

    /// <summary>
    /// Seconds to idle after a complete scan before starting the next pass.
    /// </summary>
    public const float RescanIntervalSeconds = 3.0f;

    /// <summary>
    /// Wall-clock milliseconds allocated to this system per frame for the
    /// time-sliced query.
    /// </summary>
    private const double PerFrameBudgetMs = 1.0;

    // ── State ─────────────────────────────────────────────────────────────────

    private readonly IReadOnlyList<MapLayerDefinition> _layers;
    private IteratorState _iteratorState = new();

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <summary>
    /// Constructs the system with an optional custom layer registry.
    /// </summary>
    /// <param name="layers">
    /// Layer definition list.  Defaults to <see cref="MapLayerRegistry.All"/> when
    /// <see langword="null"/>, allowing tests to inject a minimal custom set.
    /// </param>
    public MapLayerAssignmentSystem(IReadOnlyList<MapLayerDefinition>? layers = null)
        => _layers = layers ?? MapLayerRegistry.All;

    private long _lastCompletionTimestamp = 0;

    // ── IEcsModuleSystem ─────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void Execute(ISimulationView view, float deltaTime)
    {

        if (_iteratorState.IsComplete)
        {
            long currentTick = System.Diagnostics.Stopwatch.GetTimestamp();

            // Mark the timestamp the exact moment we finish a pass
            if (_lastCompletionTimestamp == 0)
            {
                _lastCompletionTimestamp = currentTick;
            }

            // Calculate real-world elapsed seconds
            double elapsedSeconds = (currentTick - _lastCompletionTimestamp) / (double)System.Diagnostics.Stopwatch.Frequency;

            if (elapsedSeconds < RescanIntervalSeconds)
                return; // Still waiting...

            // Time is up. Reset the state to begin the next full pass.
            _iteratorState.Reset();
            _lastCompletionTimestamp = 0;
        }

        var repo  = (EntityRepository)view;
        var query = view
            .Query()
            .With<SimTransform>()
            .WithLifecycle(EntityLifecycle.All)
            .Build();

        repo.QueryTimeSliced(
            query,
            _iteratorState,
            PerFrameBudgetMs,
            TimeSliceMetric.WallClockTime,
            entity =>
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
            });
    }
}
