using System;
using System.Runtime.InteropServices;
using Xunit;
using Fbt;
using Fbt.Compiler;
using Fbt.Compiler.Graph;
using Fbt.Tests.TestFixtures;

namespace Fbt.Tests.Unit
{
    public class GraphTests
    {
        // ---- Shared delegates ----

        private static NodeStatus AlwaysSuccess(
            ref TestBlackboard bb, ref BehaviorTreeState state, ref MockContext ctx, int p)
            => NodeStatus.Success;

        private static NodeStatus AlwaysFailure(
            ref TestBlackboard bb, ref BehaviorTreeState state, ref MockContext ctx, int p)
            => NodeStatus.Failure;

        // ---- Test struct for expression-bound graph test ----

        [StructLayout(LayoutKind.Sequential)]
        private struct SingleIntBlackboard
        {
            public int Value;
        }

        private static NodeStatus IntAction(
            ref int data, ref BehaviorTreeState state, ref MockContext ctx)
        {
            data = 0;
            return NodeStatus.Success;
        }

        // ---- FBT-005: SC1 / Test 1 ----

        [Fact]
        public void ToGraph_SimpleSequence_ProducesCorrectRootNode()
        {
            var graph = new BTreeBuilder<TestBlackboard, MockContext>()
                .Sequence(s => s.Action(AlwaysSuccess))
                .ToGraph("MyTree");

            Assert.IsType<CompositeNode>(graph.RootNode);
            Assert.Equal(NodeType.Sequence, graph.RootNode!.Type);
        }

        // ---- FBT-005: SC1 / Test 2 ----

        [Fact]
        public void ToGraph_ChildCount_MatchesBuilderChildren()
        {
            var graph = new BTreeBuilder<TestBlackboard, MockContext>()
                .Sequence(s => s
                    .Action(AlwaysSuccess)
                    .Action(AlwaysFailure))
                .ToGraph("ChildCountTest");

            var root = Assert.IsType<CompositeNode>(graph.RootNode);
            Assert.Equal(2, root.Children.Count);
        }

        // ---- FBT-005: SC1 / Test 3 ----

        [Fact]
        public void ToGraph_LeafNodes_AreLogicNodes()
        {
            var graph = new BTreeBuilder<TestBlackboard, MockContext>()
                .Sequence(s => s
                    .Action(AlwaysSuccess)
                    .Condition(AlwaysFailure))
                .ToGraph("LeafTypeTest");

            var root = Assert.IsType<CompositeNode>(graph.RootNode);
            Assert.IsType<LogicNode>(root.Children[0]);
            Assert.IsType<LogicNode>(root.Children[1]);
            Assert.Equal(NodeType.Action, root.Children[0].Type);
            Assert.Equal(NodeType.Condition, root.Children[1].Type);
        }

        // ---- FBT-005: SC2 / Test 4 ----

        [Fact]
        public void ToGraph_AllNodes_HaveUniqueNonEmptyVisualIds()
        {
            var graph = new BTreeBuilder<TestBlackboard, MockContext>()
                .Sequence(s => s
                    .Action(AlwaysSuccess)
                    .Action(AlwaysFailure))
                .ToGraph("UniqueIdTest");

            var root = Assert.IsType<CompositeNode>(graph.RootNode);
            var child0 = root.Children[0];
            var child1 = root.Children[1];

            // None may be Guid.Empty
            Assert.NotEqual(Guid.Empty, root.VisualId);
            Assert.NotEqual(Guid.Empty, child0.VisualId);
            Assert.NotEqual(Guid.Empty, child1.VisualId);

            // All must be distinct
            Assert.NotEqual(root.VisualId, child0.VisualId);
            Assert.NotEqual(root.VisualId, child1.VisualId);
            Assert.NotEqual(child0.VisualId, child1.VisualId);
        }

        // ---- FBT-005: Test 5 ----

        [Fact]
        public void ToGraph_ParentRefs_AreCorrect()
        {
            var graph = new BTreeBuilder<TestBlackboard, MockContext>()
                .Sequence(s => s.Action(AlwaysSuccess))
                .ToGraph("ParentRefTest");

            var root = Assert.IsType<CompositeNode>(graph.RootNode);
            // Root has no parent
            Assert.Null(root.Parent);
            // Child's parent is the root
            Assert.Single(root.Children);
            Assert.Same(root, root.Children[0].Parent);
        }

        // ---- FBT-005: Test 6 ----

        [Fact]
        public void ToGraph_ExpressionBound_LogicNode_HasTargetFieldName()
        {
            var graph = new BTreeBuilder<SingleIntBlackboard, MockContext>()
                .Action(bb => bb.Value, IntAction)
                .ToGraph("ExprBoundTest");

            var logicNode = Assert.IsType<LogicNode>(graph.RootNode);
            Assert.Equal("Value", logicNode.TargetFieldName);
            Assert.False(string.IsNullOrEmpty(logicNode.TargetDtoType));
        }

        // ---- FBT-005: Test 7 ----

        [Fact]
        public void ToGraph_TreeId_IsNonEmpty()
        {
            var graph = new BTreeBuilder<TestBlackboard, MockContext>()
                .Action(AlwaysSuccess)
                .ToGraph("TreeIdTest");

            Assert.NotEqual(Guid.Empty, graph.TreeId);
        }
    }
}
