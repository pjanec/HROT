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

        /// <summary>⭐ <c>CE-3136</c> P-5 (D9, B8) — the dispatcher runs a reload every frame, on a mount child too, whether or not the
        /// weapon is firing: a unit that hides to reload comes out with a full magazine.</summary>
        [Fact]
        public void P5_WeaponDispatcher_RunsAReload_OnAMountChild_WhileNotFiring()
        {
            var world = TestWorldFactory.Create();
            if (!world.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.WeaponState>())
                world.RegisterComponent<Fdp.Toolkit.Combat.Components.WeaponState>();
            if (!world.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.WeaponMountInfo>())
                world.RegisterComponent<Fdp.Toolkit.Combat.Components.WeaponMountInfo>();
            var sys = new WeaponDispatcherSystem();
            var gun = world.CreateEntity();
            var w = new Fdp.Toolkit.Combat.Components.WeaponState { Ammo = 100, MaxAmmo = 100 };
            Fdp.Toolkit.Combat.Magazine.Load(ref w, 30, 2f);
            w.MagazineRounds = 0;
            Fdp.Toolkit.Combat.Magazine.StartIfEmpty(ref w);
            world.AddComponent(gun, w);
            world.AddComponent(gun, new Fdp.Toolkit.Combat.Components.WeaponMountInfo { MountIndex = 1 });

            for (int i = 0; i < 21; i++) sys.Execute(world, 0.1f);

            var after = world.GetComponent<Fdp.Toolkit.Combat.Components.WeaponState>(gun);
            Assert.False(Fdp.Toolkit.Combat.Magazine.Reloading(after));
            Assert.Equal(30, after.MagazineRounds);
            Assert.Equal(100, after.Ammo);
            world.Dispose();
        }

        /// <summary>⭐ <c>CE-2333</c> (AQ98 D, R-2070) — no weapon action runs ⇒ the unit's weapon status row clears to <c>None</c>; while one
        /// runs, the executor's word stays. 📐 Measured: the row read "Aiming", stamped 10 s earlier, over a man with no fire action.
        /// ✅ Red-proof: drop the clear in the dispatcher ⇒ the row stays <c>Aiming</c> ⇒ red.</summary>
        [Fact]
        public void CE2333_WeaponDispatcher_ClearsAStaleWeaponStatus_WhenNoActionRuns()
        {
            var world = TestWorldFactory.Create();
            if (!world.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Diagnostics.ActionStatus>())
                world.RegisterComponent<Fdp.Toolkit.Behavior.Diagnostics.ActionStatus>();
            var sys = new WeaponDispatcherSystem();
            sys.RegisterExecutor(1, new SpyExecutor<WeaponChannel>());
            var e = world.CreateEntity();
            world.AddComponent(e, new WeaponChannel { ActiveAction = 1, ActionInstanceId = 1, Status = NodeStatus.Running });
            world.AddComponent(e, new ActorCapabilityState { Capabilities = ActorCapabilities.CanShoot });
            world.AddComponent(e, new Fdp.Toolkit.Behavior.Diagnostics.ActionStatus());
            Fdp.Toolkit.Behavior.Diagnostics.ActionStatusOf.Weapon(world, e, 1, Fdp.Toolkit.Behavior.Diagnostics.ActionReason.Aiming, Entity.Null);

            sys.Execute(world, 0.016f);
            Assert.Equal(Fdp.Toolkit.Behavior.Diagnostics.ActionReason.Aiming,
                world.GetComponent<Fdp.Toolkit.Behavior.Diagnostics.ActionStatus>(e).Weapon.Reason);   // an action runs: its word stays

            ref var ch = ref world.GetComponentRW<WeaponChannel>(e);
            ch.ActiveAction = 0;
            ch.Status = NodeStatus.Success;
            sys.Execute(world, 0.016f);
            Assert.Equal(Fdp.Toolkit.Behavior.Diagnostics.ActionReason.None,
                world.GetComponent<Fdp.Toolkit.Behavior.Diagnostics.ActionStatus>(e).Weapon.Reason);
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
