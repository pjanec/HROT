using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Hrot.NED.Descriptors.Orchestration;
using CycloneDDS.Runtime;
using FDP.Kernel.Logging;
using FDP.Toolkit.Orchestration;
using ModuleHost.Network.Cyclone.Services;

namespace Hrot.Orchestrator;

/// <summary>
/// Orchestrator control-plane host: system state, node heartbeats, DDS network ID allocation server,
/// bootstrap latch, heartbeat-timeout eviction, and 2PC transaction history ring buffer.
/// </summary>
public sealed class ClusterMaster : IDisposable
{
    private readonly ClusterConfiguration _config;

    // ── DDS infrastructure ────────────────────────────────────────────────
    private readonly DdsWriter<SystemStateTopic>  _systemStateWriter;
    private readonly DdsReader<NodeHeartbeat>     _heartbeatReader;
    private readonly DdsReader<ClusterOpRequest>      _sysOpRequestReader;
    private readonly DdsWriter<ClusterOpStatus>       _sysOpStatusWriter;
    private readonly DdsReader<NodeOpStatus>      _nodeOpStatusReader;

    // Per-node writer cache (Part B: keyed NodeOpCommand fan-out).
    // Each key is the target node's roster ID; the value is the dedicated writer
    // for that instance key.  Writers are created lazily on first use and disposed
    // when the node is ejected.
    private readonly Dictionary<int, DdsWriter<NodeOpCommand>> _nodeOpWriterCache = new();
    private readonly DdsParticipant _nodeOpParticipant;

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
    private DdsWriter<AssetInventoryTopic>? _inventoryWriter;
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
    private DdsIdAllocatorServer?    _idAllocatorServer;
    private CancellationTokenSource? _idServerCts;
    private Thread? _idServerThread;

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

    private sealed class BranchTransitionTask { public int RemainingAcks; }

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
    /// in its <see cref="ClusterOpRequest.PayloadJson"/>.
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

    /// <summary>
    /// Raised for time-control operations (Pause/Resume/Step/SetTimeScale) that do
    /// not require 2PC across simulation nodes.  <see cref="OrchestratorSubsystem"/>
    /// subscribes to route these to <see cref="DistributedTimeCoordinator"/>.
    /// </summary>
    public event Action<ClusterOpType, string>? TimeControlRequested;

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

    public ClusterMaster(DdsParticipant participant)
        : this(participant, ClusterConfiguration.Default) { }

    public ClusterMaster(DdsParticipant participant, ClusterConfiguration config)
    {
        _config              = config ?? ClusterConfiguration.Default;
        _history             = new DistributedTransaction[Math.Max(1, _config.TransactionHistoryCapacity)];

        _heartbeatReader     = new DdsReader<NodeHeartbeat>(participant);
        _systemStateWriter   = new DdsWriter<SystemStateTopic>(participant);
        _sysOpRequestReader  = new DdsReader<ClusterOpRequest>(participant);
        _sysOpStatusWriter   = new DdsWriter<ClusterOpStatus>(participant);
        _nodeOpStatusReader  = new DdsReader<NodeOpStatus>(participant);
        _nodeOpParticipant   = participant;
        _inventoryWriter     = new DdsWriter<AssetInventoryTopic>(participant);

        // If no mandatory nodes are configured, the latch clears immediately.
        if (_config.Mandatory.Length == 0)
        {
            _bootstrapLatch = true;
            PublishStandby();
        }

        _idAllocatorServer = new DdsIdAllocatorServer(participant);
        _idServerCts = new CancellationTokenSource();
        _idServerThread = new Thread(() => RunIdServerLoop(_idServerCts.Token))
        {
            IsBackground = true,
            Name = "Orchestrator-IdAllocServer"
        };
        _idServerThread.Start();
    }

    // ── Per-frame tick ────────────────────────────────────────────────────

