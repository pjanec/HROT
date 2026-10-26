using System;
using System.Collections.Generic;
using System.Numerics;
using Hrot.Map.Common.Components;
using Hrot.Map.Common.Events;
using Hrot.Map.Common;
using CarKinem.Commands;
using Fdp.Core;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Replication.Components;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.SimHost.Systems.Routing;

/// <summary>
/// Processes <see cref="CmdAppendPersonalWaypoint"/> events to create or mutate
/// a vehicle-owned child route entity for the targeted vehicle.
///
/// <para>
/// Runs in <see cref="SystemPhase.Input"/> so the spawned entity is visible to
/// <see cref="RouteTrajectorySyncSystem"/> (BeforeSync) in the same frame.
/// </para>
///
/// <b>Flow:</b>
/// <list type="bullet">
///   <item>
///     If the vehicle has no <see cref="PersonalRouteRef"/>, spawn a new child
///     route entity seeded with two waypoints (vehicle's current position and the
///     clicked position), attach <see cref="PersonalRouteRef"/> to the vehicle.
///   </item>
///   <item>
///     If a <see cref="PersonalRouteRef"/> already exists, append the new waypoint
///     to the existing <see cref="RoutePlan"/> via <see cref="RoutePlan.Mutate"/>.
///   </item>
///   <item>
///     After the entity command buffer is flushed, issue
///     <see cref="CmdFollowTrajectory"/> so the vehicle immediately starts
///     following the updated route. The command is deferred by one frame to
///     ensure <see cref="RouteTrajectorySyncSystem"/> has compiled the new trajectory.
///   </item>
/// </list>
/// </summary>
[UpdateInPhase(SystemPhase.Input)]
[TickMode(TickPolicy.EveryFrame)]
public sealed class PersonalRouteAuthoringSystem : IEcsModuleSystem
{
    // Follow-trajectory commands are deferred by one frame so that RouteTrajectorySyncSystem (BeforeSync) has time to compile the
    // new/updated trajectory before we issue the follow command. ⭐ R-3039 S1 — the deferral is PersonalRouteRef.FollowPending on the
    // vehicle (the ref already names the route), never a list here: a checkpoint in that frame keeps it.

    /// <inheritdoc/>
    public void Execute(ISimulationView view, float deltaTime)
    {
        if (view is not EntityRepository repo)
            throw new InvalidOperationException(
                $"{nameof(PersonalRouteAuthoringSystem)} requires direct EntityRepository access " +
                $"and cannot run on a read-only snapshot ({view.GetType().Name}).");

        // -- 1. Dispatch deferred NavigationIntent (FollowRoute) from the previous frame --
        if (repo.IsComponentTypeRegistered<PersonalRouteRef>())
            foreach (var vehicle in repo.Query().With<PersonalRouteRef>().Build())
            {
                if (repo.GetComponentRO<PersonalRouteRef>(vehicle).FollowPending == 0) continue;
                var routeEntity = repo.GetComponentRO<PersonalRouteRef>(vehicle).RouteEntity;
                repo.GetComponentRW<PersonalRouteRef>(vehicle).FollowPending = 0;

                if (!view.IsAlive(routeEntity))
                    continue;

                if (!view.HasComponent<RouteTrajectoryCache>(routeEntity))
                    continue;

                ref readonly var cache = ref view.GetComponentRO<RouteTrajectoryCache>(routeEntity);
                if (cache.TrajectoryId > 0)
                {
                    NavigationIntent intent = repo.HasComponent<NavigationIntent>(vehicle)
                        ? repo.GetComponent<NavigationIntent>(vehicle)
                        : new NavigationIntent();
                    intent.IntentId++;
                    intent.Mode = NavigationMode.FollowRoute;
                    intent.TrajectoryId = cache.TrajectoryId;
                    if (repo.HasComponent<NavigationIntent>(vehicle))
                        repo.SetComponent(vehicle, intent);
                    else
                        repo.AddComponent(vehicle, intent);
                }
            }

        // -- 2. Process CmdAppendPersonalWaypoint events --
        var events = repo.ReadEvents<CmdAppendPersonalWaypoint>();
        if (events.Length == 0) return;

        foreach (var evt in events)
        {
            var vehicleEntity = evt.VehicleEntity;
            var clickedPos    = evt.WorldPosition;

            // Silently ignore dead or unknown vehicle entities.
            if (!view.IsAlive(vehicleEntity))
                continue;

            if (view.HasComponent<PersonalRouteRef>(vehicleEntity))
            {
                // ── Case B: Personal route already exists — append waypoint ──────
                ref readonly var routeRef  = ref view.GetComponentRO<PersonalRouteRef>(vehicleEntity);
                var routeEntity = routeRef.RouteEntity;

                if (!view.IsAlive(routeEntity) || !view.HasManagedComponent<RoutePlan>(routeEntity))
                    continue;

                var existingPlan = view.GetManagedComponentRO<RoutePlan>(routeEntity);
                // CE-4046 — RoutePlan is an immutable record: the appended plan is a NEW value, Set back.
                repo.SetManagedComponent(routeEntity, existingPlan.WithWaypoints(wps => wps.Add(new RouteWaypoint
                {
                    Position    = clickedPos,
                    TargetSpeed = 0f,
                })));

                repo.GetComponentRW<PersonalRouteRef>(vehicleEntity).FollowPending = 1;
            }
            else
            {
                // ── Case A: No personal route — spawn new child route entity ─────
                // Use direct World mutations (not ECB) so the real entity handle
                // can be embedded in PersonalRouteRef immediately.
                var vehiclePos = repo.HasComponent<SimTransform>(vehicleEntity)
                    ? view.GetComponentRO<SimTransform>(vehicleEntity).Position
                    : Vector3.Zero;

                var newPlan = new RoutePlan { IsLoop = false }.WithWaypoints(wps =>
                {
                    wps.Add(new RouteWaypoint { Position = vehiclePos, TargetSpeed = 0f });
                    wps.Add(new RouteWaypoint { Position = clickedPos, TargetSpeed = 0f });
                });

                var childEntity = repo.CreateEntity();
                repo.SetManagedComponent(childEntity, newPlan);
                repo.AddComponent(childEntity, new PartMetadata { ParentEntity = vehicleEntity });
                repo.AddComponent(childEntity, new TkbIdentity { TkbType = TkbEntityTypes.TacGraphic_Route });
                repo.AddComponent(childEntity, new SimTransform { Position = vehiclePos });

                repo.AddComponent(vehicleEntity, new PersonalRouteRef { RouteEntity = childEntity, FollowPending = 1 });
            }
        }
    }
}
