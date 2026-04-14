using CarKinem.Core;
using CarKinem.Formation;
using Fdp.Core;
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
            world.RegisterComponent<FormationMember>();
            world.RegisterComponent<FormationRoster>();
            world.RegisterComponent<FormationTarget>();

            // CQRS navigation status — written by the Muscle tier (NavigationExecutionSystem)
            // and read by the Brain tier (MoveToExecutor).
            world.RegisterComponent<NavigationStatus>();

            // Per-entity stuck-detection counter (replaces static dictionary).
            world.RegisterComponent<FrustrationTicks>();
        }
    }
}
