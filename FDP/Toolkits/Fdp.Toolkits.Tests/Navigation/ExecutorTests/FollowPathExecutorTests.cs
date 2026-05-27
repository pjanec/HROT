using System.Runtime.CompilerServices;
using Fdp.Core;
using Fbt;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Executors;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests.ExecutorTests
{
    /// <summary>
    /// Unit tests for <see cref="FollowPathExecutor"/> (DD-Tests-Nav §4.5, row 10).
    /// </summary>
    public class FollowPathExecutorTests
    {
        private static (EntityRepository world, Entity entity, LocomotionChannel channel)
            BuildWorld(int routeHandle, float speed = 10f, uint existingIntentId = 0)
        {
            var world  = NavigationTestWorldFactory.Create();
            var entity = world.CreateEntity();

            world.AddComponent(entity, new NavigationIntent { IntentId = existingIntentId });
            world.AddComponent(entity, new NavigationStatus());
            world.AddComponent(entity, new LocomotionChannel());

            var channel = world.GetComponent<LocomotionChannel>(entity);
            channel.ActiveAction = NavigationConstants.ActionIdFollowPath;

            unsafe
            {
                var p = new FollowPathParams
                {
                    RouteHandle = routeHandle,
                    Speed       = speed,
                };
                Unsafe.Write(Unsafe.AsPointer(ref channel.Params[0]), p);
            }

            world.SetComponent(entity, channel);
            channel = world.GetComponent<LocomotionChannel>(entity);
            return (world, entity, channel);
        }

        // ── Test 1: OnEnter writes intent with the provided handle ────────────────────────────────

        /// <summary>
        /// DD-Tests-Nav §4.5 row 10: FollowPath_WritesNavigationIntent_WithProvidedHandle.
        /// OnEnter must copy <see cref="FollowPathParams.RouteHandle"/> into
        /// <see cref="NavigationIntent.RouteHandle"/> and set channel to Running.
        /// </summary>
        [Fact]
        public void FollowPathExecutor_OnEnter_WritesIntentWithHandle()
        {
            const int handle = 99;
            var (world, entity, channel) = BuildWorld(handle);

            var executor = new FollowPathExecutor();
            executor.OnEnter(entity, ref channel, world);

            var intent = world.GetComponent<NavigationIntent>(entity);
            Assert.Equal(handle,            intent.RouteHandle);
            Assert.Equal(NodeStatus.Running, channel.Status);
        }

        // ── Test 2: Arrived → Success ─────────────────────────────────────────────────────────────

        [Fact]
        public void FollowPathExecutor_Execute_Arrived_ReturnsSuccess()
        {
            var (world, entity, channel) = BuildWorld(routeHandle: 5);

            var executor = new FollowPathExecutor();
            executor.OnEnter(entity, ref channel, world);

            var intent = world.GetComponent<NavigationIntent>(entity);
            world.SetComponent(entity, new NavigationStatus
            {
                IntentId = intent.IntentId,
                Result   = NavigationResult.Arrived,
            });

            executor.Execute(entity, ref channel, world, 0.016f);

            Assert.Equal(NodeStatus.Success, channel.Status);
        }

        // ── Test 3: FailedInvalidHandle → Failure ─────────────────────────────────────────────────

        [Fact]
        public void FollowPathExecutor_Execute_FailedInvalidHandle_ReturnsFailure()
        {
            var (world, entity, channel) = BuildWorld(routeHandle: 0);

            var executor = new FollowPathExecutor();
            executor.OnEnter(entity, ref channel, world);

            var intent = world.GetComponent<NavigationIntent>(entity);
            world.SetComponent(entity, new NavigationStatus
            {
                IntentId = intent.IntentId,
                Result   = NavigationResult.FailedInvalidHandle,
            });

            executor.Execute(entity, ref channel, world, 0.016f);

            Assert.Equal(NodeStatus.Failure, channel.Status);
        }
    }
}
