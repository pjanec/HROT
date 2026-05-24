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
    /// Tests for EQL-009: NodeDefinition bit-flag layout.
    /// T1-T6: struct API; T7-T9: AOT integration via BTreeBuilder (EQL-010 final state).
    /// </summary>
    public class NodeDefinitionBitFlagTests
    {
        // T1: RawPayloadIndex with value 5, no bit 31 set -> PayloadIndex == 5
        [Fact]
        public void T1_PayloadIndex_MasksBit31()
        {
            var d = new NodeDefinition { RawPayloadIndex = 5 };
            Assert.Equal(5, d.PayloadIndex);
        }

        // T2: RawPayloadIndex = 5 -> IsResourceOwning == false
        [Fact]
        public void T2_IsResourceOwning_FalseWhenBit31Clear()
        {
            var d = new NodeDefinition { RawPayloadIndex = 5 };
            Assert.False(d.IsResourceOwning);
        }

        // T3: After SetResourceOwning(), PayloadIndex still == 5 (bits 0-30 unchanged)
        [Fact]
        public void T3_SetResourceOwning_PreservesBits0To30()
        {
            var d = new NodeDefinition { RawPayloadIndex = 5 };
            d.SetResourceOwning();
            Assert.Equal(5, d.PayloadIndex);
        }

        // T4: After SetResourceOwning(), IsResourceOwning == true
        [Fact]
        public void T4_SetResourceOwning_SetsBit31()
        {
            var d = new NodeDefinition { RawPayloadIndex = 5 };
            d.SetResourceOwning();
            Assert.True(d.IsResourceOwning);
        }

        // T5: RawPayloadIndex with bit 31 set -> PayloadIndex masks it out
        [Fact]
        public void T5_PayloadIndex_MasksExistingBit31()
        {
            var d = new NodeDefinition { RawPayloadIndex = unchecked((int)0x80000005) };
            Assert.Equal(5, d.PayloadIndex);
        }

        // T6: sizeof(NodeDefinition) == 8
        [Fact]
        public void T6_NodeDefinition_IsSizeOf8Bytes()
        {
            Assert.Equal(8, Marshal.SizeOf<NodeDefinition>());
        }

        // T7: BTreeBuilder compiles a tree with a registered deactivator ->
        //     the action node has IsResourceOwning == true on the blob BEFORE Interpreter construction.
        [Fact]
        public void T7_AotBaking_ResourceOwningActionHasBitSet()
        {
            var builder = new BTreeBuilder<TestBlackboard, MockContext>();
            builder.Sequence(s => s.Action(
                (ref TestBlackboard bb, ref BehaviorTreeState st, ref MockContext ctx, int p) =>
                    NodeStatus.Success));

            var blob = builder.Compile("T7");
            var registry = builder.GetRegistry();

            // Register a deactivator for the action before re-compiling isn't needed;
            // BTreeBuilder passes the registry's TryGetDeactivator during Compile.
            // Re-register after compile to demonstrate the registry-driven approach:
            string actionKey = blob.MethodNames[blob.Nodes[1].PayloadIndex];
            registry.RegisterDeactivator(actionKey,
                (ref TestBlackboard bb2, ref BehaviorTreeState st, ref MockContext ctx2, int p) => { });

            // Recompile so the deactivator is present when FlattenToBlob calls isResourceOwning
            var blob2 = builder.Compile("T7");
            Assert.True(blob2.Nodes[1].IsResourceOwning);
        }

        // T8: Action with no registered deactivator -> IsResourceOwning == false on blob.
        [Fact]
        public void T8_AotBaking_NoDeactivator_BitNotSet()
        {
            var builder = new BTreeBuilder<TestBlackboard, MockContext>();
            builder.Sequence(s => s.Action(
                (ref TestBlackboard bb, ref BehaviorTreeState st, ref MockContext ctx, int p) =>
                    NodeStatus.Success));

            // Do NOT register any deactivator
            var blob = builder.Compile("T8");
            Assert.False(blob.Nodes[1].IsResourceOwning);
        }

        // T9: Composite node (Sequence, index 0) -> IsResourceOwning == false.
        //     SetResourceOwning is only called for Action/Condition nodes.
        [Fact]
        public void T9_CompositeNode_IsResourceOwningAlwaysFalse()
        {
            var builder = new BTreeBuilder<TestBlackboard, MockContext>();
            builder.Sequence(s => s.Action(
                (ref TestBlackboard bb, ref BehaviorTreeState st, ref MockContext ctx, int p) =>
                    NodeStatus.Success));

            var registry = builder.GetRegistry();
            // Register deactivator so compile would set bits for action nodes
            string actionKey;
            var tmpBlob = builder.Compile("T9tmp");
            actionKey = tmpBlob.MethodNames[tmpBlob.Nodes[1].PayloadIndex];
            registry.RegisterDeactivator(actionKey,
                (ref TestBlackboard bb2, ref BehaviorTreeState st, ref MockContext ctx2, int p) => { });

            var blob = builder.Compile("T9");
            // Node 0 is the Sequence composite -- must NOT have IsResourceOwning set
            Assert.Equal(NodeType.Sequence, blob.Nodes[0].Type);
            Assert.False(blob.Nodes[0].IsResourceOwning);
            // Node 1 is the Action -- MUST have IsResourceOwning set
            Assert.Equal(NodeType.Action, blob.Nodes[1].Type);
            Assert.True(blob.Nodes[1].IsResourceOwning);
        }
    }
}
