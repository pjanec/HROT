using Hrot.IG.Components;
using Hrot.Map.Common;
using Hrot.Map.Common.Components;
using CarKinem.Commands;
using CarKinem.Core;
using CarKinem.Formation;
using Fdp.Kernel;
using Fdp.Kernel.Collections;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Physics.Components;

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

        // ── CarKinem command events ───────────────────────────────────────────
        world.RegisterEvent<CmdSpawnVehicle>();
        world.RegisterEvent<CmdCreateFormation>();
        world.RegisterEvent<CmdJoinFormation>();
        world.RegisterEvent<CmdLeaveFormation>();

        // ── Presentation tier ─────────────────────────────────────────────────
        // ActivePerspective singleton selects the active view (string-based, dynamic).
        world.RegisterManagedComponent<Hrot.Common.ActivePerspective>();
        // TogglePerspectiveEvent is published on FdpEventBus (managed record), not as an ECS struct event.

        // ── Mission control CQRS events (PACK-P001) ───────────────────────────
        world.RegisterEvent<Hrot.Common.Events.MissionControlAckEvent>();

        // ── Perception toolkit receptor components (MOD1-P6T1) ──────────────────
        world.RegisterComponent<VisualReceptor>();
        world.RegisterComponent<RadarReceptor>();

        // ── Navigation batch singleton (MOD1-P6T3) ───────────────────────────
        world.SetSingleton(new PathfindingBatchData
        {
            Requests = new NativeArray<PathRequest>(PathfindingBatchData.DefaultCapacity, Allocator.Persistent),
            Results  = new NativeArray<PathResult>(PathfindingBatchData.DefaultCapacity, Allocator.Persistent),
        });
    }
}
