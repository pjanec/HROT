using System.Numerics;
using System.Runtime.CompilerServices;
using Fdp.Kernel;
using Fbt;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Navigation;
using FDP.Toolkit.Navigation.Executors;
using Xunit;

namespace FDP.Toolkit.Navigation.Tests.ExecutorTests
{
    /// <summary>
    /// Unit tests for the refactored CQRS <see cref="MoveToExecutor"/> (MOD1-P1T2).
    /// <para>
    /// The executor is a pure observer of <see cref="NavigationStatus"/>.
    /// It has no physics awareness: no geo conversion, no NavState, no SimTransform reads.
    /// </para>
    /// </summary>
    public class MoveToExecutorTests
    {
        // ── Helpers ──────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Creates a world and an entity equipped for CQRS MoveToExecutor tests.
        /// </summary>
        private static (EntityRepository world, Entity entity, LocomotionChannel channel)
            BuildWorld(Vector2 destination, float arrivalRadius, float speed,
                       uint existingIntentId = 0)
        {
            var world  = NavigationTestWorldFactory.Create();
            var entity = world.CreateEntity();

            world.AddComponent(entity, new NavigationIntent { IntentId = existingIntentId });
            world.AddComponent(entity, new NavigationStatus());
            world.AddComponent(entity, new LocomotionChannel());

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

        // ── Test 1: OnEnter increments IntentId and writes Intent ────────────────────────────────

        /// <summary>
        /// MOD1-P1T2 T2: <see cref="MoveToExecutor.OnEnter"/> must increment
        /// <see cref="NavigationIntent.IntentId"/>, set Mode to DirectPoint, and copy the
        /// raw Cartesian destination directly — no geo conversion.
        /// </summary>
        [Fact]
        public void MoveToExecutor_OnEnter_WritesNavigationIntentWithIncrementedId()
        {
            var destination = new Vector2(300f, 150f);
            var (world, entity, channel) = BuildWorld(destination, arrivalRadius: 5f, speed: 15f,
                                                      existingIntentId: 5);

            var executor = new MoveToExecutor();
            executor.OnEnter(entity, ref channel, world);

            var intent = world.GetComponent<NavigationIntent>(entity);

            Assert.Equal(6u, intent.IntentId);                        // incremented from 5
            Assert.Equal(NavigationMode.DirectPoint, intent.Mode);    // set to DirectPoint
            Assert.Equal(destination, intent.FinalDestination);       // raw Cartesian copy
            Assert.Equal(15f, intent.TargetSpeed);
            Assert.Equal(5f,  intent.ArrivalRadius);
            Assert.Equal(NodeStatus.Running, channel.Status);
        }

        // ── Test 2: Execute returns Success when status is Arrived ────────────────────────────────

        /// <summary>
        /// MOD1-P1T2 T3: When <see cref="NavigationStatus.Result"/> is Arrived and the
        /// intent IDs match, <see cref="MoveToExecutor.Execute"/> must set channel status
        /// to Success.
        /// </summary>
        [Fact]
        public void MoveToExecutor_Execute_ReturnsSuccessWhenStatusArrived()
        {
            var (world, entity, channel) = BuildWorld(
                new Vector2(100f, 0f), arrivalRadius: 5f, speed: 10f, existingIntentId: 5);

            var executor = new MoveToExecutor();
            executor.OnEnter(entity, ref channel, world);

            // IntentId is now 6 after OnEnter. Set status with matching id.
            var intent = world.GetComponent<NavigationIntent>(entity);
            world.SetComponent(entity, new NavigationStatus
            {
                IntentId = intent.IntentId,
                Result   = NavigationResult.Arrived,
            });

            executor.Execute(entity, ref channel, world, 0.016f);

            Assert.Equal(NodeStatus.Success, channel.Status);
        }

        // ── Test 3: Execute ignores stale status ──────────────────────────────────────────────────

        /// <summary>
        /// MOD1-P1T2 T4: When <see cref="NavigationStatus.IntentId"/> does not match
        /// <see cref="NavigationIntent.IntentId"/>, the executor must keep Running
        /// (status is stale from a prior command).
        /// </summary>
        [Fact]
        public void MoveToExecutor_Execute_IgnoresStaleStatus()
        {
            var (world, entity, channel) = BuildWorld(
                new Vector2(100f, 0f), arrivalRadius: 5f, speed: 10f, existingIntentId: 5);

            var executor = new MoveToExecutor();
            executor.OnEnter(entity, ref channel, world);  // IntentId becomes 6

            // Write a status for an OLD intent id (stale).
            world.SetComponent(entity, new NavigationStatus
            {
                IntentId = 3,                               // stale — does not match 6
                Result   = NavigationResult.Arrived,
            });

            // Channel was set to Running by OnEnter. It must stay Running after Execute.
            var statusBefore = channel.Status;
            executor.Execute(entity, ref channel, world, 0.016f);

            Assert.Equal(NodeStatus.Running, channel.Status);
            Assert.Equal(statusBefore, channel.Status);   // unchanged
        }

        // ── Test 4: Execute returns Failure when status is FailedBlocked ──────────────────────────

        /// <summary>
        /// MOD1-P1T2 T5: When <see cref="NavigationStatus.Result"/> is FailedBlocked (or
        /// FailedUnreachable) the executor must set channel status to Failure.
        /// </summary>
        [Fact]
        public void MoveToExecutor_Execute_ReturnsFailureWhenBlocked()
        {
            var (world, entity, channel) = BuildWorld(
                new Vector2(100f, 0f), arrivalRadius: 5f, speed: 10f, existingIntentId: 5);

            var executor = new MoveToExecutor();
            executor.OnEnter(entity, ref channel, world);

            var intent = world.GetComponent<NavigationIntent>(entity);
            world.SetComponent(entity, new NavigationStatus
            {
                IntentId = intent.IntentId,
                Result   = NavigationResult.FailedBlocked,
            });

            executor.Execute(entity, ref channel, world, 0.016f);

            Assert.Equal(NodeStatus.Failure, channel.Status);
        }

        // ── Test 5: OnExit clears NavigationIntent ────────────────────────────────────────────────

        /// <summary>
        /// After <see cref="MoveToExecutor.OnExit"/> the entity's
        /// <see cref="NavigationIntent.Mode"/> must be cleared to stop the Muscle layer.
        /// <see cref="NavigationIntent.TargetSpeed"/> must be zeroed.
        /// </summary>
        [Fact]
        public void MoveToExecutor_OnExit_ClearsNavigationIntent()
        {
            var (world, entity, channel) = BuildWorld(
                new Vector2(50f, 0f), arrivalRadius: 1f, speed: 10f);

            var executor = new MoveToExecutor();
            executor.OnEnter(entity, ref channel, world);

            // Verify OnEnter set Mode and speed.
            var intentAfterEnter = world.GetComponent<NavigationIntent>(entity);
            Assert.Equal(NavigationMode.DirectPoint, intentAfterEnter.Mode);
            Assert.Equal(10f, intentAfterEnter.TargetSpeed);

            executor.OnExit(entity, ref channel, world);

            var intentAfterExit = world.GetComponent<NavigationIntent>(entity);
            Assert.Equal(NavigationMode.None, intentAfterExit.Mode);
            Assert.Equal(0f, intentAfterExit.TargetSpeed);
        }

        // ── Test 6: Execute returns Failure when FailedUnreachable ───────────────────────────────

        [Fact]
        public void MoveToExecutor_Execute_ReturnsFailureWhenUnreachable()
        {
            var (world, entity, channel) = BuildWorld(
                new Vector2(100f, 0f), arrivalRadius: 5f, speed: 10f, existingIntentId: 0);

            var executor = new MoveToExecutor();
            executor.OnEnter(entity, ref channel, world);

            var intent = world.GetComponent<NavigationIntent>(entity);
            world.SetComponent(entity, new NavigationStatus
            {
                IntentId = intent.IntentId,
                Result   = NavigationResult.FailedUnreachable,
            });

            executor.Execute(entity, ref channel, world, 0.016f);

            Assert.Equal(NodeStatus.Failure, channel.Status);
        }
    }
}

