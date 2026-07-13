using Fbt;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Hrot.AI.Behaviors.Brains;
using Xunit;

namespace Hrot.IG.Tests.Brains
{
    // ============================================================================
    // TASK-EQL-008 / S3-G: Unit tests for HillAttackCommanderNodes.Deactivate_RequestAreaQuery
    //
    // S3-G: the deactivator is now the five-parameter stateful shape
    //   (ref PlatoonHillAttackParams p, ref HillAttackMutableState s, ref BehaviorTreeState, ref BTreeContext, int).
    // It operates on the working state by ref (the emitted wrapper projects it from the Behavior-scoped
    // partition slot) and frees the cached EQS request slot — no Blackboard1024 / Unsafe.As projection.
    // These unit tests exercise the method logic directly with a local working-state value.
    // ============================================================================

    public sealed class HillAttackCommanderNodesDeactivatorTests
    {
        // ── T1 ────────────────────────────────────────────────────────────────────

        [Fact]
        public void T1_CachedIdSet_ResetsToClearedValue()
        {
            // Arrange
            using var world = new EntityRepository();
            var entity = world.CreateEntity();

            var ctx   = new BTreeContext { Self = entity, World = world };
            var state = new BehaviorTreeState();
            var p     = new PlatoonHillAttackParams();
            var s     = new HillAttackMutableState { CachedEqsRequestId = 42 };

            // Act
            HillAttackCommanderNodes.Deactivate_RequestAreaQuery(ref p, ref s, ref state, ref ctx, 0);

            // Assert — the in-flight request id is cleared.
            Assert.Equal(-1L, s.CachedEqsRequestId);
        }

        // ── T2 ────────────────────────────────────────────────────────────────────

        [Fact]
        public void T2_NoEqsInfrastructure_NoException()
        {
            // Arrange — a world with no EQS batch component; FreeAreaQuerySlot must be a safe no-op.
            using var world = new EntityRepository();
            var entity = world.CreateEntity();

            var ctx   = new BTreeContext { Self = entity, World = world };
            var state = new BehaviorTreeState();
            var p     = new PlatoonHillAttackParams();
            var s     = new HillAttackMutableState { CachedEqsRequestId = 42 };

            // Act + Assert (no exception) — and the id is still cleared.
            HillAttackCommanderNodes.Deactivate_RequestAreaQuery(ref p, ref s, ref state, ref ctx, 0);
            Assert.Equal(-1L, s.CachedEqsRequestId);
        }

        // ── T3 ────────────────────────────────────────────────────────────────────

        [Fact]
        public void T3_AlreadyCleared_ValueRemainsMinusOne()
        {
            // Arrange
            using var world = new EntityRepository();
            var entity = world.CreateEntity();

            var ctx   = new BTreeContext { Self = entity, World = world };
            var state = new BehaviorTreeState();
            var p     = new PlatoonHillAttackParams();
            var s     = new HillAttackMutableState { CachedEqsRequestId = -1 };

            // Act
            HillAttackCommanderNodes.Deactivate_RequestAreaQuery(ref p, ref s, ref state, ref ctx, 0);

            // Assert — value is still -1, no exception.
            Assert.Equal(-1L, s.CachedEqsRequestId);
        }
    }
}
