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
    /// Unit tests for <see cref="FollowRoadGraphExecutor"/> (BCS-P3-T4).
    /// </summary>
    public class FollowRoadGraphExecutorTests
    {
        // ── Helpers ──────────────────────────────────────────────────────────────────────────────

        private static unsafe (EntityRepository world, Entity entity, LocomotionChannel channel)
            BuildWorld(int targetNodeId, float speed)
        {
            var world  = NavigationTestWorldFactory.Create();
            var entity = world.CreateEntity();

            world.AddComponent(entity, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            world.AddComponent(entity, new SimVelocity());
            world.AddComponent(entity, new NavState());
            world.AddComponent(entity, new LocomotionChannel());

            var channel = world.GetComponent<LocomotionChannel>(entity);
            channel.ActiveAction = NavigationConstants.ActionIdFollowRoadGraph;

            var p = new FollowRoadGraphParams { TargetNodeId = targetNodeId, Speed = speed };
            Unsafe.Write(Unsafe.AsPointer(ref channel.Params[0]), p);

            world.SetComponent(entity, channel);
            channel = world.GetComponent<LocomotionChannel>(entity);
            return (world, entity, channel);
        }

        // ── Test 1 ────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// <see cref="FollowRoadGraphExecutor.OnEnter"/> must configure <see cref="NavState"/> with
        /// <see cref="NavigationMode.RoadGraph"/>, the target node ID, the desired speed, and set the
        /// channel status to <see cref="NodeStatus.Running"/>.
        /// </summary>
        [Fact]
        public void FollowRoadGraphExecutor_SetsRoadGraphMode_OnEnter()
        {
            const int   targetNodeId = 42;
            const float speed        = 8f;

            var (world, entity, channel) = BuildWorld(targetNodeId, speed);
            var executor = new FollowRoadGraphExecutor();
            executor.OnEnter(entity, ref channel, world);

            var nav = world.GetComponent<NavState>(entity);
            Assert.Equal(NavigationMode.RoadGraph, nav.Mode);
            Assert.Equal(targetNodeId,             nav.CurrentSegmentId);
            Assert.Equal(speed,                    nav.TargetSpeed);
            Assert.Equal(NodeStatus.Running,       channel.Status);
        }

        // ── Test 2 ────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// When the road-graph navigation system sets <see cref="NavState.HasArrived"/> to 1,
        /// <see cref="FollowRoadGraphExecutor.Execute"/> reports <see cref="NodeStatus.Success"/>.
        /// </summary>
        [Fact]
        public void FollowRoadGraphExecutor_ReportsSuccess_WhenHasArrived()
        {
            var (world, entity, channel) = BuildWorld(targetNodeId: 7, speed: 5f);
            var executor = new FollowRoadGraphExecutor();
            executor.OnEnter(entity, ref channel, world);

            // Simulate the road navigator marking arrival.
            var nav = world.GetComponent<NavState>(entity);
            nav.HasArrived = 1;
            world.SetComponent(entity, nav);

            executor.Execute(entity, ref channel, world, 0.016f);

            Assert.Equal(NodeStatus.Success, channel.Status);
        }
    }
}
