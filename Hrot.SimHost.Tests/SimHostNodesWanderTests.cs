using System.Numerics;
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
    /// Unit tests for <see cref="SimHostNodes.Action_Wander"/> verifying that: 
    /// <list type="bullet">
    ///   <item>Arrival is detected solely via <see cref="LocomotionChannel.Status"/>
    ///         (<see cref="NodeStatus.Success"/>); no <c>NavState</c> component required
    ///         (BS1-T021).</item>
    ///   <item>The action stays Running and does not pick a new target while the channel
    ///         reports <see cref="NodeStatus.Running"/>.</item>
    /// </list>
    /// </summary>
    public class SimHostNodesWanderTests
    {
        // ── World factory ─────────────────────────────────────────────────────

        private static EntityRepository CreateWorld()
        {
            var world = new EntityRepository();
            world.RegisterComponent<LocomotionChannel>();
            world.RegisterComponent<BrainBlackboard>();
            world.RegisterComponent<DoctrineState>();
            return world;
        }

        // ── BS1-T021 SC2: Wander picks new target when channel reports Success ─

        /// <summary>
        /// When the locomotion channel status is <see cref="NodeStatus.Success"/> (the MoveTo
        /// executor reported arrival), <c>Action_Wander</c> must reset the channel and write a
        /// fresh <c>MoveToParams</c>.
        /// <para>
        /// No <c>NavState</c> component is added to the entity — verifying that the secondary
        /// <c>NavState.HasArrived</c> block has been removed (BS1-T021).
        /// </para>
        /// </summary>
        [Fact]
        public unsafe void Action_Wander_WhenChannelSuccess_PicksNewTarget()
        {
            using var world = CreateWorld();
            var entity = world.CreateEntity();

            // Pre-set channel as if the MoveTo executor has already reported arrival.
            world.AddComponent(entity, new LocomotionChannel
            {
                ActiveAction     = NavigationConstants.ActionIdMoveTo,
                Status           = NodeStatus.Success,
                ActionInstanceId = 3,  // non-zero so we can detect the increment
            });
            world.AddComponent(entity, new BrainBlackboard());

            // No NavState component on the entity (Brain-only world).

            var bb    = world.GetComponent<BrainBlackboard>(entity);
            var state = new BehaviorTreeState();
            var ctx   = new BTreeContext { Self = entity, World = world };

            var result = SimHostNodes.Action_Wander(ref bb, ref state, ref ctx, 0);

            // Action_Wander always returns Running (infinite wander loop).
            Assert.Equal(NodeStatus.Running, result);

            var channel = world.GetComponent<LocomotionChannel>(entity);

            // A new MoveTo must have been written.
            Assert.Equal(NavigationConstants.ActionIdMoveTo, channel.ActiveAction);

            // ActionInstanceId must have been incremented to signal re-activation.
            Assert.Equal(4u, channel.ActionInstanceId);

            // Channel status is reset to Running for the new command.
            Assert.Equal(NodeStatus.Running, channel.Status);

            // MoveToParams must be written — read from the fixed buffer via Unsafe.
            MoveToParams written = Unsafe.ReadUnaligned<MoveToParams>(ref channel.Params[0]);

            Assert.Equal(20f, written.ArrivalRadius, precision: 3);   // WanderArrivalRadius == 20f
            Assert.Equal(10f, written.Speed,         precision: 3);   // WanderSpeed         == 10f
        }

        // ── BS1-T021 SC2 (negative): No new target while channel is Running ───

        /// <summary>
        /// When the locomotion channel status is <see cref="NodeStatus.Running"/> (the MoveTo
        /// executor is still executing), <c>Action_Wander</c> must NOT pick a new target.
        /// The action must return <see cref="NodeStatus.Running"/>.
        /// </summary>
        [Fact]
        public unsafe void Action_Wander_WhenChannelRunning_DoesNotPickNewTarget()
        {
            using var world = CreateWorld();
            var entity = world.CreateEntity();

            const uint OriginalInstanceId = 7;

            world.AddComponent(entity, new LocomotionChannel
            {
                ActiveAction      = NavigationConstants.ActionIdMoveTo,
                Status            = NodeStatus.Running,
                ActionInstanceId  = OriginalInstanceId,
            });
            world.AddComponent(entity, new BrainBlackboard());

            var bb    = world.GetComponent<BrainBlackboard>(entity);
            var state = new BehaviorTreeState();
            var ctx   = new BTreeContext { Self = entity, World = world };

            var result = SimHostNodes.Action_Wander(ref bb, ref state, ref ctx, 0);

            Assert.Equal(NodeStatus.Running, result);

            var channel = world.GetComponent<LocomotionChannel>(entity);

            // ActionInstanceId must not have been incremented (no re-activation).
            Assert.Equal(OriginalInstanceId, channel.ActionInstanceId);
        }

        // ── BS1-T021 SC2 (guard): Missing LocomotionChannel returns Failure ───

        /// <summary>
        /// If the entity has no <see cref="LocomotionChannel"/>, <c>Action_Wander</c> must
        /// return <see cref="NodeStatus.Failure"/> immediately (guard clause).
        /// </summary>
        [Fact]
        public void Action_Wander_NoLocomotionChannel_ReturnsFailure()
        {
            using var world = CreateWorld();
            var entity = world.CreateEntity();
            world.AddComponent(entity, new BrainBlackboard());

            var bb    = world.GetComponent<BrainBlackboard>(entity);
            var state = new BehaviorTreeState();
            var ctx   = new BTreeContext { Self = entity, World = world };

            var result = SimHostNodes.Action_Wander(ref bb, ref state, ref ctx, 0);

            Assert.Equal(NodeStatus.Failure, result);
        }
    }
}
