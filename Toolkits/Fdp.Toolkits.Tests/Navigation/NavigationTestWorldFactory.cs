using CarKinem.Core;
using Fdp.Kernel;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Navigation;

namespace Fdp.Toolkit.Navigation.Tests
{
    /// <summary>
    /// Creates a fully-registered <see cref="EntityRepository"/> for navigation executor unit tests.
    /// Registers all components consumed by the Navigation executor classes.
    /// </summary>
    public static class NavigationTestWorldFactory
    {
        public static EntityRepository Create()
        {
            var world = new EntityRepository();

            // Core spatial/velocity components required by legacy executors.
            world.RegisterComponent<SimTransform>();
            world.RegisterComponent<SimVelocity>();

            // CarKinem navigation state — still used by FollowRouteExecutor,
            // FollowRoadGraphExecutor, and FleeExecutor.
            world.RegisterComponent<NavState>();

            // CQRS navigation contract components — used by the refactored MoveToExecutor
            // and written by NavigationExecutionSystem.
            world.RegisterComponent<NavigationIntent>();
            world.RegisterComponent<NavigationStatus>();

            // Behavior channel — holds action params, state payload, and status.
            world.RegisterComponent<LocomotionChannel>();

            // Seed GlobalTime singleton so FleeExecutor can read FrameNumber for throttled replan.
            world.SetSingletonUnmanaged(new GlobalTime { FrameNumber = 0 });

            return world;
        }
    }
}
