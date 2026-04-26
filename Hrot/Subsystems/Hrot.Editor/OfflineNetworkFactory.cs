using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Toolkit.NetworkSpawning;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Systems;
using Hrot.Common.Abstractions;
using Hrot.Core.Network;
using Fdp.ModuleHost;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Editor;

/// <summary>
/// No-op INetworkFactory for the offline editor mode.
/// Returns null-stub implementations for all network services; no DDS is allocated.
/// </summary>
public sealed class OfflineNetworkFactory : INetworkFactory
{
    /// <inheritdoc/>
    public IReplicationModule CreateReplicationModule() => new NullReplicationModule();

    /// <inheritdoc/>
    public ICommandGateway CreateCommandGateway() => new NullCommandGateway();

    /// <inheritdoc/>
    public IExConEgressWriters CreateExConEgressWriters() => new NullExConEgressWriters();

    /// <inheritdoc/>
    public ITimeControlGateway CreateTimeControlGateway() => new NullTimeControlGateway();

    /// <inheritdoc/>
    public ISimHostMissionSender CreateSimHostMissionSender() => new NullSimHostMissionSender();

    /// <inheritdoc/>
    public ISimHostAuxiliaryTranslators CreateSimHostAuxiliaryTranslators() => new NullSimHostAuxiliaryTranslators();

    /// <inheritdoc/>
    public ISimHostPathfindingTranslators CreateSimHostPathfindingTranslators(CarKinem.Trajectory.TrajectoryPoolManager? trajectoryPool = null) => new NullSimHostPathfindingTranslators();

    /// <inheritdoc/>
    public ISimHostPerceptionTranslators CreateSimHostPerceptionTranslators(GhostCreationSystem? ghostCreationSystem = null) => new NullSimHostPerceptionTranslators();

    /// <inheritdoc/>
    public System.Collections.Generic.IReadOnlyList<Fdp.ModuleHost.Abstractions.IEcsModuleSystem> CreateSimHostAttributeUpdateSystems()
        => System.Array.Empty<Fdp.ModuleHost.Abstractions.IEcsModuleSystem>();

    /// <inheritdoc/>
    public IIgTranslators CreateIgTranslators() => new NullIgTranslators();

    /// <inheritdoc/>
    public IIgNetworkAdapter CreateIgNetworkAdapter(CycloneDDS.Runtime.DdsParticipant? participant, long nodeId = 0)
        => NullIgNetworkAdapter.Instance;

    /// <inheritdoc/>
    public System.Collections.Generic.IEnumerable<Fdp.Toolkit.DER.IIngressHandler> CreateExConIngressHandlers(
        CycloneDDS.Runtime.DdsParticipant?                participant,
        long                                              localNodeId,
        Fdp.Toolkit.DER.IDerRepo                         repo,
        System.Action<MapClickEventDto>                  onMapClick,
        System.Action<SelectionChangedEventDto>          onSelectionChanged,
        System.Action<EntityLifecycleAckDto>             onEntityLifecycleAck,
        System.Action<MapCommandAckDto>                  onMapCommandAck)
    {
        yield break;
    }

    /// <inheritdoc/>
    public INetworkFactory ConfigureForNode(
        Hrot.Common.Infrastructure.HrotNodeContext       context,
        Hrot.Common.NodeRole                             role,
        Fdp.Toolkit.Behavior.DoctrineRegistry?           doctrineRegistry = null)
        => this;

    /// <inheritdoc/>
    public INetworkFactory ConfigureForNode(
        CycloneDDS.Runtime.DdsParticipant? participant,
        int                                nodeId,
        Hrot.Common.NodeRole               role)
        => this;

    /// <inheritdoc/>
    public CycloneDDS.Runtime.DdsParticipant? Participant => null;

    /// <inheritdoc/>
    public System.Collections.Generic.IReadOnlyList<Fdp.Interfaces.IDescriptorTranslator> CreateIgEgressTranslators(
        CycloneDDS.Runtime.DdsParticipant participant,
        Fdp.Core.FdpEventBus bus,
        Fdp.Modules.Geographic.IGeographicTransform geoTransform,
        long nodeId)
        => System.Array.Empty<Fdp.Interfaces.IDescriptorTranslator>();

