using System.Runtime.CompilerServices;
using Hrot.SimHost.Brains;
using Fdp.Kernel;
using Fbt;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Navigation;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Unit tests for the <see cref="SimHostNodes.Action_WriteMoveToChannel"/> node delegate,
    /// specifically verifying that it correctly forwards the executor's terminal status
    /// (<see cref="NodeStatus.Success"/> / <see cref="NodeStatus.Failure"/>) back to the
    /// behavior tree so that <c>DoctrineFinishedEvent</c> is published.
    /// </summary>
    public class SimHostNodesActionTests
    {
        // ── World factory ─────────────────────────────────────────────────────

        private static EntityRepository CreateWorld()
        {
            var world = new EntityRepository();
            world.RegisterComponent<LocomotionChannel>();
            world.RegisterComponent<BrainBlackboard>();
            return world;
        }

        /// <summary>
        /// Writes MoveToLocationParams into blackboard memory at offset 0, matching
        /// what DoctrineDefinition.ParseParams does at runtime.
        /// Uses the ECS world to get a pinnable reference, same pattern as
        /// JoinFormationExecutorTests.
        /// </summary>
        private static unsafe BrainBlackboard BuildBlackboard(EntityRepository world, Entity entity,
            float x = 10f, float y = 20f, float speed = 5f, float arrivalRadius = 1f)
        {
            world.AddComponent(entity, new BrainBlackboard());
            ref var bb = ref world.GetComponentRW<BrainBlackboard>(entity);
            var p = new SimHostNodes.MoveToLocationParams
            {
                X             = x,
                Y             = y,
                Speed         = speed,
                ArrivalRadius = arrivalRadius
            };
            fixed (byte* dst = bb.Memory)
                Unsafe.Write(dst, p);
            return bb;
        }

        // ── Initial activation ────────────────────────────────────────────────

        /// <summary>
        /// On first tick (channel is idle, no ActiveAction set), the node must write
        /// the destination and return <see cref="NodeStatus.Running"/>.
        /// </summary>
        [Fact]
        public unsafe void Action_WriteMoveToChannel_FirstTick_ReturnsRunning()
        {
            using var world = CreateWorld();
            var entity = world.CreateEntity();
            world.AddComponent(entity, new LocomotionChannel { Status = default });

            var bb    = BuildBlackboard(world, entity);
            var state = new BehaviorTreeState();
            var ctx   = new BTreeContext { Self = entity, World = world };

            var result = SimHostNodes.Action_WriteMoveToChannel(ref bb, ref state, ref ctx, 0);

            Assert.Equal(NodeStatus.Running, result);
        }

        /// <summary>
        /// On first tick, the node must write the MoveTo action ID to the channel.
        /// </summary>
        [Fact]
        public unsafe void Action_WriteMoveToChannel_FirstTick_SetsActiveActionOnChannel()
        {
            using var world = CreateWorld();
            var entity = world.CreateEntity();
            world.AddComponent(entity, new LocomotionChannel { Status = default });

            var bb    = BuildBlackboard(world, entity);
            var state = new BehaviorTreeState();
            var ctx   = new BTreeContext { Self = entity, World = world };

            SimHostNodes.Action_WriteMoveToChannel(ref bb, ref state, ref ctx, 0);

            var channel = world.GetComponent<LocomotionChannel>(entity);
            Assert.Equal(NavigationConstants.ActionIdMoveTo, channel.ActiveAction);
        }

        // ── Success forwarding (the key fix) ──────────────────────────────────

        /// <summary>
        /// When the executor has set <c>channel.Status = NodeStatus.Success</c>
        /// (vehicle arrived at destination), the node must return
        /// <see cref="NodeStatus.Success"/> so the BTree finishes and
        /// <c>DoctrineFinishedEvent</c> is published.
        /// </summary>
        [Fact]
        public unsafe void Action_WriteMoveToChannel_WhenChannelSuccess_ReturnsSuccess()
        {
            using var world = CreateWorld();
            var entity = world.CreateEntity();

            // Pre-set the channel as if it was already activated (action running)
            // and the executor has now signalled arrival.
            world.AddComponent(entity, new LocomotionChannel
            {
                ActiveAction = NavigationConstants.ActionIdMoveTo,
                Status       = NodeStatus.Success
            });

            var bb    = BuildBlackboard(world, entity);
            var state = new BehaviorTreeState();
            var ctx   = new BTreeContext { Self = entity, World = world };

            var result = SimHostNodes.Action_WriteMoveToChannel(ref bb, ref state, ref ctx, 0);

            Assert.Equal(NodeStatus.Success, result);
        }

        // ── Failure forwarding ────────────────────────────────────────────────

        /// <summary>
        /// When a running MoveTo action reaches a failure state in the locomotion channel,
        /// the node re-activates the command (increments ActionInstanceId) and returns
        /// <see cref="NodeStatus.Running"/> — restoring the action rather than propagating
        /// failure upwards immediately, consistent with how the original code handles
        /// <c>channel.Status == NodeStatus.Failure</c> inside <c>needsActivation</c>.
        ///
        /// NOTE: The Failure path triggers re-activation because the <c>needsActivation</c>
        /// guard also checks for Failure, so the action is reissued and Running is returned.
        /// </summary>
        [Fact]
        public unsafe void Action_WriteMoveToChannel_WhenChannelFailure_ReactivatesAndReturnsRunning()
        {
            using var world = CreateWorld();
            var entity = world.CreateEntity();

            world.AddComponent(entity, new LocomotionChannel
            {
                ActiveAction = NavigationConstants.ActionIdMoveTo,
                Status       = NodeStatus.Failure
            });

            var bb    = BuildBlackboard(world, entity);
            var state = new BehaviorTreeState();
            var ctx   = new BTreeContext { Self = entity, World = world };

            var result = SimHostNodes.Action_WriteMoveToChannel(ref bb, ref state, ref ctx, 0);

            // Failure triggers needsActivation=true → resets and returns Running.
            Assert.Equal(NodeStatus.Running, result);
        }

        // ── Missing component guard ───────────────────────────────────────────

        /// <summary>
        /// If the entity has no <see cref="LocomotionChannel"/>, the node must return
        /// <see cref="NodeStatus.Failure"/> immediately (guard clause).
        /// </summary>
        [Fact]
        public unsafe void Action_WriteMoveToChannel_NoLocomotionChannel_ReturnsFailure()
        {
            using var world = CreateWorld();
            var entity = world.CreateEntity();
            // Do NOT add LocomotionChannel
            // Add a blackboard so the entity exists in the world
            world.AddComponent(entity, new BrainBlackboard());
            var bb    = world.GetComponent<BrainBlackboard>(entity);
            var state = new BehaviorTreeState();
            var ctx   = new BTreeContext { Self = entity, World = world };

            var result = SimHostNodes.Action_WriteMoveToChannel(ref bb, ref state, ref ctx, 0);

            Assert.Equal(NodeStatus.Failure, result);
        }
    }
}
