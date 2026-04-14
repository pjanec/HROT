using System;
using System.Collections.Generic;
using System.Numerics;
using Hrot.Map.Common.Components;
using CarKinem.Trajectory;
using Fdp.Kernel;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.SimHost.Systems.Routing;

/// <summary>
/// Bridges the declarative <see cref="RoutePlan"/> ECS component with the
/// deterministic <see cref="TrajectoryPoolManager"/>.
///
/// <para>
/// Runs in <see cref="SystemPhase.BeforeSync"/> — after ingress translators
/// have applied incoming DDS waypoints, but before <c>CarKinematicsSystem</c>
/// samples the trajectory pool during the Simulation phase.
/// </para>
///
/// <para>
/// On each tick the system detects version mismatches between
/// <see cref="RouteTrajectoryCache.CompiledVersion"/> and
/// <see cref="RoutePlan.Version"/>, removes the stale pool entry, and
/// registers a fresh trajectory. Entities that are destroyed between ticks
/// have their trajectory pool entries freed in the same update.
/// </para>
/// </summary>
[UpdateInPhase(SystemPhase.BeforeSync)]
public sealed class RouteTrajectorySyncSystem : ComponentSystem
{
    private readonly TrajectoryPoolManager _pool;

    /// <summary>
    /// Tracks the last-known trajectory ID per route entity so that entries
    /// can be freed when the entity is destroyed between ticks.
    /// Key = entity handle; Value = pool trajectory ID (> 0).
    /// </summary>
    private readonly Dictionary<Entity, int> _knownTrajectories = new();

    /// <summary>
    /// Default speed (m/s) used when a waypoint has <c>TargetSpeed == 0</c>
    /// (= "use entity default"). 10 m/s is a reasonable civilian default.
    /// </summary>
    private const float DefaultSpeed = 10f;

    /// <param name="pool">
    /// Shared trajectory pool. Must be the same instance passed to
    /// <c>CarKinematicsSystem</c> so both systems operate on the same entries.
    /// </param>
    public RouteTrajectorySyncSystem(TrajectoryPoolManager pool)
    {
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
    }

    /// <inheritdoc/>
    protected override void OnUpdate()
    {
        var view = (ISimulationView)World;

        // ── 1. Sync all living route entities ────────────────────────────────

        var currentEntities = new HashSet<Entity>();

        var query = World.Query()
            .WithManaged<RoutePlan>()
            .Build();

        foreach (var entity in query)
        {
            currentEntities.Add(entity);

            var routePlan = view.GetManagedComponentRO<RoutePlan>(entity);

            bool hasCache = World.HasComponent<RouteTrajectoryCache>(entity);
            var  cache    = hasCache
                ? World.GetComponent<RouteTrajectoryCache>(entity)
                : default;

            // Skip if already compiled at this version.
            if (hasCache && cache.CompiledVersion == routePlan.Version && cache.TrajectoryId > 0)
                continue;

            // Remove stale pool entry if one exists.
            if (cache.TrajectoryId > 0)
                _pool.RemoveTrajectory(cache.TrajectoryId);

            int newId = 0;
            if (routePlan.Waypoints.Count >= 2)
            {
                var positions = new Vector2[routePlan.Waypoints.Count];
                var speeds    = new float[routePlan.Waypoints.Count];

                for (int i = 0; i < routePlan.Waypoints.Count; i++)
                {
                    var wp      = routePlan.Waypoints[i];
                    positions[i] = new Vector2(wp.Position.X, wp.Position.Z);
                    speeds[i]    = wp.TargetSpeed > 0f ? wp.TargetSpeed : DefaultSpeed;
                }

                newId = _pool.RegisterTrajectory(
                    positions,
                    speeds,
                    looped:        routePlan.IsLoop,
                    interpolation: TrajectoryInterpolation.CatmullRom);
            }

            cache.TrajectoryId     = newId;
            cache.CompiledVersion  = routePlan.Version;

            if (hasCache)
                World.SetComponent(entity, cache);
            else
                World.AddComponent(entity, cache);

            // Track so we can free the pool entry on entity destruction.
            _knownTrajectories[entity] = newId;
        }

        // ── 2. Free trajectories for entities that were destroyed ─────────────

        List<Entity>? toRemove = null;

        foreach (var (trackedEntity, trajId) in _knownTrajectories)
        {
            if (!currentEntities.Contains(trackedEntity))
            {
                if (trajId > 0)
                    _pool.RemoveTrajectory(trajId);

                toRemove ??= new List<Entity>();
                toRemove.Add(trackedEntity);
            }
        }

        if (toRemove != null)
        {
            foreach (var e in toRemove)
                _knownTrajectories.Remove(e);
        }
    }
}
