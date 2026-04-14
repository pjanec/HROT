using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Systems;
using Fbt;
using Xunit;
using Xunit.Abstractions;

namespace Fdp.Toolkit.Behavior.Tests
{
    public class ChannelArbitrationTests
    {
        [Fact]
        public void Arbitration_ClearsStaleChannel()
        {
            var world = TestWorldFactory.Create();
            
            var sys = new ChannelArbitrationSystem();
            sys.Create(world);
            
            var e = world.CreateEntity();
            // Doctrine at version 2 (preempted version 1)
            world.AddComponent(e, new DoctrineState { InstanceId = 2 });
            // Channel still has action from version 1
            world.AddComponent(e, new LocomotionChannel { 
                ActiveAction = 1, 
                DoctrineInstanceId = 1,
                Status = NodeStatus.Running
            });
            
            sys.Run();
            
            var channel = world.GetComponent<LocomotionChannel>(e);
            Assert.Equal(0, channel.ActiveAction);
            // Selective-clear: only ActiveAction is zeroed and ActionInstanceId is bumped.
            // Status and DoctrineInstanceId are NOT reset (differs from `channel = default`).
            Assert.Equal(NodeStatus.Running, channel.Status);  // unchanged
            Assert.Equal(1u, channel.DoctrineInstanceId);      // unchanged (selective-clear, not full reset)
            
            sys.Dispose();
            world.Dispose();
        }

        [Fact]
        public void Arbitration_IgnoresValidChannel()
        {
            var world = TestWorldFactory.Create();
            
            var sys = new ChannelArbitrationSystem();
            sys.Create(world);
            
            var e = world.CreateEntity();
            world.AddComponent(e, new DoctrineState { InstanceId = 2 });
            world.AddComponent(e, new LocomotionChannel { 
                ActiveAction = 1, 
                DoctrineInstanceId = 2, // Matches
                Status = NodeStatus.Running
            });
            
            sys.Run();
            
            var channel = world.GetComponent<LocomotionChannel>(e);
            Assert.Equal(1, channel.ActiveAction);
            Assert.Equal(NodeStatus.Running, channel.Status);
            
            sys.Dispose();
            world.Dispose();
        }

        [Fact]
        public void Arbitration_IgnoresEmptyChannel()
        {
            var world = TestWorldFactory.Create();
            
            var sys = new ChannelArbitrationSystem();
            sys.Create(world);
            
            var e = world.CreateEntity();
            world.AddComponent(e, new DoctrineState { InstanceId = 2 });
            world.AddComponent(e, new LocomotionChannel { 
                ActiveAction = 0, // None
                DoctrineInstanceId = 1, // Stale ID
                Status = NodeStatus.Success // Old status
            });
            
            sys.Run();
            
            var channel = world.GetComponent<LocomotionChannel>(e);
            Assert.Equal(0, channel.ActiveAction);
            Assert.Equal(NodeStatus.Success, channel.Status);
            
            sys.Dispose();
            world.Dispose();
        }

        /// <summary>
        /// Ordering integration test: ChannelArbitrationSystem must run before
        /// LocomotionDispatcherSystem. When it does, a stale channel is cleared
        /// before the dispatcher sees it, so no ghost OnEnter fires.
        /// This verifies the [UpdateBefore]/[UpdateAfter] ordering contract.
        /// </summary>
        [Fact]
        public void Arbitration_Ordering_NoGhostOnEnter_WhenChannelIsStale()
        {
            var world = TestWorldFactory.Create();

            var arbitration = new ChannelArbitrationSystem();
            var dispatcher  = new LocomotionDispatcherSystem();
            var spy         = new WritingSpyExecutor<LocomotionChannel>();
            dispatcher.RegisterExecutor(1, spy);

            arbitration.Create(world);
            dispatcher.Create(world);

            var e = world.CreateEntity();
            // Doctrine at version 2; channel still thinks version 1 is current.
            world.AddComponent(e, new DoctrineState { InstanceId = 2 });
            world.AddComponent(e, new LocomotionChannel
            {
                ActiveAction       = 1,
                ActionInstanceId   = 1,
                DoctrineInstanceId = 1,   // stale — mismatches DoctrineState.InstanceId
                DispatchedInstanceId = 0,
                Status             = NodeStatus.Running
            });
            world.AddComponent(e, new ActorCapabilityState { Capabilities = ActorCapabilities.CanMove });

            // Correct order: arbitration clears stale channel BEFORE dispatcher runs.
            arbitration.Run();
            dispatcher.Run();

            // Arbitration cleared ActiveAction → dispatcher found nothing to dispatch.
            Assert.Equal(0, spy.OnEnterCallCount); // no ghost OnEnter
            var channel = world.GetComponent<LocomotionChannel>(e);
            Assert.Equal(0, channel.ActiveAction); // confirmed cleared

            arbitration.Dispose();
            dispatcher.Dispose();
            world.Dispose();
        }

