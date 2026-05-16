using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Hrot.NED.Descriptors.Orchestration;
using Hrot.Network.Orchestration;
using Fdp.Core.Logging;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using Fdp.Core;
using Fdp.Toolkit.Time.Domain;
using Fdp.Toolkit.Time.Controllers;
using ClusterState  = Hrot.NED.Descriptors.Orchestration.ClusterState;
using ClusterOpType = Hrot.NED.Descriptors.Orchestration.ClusterOpType;
using NodeOpType    = Hrot.NED.Descriptors.Orchestration.NodeOpType;
using FdpClusterState = Fdp.Toolkit.Orchestration.ClusterState;

namespace Hrot.Orchestrator;

/// <summary>
/// Orchestrator control-plane host: system state, node heartbeats, DDS network ID allocation server,
/// bootstrap latch, heartbeat-timeout eviction, and 2PC transaction history ring buffer.
/// </summary>
public sealed class ClusterMaster : IDisposable
{
    private readonly ClusterConfiguration _config;

    // ── FdpEventBus ──────────────────────────────────────────────────────
    private readonly FdpEventBus _eventBus;

    // ── Roster ────────────────────────────────────────────────────────────
    private readonly NodeRoster _roster = new();

    /// <summary>
    /// Unified 2PC transaction tracker used for ALL in-flight operations:
    /// <see cref="NodeOpType.SerializeLocal"/>, <see cref="ClusterOpType.ManageEpisode"/>,
    /// <see cref="ClusterOpType.TransitionState"/>, <see cref="ClusterOpType.TakeCheckpoint"/>,
    /// and <see cref="ClusterOpType.ReplaySeek"/>.
    /// <para>
    /// The tracker is domain-agnostic: it counts ACKs, collects raw per-node response JSON,
    /// and calls the registered aggregator pipeline when all expected ACKs arrive.
    /// Domain-specific context (e.g. episode IDs) is encoded at fan-out time via
    /// <see cref="SyntheticResponseJson"/> rather than storing it here.
    /// </para>
    /// </summary>
    private sealed class GenericTransactionTracker
    {
        public Guid RequestId;
        public int  Expected;
        public int  Received;
        public bool HasFailure;
        public OrchestrationStatusCode FailureCode;
        /// <summary>
        /// When <c>true</c>, the first node error immediately aborts and rejects the transaction
        /// (ManageEpisode policy).  When <c>false</c>, all ACKs are collected before publishing
        /// the final status (TransitionState / SerializeLocal / TakeCheckpoint / ReplaySeek policy).
        /// </summary>
        public bool AbortOnFirstFailure;
        /// <summary>
        /// When non-null, overrides the raw ACK payload for every node response stored in
        /// <see cref="NodeResponses"/>.  Used for ManageEpisode where the node's ACK does not
        /// carry episode context — the episode context is serialized at fan-out time and injected here.
        /// </summary>
        public string? SyntheticResponseJson;
        /// <summary>
        /// When <c>true</c>, <see cref="ClusterMaster.PublishClusterState"/> is called on
        /// completion.  Set for all operations that were formerly tracked in
        /// <c>_pendingBusTransitionAcks</c> (TransitionState, TakeCheckpoint, ReplaySeek).
        /// </summary>
        public bool BroadcastClusterStateOnComplete;
        /// <summary>Per-node response JSON strings fed into the aggregator pipeline.</summary>
        public readonly Dictionary<int, Dictionary<Fdp.Toolkit.Orchestration.NodeOpType, string>> NodeResponses = new();
    }

    /// <summary>
    /// Single unified dictionary that tracks all in-flight 2PC rounds keyed by transaction ID.
    /// Replaces the former bespoke <c>_pendingSerializeTasks</c>,
    /// <c>_pendingManageEpisodeTasks</c>, and <c>_pendingBusTransitionAcks</c> dictionaries.
    /// </summary>
    private readonly Dictionary<Guid, GenericTransactionTracker> _pendingTransactions = new();

    // ── Node-response aggregators (OCP/SRP: domain aggregation outside generic 2PC) ──
    private readonly Dictionary<Fdp.Toolkit.Orchestration.NodeOpType, INodeResponseAggregator> _aggregators = new();

    // ── Active archive operation cancellations (CGF1-S0505) ──────────────
    /// <summary>
    /// Tracks <see cref="CancellationTokenSource"/> instances for in-progress
    /// <see cref="ClusterOpType.ExportArchive"/> and <see cref="ClusterOpType.ImportArchive"/>
    /// operations, keyed by their originating <see cref="ClusterOpRequest.RequestId"/>.
    /// Cancelled by a <see cref="ClusterOpType.CancelOperation"/> request.
    /// </summary>
    private readonly Dictionary<Guid, CancellationTokenSource> _activeCancellations = new();

    // ── Bootstrap latch (CGF1-S0105) ──────────────────────────────────────
    /// <summary>
    /// <c>true</c> once every mandatory subsystem has appeared with <c>LocalClusterState == Standby</c>.
    /// While <c>false</c> all <see cref="ClusterOpRequest"/> messages are rejected.
    /// </summary>
    private bool _bootstrapLatch;

    // ── Active transaction ────────────────────────────────────────────────
    private DistributedTransaction? _activeTransaction;

    /// <summary>
    /// Tracks the most-recently created <see cref="ClusterOpType.TransitionState"/> transaction
    /// so that <see cref="ConsumeNodeOpStatuses"/> can populate
    /// <see cref="DistributedTransaction.NodeResponses"/> as node ACKs arrive (CGF1-S0501).
    /// </summary>
    private DistributedTransaction? _inflightTransitionTx;

    // ── Seek sync controller (CGF1-S0305 / T002) ────────────────────────────
    // MasterSyncController removed in TASK-T002: SnapAndPause moved to ReplaySeekProcessManager.


