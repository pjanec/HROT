using System;
using Xunit;
using Fbt;
using Fbt.Compiler;
using Fbt.Runtime;
using Fbt.Tests.TestFixtures;

namespace Fbt.Tests.Unit
{
    /// <summary>
    /// Tests for TASK-K-05: BehaviorInstanceFlags.Paused halts interpreter ticking.
    /// Tests for TASK-K-06: Missing BTreeBuilder methods and NodeType.ObserverSelector.
    /// </summary>
    public class BTreeNewFeaturesTests
    {
        // ---- Shared delegates ----

        private static NodeStatus AlwaysSuccess(
            ref TestBlackboard bb, ref BehaviorTreeState state, ref MockContext ctx, int p)
        {
            ctx.CallCount++;
            return NodeStatus.Success;
        }

        private static NodeStatus AlwaysFailure(
            ref TestBlackboard bb, ref BehaviorTreeState state, ref MockContext ctx, int p)
        {
            ctx.CallCount++;
            return NodeStatus.Failure;
        }

        // ================================================================
        // TASK-K-05: Paused flag
        // ================================================================

        // K-05-T1: BehaviorInstanceFlags.Paused has bit value 1.
        [Fact]
        public void BehaviorInstanceFlags_Paused_HasBitValue1()
        {
            Assert.Equal(1, (byte)BehaviorInstanceFlags.Paused);
        }

        // K-05-T2: BehaviorInstanceFlags.None has bit value 0.
        [Fact]
        public void BehaviorInstanceFlags_None_HasBitValue0()
        {
            Assert.Equal(0, (byte)BehaviorInstanceFlags.None);
        }

        // K-05-T3: BehaviorTreeState.InstanceFlags field is accessible and initially zero.
        [Fact]
        public void BehaviorTreeState_InstanceFlags_DefaultIsNone()
        {
            var state = new BehaviorTreeState();
            Assert.Equal(BehaviorInstanceFlags.None, state.InstanceFlags);
        }

        // K-05-T4: Setting InstanceFlags.Paused causes Tick to return Running without executing the tree.
        [Fact]
        public void Interpreter_PausedFlag_ReturnsRunning_WithoutExecutingTree()
        {
            var blob = new BTreeBuilder<TestBlackboard, MockContext>()
                .Sequence(s => s.Action(AlwaysSuccess))
                .Compile("PauseTest");

            var registry = new BTreeBuilder<TestBlackboard, MockContext>().GetRegistry();
            // Re-build so registry is populated.
            var builder = new BTreeBuilder<TestBlackboard, MockContext>();
            builder.Sequence(s => s.Action(AlwaysSuccess));
            var fullBlob = builder.Compile("PauseTest");
            var interpreter = new Interpreter<TestBlackboard, MockContext>(fullBlob, builder.GetRegistry());

            var bb = new TestBlackboard();
            var state = new BehaviorTreeState();
            state.InstanceFlags = BehaviorInstanceFlags.Paused;
            var ctx = new MockContext();

            var result = interpreter.Tick(ref bb, ref state, ref ctx);

            Assert.Equal(NodeStatus.Running, result);
            Assert.Equal(0, ctx.CallCount); // Action must not have been called.
        }

        // K-05-T5: After clearing the Paused flag the tree resumes normal execution.
        [Fact]
        public void Interpreter_PausedFlag_Cleared_ResumesExecution()
        {
            var builder = new BTreeBuilder<TestBlackboard, MockContext>();
            builder.Sequence(s => s.Action(AlwaysSuccess));
            var blob = builder.Compile("ResumeTest");
            var interpreter = new Interpreter<TestBlackboard, MockContext>(blob, builder.GetRegistry());

            var bb = new TestBlackboard();
            var state = new BehaviorTreeState();
            var ctx = new MockContext();

            // Paused tick.
            state.InstanceFlags = BehaviorInstanceFlags.Paused;
            var paused = interpreter.Tick(ref bb, ref state, ref ctx);
            Assert.Equal(NodeStatus.Running, paused);
            Assert.Equal(0, ctx.CallCount);

            // Resume tick.
            state.InstanceFlags = BehaviorInstanceFlags.None;
            var resumed = interpreter.Tick(ref bb, ref state, ref ctx);
            Assert.Equal(NodeStatus.Success, resumed);
            Assert.Equal(1, ctx.CallCount);
        }

