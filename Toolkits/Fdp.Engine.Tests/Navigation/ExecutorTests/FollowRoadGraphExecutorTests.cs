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
    /// Unit tests for <see cref="FollowRoadGraphExecutor"/> (BCS-P3-T4 / BS1-T019).
    /// Verifies CQRS compliance: the executor writes <see cref="NavigationIntent"/>
    /// and polls <see cref="NavigationStatus"/>, never reading/writing <c>NavState</c> directly.
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
            world.AddComponent(entity, new NavigationIntent());
            world.AddComponent(entity, new NavigationStatus());
            world.AddComponent(entity, new LocomotionChannel());

            var channel = world.GetComponent<LocomotionChannel>(entity);
            channel.ActiveAction = NavigationConstants.ActionIdFollowRoadGraph;

            var p = new FollowRoadGraphParams { TargetNodeId = targetNodeId, Speed = speed };
            Unsafe.Write(Unsafe.AsPointer(ref channel.Params[0]), p);

            world.SetComponent(entity, channel);
            channel = world.GetComponent<LocomotionChannel>(entity);
            return (world, entity, channel);
        }

        // ── Test 1 (BS1-T019) ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// BS1-T019: <see cref="FollowRoadGraphExecutor.OnEnter"/> must write
        /// <see cref="NavigationIntent"/> with <see cref="NavigationMode.RoadGraph"/> and the target
        /// node ID. NavState must NOT be mutated by the executor.
        /// </summary>
        [Fact]
        public void FollowRoadGraphExecutor_OnEnter_WritesNavigationIntent_NotNavState()
        {
            const int   targetNodeId = 42;
            const float speed        = 8f;

            var (world, entity, channel) = BuildWorld(targetNodeId, speed);
            var navStateBefore = world.GetComponent<NavState>(entity);

            var executor = new FollowRoadGraphExecutor();
            executor.OnEnter(entity, ref channel, world);

            var intent = world.GetComponent<NavigationIntent>(entity);
            Assert.Equal(NavigationMode.RoadGraph, intent.Mode);
            Assert.Equal(targetNodeId,             intent.TargetNodeId);
            Assert.Equal(speed,                    intent.TargetSpeed);
            Assert.Equal(NodeStatus.Running,       channel.Status);

            // NavState must be untouched by the executor (BS1-T019: no NavState mutation).
            var navStateAfter = world.GetComponent<NavState>(entity);
            Assert.Equal(navStateBefore.Mode,            navStateAfter.Mode);
            Assert.Equal(navStateBefore.CurrentSegmentId, navStateAfter.CurrentSegmentId);
        }

        // ── Test 2 (BS1-T019) ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// BS1-T019: When <see cref="NavigationStatus.Result"/> is <see cref="NavigationResult.Arrived"/>
        /// (and IntentIds match), <see cref="FollowRoadGraphExecutor.Execute"/> reports Success —
        /// trusting NavigationStatus even when <see cref="NavState.HasArrived"/> is 0 (mismatched).
        /// </summary>
        [Fact]
        public void FollowRoadGraphExecutor_Execute_TrustsNavigationStatus_NotNavState()
        {
            var (world, entity, channel) = BuildWorld(targetNodeId: 7, speed: 5f);
            var executor = new FollowRoadGraphExecutor();
            executor.OnEnter(entity, ref channel, world);

            // Simulate NavigationExecutionSystem setting NavigationStatus (Muscle side).
            var intent = world.GetComponent<NavigationIntent>(entity);
            world.SetComponent(entity, new NavigationStatus
            {
                IntentId = intent.IntentId,
                Result   = NavigationResult.Arrived,
            });

            // NavState.HasArrived intentionally mismatched — executor must ignore it.
            var nav = world.GetComponent<NavState>(entity);
            nav.HasArrived = 0;
            world.SetComponent(entity, nav);

            executor.Execute(entity, ref channel, world, 0.016f);

            Assert.Equal(NodeStatus.Success, channel.Status);
        }

        // ── Test 3 (BS1-T019) ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// BS1-T019: When status is stale (IntentIds differ), the executor ignores it and remains Running.
        /// </summary>
        [Fact]
        public void FollowRoadGraphExecutor_Execute_IgnoresStaleStatus()
        {
            var (world, entity, channel) = BuildWorld(targetNodeId: 5, speed: 3f);
            var executor = new FollowRoadGraphExecutor();
            executor.OnEnter(entity, ref channel, world);

            // Write a stale NavigationStatus with the wrong IntentId.
            world.SetComponent(entity, new NavigationStatus
            {
                IntentId = 999,  // does not match intent.IntentId
                Result   = NavigationResult.Arrived,
            });

            executor.Execute(entity, ref channel, world, 0.016f);

            Assert.Equal(NodeStatus.Running, channel.Status);
        }

        // ── Test 4 (BS1-T019) ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// BS1-T019: <see cref="FollowRoadGraphExecutor.OnExit"/> must clear
        /// <see cref="NavigationIntent.Mode"/> to None and must NOT write to <c>NavState</c>.
        /// </summary>
        [Fact]
        public void FollowRoadGraphExecutor_OnExit_ClearsNavigationIntent_NotNavState()
        {
            var (world, entity, channel) = BuildWorld(targetNodeId: 3, speed: 4f);
            var executor = new FollowRoadGraphExecutor();
            executor.OnEnter(entity, ref channel, world);

            var navStateBefore = world.GetComponent<NavState>(entity);
            executor.OnExit(entity, ref channel, world);

            var intent = world.GetComponent<NavigationIntent>(entity);
            Assert.Equal(NavigationMode.None, intent.Mode);

            // NavState must be untouched by OnExit.
            var navStateAfter = world.GetComponent<NavState>(entity);
            Assert.Equal(navStateBefore.Mode,            navStateAfter.Mode);
            Assert.Equal(navStateBefore.CurrentSegmentId, navStateAfter.CurrentSegmentId);
        }
    }
}
