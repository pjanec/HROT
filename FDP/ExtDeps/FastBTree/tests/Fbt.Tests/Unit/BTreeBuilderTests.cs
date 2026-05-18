using System;
using Xunit;
using Fbt;
using Fbt.Compiler;
using Fbt.Runtime;
using Fbt.Serialization;
using Fbt.Tests.TestFixtures;

namespace Fbt.Tests.Unit
{
    public class BTreeBuilderTests
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

        private static NodeStatus AlternateDelegate(
            ref TestBlackboard bb, ref BehaviorTreeState state, ref MockContext ctx, int p)
            => NodeStatus.Success;

        // ---- FBT-002: SC1 ----

        [Fact]
        public void Compile_SimpleSequence_ProducesCorrectBlob()
        {
            var blob = new BTreeBuilder<TestBlackboard, MockContext>()
                .Sequence(s => s
                    .Action(AlwaysSuccess)
                    .Action(AlternateDelegate))
                .Compile("Test");

            Assert.Equal(3, blob.Nodes.Length);
            Assert.Equal(NodeType.Sequence, blob.Nodes[0].Type);
            Assert.Equal(NodeType.Action, blob.Nodes[1].Type);
            Assert.Equal(NodeType.Action, blob.Nodes[2].Type);
            Assert.Equal(2, blob.MethodNames.Length);
        }

        // ---- FBT-002: SC2 (integration: interpreter executes correctly) ----

        [Fact]
        public void Compile_InterpreterExecutesCorrectly_ConditionFails()
        {
            var builder = new BTreeBuilder<TestBlackboard, MockContext>();
            var blob = builder
                .Sequence(s => s
                    .Condition(AlwaysFailure)
                    .Action(AlwaysSuccess))
                .Compile("FailCond");

            var registry = builder.GetRegistry();
            var interpreter = new Interpreter<TestBlackboard, MockContext>(blob, registry);
            var bb = new TestBlackboard();
            var state = new BehaviorTreeState();
            var ctx = new MockContext();

            var result = interpreter.Tick(ref bb, ref state, ref ctx);

            Assert.Equal(NodeStatus.Failure, result);
            Assert.Equal(1, ctx.CallCount); // Only condition was called, action was skipped
        }

        // ---- FBT-002: SC3 ----

        [Fact]
        public void Compile_NestedComposites_CorrectSubtreeOffsets()
        {
            // Selector(Sequence(Cond, Action), Action)
            // 0: Selector   subtree=5, children=2
            // 1: Sequence   subtree=3, children=2
            // 2: Condition  subtree=1
            // 3: Action     subtree=1
            // 4: Action     subtree=1
            var blob = new BTreeBuilder<TestBlackboard, MockContext>()
                .Selector(sel => sel
                    .Sequence(seq => seq
                        .Condition(AlwaysFailure)
                        .Action(AlwaysSuccess))
                    .Action(AlternateDelegate))
                .Compile("Nested");

            Assert.Equal(5, blob.Nodes.Length);
            Assert.Equal(NodeType.Selector, blob.Nodes[0].Type);
            Assert.Equal(5, blob.Nodes[0].SubtreeOffset);
            Assert.Equal(NodeType.Sequence, blob.Nodes[1].Type);
            Assert.Equal(3, blob.Nodes[1].SubtreeOffset);
        }

        // ---- FBT-002: SC4 ----

        [Fact]
        public void Compile_DuplicateDelegate_SingleMethodNameEntry()
        {
            // Same delegate used twice -> only one MethodNames entry
            var blob = new BTreeBuilder<TestBlackboard, MockContext>()
                .Sequence(s => s
                    .Action(AlwaysSuccess)
                    .Action(AlwaysSuccess))
                .Compile("Dedup");

            Assert.Single(blob.MethodNames);
        }

        // ---- FBT-002: SC5 ----

        [Fact]
        public void Compile_NestedRepeater_ThrowsBehaviorTreeBuildException()
        {
            var ex = Assert.Throws<BehaviorTreeBuildException>(() =>
                new BTreeBuilder<TestBlackboard, MockContext>()
                    .Repeater(2, outer => outer
                        .Repeater(3, inner => inner
                            .Action(AlwaysSuccess)))
                    .Compile("NestedRep"));

            Assert.Contains("Repeater", ex.Message);
            Assert.Contains("nested", ex.Message.ToLowerInvariant());
        }

        // ---- FBT-002: SC6 (VisualId stored in DebugMetadata) ----

        [Fact]
        public void Compile_VisualIdProvided_StoredInDebugMetadata()
        {
            var id = Guid.NewGuid();
            var blob = new BTreeBuilder<TestBlackboard, MockContext>()
                .Action(AlwaysSuccess, visualId: id)
                .Compile("VizId");

            Assert.NotNull(blob.DebugMetadata);
            Assert.Equal(id.ToString(), blob.DebugMetadata![0].VisualId);
        }

        // ---- FBT-002: SC7 (auto-assigned VisualId when omitted) ----

        [Fact]
        public void Compile_VisualIdOmitted_AutoAssignedNonEmpty()
        {
            var blob = new BTreeBuilder<TestBlackboard, MockContext>()
                .Action(AlwaysSuccess)
                .Compile("AutoViz");

            Assert.NotNull(blob.DebugMetadata);
            string vid = blob.DebugMetadata![0].VisualId;
            Assert.NotEqual(string.Empty, vid);
            Assert.NotEqual(Guid.Empty.ToString(), vid);
        }

        // ---- Additional: GetRegistry returns working registry ----

        [Fact]
        public void GetRegistry_ContainsDelegates_InterpreterCanExecute()
        {
            var builder = new BTreeBuilder<TestBlackboard, MockContext>();
            var blob = builder
                .Action(AlwaysSuccess)
                .Compile("RegTest");

            var registry = builder.GetRegistry();
            var interpreter = new Interpreter<TestBlackboard, MockContext>(blob, registry);
            var bb = new TestBlackboard();
            var state = new BehaviorTreeState();
            var ctx = new MockContext();

            var result = interpreter.Tick(ref bb, ref state, ref ctx);
            Assert.Equal(NodeStatus.Success, result);
            Assert.Equal(1, ctx.CallCount);
        }
    }
}
