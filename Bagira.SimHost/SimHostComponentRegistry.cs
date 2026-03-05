using Bagira.IG.Components;
using Bagira.Map.Common;
using Bagira.SimHost.Components;
using CarKinem.Commands;
using CarKinem.Formation;
using Fdp.Kernel;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Combat.Components;
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

        // ── Behaviour toolkit ─────────────────────────────────────────────────
        world.RegisterComponent<DoctrineState>();
        world.RegisterComponent<LocomotionChannel>();
        world.RegisterComponent<WeaponChannel>();
        world.RegisterComponent<InteractionChannel>();
        world.RegisterComponent<ActorCapabilityState>();
        world.RegisterComponent<BrainBTreeState>();
        world.RegisterComponent<BrainBlackboard>();
        world.RegisterComponent<MissionPlanQueue>();

        // ── Combat + perception ───────────────────────────────────────────────
        world.RegisterComponent<Faction>();
        world.RegisterComponent<PerceptionReceptor>();
        world.RegisterComponent<TargetMemory>();
        world.RegisterComponent<WeaponState>();
        world.RegisterComponent<Health>();
        world.RegisterComponent<HealthData>();
        world.RegisterComponent<BallisticProjectile>();
        world.RegisterComponent<PhysicsCollider>();

        // ── CarKinem / navigation ─────────────────────────────────────────────
        world.RegisterComponent<CarKinem.Core.VehicleState>();
        world.RegisterComponent<CarKinem.Core.VehicleParams>();
        world.RegisterComponent<CarKinem.Core.NavState>();
        world.RegisterComponent<FormationMember>();
        world.RegisterComponent<FormationRoster>();
        world.RegisterComponent<FormationTarget>();

        // ── Managed components ────────────────────────────────────────────────
        world.RegisterManagedComponent<IgEntityData>();
        world.RegisterManagedComponent<EntityMissionHolder>();

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
    }
}
