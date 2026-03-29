using System.Collections.Generic;
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
    private readonly DdsWriter<NodeOpCommand>     _nodeOpCommandWriter;

    // ── Roster ────────────────────────────────────────────────────────────
    private readonly NodeRoster _roster = new();

    // ── ID allocator server ───────────────────────────────────────────────
    private DdsIdAllocatorServer? _idAllocatorServer;
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
        _nodeOpCommandWriter = new DdsWriter<NodeOpCommand>(participant);

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
        ProcessSysOpRequests();
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
        BroadcastNodeOp(new NodeOpCommand
        {
            TransactionId = Guid.NewGuid(),
            Operation     = NodeOpType.AbortTransaction,
            PayloadJson   = string.Empty
        });
        BroadcastNodeOp(new NodeOpCommand
        {
            TransactionId = Guid.NewGuid(),
            Operation     = NodeOpType.PrepareState,
            PayloadJson   = ((int)DSMState.Standby).ToString()
        });

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

    /// <summary>Broadcasts a <see cref="NodeOpCommand"/> to all currently active nodes.</summary>
    private void BroadcastNodeOp(NodeOpCommand cmd)
    {
        // In Phase 1.5, the NodeOpCommand DDS topic is keyed by node; writing it once
        // with a broadcast payload delivers to all readers on the same domain.
        _nodeOpCommandWriter.Write(cmd);
    }

    private void PublishStandby()
    {
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
        _nodeOpCommandWriter.Dispose();
    }
}
