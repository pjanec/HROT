using CarKinem.Core;
using CarKinem.Formation;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Events;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Vis2D.Components;
using Hrot.Common.Serializers;
using Hrot.IG.Components;
using Hrot.Map.Common;
using Hrot.Map.Common.Components;

namespace Hrot.CGF;

/// <summary>
/// Single source of truth for ECS component registrations required by the CGF
/// simulation kernel.
///
/// <para>Three ordered tiers:</para>
/// <list type="number">
///   <item><term>Foundation</term>
///     <description><see cref="HrotSharedComponentRegistry.RegisterAll"/> — network
///     replication, geographic primitives, lifecycle events.</description></item>
///   <item><term>Cognitive + Kinematic</term>
///     <description>Brain-tier AI components (behavior, channels, BTree/HSM state),
///     locomotion and vehicle physics components.</description></item>
///   <item><term>IG Presentation</term>
///     <description>Components written by <c>EntityStatesIngressPack</c> translators
///     (EntityInfo, health, overlays, routes, mission plan).</description></item>
/// </list>
///
/// <para>Usage:
/// <code>
/// var world = new EntityRepository();
/// CgfComponentRegistry.RegisterAll(world);
/// </code>
/// </para>
/// </summary>
public static class CgfComponentRegistry
{
    /// <summary>
    /// Registers all CGF components and events into <paramref name="world"/>.
    /// Must be called immediately after <see cref="EntityRepository"/> construction,
    /// before any module or system is initialised.
    /// </summary>
    public static void RegisterAll(EntityRepository world)
    {
        // ── Tier 1: Foundation (network, geo, definitions, lifecycle events) ──
        HrotSharedComponentRegistry.RegisterAll(world);

        // ── Tier 2: Cognitive (Brain-tier AI) ─────────────────────────────────
        world.RegisterComponent<BehaviorState>();
        world.RegisterComponent<LocomotionChannel>();
        world.RegisterComponent<WeaponChannel>();
        world.RegisterComponent<InteractionChannel>();
        world.RegisterComponent<ActorCapabilityState>();
        world.RegisterComponent<BrainBTreeState>();
        world.RegisterComponent<BrainBlackboard>();
        world.RegisterComponent<BrainHsm128>();
        world.RegisterComponent<BrainHsm64>();
        world.RegisterComponent<MissionPlanQueue>();
        world.RegisterComponent<NavigationIntent>();
        world.RegisterComponent<PreviousCapabilities>(); 
        world.RegisterComponent<UnitRoster>();
        world.RegisterComponent<UnitSubordinate>();
        world.RegisterEvent<CmdAssignSubordinate>();
        world.RegisterEvent<CmdRemoveSubordinate>();
        world.RegisterEvent<CmdAssignSubordinateRejected>();

        // ── Tier 2: Perception (Brain-side threat awareness) ──────────────────
        // PerceptionReceptor and TargetMemory are part of the TKB entity template
        // (added by NedTkbBuilder.WithCombat). Registering them here ensures
        // that blueprint application stamps them on CGF entities, enabling the Brain node to
        // receive TargetMemory updates when the SensorTargets DDS translator is fully
        // implemented (currently a stub in PerceptionTranslators.cs).
        world.RegisterComponent<PerceptionReceptor>();
        world.RegisterComponent<TargetMemory>();
        world.RegisterComponent<ActiveSensorTracks>();
        world.RegisterComponent<Fdp.Toolkit.Combat.Components.WeaponState>();

        // ── Tier 2: Kinematic (Muscle-tier physics) ───────────────────────────
        world.RegisterComponent<VehicleState>();
        world.RegisterComponent<VehicleParams>();
        world.RegisterComponent<NavState>();
        world.RegisterComponent<FormationFollower>();
        world.RegisterComponent<FormationController>();
        world.RegisterComponent<FormationTarget>();
        world.RegisterComponent<NavigationStatus>();
        world.RegisterComponent<FrustrationTicks>();

        // ── Tier 3: IG presentation ───────────────────────────────────────────
        // EntityInfoIngressTranslator writes EntityInfo (ID 164).
        world.RegisterComponent<EntityInfo>();
        // EntityDamageIngressTranslator writes IgHealthState (ID 165).
        world.RegisterComponent<IgHealthState>();
        // MapVisualOverlayIngressTranslator writes EditablePolyline + MapOverlayStyle.
        world.RegisterManagedComponent<EditablePolyline>();
        world.RegisterComponent<MapOverlayStyle>();
        world.RegisterComponent<MapDisplayComponent>();
        // MapRouteIngressTranslator writes RoutePlan, PersonalRouteRef, RouteTrajectoryCache.
        world.RegisterManagedComponent<RoutePlan>();
        world.RegisterComponent<PersonalRouteRef>();
        world.RegisterComponent<RouteTrajectoryCache>();

        // MissionAdapterSystem uses MissionAdapterState to track phase-change transitions.
        world.RegisterComponent<Hrot.CGF.Components.MissionAdapterState>();

        // ── Combat pipeline events (Brain-tier ingress) ───────────────────────
        // EntityHitDamageIngressTranslator publishes DamageAssessedEvent via EntityCommandBuffer;
        // the stream must exist before Playback runs, so we pre-register it here.
        world.RegisterEvent<DamageAssessedEvent>();
        world.RegisterEvent<WeaponFireIntent>();
        world.RegisterEvent<SensorTrackStateEvent>();


        world.RegisterComponent<PhysicsCollider>();
        world.RegisterManagedComponent<ZoneMembership>();
        // EntityMissionIngressTranslator and mission feedback write ActiveMissionPlan.
        world.RegisterManagedComponent<ActiveMissionPlan>();
        world.RegisterManagedComponent<InitialUnitSubordinateIntent>();
        world.RegisterManagedComponent<InitialTargetsIntent>();
        world.RegisterManagedComponent<InitialPassengersIntent>();
        world.RegisterManagedComponent<InitialVehicleIntent>();
        world.RegisterManagedComponent<InitialHierarchyIntent>();
        world.RegisterManagedComponent<InitialRouteIntent>();

    }
}
