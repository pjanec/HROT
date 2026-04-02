using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Hrot.NED.Descriptors.Orchestration;
using Hrot.Orchestrator;
using CycloneDDS.Runtime;
using FDP.Toolkit.Orchestration;
using FDP.Toolkit.Time.Messages;
using ModuleHost.Core.Time;

namespace Hrot.ClusterRunner.Services;

/// <summary>
/// Network projection of cluster state — the CQRS read-model (CGF1-S0506).
///
/// <para>Constructs 8 DDS readers and maintains all published properties by draining
/// them on every <see cref="Update"/> call. No direct reference to
/// <see cref="ClusterMaster"/> or any local service. Thread-unsafe; must be updated
/// from a single thread.</para>
/// </summary>
public sealed class ClusterUiCache : IDisposable
{
    // ── Published state ────────────────────────────────────────────────────────
    public ClusterState    CurrentState           { get; private set; }
    public bool        IsBootstrapped         { get; private set; }
    public bool        HasInFlightTransaction  { get; private set; }

    public string[]    AvailableScenarios     { get; private set; } = Array.Empty<string>();
    public string[]    AvailableExercises        { get; private set; } = Array.Empty<string>();
    public string[]    ArchivedExercises         { get; private set; } = Array.Empty<string>();
    public string[]    UnarchivedLocalExercises  { get; private set; } = Array.Empty<string>();

    public double      MasterSimTime          =>
        _localTimeController != null
            ? _localTimeController.GetCurrentState().TotalTime
            : _networkSimTime;
    public long        MasterWallTicks        { get; private set; }
    public float       MasterTimeScale        { get; private set; } = 1f;
    public bool        IsPaused               { get; private set; }

    public IReadOnlyDictionary<int, NodeHeartbeat> ActiveNodes => _activeNodes;
    public IReadOnlyList<DistributedTransaction>   TxHistory   => _txHistory;

    /// <summary>
    /// Cluster states reachable from <see cref="CurrentState"/> in a single planning step.
    /// Recomputed each time <see cref="CurrentState"/> changes.
    /// </summary>
    public IReadOnlyList<ClusterState> ReachableTargets { get; private set; } = Array.Empty<ClusterState>();

    /// <summary>
    /// The in-flight transaction with the most recent <see cref="DistributedTransaction.TransactionId"/>,
    /// or <c>null</c> when no transaction is in flight.
    /// </summary>
    public DistributedTransaction? ActiveTransaction =>
        HasInFlightTransaction && _txHistory.Count > 0 ? _txHistory[0] : null;

    /// <summary>Currently active episode IDs as snooped from ManageEpisode NodeOpCommands.</summary>
    public IReadOnlySet<Guid> ActiveEpisodes => _activeEpisodes;

    // ── DDS Readers ────────────────────────────────────────────────────────────
    private readonly DdsReader<SystemStateTopic>      _stateReader;
    private readonly DdsReader<AssetInventoryTopic>   _inventoryReader;
    private readonly DdsReader<NodeHeartbeat>         _heartbeatReader;
    private readonly DdsReader<ClusterOpStatus>           _sysOpStatusReader;
    private readonly DdsReader<NodeOpCommand>         _nodeOpCmdReader;
    private readonly DdsReader<NodeOpStatus>          _nodeOpStatusReader;
    private readonly DdsReader<SwitchTimeModeWireDto> _timeModeReader;

    // ── Internal state ─────────────────────────────────────────────────────────
    private readonly Dictionary<int, NodeHeartbeat>           _activeNodes  = new();
    private readonly Dictionary<int, long>                    _nodeReceivedMs = new();
    private readonly List<DistributedTransaction>             _txHistory    = new();
    private readonly Dictionary<Guid, DistributedTransaction> _inFlight     = new();
    private readonly HashSet<Guid>                            _activeEpisodes = new();
    private readonly ClusterMasterPlanner                       _planner = new ClusterMasterPlanner(HrotStateGraph.Build());
    private readonly ITimeController?                         _localTimeController;
    private double                                            _networkSimTime;

