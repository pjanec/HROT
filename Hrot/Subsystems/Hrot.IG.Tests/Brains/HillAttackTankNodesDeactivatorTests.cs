using System.Runtime.CompilerServices;
using Fbt;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Navigation;
using Hrot.AI.Behaviors.Brains;
using Xunit;

namespace Hrot.IG.Tests.Brains
{
    // ============================================================================
    // TASK-EQL-006: Unit tests for HillAttackTankNodes.Deactivate_CreepToAndBeyondSlot
    // TASK-EQL-007: Unit tests for HillAttackTankNodes.Deactivate_AimAndFireSpecific
    // ============================================================================

    // ── EQL-006: LocomotionChannel deactivator ────────────────────────────────────

    public sealed class HillAttackTankNodes_CreepDeactivatorTests
    {
        // ── T1 ────────────────────────────────────────────────────────────────────

        [Fact]
        public void T1_WithLocomotionChannel_ActiveActionMoveTo_ClearsActionAndIncrementsInstanceId()
        {
            // Arrange
            using var world = new EntityRepository();
            world.RegisterComponent<LocomotionChannel>();
            var entity = world.CreateEntity();
            world.AddComponent(entity, new LocomotionChannel
            {
                ActiveAction     = NavigationConstants.ActionIdMoveTo,
                ActionInstanceId = 0,
            });

            var ctx   = new BTreeContext { Self = entity, World = world };
            var state = new BehaviorTreeState();
            var bb    = new BrainBlackboard();

            // Act
            HillAttackTankNodes.Deactivate_CreepToAndBeyondSlot(ref bb, ref state, ref ctx, 0);

            // Assert
            var loco = world.GetComponent<LocomotionChannel>(entity);
            Assert.Equal((ushort)0, loco.ActiveAction);
            Assert.Equal((uint)1, loco.ActionInstanceId);
        }

        // ── T2 ────────────────────────────────────────────────────────────────────

        [Fact]
        public void T2_WithoutLocomotionChannel_NoException()
        {
            // Arrange
            using var world = new EntityRepository();
            world.RegisterComponent<LocomotionChannel>();
            var entity = world.CreateEntity();
            // LocomotionChannel is NOT added.

            var ctx   = new BTreeContext { Self = entity, World = world };
            var state = new BehaviorTreeState();
            var bb    = new BrainBlackboard();

            // Act + Assert (no exception)
            HillAttackTankNodes.Deactivate_CreepToAndBeyondSlot(ref bb, ref state, ref ctx, 0);
        }

        // ── T3 ────────────────────────────────────────────────────────────────────

        [Fact]
        public void T3_WithLocomotionChannel_DifferentActiveAction_ChannelUnchanged()
        {
            // Arrange
            const ushort otherAction = 77;
            using var world = new EntityRepository();
            world.RegisterComponent<LocomotionChannel>();
            var entity = world.CreateEntity();
            world.AddComponent(entity, new LocomotionChannel
            {
                ActiveAction     = otherAction,
                ActionInstanceId = 3,
            });

            var ctx   = new BTreeContext { Self = entity, World = world };
            var state = new BehaviorTreeState();
            var bb    = new BrainBlackboard();

            // Act
            HillAttackTankNodes.Deactivate_CreepToAndBeyondSlot(ref bb, ref state, ref ctx, 0);

            // Assert — channel must be entirely unchanged
            var loco = world.GetComponent<LocomotionChannel>(entity);
            Assert.Equal(otherAction, loco.ActiveAction);
            Assert.Equal((uint)3, loco.ActionInstanceId);
        }
    }

    // ── EQL-007: WeaponChannel deactivator ───────────────────────────────────────

    public sealed class HillAttackTankNodes_AimAndFireSpecificDeactivatorTests
    {
        // ── T1 ────────────────────────────────────────────────────────────────────

        [Fact]
        public void T1_WithWeaponChannel_ActiveActionMatches_ClearsActionAndIncrementsInstanceId()
        {
            // Arrange
            using var world = new EntityRepository();
            world.RegisterComponent<WeaponChannel>();
            var entity = world.CreateEntity();
            world.AddComponent(entity, new WeaponChannel
            {
                ActiveAction     = CombatConstants.ActionIdAimAndFire,
                ActionInstanceId = 0,
            });

            var ctx   = new BTreeContext { Self = entity, World = world };
            var state = new BehaviorTreeState();
            var bb    = new BrainBlackboard();

            // Act
            HillAttackTankNodes.Deactivate_AimAndFireSpecific(ref bb, ref state, ref ctx, 0);

            // Assert
            var ch = world.GetComponent<WeaponChannel>(entity);
            Assert.Equal((ushort)0, ch.ActiveAction);
            Assert.Equal((uint)1, ch.ActionInstanceId);
        }

        // ── T2 ────────────────────────────────────────────────────────────────────

        [Fact]
        public void T2_WithoutWeaponChannel_NoException()
        {
            // Arrange
            using var world = new EntityRepository();
            world.RegisterComponent<WeaponChannel>();
            var entity = world.CreateEntity();
            // WeaponChannel is NOT added.

            var ctx   = new BTreeContext { Self = entity, World = world };
            var state = new BehaviorTreeState();
            var bb    = new BrainBlackboard();

            // Act + Assert (no exception)
            HillAttackTankNodes.Deactivate_AimAndFireSpecific(ref bb, ref state, ref ctx, 0);
        }

        // ── T3 ────────────────────────────────────────────────────────────────────

        [Fact]
        public void T3_WithWeaponChannel_ActiveActionZero_DoesNotIncrementInstanceId()
        {
            // Arrange
            using var world = new EntityRepository();
            world.RegisterComponent<WeaponChannel>();
            var entity = world.CreateEntity();
            world.AddComponent(entity, new WeaponChannel
            {
                ActiveAction     = 0,
                ActionInstanceId = 9,
            });

            var ctx   = new BTreeContext { Self = entity, World = world };
            var state = new BehaviorTreeState();
            var bb    = new BrainBlackboard();

            // Act
            HillAttackTankNodes.Deactivate_AimAndFireSpecific(ref bb, ref state, ref ctx, 0);

            // Assert — ActionInstanceId must remain unchanged
            var ch = world.GetComponent<WeaponChannel>(entity);
            Assert.Equal((uint)9, ch.ActionInstanceId);
        }

        // ── T4 ────────────────────────────────────────────────────────────────────

        [Fact]
        public void T4_WithWeaponChannel_DifferentActiveAction_ChannelUnchanged()
        {
            // Arrange
            const ushort otherAction = 55;
            using var world = new EntityRepository();
            world.RegisterComponent<WeaponChannel>();
            var entity = world.CreateEntity();
            world.AddComponent(entity, new WeaponChannel
            {
                ActiveAction     = otherAction,
                ActionInstanceId = 11,
            });

            var ctx   = new BTreeContext { Self = entity, World = world };
            var state = new BehaviorTreeState();
            var bb    = new BrainBlackboard();

            // Act
            HillAttackTankNodes.Deactivate_AimAndFireSpecific(ref bb, ref state, ref ctx, 0);

            // Assert — channel must be entirely unchanged
            var ch = world.GetComponent<WeaponChannel>(entity);
            Assert.Equal(otherAction, ch.ActiveAction);
            Assert.Equal((uint)11, ch.ActionInstanceId);
        }
    }
}
