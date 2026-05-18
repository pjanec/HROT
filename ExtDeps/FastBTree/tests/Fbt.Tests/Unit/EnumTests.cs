using System.Runtime.CompilerServices;
using Xunit;
using Fbt;

namespace Fbt.Tests.Unit
{
    public class EnumTests
    {
        [Fact]
        public void NodeType_ShouldBeByte()
        {
            Assert.Equal(sizeof(byte), Unsafe.SizeOf<NodeType>());
        }

        [Fact]
        public void NodeStatus_ShouldBeByte()
        {
            Assert.Equal(sizeof(byte), Unsafe.SizeOf<NodeStatus>());
        }

        [Fact]
        public void NodeStatus_FailureIsZero()
        {
            Assert.Equal(0, (int)NodeStatus.Failure);
        }
    }
}
