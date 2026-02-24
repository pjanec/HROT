using System.Numerics;
using System.Runtime.CompilerServices;
using CarKinem.Core;
using Fdp.Kernel;
using Fbt;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Navigation.Executors;
using Xunit;

namespace FDP.Toolkit.Navigation.Tests.ExecutorTests
{
    /// <summary>
    /// Unit tests for <see cref="FollowRouteExecutor"/> (BCS-P3-T5).
    /// Covers single-run completion and loop-restart behaviour.
    /// </summary>
    public class FollowRouteExecutorTests
    {
        // ── Helpers ──────────────────────────────────────────────────────────────────────────────

        private static unsafe (EntityRepository world, Entity entity, LocomotionChannel channel)
            BuildWorld(int trajectoryId, byte isLooped)
        {
            var world  = NavigationTestWorldFactory.Create();
            var entity = world.CreateEntity();

            world.AddComponent(entity, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            world.AddComponent(entity, new SimVelocity());
            world.AddComponent(entity, new NavState());
            world.AddComponent(entity, new LocomotionChannel());

            var channel = world.GetComponent<LocomotionChannel>(entity);
            channel.ActiveAction = NavigationConstants.ActionIdFollowRoute;

            var p = new FollowRouteParams { TrajectoryId = trajectoryId, IsLooped = isLooped };
            Unsafe.Write(Unsafe.AsPointer(ref channel.Params[0]), p);

            world.SetComponent(entity, channel);
            channel = world.GetComponent<LocomotionChannel>(entity);
            return (world, entity, channel);
        }

        // ── Test 1 ────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// When the route completes (<see cref="NavState.HasArrived"/> == 1) and
        /// <see cref="FollowRouteParams.IsLooped"/> is 0, the executor reports
        /// <see cref="NodeStatus.Success"/>.
        /// </summary>
        [Fact]
        public void FollowRouteExecutor_ReportsSuccess_WhenRouteCompleteAndNotLooped()
        {
            var (world, entity, channel) = BuildWorld(trajectoryId: 5, isLooped: 0);
            var executor = new FollowRouteExecutor();
            executor.OnEnter(entity, ref channel, world);

            // Kinematics marks the route as completed.
            var nav = world.GetComponent<NavState>(entity);
            nav.HasArrived = 1;
            world.SetComponent(entity, nav);

            executor.Execute(entity, ref channel, world, 0.016f);

            Assert.Equal(NodeStatus.Success, channel.Status);
        }

        // ── Test 2 ────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// When the route completes and <see cref="FollowRouteParams.IsLooped"/> is non-zero,
        /// the executor resets <see cref="NavState.HasArrived"/> and <see cref="NavState.ProgressS"/>
        /// to restart the route, keeping the channel status as <see cref="NodeStatus.Running"/>.
        /// The <see cref="NavState.TrajectoryId"/> must still equal the original trajectory ID.
        /// </summary>
        [Fact]
        public void FollowRouteExecutor_LoopsRoute_WhenFlagSet()
        {
            const int trajectoryId = 12;
            var (world, entity, channel) = BuildWorld(trajectoryId: trajectoryId, isLooped: 1);
            var executor = new FollowRouteExecutor();
            executor.OnEnter(entity, ref channel, world);

            // Route completes.
            var nav = world.GetComponent<NavState>(entity);
            nav.HasArrived = 1;
            world.SetComponent(entity, nav);

            executor.Execute(entity, ref channel, world, 0.016f);

            // Status must stay Running — the action looped, not finished.
            Assert.Equal(NodeStatus.Running, channel.Status);

            // NavState must be re-armed: the trajectory ID is preserved, progress reset, HasArrived cleared.
            var navAfter = world.GetComponent<NavState>(entity);
            Assert.Equal(trajectoryId, navAfter.TrajectoryId);
            Assert.Equal(0,            navAfter.HasArrived);
            Assert.Equal(0f,           navAfter.ProgressS);
        }
    }
}
