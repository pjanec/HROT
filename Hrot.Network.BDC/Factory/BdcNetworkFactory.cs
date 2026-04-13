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

        /// <inheritdoc/>
        public ITimeControlGateway CreateTimeControlGateway()
            => new BdcNullTimeControlGateway();

        /// <inheritdoc/>
        public ISimHostMissionSender CreateSimHostMissionSender()
            => new BdcNullSimHostMissionSender();

        /// <inheritdoc/>
        public ISimHostAuxiliaryTranslators CreateSimHostAuxiliaryTranslators()
            => new BdcNullSimHostAuxiliaryTranslators();

        /// <inheritdoc/>
        public ISimHostPathfindingTranslators CreateSimHostPathfindingTranslators()
            => new BdcNullSimHostPathfindingTranslators();

        /// <inheritdoc/>
        public ISimHostPerceptionTranslators CreateSimHostPerceptionTranslators()
            => new BdcNullSimHostPerceptionTranslators();

        /// <inheritdoc/>
        public IIgTranslators CreateIgTranslators()
            => new NullIgTranslators();

        /// <inheritdoc/>
        public IIgNetworkAdapter CreateIgNetworkAdapter(CycloneDDS.Runtime.DdsParticipant? participant, long nodeId = 0)
            => Hrot.Core.Network.NullIgNetworkAdapter.Instance;

        /// <inheritdoc/>
        public System.Collections.Generic.IEnumerable<FDP.Toolkit.DER.IIngressHandler> CreateExConIngressHandlers(
            CycloneDDS.Runtime.DdsParticipant?                   participant,
            long                                                  localNodeId,
            FDP.Toolkit.DER.IDerRepo                             repo,
            Action<Hrot.Core.Network.MapClickEventDto>           onMapClick,
            Action<Hrot.Core.Network.SelectionChangedEventDto>   onSelectionChanged,
            Action<Hrot.Core.Network.EntityLifecycleAckDto>      onEntityLifecycleAck,
            Action<Hrot.Core.Network.MapCommandAckDto>           onMapCommandAck)
        {
            yield break; // BDC does not support ExCon ingress handlers yet.
        }

        /// <inheritdoc/>
        public Hrot.Core.Network.INetworkFactory ConfigureForNode(
            Hrot.Common.Infrastructure.HrotNodeContext context,
            Hrot.Common.NodeRole                       role,
            FDP.Toolkit.Behavior.DoctrineRegistry?     doctrineRegistry = null)
        {
            return new BdcNetworkFactory(
                context.Participant,
                context.EntityMap,
                (Fdp.Modules.Geographic.IGeographicTransform)(context.GeoTransform ?? Hrot.Map.Common.HrotEnvironment.CreateGeoTransform()),
                context.EventBus,
                context.NodeId,
                role);
        }

        /// <inheritdoc/>
        public Hrot.Core.Network.INetworkFactory ConfigureForNode(
            CycloneDDS.Runtime.DdsParticipant? participant,
            int                                nodeId,
            Hrot.Common.NodeRole               role)
        {
            return new BdcNetworkFactory(participant, _entityMap, _geoTransform, _eventBus, nodeId, role);
        }

        /// <inheritdoc/>
        public long WorldPosDescriptorId => 0;
    }

    internal sealed class BdcNullSimHostMissionSender : ISimHostMissionSender
    {
        public void SendNavigateToPoint(long id, System.Numerics.Vector2 dest, float speed, float radius) { }
        public void Dispose() { }
    }

    internal sealed class BdcNullSimHostAuxiliaryTranslators : ISimHostAuxiliaryTranslators
    {
        public void RegisterOn(ModuleHost.Core.ModuleHostKernel kernel) { }
        public void Dispose() { }
    }

    internal sealed class BdcNullSimHostPathfindingTranslators : ISimHostPathfindingTranslators
    {
        public void RegisterOn(ModuleHost.Core.ModuleHostKernel kernel) { }
        public void Dispose() { }
    }

    internal sealed class BdcNullSimHostPerceptionTranslators : ISimHostPerceptionTranslators
    {
        public void RegisterOn(ModuleHost.Core.ModuleHostKernel kernel) { }
        public void Dispose() { }
    }

    internal sealed class BdcNullCommandGateway : ICommandGateway
    {
        public Task<int> CreateEntityAsync(CreateEntityCommand cmd, CancellationToken ct = default)
            => Task.FromResult(0);
        public Task SendUpdateDescriptorAsync(UpdateEntityDescriptorCommand cmd, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task<MissionCommitResult> SendMissionControlRequestAsync(MissionControlCommand cmd, CancellationToken ct = default)
            => Task.FromResult(new MissionCommitResult { Success = false, ErrorMessage = "No gateway" });
        public void Dispose() { }
    }

    internal sealed class BdcNullExConEgressWriters : IExConEgressWriters
    {
        public void WriteMapConfig(MapConfigDto config) { }
        public void WriteDeleteEntity(int entityId) { }
        public void WriteCreateEntity(CreateEntityCommand cmd) { }
        public void WriteMapCommand(MapCommandDto cmd) { }
        public void PushContextActions(int mapGroupId, System.Collections.Generic.IReadOnlyList<int>? forSelection, string actionsJson) { }
        public void Dispose() { }
    }

    internal sealed class BdcNullTimeControlGateway : ITimeControlGateway
    {
        public void RequestPause() { }
        public void RequestResume() { }
        public void RequestStep() { }
        public void SetTimeScale(float scale) { }
    }
}
