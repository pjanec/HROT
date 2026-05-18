using System;
using System.Runtime.InteropServices;
using Xunit;
using Fbt;
using Fbt.Compiler;
using Fbt.Runtime;
using Fbt.Tests.TestFixtures;

namespace Fbt.Tests.Unit
{
    /// <summary>
    /// Dedicated validation test suite for Phase 1 (FBT-006).
    /// Covers negative paths -- invalid tree constructs that must be rejected at
    /// compile time rather than crashing at runtime.
    /// </summary>
    public class BuilderValidationTests
    {
        // ---- Shared delegates ----

        private static NodeStatus AlwaysSuccess(
            ref TestBlackboard bb, ref BehaviorTreeState state, ref MockContext ctx, int p)
            => NodeStatus.Success;

        private static NodeStatus AlwaysFailure(
            ref TestBlackboard bb, ref BehaviorTreeState state, ref MockContext ctx, int p)
        {
            ctx.CallCount++;
            return NodeStatus.Failure;
        }

        private static NodeStatus CountingAction(
            ref TestBlackboard bb, ref BehaviorTreeState state, ref MockContext ctx, int p)
        {
            ctx.ActionCallCount++;
            return NodeStatus.Success;
        }

        // ---- A blackboard struct that exceeds the 128-byte limit ----
        // 33 int fields = 132 bytes > 128; used for the DtoTooLarge test.
        [StructLayout(LayoutKind.Sequential)]
        private struct LargeBlackboard
        {
            public int F00; public int F01; public int F02; public int F03;
            public int F04; public int F05; public int F06; public int F07;
            public int F08; public int F09; public int F10; public int F11;
            public int F12; public int F13; public int F14; public int F15;
            public int F16; public int F17; public int F18; public int F19;
            public int F20; public int F21; public int F22; public int F23;
            public int F24; public int F25; public int F26; public int F27;
            public int F28; public int F29; public int F30; public int F31;
            public int F32; // 33 * 4 = 132 bytes
        }

        private static NodeStatus ReusableInt(
            ref int val, ref BehaviorTreeState state, ref MockContext ctx)
            => NodeStatus.Success;

        // ---- FBT-006: Mandatory negative-path tests ----

        /// <summary>FBT-006 SC: nested Repeater must throw BehaviorTreeBuildException.</summary>
        [Fact]
        public void NestedRepeater_ThrowsBehaviorTreeBuildException()
        {
            var ex = Assert.Throws<BehaviorTreeBuildException>(() =>
                new BTreeBuilder<TestBlackboard, MockContext>()
                    .Repeater(2, r => r.Repeater(3, inner => inner.Action(AlwaysSuccess)))
                    .Compile("NestedRepeaterTree"));

            Assert.Contains("Repeater", ex.Message);
            Assert.Contains("nested", ex.Message);
        }

        /// <summary>FBT-006 SC: nested Parallel must throw BehaviorTreeBuildException.</summary>
        [Fact]
        public void NestedParallel_ThrowsBehaviorTreeBuildException()
        {
            var ex = Assert.Throws<BehaviorTreeBuildException>(() =>
                new BTreeBuilder<TestBlackboard, MockContext>()
                    .Parallel(0, outer => outer.Parallel(0, inner => inner.Action(AlwaysSuccess)))
                    .Compile("NestedParallelTree"));

            Assert.Contains("Parallel", ex.Message);
            Assert.Contains("nested", ex.Message);
        }

        /// <summary>FBT-006 SC: expression binding with a DTO struct whose sizeof > 128 bytes
        /// must throw BehaviorTreeBuildException at Compile() time.</summary>
        [Fact]
        public void DtoTooLarge_ThrowsBehaviorTreeBuildException()
        {
            var ex = Assert.Throws<BehaviorTreeBuildException>(() =>
                new BTreeBuilder<LargeBlackboard, MockContext>()
                    .Condition(bb => bb.F00, ReusableInt)
                    .Compile("LargeDtoTree"));

            // The message must mention the size limit so the developer knows what to fix.
            Assert.Contains("128", ex.Message);
        }

        /// <summary>FBT-006 SC: a correctly structured tree compiles without exception (control).</summary>
        [Fact]
        public void ValidTree_DoesNotThrow()
        {
            // Should compile without any exception.
            var blob = new BTreeBuilder<TestBlackboard, MockContext>()
                .Sequence(s => s
                    .Condition(AlwaysSuccess)
                    .Action(AlwaysSuccess))
                .Compile("ValidTree");

            Assert.NotNull(blob);
            Assert.Equal(3, blob.Nodes.Length);
        }

        // ---- Additional gap-filling tests ----

        /// <summary>Calling Compile() on an empty builder must throw InvalidOperationException.</summary>
        [Fact]
        public void EmptyBuilder_Compile_ThrowsInvalidOperationException()
        {
            Assert.Throws<InvalidOperationException>(() =>
                new BTreeBuilder<TestBlackboard, MockContext>().Compile("EmptyTree"));
        }

        /// <summary>A builder with two top-level nodes must throw InvalidOperationException.</summary>
        [Fact]
        public void TwoRootNodes_Compile_ThrowsInvalidOperationException()
        {
            // Adding two actions at the root level creates two root entries.
            Assert.Throws<InvalidOperationException>(() =>
                new BTreeBuilder<TestBlackboard, MockContext>()
                    .Action(AlwaysSuccess)
                    .Action(AlwaysSuccess)
                    .Compile("TwoRoots"));
        }

        /// <summary>In a Sequence, when the first condition returns Failure, the action must
        /// never be called (short-circuit semantics).</summary>
        [Fact]
        public void Condition_ReturnsFailure_ActionNotCalled()
        {
            var builder = new BTreeBuilder<TestBlackboard, MockContext>();
            var blob = builder
                .Sequence(s => s
                    .Condition(AlwaysFailure)
                    .Action(CountingAction))
                .Compile("ShortCircuit");

            var registry = builder.GetRegistry();
            var interpreter = new Interpreter<TestBlackboard, MockContext>(blob, registry);
            var bb = new TestBlackboard();
            var state = new BehaviorTreeState();
            var ctx = new MockContext();

            var result = interpreter.Tick(ref bb, ref state, ref ctx);

            Assert.Equal(NodeStatus.Failure, result);
            // The condition incremented CallCount; the action must NOT have incremented ActionCallCount.
            Assert.Equal(1, ctx.CallCount);
            Assert.Equal(0, ctx.ActionCallCount);
        }
    }
}
