using FDP.Toolkit.Replication.Components;
using Xunit;

namespace FDP.Toolkit.Replication.Tests
{
    public class ComponentTests
    {
        [Fact]
        public void NetworkIdentity_StoresValueCorrectly()
        {
            var id = new NetworkIdentity(12345);
            Assert.Equal(12345, id.Value);
            Assert.Equal("NetID:12345", id.ToString());
        }

        [Theory]
        [InlineData(1, 1, true)]
        [InlineData(1, 2, false)]
        [InlineData(2, 1, false)]
        public void NetworkAuthority_HasAuthority_LogicCheck(int owner, int local, bool expected)
        {
            var rpc = new NetworkAuthority(owner, local);
            Assert.Equal(expected, rpc.HasAuthority);
        }
    }
}