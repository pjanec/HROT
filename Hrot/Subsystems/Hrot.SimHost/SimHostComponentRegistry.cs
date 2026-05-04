using Hrot.IG.Components;
using Hrot.Map.Common;
using Hrot.Map.Common.Components;
using CarKinem.Commands;
using CarKinem.Core;
using CarKinem.Formation;
using Fdp.Core;
using Fdp.Core.Collections;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Spatial.Eqs;

namespace Hrot.SimHost;

/// <summary>
/// Single source of truth for ECS component and event registrations required
/// by the SimHost simulation kernel.
///
/// <para>Calls <see cref="HrotSharedComponentRegistry.RegisterAll"/> first
/// (network replication, geographic, shared definitions, lifecycle events), then
/// registers the simulation-specific types: behaviour AI, CarKinem physics,
/// combat, perception, and mission management.</para>
///
/// <para>Usage example:
/// <code>
/// var world = new EntityRepository();
/// SimHostComponentRegistry.RegisterAll(world);
/// </code>
/// </para>
/// </summary>
public static class SimHostComponentRegistry
{
    /// <summary>
    /// Registers all SimHost components and events into <paramref name="world"/>.
    /// Must be called immediately after <see cref="EntityRepository"/> construction,
    /// before any module or system is initialised.
    /// </summary>
    public static void RegisterAll(EntityRepository world)
    {
        // ── Foundation: network, geographic, definitions, lifecycle events ────
        HrotSharedComponentRegistry.RegisterAll(world);

        // ── Domain-specific registries ────────────────────────────────────────
        // Each registry is responsible for its own domain's ECS components so they
        // can be composed independently by NodeBootstrapper for role-based deployments.
        CognitiveComponentRegistry.RegisterAll(world);   // Brain-tier AI + CQRS NavigationIntent
        KinematicComponentRegistry.RegisterAll(world);   // Muscle-tier physics + CQRS NavigationStatus
        CombatComponentRegistry.RegisterAll(world);      // Perception, combat, physics colliders

        // ── Unmanaged struct / managed components ─────────────────────────────
        world.RegisterComponent<EntityInfo>();
        world.RegisterManagedComponent<ActiveMissionPlan>();
        world.RegisterManagedComponent<EditablePolyline>();
        world.RegisterComponent<MapOverlayStyle>();
        world.RegisterComponent<SelectionState>();

        // ── Route planning components (ROUTES1) ───────────────────────────────
        world.RegisterManagedComponent<RoutePlan>();
        world.RegisterComponent<PersonalRouteRef>();
        world.RegisterComponent<RouteTrajectoryCache>();

        // ── Genesis Intent DTOs (transient managed; resolved by GenesisMaterializationSystem) ─
        world.RegisterManagedComponent<Hrot.Common.Serializers.InitialPassengersIntent>();
        world.RegisterManagedComponent<Hrot.Common.Serializers.InitialVehicleIntent>();
        world.RegisterManagedComponent<Hrot.Common.Serializers.InitialHierarchyIntent>();
        world.RegisterManagedComponent<Hrot.Common.Serializers.InitialRouteIntent>();
        world.RegisterManagedComponent<Hrot.Common.Serializers.InitialTargetsIntent>();
        world.RegisterManagedComponent<Hrot.Common.Serializers.InitialUnitSubordinateIntent>();

        // ── CarKinem command events ───────────────────────────────────────────
        world.RegisterEvent<CmdSpawnVehicle>();
        world.RegisterEvent<CmdCreateFormation>();
        world.RegisterEvent<CmdJoinFormation>();
        world.RegisterEvent<CmdLeaveFormation>();

        // ── Commander-Subordinate hierarchy components (commander-subordinates workstream) ─
        world.RegisterComponent<Fdp.Core.CommandHierarchy.UnitRoster>();
        world.RegisterComponent<Fdp.Core.CommandHierarchy.UnitSubordinate>();

        // ── Commander-Subordinate hierarchy events (commander-subordinates workstream) ─
        world.RegisterEvent<Fdp.Core.CommandHierarchy.CmdAssignSubordinate>();
        world.RegisterEvent<Fdp.Core.CommandHierarchy.CmdRemoveSubordinate>();
        world.RegisterEvent<Fdp.Core.CommandHierarchy.CmdAssignSubordinateRejected>();

        // ── Presentation tier ─────────────────────────────────────────────────
        // ActivePerspective singleton selects the active view (string-based, dynamic).
        world.RegisterManagedComponent<Hrot.Common.ActivePerspective>();
        // TogglePerspectiveEvent is published on FdpEventBus (managed record), not as an ECS struct event.

        // ── Mission control CQRS events (PACK-P001) ───────────────────────────
        world.RegisterEvent<Hrot.Common.Events.MissionControlAckEvent>();

        // ── Navigation batch singleton (MOD1-P6T3) ───────────────────────────
        world.SetSingleton(new PathfindingBatchData
        {
            Results  = new NativeArray<PathResult>(PathfindingBatchData.DefaultCapacity, Allocator.Persistent),
        });

        // ── Pathfinding events ────────────────────────────────────────────────
        world.RegisterEvent<Fdp.Toolkit.Navigation.PathfindingRequestEvent>();
        world.RegisterEvent<Fdp.Toolkit.Navigation.PathfindingResultEvent>();

        // ── EQS batch singletons (TASK-HA001) ────────────────────────────────
        world.SetSingleton(new AreaQueryBatchData
        {
            Results  = new NativeArray<AreaQueryResult>(AreaQueryBatchData.DefaultCapacity, Allocator.Persistent),
        });
        world.SetSingleton(new EqsTargetPool
        {
            Targets = new NativeArray<long>(EqsTargetPool.PoolCapacity, Allocator.Persistent),
        });

        // ── EQS events ────────────────────────────────────────────────────────
        world.RegisterEvent<Fdp.Toolkit.Spatial.Eqs.AreaQueryRequestEvent>();
        world.RegisterEvent<Fdp.Toolkit.Spatial.Eqs.AreaQueryResultEvent>();

        // ── Raycast events ────────────────────────────────────────────────────
        world.RegisterEvent<Fdp.Toolkit.Physics.RaycastRequestEvent>();
        world.RegisterEvent<Fdp.Toolkit.Physics.RaycastResultEvent>();
    }
}
