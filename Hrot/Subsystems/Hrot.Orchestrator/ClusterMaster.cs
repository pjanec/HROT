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
using ClusterState  = Hrot.NED.Descriptors.Orchestration.ClusterState;
using ClusterOpType = Hrot.NED.Descriptors.Orchestration.ClusterOpType;
using NodeOpType    = Hrot.NED.Descriptors.Orchestration.NodeOpType;

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

    // ── Storage gateway (CGF1-S0301) ──────────────────────────────────────
    /// <summary>
    /// Optional storage gateway used to collect node snapshots onto the NAS after a
    /// <c>SerializeLocal</c> round.  Set via <see cref="SetStorageGateway"/>.
    /// </summary>
    private StorageGatewayModule? _gateway;
    private string _nasBasePath = string.Empty;

    // ── Asset inventory publisher (CGF1-S0506) ────────────────────────────
    private DateTime _lastInventoryScan = DateTime.MinValue;

    /// <summary>
    /// Tracks in-progress <c>SerializeLocal</c> rounds keyed by transaction ID.
    /// Each entry records the number of outstanding ACKs and the manifests collected
    /// so far.  When <c>SerializeLocalTask.RemainingAcks</c> reaches zero,
    /// <see cref="ConsumeNodeOpStatuses"/> fires <see cref="StorageGatewayModule.PullToNasAsync"/>.
    /// </summary>
    private readonly Dictionary<Guid, SerializeLocalTask> _pendingSerializeTasks = new();

    private sealed class SerializeLocalTask
    {
        public int RemainingAcks;
        public int FailureCount;
        public readonly List<FileManifestEntry> Manifests = new();

        // Archive export tracking: non-Empty => this is an ExportArchive operation.
        public Guid ArchiveRequestId = Guid.Empty;
        public CancellationTokenSource? ArchiveCts;
    }

    // ── Pending prefetch state (CGF1-S0302 / A.1) ─────────────────────────
    /// <summary>
    /// Tracks an in-flight <see cref="StorageGatewayModule.PrefetchScenarioAsync"/> task.
    /// <c>PrefetchFiles</c> is only fan-out to nodes <em>after</em> the gateway copy
    /// completes successfully, preventing nodes from running <c>LoadingEdit</c> before
    /// staging files are physically present on their local SSD.
    /// </summary>
    private sealed class PendingPrefetchOp
    {
        public Guid                    RequestId;
        public string                  ScenarioId = string.Empty;
        public Task<GatewayResult>     GatewayTask = null!;
    }

    private PendingPrefetchOp? _pendingPrefetch;

    // ── Active archive operation cancellations (CGF1-S0505) ──────────────
    /// <summary>
    /// Tracks <see cref="CancellationTokenSource"/> instances for in-progress
    /// <see cref="ClusterOpType.ExportArchive"/> and <see cref="ClusterOpType.ImportArchive"/>
    /// operations, keyed by their originating <see cref="ClusterOpRequest.RequestId"/>.
    /// Cancelled by a <see cref="ClusterOpType.CancelOperation"/> request.
    /// </summary>
    private readonly Dictionary<Guid, CancellationTokenSource> _activeCancellations = new();

    // ── Global context handler (CGF1-S0307) ──────────────────────────────
    private GlobalContextClusterOpHandler? _globalContextHandler;

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

    // ── Live-from-Replay temporal interlock (CGF1-S0305) ─────────────────
    /// <summary>
    /// Optional module that freezes / restores the cluster time scale during
    /// Live-from-Replay branch transitions.  Set via <see cref="SetReplayMasterModule"/>.
    /// </summary>
    private ReplayMasterModule? _replayMasterModule;

    /// <summary>
    /// Tracks in-progress Live-from-Replay branch fan-outs keyed by the
    /// <see cref="NodeOpCommand.TransactionId"/> broadcast to nodes.  Each entry
    /// holds the number of outstanding ACKs; when <c>RemainingAcks</c> reaches zero
    /// <see cref="ConsumeNodeOpStatuses"/> calls <see cref="ReplayMasterModule.RestoreTime"/>.
    /// </summary>
    private readonly Dictionary<Guid, BranchTransitionTask> _pendingBranchTasks = new();

    private sealed class BranchTransitionTask
    {
        public int  RemainingAcks;
        public Guid RequestId;  // for bus-mode: publish ClusterOpStatus(Success) when branch ACKs complete
    }

    // ── Episode 2PC (CGF1-S0308 / BATCH-21 Part A.1) ───────────────────────
    /// <summary>
    /// Tracks an in-progress <see cref="ClusterOpType.ManageEpisode"/> fan-out waiting for
    /// all targeted nodes to ACK with <see cref="NodeOpStatus"/>.
    /// <para>
    /// Policy: every ACK — whether <c>IsParticipating == true</c> or <c>false</c> —
    /// counts as the node's response.  When <see cref="RemainingNodeIds"/> is empty
    /// the episode set is updated and the operation is considered complete.
    /// </para>
    /// </summary>
    private sealed class ManageEpisodeTask
    {
        public Guid        RequestId;
        public bool        IsStart;
        public Guid        EpisodeId;
        /// <summary>Node IDs that still owe an ACK for this transaction.</summary>
        public HashSet<int> RemainingNodeIds = new();
    }

    /// <summary>
    /// Active ManageEpisode fan-outs keyed by <see cref="NodeOpCommand.TransactionId"/>.
    /// Entries are removed once all targeted nodes have ACKed (see
    /// <see cref="ConsumeNodeOpStatuses"/>).
    /// </summary>
    private readonly Dictionary<Guid, ManageEpisodeTask> _pendingManageEpisodeTasks = new();

    // ── Bus-mode 2PC ACK tracking (CMC-S016 / BATCH-06) ─────────────────────
    /// <summary>
    /// Tracks in-flight <see cref="ClusterOpType.TransitionState"/> 2PC rounds in bus mode.
    /// Removed once all expected <see cref="NodeOpCompletedEvent"/> ACKs arrive and a
    /// <see cref="ClusterOpCompletedEvent"/> is published.
    /// </summary>
    private sealed class BusTransitionAckTracker
    {
        public Guid RequestId;
        public int  Expected;
        public int  Received;
        public bool HasFailure;
        public OrchestrationStatusCode  FailureCode;
    }

    private readonly Dictionary<Guid, BusTransitionAckTracker> _pendingBusTransitionAcks = new();

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
    /// with authoritative tracking (last written <see cref="SystemStateTopic.CurrentState"/>
    /// or aggregated <c>NodeOpStatus</c> confirmation) in a later stage.</para>
    /// </summary>
    private ClusterState _currentDsmState = ClusterState.Idle;

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

    // ── Active episodes (CGF1-S0308) ──────────────────────────────────────
    /// <summary>
    /// Set of episode IDs currently injected into the running exercise via
    /// <see cref="ClusterOpType.ManageEpisode"/> Start operations.  Entries are removed
    /// by corresponding Stop operations.
    /// </summary>
    private readonly HashSet<Guid> _activeEpisodes = new();

    /// <summary>
    /// Read-only view of the currently active episode IDs.
    /// Updated by <c>ManageEpisode</c> <c>ClusterOpRequest</c> processing.
    /// </summary>
    public IReadOnlyCollection<Guid> ActiveEpisodes => _activeEpisodes;

    private bool _disposed;

    // ── Public surface ────────────────────────────────────────────────────
    public NodeRoster NodeRoster => _roster;

    /// <summary><c>true</c> once all mandatory nodes have reached <c>Standby</c>.</summary>
    public bool BootstrapComplete => _bootstrapLatch;

    /// <summary>
    /// Current cluster Cluster state (optimistic — advances on accepted transitions).
    /// Exposed for UI panels (CGF1-S0106) and time-mode consumers.
    /// </summary>
    public ClusterState CurrentSystemState => _currentDsmState;

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
    /// Optional storage-gateway reference for scenarios list / NAS operations.
    /// Exposed so <c>OrchestratorScenarioPanel</c> can call
    /// <c>ListScenariosAsync()</c> (CGF1-S0106).
    /// </summary>
    public StorageGatewayModule? StorageGateway => _gateway;

    /// <summary>NAS base path configured via <see cref="SetStorageGateway"/>. Used for inventory publishing.</summary>
    public string NasBasePath => _nasBasePath;

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
        DrainPendingPrefetch();
        DrainInjectedRequests();

        // Bus-based intent drain (CMC-S008).
        ProcessTransitionStateIntents();
        ProcessManageEpisodeIntents();
        ProcessStorageOpIntents();
        ProcessTakeCheckpointIntents();
        ProcessSeekReplayIntents();
        ProcessCancelOperationIntents();

        ConsumeNodeOpStatuses();

        if ((DateTime.UtcNow - _lastInventoryScan).TotalSeconds >= 5.0)
        {
            PublishAssetInventory();
            _lastInventoryScan = DateTime.UtcNow;
        }
    }

    // ── UI / test injection path ──────────────────────────────────────────

    private readonly System.Collections.Concurrent.ConcurrentQueue<ClusterOpRequest>
        _injectedRequests = new();

    // ── Asset inventory publisher (CGF1-S0506) ────────────────────────────

    private void PublishAssetInventory()
    {
        if (_gateway == null) return;

        var localScenarios    = _gateway.ScanLocalScenarios(_nasBasePath);
        var localExercises    = _gateway.ScanLocalExercises(_nasBasePath);
        var archivedExercises = _gateway.ScanNasExercises(_nasBasePath);
        var unarchived        = localExercises.Except(archivedExercises).ToArray();

        _eventBus.PublishManaged(new AssetInventoryUpdateEvent
        {
            LocalScenarios           = localScenarios.ToArray(),
            LocalExercises           = localExercises.ToArray(),
            ArchivedExercises        = archivedExercises.ToArray(),
            UnarchivedLocalExercises = unarchived,
        });
    }

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
                    float delta = ParseStepDelta(ClusterOpRequestAdapter.GetPayloadString(req), 1f / 60f);
                    _eventBus.PublishManaged(new StepTimeIntent { DeltaSeconds = delta });
                    break;
                }
                case ClusterOpType.SetTimeScale:
                {
                    float scale = ParseTimeScale(ClusterOpRequestAdapter.GetPayloadString(req), 1f);
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
                _pendingBusTransitionAcks[txId] = new BusTransitionAckTracker
                {
                    RequestId = req.RequestId,
                    Expected  = nodeIds.Count,
                };
                break;
            }

            case ClusterOpType.ReplaySeek:
                ProcessSeekReplayIntent(ClusterOpRequestAdapter.ToSeekReplayIntent(req));
                PublishOpStatus(req.RequestId, OrchestrationStatusCode.Success);
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
            _pendingBusTransitionAcks[ckTxId] = new BusTransitionAckTracker
            {
                RequestId = intent.RequestId,
                Expected  = ckNodeIds.Count,
            };
        }
    }

    private void ProcessSeekReplayIntents()
    {
        foreach (var intent in _eventBus.ReadManaged<SeekReplayIntent>())
        {
            ProcessSeekReplayIntent(intent);
            PublishOpStatus(intent.RequestId, OrchestrationStatusCode.Success);
        }
    }

    private void ProcessCancelOperationIntents()
    {
        foreach (var intent in _eventBus.ReadManaged<CancelOperationIntent>())
            ProcessCancelOperationIntent(intent);
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
        var capturedSourceState = _currentDsmState;
        _currentDsmState = resolvedTarget;

        // CGF1-S0302: Start async prefetch immediately; PrefetchFiles fan-out is deferred.
        foreach (var step in trajectory)
        {
            if (step is OperationStep { Operation: ClusterOpType.PrefetchScenario } ps)
            {
                ExecutePrefetchScenario((string?)ps.DomainPayload ?? string.Empty, requestId);
                break;
            }
        }

        // CGF1-S0205: Capture TimeMode when trajectory passes through LoadingLive.
        bool passesLoadingLive = trajectory.OfType<TransitionStep>()
            .Any(ts => ts.TargetState == ClusterState.LoadingLive);

        if (passesLoadingLive && !string.IsNullOrWhiteSpace(intent.TimeMode))
            PendingTimeMode = intent.TimeMode;
        if (resolvedTarget == ClusterState.Idle)
            PendingTimeMode = null;

        // CGF1-S0305: Live-from-Replay temporal interlock.
        bool isLiveFromReplayBranch = false;
        if (passesLoadingLive && stateBeforeAdvance == ClusterState.OperatingReplay)
        {
            isLiveFromReplayBranch = true;
            _replayMasterModule?.FreezeTime();

            var branchedExerciseId = Guid.TryParse(intent.ExerciseId, out var parsedBranchId)
                ? parsedBranchId
                : Guid.NewGuid();
            var branchTxId    = Guid.NewGuid();
            var branchNodeIds = new List<int>(_roster.ActiveNodes.Keys);

            if (branchNodeIds.Count > 0)
            {
                _pendingBranchTasks[branchTxId] = new BranchTransitionTask { RemainingAcks = branchNodeIds.Count, RequestId = requestId };
                FanOutNodeOp(NodeOpType.PrepareLive, branchTxId, branchedExerciseId, branchNodeIds);
                FdpLog<ClusterMaster>.Info(
                    "[Orchestrator] S0305: Live-from-Replay branch — time frozen, " +
                    "PrepareLive fan-out (branchedExerciseId={0}, nodes={1}).",
                    branchedExerciseId, branchNodeIds.Count);
            }
            else
            {
                _replayMasterModule?.RestoreTime();
                FdpLog<ClusterMaster>.Warn(
                    "[Orchestrator] S0305: Live-from-Replay branch with zero active nodes — time restored immediately.");
            }
        }

        var tx = new DistributedTransaction
        {
            TransactionId   = Guid.NewGuid(),
            OriginRequestId = requestId,
            TargetDsmState  = resolvedTarget,
            TotalSteps      = totalSteps,
            CompletedSteps  = totalSteps,
            IsAborted       = false,
            SourceDsmState  = capturedSourceState,
            PayloadJson     = JsonSerializer.Serialize(intent, new JsonSerializerOptions
            {
                IncludeFields = true,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            }),
        };
        _activeTransaction    = tx;
        _inflightTransitionTx = tx;
        AppendToHistory(tx);

        // S0502: Fan out PrepareXxx + CommitState to all active nodes.
        var activeNodeIds = new List<int>(_roster.ActiveNodes.Keys);
        if (!isLiveFromReplayBranch)
        {
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
                            (int)tStep.TargetState,
                            ExerciseId: intent.ExerciseId);

                        FanOutNodeOp(prepareOp,             tx.TransactionId, preparePayload,              activeNodeIds);
                        FanOutNodeOp(NodeOpType.CommitState, tx.TransactionId,
                            new CommitStatePayload((int)tStep.TargetState), activeNodeIds);

                        // CGF1-S0307: Invoke local context handler for load transitions.
                        if (_globalContextHandler != null &&
                            (tStep.TargetState == ClusterState.LoadingLive ||
                             tStep.TargetState == ClusterState.LoadingEdit))
                        {
                            var localPayload = !string.IsNullOrEmpty(intent.ScenarioId)
                                ? JsonSerializer.Serialize(new { TargetState = (int)tStep.TargetState, ScenarioId = intent.ScenarioId })
                                : ((int)tStep.TargetState).ToString();
                            _globalContextHandler.Commit(
                                ClusterNodeOpBuilder.LocalContextCmd(NodeOpType.CommitState, tx.TransactionId, localPayload), null);
                        }
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
        }

        // ── Bus-mode 2PC ACK tracking (CMC-S016 / BATCH-06) ─────────────────
        if (_eventBus != null)
        {
            // Count expected ACKs: one per PrepareXxx TransitionStep per active node.
            // CommitState is handled in-slave synchronously and does NOT publish ACK.
            // For live-from-replay branch, the branch ACKs are tracked via _pendingBranchTasks
            // using branchTxId; the main tx has no fan-out so expectedAcks = 0.
            int prepSteps    = trajectory.OfType<TransitionStep>().Count();
            int expectedAcks = isLiveFromReplayBranch ? 0 : (prepSteps * activeNodeIds.Count);
            if (expectedAcks > 0)
            {
                _pendingBusTransitionAcks[tx.TransactionId] = new BusTransitionAckTracker
                {
                    RequestId = requestId,
                    Expected  = expectedAcks,
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
                if (_gateway != null)
                    ExecutePrefetchScenario((string?)prefetch.DomainPayload ?? string.Empty, requestId);
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
                    _pendingManageEpisodeTasks[txId] = new ManageEpisodeTask
                    {
                        RequestId        = requestId,
                        IsStart          = intent.IsStart,
                        EpisodeId        = intent.EpisodeId,
                        RemainingNodeIds = new HashSet<int>(nodeIds),
                    };
                }
                else
                {
                    if (intent.IsStart) _activeEpisodes.Add(intent.EpisodeId);
                    else                _activeEpisodes.Remove(intent.EpisodeId);
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

                if (_globalContextHandler != null)
                {
                    var localExercisePayload = intent.ExerciseId != null ? JsonSerializer.Serialize(new { ExerciseId = intent.ExerciseId }) : string.Empty;
                    var localCmd = ClusterNodeOpBuilder.LocalContextCmd(NodeOpType.SerializeLocal, txId, localExercisePayload);
                    _ = _globalContextHandler.PrepareAsync(localCmd, System.Threading.CancellationToken.None)
                        .ContinueWith(t =>
                        {
                            if (!t.IsFaulted)
                                _globalContextHandler.Commit(localCmd, null);
                        }, System.Threading.Tasks.TaskScheduler.Default);
                }

                FdpLog<ClusterMaster>.Info("[Orchestrator] SaveScenario → SerializeLocal fan-out to {0} node(s).", nodeIds.Count);
                break;
            }

            case StorageOpType.Export:
            {
                if (string.IsNullOrWhiteSpace(intent.ExerciseId))
                {
                    FdpLog<ClusterMaster>.Warn("[Orchestrator] ExportArchive missing ExerciseId — rejected (requestId={0}).", intent.RequestId);
                    PublishOpStatus(intent.RequestId, OrchestrationStatusCode.Rejected);
                    return;
                }

                var exportCts = new CancellationTokenSource();
                _activeCancellations[intent.RequestId] = exportCts;

                var exportNodeIds = new List<int>(_roster.ActiveNodes.Keys);
                var exportTxId    = Guid.NewGuid();
                FanOutSerializeLocal(exportTxId, exportNodeIds, new ArchiveHandlerPayload(intent.ExerciseId));

                if (_pendingSerializeTasks.TryGetValue(exportTxId, out var archTask))
                {
                    archTask.ArchiveRequestId = intent.RequestId;
                    archTask.ArchiveCts       = exportCts;
                }
                else if (exportNodeIds.Count == 0)
                {
                    _activeCancellations.Remove(intent.RequestId);
                    exportCts.Dispose();
                    PublishOpStatus(intent.RequestId, OrchestrationStatusCode.Success);
                }

                PublishOpStatus(intent.RequestId, OrchestrationStatusCode.InProgress);
                break;
            }

            case StorageOpType.Import:
            {
                if (string.IsNullOrWhiteSpace(intent.ExerciseId))
                {
                    FdpLog<ClusterMaster>.Warn("[Orchestrator] ImportArchive missing ExerciseId — rejected (requestId={0}).", intent.RequestId);
                    PublishOpStatus(intent.RequestId, OrchestrationStatusCode.Rejected);
                    return;
                }

                var importCts       = new CancellationTokenSource();
                _activeCancellations[intent.RequestId] = importCts;

                var importTargets   = BuildNodeDistributionTargetsForExercise(intent.ExerciseId);
                var importRequestId = intent.RequestId;

                if (_gateway != null)
                {
                    _ = _gateway.PrefetchArchiveAsync(intent.ExerciseId, importTargets, _nasBasePath, importCts.Token)
                        .ContinueWith(t =>
                        {
                            _activeCancellations.Remove(importRequestId);
                            if (t.IsCanceled)
                                PublishOpStatus(importRequestId, OrchestrationStatusCode.Rejected);
                            else if (t.IsFaulted)
                            {
                                FdpLog<ClusterMaster>.Error("[Orchestrator] ImportArchive gateway error: {0}",
                                    t.Exception?.GetBaseException().Message!);
                                PublishOpStatus(importRequestId, OrchestrationStatusCode.Rejected);
                            }
                            else
                                PublishOpStatus(importRequestId, OrchestrationStatusCode.Success);
                        }, System.Threading.Tasks.TaskScheduler.Default);
                }
                else
                {
                    _activeCancellations.Remove(intent.RequestId);
                    importCts.Dispose();
                    PublishOpStatus(intent.RequestId, OrchestrationStatusCode.Success);
                }

                PublishOpStatus(intent.RequestId, OrchestrationStatusCode.InProgress);
                break;
            }
        }
    }

    private void ProcessSeekReplayIntent(SeekReplayIntent intent)
    {
        var seekNodeIds = new List<int>(_roster.ActiveNodes.Keys);
        if (seekNodeIds.Count > 0)
            FanOutNodeOp(NodeOpType.NodeReplaySeek, Guid.NewGuid(),
            new ReplaySeekPayload(intent.TargetWallTicks), seekNodeIds);
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
    private void PublishOpStatus(Guid requestId, OrchestrationStatusCode statusCode)
    {
        _eventBus.PublishManaged(new ClusterOpCompletedEvent
        {
            RequestId     = requestId,
            StatusCode    = statusCode,
            ResultPayload = null,
        });
    }

    /// <summary>Publishes a cluster state transition to the bus.</summary>
    private void PublishClusterState(ClusterState state)
    {
        _eventBus.PublishManaged(new ClusterStateTransitionedEvent
        {
            NewStateId    = (Fdp.Toolkit.Orchestration.ClusterState)(int)state,
            SubsystemName = "Cluster",
        });
        _eventBus.PublishManaged(new SystemStateUpdateEvent
        {
            CurrentState = (Fdp.Toolkit.Orchestration.ClusterState)(int)state,
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

    // ── Storage gateway integration (CGF1-S0301) ─────────────────────────

    /// <summary>
    /// Registers the <see cref="StorageGatewayModule"/> instance used to collect node
    /// snapshots onto the NAS after every <c>SerializeLocal</c> round.
    /// Call this once at startup, before any simulation transitions are issued.
    /// </summary>
    /// <param name="gateway">The storage gateway instance to use.</param>
    /// <param name="nasBasePath">
    /// Root path on the NAS (local or UNC) under which manifest
    /// <see cref="FileManifestEntry.RelativeDest"/> paths are resolved.
    /// </param>
    public void SetStorageGateway(StorageGatewayModule gateway, string nasBasePath)
    {
        _gateway     = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _nasBasePath = nasBasePath ?? throw new ArgumentNullException(nameof(nasBasePath));
    }

    /// <summary>
    /// Registers the <see cref="GlobalContextClusterOpHandler"/> that serializes/restores the
    /// Orchestrator's own global context during scenario save/load operations.
    /// Call once at startup, before any scenario operations are issued.
    /// </summary>
    public void SetGlobalContextHandler(GlobalContextClusterOpHandler handler)
    {
        _globalContextHandler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    /// <summary>
    /// Registers the <see cref="ReplayMasterModule"/> used to freeze and restore
    /// the cluster time scale during Live-from-Replay branch transitions (CGF1-S0305).
    /// Call once at startup, before any simulation transitions are issued.
    /// </summary>
    /// <param name="module">The replay master module instance to register.</param>
    public void SetReplayMasterModule(ReplayMasterModule module)
    {
        _replayMasterModule = module ?? throw new ArgumentNullException(nameof(module));
    }

    /// <summary>
    /// Sends a <see cref="NodeOpType.SerializeLocal"/> command to each node in
    /// <paramref name="nodeIds"/> and registers a pending task that waits for all
    /// <c>NodeOpStatus(Success)</c> ACKs before invoking
    /// <see cref="StorageGatewayModule.PullToNasAsync"/> on the collected manifests.
    ///
    /// <para>Intended to be called from Phase 3 <c>SysOpType.SaveScenario</c> handling
    /// in <see cref="ProcessClusterOpRequests"/>.  If no <see cref="StorageGatewayModule"/>
    /// has been registered the manifest pull step is skipped.</para>
    /// </summary>
    internal void FanOutSerializeLocal(Guid requestId, IReadOnlyList<int> nodeIds, object? domainPayload = null)
    {
        if (nodeIds.Count == 0) return;
        _pendingSerializeTasks[requestId] = new SerializeLocalTask { RemainingAcks = nodeIds.Count };
        FanOutNodeOp(NodeOpType.SerializeLocal, requestId, domainPayload, nodeIds);
    }

    /// <summary>
    /// Reads all pending <see cref="NodeOpStatus"/> samples.  For each sample that
    /// matches a tracked <c>SerializeLocal</c> task, deserializes the
    /// <see cref="FileManifestEntry"/> list from <c>ResultJson</c> and decrements the
    /// outstanding ACK counter.  When the counter reaches zero the full collected
    /// manifest is passed to <see cref="StorageGatewayModule.PullToNasAsync"/> if a
    /// gateway is registered.
    /// </summary>
    private void ConsumeNodeOpStatuses()
    {
        foreach (var ev in _eventBus.ReadManaged<NodeOpCompletedEvent>())
        {
            // CGF1-S0305: Branch-transition ACK.
            if (_pendingBranchTasks.TryGetValue(ev.TransactionId, out var branchTask))
            {
                branchTask.RemainingAcks--;
                if (branchTask.RemainingAcks <= 0)
                {
                    _pendingBranchTasks.Remove(ev.TransactionId);
                    _replayMasterModule?.RestoreTime();
                    PublishOpStatus(branchTask.RequestId, OrchestrationStatusCode.Success);
                    FdpLog<ClusterMaster>.Info(
                        "[Orchestrator] S0305 (bus): All branch ACKs received — time scale restored.");
                }
                continue;
            }

            // Transition ACK handling.
            if (_pendingBusTransitionAcks.TryGetValue(ev.TransactionId, out var tracker))
            {
                if (ev.StatusCode.IsError())
                {
                    tracker.HasFailure  = true;
                    tracker.FailureCode = ev.StatusCode;
                }
                tracker.Received++;
                if (tracker.Received >= tracker.Expected)
                {
                    _pendingBusTransitionAcks.Remove(ev.TransactionId);
                    PublishOpStatus(tracker.RequestId,
                        tracker.HasFailure ? tracker.FailureCode : OrchestrationStatusCode.Success);

                // Broadcast the new cluster state across the bus so UI panels update
                PublishClusterState(_currentDsmState);                }
                continue;
            }

            // ManageEpisode 2PC ACK.
            if (_pendingManageEpisodeTasks.TryGetValue(ev.TransactionId, out var episodeTask))
            {
                if (ev.StatusCode.IsError())
                {
                    _pendingManageEpisodeTasks.Remove(ev.TransactionId);
                    FdpLog<ClusterMaster>.Warn(
                        "[Orchestrator] ManageEpisode 2PC aborted for episode {0}: node {1} returned error {2}.",
                        episodeTask.EpisodeId, ev.NodeId, ev.StatusCode);
                    PublishOpStatus(episodeTask.RequestId, OrchestrationStatusCode.Rejected);
                    continue;
                }

                episodeTask.RemainingNodeIds.Remove(ev.NodeId);
                if (episodeTask.RemainingNodeIds.Count == 0)
                {
                    _pendingManageEpisodeTasks.Remove(ev.TransactionId);
                    if (episodeTask.IsStart) _activeEpisodes.Add(episodeTask.EpisodeId);
                    else                     _activeEpisodes.Remove(episodeTask.EpisodeId);
                    PublishOpStatus(episodeTask.RequestId, OrchestrationStatusCode.Success);
                    FdpLog<ClusterMaster>.Info(
                        "[Orchestrator] ManageEpisode 2PC complete for episode {0}: all node ACKs received.",
                        episodeTask.EpisodeId);
                }
                continue;
            }

            // S0501: Record per-node responses for the active transition transaction.
            if (_inflightTransitionTx != null && _inflightTransitionTx.TransactionId == ev.TransactionId)
            {
                string payloadStr = ev.ResultPayload is null ? string.Empty
                    : ev.ResultPayload is string s ? s
                    : JsonSerializer.Serialize(ev.ResultPayload);

                if (!_inflightTransitionTx.NodeResponses.TryGetValue(ev.NodeId, out var opDict))
                {
                    opDict = new Dictionary<Fdp.Toolkit.Orchestration.NodeOpType, string>();
                    _inflightTransitionTx.NodeResponses[ev.NodeId] = opDict;
                }
                opDict[ev.Operation] = payloadStr;
            }

            // SerializeLocal ACK handling.
            if (ev.Operation == Fdp.Toolkit.Orchestration.NodeOpType.SerializeLocal &&
                _pendingSerializeTasks.TryGetValue(ev.TransactionId, out var serTask))
            {
                if (!ev.StatusCode.IsError() && ev.ResultPayload is List<FileManifestEntry> entries)
                    serTask.Manifests.AddRange(entries);
                else if (ev.StatusCode.IsError())
                    serTask.FailureCount++;

                serTask.RemainingAcks--;
                if (serTask.RemainingAcks <= 0)
                {
                    _pendingSerializeTasks.Remove(ev.TransactionId);
                    HandleSerializeLocalCompletion(serTask);
                }
            }
        }
    }

    /// <summary>
    /// Runs the post-completion logic for a <see cref="SerializeLocalTask"/> once all node ACKs
    /// have arrived: logs failures, appends the orchestrator's own manifest entry, and either
    /// triggers the archive export path or the legacy SaveScenario NAS-push path.
    /// Called from <see cref="ConsumeNodeOpStatuses"/>.
    /// </summary>
    private void HandleSerializeLocalCompletion(SerializeLocalTask task)
    {
        if (task.FailureCount > 0)
            FdpLog<ClusterMaster>.Error(
                "[Orchestrator] SaveScenario completed with {0} node(s) reporting malformed ResultJson — NAS manifest may be incomplete.",
                task.FailureCount);

        // Append the Orchestrator's own manifest entry if the local handler produced one.
        if (_globalContextHandler?.CommitManifestEntry != null)
            task.Manifests.Add(_globalContextHandler.CommitManifestEntry);

        if (task.ArchiveCts != null)
        {
            // ─── Archive export path (CGF1-S0505) ─────────────────────────────
            var archRequestId = task.ArchiveRequestId;
            var archCts       = task.ArchiveCts;
            if (_gateway != null && task.Manifests.Count > 0)
            {
                _ = _gateway.PullToNasAsync(task.Manifests, _nasBasePath, archCts.Token)
                    .ContinueWith(pullTask =>
                    {
                        _activeCancellations.Remove(archRequestId);
                        if (pullTask.IsCanceled)
                            PublishOpStatus(archRequestId, OrchestrationStatusCode.Rejected);
                        else if (pullTask.IsFaulted)
                        {
                            FdpLog<ClusterMaster>.Error("[Orchestrator] ExportArchive gateway error: {0}",
                                pullTask.Exception?.GetBaseException().Message!);
                            PublishOpStatus(archRequestId, OrchestrationStatusCode.Rejected);
                        }
                        else
                            PublishOpStatus(archRequestId, OrchestrationStatusCode.Success);
                    }, System.Threading.Tasks.TaskScheduler.Default);
            }
            else
            {
                _activeCancellations.Remove(archRequestId);
                archCts.Dispose();
                PublishOpStatus(archRequestId, OrchestrationStatusCode.Success);
            }
        }
        else if (_gateway != null && task.Manifests.Count > 0)
        {
            // ─── Legacy SaveScenario path ──────────────────────────────────────
            // Fire-and-forget: the pull is async; ClusterMaster continues ticking.
            // Phase 3+ will track completion and publish SysOpStatus(Success)/Failure.
            _ = _gateway.PullToNasAsync(task.Manifests, _nasBasePath)
                .ContinueWith(pullTask =>
                {
                    if (pullTask.IsCompletedSuccessfully)
                        _ = _gateway.WriteScenarioManifestAsync(task.Manifests, _nasBasePath);
                }, System.Threading.Tasks.TaskScheduler.Default);
        }
    }

    // ── Prefetch execution (CGF1-S0302 / A.1) ─────────────────────────────

    /// <summary>
    /// Resolves a pending prefetch: if the gateway task has completed, either fans out
    /// <see cref="NodeOpType.PrefetchFiles"/> on success or publishes
    /// <see cref="ClusterOpStatus.Failure"/> on fault / policy violation (FailureCount &gt; 0).
    /// Must be called each <see cref="Tick"/> before <see cref="ProcessClusterOpRequests"/> to
    /// ensure <c>PrefetchFiles</c> is delivered only after files are physically on-disk.
    /// </summary>
    private void DrainPendingPrefetch()
    {
        if (_pendingPrefetch == null) return;
        var op = _pendingPrefetch;
        if (!op.GatewayTask.IsCompleted) return;

        _pendingPrefetch = null;

        bool hasFault  = op.GatewayTask.IsFaulted || op.GatewayTask.IsCanceled;
        bool hasFailure = !hasFault && op.GatewayTask.Result.FailureCount > 0;

        if (hasFault || hasFailure)
        {
            var reason = hasFault
                ? op.GatewayTask.Exception?.GetBaseException().Message ?? "task faulted"
                : $"{op.GatewayTask.Result.FailureCount} file(s) failed to copy";
            FdpLog<ClusterMaster>.Error(
                "[Orchestrator] PrefetchScenario for '{0}' failed ({1}) — publishing SysOpStatus.Failure for request {2}.",
                op.ScenarioId, reason, op.RequestId);
            PublishOpStatus(op.RequestId, OrchestrationStatusCode.Timeout);
            return;
        }

        // Success — now safe to fan-out PrefetchFiles so nodes verify their staging dirs.
        FdpLog<ClusterMaster>.Info(
            "[Orchestrator] PrefetchScenario for '{0}' succeeded ({1} file(s)) — fanning out PrefetchFiles to {2} node(s).",
            op.ScenarioId, op.GatewayTask.Result.SuccessCount, _roster.ActiveNodes.Count);
        FanOutNodeOp(NodeOpType.PrefetchFiles, Guid.NewGuid(), new PrefetchHandlerPayload(op.ScenarioId), new List<int>(_roster.ActiveNodes.Keys));
    }

    /// <summary>
    /// Starts an asynchronous <see cref="StorageGatewayModule.PrefetchScenarioAsync"/>
    /// task and stores it in <see cref="_pendingPrefetch"/>.  The <see cref="NodeOpType.PrefetchFiles"/>
    /// fan-out is deferred to <see cref="DrainPendingPrefetch"/> once the copy completes,
    /// ensuring nodes never receive <c>PrefetchFiles</c> before staging files are present.
    /// </summary>
    /// <param name="scenarioId">Logical scenario identifier (sub-directory under NAS root).</param>
    /// <param name="requestId">Originating <see cref="ClusterOpRequest.RequestId"/>; used to
    /// surface <see cref="ClusterOpStatus.Failure"/> if the gateway copy fails.</param>
    /// <remarks>
    /// When no <see cref="StorageGatewayModule"/> is configured (e.g. in headless tests where
    /// scenario files are pre-staged locally), the prefetch step is skipped silently; the
    /// assumption is that the file is already present on the node's local staging directory.
    /// </remarks>
    private void ExecutePrefetchScenario(string scenarioId, Guid requestId)
    {
        if (_gateway == null)
        {
            FdpLog<ClusterMaster>.Info(
                "[Orchestrator] PrefetchScenario for '{0}' skipped — no StorageGatewayModule configured. " +
                "Assuming files are pre-staged locally.", scenarioId);
            return;
        }

        if (string.IsNullOrWhiteSpace(_nasBasePath))
        {
            FdpLog<ClusterMaster>.Info(
                "[Orchestrator] PrefetchScenario for '{0}' skipped — no NAS base path configured. " +
                "Assuming files are pre-staged locally.", scenarioId);
            return;
        }

        var targets = BuildNodeDistributionTargets(scenarioId);

        _pendingPrefetch = new PendingPrefetchOp
        {
            RequestId  = requestId,
            ScenarioId = scenarioId,
            GatewayTask = _gateway.PrefetchScenarioAsync(scenarioId, targets, _nasBasePath),
        };

        FdpLog<ClusterMaster>.Info(
            "[Orchestrator] PrefetchScenario started for '{0}' (requestId={1}); PrefetchFiles deferred until copy completes.",
            scenarioId, requestId);
    }

    /// <summary>
    /// Builds a <see cref="NodeDistributionTarget"/> list for all currently active
    /// roster nodes using the local staging directory convention
    /// <c>C:\FDP_Temp\&lt;scenarioId&gt;\</c>.
    ///
    /// <para><b>Production note:</b> Production environments that span multiple physical
    /// hosts should replace these local paths with UNC paths of the form
    /// <c>\\&lt;hostname&gt;\c$\FDP_Temp\&lt;scenarioId&gt;\</c>.  The node hostname
    /// registry is not available in <see cref="NodeHealthProfile"/> and must be resolved
    /// via a separate configuration map.</para>
    /// </summary>
    private List<NodeDistributionTarget> BuildNodeDistributionTargets(string scenarioId)
    {
        var targets = new List<NodeDistributionTarget>();
        foreach (var kv in _roster.ActiveNodes)
        {
            targets.Add(new NodeDistributionTarget
            {
                NodeId          = kv.Key,
                DestinationPath = Path.Combine(OrchestrationConstants.DefaultStagingDirectory, scenarioId),
            });
        }
        return targets;
    }

    /// <summary>
    /// Builds <see cref="NodeDistributionTarget"/> list for archive import: each
    /// node's destination is the per-node <c>.fdp</c> file path under its local temp root.
    /// </summary>
    private List<NodeDistributionTarget> BuildNodeDistributionTargetsForExercise(string exerciseId)
    {
        var targets = new List<NodeDistributionTarget>();
        foreach (var kv in _roster.ActiveNodes)
        {
            targets.Add(new NodeDistributionTarget
            {
                NodeId          = kv.Key,
                DestinationPath = Path.Combine(OrchestrationConstants.DefaultStagingDirectory, exerciseId, $"node_{kv.Key}.fdp"),
            });
        }
        return targets;
    }

    // ── Time-control payload parsers ──────────────────────────────────────

    /// <summary>
    /// Parses a FixedDelta seconds value from a StepTime payload JSON.
    /// Returns <paramref name="fallback"/> when the payload is absent, malformed, or non-positive.
    /// </summary>
    private static float ParseStepDelta(string? payload, float fallback)
    {
        if (string.IsNullOrWhiteSpace(payload)) return fallback;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("FixedDelta", out var el))
            {
                float v = el.GetSingle();
                return v > 0f ? v : fallback;
            }
        }
        catch { }
        return fallback;
    }

    /// <summary>Parses a plain float time-scale value from a SetTimeScale payload.</summary>
    private static float ParseTimeScale(string? payload, float fallback)
    {
        if (string.IsNullOrWhiteSpace(payload)) return fallback;
        if (float.TryParse(payload,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out float s) && s > 0f)
            return s;
        return fallback;
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
