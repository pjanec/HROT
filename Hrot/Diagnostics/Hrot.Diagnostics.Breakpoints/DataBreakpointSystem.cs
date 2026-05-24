using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Diagnostics.Breakpoints;

/// <summary>
/// ECS system that evaluates compiled component predicates and event scanners
/// against the live simulation state each tick.
///
/// Scheduled in <see cref="SystemPhase.PostSimulation"/> so it runs after all module
/// ticks have completed for the frame, but before any rewind is applied.
///
/// Early-out path: if <see cref="IDataBreakpointManager.HasMountedDelegates"/> is false,
/// Execute returns without touching the repository (zero allocation, zero work).
/// </summary>
[UpdateInPhase(SystemPhase.PostSimulation)]
public sealed class DataBreakpointSystem : IEcsModuleSystem
{
    private readonly IDataBreakpointManager _manager;
    private readonly FdpEventBus? _bus;

    /// <summary>
    /// Creates a <see cref="DataBreakpointSystem"/> with component-path support only.
    /// </summary>
    public DataBreakpointSystem(IDataBreakpointManager manager)
        : this(manager, null)
    {
    }

    /// <summary>
    /// Creates a <see cref="DataBreakpointSystem"/> with both component-path and event-path support.
    /// </summary>
    /// <param name="manager">The breakpoint manager that owns breakpoint state.</param>
    /// <param name="bus">
    /// The live event bus. Required for event-scanner breakpoints; pass null to disable the event path.
    /// </param>
    public DataBreakpointSystem(IDataBreakpointManager manager, FdpEventBus? bus)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _bus     = bus;
    }

    /// <inheritdoc/>
    public void Execute(ISimulationView view, float deltaTime)
    {
        // Fast early-out: no compiled delegates mounted.
        // This method intentionally contains no lambdas so that the compiler
        // does not emit closure allocations before this guard is reached.
        if (!_manager.HasMountedDelegates) return;
        ExecuteCore(view);
    }

    // All lambda/closure code lives here so Execute itself stays allocation-free.
    private void ExecuteCore(ISimulationView view)
    {
        if (view is not EntityRepository repo)
            throw new InvalidOperationException(
                $"DataBreakpointSystem requires EntityRepository, got {view?.GetType().Name ?? "null"}.");

        // ---- Component-data path ----------------------------------------

        foreach (var (bp, compiled) in _manager.MountedComponentPredicates)
        {
            // Build a query that filters to entities with all mandatory components.
            var queryBuilder = repo.Query();
            foreach (var t in compiled.MandatoryComponents)
            {
                // TODO: optimise by tracking the last-scanned version per breakpoint
                //       and passing it here instead of 0 so unchanged entities are skipped.
                int componentId = ComponentTypeRegistry.GetId(t);
                if (componentId >= 0)
                    queryBuilder = queryBuilder.WithComponentId(componentId);
            }
            var query = queryBuilder.Build();

            // Collect matches first; OnHit modifies liveRepo (SyncFrom rewind) so it
            // must NOT be called inside the QueryDelta callback.
            var pendingHits = new List<Entity>();

            // sinceVersion = 0 scans all entities every tick.
            repo.QueryDelta(query, 0u, entity =>
            {
                if (bp.FilterEntity is { } filterEntity && filterEntity != entity) return;
                if (!compiled.Delegate(repo, entity)) return;
                pendingHits.Add(entity);
            });

            foreach (var hitEntity in pendingHits)
                _manager.OnHit(bp, hitEntity);
        }

        // ---- Event path -------------------------------------------------

        if (_bus != null)
        {
            foreach (var (bp, scanner) in _manager.MountedEventScanners)
            {
                if (scanner.Evaluate(_bus, repo))
                    _manager.OnHit(bp, Entity.Null);
            }
        }

        // ---- Stateful trackers (structural / spatial / lifecycle) --------

        if (_manager.HasStatefulTrackers)
            _manager.EvaluateStatefulBreakpoints(repo);
    }
}
