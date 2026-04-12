using CycloneDDS.Runtime;
using Fdp.Kernel;
using Fdp.Modules.Geographic;
using FDP.Toolkit.Replication.Services;
using Hrot.Common;
using Hrot.Core.Network;

namespace Hrot.BDC.Factory
{
    /// <summary>
    /// Implements <see cref="INetworkFactory"/> using BDC (Battlefield Data Channel)
    /// DDS protocols for simulation data exchange.
    /// </summary>
    public sealed class BdcNetworkFactory : INetworkFactory
    {
        private readonly DdsParticipant?      _participant;
        private readonly NetworkEntityMap     _entityMap;
        private readonly IGeographicTransform _geoTransform;
        private readonly FdpEventBus          _eventBus;
        private readonly long                 _localNodeId;
        private readonly NodeRole             _role;

        public BdcNetworkFactory(
            DdsParticipant?      participant,
            NetworkEntityMap     entityMap,
            IGeographicTransform geoTransform,
            FdpEventBus          eventBus,
            long                 localNodeId,
            NodeRole             role)
        {
            _participant  = participant;
            _entityMap    = entityMap;
            _geoTransform = geoTransform;
            _eventBus     = eventBus;
            _localNodeId  = localNodeId;
            _role         = role;
        }

        /// <inheritdoc/>
        public Hrot.Common.Abstractions.IReplicationModule CreateReplicationModule()
            => new Hrot.BDC.Replication.BdcReplicationModule(
                _participant, _role, _entityMap, _geoTransform, _eventBus, _localNodeId);

        /// <inheritdoc/>
        public ICommandGateway CreateCommandGateway()
            => new BdcNullCommandGateway();

        /// <inheritdoc/>
        public IExConEgressWriters CreateExConEgressWriters()
            => new BdcNullExConEgressWriters();
    }

    internal sealed class BdcNullCommandGateway : ICommandGateway
    {
        public Task<int> CreateEntityAsync(CreateEntityCommand cmd, CancellationToken ct = default)
            => Task.FromResult(0);
        public Task SendUpdateDescriptorAsync(UpdateEntityDescriptorCommand cmd, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task SendMissionControlRequestAsync(MissionControlCommand cmd, CancellationToken ct = default)
            => Task.CompletedTask;
        public void Dispose() { }
    }

    internal sealed class BdcNullExConEgressWriters : IExConEgressWriters
    {
        public void WriteMapConfig(MapConfigDto config) { }
        public void WriteDeleteEntity(int entityId) { }
        public void WriteCreateEntity(CreateEntityCommand cmd) { }
        public void WriteMapCommand(MapCommandDto cmd) { }
        public void Dispose() { }
    }
}