    public void Tick()
    {
        IngestHeartbeats();
        CheckBootstrapLatch();
        DetectAndEjectTimedOutNodes();
        DrainPendingPrefetch();
        DrainInjectedRequests();
        ProcessClusterOpRequests();
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
        if (_gateway == null || _inventoryWriter == null) return;

        var localScenarios = _gateway.ScanLocalScenarios(_nasBasePath);
        var localExercises    = _gateway.ScanLocalExercises(_nasBasePath);
        var archivedExercises = _gateway.ScanNasExercises(_nasBasePath);
        var unarchived     = localExercises.Except(archivedExercises).ToList();

        _inventoryWriter.Write(new AssetInventoryTopic
        {
            NodeId                    = 0,
            LocalScenariosJson        = JsonSerializer.Serialize(localScenarios),
            LocalExercisesJson           = JsonSerializer.Serialize(localExercises),
            ArchivedExercisesJson        = JsonSerializer.Serialize(archivedExercises),
            UnarchivedLocalExercisesJson = JsonSerializer.Serialize(unarchived),
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

    // ── Private helpers ───────────────────────────────────────────────────

    private void IngestHeartbeats()
    {
        using var scope = _heartbeatReader.Take();
        foreach (var sample in scope)
        {
            if (!sample.IsValid) continue;
            var hb = sample.Data;
            var profile = new NodeHealthProfile
            {
                NodeId = hb.NodeId,
                SubsystemName = hb.SubsystemName ?? string.Empty,
                LocalClusterState = hb.LocalClusterState,
                LastHeartbeatUtcSeconds = UtcNowSeconds(),
                CpuUsagePercent = hb.CpuUsagePercent,
                RamUsedBytes    = hb.RamUsedBytes,
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
                if (kv.Value.SubsystemName == name && kv.Value.LocalClusterState == ClusterState.Idle)
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
        // Dispose and remove the per-node writer so no further commands reach the ejected node.
        if (_nodeOpWriterCache.TryGetValue(nodeId, out var ejectedWriter))
        {
            ejectedWriter.Dispose();
            _nodeOpWriterCache.Remove(nodeId);
        }
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
        _systemStateWriter.Write(new SystemStateTopic
        {
            CurrentState        = ClusterState.Degraded,
            ExerciseId             = Guid.Empty,
            StateStartWallTicks = DateTimeOffset.UtcNow.Ticks,
            TransactionEpoch    = 0
        });
        FdpLog<ClusterMaster>.Warn("[Orchestrator] System entered Degraded state (mandatory node {0} lost).", profile.SubsystemName);

        // Broadcast AbortTransaction + PrepareState(Standby) to surviving nodes only.
        var survivingIds = new List<int>(_roster.ActiveNodes.Keys);
        FanOutNodeOp(new NodeOpCommand
        {
            TransactionId = Guid.NewGuid(),
            Operation     = NodeOpType.AbortTransaction,
            PayloadJson   = string.Empty
        }, survivingIds);
        FanOutNodeOp(new NodeOpCommand
        {
            TransactionId = Guid.NewGuid(),
            Operation     = NodeOpType.PrepareState,
            PayloadJson   = ((int)ClusterState.Idle).ToString()
        }, survivingIds);

        // Re-engage bootstrap latch until the mandatory node returns.
        _bootstrapLatch = false;
    }

    /// <summary>
    /// Reads all pending <see cref="ClusterOpRequest"/> messages and replies with
    /// <see cref="ClusterOpStatus"/>. While the bootstrap latch is inactive all requests
    /// are rejected with <see cref="OpStatus.Rejected"/>.
    /// </summary>
    private void ProcessClusterOpRequests()
    {
        using var scope = _sysOpRequestReader.Take();
        foreach (var sample in scope)
        {
            if (!sample.IsValid) continue;
            ProcessSingleClusterOpRequest(sample.Data);
        }
    }

    /// <summary>
    /// Processes a single <see cref="ClusterOpRequest"/> — called from both the DDS drain
    /// path (<see cref="ProcessClusterOpRequests"/>) and the UI/test injection path
    /// (<see cref="DrainInjectedRequests"/>).
    /// </summary>
    private void ProcessSingleClusterOpRequest(ClusterOpRequest req)
    {
            if (!_bootstrapLatch)
            {
                _sysOpStatusWriter.Write(new ClusterOpStatus
                {
                    RequestId  = req.RequestId,
                    StatusCode = OrchestrationStatusCode.Rejected,
                    ResultJson = string.Empty
                });
                return;
            }

            // S0503: Time-control operations bypass 2PC — route directly and return.
            if (req.OperationType is ClusterOpType.PauseTime or ClusterOpType.ResumeTime
                                  or ClusterOpType.StepTime  or ClusterOpType.SetTimeScale)
            {
                TimeControlRequested?.Invoke(req.OperationType, req.PayloadJson ?? string.Empty);
                return;
            }

            // Accept the request — resolve target via planner for TransitionState ops.
            ClusterState resolvedTarget = _currentDsmState;
            int totalSteps = 1;

            // S0501: capture source state before any mutation so it can be stored in the transaction.
            ClusterState capturedSourceState = _currentDsmState;
            // S0502: capture trajectory and branch-flag for the fan-out loop below.
            Queue<ISysOpStep>? capturedTrajectory = null;
            bool isLiveFromReplayBranch = false;

            if (req.OperationType == ClusterOpType.TransitionState)
            {
                try
                {
                    // Capture current state before optimistic advance (needed for S0305 detection).
                    var stateBeforeAdvance = _currentDsmState;

                    var trajectory = _planner.PlanTrajectory(_currentDsmState, req);
                    totalSteps         = trajectory.Count;
                    capturedTrajectory = trajectory; // S0502: capture for fan-out loop
                    // Extract the final TransitionStep target for history recording
                    // and optimistic _currentDsmState advance.
                    resolvedTarget = _currentDsmState;
                    foreach (var step in trajectory)
                    {
                        if (step is TransitionStep ts)
                            resolvedTarget = ts.TargetState;
                    }
                    // Advance optimistically so the next PlanTrajectory call starts from
                    // the intended end-state (see _currentDsmState XML docs for caveats).
                    _currentDsmState = resolvedTarget;

                    // CGF1-S0302 / A.1: Execute any PrefetchScenario step immediately so
                    // scenario files reach all nodes before the first TransitionStep runs.
                    // The gateway copy runs async; PrefetchFiles fan-out is deferred until
                    // DrainPendingPrefetch() confirms success (see Tick()).
                    foreach (var step in trajectory)
                    {
                        if (step is OperationStep { Operation: ClusterOpType.PrefetchScenario } prefetchStep)
                        {
                            ExecutePrefetchScenario(prefetchStep.PayloadJson, req.RequestId);
                            break;
                        }
                    }

                    // CGF1-S0205: If the trajectory passes through LoadingLive, check
                    // for "TimeMode": "Deterministic" in the payload and store it so
                    // the hosting subsystem (OrchestratorSubsystem) can instruct the
                    // DistributedTimeCoordinator to switch before RunningLive.
                    bool passesLoadingLive = false;
                    foreach (var step in trajectory)
                    {
                        if (step is TransitionStep ts && ts.TargetState == ClusterState.LoadingLive)
                        {
                            passesLoadingLive = true;
                            break;
                        }
                    }
                    if (passesLoadingLive && !string.IsNullOrWhiteSpace(req.PayloadJson))
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(req.PayloadJson);
                            // Only attempt property lookup when the root is a JSON object.
                            // Plain-integer payloads (e.g. legacy TransitionState) are
                            // intentionally ignored.
                            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object &&
                                doc.RootElement.TryGetProperty("TimeMode", out var timeModeEl))
                            {
                                PendingTimeMode = timeModeEl.GetString();
                            }
                        }
                        catch (JsonException)
                        {
                            // Malformed JSON — leave PendingTimeMode unchanged.
                        }
                    }
                    // Clear pending mode when transitioning back to Standby.
                    if (resolvedTarget == ClusterState.Idle)
                        PendingTimeMode = null;

                    // CGF1-S0305: Live-from-Replay temporal interlock.
                    // When the trajectory passes through LoadingLive from RunningReplay,
                    // hard-freeze time and fan out PrepareLive with a new branched ExerciseId
                    // before any node begins recording.  Time is restored once all nodes ACK.
                    if (passesLoadingLive && stateBeforeAdvance == ClusterState.OperatingReplay)
                    {
                        isLiveFromReplayBranch = true; // S0502: suppress general fan-out for this path
                        _replayMasterModule?.FreezeTime();

                        var branchedExerciseId = Guid.NewGuid();
                        var branchTxId      = Guid.NewGuid();
                        var activeNodeIds   = new List<int>(_roster.ActiveNodes.Keys);

                        if (activeNodeIds.Count > 0)
                        {
                            _pendingBranchTasks[branchTxId] = new BranchTransitionTask
                            {
                                RemainingAcks = activeNodeIds.Count
                            };
                            FanOutNodeOp(new NodeOpCommand
                            {
                                TransactionId = branchTxId,
                                Operation     = NodeOpType.PrepareLive,
                                PayloadJson   = $"{{\"ExerciseId\":\"{branchedExerciseId}\"}}"
                            }, activeNodeIds);
                            FdpLog<ClusterMaster>.Info(
                                "[Orchestrator] S0305: Live-from-Replay branch — time frozen, " +
                                "PrepareLive fan-out (branchedExerciseId={0}, nodes={1}).",
                                branchedExerciseId, activeNodeIds.Count);
                        }
                        else
                        {
                            // No nodes to wait for; restore time immediately.
                            _replayMasterModule?.RestoreTime();
                            FdpLog<ClusterMaster>.Warn(
                                "[Orchestrator] S0305: Live-from-Replay branch with zero active nodes — time restored immediately.");
                        }
                    }
                }
                catch (InvalidOperationException ex)
                {
                    FdpLog<ClusterMaster>.Warn(
                        "[Orchestrator] TransitionState request {0} rejected by planner: {1}",
                        req.RequestId, ex.Message);
                    _sysOpStatusWriter.Write(new ClusterOpStatus
                    {
                        RequestId  = req.RequestId,
                        StatusCode = OrchestrationStatusCode.Rejected + 1,  // Failure=11 (Timeout range)
                        ResultJson = string.Empty
                    });
                    return;
                }
            }
            else if (req.OperationType == ClusterOpType.SaveScenario)
            {
                // A.5 / CGF1-S0307: Fan out SerializeLocal to all active nodes so each
                // writes its own scenario file to local SSD.  ConsumeNodeOpStatuses then
                // pulls the manifests to the NAS once all ACKs arrive.
                var nodeIds = new List<int>(_roster.ActiveNodes.Keys);
                var txId    = Guid.NewGuid();
                FanOutSerializeLocal(txId, nodeIds, req.PayloadJson);

                // Orchestrator's own context — handled in-process (no DDS round-trip).
                if (_globalContextHandler != null)
                {
                    var localCmd = new NodeOpCommand
                    {
                        TransactionId = txId,
                        Operation     = NodeOpType.SerializeLocal,
                        PayloadJson   = req.PayloadJson ?? string.Empty,
                    };
                    // PrepareAsync + Commit are lightweight / synchronous by convention;
                    // fire-and-forget from the tick thread (errors are logged inside handler).
                    _ = _globalContextHandler.PrepareAsync(localCmd, System.Threading.CancellationToken.None)
                        .ContinueWith(t =>
                        {
                            if (!t.IsFaulted)
                                _globalContextHandler.Commit(localCmd, null);
                        }, System.Threading.Tasks.TaskScheduler.Default);
                }

                FdpLog<ClusterMaster>.Info(
                    "[Orchestrator] SaveScenario request {0} → SerializeLocal fan-out to {1} node(s).",
                    req.RequestId, nodeIds.Count);
            }
            else if (req.OperationType == ClusterOpType.ManageEpisode)
            {
                // CGF1-S0308: validate state, plan episode steps, fan out to nodes.
                try
                {
                    var episodeSteps = _planner.PlanManageEpisode(_currentDsmState, req);
                    totalSteps     = episodeSteps.Count;

                    // Parse Mode and EpisodeId from the payload for episode set maintenance.
                    string? episodeMode = null;
                    Guid    episodeId   = Guid.Empty;
                    if (!string.IsNullOrWhiteSpace(req.PayloadJson))
                    {
                        try
                        {
                            using var sd = JsonDocument.Parse(req.PayloadJson);
                            if (sd.RootElement.TryGetProperty("Mode",    out var mp)) episodeMode = mp.GetString();
                            if (sd.RootElement.TryGetProperty("EpisodeId", out var sp)) Guid.TryParse(sp.GetString(), out episodeId);
                        }
                        catch (JsonException) { }
                    }

                    // BATCH-22 A.2: Fail loud on unparseable or incomplete ManageEpisode payload.
                    // Reject the request rather than issuing a silent FanOutNodeOp with no
                    // pending task tracking (orphan node ops).
                    if (episodeId == Guid.Empty)
                        throw new InvalidOperationException(
                            $"[Orchestrator] ManageEpisode payload is missing or has an invalid 'EpisodeId' field. " +
                            $"PayloadJson='{req.PayloadJson}'.");
                    if (string.IsNullOrWhiteSpace(episodeMode))
                        throw new InvalidOperationException(
                            $"[Orchestrator] ManageEpisode payload is missing or has an invalid 'Mode' field. " +
                            $"PayloadJson='{req.PayloadJson}'.");

                    // Execute the planned episode steps.
                    foreach (var step in episodeSteps)
                    {
                        if (step is OperationStep { Operation: ClusterOpType.PrefetchScenario } prefetch)
                        {
                            // Skip silently when no storage gateway is present (e.g. unit tests).
                            if (_gateway != null)
                                ExecutePrefetchScenario(prefetch.PayloadJson, req.RequestId);
                        }
                        else if (step is OperationStep { Operation: ClusterOpType.ManageEpisode } manageStep)
                        {
                            // Determine NodeOpType from mode.
                            bool isStart = string.Equals(episodeMode, "Start", StringComparison.OrdinalIgnoreCase);
                            var nodeOp   = isStart ? NodeOpType.StartEpisode : NodeOpType.StopEpisode;

                            var txId    = Guid.NewGuid();
                            var nodeIds = new List<int>(_roster.ActiveNodes.Keys);
                            FanOutNodeOp(new NodeOpCommand
                            {
                                TransactionId = txId,
                                Operation     = nodeOp,
                                PayloadJson   = manageStep.PayloadJson,
                            }, nodeIds);

                            // 2PC (BATCH-21 Part A.1): defer _activeEpisodes update until all
                            // targeted nodes have ack-ed.  When there are no nodes, update
                            // immediately (no ACKs will arrive).
                            if (episodeId != Guid.Empty)
                            {
                                if (nodeIds.Count > 0)
                                {
                                    _pendingManageEpisodeTasks[txId] = new ManageEpisodeTask
                                    {
                                        RequestId        = req.RequestId,
                                        IsStart          = isStart,
                                        EpisodeId          = episodeId,
                                        RemainingNodeIds = new HashSet<int>(nodeIds),
                                    };
                                }
                                else
                                {
                                    // No targeted nodes — update immediately.
                                    if (isStart) _activeEpisodes.Add(episodeId);
                                    else         _activeEpisodes.Remove(episodeId);
                                }
                            }

                            FdpLog<ClusterMaster>.Info(
                                "[Orchestrator] ManageEpisode {0}: episode {1} → {2} to {3} node(s).",
                                episodeMode ?? "?", episodeId, nodeOp, nodeIds.Count);
                        }
                    }
                }
                catch (InvalidOperationException ex)
                {
                    FdpLog<ClusterMaster>.Warn(
                        "[Orchestrator] ManageEpisode request {0} rejected: {1}",
                        req.RequestId, ex.Message);
                    _sysOpStatusWriter.Write(new ClusterOpStatus
                    {
                        RequestId  = req.RequestId,
                        StatusCode = OrchestrationStatusCode.Rejected,
                        ResultJson = string.Empty
                    });
                    return;
                }
            }
            else if (req.OperationType == ClusterOpType.ExportArchive)
            {
                // CGF1-S0505: Fan out SerializeLocal to all nodes to write .fdp archives,
                // then pull manifests to NAS when all ACKs arrive.
                string? exportExerciseId = ParsePayloadString(req.PayloadJson, "ExerciseId");
                if (exportExerciseId is null)
                {
                    FdpLog<ClusterMaster>.Warn("[Orchestrator] ExportArchive request {0} missing ExerciseId — rejected.", req.RequestId);
                    _sysOpStatusWriter.Write(new ClusterOpStatus
                    {
                        RequestId  = req.RequestId,
                        StatusCode = OrchestrationStatusCode.Rejected,
                        ResultJson = string.Empty
                    });
                    return;
                }

                var exportCts    = new CancellationTokenSource();
                _activeCancellations[req.RequestId] = exportCts;

                var exportNodeIds = new List<int>(_roster.ActiveNodes.Keys);
                var exportTxId    = Guid.NewGuid();
                FanOutSerializeLocal(exportTxId, exportNodeIds, req.PayloadJson ?? string.Empty);

                // Mark the pending task as an archive export so ConsumeNodeOpStatuses
                // applies the gateway pull with CT and publishes the final SysOpStatus.
                if (_pendingSerializeTasks.TryGetValue(exportTxId, out var archTask))
                {
                    archTask.ArchiveRequestId = req.RequestId;
                    archTask.ArchiveCts       = exportCts;
                }
                else if (exportNodeIds.Count == 0)
                {
                    // No nodes — complete immediately.
                    _activeCancellations.Remove(req.RequestId);
                    exportCts.Dispose();
                    _sysOpStatusWriter.Write(new ClusterOpStatus
                    {
                        RequestId  = req.RequestId,
                        StatusCode = OrchestrationStatusCode.Success,
                        ResultJson = string.Empty
                    });
                }

                _sysOpStatusWriter.Write(new ClusterOpStatus
                {
                    RequestId  = req.RequestId,
                    StatusCode = OrchestrationStatusCode.InProgress,
                    ResultJson = string.Empty
                });
                return;   // skip generic transaction creation below
            }
            else if (req.OperationType == ClusterOpType.ImportArchive)
            {
                // CGF1-S0505: Fetch per-node .fdp archives from NAS and distribute to nodes.
                string? importExerciseId = ParsePayloadString(req.PayloadJson, "ExerciseId");
                if (importExerciseId is null)
                {
                    FdpLog<ClusterMaster>.Warn("[Orchestrator] ImportArchive request {0} missing ExerciseId — rejected.", req.RequestId);
                    _sysOpStatusWriter.Write(new ClusterOpStatus
                    {
                        RequestId  = req.RequestId,
                        StatusCode = OrchestrationStatusCode.Rejected,
                        ResultJson = string.Empty
                    });
                    return;
                }

                var importCts = new CancellationTokenSource();
                _activeCancellations[req.RequestId] = importCts;

                var importTargets   = BuildNodeDistributionTargetsForExercise(importExerciseId);
                var importRequestId = req.RequestId;

                if (_gateway != null)
                {
                    _ = _gateway.PrefetchArchiveAsync(importExerciseId, importTargets, _nasBasePath, importCts.Token)
                        .ContinueWith(t =>
                        {
                            _activeCancellations.Remove(importRequestId);
                            if (t.IsCanceled)
                                _sysOpStatusWriter.Write(new ClusterOpStatus
                                {
                                    RequestId  = importRequestId,
                                    StatusCode = OrchestrationStatusCode.Rejected,
                                    ResultJson = string.Empty
                                });
                            else if (t.IsFaulted)
                            {
                                FdpLog<ClusterMaster>.Error("[Orchestrator] ImportArchive gateway error: {0}",
                                    t.Exception?.GetBaseException().Message);
                                _sysOpStatusWriter.Write(new ClusterOpStatus
                                {
                                    RequestId  = importRequestId,
                                    StatusCode = OrchestrationStatusCode.Rejected,
                                    ResultJson = string.Empty
                                });
                            }
                            else
                                _sysOpStatusWriter.Write(new ClusterOpStatus
                                {
                                    RequestId  = importRequestId,
                                    StatusCode = OrchestrationStatusCode.Success,
                                    ResultJson = string.Empty
                                });
                        }, System.Threading.Tasks.TaskScheduler.Default);
                }
                else
                {
                    _activeCancellations.Remove(req.RequestId);
                    importCts.Dispose();
                    _sysOpStatusWriter.Write(new ClusterOpStatus
                    {
                        RequestId  = req.RequestId,
                        StatusCode = OrchestrationStatusCode.Success,
                        ResultJson = string.Empty
                    });
                }

                _sysOpStatusWriter.Write(new ClusterOpStatus
                {
                    RequestId  = req.RequestId,
                    StatusCode = OrchestrationStatusCode.InProgress,
                    ResultJson = string.Empty
                });
                return;   // skip generic transaction creation below
            }
            else if (req.OperationType == ClusterOpType.CancelOperation)
            {
                // CGF1-S0505: Cancel an in-progress archive operation.
                // Payload = raw GUID string of the target operation's RequestId.
                Guid targetId = Guid.Empty;
                if (!string.IsNullOrWhiteSpace(req.PayloadJson))
                    Guid.TryParse(req.PayloadJson.Trim(), out targetId);

                if (targetId != Guid.Empty && _activeCancellations.TryGetValue(targetId, out var cancelCts))
                {
                    cancelCts.Cancel();
                    // Note: removal happens lazily in the ContinueWith callbacks or via Dispose().
                    FdpLog<ClusterMaster>.Info("[Orchestrator] CancelOperation: cancelled operation {0}.", targetId);
                }
                else
                {
                    FdpLog<ClusterMaster>.Warn("[Orchestrator] CancelOperation: no active operation found for {0}.", targetId);
                }

                // Fan out AbortTransaction to all active nodes.
                if (targetId != Guid.Empty)
                {
                    var cancelNodeIds = new List<int>(_roster.ActiveNodes.Keys);
                    if (cancelNodeIds.Count > 0)
                    {
                        FanOutNodeOp(new NodeOpCommand
                        {
                            TransactionId = Guid.NewGuid(),
                            Operation     = NodeOpType.AbortTransaction,
                            PayloadJson   = targetId.ToString(),
                        }, cancelNodeIds);
                    }
                }

                return;   // No SysOpStatus reply for CancelOperation itself
            }
            else if (req.OperationType == ClusterOpType.ReplaySeek)
            {
                // Standalone ReplaySeek: fan out NodeReplaySeek to all active nodes immediately.
                var seekNodeIds = new List<int>(_roster.ActiveNodes.Keys);
                if (seekNodeIds.Count > 0)
                {
                    FanOutNodeOp(new NodeOpCommand
                    {
                        TransactionId = Guid.NewGuid(),
                        Operation     = NodeOpType.NodeReplaySeek,
                        PayloadJson   = req.PayloadJson ?? string.Empty,
                    }, seekNodeIds);
                }
            }

            var tx = new DistributedTransaction
            {
                TransactionId    = Guid.NewGuid(),
                OriginRequestId  = req.RequestId,
                TargetDsmState   = resolvedTarget,
                TotalSteps       = totalSteps,
                CompletedSteps   = totalSteps,
                IsAborted        = false,
                SourceDsmState   = capturedSourceState,       // S0501
                PayloadJson      = req.PayloadJson ?? string.Empty, // S0501
            };
            _activeTransaction     = tx;        // S0501: expose as HasInFlightTransaction / ActiveTransaction
            _inflightTransitionTx  = tx;        // S0501: used by ConsumeNodeOpStatuses for NodeResponses
            AppendToHistory(tx);

            // S0502: Fan out PrepareXxx + CommitState to all active nodes for TransitionState operations.
            // The S0305 live-from-replay path handles its own fan-out above; skip the general loop there.
            if (capturedTrajectory != null && !isLiveFromReplayBranch)
            {
                var activeNodeIds = new List<int>(_roster.ActiveNodes.Keys);
                if (activeNodeIds.Count > 0)
                {
                    foreach (var step in capturedTrajectory)
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
                                _                        => NodeOpType.PrepareState,
                            };

                            // Use tx.TransactionId for both PrepareXxx and CommitState so that
                            // ConsumeNodeOpStatuses can correlate ACKs back to the in-flight
                            // transaction and populate NodeResponses for the 2PC History UI.
                            // Deduplication on the slave now uses a compound (TransactionId,
                            // OperationId) key, so both commands are accepted without dropping.
                            FanOutNodeOp(new NodeOpCommand
                            {
                                TransactionId = tx.TransactionId,
                                Operation     = prepareOp,
                                PayloadJson   = req.PayloadJson ?? string.Empty,
                            }, activeNodeIds);

                            FanOutNodeOp(new NodeOpCommand
                            {
                                TransactionId = tx.TransactionId,
                                Operation     = NodeOpType.CommitState,
                                PayloadJson   = ((int)tStep.TargetState).ToString(),
                            }, activeNodeIds);

                            // Invoke the local context handler for load transitions so that
                            // the Orchestrator node's own Orchestrator.json is parsed and
                            // OnContextLoaded fires (CGF1-S0307 / design-talk fix).
                            if (_globalContextHandler != null &&
                                (tStep.TargetState == ClusterState.LoadingLive ||
                                 tStep.TargetState == ClusterState.LoadingEdit))
                            {
                                // Build a payload that carries both TargetState (for routing
                                // inside GlobalContextClusterOpHandler.Commit) and ScenarioId
                                // (for file-path resolution inside CommitLoad).
                                string? scenId = ParsePayloadString(req.PayloadJson, "ScenarioId");
                                string localPayload = !string.IsNullOrEmpty(scenId)
                                    ? $"{{\"TargetState\":{(int)tStep.TargetState},\"ScenarioId\":\"{scenId}\"}}"
                                    : ((int)tStep.TargetState).ToString();

                                _globalContextHandler.Commit(new NodeOpCommand
                                {
                                    TransactionId = tx.TransactionId,
                                    Operation     = NodeOpType.CommitState,
                                    PayloadJson   = localPayload,
                                }, null);
                            }
                        }
                        else if (step is OperationStep opStep &&
                                 opStep.Operation == ClusterOpType.ReplaySeek)
                        {
                            FanOutNodeOp(new NodeOpCommand
                            {
                                TransactionId = Guid.NewGuid(),
                                Operation     = NodeOpType.NodeReplaySeek,
                                PayloadJson   = opStep.PayloadJson,
                            }, activeNodeIds);
                        }
                    }

                    FdpLog<ClusterMaster>.Info(
                        "[Orchestrator] S0502: TransitionState fan-out complete " +
                        "(transaction={0}, nodes={1}).", tx.TransactionId, activeNodeIds.Count);
                }
            }

