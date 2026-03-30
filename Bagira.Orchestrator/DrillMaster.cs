using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using Bagira.BDC.SSTD.Orchestration;
using CycloneDDS.Runtime;
using FDP.Kernel.Logging;
using ModuleHost.Network.Cyclone.Services;

namespace Bagira.Orchestrator;

/// <summary>
/// Orchestrator control-plane host: system state, node heartbeats, DDS network ID allocation server,
/// bootstrap latch, heartbeat-timeout eviction, and 2PC transaction history ring buffer.
/// </summary>
public sealed class DrillMaster : IDisposable
{
    private readonly ClusterConfiguration _config;

    // ── DDS infrastructure ────────────────────────────────────────────────
    private readonly DdsWriter<SystemStateTopic>  _systemStateWriter;
    private readonly DdsReader<NodeHeartbeat>     _heartbeatReader;
    private readonly DdsReader<SysOpRequest>      _sysOpRequestReader;
    private readonly DdsWriter<SysOpStatus>       _sysOpStatusWriter;
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

    // ── Global context handler (CGF1-S0307) ──────────────────────────────
    private GlobalContextDsmHandler? _globalContextHandler;
    private DdsIdAllocatorServer?    _idAllocatorServer;
    private CancellationTokenSource? _idServerCts;
    private Thread? _idServerThread;

    // ── Bootstrap latch (CGF1-S0105) ──────────────────────────────────────
    /// <summary>
    /// <c>true</c> once every mandatory subsystem has appeared with <c>LocalDsmState == Standby</c>.
    /// While <c>false</c> all <see cref="SysOpRequest"/> messages are rejected.
    /// </summary>
    private bool _bootstrapLatch;

    // ── Active transaction ────────────────────────────────────────────────
    private DistributedTransaction? _activeTransaction;

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

    // ── Current DSM state (tracked here so the planner can compute relative paths) ─
    /// <summary>
    /// Optimistic cluster DSM state used as the <c>current</c> argument to
    /// <see cref="TransitionPlanner.PlanTrajectory"/>.
    ///
    /// <para><b>Update rule (Phase 2.0 — optimistic):</b> Whenever a
    /// <see cref="SysOpType.TransitionState"/> request is <em>accepted</em> (plan
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
    private DSMState _currentDsmState = DSMState.Standby;

    // ── Transition planner (CGF1-S0201) ─────────────────────────────────────
    private readonly TransitionPlanner _planner = new();

    // ── 2PC history ring buffer (CGF1-S0105) ─────────────────────────────
    private readonly DistributedTransaction[] _history;
    private int  _historyHead;

    // ── Time mode hint (CGF1-S0205) ──────────────────────────────────────
    /// <summary>
    /// Set when a <see cref="SysOpType.TransitionState"/> request heading toward
    /// <see cref="DSMState.LoadingLive"/> carries <c>"TimeMode": "Deterministic"</c>
    /// in its <see cref="SysOpRequest.PayloadJson"/>.
    ///
    /// <para>Consumers (e.g. <c>OrchestratorSubsystem</c>) should read this property
    /// after <see cref="Tick"/> and trigger <c>DistributedTimeCoordinator.SwitchToDeterministic</c>
    /// before the cluster enters <see cref="DSMState.RunningLive"/>.</para>
    ///
    /// <para>Reset to <c>null</c> when a <see cref="DSMState.Standby"/> trajectory clears the
    /// pending mode.</para>
    /// </summary>
    public string? PendingTimeMode { get; private set; }

    // ── Active stories (CGF1-S0308) ──────────────────────────────────────
    /// <summary>
    /// Set of story IDs currently injected into the running drill via
    /// <see cref="SysOpType.ManageStory"/> Start operations.  Entries are removed
    /// by corresponding Stop operations.
    /// </summary>
    private readonly HashSet<Guid> _activeStories = new();

    /// <summary>
    /// Read-only view of the currently active story IDs.
    /// Updated by <c>ManageStory</c> <c>SysOpRequest</c> processing.
    /// </summary>
    public IReadOnlyCollection<Guid> ActiveStories => _activeStories;

    private bool _disposed;

