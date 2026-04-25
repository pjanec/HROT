using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Executors;
using Fdp.Toolkit.Behavior.Systems;
using Fbt;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests
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
            sys.Execute(world, 0.016f);
            Assert.Equal(1, spy.OnEnterCallCount);
            Assert.Equal(1, spy.ExecuteCallCount);

            var ch1 = world.GetComponent<LocomotionChannel>(e);
            Assert.Equal(NodeStatus.Running, ch1.Status);           // executor wrote it
            Assert.Equal(ch1.ActionInstanceId, ch1.DispatchedInstanceId); // prevents repeat OnEnter

            // Second tick: no second OnEnter, Execute again.
            sys.Execute(world, 0.016f);
            Assert.Equal(1, spy.OnEnterCallCount);
            Assert.Equal(2, spy.ExecuteCallCount);

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
            sys.Execute(world, 0.016f);
            Assert.Equal(1, spy1.OnEnterCallCount);
            Assert.Equal(0, spy1.OnExitCallCount);

            // Brain changes to action 2.
            ref var ch = ref world.GetComponentRW<LocomotionChannel>(e);
            ch.ActiveAction = 2;
            ch.ActionInstanceId = 2;

            // Tick 2: exits action 1, enters action 2.
            sys.Execute(world, 0.016f);
            Assert.Equal(1, spy1.OnExitCallCount);
            Assert.Equal(1, spy2.OnEnterCallCount);

            world.Dispose();
        }

        [Fact]
        public void Dispatcher_FailsChannel_WhenCannotMove()
        {
            var world = TestWorldFactory.Create();
            var sys = new LocomotionDispatcherSystem();
            var spy = new SpyExecutor<LocomotionChannel>();
            sys.RegisterExecutor(1, spy);

            var e = world.CreateEntity();
            world.AddComponent(e, new LocomotionChannel
            {
                ActiveAction = 1,
                ActionInstanceId = 1,
                DispatchedInstanceId = 0,
                Status = NodeStatus.Running
            });
            world.AddComponent(e, new ActorCapabilityState { Capabilities = ActorCapabilities.None });

            sys.Execute(world, 0.016f);

            var channel = world.GetComponent<LocomotionChannel>(e);
            Assert.Equal(NodeStatus.Failure, channel.Status);
            Assert.Equal(0, spy.ExecuteCallCount);
            Assert.Equal(0, spy.OnEnterCallCount);

            world.Dispose();
        }

        [Fact]
        public void Dispatcher_SkipsNullExecutor_Gracefully()
        {
            var world = TestWorldFactory.Create();
            var sys = new LocomotionDispatcherSystem();
            // No executor registered for action 1.

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
            sys.Execute(world, 0.016f);

            // Lifecycle bookkeeping still ran even without a registered executor.
            var channel = world.GetComponent<LocomotionChannel>(e);
            Assert.Equal(channel.ActionInstanceId, channel.DispatchedInstanceId); // updated even without executor

            world.Dispose();
        }

        // ── DEBT-024 test ─────────────────────────────────────────────────────
        /// <summary>
        /// When an entity is destroyed inside an executor's Execute() callback
        /// (e.g. lethal self-damage), the dispatcher must call OnExit to clean
        /// up executor state, and must not throw.
        /// </summary>
        [Fact]
        public void Dispatcher_CallsOnExit_WhenEntityDestroyedMidAction()
        {
            var world = TestWorldFactory.Create();
            var sys = new LocomotionDispatcherSystem();

            // Spy executor that destroys the entity inside Execute() and records
            // whether OnExit was subsequently called by the dispatcher guard.
            var spy = new SelfDestroyingExecutor();
            sys.RegisterExecutor(1, spy);

            var e = world.CreateEntity();
            // Set ActionInstanceId == DispatchedInstanceId so the lifecycle block
            // doesn't fire OnEnter on this tick — we only want Execute() to run.
            world.AddComponent(e, new LocomotionChannel
            {
                ActiveAction         = 1,
                ActionInstanceId     = 1,
                DispatchedInstanceId = 1,
                Status               = NodeStatus.Running,
            });
            world.AddComponent(e, new ActorCapabilityState
            {
                Capabilities = ActorCapabilities.CanMove,
            });

            // Act: Execute() destroys the entity; the post-Execute guard should
            // call OnExit and not throw.
            var exception = Record.Exception(() => sys.Execute(world, 0.016f));
            Assert.Null(exception);
            Assert.Equal(1, spy.ExecuteCallCount);
            Assert.Equal(1, spy.OnExitCallCount); // guard called OnExit

            world.Dispose();
        }
    }

    /// <summary>
    /// Test executor whose Execute() call destroys the entity, simulating
    /// an in-action lethal event (DEBT-024).
    /// </summary>
    internal sealed class SelfDestroyingExecutor : IActionExecutor<LocomotionChannel>
    {
        public int ExecuteCallCount { get; private set; }
        public int OnExitCallCount  { get; private set; }

        public void OnEnter(Entity entity, ref LocomotionChannel channel, EntityRepository world) { }

        public void Execute(Entity entity, ref LocomotionChannel channel, EntityRepository world, float dt)
        {
            ExecuteCallCount++;
            world.DestroyEntity(entity);
        }

        public void OnExit(Entity entity, ref LocomotionChannel channel, EntityRepository world)
            => OnExitCallCount++;
    }
}
