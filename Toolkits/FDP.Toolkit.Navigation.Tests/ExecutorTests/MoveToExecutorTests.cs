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
    /// Unit tests for <see cref="MoveToExecutor"/> (BCS-P3-T2).
    /// Covers success-on-arrival, frustration-guard failure (DEBT-016), and OnExit cleanup.
    /// </summary>
    public class MoveToExecutorTests
    {
        // ── Helpers ──────────────────────────────────────────────────────────────────────────────

        /// <summary>Create an entity with all components required by MoveToExecutor.</summary>
        private static (EntityRepository world, Entity entity, LocomotionChannel channel)
            BuildWorld(Vector2 destination, float arrivalRadius, float speed,
                       Vector3 position, Vector3 linearVelocity)
        {
            var world  = NavigationTestWorldFactory.Create();
            var entity = world.CreateEntity();

            world.AddComponent(entity, new SimTransform { Position = position, Rotation = Quaternion.Identity });
            world.AddComponent(entity, new SimVelocity  { Linear   = linearVelocity });
            world.AddComponent(entity, new NavState());
            world.AddComponent(entity, new LocomotionChannel());

            // Build the channel with MoveToParams written into Params.
            var channel = world.GetComponent<LocomotionChannel>(entity);
            channel.ActiveAction = NavigationConstants.ActionIdMoveTo;

            unsafe
            {
                var p = new MoveToParams
                {
                    Destination   = destination,
                    ArrivalRadius = arrivalRadius,
                    Speed         = speed,
                };
                Unsafe.Write(Unsafe.AsPointer(ref channel.Params[0]), p);
            }

            world.SetComponent(entity, channel);
            channel = world.GetComponent<LocomotionChannel>(entity);
            return (world, entity, channel);
        }

        // ── Test 1 ────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// When <see cref="NavState.HasArrived"/> is set to 1 (the kinematics system marks the
        /// vehicle as arrived), <see cref="MoveToExecutor.Execute"/> must report
        /// <see cref="NodeStatus.Success"/>.
        /// </summary>
        [Fact]
        public void MoveToExecutor_ReportsSuccess_WhenNavStateHasArrived()
        {
            var (world, entity, channel) = BuildWorld(
                destination:    new Vector2(10f, 10f),
                arrivalRadius:  1f,
                speed:          5f,
                position:       Vector3.Zero,
                linearVelocity: new Vector3(5f, 0f, 0f));

            var executor = new MoveToExecutor();
            executor.OnEnter(entity, ref channel, world);

            // Simulate the kinematics system signalling arrival.
            var nav = world.GetComponent<NavState>(entity);
            nav.HasArrived = 1;
            world.SetComponent(entity, nav);

            executor.Execute(entity, ref channel, world, 0.016f);

            Assert.Equal(NodeStatus.Success, channel.Status);
        }

        // ── Test 2 ────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// When the vehicle velocity stays below <see cref="NavigationConstants.FrustrationSpeedThreshold"/>
        /// and the destination is far away for more than <see cref="NavigationConstants.FrustrationTickThreshold"/>
        /// consecutive ticks, <see cref="MoveToExecutor.Execute"/> must report
        /// <see cref="NodeStatus.Failure"/>.
        /// <para>
        /// DEBT-016: the loop count references <see cref="NavigationConstants.FrustrationTickThreshold"/>
        /// directly — not the hardcoded literal 120 — so changing the constant automatically keeps
        /// this test honest.
        /// </para>
        /// </summary>
        [Fact]
        public void MoveToExecutor_ReportsFailure_WhenFrustrationThresholdExceeded()
        {
            // Entity at origin; destination far away (>> ArrivalRadius * 2).
            // Velocity is zero → stuck every tick.
            var (world, entity, channel) = BuildWorld(
                destination:    new Vector2(1000f, 1000f),
                arrivalRadius:  2f,
                speed:          5f,
                position:       Vector3.Zero,
                linearVelocity: Vector3.Zero);  // stuck — speed = 0

            var executor = new MoveToExecutor();
            executor.OnEnter(entity, ref channel, world);

            // Run exactly FrustrationTickThreshold + 1 ticks; the (+1)th tick exceeds the threshold.
            NodeStatus? lastStatus = null;
            for (int i = 0; i <= NavigationConstants.FrustrationTickThreshold; i++)
            {
                executor.Execute(entity, ref channel, world, 0.016f);
                lastStatus = channel.Status;
                if (lastStatus == NodeStatus.Failure)
                    break;
            }

            Assert.Equal(NodeStatus.Failure, lastStatus);
        }

        // ── Test 3 ────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// After <see cref="MoveToExecutor.OnExit"/> the entity's <see cref="NavState"/>
        /// must have <see cref="NavState.TargetSpeed"/> zeroed and
        /// <see cref="NavState.Mode"/> set to <see cref="NavigationMode.None"/>
        /// to stop the vehicle.
        /// </summary>
        [Fact]
        public void MoveToExecutor_OnExit_SetsNavStateSpeedToZero()
        {
            var (world, entity, channel) = BuildWorld(
                destination:    new Vector2(50f, 0f),
                arrivalRadius:  1f,
                speed:          10f,
                position:       Vector3.Zero,
                linearVelocity: new Vector3(10f, 0f, 0f));

            var executor = new MoveToExecutor();
            executor.OnEnter(entity, ref channel, world);

            // Verify OnEnter actually set speed (sanity check).
            var navAfterEnter = world.GetComponent<NavState>(entity);
            Assert.Equal(10f, navAfterEnter.TargetSpeed);

            executor.OnExit(entity, ref channel, world);

            var navAfterExit = world.GetComponent<NavState>(entity);
            Assert.Equal(0f,                  navAfterExit.TargetSpeed);
            Assert.Equal(NavigationMode.None, navAfterExit.Mode);
        }
    }
}
