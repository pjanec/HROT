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
    // CE-504 slice 4: the deactivator takes the shared stateful form
    //   (ref PlatoonHillAttackParams p, ref HillAttackMutableState s, Entity self, EntityRepository world).
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
            var p     = new PlatoonHillAttackParams();
            var s     = new HillAttackMutableState { CachedEqsRequestId = 42 };

            // Act
            HillAttackCommanderNodes.Deactivate_RequestAreaQuery(ref p, ref s, ctx.Self, ctx.World);

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
            var p     = new PlatoonHillAttackParams();
            var s     = new HillAttackMutableState { CachedEqsRequestId = 42 };

            // Act + Assert (no exception) — and the id is still cleared.
            HillAttackCommanderNodes.Deactivate_RequestAreaQuery(ref p, ref s, ctx.Self, ctx.World);
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
            var p     = new PlatoonHillAttackParams();
            var s     = new HillAttackMutableState { CachedEqsRequestId = -1 };

            // Act
            HillAttackCommanderNodes.Deactivate_RequestAreaQuery(ref p, ref s, ctx.Self, ctx.World);

            // Assert — value is still -1, no exception.
            Assert.Equal(-1L, s.CachedEqsRequestId);
        }
    }
}
