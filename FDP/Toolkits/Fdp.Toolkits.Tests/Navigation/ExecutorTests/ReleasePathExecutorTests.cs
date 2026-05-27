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
    /// Unit tests for <see cref="ReleasePathExecutor"/> (DD-Tests-Nav §4.5, row 13).
    /// </summary>
    public class ReleasePathExecutorTests
    {
        private static (EntityRepository world, Entity entity, LocomotionChannel channel)
            BuildWorld(int routeHandle, uint existingIntentId = 0)
        {
            var world  = NavigationTestWorldFactory.Create();
            var entity = world.CreateEntity();

            world.AddComponent(entity, new NavigationIntent { IntentId = existingIntentId });
            world.AddComponent(entity, new LocomotionChannel());

            var channel = world.GetComponent<LocomotionChannel>(entity);
            channel.ActiveAction = NavigationConstants.ActionIdReleasePath;

            unsafe
            {
                var p = new ReleasePathParams { RouteHandle = routeHandle };
                Unsafe.Write(Unsafe.AsPointer(ref channel.Params[0]), p);
            }

            world.SetComponent(entity, channel);
            channel = world.GetComponent<LocomotionChannel>(entity);
            return (world, entity, channel);
        }

        // ── Test 1: OnEnter writes intent and succeeds immediately ────────────────────────────────

        /// <summary>
        /// DD-Tests-Nav §4.5 row 13: ReleasePath_WritesNavigationIntent_ActiveActionReleasePath.
        /// OnEnter must copy <see cref="ReleasePathParams.RouteHandle"/> into
        /// <see cref="NavigationIntent.RouteHandle"/> and set channel to Success immediately.
        /// </summary>
        [Fact]
        public void ReleasePathExecutor_OnEnter_WritesIntentAndSucceeds()
        {
            const int handle = 33;
            var (world, entity, channel) = BuildWorld(handle, existingIntentId: 0);

            var executor = new ReleasePathExecutor();
            executor.OnEnter(entity, ref channel, world);

            var intent = world.GetComponent<NavigationIntent>(entity);
            Assert.Equal(handle,            intent.RouteHandle);
            Assert.Equal(NodeStatus.Success, channel.Status);
        }
    }
}
