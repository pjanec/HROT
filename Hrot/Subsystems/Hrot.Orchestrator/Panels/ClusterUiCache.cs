using System;
using System.Collections.Generic;
using System.Text.Json;
using Hrot.NED.Descriptors.Orchestration;
using Hrot.Network.Orchestration;
using Hrot.Orchestrator;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using Fdp.Toolkit.Time;
using Fdp.Toolkit.Time.Messages;
using Fdp.ModuleHost.Time;
using ClusterState  = Hrot.NED.Descriptors.Orchestration.ClusterState;
using FdpNodeOpType = Fdp.Toolkit.Orchestration.NodeOpType;

namespace Hrot.Orchestrator.Panels;

/// <summary>
/// Network projection of cluster state — the CQRS read-model (CGF1-S0506).
///
/// <para>Subscribes to an <see cref="FdpEventBus"/> and maintains all published properties
/// by draining events on every <see cref="Update"/> call. No direct reference to
/// <see cref="ClusterMaster"/> or any DDS type. Thread-unsafe; must be updated
/// from a single thread.</para>
/// </summary>
public sealed class ClusterUiCache : IDisposable
{
    private static readonly JsonSerializerOptions PayloadJsonOptions = new()
    {
        IncludeFields = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    // ── Published state ────────────────────────────────────────────────────────
    public ClusterState    CurrentState           { get; private set; }
    public Guid        ActiveExerciseId       { get; private set; }
    public bool        IsBootstrapped         { get; private set; }
    public bool        HasInFlightTransaction  { get; private set; }

    public string[]    AvailableScenarios     { get; private set; } = Array.Empty<string>();
    public ExerciseInventoryItem[] AvailableExercises        { get; private set; } = Array.Empty<ExerciseInventoryItem>();
    public ExerciseInventoryItem[] ArchivedExercises         { get; private set; } = Array.Empty<ExerciseInventoryItem>();
    public ExerciseInventoryItem[] UnarchivedLocalExercises  { get; private set; } = Array.Empty<ExerciseInventoryItem>();

    public double      MasterSimTime          =>
        _localTimeController != null
            ? _localTimeController.GetCurrentState().TotalTime
            : _clusterTime.ResumeSimTime;
    public long        MasterWallTicks        => _clusterTime.BarrierWallTicks;
    public float       MasterTimeScale        => _clusterTime.TimeScale;

    /// <summary>
    /// The cluster's last pause DECISION — see <see cref="ClusterTimeObservation.PauseRequested"/>,
    /// which this forwards. `T7` measured the distinction that the old name hides: on the
    /// Orchestrator this turns over with the master's frozen sim time, and on ExCon it runs ahead of
    /// the local slave clock by the barrier window. It is not "my clock is stopped" — for that, ask
    /// <c>ISimClock.IsAdvancing</c>.
    /// </summary>
    public bool        IsPaused               => _clusterTime.PauseRequested;

    public IReadOnlyDictionary<int, NodeHeartbeat> ActiveNodes => _activeNodes;
    public IReadOnlyList<DistributedTransaction>   TxHistory   => _txHistory;

    /// <summary>
    /// The stripped file manifest from the most recent successful diagnostic dump.
    /// Contains only <see cref="FileManifestEntry.RelativeDest"/> (SourceUnc is empty).
    /// Remains <see cref="Array.Empty{T}"/> until the first successful dump completes.
    /// </summary>
    public IReadOnlyList<FileManifestEntry> LastDiagnosticManifest { get; private set; }
        = Array.Empty<FileManifestEntry>();

    /// <summary>
    /// Duration in seconds of the currently loaded replay, aggregated as the maximum
    /// value reported by all participating nodes in their <c>PrepareReplay</c> response.
    /// Defaults to 3600 until a successful <c>LoadingReplay</c> transition reports a
    /// real duration via <see cref="Fdp.Toolkit.Orchestration.Handlers.ReplayPrepareResult"/>.
    /// </summary>
    public float ReplayDuration { get; private set; } = 3600f;

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

    /// <summary>Currently active episode IDs as snooped from ManageEpisode ExecuteNodeOpIntents.</summary>
    public IReadOnlySet<Guid> ActiveEpisodes => _activeEpisodes;

    // ── FdpEventBus ───────────────────────────────────────────────────────────
    private readonly FdpEventBus _bus;

    // ── Internal state ─────────────────────────────────────────────────────────
    private readonly Dictionary<int, NodeHeartbeat>           _activeNodes  = new();
    private readonly Dictionary<int, long>                    _nodeReceivedMs = new();
    private readonly List<DistributedTransaction>             _txHistory    = new();
    private readonly Dictionary<Guid, DistributedTransaction> _inFlight     = new();
    private readonly HashSet<Guid>                            _activeEpisodes = new();
    private readonly ClusterMasterPlanner                       _planner = new ClusterMasterPlanner(HrotStateGraph.Build());
    private readonly ITimeController?                         _localTimeController;

    // `T7`: the SwitchTimeModeEvent fold, shared with ClusterTimeTransportAdapter. The two classes
    // are different roles on different nodes — no node constructs both — but they folded the same
    // event with the same four lines, and that half was genuine duplicate code.
    private readonly ClusterTimeObservation                   _clusterTime  = new();

    public ClusterUiCache(FdpEventBus bus, ITimeController? localTimeController = null)
    {
        _bus                 = bus ?? throw new ArgumentNullException(nameof(bus));
        _localTimeController = localTimeController;

        // even before the first network message arrives, the UI cache already knows what transitions
        // are legal from the default Idle state, and buttons like LoadingEdit, LoadingLive, and LoadingReplay
        // will immediately appear
        ReachableTargets = _planner.GetReachableTargets(CurrentState);
    }

    /// <summary>Drains all bus events and updates the published state. Call once per frame.</summary>
    public void Update()
    {
        DrainClusterState();
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

    public void Dispose() { /* FdpEventBus is owned by the caller; nothing to dispose here. */ }

    // ── Private drain methods ──────────────────────────────────────────────────

    private void DrainClusterState()
    {
        foreach (var ev in _bus.ReadManaged<ClusterStateUpdateEvent>())
        {
            var prev = CurrentState;
            CurrentState   = (ClusterState)(int)ev.CurrentState;
            ActiveExerciseId = ev.ExerciseId;
            IsBootstrapped = CurrentState != ClusterState.Degraded;
            if (CurrentState != prev)
                ReachableTargets = _planner.GetReachableTargets(CurrentState);
        }
    }

    private void DrainInventory()
    {
        foreach (var ev in _bus.ReadManaged<AssetInventoryUpdateEvent>())
        {
            AvailableScenarios           = ev.LocalScenarios           ?? Array.Empty<string>();
            AvailableExercises           = ev.LocalExercises           ?? Array.Empty<ExerciseInventoryItem>();
            ArchivedExercises            = ev.ArchivedExercises        ?? Array.Empty<ExerciseInventoryItem>();
            UnarchivedLocalExercises     = ev.UnarchivedLocalExercises ?? Array.Empty<ExerciseInventoryItem>();
        }
    }

    private void DrainHeartbeats()
    {
        foreach (var ev in _bus.ReadManaged<NodeHeartbeatEvent>())
        {
            _activeNodes[ev.NodeId] = new NodeHeartbeat
            {
                NodeId            = ev.NodeId,
                SubsystemName     = ev.SubsystemName ?? string.Empty,
                LocalClusterState = (ClusterState)ev.LocalStateId,
                WallTicksUtc      = ev.WallTicksUtc,
            };
            _nodeReceivedMs[ev.NodeId] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }

    private void DrainTimeMode()
    {
        foreach (var ev in _bus.Read<SwitchTimeModeEvent>())
            _clusterTime.Apply(ev);
    }

    private void Process2PcNetworkTraffic()
    {
        // Insert new transactions when ExecuteNodeOpIntent arrives
        foreach (var intent in _bus.ReadManaged<ExecuteNodeOpIntent>())
        {
            // Sniff ManageEpisode Start/Stop/Forget to maintain active episodes set
            if (intent.Operation == FdpNodeOpType.StartEpisode
                && intent.DomainPayload is EpisodeHandlerPayload startEp
                && startEp.EpisodeId != Guid.Empty)
            {
                _activeEpisodes.Add(startEp.EpisodeId);
            }
            else if ((intent.Operation == FdpNodeOpType.StopEpisode
                      || intent.Operation == FdpNodeOpType.ForgetEpisode)
                     && intent.DomainPayload is EpisodeHandlerPayload stopEp)
            {
                _activeEpisodes.Remove(stopEp.EpisodeId);
            }

            var txId = intent.TransactionId;
            if (!_inFlight.ContainsKey(txId))
            {
                // Extract target cluster state from typed payload — no JSON parsing required
                var targetState = CurrentState;
                if (intent.DomainPayload is EditLoadHandlerPayload ep)
                    targetState = (ClusterState)ep.TargetState;
                else if (intent.DomainPayload is CommitStatePayload cp)
                    targetState = (ClusterState)(int)cp.TargetState;
                else if (intent.DomainPayload is int raw)
                    targetState = (ClusterState)raw;

                var tx = new DistributedTransaction
                {
                    TransactionId  = txId,
                    SourceDsmState = CurrentState,
                    TargetDsmState = targetState,
                    PayloadJson    = SerializePayload(intent.DomainPayload),
                };
                _inFlight[txId] = tx;
                _txHistory.Insert(0, tx);
                while (_txHistory.Count > 10) _txHistory.RemoveAt(_txHistory.Count - 1);
            }
        }
        HasInFlightTransaction = _inFlight.Count > 0;

        // Append NodeOpCompletedEvent ACKs to in-flight transactions
        foreach (var ev in _bus.ReadManaged<NodeOpCompletedEvent>())
        {
            if (_inFlight.TryGetValue(ev.TransactionId, out var tx))
            {
                if (!tx.NodeResponses.TryGetValue(ev.NodeId, out var opDict))
                {
                    opDict = new Dictionary<FdpNodeOpType, string>();
                    tx.NodeResponses[ev.NodeId] = opDict;
                }
                opDict[ev.Operation] = SerializePayload(ev.ResultPayload);
            }
        }
    }

    private void DrainSysOpStatus()
    {
        foreach (var ev in _bus.ReadManaged<ClusterOpCompletedEvent>())
        {
            // Skip InProgress (non-terminal) status codes
            if (ev.StatusCode == OrchestrationStatusCode.InProgress) continue;

            bool success = ev.StatusCode == OrchestrationStatusCode.Success;

            // Try an exact match (works when ClusterOpCompletedEvent.RequestId == transaction ID).
            if (_inFlight.Remove(ev.RequestId, out var matchedTx))
            {
                matchedTx.Completed = success;
                matchedTx.IsAborted = !success;
                if (success && ev.ResultPayload is ReplayPrepareResult rpr && rpr.DurationSeconds > 0f)
                    ReplayDuration = rpr.DurationSeconds;
            }
            else if (_inFlight.Count > 0)
            {
                // Fallback: a terminal ClusterOpCompletedEvent means the operation is done —
                // close all in-flight transactions.
                if (success && ev.ResultPayload is ReplayPrepareResult rpr2 && rpr2.DurationSeconds > 0f)
                    ReplayDuration = rpr2.DurationSeconds;
                foreach (var tx in _inFlight.Values)
                {
                    tx.Completed = success;
                    tx.IsAborted = !success;
                }
                _inFlight.Clear();
            }
            HasInFlightTransaction = _inFlight.Count > 0;

            // Update the diagnostic manifest when a DumpDiagnostics operation completes.
            // Orchestrator path: ResultPayload is List<FileManifestEntry> (after NAS pull).
            // ExCon path: ResultPayload is a JSON string (from DDS observer).
            if (success)
            {
                if (ev.ResultPayload is List<FileManifestEntry> directManifest && directManifest.Count > 0)
                {
                    LastDiagnosticManifest = directManifest;
                }
                else if (ev.ResultPayload is string json
                         && json.Length > 0
                         && json.TrimStart().StartsWith('['))
                {
                    try
                    {
                        var parsed = System.Text.Json.JsonSerializer.Deserialize<List<FileManifestEntry>>(
                            json, PayloadJsonOptions);
                        if (parsed != null && parsed.Count > 0)
                            LastDiagnosticManifest = parsed;
                    }
                    catch { /* Malformed JSON — keep previous manifest. */ }
                }
            }
        }
    }

    private static string SerializePayload(object? payload)
    {
        if (payload == null) return string.Empty;
        if (payload is string s) return s;
        try
        {
            return JsonSerializer.Serialize(payload, payload.GetType(), PayloadJsonOptions);
        }
        catch
        {
            return payload.ToString() ?? string.Empty;
        }
    }
}