    public ClusterUiCache(DdsParticipant participant, ITimeController? localTimeController = null)
    {
        _localTimeController = localTimeController;
        _stateReader        = new DdsReader<SystemStateTopic>(participant);
        _inventoryReader    = new DdsReader<AssetInventoryTopic>(participant);
        _heartbeatReader    = new DdsReader<NodeHeartbeat>(participant);
        _sysOpStatusReader  = new DdsReader<ClusterOpStatus>(participant);
        _nodeOpCmdReader    = new DdsReader<NodeOpCommand>(participant);
        _nodeOpStatusReader = new DdsReader<NodeOpStatus>(participant);
        _timeModeReader     = new DdsReader<SwitchTimeModeWireDto>(participant);

        // even before the first network message arrives, the UI cache already knows what transitions
        // are legal from the default Idle state, and buttons like LoadingEdit, LoadingLive, and LoadingReplay
        // will immediately appear
        ReachableTargets = _planner.GetReachableTargets(CurrentState);
    }

    /// <summary>Drains all readers and updates the published state. Call once per frame.</summary>
    public void Update()
    {
        DrainSystemState();
        DrainInventory();
        DrainHeartbeats();
        DrainTimeMode();
        Process2PcNetworkTraffic();
        DrainSysOpStatus();
    }

    /// <summary>
    /// Returns the UTC-milliseconds timestamp at which the last heartbeat for
    /// <paramref name="nodeId"/> was received, or 0 if not tracked.
    /// </summary>
    public long GetNodeLastSeenMs(int nodeId) =>
        _nodeReceivedMs.TryGetValue(nodeId, out var ms) ? ms : 0L;

    public void Dispose()
    {
        _stateReader.Dispose();
        _inventoryReader.Dispose();
        _heartbeatReader.Dispose();
        _sysOpStatusReader.Dispose();
        _nodeOpCmdReader.Dispose();
        _nodeOpStatusReader.Dispose();
        _timeModeReader.Dispose();
    }

    // ── Private drain methods ──────────────────────────────────────────────────

    private void DrainSystemState()
    {
        using var l = _stateReader.Take();
        foreach (var s in l)
        {
            if (!s.IsValid) continue;
            var prev = CurrentState;
            CurrentState   = s.Data.CurrentState;
            IsBootstrapped = s.Data.CurrentState != ClusterState.Degraded;
            if (CurrentState != prev)
                ReachableTargets = _planner.GetReachableTargets(CurrentState);
        }
    }

    private void DrainInventory()
    {
        using var l = _inventoryReader.Take();
        foreach (var s in l)
        {
            if (!s.IsValid) continue;
            AvailableScenarios    = DeserializeStringArray(s.Data.LocalScenariosJson);
            AvailableExercises       = DeserializeStringArray(s.Data.LocalExercisesJson);
            ArchivedExercises        = DeserializeStringArray(s.Data.ArchivedExercisesJson);
            UnarchivedLocalExercises = DeserializeStringArray(s.Data.UnarchivedLocalExercisesJson);
        }
    }

