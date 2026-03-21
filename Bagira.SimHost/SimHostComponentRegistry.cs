using Bagira.IG.Components;
using Bagira.Map.Common;
using Bagira.Map.Common.Components;
using Bagira.SimHost.Components;
using Bagira.SimHost.Events;
using CarKinem.Commands;
using CarKinem.Core;
using CarKinem.Formation;
using Fdp.Kernel;
using Fdp.Kernel.Collections;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Combat.Components;
using FDP.Toolkit.Navigation;
using FDP.Toolkit.Perception.Components;
using FDP.Toolkit.Physics.Components;

namespace Bagira.SimHost;

/// <summary>
/// Single source of truth for ECS component and event registrations required
/// by the SimHost simulation kernel.
///
/// <para>Calls <see cref="BagiraSharedComponentRegistry.RegisterAll"/> first
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
        BagiraSharedComponentRegistry.RegisterAll(world);

        // ── Domain-specific registries ────────────────────────────────────────
        // Each registry is responsible for its own domain's ECS components so they
        // can be composed independently by NodeBootstrapper for role-based deployments.
        CognitiveComponentRegistry.RegisterAll(world);   // Brain-tier AI + CQRS NavigationIntent
        KinematicComponentRegistry.RegisterAll(world);   // Muscle-tier physics + CQRS NavigationStatus
        CombatComponentRegistry.RegisterAll(world);      // Perception, combat, physics colliders

        // ── Unmanaged struct / managed components ─────────────────────────────
        world.RegisterComponent<EntityInfo>();
        world.RegisterManagedComponent<EntityMissionHolder>();
        world.RegisterManagedComponent<EditablePolyline>();

        // ── Route planning components (ROUTES1) ───────────────────────────────
        world.RegisterManagedComponent<RoutePlan>();
        world.RegisterComponent<PersonalRouteRef>();
        world.RegisterComponent<RouteTrajectoryCache>();

        // ── CarKinem command events ───────────────────────────────────────────
        world.RegisterEvent<CmdSpawnVehicle>();
        world.RegisterEvent<CmdCreateFormation>();
        world.RegisterEvent<CmdNavigateToPoint>();
        world.RegisterEvent<CmdFollowTrajectory>();
        world.RegisterEvent<CmdNavigateViaRoad>();
        world.RegisterEvent<CmdJoinFormation>();
        world.RegisterEvent<CmdLeaveFormation>();
        world.RegisterEvent<CmdStop>();
        world.RegisterEvent<CmdSetSpeed>();

        // ── Presentation tier ─────────────────────────────────────────────────
        // ActivePerspective singleton selects the active view (IG vs. Sim Map).
        world.RegisterComponent<ActivePerspective>();
        // TogglePerspectiveEvent lets UI code trigger a perspective switch via ECS bus.
        world.RegisterEvent<TogglePerspectiveEvent>();

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
