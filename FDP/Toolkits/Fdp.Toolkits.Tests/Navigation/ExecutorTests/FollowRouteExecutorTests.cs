using System.Runtime.CompilerServices;
using CarKinem.Core;
using Fdp.Core;
using Fbt;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Navigation.Executors;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests.ExecutorTests
{
    /// <summary>
    /// Unit tests for <see cref="FollowRouteExecutor"/> (BCS-P3-T5 / BS1-T020).
    /// Verifies CQRS compliance: the executor writes <see cref="NavigationIntent"/>
    /// and polls <see cref="NavigationStatus"/>; it never reads <c>NavState.HasArrived</c>
    /// or writes <c>NavState.ProgressS</c> directly.
    /// </summary>
    public class FollowRouteExecutorTests
    {
        // ── Helpers ──────────────────────────────────────────────────────────────────────────────

        private static unsafe (EntityRepository world, Entity entity, LocomotionChannel channel)
            BuildWorld(int trajectoryId, byte isLooped)
        {
            var world  = NavigationTestWorldFactory.Create();
            var entity = world.CreateEntity();

            world.AddComponent(entity, new NavState());
            world.AddComponent(entity, new NavigationIntent());
            world.AddComponent(entity, new NavigationStatus());
            world.AddComponent(entity, new LocomotionChannel());

            var channel = world.GetComponent<LocomotionChannel>(entity);
            channel.ActiveAction = NavigationConstants.ActionIdFollowRoute;

            var p = new FollowRouteParams { TrajectoryId = trajectoryId, IsLooped = isLooped };
            Unsafe.Write(Unsafe.AsPointer(ref channel.Params[0]), p);

            world.SetComponent(entity, channel);
            channel = world.GetComponent<LocomotionChannel>(entity);
            return (world, entity, channel);
        }

        // ── Test 1 (BS1-T020) ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// BS1-T020: <see cref="FollowRouteExecutor.OnEnter"/> writes a
        /// <see cref="NavigationMode.FollowRoute"/> intent with the correct TrajectoryId.
        /// </summary>
        [Fact]
        public void FollowRouteExecutor_OnEnter_WritesNavigationIntentWithTrajectoryId()
        {
            const int trajectoryId = 5;
            var (world, entity, channel) = BuildWorld(trajectoryId: trajectoryId, isLooped: 0);

            var executor = new FollowRouteExecutor();
            executor.OnEnter(entity, ref channel, world);

            var intent = world.GetComponent<NavigationIntent>(entity);
            Assert.Equal(NavigationMode.FollowRoute, intent.Mode);
            Assert.Equal(trajectoryId, intent.TrajectoryId);
            Assert.Equal(NodeStatus.Running, channel.Status);
        }

        // ── Test 2 ───────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// BS1-T020: When <see cref="NavigationStatus.Result"/> is Arrived and the route is NOT looped,
        /// the executor reports <see cref="NodeStatus.Success"/>.
        /// </summary>
        [Fact]
        public void FollowRouteExecutor_ReportsSuccess_WhenArrivedAndNotLooped()
        {
            var (world, entity, channel) = BuildWorld(trajectoryId: 5, isLooped: 0);
            var executor = new FollowRouteExecutor();
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

        // ── Test 3 (BS1-T020) ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// BS1-T020: When the route completes and <see cref="FollowRouteParams.IsLooped"/> is set,
        /// the executor increments IntentId (loop-reset signal) and keeps the channel Running.
        /// It does NOT directly write <c>NavState.ProgressS</c>.
        /// </summary>
        [Fact]
        public void FollowRouteExecutor_LoopsRoute_IncrementsIntentId_NotNavState()
        {
            const int trajectoryId = 12;
            var (world, entity, channel) = BuildWorld(trajectoryId: trajectoryId, isLooped: 1);
            var executor = new FollowRouteExecutor();
            executor.OnEnter(entity, ref channel, world);

            var intentBeforeLoop = world.GetComponent<NavigationIntent>(entity);
            uint intentIdBeforeLoop = intentBeforeLoop.IntentId;

            // Simulate Arrived status.
            world.SetComponent(entity, new NavigationStatus
            {
                IntentId = intentIdBeforeLoop,
                Result   = NavigationResult.Arrived,
            });

            var navBefore = world.GetComponent<NavState>(entity);
            executor.Execute(entity, ref channel, world, 0.016f);

            // Status must stay Running — the action looped, not finished.
            Assert.Equal(NodeStatus.Running, channel.Status);

            // IntentId must have been incremented (signals the bridge to restart the route).
            var intentAfterLoop = world.GetComponent<NavigationIntent>(entity);
            Assert.True(intentAfterLoop.IntentId > intentIdBeforeLoop,
                "IntentId must be incremented on loop reset to signal the bridge system.");

            // NavState.ProgressS must NOT be modified by the executor (BS1-T020).
            var navAfter = world.GetComponent<NavState>(entity);
            Assert.Equal(navBefore.ProgressS, navAfter.ProgressS);
        }

        // ── Test 4 (BS1-T020) ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// BS1-T020: <see cref="FollowRouteExecutor.OnExit"/> clears NavigationIntent.Mode to None.
        /// </summary>
        [Fact]
        public void FollowRouteExecutor_OnExit_ClearsNavigationIntent()
        {
            var (world, entity, channel) = BuildWorld(trajectoryId: 7, isLooped: 0);
            var executor = new FollowRouteExecutor();
            executor.OnEnter(entity, ref channel, world);
            executor.OnExit(entity, ref channel, world);

            var intent = world.GetComponent<NavigationIntent>(entity);
            Assert.Equal(NavigationMode.None, intent.Mode);
        }

        /// <summary>
        /// ⭐ CE-2173 slice 3 (docs/designs/air-attack/DESIGN_Air_Operations.md §9b) — the Brain's hold release (the params' <c>ReleaseHold</c>)
        /// is mirrored onto <see cref="NavigationIntent.ReleaseHold"/> every tick as a LEVEL: on when the params say so, off again when they
        /// stop — the motion side consumes one rising edge per hold. ✅ Red-proof: mirroring only in <c>OnEnter</c> ⇒ the bit never comes on.
        /// </summary>
        [Fact]
        public unsafe void CE2173_TheBrainsHoldRelease_ReachesTheIntent_AsALevel()
        {
            var (world, entity, channel) = BuildWorld(trajectoryId: 5, isLooped: 0);
            var executor = new FollowRouteExecutor();
            executor.OnEnter(entity, ref channel, world);
            executor.Execute(entity, ref channel, world, 0.016f);
            Assert.False(world.GetComponent<NavigationIntent>(entity).ReleaseHold);

            var p = Unsafe.Read<FollowRouteParams>(Unsafe.AsPointer(ref channel.Params[0]));
            p.ReleaseHold = 1;
            Unsafe.Write(Unsafe.AsPointer(ref channel.Params[0]), p);
            uint id = world.GetComponent<NavigationIntent>(entity).IntentId;
            executor.Execute(entity, ref channel, world, 0.016f);
            Assert.True(world.GetComponent<NavigationIntent>(entity).ReleaseHold);
            Assert.Equal(id, world.GetComponent<NavigationIntent>(entity).IntentId);   // the same order — no restart

            p.ReleaseHold = 0;
            Unsafe.Write(Unsafe.AsPointer(ref channel.Params[0]), p);
            executor.Execute(entity, ref channel, world, 0.016f);
            Assert.False(world.GetComponent<NavigationIntent>(entity).ReleaseHold);
        }
    }
}
