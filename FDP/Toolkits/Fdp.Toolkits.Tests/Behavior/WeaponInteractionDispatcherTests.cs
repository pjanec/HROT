using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Systems;
using Fbt;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests
{
    public class WeaponInteractionDispatcherTests
    {
        [Fact]
        public void WeaponDispatcher_FailsChannel_WhenCannotShoot()
        {
            var world = TestWorldFactory.Create();
            var sys = new WeaponDispatcherSystem();
            var spy = new SpyExecutor<WeaponChannel>();
            sys.RegisterExecutor(1, spy);

            var e = world.CreateEntity();
            world.AddComponent(e, new WeaponChannel
            {
                ActiveAction = 1,
                ActionInstanceId = 1,
                DispatchedInstanceId = 0,
                Status = NodeStatus.Running
            });
            // CanShoot not set.
            world.AddComponent(e, new ActorCapabilityState { Capabilities = ActorCapabilities.None });

            sys.Execute(world, 0.016f);

            var channel = world.GetComponent<WeaponChannel>(e);
            Assert.Equal(NodeStatus.Failure, channel.Status);
            Assert.Equal(0, spy.ExecuteCallCount);

            world.Dispose();
        }

        [Fact]
        public void InteractionDispatcher_RunsExecutor_WhenCanInteract()
        {
            var world = TestWorldFactory.Create();
            var sys = new InteractionDispatcherSystem();
            var spy = new SpyExecutor<InteractionChannel>();
            sys.RegisterExecutor(1, spy);

            var e = world.CreateEntity();
            world.AddComponent(e, new InteractionChannel
            {
                ActiveAction = 1,
                ActionInstanceId = 1,
                DispatchedInstanceId = 0,
                Status = NodeStatus.Running
            });
            world.AddComponent(e, new ActorCapabilityState { Capabilities = ActorCapabilities.CanInteract });

            sys.Execute(world, 0.016f);

            Assert.Equal(1, spy.OnEnterCallCount);
            Assert.Equal(1, spy.ExecuteCallCount);

            world.Dispose();
        }
    }
}
