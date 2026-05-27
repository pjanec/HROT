using CarKinem.Core;
using Fdp.Core;
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

            // CarKinem navigation state — still used by FollowRouteExecutor and FleeExecutor.
            world.RegisterComponent<NavState>();

            // CQRS navigation contract components — used by the refactored MoveToExecutor
            // and written by NavigationExecutionSystem.
            world.RegisterComponent<NavigationIntent>();
            world.RegisterComponent<NavigationStatus>();

            // Behavior channel — holds action params, state payload, and status.
            world.RegisterComponent<LocomotionChannel>();

            // Phase 1 corridor + crowd components — required by Phase 2+ systems and tests.
            world.RegisterComponent<NavigationCorridorMuscle>();
            world.RegisterComponent<NavigationCorridorPreview>();
            world.RegisterComponent<NavigationPathDetailsBuffer>();
            world.RegisterComponent<CrowdAgent>();
            world.RegisterComponent<NavAgentProfile>();

            // Seed GlobalTime singleton so FleeExecutor can read FrameNumber for throttled replan.
            world.SetSingletonUnmanaged(new GlobalTime { FrameNumber = 0 });

            // Frustration tracking — written exclusively by NavigationExecutionSystem.
            world.RegisterComponent<FrustrationTicks>();

            // Navigation lifecycle events — required by MoveToExecutor event-emission tests
            // and by NavigationPathDetailsUpdateSystem tests.
            world.RegisterEvent<MoveCompletedEvent>();
            world.RegisterEvent<NavigationPathDetailsResponseEvent>();
            world.RegisterEvent<NavigationPathDetailsArrivedEvent>();

            // Phase 5 replan-flow events — required by NavigationExecutionSystem replan tests.
            world.RegisterEvent<MoveStartedEvent>();
            world.RegisterEvent<PathReplannedEvent>();
            world.RegisterEvent<MoveBlockedEvent>();
            world.RegisterEvent<PathfindingRequestEvent>();
            world.RegisterEvent<WaypointReachedEvent>();

            return world;
        }
    }
}
