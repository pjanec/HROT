using Hrot.BDC.Factory;
using Hrot.Common;
using Hrot.Common.Abstractions;
using Hrot.Core.Network;
using Fdp.Toolkit.Replication.Services;
using Fdp.Modules.Geographic;
using Fdp.Toolkit.Replication.Systems;
using Fdp.Core;
using Xunit;
using NSubstitute;

namespace Hrot.Network.BDC.Tests
{
    public class BdcNetworkFactoryTests
    {
        private static BdcNetworkFactory CreateFactory()
        {
            var entityMap    = new NetworkEntityMap();
            var geoTransform = Substitute.For<IGeographicTransform>();
            var eventBus     = new FdpEventBus();
            return new BdcNetworkFactory(
                participant:  null,      // headless — no real DDS
                entityMap:    entityMap,
                geoTransform: geoTransform,
                eventBus:     eventBus,
                localNodeId:  1,
                role:         NodeRole.Brain | NodeRole.MuscleGround | NodeRole.ImageGenerator);
        }

        [Fact]
        public void BdcNetworkFactory_CreatesReplicationModule_WhenParticipantIsNull()
        {
            var factory = CreateFactory();
            var module = factory.CreateReplicationModule();
            Assert.NotNull(module);
            Assert.IsAssignableFrom<IReplicationModule>(module);
        }

        [Fact]
        public void BdcNetworkFactory_ReplicationModuleName_IsBdcReplication()
        {
            var factory = CreateFactory();
            var module = factory.CreateReplicationModule();
            Assert.Equal("BdcReplication", module.Name);
        }

        [Fact]
        public void BdcNetworkFactory_GhostCreationSystem_IsNotNull()
        {
            var factory = CreateFactory();
            var module  = factory.CreateReplicationModule();
            Assert.NotNull(module.GhostCreationSystem);
        }

        [Fact]
        public void BdcNetworkFactory_CreateCommandGateway_ReturnsNonNull()
        {
            var factory  = CreateFactory();
            var gateway  = factory.CreateCommandGateway();
            Assert.NotNull(gateway);
            // No-op gateway must be disposable without throwing
            gateway.Dispose();
        }

        [Fact]
        public void BdcNetworkFactory_CreateExConEgressWriters_ReturnsNonNull()
        {
            var factory  = CreateFactory();
            var writers  = factory.CreateExConEgressWriters();
            Assert.NotNull(writers);
            writers.Dispose();
        }

        [Fact]
        public void BdcNetworkFactory_DriveFromNetwork_FalseForAllInOneRole()
        {
            // Brain | MuscleGround has both, so DriveFromNetwork = false
            var factory = CreateFactory(); // role = Brain | MuscleGround | ImageGenerator
            var module  = factory.CreateReplicationModule();
            Assert.False(module.DriveFromNetwork);
        }

        [Fact]
        public void BdcNetworkFactory_DriveFromNetwork_TrueForIgOnlyRole()
        {
            var entityMap    = new NetworkEntityMap();
            var geoTransform = Substitute.For<IGeographicTransform>();
            var eventBus     = new FdpEventBus();
            var factory = new BdcNetworkFactory(
                null, entityMap, geoTransform, eventBus, 1, NodeRole.ImageGenerator);
            var module = factory.CreateReplicationModule();
            Assert.True(module.DriveFromNetwork);
        }

        [Fact]
        public void BdcNetworkFactory_SatisfiesINetworkFactoryContract()
        {
            // This test verifies the compile-time contract: assigning the concrete factory
            // to the interface works without referencing Hrot.Network.NED.
            INetworkFactory factory = CreateFactory();
            Assert.NotNull(factory.CreateReplicationModule());
        }
    }
}
