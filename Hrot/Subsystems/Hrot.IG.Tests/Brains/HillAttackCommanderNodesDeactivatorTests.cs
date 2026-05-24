using System.Runtime.CompilerServices;
using Fbt;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Hrot.AI.Behaviors.Brains;
using Xunit;

namespace Hrot.IG.Tests.Brains
{
    // ============================================================================
    // TASK-EQL-008: Unit tests for HillAttackCommanderNodes.Deactivate_RequestAreaQuery
    // ============================================================================

    public sealed class HillAttackCommanderNodesDeactivatorTests
    {
        // ── T1 ────────────────────────────────────────────────────────────────────

        [Fact]
        public unsafe void T1_WithBlackboard1024_CachedIdSet_ResetsToClearedValue()
        {
            // Arrange
            using var world = new EntityRepository();
            world.RegisterComponent<Blackboard1024>();
            var entity = world.CreateEntity();

            var bb1024 = new Blackboard1024();
            ref var s  = ref Unsafe.As<Blackboard1024, HillAttackMutableState>(ref bb1024);
            s.CachedEqsRequestId = 42;
            world.AddComponent(entity, bb1024);

            var ctx   = new BTreeContext { Self = entity, World = world };
            var state = new BehaviorTreeState();
            var bb    = new BrainBlackboard();

            // Act
            HillAttackCommanderNodes.Deactivate_RequestAreaQuery(ref bb, ref state, ref ctx, 0);

            // Assert
            ref var result = ref world.GetComponentRW<Blackboard1024>(entity);
            ref var s2     = ref Unsafe.As<Blackboard1024, HillAttackMutableState>(ref result);
            Assert.Equal(-1L, s2.CachedEqsRequestId);
        }

        // ── T2 ────────────────────────────────────────────────────────────────────

        [Fact]
        public void T2_WithoutBlackboard1024_NoException()
        {
            // Arrange
            using var world = new EntityRepository();
            world.RegisterComponent<Blackboard1024>();
            var entity = world.CreateEntity();
            // Blackboard1024 is NOT added.

            var ctx   = new BTreeContext { Self = entity, World = world };
            var state = new BehaviorTreeState();
            var bb    = new BrainBlackboard();

            // Act + Assert (no exception)
            HillAttackCommanderNodes.Deactivate_RequestAreaQuery(ref bb, ref state, ref ctx, 0);
        }

        // ── T3 ────────────────────────────────────────────────────────────────────

        [Fact]
        public unsafe void T3_WithBlackboard1024_AlreadyCleared_ValueRemainsMinusOne()
        {
            // Arrange
            using var world = new EntityRepository();
            world.RegisterComponent<Blackboard1024>();
            var entity = world.CreateEntity();

            var bb1024 = new Blackboard1024();
            ref var s  = ref Unsafe.As<Blackboard1024, HillAttackMutableState>(ref bb1024);
            s.CachedEqsRequestId = -1;
            world.AddComponent(entity, bb1024);

            var ctx   = new BTreeContext { Self = entity, World = world };
            var state = new BehaviorTreeState();
            var bb    = new BrainBlackboard();

            // Act
            HillAttackCommanderNodes.Deactivate_RequestAreaQuery(ref bb, ref state, ref ctx, 0);

            // Assert — value is still -1, no exception
            ref var result = ref world.GetComponentRW<Blackboard1024>(entity);
            ref var s2     = ref Unsafe.As<Blackboard1024, HillAttackMutableState>(ref result);
            Assert.Equal(-1L, s2.CachedEqsRequestId);
        }
    }
}
