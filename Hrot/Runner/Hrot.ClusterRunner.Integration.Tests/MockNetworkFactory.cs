using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Systems;
using Hrot.Common.Abstractions;
using Hrot.Core.Network;
using Fdp.ModuleHost;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// All-null-stub INetworkFactory for headless integration tests that do not require a live
/// DDS domain. Mirrors the structure of <see cref="Hrot.Editor.OfflineNetworkFactory"/>,
/// but lives in the test assembly so it can be used without a reference to Hrot.Editor.
/// </summary>
internal sealed class MockNetworkFactory : INetworkFactory
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
    public ISimHostPathfindingTranslators CreateSimHostPathfindingTranslators() => new NullSimHostPathfindingTranslators();

    /// <inheritdoc/>
    public ISimHostPerceptionTranslators CreateSimHostPerceptionTranslators() => new NullSimHostPerceptionTranslators();

    /// <inheritdoc/>
    public System.Collections.Generic.IReadOnlyList<Fdp.Core.ComponentSystem> CreateSimHostAttributeUpdateSystems()
        => System.Array.Empty<Fdp.Core.ComponentSystem>();

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
        Action<MapClickEventDto>                         onMapClick,
        Action<SelectionChangedEventDto>                 onSelectionChanged,
        Action<EntityLifecycleAckDto>                    onEntityLifecycleAck,
        Action<MapCommandAckDto>                         onMapCommandAck)
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
    public long WorldPosDescriptorId => 0;
    public ICgfEntityLifecycleAdapters? CreateCgfEntityLifecycleAdapters() => null;
    public System.Collections.Generic.IReadOnlyList<Fdp.Interfaces.IDescriptorTranslator> CreateIgEgressTranslators(
        CycloneDDS.Runtime.DdsParticipant participant,
        Fdp.Core.FdpEventBus bus,
        Fdp.Modules.Geographic.IGeographicTransform geoTransform,
        long nodeId)
        => System.Array.Empty<Fdp.Interfaces.IDescriptorTranslator>();

    /// <inheritdoc/>
    public IOrchestrationTranslator CreateOrchestratorTranslators(Fdp.Core.FdpEventBus bus, int nodeId)
        => new NullOrchestrationTranslator();

    /// <inheritdoc/>
    public IDisposable CreateIdAllocatorServer()
        => new NullDisposable();

    /// <inheritdoc/>
    public IMasterTimeTranslators CreateMasterTimeTranslators(Fdp.Core.FdpEventBus bus, int nodeId)
        => new NullMasterTimeTranslators();

    /// <inheritdoc/>
    public ISlaveOrchestrationTranslator CreateSlaveOrchestratorTranslators(Fdp.Core.FdpEventBus bus, int nodeId)
        => new NullSlaveOrchestrationTranslator();

    /// <inheritdoc/>
    public IOrchestrationObserver CreateOrchestrationObserver(Fdp.Core.FdpEventBus bus)
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
