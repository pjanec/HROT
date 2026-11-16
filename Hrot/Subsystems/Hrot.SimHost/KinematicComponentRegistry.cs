using CarKinem.Core;
using CarKinem.Formation;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Navigation;

namespace Hrot.SimHost
{
    /// <summary>
    /// ECS component registry for kinematic / Muscle-tier components.
    ///
    /// <para>Registers: vehicle state and params, navigation state, formation membership,
    /// and the CQRS <see cref="NavigationStatus"/> reply component together with the
    /// frustration-tick counter for stuck-detection.</para>
    ///
    /// <para>
    /// Components not registered here (e.g. SimTransform, SimVelocity) are owned by
    /// <c>HrotSharedComponentRegistry</c> and must not be duplicated.
    /// Call <c>HrotSharedComponentRegistry.RegisterAll</c> before this method.
    /// </para>
    /// </summary>
    public static class KinematicComponentRegistry
    {
        /// <summary>
        /// Registers all kinematic simulation components into <paramref name="world"/>.
        /// </summary>
        public static void RegisterAll(EntityRepository world)
        {
            world.RegisterComponent<VehicleState>();
            world.RegisterComponent<VehicleParams>();
            world.RegisterComponent<NavState>();
            world.RegisterComponent<PathTrace>();   // ⭐ CE-3117 — the planned path, for the map (R-226)
            world.RegisterComponent<FormationFollower>();
            world.RegisterComponent<FormationController>();
            world.RegisterComponent<FormationTarget>();
            world.RegisterComponent<CarKinem.Convoy.ConvoyMember>();     // ⭐ CE-9001 — the road convoy (DESIGN_Road_Convoy C-4): the vehicle's place in a column …
            world.RegisterComponent<CarKinem.Convoy.ConvoyTrail>();      // … the lead's actual path …
            world.RegisterComponent<CarKinem.Convoy.ConvoyProgress>();   // … and a follower's place on it

            // CQRS navigation status — written by the Muscle tier (NavigationExecutionSystem)
            // and read by the Brain tier (MoveToExecutor).
            world.RegisterComponent<NavigationStatus>();

            // Per-entity stuck-detection counter (replaces static dictionary).
            world.RegisterComponent<FrustrationTicks>();
            world.RegisterComponent<DoorPassage>();   // ⭐ R-3039 S1 — a walker's passage through a doorway (DoorPassageSystem)

            world.RegisterEvent<MoveStartedEvent>();
            world.RegisterEvent<OffMeshTraversalStartedEvent>();
            world.RegisterEvent<MoveCompletedEvent>();
            world.RegisterEvent<MoveBlockedEvent>();
            world.RegisterEvent<WaypointReachedEvent>();
            world.RegisterEvent<PathReplannedEvent>();
            world.RegisterEvent<OffMeshTraversalEndedEvent>();
            world.RegisterEvent<NavigationPathDetailsResponseEvent>();

            // Commander-Subordinate hierarchy components (AI tier)
            world.RegisterComponent<UnitSubordinate>();
            world.RegisterComponent<UnitRoster>();
        }
    }
}