        // K-05-T6: BehaviorTreeState struct size must remain exactly 64 bytes.
        [Fact]
        public void BehaviorTreeState_SizeIs64Bytes()
        {
            Assert.Equal(64, System.Runtime.InteropServices.Marshal.SizeOf<BehaviorTreeState>());
        }

        // ================================================================
        // TASK-K-06: Missing BTreeBuilder methods + NodeType.ObserverSelector
        // ================================================================

        // K-06-T1: NodeType.ObserverSelector has value 5.
        [Fact]
        public void NodeType_ObserverSelector_HasValue5()
        {
            Assert.Equal(5, (byte)NodeType.ObserverSelector);
        }

        // K-06-T2: ForceSuccess builder method compiles to a node with correct type.
        [Fact]
        public void BTreeBuilder_ForceSuccess_ProducesCorrectNodeType()
        {
            var builder = new BTreeBuilder<TestBlackboard, MockContext>();
            builder.ForceSuccess(c => c.Action(AlwaysFailure));
            var blob = builder.Compile("FS");
            Assert.Equal(NodeType.ForceSuccess, blob.Nodes[0].Type);
        }

        // K-06-T3: ForceFailure builder method compiles to a node with correct type.
        [Fact]
        public void BTreeBuilder_ForceFailure_ProducesCorrectNodeType()
        {
            var builder = new BTreeBuilder<TestBlackboard, MockContext>();
            builder.ForceFailure(c => c.Action(AlwaysSuccess));
            var blob = builder.Compile("FF");
            Assert.Equal(NodeType.ForceFailure, blob.Nodes[0].Type);
        }

        // K-06-T4: UntilSuccess builder method compiles to a node with correct type.
        [Fact]
        public void BTreeBuilder_UntilSuccess_ProducesCorrectNodeType()
        {
            var builder = new BTreeBuilder<TestBlackboard, MockContext>();
            builder.UntilSuccess(c => c.Action(AlwaysSuccess));
            var blob = builder.Compile("US");
            Assert.Equal(NodeType.UntilSuccess, blob.Nodes[0].Type);
        }

        // K-06-T5: UntilFailure builder method compiles to a node with correct type.
        [Fact]
        public void BTreeBuilder_UntilFailure_ProducesCorrectNodeType()
        {
            var builder = new BTreeBuilder<TestBlackboard, MockContext>();
            builder.UntilFailure(c => c.Action(AlwaysFailure));
            var blob = builder.Compile("UF");
            Assert.Equal(NodeType.UntilFailure, blob.Nodes[0].Type);
        }

        // K-06-T6: ObserverSelector builder method compiles to correct node type.
        [Fact]
        public void BTreeBuilder_ObserverSelector_ProducesCorrectNodeType()
        {
            var builder = new BTreeBuilder<TestBlackboard, MockContext>();
            builder.ObserverSelector(c => c.Action(AlwaysSuccess));
            var blob = builder.Compile("OS");
            Assert.Equal(NodeType.ObserverSelector, blob.Nodes[0].Type);
        }

        // K-06-T7: Subtree builder method compiles to correct node type.
        [Fact]
        public void BTreeBuilder_Subtree_ProducesCorrectNodeType()
        {
            var builder = new BTreeBuilder<TestBlackboard, MockContext>();
            builder.Subtree("EnemyBehavior");
            var blob = builder.Compile("ST");
            Assert.Equal(NodeType.Subtree, blob.Nodes[0].Type);
        }

        // K-06-T8: ForceSuccess with a failing child returns Success (not Failure).
        [Fact]
        public void Interpreter_ForceSuccess_MakesFailureIntoSuccess()
        {
            var builder = new BTreeBuilder<TestBlackboard, MockContext>();
            builder.ForceSuccess(c => c.Action(AlwaysFailure));
            var blob = builder.Compile("FS_Exec");
            var interpreter = new Interpreter<TestBlackboard, MockContext>(blob, builder.GetRegistry());

            var bb = new TestBlackboard();
            var state = new BehaviorTreeState();
            var ctx = new MockContext();

            Assert.Equal(NodeStatus.Success, interpreter.Tick(ref bb, ref state, ref ctx));
        }

