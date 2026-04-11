using Xunit;
using Moq;
using ModuleHost.Core.Network.Interfaces;

namespace ModuleHost.Core.Tests.Network.Interfaces
{
    public class NetworkInterfacesTests
    {
        [Fact]
        public void INetworkIdAllocator_CanBeMocked()
        {
            // Arrange
            var mock = new Mock<INetworkIdAllocator>();
            mock.Setup(x => x.AllocateId()).Returns(123);

            // Act
            var id = mock.Object.AllocateId();

            // Assert
            Assert.Equal(123, id);
        }

        [Fact]
        public void INetworkTopology_CanBeMocked()
        {
            // Arrange
            var mock = new Mock<INetworkTopology>();
            mock.Setup(x => x.GetExpectedPeers(ReliableInitType.PhysicsServer))
                .Returns(new[] { 1, 2, 3 });

            // Act
            var peers = mock.Object.GetExpectedPeers(ReliableInitType.PhysicsServer);

            // Assert
            Assert.Contains(1, peers);
            Assert.Contains(2, peers);
            Assert.Contains(3, peers);
        }
    }
}