            _sysOpStatusWriter.Write(new ClusterOpStatus
            {
                RequestId  = req.RequestId,
                StatusCode = OrchestrationStatusCode.InProgress,
                ResultJson = string.Empty
            });

            // For TransitionState, commands are fully fanned out synchronously — there are
            // no ACKs tracked by ClusterMaster.  Clear _activeTransaction so that
            // HasInFlightTransaction correctly returns false between back-to-back transitions.
            if (req.OperationType == ClusterOpType.TransitionState)
                _activeTransaction = null;

            FdpLog<ClusterMaster>.Info(
                "[Orchestrator] ClusterOpRequest {0} ({1}) accepted (transaction {2}).",
                req.RequestId, req.OperationType, tx.TransactionId);
    }

    /// <summary>
    /// Writes a <see cref="NodeOpCommand"/> to each specified target node using per-node
    /// keyed writers.  Writers are created lazily and cached in
    /// <see cref="_nodeOpWriterCache"/>; they are disposed in <see cref="EjectNode"/> when
    /// a node leaves the roster, ensuring no further samples reach the evicted instance key.
    /// </summary>
    private void FanOutNodeOp(NodeOpCommand template, IEnumerable<int> targetNodeIds)
    {
        foreach (var nodeId in targetNodeIds)
        {
            if (!_nodeOpWriterCache.TryGetValue(nodeId, out var writer))
            {
                writer = new DdsWriter<NodeOpCommand>(_nodeOpParticipant);
                _nodeOpWriterCache[nodeId] = writer;
            }
            var cmd = template;
            cmd.TargetNodeId = nodeId;
            writer.Write(cmd);
        }
    }

    /// <summary>Broadcasts a <see cref="NodeOpCommand"/> to all currently active nodes.</summary>
    private void BroadcastNodeOp(NodeOpCommand cmd)
        => FanOutNodeOp(cmd, _roster.ActiveNodes.Keys);

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
    internal void FanOutSerializeLocal(Guid requestId, IReadOnlyList<int> nodeIds, string payloadJson = "")
    {
        if (nodeIds.Count == 0) return;
        _pendingSerializeTasks[requestId] = new SerializeLocalTask { RemainingAcks = nodeIds.Count };
        FanOutNodeOp(new NodeOpCommand
        {
            TransactionId = requestId,
            Operation     = NodeOpType.SerializeLocal,
            PayloadJson   = payloadJson ?? string.Empty
        }, nodeIds);
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
        using var scope = _nodeOpStatusReader.Take();
        foreach (var sample in scope)
        {
            if (!sample.IsValid) continue;
            var status = sample.Data;

            // CGF1-S0305: Branch-transition ACK — decrement and restore time on completion.
            if (_pendingBranchTasks.TryGetValue(status.TransactionId, out var branchTask))
            {
                branchTask.RemainingAcks--;
                if (branchTask.RemainingAcks <= 0)
                {
                    _pendingBranchTasks.Remove(status.TransactionId);
                    _replayMasterModule?.RestoreTime();
                    FdpLog<ClusterMaster>.Info(
                        "[Orchestrator] S0305: All branch ACKs received — time scale restored.");
                }
                continue;
            }

            // BATCH-21 Part A.1 / BATCH-22 A.1: ManageEpisode 2PC — consume node ACKs.
            // Policy: every ACK (IsParticipating true or false) counts as the node's
            // acknowledgement for the transaction.  _activeEpisodes is updated only after
            // all targeted nodes have responded, preventing silent divergence between
            // orchestrator episode state and on-node reality.
            // BATCH-22 A.1: If any node returns an error StatusCode, abort immediately —
            // do NOT update _activeEpisodes and publish SysOpStatus.Rejected.
            if (_pendingManageEpisodeTasks.TryGetValue(status.TransactionId, out var episodeTask))
            {
                if (OrchestrationStatusCode.IsError(status.StatusCode))
                {
                    _pendingManageEpisodeTasks.Remove(status.TransactionId);
                    FdpLog<ClusterMaster>.Warn(
                        "[Orchestrator] ManageEpisode 2PC aborted for episode {0}: node {1} returned error StatusCode {2}.",
                        episodeTask.EpisodeId, status.NodeId, status.StatusCode);
                    _sysOpStatusWriter.Write(new ClusterOpStatus
                    {
                        RequestId  = episodeTask.RequestId,
                        StatusCode = OrchestrationStatusCode.Rejected,
                        ResultJson = string.Empty
                    });
                    continue;
                }

                episodeTask.RemainingNodeIds.Remove(status.NodeId);
                if (episodeTask.RemainingNodeIds.Count == 0)
                {
                    _pendingManageEpisodeTasks.Remove(status.TransactionId);
                    if (episodeTask.IsStart) _activeEpisodes.Add(episodeTask.EpisodeId);
                    else                   _activeEpisodes.Remove(episodeTask.EpisodeId);
                    // BATCH-22 A.1: Publish SysOpStatus.Completed so clients can correlate
                    // the full ManageEpisode round-trip via the sys-op channel.
                    _sysOpStatusWriter.Write(new ClusterOpStatus
                    {
                        RequestId  = episodeTask.RequestId,
                        StatusCode = OrchestrationStatusCode.Success,
                        ResultJson = string.Empty
                    });
                    FdpLog<ClusterMaster>.Info(
                        "[Orchestrator] ManageEpisode 2PC complete for episode {0}: all node ACKs received.",
                        episodeTask.EpisodeId);
                }
                continue;
            }

            // S0501: Record per-node ResultJson responses for the active transition transaction.
            // This runs before the serialize-task early-exit so that transition ACKs (which are
            // NOT in _pendingSerializeTasks) still get their NodeResponses populated.
            if (_inflightTransitionTx != null && _inflightTransitionTx.TransactionId == status.TransactionId)
            {
                _inflightTransitionTx.NodeResponses[status.NodeId] = status.ResultJson ?? string.Empty;
            }

            if (!_pendingSerializeTasks.TryGetValue(status.TransactionId, out var task))
                continue;

            if (!OrchestrationStatusCode.IsError(status.StatusCode) && !string.IsNullOrEmpty(status.ResultJson))
            {
                try
                {
                    var entries = JsonSerializer.Deserialize<List<FileManifestEntry>>(
                        status.ResultJson,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (entries != null)
                        task.Manifests.AddRange(entries);
                }
                catch (JsonException)
                {
                    // Malformed ResultJson — skip manifest for this node and record the failure.
                    FdpLog<ClusterMaster>.Warn(
                        "[Orchestrator] SerializeLocal ACK from node {0} has invalid ResultJson — incrementing failure count.",
                        status.NodeId);
                    task.FailureCount++;
                }
            }

            task.RemainingAcks--;
            if (task.RemainingAcks <= 0)
            {
                _pendingSerializeTasks.Remove(status.TransactionId);

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
                                    _sysOpStatusWriter.Write(new ClusterOpStatus
                                    {
                                        RequestId  = archRequestId,
                                        StatusCode = OrchestrationStatusCode.Rejected,
                                        ResultJson = string.Empty
                                    });
                                else if (pullTask.IsFaulted)
                                {
                                    FdpLog<ClusterMaster>.Error("[Orchestrator] ExportArchive gateway error: {0}",
                                        pullTask.Exception?.GetBaseException().Message);
                                    _sysOpStatusWriter.Write(new ClusterOpStatus
                                    {
                                        RequestId  = archRequestId,
                                        StatusCode = OrchestrationStatusCode.Rejected,
                                        ResultJson = string.Empty
                                    });
                                }
                                else
                                    _sysOpStatusWriter.Write(new ClusterOpStatus
                                    {
                                        RequestId  = archRequestId,
                                        StatusCode = OrchestrationStatusCode.Success,
                                        ResultJson = string.Empty
                                    });
                            }, System.Threading.Tasks.TaskScheduler.Default);
                    }
                    else
                    {
                        _activeCancellations.Remove(archRequestId);
                        archCts.Dispose();
                        _sysOpStatusWriter.Write(new ClusterOpStatus
                        {
                            RequestId  = archRequestId,
                            StatusCode = OrchestrationStatusCode.Success,
                            ResultJson = string.Empty
                        });
                    }
                }
                else if (_gateway != null && task.Manifests.Count > 0)
                {
                    // ─── Legacy SaveScenario path ──────────────────────────────────
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
            _sysOpStatusWriter.Write(new ClusterOpStatus
            {
                RequestId  = op.RequestId,
                StatusCode = OrchestrationStatusCode.Timeout,
                ResultJson = string.Empty
            });
            return;
        }

        // Success — now safe to fan-out PrefetchFiles so nodes verify their staging dirs.
        FdpLog<ClusterMaster>.Info(
            "[Orchestrator] PrefetchScenario for '{0}' succeeded ({1} file(s)) — fanning out PrefetchFiles to {2} node(s).",
            op.ScenarioId, op.GatewayTask.Result.SuccessCount, _roster.ActiveNodes.Count);
        FanOutNodeOp(new NodeOpCommand
        {
            TransactionId = Guid.NewGuid(),
            Operation     = NodeOpType.PrefetchFiles,
            PayloadJson   = $"{{\"ScenarioId\":\"{op.ScenarioId}\"}}",
        }, new List<int>(_roster.ActiveNodes.Keys));
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
    /// <exception cref="InvalidOperationException">
    /// Thrown when no <see cref="StorageGatewayModule"/> or NAS base path has been
    /// configured — both are required for prefetch to proceed.
    /// </exception>
    private void ExecutePrefetchScenario(string scenarioId, Guid requestId)
    {
        if (_gateway == null)
            throw new InvalidOperationException(
                "[Orchestrator] PrefetchScenario step requires a StorageGatewayModule to be registered. " +
                "Call SetStorageGateway() before issuing load transitions with a ScenarioId.");

        if (string.IsNullOrWhiteSpace(_nasBasePath))
            throw new InvalidOperationException(
                "[Orchestrator] PrefetchScenario step requires a non-empty NAS base path. " +
                "Call SetStorageGateway() with a valid nasBasePath before issuing load transitions.");

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
                DestinationPath = Path.Combine(@"C:\FDP_Temp", scenarioId),
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
                DestinationPath = Path.Combine(@"C:\FDP_Temp", exerciseId, $"node_{kv.Key}.fdp"),
            });
        }
        return targets;
    }

    /// <summary>
    /// Extracts a string property value from a JSON payload; returns <c>null</c> if
    /// the payload is absent, malformed, or the property is missing.
    /// </summary>
    private static string? ParsePayloadString(string? json, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
            if (doc.RootElement.TryGetProperty(propertyName, out var el))
                return el.GetString();
        }
        catch (JsonException) { }
        return null;
    }

    private void PublishStandby()    {
        _systemStateWriter.Write(new SystemStateTopic
        {
            CurrentState        = ClusterState.Idle,
            ExerciseId             = Guid.Empty,
            StateStartWallTicks = 0,
            TransactionEpoch    = 0
        });
    }

    private void AppendToHistory(DistributedTransaction tx)
    {
        _history[_historyHead] = tx;
        _historyHead = (_historyHead + 1) % _history.Length;
    }

    private static double UtcNowSeconds() =>
        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

    private void RunIdServerLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            _idAllocatorServer?.ProcessRequests();
            Thread.Sleep(1);
        }
        FdpLog<ClusterMaster>.Info("[Orchestrator] IdAllocatorServer loop exited.");
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
        _sysOpRequestReader.Dispose();
        _sysOpStatusWriter.Dispose();
        _nodeOpStatusReader.Dispose();
        _inventoryWriter?.Dispose();
        _inventoryWriter = null;
        foreach (var w in _nodeOpWriterCache.Values)
            w.Dispose();
        _nodeOpWriterCache.Clear();
        foreach (var cts in _activeCancellations.Values)
            cts.Dispose();
        _activeCancellations.Clear();
    }
}