    // ── Public surface ────────────────────────────────────────────────────
    public NodeRoster NodeRoster => _roster;

    /// <summary><c>true</c> once all mandatory nodes have reached <c>Standby</c>.</summary>
    public bool BootstrapComplete => _bootstrapLatch;

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

    public DrillMaster(DdsParticipant participant)
        : this(participant, ClusterConfiguration.Default) { }

    public DrillMaster(DdsParticipant participant, ClusterConfiguration config)
    {
        _config              = config ?? ClusterConfiguration.Default;
        _history             = new DistributedTransaction[Math.Max(1, _config.TransactionHistoryCapacity)];

        _heartbeatReader     = new DdsReader<NodeHeartbeat>(participant);
        _systemStateWriter   = new DdsWriter<SystemStateTopic>(participant);
        _sysOpRequestReader  = new DdsReader<SysOpRequest>(participant);
        _sysOpStatusWriter   = new DdsWriter<SysOpStatus>(participant);
        _nodeOpStatusReader  = new DdsReader<NodeOpStatus>(participant);
        _nodeOpParticipant   = participant;

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
        ProcessSysOpRequests();
        ConsumeNodeOpStatuses();
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
                LocalDsmState = hb.LocalDsmState,
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
                if (kv.Value.SubsystemName == name && kv.Value.LocalDsmState == DSMState.Standby)
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
        FdpLog<DrillMaster>.Info("[Orchestrator] All mandatory nodes reached Standby — bootstrap complete.");
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
        FdpLog<DrillMaster>.Warn("[Orchestrator] Node {0} ({1}) ejected (heartbeat timeout).",
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
            CurrentState        = DSMState.Degraded,
            DrillId             = Guid.Empty,
            StateStartWallTicks = DateTimeOffset.UtcNow.Ticks,
            TransactionEpoch    = 0
        });
        FdpLog<DrillMaster>.Warn("[Orchestrator] System entered Degraded state (mandatory node {0} lost).", profile.SubsystemName);

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
            PayloadJson   = ((int)DSMState.Standby).ToString()
        }, survivingIds);

        // Re-engage bootstrap latch until the mandatory node returns.
        _bootstrapLatch = false;
    }

    /// <summary>
    /// Reads all pending <see cref="SysOpRequest"/> messages and replies with
    /// <see cref="SysOpStatus"/>. While the bootstrap latch is inactive all requests
    /// are rejected with <see cref="OpStatus.Rejected"/>.
    /// </summary>
    private void ProcessSysOpRequests()
    {
        using var scope = _sysOpRequestReader.Take();
        foreach (var sample in scope)
        {
            if (!sample.IsValid) continue;
            var req = sample.Data;

            if (!_bootstrapLatch)
            {
                _sysOpStatusWriter.Write(new SysOpStatus
                {
                    RequestId  = req.RequestId,
                    Status     = OpStatus.Rejected,
                    ErrorCode  = 0,
                    ResultJson = string.Empty
                });
                continue;
            }

            // Accept the request — resolve target via planner for TransitionState ops.
            DSMState resolvedTarget = _currentDsmState;
            int totalSteps = 1;

            if (req.OperationType == SysOpType.TransitionState)
            {
                try
                {
                    // Capture current state before optimistic advance (needed for S0305 detection).
                    var stateBeforeAdvance = _currentDsmState;

                    var trajectory = _planner.PlanTrajectory(_currentDsmState, req);
                    totalSteps     = trajectory.Count;
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
                        if (step is OperationStep { Operation: SysOpType.PrefetchScenario } prefetchStep)
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
                        if (step is TransitionStep ts && ts.TargetState == DSMState.LoadingLive)
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
                    if (resolvedTarget == DSMState.Standby)
                        PendingTimeMode = null;

                    // CGF1-S0305: Live-from-Replay temporal interlock.
                    // When the trajectory passes through LoadingLive from RunningReplay,
                    // hard-freeze time and fan out PrepareLive with a new branched DrillId
                    // before any node begins recording.  Time is restored once all nodes ACK.
                    if (passesLoadingLive && stateBeforeAdvance == DSMState.RunningReplay)
                    {
                        _replayMasterModule?.FreezeTime();

                        var branchedDrillId = Guid.NewGuid();
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
                                PayloadJson   = $"{{\"DrillId\":\"{branchedDrillId}\"}}"
                            }, activeNodeIds);
                            FdpLog<DrillMaster>.Info(
                                "[Orchestrator] S0305: Live-from-Replay branch — time frozen, " +
                                "PrepareLive fan-out (branchedDrillId={0}, nodes={1}).",
                                branchedDrillId, activeNodeIds.Count);
                        }
                        else
                        {
                            // No nodes to wait for; restore time immediately.
                            _replayMasterModule?.RestoreTime();
                            FdpLog<DrillMaster>.Warn(
                                "[Orchestrator] S0305: Live-from-Replay branch with zero active nodes — time restored immediately.");
                        }
                    }
                }
                catch (InvalidOperationException ex)
                {
                    FdpLog<DrillMaster>.Warn(
                        "[Orchestrator] TransitionState request {0} rejected by planner: {1}",
                        req.RequestId, ex.Message);
                    _sysOpStatusWriter.Write(new SysOpStatus
                    {
                        RequestId  = req.RequestId,
                        Status     = OpStatus.Failure,
                        ErrorCode  = 1,
                        ResultJson = string.Empty
                    });
                    continue;
                }
            }
            else if (req.OperationType == SysOpType.SaveScenario)
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

                FdpLog<DrillMaster>.Info(
                    "[Orchestrator] SaveScenario request {0} → SerializeLocal fan-out to {1} node(s).",
                    req.RequestId, nodeIds.Count);
            }
            else if (req.OperationType == SysOpType.ManageStory)
            {
                // CGF1-S0308: validate state, plan story steps, fan out to nodes.
                try
                {
                    var storySteps = _planner.PlanManageStory(_currentDsmState, req);
                    totalSteps     = storySteps.Count;

                    // Parse Mode and StoryId from the payload for story set maintenance.
                    string? storyMode = null;
                    Guid    storyId   = Guid.Empty;
                    if (!string.IsNullOrWhiteSpace(req.PayloadJson))
                    {
                        try
                        {
                            using var sd = JsonDocument.Parse(req.PayloadJson);
                            if (sd.RootElement.TryGetProperty("Mode",    out var mp)) storyMode = mp.GetString();
                            if (sd.RootElement.TryGetProperty("StoryId", out var sp)) Guid.TryParse(sp.GetString(), out storyId);
                        }
                        catch (JsonException) { }
                    }

                    // Execute the planned story steps.
                    foreach (var step in storySteps)
                    {
                        if (step is OperationStep { Operation: SysOpType.PrefetchScenario } prefetch)
                        {
                            ExecutePrefetchScenario(prefetch.PayloadJson, req.RequestId);
                        }
                        else if (step is OperationStep { Operation: SysOpType.ManageStory } manageStep)
                        {
                            // Determine NodeOpType from mode.
                            bool isStart = string.Equals(storyMode, "Start", StringComparison.OrdinalIgnoreCase);
                            var nodeOp   = isStart ? NodeOpType.StartStory : NodeOpType.StopStory;

                            var txId     = Guid.NewGuid();
                            var nodeIds  = new List<int>(_roster.ActiveNodes.Keys);
                            FanOutNodeOp(new NodeOpCommand
                            {
                                TransactionId = txId,
                                Operation     = nodeOp,
                                PayloadJson   = manageStep.PayloadJson,
                            }, nodeIds);

                            // Update in-memory active story set.
                            if (storyId != Guid.Empty)
                            {
                                if (isStart) _activeStories.Add(storyId);
                                else         _activeStories.Remove(storyId);
                            }

                            FdpLog<DrillMaster>.Info(
                                "[Orchestrator] ManageStory {0}: story {1} → {2} to {3} node(s).",
                                storyMode, storyId, nodeOp, nodeIds.Count);
                        }
                    }
                }
                catch (InvalidOperationException ex)
                {
                    FdpLog<DrillMaster>.Warn(
                        "[Orchestrator] ManageStory request {0} rejected: {1}",
                        req.RequestId, ex.Message);
                    _sysOpStatusWriter.Write(new SysOpStatus
                    {
                        RequestId  = req.RequestId,
                        Status     = OpStatus.Rejected,
                        ErrorCode  = 2,
                        ResultJson = string.Empty
                    });
                    continue;
                }
            }

            var tx = new DistributedTransaction
            {
                TransactionId    = Guid.NewGuid(),
                OriginRequestId  = req.RequestId,
                TargetDsmState   = resolvedTarget,
                TotalSteps       = totalSteps,
                CompletedSteps   = totalSteps,
                IsAborted        = false
            };
            AppendToHistory(tx);

            _sysOpStatusWriter.Write(new SysOpStatus
            {
                RequestId  = req.RequestId,
                Status     = OpStatus.InProgress,
                ErrorCode  = 0,
                ResultJson = string.Empty
            });

            FdpLog<DrillMaster>.Info(
                "[Orchestrator] SysOpRequest {0} ({1}) accepted (transaction {2}).",
                req.RequestId, req.OperationType, tx.TransactionId);
        }
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
    /// Registers the <see cref="GlobalContextDsmHandler"/> that serializes/restores the
    /// Orchestrator's own global context during scenario save/load operations.
    /// Call once at startup, before any scenario operations are issued.
    /// </summary>
    public void SetGlobalContextHandler(GlobalContextDsmHandler handler)
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
    /// in <see cref="ProcessSysOpRequests"/>.  If no <see cref="StorageGatewayModule"/>
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
                    FdpLog<DrillMaster>.Info(
                        "[Orchestrator] S0305: All branch ACKs received — time scale restored.");
                }
                continue;
            }

            if (!_pendingSerializeTasks.TryGetValue(status.TransactionId, out var task))
                continue;

            if (status.Status == OpStatus.Success && !string.IsNullOrEmpty(status.ResultJson))
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
                    FdpLog<DrillMaster>.Warn(
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
                    FdpLog<DrillMaster>.Error(
                        "[Orchestrator] SaveScenario completed with {0} node(s) reporting malformed ResultJson — NAS manifest may be incomplete.",
                        task.FailureCount);

                // Append the Orchestrator's own manifest entry if the local handler produced one.
                if (_globalContextHandler?.CommitManifestEntry != null)
                    task.Manifests.Add(_globalContextHandler.CommitManifestEntry);

                if (_gateway != null && task.Manifests.Count > 0)
                {
                    // Fire-and-forget: the pull is async; DrillMaster continues ticking.
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
    /// <see cref="SysOpStatus.Failure"/> on fault / policy violation (FailureCount &gt; 0).
    /// Must be called each <see cref="Tick"/> before <see cref="ProcessSysOpRequests"/> to
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
            FdpLog<DrillMaster>.Error(
                "[Orchestrator] PrefetchScenario for '{0}' failed ({1}) — publishing SysOpStatus.Failure for request {2}.",
                op.ScenarioId, reason, op.RequestId);
            _sysOpStatusWriter.Write(new SysOpStatus
            {
                RequestId  = op.RequestId,
                Status     = OpStatus.Failure,
                ErrorCode  = 2,
                ResultJson = string.Empty
            });
            return;
        }

        // Success — now safe to fan-out PrefetchFiles so nodes verify their staging dirs.
        FdpLog<DrillMaster>.Info(
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
    /// <param name="requestId">Originating <see cref="SysOpRequest.RequestId"/>; used to
    /// surface <see cref="SysOpStatus.Failure"/> if the gateway copy fails.</param>
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

        FdpLog<DrillMaster>.Info(
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

    private void PublishStandby()    {
        _systemStateWriter.Write(new SystemStateTopic
        {
            CurrentState        = DSMState.Standby,
            DrillId             = Guid.Empty,
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
        _sysOpRequestReader.Dispose();
        _sysOpStatusWriter.Dispose();
        _nodeOpStatusReader.Dispose();
        foreach (var w in _nodeOpWriterCache.Values)
            w.Dispose();
        _nodeOpWriterCache.Clear();
    }
}
