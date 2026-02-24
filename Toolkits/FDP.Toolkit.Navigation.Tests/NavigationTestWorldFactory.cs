using CarKinem.Core;
using Fdp.Kernel;
using FDP.Toolkit.Behavior.Components;

namespace FDP.Toolkit.Navigation.Tests
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

            // Core spatial/velocity components required by all executor Execute() methods.
            world.RegisterComponent<SimTransform>();
            world.RegisterComponent<SimVelocity>();

            // CarKinem navigation state — written by OnEnter and read by Execute.
            world.RegisterComponent<NavState>();

            // Behavior channel — holds action params, state payload, and status.
            world.RegisterComponent<LocomotionChannel>();

            // Seed GlobalTime singleton so FleeExecutor can read FrameNumber for throttled replan.
            world.SetSingletonUnmanaged(new GlobalTime { FrameNumber = 0 });

            return world;
        }
    }
}
