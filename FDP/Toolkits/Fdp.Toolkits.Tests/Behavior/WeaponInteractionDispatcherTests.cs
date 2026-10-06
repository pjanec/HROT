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

        /// <summary>⭐ <c>CE-3089</c> (G7) — a weapon MOUNT child reloads every frame although it has no WeaponChannel (measured
        /// live: a TOW's cooldown stayed at 1 forever after its first shot). ✅ Red-proof: drop the mount loop ⇒ stays 1.</summary>
        [Fact]
        public void CE3089_WeaponDispatcher_DrainsAMountChildsCooldown()
        {
            var world = TestWorldFactory.Create();
            if (!world.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.WeaponState>())
                world.RegisterComponent<Fdp.Toolkit.Combat.Components.WeaponState>();
            if (!world.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.WeaponMountInfo>())
                world.RegisterComponent<Fdp.Toolkit.Combat.Components.WeaponMountInfo>();
            var sys = new WeaponDispatcherSystem();
            var tow = world.CreateEntity();
            world.AddComponent(tow, new Fdp.Toolkit.Combat.Components.WeaponState { Ammo = 6, MaxAmmo = 7, CooldownSecondsRemaining = 1f });
            world.AddComponent(tow, new Fdp.Toolkit.Combat.Components.WeaponMountInfo { MountIndex = 1 });

            for (int i = 0; i < 10; i++) sys.Execute(world, 0.1f);

            Assert.True(world.GetComponent<Fdp.Toolkit.Combat.Components.WeaponState>(tow).CooldownSecondsRemaining <= 0f);
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