    private void DrainHeartbeats()
    {
        using var l = _heartbeatReader.Take();
        foreach (var s in l)
        {
            if (!s.IsValid) continue;
            _activeNodes[s.Data.NodeId] = s.Data;
            _nodeReceivedMs[s.Data.NodeId] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }

    private void DrainTimeMode()
    {
        using var l = _timeModeReader.Take();
        foreach (var s in l)
        {
            if (!s.IsValid) continue;
            var isDeterministic = (TimeMode)s.Data.TargetModeInt == TimeMode.Deterministic;
            IsPaused = isDeterministic;
            if (s.Data.TimeScale > 0f)
                MasterTimeScale = s.Data.TimeScale;
            if (s.Data.BarrierWallTicks > 0)
                MasterWallTicks = s.Data.BarrierWallTicks;
            if (!isDeterministic && s.Data.SimTimeSnapshot > 0.0)
                _networkSimTime = s.Data.SimTimeSnapshot;
        }
    }

    private void Process2PcNetworkTraffic()
    {
        // Insert new transactions when PrepareState NodeOpCommand arrives
        using var cmdList = _nodeOpCmdReader.Take();
        foreach (var s in cmdList)
        {
            if (!s.IsValid) continue;

            // Sniff ManageEpisode Start/Stop to maintain active episodes set
            if (s.Data.Operation == NodeOpType.StartEpisode)
            {
                var episodeId = ParseGuidFromPayload(s.Data.PayloadJson, "EpisodeId");
                if (episodeId != Guid.Empty) _activeEpisodes.Add(episodeId);
            }
            else if (s.Data.Operation == NodeOpType.StopEpisode || s.Data.Operation == NodeOpType.ForgetEpisode)
            {
                var episodeId = ParseGuidFromPayload(s.Data.PayloadJson, "EpisodeId");
                if (episodeId != Guid.Empty) _activeEpisodes.Remove(episodeId);
            }

            //if (s.Data.Operation != NodeOpType.PrepareState) continue;

            var txId = s.Data.TransactionId;
            if (!_inFlight.ContainsKey(txId))
            {
                // Parse target Cluster state from payload JSON
                var targetState = ClusterState.Idle;
                try
                {
                    using var doc = JsonDocument.Parse(s.Data.PayloadJson ?? "{}");
                    if (doc.RootElement.TryGetProperty("TargetState", out var el))
                        targetState = (ClusterState)el.GetInt32();
                }
                catch { }

                var tx = new DistributedTransaction
                {
                    TransactionId  = txId,
                    TargetDsmState = targetState,
                };
                _inFlight[txId] = tx;
                _txHistory.Insert(0, tx);
                while (_txHistory.Count > 10) _txHistory.RemoveAt(_txHistory.Count - 1);
            }
            HasInFlightTransaction = _inFlight.Count > 0;
        }

        // Append NodeOpStatus ACKs to in-flight transactions
        using var statusList = _nodeOpStatusReader.Take();
        foreach (var s in statusList)
        {
            if (!s.IsValid) continue;
            if (_inFlight.TryGetValue(s.Data.TransactionId, out var tx))
                tx.NodeResponses[s.Data.NodeId] = s.Data.ResultJson ?? string.Empty;
        }
    }

    private void DrainSysOpStatus()
    {
        using var l = _sysOpStatusReader.Take();
        foreach (var s in l)
        {
            if (!s.IsValid) continue;
            // Skip InProgress (non-terminal) status codes
            if (s.Data.StatusCode == OrchestrationStatusCode.InProgress) continue;

            bool success = s.Data.StatusCode == OrchestrationStatusCode.Success;

            // Try an exact match (works when SysOpStatus.RequestId == NodeOpCommand.TransactionId,
            // e.g. in unit tests or future protocol alignment).
            if (_inFlight.Remove(s.Data.RequestId, out var matchedTx))
            {
                matchedTx.Completed = success;
                matchedTx.IsAborted = !success;
            }
            else if (_inFlight.Count > 0)
            {
                // Fallback: in production ClusterMaster uses different GUIDs for
                // TransactionId vs RequestId.  A terminal SysOpStatus means the
                // operation is done — close all in-flight transactions.
                foreach (var tx in _inFlight.Values)
                {
                    tx.Completed = success;
                    tx.IsAborted = !success;
                }
                _inFlight.Clear();
            }
            HasInFlightTransaction = _inFlight.Count > 0;
        }
    }

    private static string[] DeserializeStringArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try { return JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>(); }
        catch { return Array.Empty<string>(); }
    }

    private static Guid ParseGuidFromPayload(string? json, string propertyName)
    {
        if (string.IsNullOrEmpty(json)) return Guid.Empty;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty(propertyName, out var el) &&
                Guid.TryParse(el.GetString(), out var id))
                return id;
        }
        catch { }
        return Guid.Empty;
    }
}
