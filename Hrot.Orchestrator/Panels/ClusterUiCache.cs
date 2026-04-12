using System;
using System.Collections.Generic;
using Hrot.NED.Descriptors.Orchestration;
using Hrot.Orchestrator;
using Fdp.Kernel;
using FDP.Toolkit.Orchestration;
using FDP.Toolkit.Orchestration.Handlers;
using FDP.Toolkit.Time;
using FDP.Toolkit.Time.Messages;
using ModuleHost.Core.Time;
using ClusterState  = Hrot.NED.Descriptors.Orchestration.ClusterState;
using FdpNodeOpType = FDP.Toolkit.Orchestration.NodeOpType;

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
    private double                                            _networkSimTime;

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

    public void Dispose() { /* FdpEventBus is owned by the caller; nothing to dispose here. */ }

    // ── Private drain methods ──────────────────────────────────────────────────

    private void DrainSystemState()
    {
        foreach (var ev in _bus.ConsumeManaged<SystemStateUpdateEvent>())
        {
            var prev = CurrentState;
            CurrentState   = (ClusterState)(int)ev.CurrentState;
            IsBootstrapped = CurrentState != ClusterState.Degraded;
            if (CurrentState != prev)
                ReachableTargets = _planner.GetReachableTargets(CurrentState);
        }
    }

    private void DrainInventory()
    {
        foreach (var ev in _bus.ConsumeManaged<AssetInventoryUpdateEvent>())
        {
            AvailableScenarios           = ev.LocalScenarios           ?? Array.Empty<string>();
            AvailableExercises           = ev.LocalExercises           ?? Array.Empty<string>();
            ArchivedExercises            = ev.ArchivedExercises        ?? Array.Empty<string>();
            UnarchivedLocalExercises     = ev.UnarchivedLocalExercises ?? Array.Empty<string>();
        }
    }

    private void DrainHeartbeats()
    {
        foreach (var ev in _bus.ConsumeManaged<NodeHeartbeatEvent>())
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
        foreach (var ev in _bus.Consume<SwitchTimeModeEvent>())
        {
            var isDeterministic = ev.TargetMode == TimeMode.Deterministic;
            IsPaused = isDeterministic;
            if (ev.TimeScale > 0f)
                MasterTimeScale = ev.TimeScale;
            if (ev.BarrierWallTicks > 0)
                MasterWallTicks = ev.BarrierWallTicks;
            if (!isDeterministic && ev.SimTimeSnapshot > 0.0)
                _networkSimTime = ev.SimTimeSnapshot;
        }
    }

    private void Process2PcNetworkTraffic()
    {
        // Insert new transactions when ExecuteNodeOpIntent arrives
        foreach (var intent in _bus.ConsumeManaged<ExecuteNodeOpIntent>())
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
                var targetState = ClusterState.Idle;
                if (intent.DomainPayload is EditLoadHandlerPayload ep)
                    targetState = (ClusterState)ep.TargetState;
                else if (intent.DomainPayload is CommitStatePayload cp)
                    targetState = (ClusterState)cp.TargetStateId;
                else if (intent.DomainPayload is int raw)
                    targetState = (ClusterState)raw;

                var tx = new DistributedTransaction
                {
                    TransactionId  = txId,
                    TargetDsmState = targetState,
                };
                _inFlight[txId] = tx;
                _txHistory.Insert(0, tx);
                while (_txHistory.Count > 10) _txHistory.RemoveAt(_txHistory.Count - 1);
            }
        }
        HasInFlightTransaction = _inFlight.Count > 0;

        // Append NodeOpCompletedEvent ACKs to in-flight transactions
        foreach (var ev in _bus.ConsumeManaged<NodeOpCompletedEvent>())
        {
            if (_inFlight.TryGetValue(ev.TransactionId, out var tx))
                tx.NodeResponses[ev.NodeId] = ev.ResultPayload?.ToString() ?? string.Empty;
        }
    }

    private void DrainSysOpStatus()
    {
        foreach (var ev in _bus.ConsumeManaged<ClusterOpCompletedEvent>())
        {
            // Skip InProgress (non-terminal) status codes
            if (ev.StatusCode == OrchestrationStatusCode.InProgress) continue;

            bool success = ev.StatusCode == OrchestrationStatusCode.Success;

            // Try an exact match (works when ClusterOpCompletedEvent.RequestId == transaction ID).
            if (_inFlight.Remove(ev.RequestId, out var matchedTx))
            {
                matchedTx.Completed = success;
                matchedTx.IsAborted = !success;
            }
            else if (_inFlight.Count > 0)
            {
                // Fallback: a terminal ClusterOpCompletedEvent means the operation is done —
                // close all in-flight transactions.
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
}