    /// <inheritdoc/>
    public ICgfEntityLifecycleAdapters? CreateCgfEntityLifecycleAdapters() => null;

    /// <inheritdoc/>
    public long WorldPosDescriptorId => 0;

    /// <inheritdoc/>
    public long NavigationStatusDescriptorId => 0;

    /// <inheritdoc/>
    public IOrchestrationTranslator CreateOrchestratorTranslators(FdpEventBus bus, int nodeId)
        => new NullOrchestrationTranslator();

    /// <inheritdoc/>
    public IDisposable CreateIdAllocatorServer()
        => new NullDisposable();

    /// <inheritdoc/>
    public INetworkIdAllocator CreateIdAllocator(string clientId, bool skipRoutingWait = false)
        => new SequentialIdAllocator();

    /// <inheritdoc/>
    public IMasterTimeTranslators CreateMasterTimeTranslators(FdpEventBus bus, int nodeId)
        => new NullMasterTimeTranslators();

    /// <inheritdoc/>
    public ISlaveOrchestrationTranslator CreateSlaveOrchestratorTranslators(FdpEventBus bus, int nodeId)
        => new NullSlaveOrchestrationTranslator();

    /// <inheritdoc/>
    public IOrchestrationObserver CreateOrchestrationObserver(FdpEventBus bus)
        => new NullOrchestrationObserver();

    // ---- null stubs -------------------------------------------------------

    private sealed class NullReplicationModule : IReplicationModule
    {
        private readonly GhostCreationSystem _ghostCreationSystem = new(new NetworkEntityMap());
        private readonly Fdp.ModuleHost.Scheduling.NetworkLifecycleSystemGroup _lifecycleGroup
            = new Fdp.ModuleHost.Scheduling.NetworkLifecycleSystemGroup();

        public string Name => "NullReplication";
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();
        public GhostCreationSystem GhostCreationSystem => _ghostCreationSystem;
        public bool DriveFromNetwork => false;
        public Fdp.ModuleHost.Scheduling.NetworkLifecycleSystemGroup NetworkLifecycleGroup => _lifecycleGroup;
        public void Tick(ISimulationView view, float deltaTime) { }
    }

    private sealed class NullCommandGateway : ICommandGateway
    {
        public Task<int> CreateEntityAsync(CreateEntityCommand cmd, CancellationToken ct = default)
            => Task.FromResult(0);
        public Task SendUpdateDescriptorAsync(UpdateEntityDescriptorCommand cmd, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task<MissionCommitResult> SendMissionControlRequestAsync(MissionControlCommand cmd, CancellationToken ct = default)
            => Task.FromResult(new MissionCommitResult { Success = false, ErrorMessage = "No gateway" });
        public void Dispose() { }
    }

    private sealed class NullExConEgressWriters : IExConEgressWriters
    {
        public void WriteMapConfig(MapConfigDto config) { }
        public void WriteDeleteEntity(int entityId) { }
        public void WriteCreateEntity(CreateEntityCommand cmd) { }
        public void WriteMapCommand(MapCommandDto cmd) { }
        public void PushContextActions(int mapGroupId, System.Collections.Generic.IReadOnlyList<int>? forSelection, string actionsJson) { }
        public void Dispose() { }
    }

    private sealed class NullTimeControlGateway : ITimeControlGateway
    {
        public void RequestPause() { }
        public void RequestResume() { }
        public void RequestStep() { }
        public void SetTimeScale(float scale) { }
    }

    private sealed class NullSimHostMissionSender : ISimHostMissionSender
    {
        public void SendNavigateToPoint(long entityNetworkId, Vector2 destination, float speed, float arrivalRadius) { }
        public void Dispose() { }
    }

    private sealed class NullSimHostAuxiliaryTranslators : ISimHostAuxiliaryTranslators
    {
        public void RegisterOn(ModuleHostKernel kernel) { }
        public void Dispose() { }
    }

    private sealed class NullSimHostPathfindingTranslators : ISimHostPathfindingTranslators
    {
        public void RegisterOn(ModuleHostKernel kernel) { }
        public void Dispose() { }
    }

    private sealed class NullSimHostPerceptionTranslators : ISimHostPerceptionTranslators
    {
        public void RegisterOn(ModuleHostKernel kernel) { }
        public void Dispose() { }
    }
}