    // ── Current Cluster state (tracked here so the planner can compute relative paths) ─
    /// <summary>
    /// Optimistic cluster Cluster state used as the <c>current</c> argument to
    /// <see cref="ClusterMasterPlanner.PlanTrajectory"/>.
    ///
    /// <para><b>Update rule (Phase 2.0 — optimistic):</b> Whenever a
    /// <see cref="ClusterOpType.TransitionState"/> request is <em>accepted</em> (plan
    /// succeeds), this field is immediately advanced to the final
    /// <see cref="TransitionStep.TargetState"/> in the computed trajectory.
    /// This ensures that a second <c>TransitionState</c> request issued before the
    /// first completes is planned from the <em>intended</em> end-state rather than
    /// the stale initial state.</para>
    ///
    /// <para><b>Limitation:</b> Until proper two-phase commit ACKs land in
    /// <c>CGF1-S0202+</c> the value is optimistic and may diverge from cluster
    /// reality if a transaction is aborted mid-flight.  The field will be replaced
    /// with authoritative tracking (last written <see cref="ClusterStateTopic.CurrentState"/>
    /// or aggregated <c>NodeOpStatus</c> confirmation) in a later stage.</para>
    /// </summary>
    private ClusterState _currentDsmState = ClusterState.Idle;
    private Guid _activeExerciseId;

    // ── Transition planner (CGF1-S0201) ─────────────────────────────────────
    private readonly ClusterMasterPlanner _planner = new ClusterMasterPlanner(HrotStateGraph.Build());

    // ── 2PC history ring buffer (CGF1-S0105) ─────────────────────────────
    private readonly DistributedTransaction[] _history;
    private int  _historyHead;

    // ── Time mode hint (CGF1-S0205) ──────────────────────────────────────
    /// <summary>
    /// Set when a <see cref="ClusterOpType.TransitionState"/> request heading toward
    /// <see cref="ClusterState.LoadingLive"/> carries <c>"TimeMode": "Deterministic"</c>
    /// in the transition intent's typed payload.
    ///
    /// <para>Consumers (e.g. <c>OrchestratorSubsystem</c>) should read this property
    /// after <see cref="Tick"/> and trigger <c>DistributedTimeCoordinator.SwitchToDeterministic</c>
    /// before the cluster enters <see cref="ClusterState.OperatingLive"/>.</para>
    ///
    /// <para>Reset to <c>null</c> when a <see cref="ClusterState.Idle"/> trajectory clears the
    /// pending mode.</para>
    /// </summary>
    public string? PendingTimeMode { get; private set; }

    private bool _disposed;

    // ── Public surface ────────────────────────────────────────────────────
    public NodeRoster NodeRoster => _roster;

    /// <summary><c>true</c> once all mandatory nodes have reached <c>Standby</c>.</summary>
    public bool BootstrapComplete => _bootstrapLatch;

    /// <summary>
    /// Current cluster Cluster state (optimistic — advances on accepted transitions).
    /// Exposed for UI panels (CGF1-S0106) and time-mode consumers.
    /// </summary>
    public ClusterState CurrentClusterState => _currentDsmState;
    public Guid ActiveExerciseId => _activeExerciseId;

    /// <summary>
    /// <c>true</c> when a distributed transaction is currently in flight.
    /// Used by <c>OrchestratorScenarioPanel</c> to disable command buttons while
    /// a 2PC round is pending (CGF1-S0106).
    /// </summary>
    public bool HasInFlightTransaction => _activeTransaction != null;

    /// <summary>
    /// The currently active distributed transaction, or <see langword="null"/> when idle.
    /// Exposed for the status banner in <c>OrchestratorScenarioPanel</c> (CGF1-S0106).
    /// </summary>
    public DistributedTransaction? ActiveTransaction => _activeTransaction;

    /// <summary>
    /// Returns the Cluster states that can be reached from the current cluster state in a
    /// single planning step.  Used by <c>OrchestratorScenarioPanel</c> to populate
    /// the Cluster Control buttons dynamically (CGF1-S0106).
    /// </summary>
    public IReadOnlyList<ClusterState> GetReachableTargets() =>
        _planner.GetReachableTargets(_currentDsmState);

    /// <summary>
    /// Snapshot of completed and aborted transactions in insertion order.
    /// The returned array may contain trailing nulls when the buffer is not yet full.
    /// </summary>
    public IReadOnlyList<DistributedTransaction> TransactionHistory
    {
        get
        {
            var cap   = _history.Length;
            var count = 0;
            for (int i = 0; i < cap; i++)
                if (_history[i] != null) count++;
            var result = new DistributedTransaction[count];
            int ri = 0;

            // Return in chronological order (oldest → newest)
            if (count == cap)
            {
                for (int i = 0; i < cap; i++)
                    result[ri++] = _history[(_historyHead + i) % cap];
            }
            else
            {
                for (int i = 0; i < cap && ri < count; i++)
                    if (_history[i] != null) result[ri++] = _history[i];
            }
            return result;
        }
    }

    // ── Constructors ──────────────────────────────────────────────────────

    /// <summary>
    /// Bus-based constructor.  <c>ClusterMaster</c> ingests heartbeats and publishes
    /// fan-out operations exclusively via the <paramref name="eventBus"/>.
    /// </summary>
    public ClusterMaster(FdpEventBus eventBus, ClusterConfiguration? config = null)
    {
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _config   = config ?? ClusterConfiguration.Default;
        _history  = new DistributedTransaction[Math.Max(1, _config.TransactionHistoryCapacity)];

        if (_config.Mandatory.Length == 0) { _bootstrapLatch = true; PublishStandby(); }
    }

    // ── Per-frame tick ────────────────────────────────────────────────────

    public void Tick()
    {
        IngestHeartbeats();
        CheckBootstrapLatch();
        DetectAndEjectTimedOutNodes();
        DrainInjectedRequests();

        // Bus-based intent drain (CMC-S008).
        ProcessTransitionStateIntents();
        ProcessManageEpisodeIntents();
        ProcessStorageOpIntents();
        ProcessTakeCheckpointIntents();
        ProcessSeekReplayIntents();
        ProcessCancelOperationIntents();
        ProcessDiagnosticDumpIntents();

        ConsumeNodeOpStatuses();
    }

    // ── UI / test injection path ──────────────────────────────────────────

    private readonly System.Collections.Concurrent.ConcurrentQueue<ClusterOpRequest>
        _injectedRequests = new();

    /// <summary>
    /// Injects a <see cref="ClusterOpRequest"/> directly into the ClusterMaster processing
    /// queue, bypassing DDS.  Used by UI panels (e.g. <c>OrchestratorScenarioPanel</c>)
    /// and integration tests that need to drive the orchestrator without creating a
    /// separate DDS publisher (CGF1-S0106).
    ///
    /// <para>
    /// Thread-safe: may be called from any thread.  The request is processed on the
    /// next <see cref="Tick"/> call on the main thread.
    /// </para>
    /// </summary>
    public void HandleClusterOpRequest(ClusterOpRequest request)
    {
        _injectedRequests.Enqueue(request);
    }

