using System.Runtime.CompilerServices;
using Xunit;
using Fbt;

namespace Fbt.Tests.Unit
{
    public class DataStructuresTests
    {
        [Fact]
        public void NodeDefinition_ShouldBe8Bytes()
        {
            var size = Unsafe.SizeOf<NodeDefinition>();
            Assert.Equal(8, size);
        }

        [Fact]
        public void NodeDefinition_FieldOffsets_MatchSpecification()
        {
            var def = new NodeDefinition
            {
                Type = NodeType.Action,
                ChildCount = 2,
                SubtreeOffset = 5,
                PayloadIndex = 10
            };
            
            Assert.Equal(NodeType.Action, def.Type);
            Assert.Equal(2, def.ChildCount);
            Assert.Equal(5, def.SubtreeOffset);
            Assert.Equal(10, def.PayloadIndex);
        }

        [Fact]
        public void NodeDefinition_CanBeUsedInArray()
        {
            var array = new NodeDefinition[100];
            array[0] = new NodeDefinition { Type = NodeType.Root };
            Assert.Equal(NodeType.Root, array[0].Type);
        }

        [Fact]
        public void BehaviorTreeState_ShouldBe64Bytes()
        {
            var size = Unsafe.SizeOf<BehaviorTreeState>();
            Assert.Equal(64, size);
        }

        [Fact]
        public void BehaviorTreeState_Reset_ClearsAllState()
        {
            var state = new BehaviorTreeState();
            state.RunningNodeIndex = 5;
            state.TreeVersion = 10;
            unsafe
            {
                state.NodeIndexStack[0] = 3;
                state.LocalRegisters[0] = 42;
                state.AsyncHandles[0] = 999;
            }
            
            state.Reset();
            
            Assert.Equal(0, state.RunningNodeIndex);
            Assert.Equal(11u, state.TreeVersion); // Incremented
            unsafe
            {
                Assert.Equal(0, state.NodeIndexStack[0]);
                Assert.Equal(0, state.LocalRegisters[0]);
                Assert.Equal(0ul, state.AsyncHandles[0]);
            }
        }

        [Fact]
        public void BehaviorTreeState_PushPop_ManagesStack()
        {
            var state = new BehaviorTreeState();
            
            state.PushNode(5);
            Assert.Equal(1, state.StackPointer);
            Assert.Equal(5, state.CurrentRunningNode);
            
            state.PushNode(10);
            Assert.Equal(2, state.StackPointer);
            Assert.Equal(10, state.CurrentRunningNode);
            
            state.PopNode();
            Assert.Equal(1, state.StackPointer);
            Assert.Equal(5, state.CurrentRunningNode);
            
            state.PopNode();
            Assert.Equal(0, state.StackPointer);
        }

        [Fact]
        public void BehaviorTreeState_StackOverflow_Handled()
        {
            var state = new BehaviorTreeState();
            
            // Push 8 times (max depth)
            for (int i = 0; i < 8; i++)
            {
                state.PushNode((ushort)i);
            }
            
            Assert.Equal(7, state.StackPointer); // Should be at max
            
            // Attempt overflow - should not crash, just not push
            state.PushNode(99);
            Assert.Equal(7, state.StackPointer); // Still at max
        }

        [Fact]
        public void BehaviorTreeState_CurrentRunningNode_CanBeSet()
        {
            var state = new BehaviorTreeState();
            state.CurrentRunningNode = 99;
            Assert.Equal(99, state.CurrentRunningNode);
            unsafe
            {
                Assert.Equal(99, state.NodeIndexStack[0]);
            }
        }

        [Fact]
        public void BehaviorTreeState_PopNode_AtRoot_DoesSafeCheck()
        {
            var state = new BehaviorTreeState();
            state.StackPointer = 0;
            state.PopNode(); // Should not crash or underflow
            Assert.Equal(0, state.StackPointer);
        }

        [Fact]
        public void AsyncToken_PackUnpack_RoundTrips()
        {
            var original = new AsyncToken(12345, 67);
            
            ulong packed = original.Pack();
            var unpacked = AsyncToken.Unpack(packed);
            
            Assert.Equal(original.RequestID, unpacked.RequestID);
            Assert.Equal(original.Version, unpacked.Version);
        }

        [Fact]
        public void AsyncToken_IsValid_CurrentVersion_ReturnsTrue()
        {
            var token = new AsyncToken(100, 5);
            Assert.True(token.IsValid(5));
        }

        [Fact]
        public void AsyncToken_IsValid_OldVersion_ReturnsFalse()
        {
            var token = new AsyncToken(100, 5);
            Assert.False(token.IsValid(6)); // Version incremented
        }

        [Fact]
        public void AsyncToken_IsValid_ZeroRequest_ReturnsFalse()
        {
            var token = new AsyncToken(0, 5);
            Assert.False(token.IsValid(5)); // RequestID=0 is invalid
        }

        [Fact]
        public void AsyncToken_Pack_PreservesAllBits()
        {
            var token = new AsyncToken(-1, uint.MaxValue);
            var packed = token.Pack();
            var unpacked = AsyncToken.Unpack(packed);
            
            Assert.Equal(-1, unpacked.RequestID);
            Assert.Equal(uint.MaxValue, unpacked.Version);
        }
    }
}