        // ── Task-3 Tests: OnExit Guarantee (───────────────────────────────────────

        [Fact]
        public void ChannelClear_ShouldNotZeroActionInstanceId()
        {
            // ActionInstanceId must be INCREMENTED (not zeroed) so that
            // LocomotionDispatcherSystem evaluates (ActionInstanceId != DispatchedInstanceId)
            // and fires OnExit for the preempted action.
            var world = TestWorldFactory.Create();
            var sys   = new ChannelArbitrationSystem();
            sys.Create(world);

            var e = world.CreateEntity();
            world.AddComponent(e, new DoctrineState { InstanceId = 1 });
            world.AddComponent(e, new LocomotionChannel
            {
                ActiveAction         = 5,
                DoctrineInstanceId   = 0,  // stale: 0 != 1
                ActionInstanceId     = 7,
                DispatchedInstanceId = 7,
            });

            sys.Run();

            var ch = world.GetComponent<LocomotionChannel>(e);
            Assert.Equal(0,  ch.ActiveAction);          // cleared
            Assert.Equal(8u, ch.ActionInstanceId);      // incremented: 7 → 8
            Assert.Equal(7u, ch.DispatchedInstanceId);  // unchanged — dispatcher will fire OnExit

            sys.Dispose();
            world.Dispose();
        }

        [Fact]
        public void NoPreemption_WhenDoctrineMatches()
        {
            var world = TestWorldFactory.Create();
            var sys   = new ChannelArbitrationSystem();
            sys.Create(world);

            var e = world.CreateEntity();
            world.AddComponent(e, new DoctrineState { InstanceId = 3 });
            world.AddComponent(e, new LocomotionChannel
            {
                ActiveAction       = 2,
                DoctrineInstanceId = 3,  // matches — must not be preempted
                ActionInstanceId   = 1,
            });

            sys.Run();

            var ch = world.GetComponent<LocomotionChannel>(e);
            Assert.Equal(2, ch.ActiveAction);   // unchanged
            Assert.Equal(1u, ch.ActionInstanceId); // unchanged

            sys.Dispose();
            world.Dispose();
        }

        [Fact]
        public void WeaponChannel_ReceivesOnExitSignal()
        {
            var world = TestWorldFactory.Create();
            var sys   = new ChannelArbitrationSystem();
            sys.Create(world);

            var e = world.CreateEntity();
            world.AddComponent(e, new DoctrineState { InstanceId = 1 });
            world.AddComponent(e, new WeaponChannel
            {
                ActiveAction         = 5,
                DoctrineInstanceId   = 0,   // stale
                ActionInstanceId     = 7,
                DispatchedInstanceId = 7,
            });

            sys.Run();

            var ch = world.GetComponent<WeaponChannel>(e);
            Assert.Equal(0,  ch.ActiveAction);
            Assert.Equal(8u, ch.ActionInstanceId);      // incremented
            Assert.Equal(7u, ch.DispatchedInstanceId);  // unchanged

            sys.Dispose();
            world.Dispose();
        }

        [Fact]
        public void InteractionChannel_ReceivesOnExitSignal()
        {
            var world = TestWorldFactory.Create();
            var sys   = new ChannelArbitrationSystem();
            sys.Create(world);

            var e = world.CreateEntity();
            world.AddComponent(e, new DoctrineState { InstanceId = 1 });
            world.AddComponent(e, new InteractionChannel
            {
                ActiveAction         = 5,
                DoctrineInstanceId   = 0,   // stale
                ActionInstanceId     = 7,
                DispatchedInstanceId = 7,
            });

            sys.Run();

            var ch = world.GetComponent<InteractionChannel>(e);
            Assert.Equal(0,  ch.ActiveAction);
            Assert.Equal(8u, ch.ActionInstanceId);      // incremented
            Assert.Equal(7u, ch.DispatchedInstanceId);  // unchanged

            sys.Dispose();
            world.Dispose();
        }
    }
}
