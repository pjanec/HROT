using System.Runtime.CompilerServices;
using Fdp.Core;
using Fbt;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Executors;
using Fdp.Toolkit.Navigation.Fake;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests.ExecutorTests
{
    /// <summary>
    /// Unit tests for <see cref="FetchPathDetailsExecutor"/> (DD-Tests-Nav §4.5, rows 11-12).
    /// </summary>
    public class FetchPathDetailsExecutorTests
    {
        private static (EntityRepository world, Entity entity, LocomotionChannel channel)
            BuildWorld(int routeHandle, byte nonBlocking, uint existingIntentId = 0)
        {
            var world  = NavigationTestWorldFactory.Create();
            var entity = world.CreateEntity();

            world.AddComponent(entity, new NavigationIntent { IntentId = existingIntentId });
            world.AddComponent(entity, new NavigationStatus());
            world.AddComponent(entity, new LocomotionChannel());

            var channel = world.GetComponent<LocomotionChannel>(entity);
            channel.ActiveAction = NavigationConstants.ActionIdFetchPathDetails;

            unsafe
            {
                var p = new FetchPathDetailsParams
                {
                    RouteHandle  = routeHandle,
                    NonBlocking  = nonBlocking,
                };
                Unsafe.Write(Unsafe.AsPointer(ref channel.Params[0]), p);
            }

            world.SetComponent(entity, channel);
            channel = world.GetComponent<LocomotionChannel>(entity);
            return (world, entity, channel);
        }

        // ── Test 1: Blocking mode polls registry until cached ─────────────────────────────────────

        /// <summary>
        /// DD-Tests-Nav §4.5 row 11: FetchPathDetails_Blocking_PollsRegistryUntilCached.
        /// With NonBlocking=0, Execute must keep Running until <see cref="IPathRegistry.IsCached"/>
        /// returns true for the route handle.
        /// </summary>
        [Fact]
        public void FetchPathDetailsExecutor_Blocking_PollsRegistryUntilCached()
        {
            const int routeHandle = 17;
            var registry = new MusclePathRegistry();

            var (world, entity, channel) = BuildWorld(routeHandle, nonBlocking: 0);
            var executor = new FetchPathDetailsExecutor(registry);
            executor.OnEnter(entity, ref channel, world);

            // First Execute: not cached yet → Running.
            executor.Execute(entity, ref channel, world, 0.016f);
            Assert.Equal(NodeStatus.Running, channel.Status);

            // Store path in registry (simulates Muscle-side path result materialisation).
            registry.StoreOrReplace(routeHandle, new[]
            {
                new NavWaypoint { Position = System.Numerics.Vector3.Zero },
            });

            // Second Execute: now cached → Success.
            executor.Execute(entity, ref channel, world, 0.016f);
            Assert.Equal(NodeStatus.Success, channel.Status);
        }

        // ── Test 2: Non-blocking mode returns Success immediately ──────────────────────────────────

        /// <summary>
        /// DD-Tests-Nav §4.5 row 12: FetchPathDetails_NonBlocking_ReturnsImmediatelySuccess.
        /// With NonBlocking=1, Execute must return Success on the first call regardless of
        /// whether the path is cached.
        /// </summary>
        [Fact]
        public void FetchPathDetailsExecutor_NonBlocking_ReturnsSuccessImmediately()
        {
            const int routeHandle = 21;
            var registry = new MusclePathRegistry(); // empty — path NOT cached

            var (world, entity, channel) = BuildWorld(routeHandle, nonBlocking: 1);
            var executor = new FetchPathDetailsExecutor(registry);
            executor.OnEnter(entity, ref channel, world);

            executor.Execute(entity, ref channel, world, 0.016f);

            Assert.Equal(NodeStatus.Success, channel.Status);
        }
    }
}
