using Fbt;
using Fdp.Core;
using Fdp.Examples.UrbanCombat.Brains;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat;
using Xunit;

namespace Fdp.Examples.UrbanCombat.Tests
{
    // ============================================================================
    // TASK-EQL-005: Unit tests for InsurgentNodes.Deactivate_AimAndFire
    // ============================================================================

    public sealed class InsurgentNodesDeactivatorTests
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
                ActiveAction    = CombatConstants.ActionIdAimAndFire,
                ActionInstanceId = 0,
            });

            var ctx   = new BTreeContext { Self = entity, World = world };
            var state = new BehaviorTreeState();
            var bb    = new BrainBlackboard();

            // Act
            InsurgentNodes.Deactivate_AimAndFire(ref bb, ref state, ref ctx, 0);

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
            // WeaponChannel is NOT added to this entity.

            var ctx   = new BTreeContext { Self = entity, World = world };
            var state = new BehaviorTreeState();
            var bb    = new BrainBlackboard();

            // Act + Assert (no exception)
            InsurgentNodes.Deactivate_AimAndFire(ref bb, ref state, ref ctx, 0);
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
                ActionInstanceId = 5,
            });

            var ctx   = new BTreeContext { Self = entity, World = world };
            var state = new BehaviorTreeState();
            var bb    = new BrainBlackboard();

            // Act
            InsurgentNodes.Deactivate_AimAndFire(ref bb, ref state, ref ctx, 0);

            // Assert — ActionInstanceId must remain unchanged
            var ch = world.GetComponent<WeaponChannel>(entity);
            Assert.Equal((uint)5, ch.ActionInstanceId);
        }

        // ── T4 ────────────────────────────────────────────────────────────────────

        [Fact]
        public void T4_WithWeaponChannel_DifferentActiveAction_ChannelUnchanged()
        {
            // Arrange
            const ushort otherAction = 99;
            using var world = new EntityRepository();
            world.RegisterComponent<WeaponChannel>();
            var entity = world.CreateEntity();
            world.AddComponent(entity, new WeaponChannel
            {
                ActiveAction     = otherAction,
                ActionInstanceId = 7,
            });

            var ctx   = new BTreeContext { Self = entity, World = world };
            var state = new BehaviorTreeState();
            var bb    = new BrainBlackboard();

            // Act
            InsurgentNodes.Deactivate_AimAndFire(ref bb, ref state, ref ctx, 0);

            // Assert — channel must be entirely unchanged
            var ch = world.GetComponent<WeaponChannel>(entity);
            Assert.Equal(otherAction, ch.ActiveAction);
            Assert.Equal((uint)7, ch.ActionInstanceId);
        }
    }
}
