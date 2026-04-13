using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Replication.Systems;
using Hrot.Common.Abstractions;
using Hrot.Core.Network;
using ModuleHost.Core;
using ModuleHost.Core.Abstractions;

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
    public ISimHostPathfindingTranslators CreateSimHostPathfindingTranslators() => new NullSimHostPathfindingTranslators();

    /// <inheritdoc/>
    public ISimHostPerceptionTranslators CreateSimHostPerceptionTranslators() => new NullSimHostPerceptionTranslators();

    /// <inheritdoc/>
    public IIgTranslators CreateIgTranslators() => new NullIgTranslators();

    /// <inheritdoc/>
    public IIgNetworkAdapter CreateIgNetworkAdapter(CycloneDDS.Runtime.DdsParticipant? participant, long nodeId = 0)
        => NullIgNetworkAdapter.Instance;

    // ---- null stubs -------------------------------------------------------

    private sealed class NullReplicationModule : IReplicationModule
    {
        private readonly GhostCreationSystem _ghostCreationSystem = new(new NetworkEntityMap());

        public string Name => "NullReplication";
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();
        public GhostCreationSystem GhostCreationSystem => _ghostCreationSystem;
        public bool DriveFromNetwork => false;
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
