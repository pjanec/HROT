using System;
using Xunit;
using Fbt;
using Fbt.Compiler;
using Fbt.Runtime;
using Fbt.Serialization;
using Fbt.Tests.TestFixtures;

namespace Fbt.Tests.Unit
{
    /// <summary>Tests for EQL-010: AOT compilation pipeline.</summary>
    public class AotCompilationPipelineTests
    {
        // T1: BTreeBuilder.Compile with deactivator registered for action A ->
        //     blob has IsResourceOwning set on the action node BEFORE Interpreter construction.
        [Fact]
        public void T1_BTreeBuilder_SetsResourceOwningBit_WhenDeactivatorRegistered()
        {
            var builder = new BTreeBuilder<TestBlackboard, MockContext>();
            builder.Sequence(s => s.Action(
                (ref TestBlackboard bb, ref BehaviorTreeState st, ref MockContext ctx, int p) =>
                    NodeStatus.Success));
            // Get a blob to find the method key, then re-register deactivator
            var tmpBlob = builder.Compile("T1tmp");
            string key = tmpBlob.MethodNames[tmpBlob.Nodes[1].PayloadIndex];
            builder.GetRegistry().RegisterDeactivator(key,
                (ref TestBlackboard bb2, ref BehaviorTreeState st, ref MockContext ctx2, int p) => { });

            var blob = builder.Compile("T1");
            // Action is at node index 1 (Sequence=0, Action=1)
            Assert.True(blob.Nodes[1].IsResourceOwning);
        }

        // T2: Action B has no deactivator -> blob.Nodes[actionBIndex].IsResourceOwning == false.
        [Fact]
        public void T2_BTreeBuilder_NoDeactivator_BitNotSet()
        {
            var builder = new BTreeBuilder<TestBlackboard, MockContext>();
            builder.Sequence(s => s.Action(
                (ref TestBlackboard bb, ref BehaviorTreeState st, ref MockContext ctx, int p) =>
                    NodeStatus.Success));
            // No RegisterDeactivator call
            var blob = builder.Compile("T2");
            Assert.False(blob.Nodes[1].IsResourceOwning);
        }

        // T3: BuilderNode.IsResourceOwning = true with no registry match ->
        //     compiled blob still has IsResourceOwning bit set.
        [Fact]
        public void T3_BuilderNodeFlag_HonoredEvenWithoutRegistryMatch()
        {
            // Build a blob directly via FlattenToBlob with a BuilderNode that has IsResourceOwning=true
            // but pass no isResourceOwning delegate (null) -- the BuilderNode flag alone should set the bit.
            var action = new BuilderNode
            {
                Type = NodeType.Action,
                MethodName = "SomeAction",
                IsResourceOwning = true
            };
            var seq = new BuilderNode { Type = NodeType.Sequence };
            seq.Children.Add(action);

            var blob = TreeCompiler.FlattenToBlob(seq, "T3", null);
            // Node 0 = Sequence, Node 1 = Action
            Assert.True(blob.Nodes[1].IsResourceOwning);
        }

        // T4: Sequence node in the same tree -> IsResourceOwning == false.
        [Fact]
        public void T4_CompositeNode_NeverHasResourceOwningBit()
        {
            var builder = new BTreeBuilder<TestBlackboard, MockContext>();
            builder.Sequence(s => s.Action(
                (ref TestBlackboard bb, ref BehaviorTreeState st, ref MockContext ctx, int p) =>
                    NodeStatus.Success));
            var tmpBlob = builder.Compile("T4tmp");
            string key = tmpBlob.MethodNames[tmpBlob.Nodes[1].PayloadIndex];
            builder.GetRegistry().RegisterDeactivator(key,
                (ref TestBlackboard bb2, ref BehaviorTreeState st, ref MockContext ctx2, int p) => { });

            var blob = builder.Compile("T4");
            Assert.Equal(NodeType.Sequence, blob.Nodes[0].Type);
            Assert.False(blob.Nodes[0].IsResourceOwning);
        }

        // T5: FlattenToBlob called without delegate -> no IsResourceOwning bits set.
        [Fact]
        public void T5_FlattenToBlob_NullDelegate_NoBitsSet()
        {
            var action = new BuilderNode { Type = NodeType.Action, MethodName = "SomeAction" };
            var seq = new BuilderNode { Type = NodeType.Sequence };
            seq.Children.Add(action);

            var blob = TreeCompiler.FlattenToBlob(seq, "T5");
            Assert.False(blob.Nodes[1].IsResourceOwning);
        }

        // T6 (regression - no patch loop): Hybrid lifecycle tests L1-L8 pass via BTreeBuilder.
        //     Verified by running HybridLifecycleTests externally; this test checks the
        //     Interpreter constructor does NOT contain a node-patching loop (compile-time only).
        [Fact]
        public void T6_Interpreter_HasNo_PatchingLoop_InConstructor()
        {
            // If the patch loop existed, constructing an Interpreter from a blob where bits
            // are already set (by AOT) and the registry has no deactivators would clear them.
            // Verify that a V2 blob's resource-owning bit is NOT cleared by construction.
            var action = new BuilderNode
            {
                Type = NodeType.Action,
                MethodName = "SomeAction",
                IsResourceOwning = true // explicitly set
            };
            var seq = new BuilderNode { Type = NodeType.Sequence };
            seq.Children.Add(action);

            var blob = TreeCompiler.FlattenToBlob(seq, "T6", null);
            // ManuallySet bit via FlattenToBlob with explicit BuilderNode flag
            Assert.True(blob.Nodes[1].IsResourceOwning);

            // Construct Interpreter with an EMPTY registry (no deactivators)
            var registry = new ActionRegistry<TestBlackboard, MockContext>();
            registry.Register("SomeAction",
                (ref TestBlackboard bb, ref BehaviorTreeState st, ref MockContext ctx, int p) =>
                    NodeStatus.Success);

            var interpreter = new Interpreter<TestBlackboard, MockContext>(blob, registry);
            // Bit must still be set (constructor did not clear it via a spurious patch loop)
            Assert.True(blob.Nodes[1].IsResourceOwning);
        }

        // T7: All three projects build without errors (verified by running dotnet build).
        //     No automated assertion; build success is the test.
    }
}