    /// <summary>
    /// Async wrapper around <see cref="HandleClusterOpRequest"/> for use by UI panels and
    /// headless test action handlers that await the enqueue step.  The returned
    /// <see cref="Task"/> completes immediately after the request is enqueued; callers
    /// that need to wait for the resulting <see cref="ClusterOpStatus"/> must poll a
    /// <c>DdsReader&lt;SysOpStatus&gt;</c> independently.
    /// </summary>
    public Task HandleClusterOpRequestAsync(ClusterOpRequest request)
    {
        HandleClusterOpRequest(request);
        return Task.CompletedTask;
    }

    private void DrainInjectedRequests()
    {
        while (_injectedRequests.TryDequeue(out var req))
            ProcessSingleClusterOpRequest(req);
    }

    /// <summary>
    /// Adapts a legacy <see cref="ClusterOpRequest"/> (from UI injection or test harness) to
    /// typed bus events using <see cref="ClusterOpRequestAdapter"/>.  This is the only
    /// remaining path that uses NED DTO types in ClusterMaster; DDS infrastructure has been
    /// fully removed.  Full purge of this API is deferred to Phase 6.
    /// </summary>
    private void ProcessSingleClusterOpRequest(ClusterOpRequest req)
    {
        if (!_bootstrapLatch)
        {
            PublishOpStatus(req.RequestId, OrchestrationStatusCode.Rejected);
            return;
        }

        // S0503: Time-control operations bypass 2PC.
        // Publish typed intents to the bus; MasterSyncController drains them in Phase 3 (HEXAG2-S011).
        if (req.OperationType is ClusterOpType.PauseTime or ClusterOpType.ResumeTime
                              or ClusterOpType.StepTime  or ClusterOpType.SetTimeScale)
        {
            switch (req.OperationType)
            {
                case ClusterOpType.PauseTime:
                {
                    var slaveIds = _roster.ActiveNodes
                        .Where(kv => kv.Value.SubsystemName is "SimHost" or "IG" or "CGF")
                        .Select(kv => kv.Key)
                        .ToHashSet();
                    _eventBus.PublishManaged(new SlaveNodeSetUpdatedEvent { SlaveNodeIds = slaveIds });
                    _eventBus.PublishManaged(new PauseTimeIntent());
                    break;
                }
                case ClusterOpType.ResumeTime:
                    _eventBus.PublishManaged(new ResumeTimeIntent());
                    break;
                case ClusterOpType.StepTime:
                {
                    string payload = ClusterOpRequestAdapter.GetPayloadString(req);
                    StepTimePayloadDto? dto = null;
                    if (!string.IsNullOrWhiteSpace(payload))
                    {
                        try { dto = JsonSerializer.Deserialize<StepTimePayloadDto>(payload, OrchestrationJsonOptions.Default); }
                        catch { }
                    }
                    float delta = dto != null && dto.FixedDelta > 0f ? dto.FixedDelta : 1f / 60f;
                    _eventBus.PublishManaged(new StepTimeIntent { DeltaSeconds = delta });
                    break;
                }
                case ClusterOpType.SetTimeScale:
                {
                    string payload = ClusterOpRequestAdapter.GetPayloadString(req);
                    SetTimeScalePayloadDto? dto = null;
                    if (!string.IsNullOrWhiteSpace(payload))
                    {
                        try { dto = JsonSerializer.Deserialize<SetTimeScalePayloadDto>(payload, OrchestrationJsonOptions.Default); }
                        catch { }
                    }
                    float scale = dto != null && dto.TimeScale > 0f ? dto.TimeScale : 1f;
                    _eventBus.PublishManaged(new SetTimeScaleIntent { TimeScale = scale });
                    break;
                }
            }
            return;
        }

        switch (req.OperationType)
        {
            case ClusterOpType.TransitionState:
                try
                {
                    ProcessTransitionStateIntent(ClusterOpRequestAdapter.ToTransitionStateIntent(req));
                }
                catch (InvalidOperationException ex)
                {
                    FdpLog<ClusterMaster>.Warn("[Orchestrator] TransitionState request {0} rejected: {1}", req.RequestId, ex.Message);
                    PublishOpStatus(req.RequestId, OrchestrationStatusCode.Failure);
                }
                break;

            case ClusterOpType.ManageEpisode:
                try
                {
                    ProcessManageEpisodeIntent(ClusterOpRequestAdapter.ToManageEpisodeIntent(req));
                }
                catch (InvalidOperationException ex)
                {
                    FdpLog<ClusterMaster>.Warn("[Orchestrator] ManageEpisode request {0} rejected: {1}", req.RequestId, ex.Message);
                    PublishOpStatus(req.RequestId, OrchestrationStatusCode.Rejected);
                }
                break;

            case ClusterOpType.SaveScenario:
            case ClusterOpType.ExportArchive:
            case ClusterOpType.ImportArchive:
                ProcessStorageOpIntent(ClusterOpRequestAdapter.ToExecuteStorageOpIntent(req));
                break;

            case ClusterOpType.TakeCheckpoint:
            {
                var nodeIds = new List<int>(_roster.ActiveNodes.Keys);
                if (nodeIds.Count == 0)
                {
                    PublishOpStatus(req.RequestId, OrchestrationStatusCode.Success);
                    break;
                }
                var txId = Guid.NewGuid();
                FanOutNodeOp(NodeOpType.TakeSnapshot, txId, null, nodeIds);
                _pendingTransactions[txId] = new GenericTransactionTracker
                {
                    RequestId                       = req.RequestId,
                    Expected                        = nodeIds.Count,
                    BroadcastClusterStateOnComplete = true,
                };
                break;
            }

            case ClusterOpType.ReplaySeek:
                ProcessSeekReplayIntent(ClusterOpRequestAdapter.ToSeekReplayIntent(req));
                break;

            case ClusterOpType.CancelOperation:
                ProcessCancelOperationIntent(ClusterOpRequestAdapter.ToCancelOperationIntent(req));
                break;
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────

    private void IngestHeartbeats()
    {
        foreach (var hb in _eventBus.ReadManaged<NodeHeartbeatEvent>())
        {
            var profile = new NodeHealthProfile
            {
                NodeId                  = hb.NodeId,
                SubsystemName           = hb.SubsystemName ?? string.Empty,
                LocalClusterState       = (ClusterState)(int)hb.LocalStateId,
                LastHeartbeatUtcSeconds = UtcNowSeconds(),
            };
            _roster.Upsert(profile);
        }
    }

    /// <summary>
    /// Re-evaluates whether all mandatory subsystem names have a roster entry in <c>Standby</c>.
    /// Clears the bootstrap latch and publishes <c>Standby</c> when the condition first becomes true.
    /// </summary>
    private void CheckBootstrapLatch()
    {
        if (_bootstrapLatch) return;
        if (_config.Mandatory.Length == 0) return;

        foreach (var name in _config.Mandatory)
        {
            bool found = false;
            foreach (var kv in _roster.ActiveNodes)
            {
                if (string.Equals(kv.Value.SubsystemName, name, StringComparison.OrdinalIgnoreCase)
                    && kv.Value.LocalClusterState == ClusterState.Idle)
                {
                    found = true;
                    break;
                }
            }
            if (!found) return;
        }

        // All mandatory nodes are in Standby — latch released.
        _bootstrapLatch = true;
        PublishStandby();
        FdpLog<ClusterMaster>.Info("[Orchestrator] All mandatory nodes reached Standby — bootstrap complete.");
    }

    /// <summary>
    /// Iterates the roster and calls <see cref="EjectNode"/> for any node whose last heartbeat
    /// is older than <see cref="ClusterConfiguration.HeartbeatTimeoutSeconds"/>.
    /// If a mandatory-node ejection re-engages the bootstrap latch, processing stops so that
    /// remaining nodes stay in the roster and receive the <c>PrepareState(Standby)</c> broadcast.
    /// </summary>
    private void DetectAndEjectTimedOutNodes()
    {
        var now          = UtcNowSeconds();
        var timeout      = _config.HeartbeatTimeoutSeconds;
        var timedOut     = new List<int>();

        foreach (var kv in _roster.ActiveNodes)
        {
            if (now - kv.Value.LastHeartbeatUtcSeconds > timeout)
                timedOut.Add(kv.Key);
        }

        foreach (var nodeId in timedOut)
        {
            EjectNode(nodeId);
            // After a mandatory-node ejection the latch is re-engaged; stop here so surviving
            // nodes remain in the roster and receive the broadcast in this same tick.
            if (!_bootstrapLatch) break;
        }
    }

    /// <summary>
    /// Evicts a node from the cluster:
    /// <list type="number">
    ///   <item>Removes from the active roster.</item>
    ///   <item>If the node was mandatory: aborts any in-flight transaction, publishes
    ///         <c>Degraded</c>, broadcasts <c>AbortTransaction</c> + <c>PrepareState(Standby)</c>
    ///         to surviving nodes, and re-engages the bootstrap latch.</item>
    /// </list>
    /// </summary>
    public void EjectNode(int nodeId)
    {
        if (!_roster.ActiveNodes.TryGetValue(nodeId, out var profile))
            return;

        _roster.Remove(nodeId);
        FdpLog<ClusterMaster>.Warn("[Orchestrator] Node {0} ({1}) ejected (heartbeat timeout).",
            nodeId, profile.SubsystemName);

        bool isMandatory = Array.IndexOf(_config.Mandatory, profile.SubsystemName) >= 0;
        if (!isMandatory) return;

        // Abort any in-flight transaction.
        if (_activeTransaction != null)
        {
            _activeTransaction.IsAborted = true;
            AppendToHistory(_activeTransaction);
            _activeTransaction = null;
        }

        // Publish Degraded system state.
        PublishClusterState(ClusterState.Degraded);
        FdpLog<ClusterMaster>.Warn("[Orchestrator] System entered Degraded state (mandatory node {0} lost).", profile.SubsystemName);

        // Broadcast AbortTransaction + PrepareState(Standby) to surviving nodes only.
        var survivingIds = new List<int>(_roster.ActiveNodes.Keys);
        FanOutNodeOp(NodeOpType.AbortTransaction, Guid.NewGuid(), null, survivingIds);
        FanOutNodeOp(NodeOpType.PrepareState,     Guid.NewGuid(), (int)ClusterState.Idle, survivingIds);

        // Re-engage bootstrap latch until the mandatory node returns.
        _bootstrapLatch = false;
    }

    // ── Bus-path intent drain methods (CMC-S008) ──────────────────────────

    private void ProcessTransitionStateIntents()
    {
        foreach (var intent in _eventBus.ReadManaged<TransitionStateIntent>())
        {
            if (!_bootstrapLatch) { PublishOpStatus(intent.TransactionId, OrchestrationStatusCode.Rejected); continue; }
            try   { ProcessTransitionStateIntent(intent); }
            catch (InvalidOperationException ex)
            {
                FdpLog<ClusterMaster>.Warn("[Orchestrator] TransitionStateIntent rejected: {0}", ex.Message);
                PublishOpStatus(intent.TransactionId, OrchestrationStatusCode.Failure);
            }
        }
    }

    private void ProcessManageEpisodeIntents()
    {
        foreach (var intent in _eventBus.ReadManaged<ManageEpisodeIntent>())
        {
            if (!_bootstrapLatch) { PublishOpStatus(intent.TransactionId, OrchestrationStatusCode.Rejected); continue; }
            try   { ProcessManageEpisodeIntent(intent); }
            catch (InvalidOperationException ex)
            {
                FdpLog<ClusterMaster>.Warn("[Orchestrator] ManageEpisodeIntent rejected: {0}", ex.Message);
                PublishOpStatus(intent.TransactionId, OrchestrationStatusCode.Rejected);
            }
        }
    }

    private void ProcessStorageOpIntents()
    {
        foreach (var intent in _eventBus.ReadManaged<ExecuteStorageOpIntent>())
            ProcessStorageOpIntent(intent);
    }

    private void ProcessTakeCheckpointIntents()
    {
        foreach (var intent in _eventBus.ReadManaged<TakeCheckpointIntent>())
        {
            var ckNodeIds = new List<int>(_roster.ActiveNodes.Keys);
            if (ckNodeIds.Count == 0)
            {
                PublishOpStatus(intent.RequestId, OrchestrationStatusCode.Success);
                continue;
            }
            var ckTxId = Guid.NewGuid();
            FanOutNodeOp(NodeOpType.TakeSnapshot, ckTxId, null, ckNodeIds);
            _pendingTransactions[ckTxId] = new GenericTransactionTracker
            {
                RequestId                       = intent.RequestId,
                Expected                        = ckNodeIds.Count,
                BroadcastClusterStateOnComplete = true,
            };
        }
    }

    private void ProcessSeekReplayIntents()
    {
        foreach (var intent in _eventBus.ReadManaged<SeekReplayIntent>())
        {
            ProcessSeekReplayIntent(intent);
        }
    }

    private void ProcessCancelOperationIntents()
    {
        foreach (var intent in _eventBus.ReadManaged<CancelOperationIntent>())
            ProcessCancelOperationIntent(intent);
    }

    private void ProcessDiagnosticDumpIntents()
    {
        foreach (var intent in _eventBus.ReadManaged<ExecuteDiagnosticDumpIntent>())
        {
            if (!_bootstrapLatch)
            {
                PublishOpStatus(intent.RequestId, OrchestrationStatusCode.Rejected);
                continue;
            }

            DiagnosticDumpPayloadDto? dto = null;
            try
            {
                dto = JsonSerializer.Deserialize<DiagnosticDumpPayloadDto>(
                    intent.PayloadJson, OrchestrationJsonOptions.Default);
            }
            catch (Exception ex)
            {
                FdpLog<ClusterMaster>.Warn("[Orchestrator] Failed to parse diagnostic dump payload: {0}", ex.Message);
            }

            if (dto == null)
            {
                PublishOpStatus(intent.RequestId, OrchestrationStatusCode.Rejected);
                continue;
            }

            var targetNodes = dto.TargetNodeIds != null && dto.TargetNodeIds.Length > 0
                ? new List<int>(dto.TargetNodeIds)
                : new List<int>(_roster.ActiveNodes.Keys);

            if (targetNodes.Count == 0)
            {
                PublishOpStatus(intent.RequestId, OrchestrationStatusCode.Success);
                continue;
            }

            FanOutNodeOp(NodeOpType.CollectDiagnostics, intent.RequestId, dto, targetNodes);

            _pendingTransactions[intent.RequestId] = new GenericTransactionTracker
            {
                RequestId = intent.RequestId,
                Expected  = targetNodes.Count,
                BroadcastClusterStateOnComplete = false,
            };

            FdpLog<ClusterMaster>.Info(
                "[Orchestrator] Diagnostic Dump {0} fanned out to {1} node(s).",
                intent.RequestId, targetNodes.Count);
        }
    }

    // ── Typed intent handlers (shared by bus and legacy DDS paths) ────────

    private void ProcessTransitionStateIntent(TransitionStateIntent intent)
    {
        var requestId          = intent.TransactionId;
        var stateBeforeAdvance = _currentDsmState;

        var trajectory = _planner.PlanTrajectory(_currentDsmState, intent);
        int totalSteps = trajectory.Count;

        // Extract the final TransitionStep target for history recording and optimistic advance.
        var resolvedTarget = _currentDsmState;
        foreach (var step in trajectory)
        {
            if (step is TransitionStep ts) resolvedTarget = ts.TargetState;
        }

        if (intent.ExerciseId != Guid.Empty)
        {
            _activeExerciseId = intent.ExerciseId;
        }
        else if (resolvedTarget == ClusterState.Idle)
        {
            _activeExerciseId = Guid.Empty;
        }

        var capturedSourceState = _currentDsmState;
        _currentDsmState = resolvedTarget;

        // CGF1-S0302: Emit prefetch intent; PrefetchFiles fan-out is deferred until staging completes.
        foreach (var step in trajectory)
        {
            if (step is OperationStep { Operation: ClusterOpType.PrefetchScenario } ps)
            {
                _eventBus.PublishManaged(new ExecutePrefetchIntent
                {
                    RequestId     = requestId,
                    ScenarioId    = (string?)ps.DomainPayload ?? string.Empty,
                    ActiveNodeIds = new List<int>(_roster.ActiveNodes.Keys),
                });
                break;
            }
        }

        // CGF1-S0205: Capture TimeMode when trajectory passes through simulation bootstrap.
        bool passesSimulationStart = trajectory.OfType<TransitionStep>()
            .Any(ts => ts.TargetState == ClusterState.LoadingLive || ts.TargetState == ClusterState.LoadingPreview);

        if (passesSimulationStart && !string.IsNullOrWhiteSpace(intent.TimeMode))
            PendingTimeMode = intent.TimeMode;
        if (resolvedTarget == ClusterState.Idle)
            PendingTimeMode = null;

        // Live-from-Replay FreezeTime is now handled by LiveBranchProcessManager (TASK-T001).

        var tx = new DistributedTransaction
        {
            TransactionId   = Guid.NewGuid(),
            OriginRequestId = requestId,
            TargetDsmState  = resolvedTarget,
            TotalSteps      = totalSteps,
            CompletedSteps  = totalSteps,
            IsAborted       = false,
            SourceDsmState  = capturedSourceState,
            PayloadJson     = string.Empty,
        };
        _activeTransaction    = tx;
        _inflightTransitionTx = tx;
        AppendToHistory(tx);

        // S0502: Fan out PrepareXxx + CommitState to all active nodes.
        var activeNodeIds = new List<int>(_roster.ActiveNodes.Keys);
        if (activeNodeIds.Count > 0)
        {
                foreach (var step in trajectory)
                {
                    if (step is TransitionStep tStep)
                    {
                        NodeOpType prepareOp = tStep.TargetState switch
                        {
                            ClusterState.LoadingLive     => NodeOpType.PrepareLive,
                            ClusterState.UnloadingLive   => NodeOpType.FinalizeLive,
                            ClusterState.LoadingReplay   => NodeOpType.PrepareReplay,
                            ClusterState.UnloadingReplay => NodeOpType.FinalizeReplay,
                            ClusterState.LoadingEdit     => NodeOpType.PrepareEdit,
                            ClusterState.UnloadingEdit   => NodeOpType.FinalizeEdit,
                            _                            => NodeOpType.PrepareState,
                        };

                        // DomainPayload always carries TargetState so ClusterSlave can use it
                        // as a dedup discriminant for PrepareState ops that share the same txId.
                        // ScenarioId is only populated for the two load states that actually need it.
                        var preparePayload = new EditLoadHandlerPayload(
                            tStep.TargetState == ClusterState.LoadingLive || tStep.TargetState == ClusterState.LoadingEdit
                                ? intent.ScenarioId : null,
                            false,
                            (FdpClusterState)(int)tStep.TargetState,
                            ExerciseId: intent.ExerciseId);

                        FanOutNodeOp(prepareOp,             tx.TransactionId, preparePayload,              activeNodeIds);
                        FanOutNodeOp(NodeOpType.CommitState, tx.TransactionId,
                            new CommitStatePayload((FdpClusterState)(int)tStep.TargetState), activeNodeIds);
                    }
                    else if (step is OperationStep opStep && opStep.Operation == ClusterOpType.ReplaySeek)
                    {
                        FanOutNodeOp(NodeOpType.NodeReplaySeek, Guid.NewGuid(), opStep.DomainPayload, activeNodeIds);
                    }
                }

                FdpLog<ClusterMaster>.Info(
                    "[Orchestrator] S0502: TransitionState fan-out complete (transaction={0}, nodes={1}).",
                    tx.TransactionId, activeNodeIds.Count);
        }

        // ── Unified 2PC ACK tracking ──────────────────────────────────────────
        if (_eventBus != null)
        {
            // Count expected ACKs: one per PrepareXxx TransitionStep per active node.
            // CommitState is handled in-slave synchronously and does NOT publish ACK.
            int prepSteps    = trajectory.OfType<TransitionStep>().Count();
            int expectedAcks = prepSteps * activeNodeIds.Count;
            if (expectedAcks > 0)
            {
                _pendingTransactions[tx.TransactionId] = new GenericTransactionTracker
                {
                    RequestId                    = requestId,
                    Expected                     = expectedAcks,
                    BroadcastClusterStateOnComplete = true,
                };
            }
            else
            {
                // No nodes registered or no prepare steps — complete immediately.
                PublishOpStatus(requestId, OrchestrationStatusCode.Success);
            }
        }
        else
        {
            PublishOpStatus(requestId, OrchestrationStatusCode.InProgress);
        }
        _activeTransaction = null;  // ClusterMaster uses sync fan-out; clear immediately

        FdpLog<ClusterMaster>.Info(
            "[Orchestrator] TransitionStateIntent {0} accepted (transaction {1}).",
            requestId, tx.TransactionId);
    }

    private void ProcessManageEpisodeIntent(ManageEpisodeIntent intent)
    {
        var requestId = intent.TransactionId;

        var episodeSteps = _planner.PlanManageEpisode(_currentDsmState, intent);

        foreach (var step in episodeSteps)
        {
            if (step is OperationStep { Operation: ClusterOpType.PrefetchScenario } prefetch)
            {
                _eventBus.PublishManaged(new ExecutePrefetchIntent
                {
                    RequestId     = requestId,
                    ScenarioId    = (string?)prefetch.DomainPayload ?? string.Empty,
                    ActiveNodeIds = new List<int>(_roster.ActiveNodes.Keys),
                });
            }
            else if (step is OperationStep { Operation: ClusterOpType.ManageEpisode })
            {
                var nodeOp  = intent.IsStart ? NodeOpType.StartEpisode : NodeOpType.StopEpisode;
                var txId    = Guid.NewGuid();
                var nodeIds = new List<int>(_roster.ActiveNodes.Keys);

                var episodePayload = new EpisodeHandlerPayload(intent.EpisodeId, intent.ScenarioId, intent.IsStart);
                FanOutNodeOp(nodeOp, txId, episodePayload, nodeIds);

                if (nodeIds.Count > 0)
                {
                    var syntheticJson = System.Text.Json.JsonSerializer.Serialize(
                        new EpisodeConsensusPayload { EpisodeId = intent.EpisodeId, IsStart = intent.IsStart });
                    _pendingTransactions[txId] = new GenericTransactionTracker
                    {
                        RequestId             = requestId,
                        Expected              = nodeIds.Count,
                        AbortOnFirstFailure   = true,
                        SyntheticResponseJson = syntheticJson,
                    };
                }
                else
                {
                    // Zero-node roster: publish event directly so EpisodeProcessManager still fires.
                    PublishOpStatus(requestId, OrchestrationStatusCode.Success,
                        new EpisodeConsensusPayload { EpisodeId = intent.EpisodeId, IsStart = intent.IsStart });
                }

                FdpLog<ClusterMaster>.Info(
                    "[Orchestrator] ManageEpisode {0}: episode {1} → {2} to {3} node(s).",
                    intent.IsStart ? "Start" : "Stop", intent.EpisodeId, nodeOp, nodeIds.Count);
            }
        }
    }

    private void ProcessStorageOpIntent(ExecuteStorageOpIntent intent)
    {
        switch (intent.Operation)
        {
            case StorageOpType.SaveScenario:
            {
                var nodeIds = new List<int>(_roster.ActiveNodes.Keys);
                var txId    = Guid.NewGuid();
                FanOutSerializeLocal(txId, nodeIds, new ArchiveHandlerPayload(intent.ExerciseId));

                FdpLog<ClusterMaster>.Info("[Orchestrator] SaveScenario → SerializeLocal fan-out to {0} node(s).", nodeIds.Count);
                break;
            }

            case StorageOpType.Export:
            {
                if (intent.ExerciseId == Guid.Empty)
                {
                    FdpLog<ClusterMaster>.Warn("[Orchestrator] ExportArchive missing ExerciseId — rejected (requestId={0}).", intent.RequestId);
                    PublishOpStatus(intent.RequestId, OrchestrationStatusCode.Rejected);
                    return;
                }

                var exportCts = new CancellationTokenSource();
                _activeCancellations[intent.RequestId] = exportCts;

                var exportNodeIds = new List<int>(_roster.ActiveNodes.Keys);
                if (exportNodeIds.Count == 0)
                {
                    _activeCancellations.Remove(intent.RequestId);
                    exportCts.Dispose();
                    PublishOpStatus(intent.RequestId, OrchestrationStatusCode.Success);
                    break;
                }

                var exportTxId = Guid.NewGuid();
                // Notify StorageProcessManager of the archive context before the fan-out.
                _eventBus.PublishManaged(new ExportArchiveBegunEvent
                {
                    TransactionId    = exportTxId,
                    ArchiveRequestId = intent.RequestId,
                    Cts              = exportCts,
                });
                FanOutSerializeLocal(exportTxId, exportNodeIds, new ArchiveHandlerPayload(intent.ExerciseId));
                PublishOpStatus(intent.RequestId, OrchestrationStatusCode.InProgress);
                break;
            }

            case StorageOpType.Import:
            {
                if (intent.ExerciseId == Guid.Empty)
                {
                    FdpLog<ClusterMaster>.Warn("[Orchestrator] ImportArchive missing ExerciseId — rejected (requestId={0}).", intent.RequestId);
                    PublishOpStatus(intent.RequestId, OrchestrationStatusCode.Rejected);
                    return;
                }

                var importCts     = new CancellationTokenSource();
                _activeCancellations[intent.RequestId] = importCts;

                var importTargets = BuildNodeDistributionTargetsForExercise(intent.ExerciseId);

                // Delegate the NAS prefetch to StorageProcessManager via event bus (SRP).
                _eventBus.PublishManaged(new ImportArchiveBegunEvent
                {
                    RequestId  = intent.RequestId,
                    ExerciseId = intent.ExerciseId.ToString(),
                    Targets    = importTargets,
                    Cts        = importCts,
                });

                PublishOpStatus(intent.RequestId, OrchestrationStatusCode.InProgress);
                break;
            }
        }
    }

    private void ProcessSeekReplayIntent(SeekReplayIntent intent)
    {
        // RT-008: fan-out with ACK tracker; immediate Success when roster is empty.
        // SlaveNodeSetUpdatedEvent and PauseTimeIntent are published by ReplaySeekProcessManager (TASK-T002).
        var seekNodeIds = new List<int>(_roster.ActiveNodes.Keys);
        if (seekNodeIds.Count == 0)
        {
            PublishOpStatus(intent.RequestId, OrchestrationStatusCode.Success);
            return;
        }
        var txId = Guid.NewGuid();
        FanOutNodeOp(NodeOpType.NodeReplaySeek, txId,
            new ReplaySeekPayload(intent.TargetWallTicks), seekNodeIds);
        _pendingTransactions[txId] = new GenericTransactionTracker
        {
            RequestId                       = intent.RequestId,
            Expected                        = seekNodeIds.Count,
            BroadcastClusterStateOnComplete = true,
        };
    }

    private void ProcessCancelOperationIntent(CancelOperationIntent intent)
    {
        var targetId = intent.TargetRequestId;
        if (targetId != Guid.Empty && _activeCancellations.TryGetValue(targetId, out var cancelCts))
        {
            cancelCts.Cancel();
            FdpLog<ClusterMaster>.Info("[Orchestrator] CancelOperation: cancelled operation {0}.", targetId);
        }
        else
        {
            FdpLog<ClusterMaster>.Warn("[Orchestrator] CancelOperation: no active operation found for {0}.", targetId);
        }

        if (targetId != Guid.Empty)
        {
            var cancelNodeIds = new List<int>(_roster.ActiveNodes.Keys);
            if (cancelNodeIds.Count > 0)
                FanOutNodeOp(NodeOpType.AbortTransaction, Guid.NewGuid(),
                    new AbortTransactionPayload(targetId), cancelNodeIds);
        }
    }

    // ── Egress helpers (CMC-S009) ─────────────────────────────────────────

    /// <summary>Publishes an operation status to the bus.</summary>
    private void PublishOpStatus(Guid requestId, OrchestrationStatusCode statusCode, object? resultPayload = null)
    {
        _eventBus.PublishManaged(new ClusterOpCompletedEvent
        {
            RequestId     = requestId,
            StatusCode    = statusCode,
            ResultPayload = resultPayload,
        });
    }

    /// <summary>
    /// Registers a domain-specific <see cref="INodeResponseAggregator"/> that is invoked
    /// when all node ACKs for a <see cref="Fdp.Toolkit.Orchestration.ClusterOpType.TransitionState"/>
    /// round have arrived, attaching its result to
    /// <see cref="ClusterOpCompletedEvent.ResultPayload"/>.
    /// </summary>
    public void RegisterAggregator(INodeResponseAggregator aggregator)
    {
        if (aggregator == null) throw new ArgumentNullException(nameof(aggregator));
        _aggregators[aggregator.TargetOp] = aggregator;
    }

    /// <summary>
    /// Runs all registered aggregators against the in-flight transaction's
    /// <see cref="DistributedTransaction.NodeResponses"/> and returns the first
    /// non-null result, or <c>null</c> if no aggregator produces a result.
    /// </summary>
    private object? TryAggregate(Guid txId, IReadOnlyDictionary<int, Dictionary<Fdp.Toolkit.Orchestration.NodeOpType, string>>? fallbackResponses = null)
    {
        var nodeResponses = (_inflightTransitionTx?.TransactionId == txId)
            ? (IReadOnlyDictionary<int, Dictionary<Fdp.Toolkit.Orchestration.NodeOpType, string>>)_inflightTransitionTx!.NodeResponses
            : fallbackResponses;

        if (nodeResponses == null) return null;

        foreach (var agg in _aggregators.Values)
        {
            var result = agg.Aggregate(nodeResponses);
            if (result != null) return result;
        }
        return null;
    }

    /// <summary>Publishes a cluster state transition to the bus.</summary>
    private void PublishClusterState(ClusterState state)
    {
        _eventBus.PublishManaged(new ClusterStateTransitionedEvent
        {
            NewStateId    = (Fdp.Toolkit.Orchestration.ClusterState)(int)state,
            SubsystemName = "Cluster",
            ExerciseId    = _activeExerciseId,
        });
        _eventBus.PublishManaged(new ClusterStateUpdateEvent
        {
            CurrentState = (Fdp.Toolkit.Orchestration.ClusterState)(int)state,
            ExerciseId   = _activeExerciseId,
        });
    }

    /// <summary>
    /// Publishes a node-level operation to each target node via the event bus (bus path)
    /// or per-node DDS writers (DDS path, legacy).
    /// </summary>
    private void FanOutNodeOp(NodeOpType operation, Guid transactionId, object? domainPayload, IEnumerable<int> targetNodeIds)
    {
        foreach (var nodeId in targetNodeIds)
        {
            _eventBus.PublishManaged(new ExecuteNodeOpIntent
            {
                TransactionId = transactionId,
                TargetNodeId  = nodeId,
                Operation     = (Fdp.Toolkit.Orchestration.NodeOpType)(int)operation,
                DomainPayload = domainPayload,
            });
        }
    }

    /// <summary>Broadcasts a node operation to all currently active nodes.</summary>
    private void BroadcastNodeOp(NodeOpType operation, Guid transactionId, object? domainPayload)
        => FanOutNodeOp(operation, transactionId, domainPayload, _roster.ActiveNodes.Keys);

    /// <summary>
    /// Sends a <see cref="NodeOpType.SerializeLocal"/> command to each node in
    /// <paramref name="nodeIds"/> and registers a pending task that waits for all
    /// <c>NodeOpStatus(Success)</c> ACKs before publishing a <see cref="ClusterOpCompletedEvent"/>
    /// with aggregated manifest payload. <see cref="StorageProcessManager"/> reacts to this
    /// event and invokes <see cref="StorageGatewayModule.PullToNasAsync"/>.
    ///
    /// <para>Intended to be called from Phase 3 <c>SysOpType.SaveScenario</c> handling
    /// in <see cref="ProcessClusterOpRequests"/>.  If no <see cref="StorageGatewayModule"/>
    /// has been registered the manifest pull step is skipped.</para>
    /// </summary>
    internal void FanOutSerializeLocal(Guid requestId, IReadOnlyList<int> nodeIds, object? domainPayload = null)
    {
        if (nodeIds.Count == 0) return;
        _pendingTransactions[requestId] = new GenericTransactionTracker
        {
            RequestId = requestId,
            Expected  = nodeIds.Count,
        };
        FanOutNodeOp(NodeOpType.SerializeLocal, requestId, domainPayload, nodeIds);
    }

    /// <summary>
    /// Reads all pending <see cref="NodeOpCompletedEvent"/> samples and routes each ACK
    /// through the unified <see cref="_pendingTransactions"/> tracker.  When a tracker's
    /// expected ACK count is satisfied, calls the registered aggregator pipeline and
    /// publishes <see cref="ClusterOpCompletedEvent"/> with the aggregated payload.
    /// </summary>
    private void ConsumeNodeOpStatuses()
    {
        foreach (var ev in _eventBus.ReadManaged<NodeOpCompletedEvent>())
        {
            // S0501: Mirror ACKs into _inflightTransitionTx.NodeResponses for history.
            if (_inflightTransitionTx != null && _inflightTransitionTx.TransactionId == ev.TransactionId)
            {
                string mirrorJson = ev.ResultPayload is null ? string.Empty
                    : ev.ResultPayload is string ms ? ms
                    : JsonSerializer.Serialize(ev.ResultPayload);
                if (!_inflightTransitionTx.NodeResponses.TryGetValue(ev.NodeId, out var txOpDict))
                {
                    txOpDict = new Dictionary<Fdp.Toolkit.Orchestration.NodeOpType, string>();
                    _inflightTransitionTx.NodeResponses[ev.NodeId] = txOpDict;
                }
                txOpDict[ev.Operation] = mirrorJson;
            }

            if (!_pendingTransactions.TryGetValue(ev.TransactionId, out var tracker))
                continue;

            // AbortOnFirstFailure: reject immediately on the first node error (ManageEpisode policy).
            if (ev.StatusCode.IsError() && tracker.AbortOnFirstFailure)
            {
                _pendingTransactions.Remove(ev.TransactionId);
                FdpLog<ClusterMaster>.Warn(
                    "[Orchestrator] 2PC transaction {0} aborted: node {1} returned error {2}.",
                    ev.TransactionId, ev.NodeId, ev.StatusCode);
                PublishOpStatus(tracker.RequestId, OrchestrationStatusCode.Rejected);
                continue;
            }

            if (ev.StatusCode.IsError())
            {
                tracker.HasFailure  = true;
                tracker.FailureCode = ev.StatusCode;
            }

            // Populate NodeResponses: use SyntheticResponseJson when set (episode context),
            // otherwise use the actual ACK payload.
            string responseJson = tracker.SyntheticResponseJson
                ?? (ev.ResultPayload is null ? string.Empty
                    : ev.ResultPayload is string s ? s
                    : JsonSerializer.Serialize(ev.ResultPayload, OrchestrationJsonOptions.Default));
            if (!tracker.NodeResponses.TryGetValue(ev.NodeId, out var opDict))
            {
                opDict = new Dictionary<Fdp.Toolkit.Orchestration.NodeOpType, string>();
                tracker.NodeResponses[ev.NodeId] = opDict;
            }
            opDict[ev.Operation] = responseJson;

            tracker.Received++;
            if (tracker.Received >= tracker.Expected)
            {
                _pendingTransactions.Remove(ev.TransactionId);

                if (tracker.HasFailure)
                {
                    FdpLog<ClusterMaster>.Error(
                        "[Orchestrator] 2PC transaction {0} completed with failures (code={1}).",
                        ev.TransactionId, tracker.FailureCode);
                    PublishOpStatus(tracker.RequestId, tracker.FailureCode);
                }
                else
                {
                    var aggregated = TryAggregate(ev.TransactionId, tracker.NodeResponses);
                    PublishOpStatus(tracker.RequestId, OrchestrationStatusCode.Success, aggregated);
                }

                if (tracker.BroadcastClusterStateOnComplete)
                    PublishClusterState(_currentDsmState);
            }
        }
    }

    /// <summary>
    /// Builds <see cref="NodeDistributionTarget"/> list for archive import: each
    /// node's destination is the per-node <c>.fdp</c> file path under its local temp root.
    /// </summary>
    private List<NodeDistributionTarget> BuildNodeDistributionTargetsForExercise(Guid exerciseId)
    {
        var exerciseIdText = exerciseId.ToString();
        var targets = new List<NodeDistributionTarget>();
        foreach (var kv in _roster.ActiveNodes)
        {
            targets.Add(new NodeDistributionTarget
            {
                NodeId          = kv.Key,
                DestinationPath = Path.Combine(
                    OrchestrationConstants.GetNodeExercisesRoot(kv.Key),
                    exerciseIdText,
                    OrchestrationConstants.GetNodeRecordingFileName(kv.Key)),
            });
        }
        return targets;
    }

    private void PublishStandby() => PublishClusterState(ClusterState.Idle);

    private void AppendToHistory(DistributedTransaction tx)
    {
        _history[_historyHead] = tx;
        _historyHead = (_historyHead + 1) % _history.Length;
    }

    private static double UtcNowSeconds() =>
        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var cts in _activeCancellations.Values)
            cts.Dispose();
        _activeCancellations.Clear();
    }
}
