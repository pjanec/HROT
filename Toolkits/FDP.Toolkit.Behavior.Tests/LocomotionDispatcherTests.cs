using Fdp.Kernel;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Behavior.Systems;
using Fbt;
using Xunit;

namespace FDP.Toolkit.Behavior.Tests
{
    public class LocomotionDispatcherTests
    {
        [Fact]
        public void Dispatcher_CallsOnEnter_OnFirstTick()
        {
            var world = TestWorldFactory.Create();
            var sys = new LocomotionDispatcherSystem();
            var spy = new WritingSpyExecutor<LocomotionChannel>();
            sys.RegisterExecutor(1, spy);
            sys.Create(world);

            var e = world.CreateEntity();
            world.AddComponent(e, new LocomotionChannel
            {
                ActiveAction = 1,
                ActionInstanceId = 1,
                DispatchedInstanceId = 0,
                Status = NodeStatus.Running
            });
            world.AddComponent(e, new ActorCapabilityState { Capabilities = ActorCapabilities.CanMove });

            // First tick: lifecycle fires (OnEnter) then Execute.
            sys.Run();
            Assert.Equal(1, spy.OnEnterCallCount);
            Assert.Equal(1, spy.ExecuteCallCount);

            var ch1 = world.GetComponent<LocomotionChannel>(e);
            Assert.Equal(NodeStatus.Running, ch1.Status);           // executor wrote it
            Assert.Equal(ch1.ActionInstanceId, ch1.DispatchedInstanceId); // prevents repeat OnEnter

            // Second tick: no second OnEnter, Execute again.
            sys.Run();
            Assert.Equal(1, spy.OnEnterCallCount);
            Assert.Equal(2, spy.ExecuteCallCount);

            sys.Dispose();
            world.Dispose();
        }

        [Fact]
        public void Dispatcher_CallsOnExit_WhenActionChanges()
        {
            var world = TestWorldFactory.Create();
            var sys = new LocomotionDispatcherSystem();
            var spy1 = new SpyExecutor<LocomotionChannel>();
            var spy2 = new SpyExecutor<LocomotionChannel>();
            sys.RegisterExecutor(1, spy1);
            sys.RegisterExecutor(2, spy2);
            sys.Create(world);

            var e = world.CreateEntity();
            world.AddComponent(e, new LocomotionChannel
            {
                ActiveAction = 1,
                ActionInstanceId = 1,
                DispatchedInstanceId = 0,
                Status = NodeStatus.Running
            });
            world.AddComponent(e, new ActorCapabilityState { Capabilities = ActorCapabilities.CanMove });

            // Tick 1: enters action 1.
            sys.Run();
            Assert.Equal(1, spy1.OnEnterCallCount);
            Assert.Equal(0, spy1.OnExitCallCount);

            // Brain changes to action 2.
            ref var ch = ref world.GetComponentRW<LocomotionChannel>(e);
            ch.ActiveAction = 2;
            ch.ActionInstanceId = 2;

            // Tick 2: exits action 1, enters action 2.
            sys.Run();
            Assert.Equal(1, spy1.OnExitCallCount);
            Assert.Equal(1, spy2.OnEnterCallCount);

            sys.Dispose();
            world.Dispose();
        }

        [Fact]
        public void Dispatcher_FailsChannel_WhenCannotMove()
        {
            var world = TestWorldFactory.Create();
            var sys = new LocomotionDispatcherSystem();
            var spy = new SpyExecutor<LocomotionChannel>();
            sys.RegisterExecutor(1, spy);
            sys.Create(world);

            var e = world.CreateEntity();
            world.AddComponent(e, new LocomotionChannel
            {
                ActiveAction = 1,
                ActionInstanceId = 1,
                DispatchedInstanceId = 0,
                Status = NodeStatus.Running
            });
            world.AddComponent(e, new ActorCapabilityState { Capabilities = ActorCapabilities.None });

            sys.Run();

            var channel = world.GetComponent<LocomotionChannel>(e);
            Assert.Equal(NodeStatus.Failure, channel.Status);
            Assert.Equal(0, spy.ExecuteCallCount);
            Assert.Equal(0, spy.OnEnterCallCount);

            sys.Dispose();
            world.Dispose();
        }

        [Fact]
        public void Dispatcher_SkipsNullExecutor_Gracefully()
        {
            var world = TestWorldFactory.Create();
            var sys = new LocomotionDispatcherSystem();
            // No executor registered for action 1.
            sys.Create(world);

            var e = world.CreateEntity();
            world.AddComponent(e, new LocomotionChannel
            {
                ActiveAction = 1,
                ActionInstanceId = 1,
                DispatchedInstanceId = 0,
                Status = NodeStatus.Running
            });
            world.AddComponent(e, new ActorCapabilityState { Capabilities = ActorCapabilities.CanMove });

            // Must not throw.
            sys.Run();

            // Lifecycle bookkeeping still ran even without a registered executor.
            var channel = world.GetComponent<LocomotionChannel>(e);
            Assert.Equal(channel.ActionInstanceId, channel.DispatchedInstanceId); // updated even without executor

            sys.Dispose();
            world.Dispose();
        }
    }
}
