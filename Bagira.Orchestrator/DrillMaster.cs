using System.Threading;
using Bagira.BDC.SSTD.Orchestration;
using CycloneDDS.Runtime;
using FDP.Kernel.Logging;
using ModuleHost.Network.Cyclone.Services;

namespace Bagira.Orchestrator;

/// <summary>
/// Orchestrator control-plane host: system state, node heartbeats, DDS network ID allocation server.
/// </summary>
public sealed class DrillMaster : IDisposable
{
    public const double DefaultHeartbeatPruneSeconds = 5.0;

    private readonly DdsWriter<SystemStateTopic> _systemStateWriter;
    private readonly DdsReader<NodeHeartbeat> _heartbeatReader;
    private readonly Dictionary<int, NodeHealthProfile> _profiles = new();
    private readonly NodeRoster _roster = new();
    private DdsIdAllocatorServer? _idAllocatorServer;
    private CancellationTokenSource? _idServerCts;
    private Thread? _idServerThread;
    private bool _disposed;

    public NodeRoster NodeRoster => _roster;

    public DrillMaster(DdsParticipant participant)
    {
        _heartbeatReader = new DdsReader<NodeHeartbeat>(participant);
        _systemStateWriter = new DdsWriter<SystemStateTopic>(participant);
        PublishStandby();

        _idAllocatorServer = new DdsIdAllocatorServer(participant);
        _idServerCts = new CancellationTokenSource();
        _idServerThread = new Thread(() => RunIdServerLoop(_idServerCts.Token))
        {
            IsBackground = true,
            Name = "Orchestrator-IdAllocServer"
        };
        _idServerThread.Start();
    }

    private void PublishStandby()
    {
        _systemStateWriter.Write(new SystemStateTopic
        {
            CurrentState = DSMState.Standby,
            DrillId = Guid.Empty,
            StateStartWallTicks = 0,
            TransactionEpoch = 0
        });
    }

    public void Tick()
    {
        IngestHeartbeats();
        var now = UtcNowSeconds();
        _roster.PruneStale(now, DefaultHeartbeatPruneSeconds);
        _idAllocatorServer?.ProcessRequests();
    }

    private void IngestHeartbeats()
    {
        using var scope = _heartbeatReader.Take();
        foreach (var sample in scope)
        {
            if (!sample.IsValid) continue;
            var hb = sample.Data;
            var now = UtcNowSeconds();
            var profile = new NodeHealthProfile
            {
                NodeId = hb.NodeId,
                SubsystemName = hb.SubsystemName ?? string.Empty,
                LocalDsmState = hb.LocalDsmState,
                LastHeartbeatUtcSeconds = now
            };
            _profiles[hb.NodeId] = profile;
            _roster.Upsert(profile);
        }
    }

    private static double UtcNowSeconds() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

    private void RunIdServerLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            _idAllocatorServer?.ProcessRequests();
            Thread.Sleep(1);
        }
        FdpLog<DrillMaster>.Info("[Orchestrator] IdAllocatorServer loop exited.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _idServerCts?.Cancel();
        _idServerThread?.Join(TimeSpan.FromSeconds(2));
        _idServerCts?.Dispose();
        _idServerCts = null;
        _idServerThread = null;
        _idAllocatorServer?.Dispose();
        _idAllocatorServer = null;
        _systemStateWriter.Dispose();
        _heartbeatReader.Dispose();
    }
}