        // K-06-T9: ForceFailure with a succeeding child returns Failure.
        [Fact]
        public void Interpreter_ForceFailure_MakesSuccessIntoFailure()
        {
            var builder = new BTreeBuilder<TestBlackboard, MockContext>();
            builder.ForceFailure(c => c.Action(AlwaysSuccess));
            var blob = builder.Compile("FF_Exec");
            var interpreter = new Interpreter<TestBlackboard, MockContext>(blob, builder.GetRegistry());

            var bb = new TestBlackboard();
            var state = new BehaviorTreeState();
            var ctx = new MockContext();

            Assert.Equal(NodeStatus.Failure, interpreter.Tick(ref bb, ref state, ref ctx));
        }

        // K-06-T10: UntilSuccess keeps Running while child fails, returns Success when child succeeds.
        [Fact]
        public void Interpreter_UntilSuccess_ReturnsRunning_ThenSuccess()
        {
            int tickCount = 0;
            NodeStatus UntilSuccessAction(
                ref TestBlackboard bb, ref BehaviorTreeState state, ref MockContext ctx, int p)
            {
                tickCount++;
                return tickCount >= 3 ? NodeStatus.Success : NodeStatus.Failure;
            }

            var builder = new BTreeBuilder<TestBlackboard, MockContext>();
            builder.UntilSuccess(c => c.Action(UntilSuccessAction));
            var blob = builder.Compile("UntilS_Exec");
            var interpreter = new Interpreter<TestBlackboard, MockContext>(blob, builder.GetRegistry());

            var bb = new TestBlackboard();
            var state = new BehaviorTreeState();
            var ctx = new MockContext();

            Assert.Equal(NodeStatus.Running, interpreter.Tick(ref bb, ref state, ref ctx));
            Assert.Equal(NodeStatus.Running, interpreter.Tick(ref bb, ref state, ref ctx));
            Assert.Equal(NodeStatus.Success, interpreter.Tick(ref bb, ref state, ref ctx));
        }

        // K-06-T11: UntilFailure keeps Running while child succeeds, returns Success when child fails.
        [Fact]
        public void Interpreter_UntilFailure_ReturnsRunning_ThenSuccess()
        {
            int tickCount = 0;
            NodeStatus UntilFailureAction(
                ref TestBlackboard bb, ref BehaviorTreeState state, ref MockContext ctx, int p)
            {
                tickCount++;
                return tickCount >= 3 ? NodeStatus.Failure : NodeStatus.Success;
            }

            var builder = new BTreeBuilder<TestBlackboard, MockContext>();
            builder.UntilFailure(c => c.Action(UntilFailureAction));
            var blob = builder.Compile("UntilF_Exec");
            var interpreter = new Interpreter<TestBlackboard, MockContext>(blob, builder.GetRegistry());

            var bb = new TestBlackboard();
            var state = new BehaviorTreeState();
            var ctx = new MockContext();

            Assert.Equal(NodeStatus.Running, interpreter.Tick(ref bb, ref state, ref ctx));
            Assert.Equal(NodeStatus.Running, interpreter.Tick(ref bb, ref state, ref ctx));
            Assert.Equal(NodeStatus.Success, interpreter.Tick(ref bb, ref state, ref ctx));
        }

        // K-06-T12: ObserverSelector interprets like a Selector (first success short-circuits).
        [Fact]
        public void Interpreter_ObserverSelector_BehavesLikeSelector()
        {
            var builder = new BTreeBuilder<TestBlackboard, MockContext>();
            builder.ObserverSelector(c => c
                .Action(AlwaysSuccess)
                .Action(AlwaysSuccess));
            var blob = builder.Compile("OS_Exec");
            var interpreter = new Interpreter<TestBlackboard, MockContext>(blob, builder.GetRegistry());

            var bb = new TestBlackboard();
            var state = new BehaviorTreeState();
            var ctx = new MockContext();

            var result = interpreter.Tick(ref bb, ref state, ref ctx);
            Assert.Equal(NodeStatus.Success, result);
            Assert.Equal(1, ctx.CallCount); // Only first child called.
        }

        // K-06-T13: Subtree node returns Failure (stub behaviour).
        [Fact]
        public void Interpreter_Subtree_ReturnsFailure_AsStub()
        {
            var builder = new BTreeBuilder<TestBlackboard, MockContext>();
            builder.Subtree("TestSubtree");
            var blob = builder.Compile("ST_Exec");
            var interpreter = new Interpreter<TestBlackboard, MockContext>(blob, builder.GetRegistry());

            var bb = new TestBlackboard();
            var state = new BehaviorTreeState();
            var ctx = new MockContext();

            Assert.Equal(NodeStatus.Failure, interpreter.Tick(ref bb, ref state, ref ctx));
        }
    }
}
