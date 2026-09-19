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
using Fdp.Toolkit.NetworkSpawning;
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

    // CE-285/286 (C-cap/C-roles): the last capability token set advertised per node, gathered from the durable
    // NodeCapabilities descriptor. Persisted here (not on the heartbeat-rebuilt profile) so every heartbeat can
    // re-apply it. The NodeRole mask is DERIVED from the fdp.role.* subset — nobody publishes the mask (AQ-70 §Q70-C).
    private readonly Dictionary<int, string[]> _nodeCapabilities = new();

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
        /// <summary>
        /// ⭐ When non-null this tracker is a <b>NON-TERMINAL PHASE</b> of a multi-phase round
        /// (today: <c>PrepareZone</c> → <c>CommitZone</c>). On all-ACK success the continuation runs
        /// INSTEAD of publishing a final status, so the requester is not told "Success" while the
        /// commit phase has not even been sent.
        /// <para>⚠ On FAILURE the round does not silently stop: <see cref="Targets"/> is fanned an
        /// <see cref="NodeOpType.AbortTransaction"/> so nodes free their staged buffers — the abort
        /// arm of <c>docs/designs/mgmt-1/DESIGN.md</c> §11.1.</para>
        /// </summary>
        public Action? OnPhaseSuccess;
        /// <summary>
        /// The nodes this phase was fanned out to. Only needed to address the abort of a failed
        /// <see cref="OnPhaseSuccess"/> round; a terminal tracker leaves it null.
        /// </summary>
        public List<int>? Targets;
        /// <summary>Per-node response JSON strings fed into the aggregator pipeline.</summary>
        public readonly Dictionary<int, Dictionary<Fdp.Toolkit.Orchestration.NodeOpType, string>> NodeResponses = new();
    }

    /// <summary>
    /// Single unified dictionary that tracks all in-flight 2PC rounds keyed by transaction ID.
    /// Replaces the former bespoke <c>_pendingSerializeTasks</c>,
    /// <c>_pendingManageEpisodeTasks</c>, and <c>_pendingBusTransitionAcks</c> dictionaries.
    /// </summary>
    private readonly Dictionary<Guid, GenericTransactionTracker> _pendingTransactions = new();

    // ── L8: the PARKED transition ─────────────────────────────────────────
    /// <summary>
    /// ⭐⭐⭐ <c>L8</c> — a transition that has been PLANNED but not yet FANNED OUT, because the scenario's
    /// files are still being distributed. Every field is a local that
    /// <see cref="ProcessTransitionStateIntent"/> already built; parking simply keeps them alive for a few
    /// frames instead of a few statements. 📄 <c>docs/DESIGN_Cluster_Load_Phase.md</c> §7.2.
    /// </summary>
    private sealed record ParkedTransition(
        Guid                  RequestId,
        TransitionStateIntent Intent,
        Queue<ISysOpStep>     Trajectory,
        ClusterState          SourceState,
        ClusterState          ResolvedTarget,
        int                   TotalSteps,
        double                ParkedAtSeconds);

    /// <summary>
    /// ⭐⭐ AT MOST ONE (§7.2c). ⛔ A dictionary keyed by request id would make several simultaneous parked
    /// transitions <em>representable</em>, which is a new concurrency property nobody asked for; the master
    /// tracks a single in-flight transition, so a second arrival is REJECTED, not queued.
    /// <para>⚠ <b>The editor's offline master parks too</b>, and that is fine — it constructs and ticks the
    /// same prefetch saga and acks <c>PrefetchFiles</c> on its own one-node slave, so it unparks on exactly
    /// the same path (§7.2b records the measurement; the design's first draft wrongly claimed it was
    /// exempt). ⭐ What IS exempt is any transition naming no scenario — it plans no copy, so nothing ever
    /// writes here.</para>
    /// </summary>
    private ParkedTransition? _parked;

    /// <summary>
    /// ⭐ The liveness bound on a PARKED entry (§7.3 ⑤). ⛔ This is NOT the timeout the change removes: a
    /// deterministic wait still needs a bound, and the difference is that expiry FAILS the request loudly
    /// instead of silently proceeding with files that never arrived. Settable so rails need not wait it out.
    /// </summary>
    public double ParkedTransitionExpirySeconds { get; set; } = 300.0;

    /// <summary>Test/diagnostic seam: the request id of the parked transition, or <c>null</c>.</summary>
    public Guid? ParkedRequestId => _parked?.RequestId;

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

    // ── The world's ONE id authority (HN-037) ────────────────────────────────
    /// <summary>
    /// ⭐⭐⭐ <b>The id authority this world resets at a world boundary</b>, or <see langword="null"/> when the
    /// host has none. 📄 <c>docs/DESIGN_Deterministic_Network_Ids.md</c> §11.
    ///
    /// <para>⭐⭐ <b>Why the hook is HERE and not once per host.</b> §11d planned two change points — a local
    /// reset in the editor and a cluster reset on the orchestrator. 📐 Measured while building: <b>the editor
    /// runs its own <c>ClusterMaster</c></b> *(<c>EditorSubsystem.cs:1702</c>, "the editor is a ONE-NODE
    /// cluster")*, so both hosts already share this class. ⇒ ⭐ one hook, two authorities — which is the
    /// §11e claim *("the editor becomes the offline one-node instance of the same authority")* expressed in
    /// code instead of asserted in prose. ⛔ Two hooks would have been two implementations of one rule.</para>
    ///
    /// <para>⚠ <see langword="null"/> is legitimate and NOT a silent default: a headless master with no
    /// network factory hosts no authority, and there is nothing for it to reset. ⛔ What is forbidden is a
    /// host that HAS an authority and does not pass it — see the forwarding rails.</para>
    /// </summary>
    public IWorldIdAuthority? IdAuthority { get; set; }

    /// <summary>
    /// ⭐ Test/diagnostic hook: how many times a world boundary has reset the authority.
    /// <para>📌 Exists so the guard can be asserted from the OUTSIDE — a rail proving the reset does NOT
    /// fire on replay/preview/step needs to observe non-firing, and "nothing happened" is only checkable
    /// against a counter.</para>
    /// </summary>
    public int WorldIdResetCount { get; private set; }

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
        IngestCapabilities();   // CE-285: gather static capability tokens BEFORE heartbeats derive roles from them.
        IngestHeartbeats();
        CheckBootstrapLatch();
        DetectAndEjectTimedOutNodes();
        DrainInjectedRequests();

        // ⭐⭐⭐ L8 — resume or expire a PARKED transition BEFORE new intents are admitted, so a transition
        //    whose files landed this frame does not cause the next request to be rejected as "busy".
        ProcessParkedTransition();

        // Bus-based intent drain (CMC-S008).
        ProcessTransitionStateIntents();
        ProcessManageEpisodeIntents();
        ProcessStorageOpIntents();
        ProcessTakeCheckpointIntents();
        ProcessSeekReplayIntents();
        ProcessCancelOperationIntents();
        ProcessDiagnosticDumpIntents();
        ProcessLoadZoneIntents();
        ProcessBuildTerrainAssetIntents();

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

            // CE-278: ClusterOpType.SaveScenario (=2) retired — not routed to ProcessStorageOpIntent.
            case ClusterOpType.SaveScenario:   // CE-277(c0): distributed JSON scenario save
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

            // C4 — §9.6: the zone-load action is ALWAYS cluster-wide, and on the editor (a single-node
            // cluster) it arrives through THIS injected path, not the DDS translator.
            case ClusterOpType.LoadZone:
                ProcessLoadZoneIntent(ClusterOpRequestAdapter.ToLoadZoneIntent(req));
                break;

            // E4 — same reasoning: the cluster panel injects the build op directly when it holds the
            // master, so both paths must route it or the button works on one host and not the other.
            case ClusterOpType.BuildTerrainAsset:
                ProcessBuildTerrainAssetIntent(
                    ClusterOpRequestAdapter.ToBuildTerrainAssetIntent(req));
                break;
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────

    private void IngestHeartbeats()
    {
        foreach (var hb in _eventBus.ReadManaged<NodeHeartbeatEvent>())
        {
            // CE-286 (C-roles): the heartbeat is telemetry-only now; roles are DERIVED from the node's
            // capability tokens (gathered by IngestCapabilities), so a heartbeat re-applies the latest known
            // capabilities + mask. Unknown-so-far ⇒ empty set / None until the capabilities advert lands.
            var tokens = _nodeCapabilities.TryGetValue(hb.NodeId, out var t) ? t : System.Array.Empty<string>();
            var profile = new NodeHealthProfile
            {
                NodeId                  = hb.NodeId,
                SubsystemName           = hb.SubsystemName ?? string.Empty,
                LocalClusterState       = (ClusterState)(int)hb.LocalStateId,
                LastHeartbeatUtcSeconds = UtcNowSeconds(),
                Capabilities            = new System.Collections.Generic.HashSet<string>(tokens),
                Roles                   = NodeRoleTokens.MaskFromTokens(tokens),
            };
            _roster.Upsert(profile);
        }
    }

    /// <summary>CE-285 (C-cap): gather each node's durable capability token set. Stored in a side-map so every
    /// heartbeat can re-apply it (the heartbeat rebuilds the profile). A late advert also immediately corrects
    /// an already-present profile's <see cref="NodeHealthProfile.Capabilities"/> + derived
    /// <see cref="NodeHealthProfile.Roles"/> without waiting for the next heartbeat.</summary>
    private void IngestCapabilities()
    {
        foreach (var caps in _eventBus.ReadManaged<NodeCapabilitiesEvent>())
        {
            var tokens = caps.Capabilities ?? System.Array.Empty<string>();
            _nodeCapabilities[caps.NodeId] = tokens;
            if (_roster.ActiveNodes.TryGetValue(caps.NodeId, out var existing))
            {
                existing.Capabilities = new System.Collections.Generic.HashSet<string>(tokens);
                existing.Roles        = NodeRoleTokens.MaskFromTokens(tokens);
            }
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

    /// <summary>
    /// C4 — the consumer <see cref="LoadZoneIntent"/> never had. Each intent starts its OWN
    /// <c>PrepareZone</c> → <c>CommitZone</c> round for exactly ONE zone.
    ///
    /// <para>⭐⭐ <b>One op per zone</b> (📄 docs/DESIGN_Terrain_Zones_And_Assets.md §9.3): a bad zone
    /// fails its own round instead of the batch, per-row progress falls straight out of
    /// <see cref="_pendingTransactions"/>, and retry granularity is a zone. The unbounded-fan-out
    /// question §9.3 raises is answered at the REQUESTER, not here — the master deliberately imposes
    /// no cap.</para>
    ///
    /// <para>⛔⛔ <b>NOT routed through <c>_activeTransaction</c></b> (§9.4): that is a SINGLE SLOT owned
    /// by the cluster state machine, assigned and cleared inside one method. Two concurrent zone rounds
    /// through it would overwrite each other. <see cref="_pendingTransactions"/> is keyed by
    /// transaction id and is the only structure here that survives concurrency.</para>
    /// </summary>
    private void ProcessLoadZoneIntents()
    {
        foreach (var intent in _eventBus.ReadManaged<LoadZoneIntent>())
        {
            if (!_bootstrapLatch)
            {
                PublishOpStatus(intent.RequestId, OrchestrationStatusCode.Rejected);
                continue;
            }

            ProcessLoadZoneIntent(intent);
        }
    }

    /// <summary>
    /// ⭐⭐ <c>E4</c> — the TERRAIN-ASSET BUILD round: one <c>PrepareTerrainAsset</c> →
    /// <c>CommitTerrainAsset</c> pair over every active node.
    ///
    /// <para>⚠ <b>Distinct from the zone round on purpose</b> (design §2.1b): zone PREPARATION and the
    /// asset BUILD "are different in nature and stay separate ops". A zone round names ONE zone (§9.3);
    /// a build names KINDS and sweeps whatever those kinds cover. ⭐ Both reuse the same
    /// <c>OnPhaseSuccess</c> continuation and the same abort arm, so there is one 2PC mechanism with two
    /// vocabularies — not two mechanisms.</para>
    /// </summary>
    private void ProcessBuildTerrainAssetIntents()
    {
        foreach (var intent in _eventBus.ReadManaged<BuildTerrainAssetIntent>())
        {
            if (!_bootstrapLatch)
            {
                PublishOpStatus(intent.RequestId, OrchestrationStatusCode.Rejected);
                continue;
            }

            ProcessBuildTerrainAssetIntent(intent);
        }
    }

    /// <summary>Shared by the bus drain and the injected-request path, so the editor cannot drift.</summary>
    private void ProcessBuildTerrainAssetIntent(BuildTerrainAssetIntent intent)
    {
        var targets = new List<int>(_roster.ActiveNodes.Keys);
        if (targets.Count == 0)
        {
            PublishOpStatus(intent.RequestId, OrchestrationStatusCode.Success);
            return;
        }

        var payload = new TerrainAssetOpPayload(intent.Kinds);
        var roundTx = Guid.NewGuid();

        FanOutNodeOp(NodeOpType.PrepareTerrainAsset, roundTx, payload, targets);
        _pendingTransactions[roundTx] = new GenericTransactionTracker
        {
            RequestId      = intent.RequestId,
            Expected       = targets.Count,
            Targets        = targets,
            OnPhaseSuccess = () => CommitTerrainAssetRound(intent.RequestId, intent.Kinds, roundTx, targets),
        };

        PublishOpStatus(intent.RequestId, OrchestrationStatusCode.InProgress);

        FdpLog<ClusterMaster>.Info(
            "[Orchestrator] Terrain asset build {0}: PrepareTerrainAsset fanned out to {1} node(s).",
            intent.RequestId, targets.Count);
    }

    /// <summary>Phase 2 — every node staged its build, so tell them all to publish it.</summary>
    private void CommitTerrainAssetRound(Guid requestId, string[]? kinds, Guid roundTx, List<int> targets)
    {
        FanOutNodeOp(NodeOpType.CommitTerrainAsset, roundTx, new TerrainAssetOpPayload(kinds), targets);
        _pendingTransactions[roundTx] = new GenericTransactionTracker
        {
            RequestId = requestId,
            Expected  = targets.Count,
        };

        FdpLog<ClusterMaster>.Info(
            "[Orchestrator] Terrain asset build {0}: all nodes staged, CommitTerrainAsset fanned out.",
            requestId);
    }

    /// <summary>
    /// One zone, one round. Shared by the bus drain above and the injected-request path
    /// (<see cref="ProcessSingleClusterOpRequest"/>), so the editor and the cluster cannot drift.
    /// </summary>
    private void ProcessLoadZoneIntent(LoadZoneIntent intent)
    {
        if (string.IsNullOrWhiteSpace(intent.ZoneId))
        {
            FdpLog<ClusterMaster>.Warn("[Orchestrator] LoadZone {0} rejected: no zone id.", intent.RequestId);
            PublishOpStatus(intent.RequestId, OrchestrationStatusCode.Rejected);
            return;
        }

        StartZoneLoadRound(intent.RequestId, intent.ZoneId!);
    }

    /// <summary>
    /// Fans out phase 1 of one zone's round and registers the continuation that fans out phase 2.
    /// ⭐ The zone load is ALWAYS cluster-wide (§9.6) — every active node, including the ones that will
    /// build nothing and ACK at once.
    /// </summary>
    private void StartZoneLoadRound(Guid requestId, string zoneId)
    {
        var targets = new List<int>(_roster.ActiveNodes.Keys);
        if (targets.Count == 0)
        {
            // Nothing to ask ⇒ the postcondition already holds. Reporting Success is honest here in a
            // way it would not be if a node had refused.
            PublishOpStatus(requestId, OrchestrationStatusCode.Success);
            return;
        }

        var payload = new ZoneOpPayload(zoneId);

        // ⭐⭐ ONE transaction id for BOTH phases — a round IS a transaction. The node stages under this
        //    id in PrepareZone and consumes that staging in CommitZone, so two ids would leave every
        //    commit unable to find what its own prepare staged. Safe against the two id-keyed maps:
        //    _pendingTransactions removes phase 1's tracker before phase 2's is added, and ClusterSlave
        //    dedups on (txId, OPERATION, state) so the two phases never look like a duplicate.
        var roundTx = Guid.NewGuid();

        FanOutNodeOp(NodeOpType.PrepareZone, roundTx, payload, targets);
        _pendingTransactions[roundTx] = new GenericTransactionTracker
        {
            RequestId      = requestId,
            Expected       = targets.Count,
            Targets        = targets,
            OnPhaseSuccess = () => CommitZoneLoadRound(requestId, zoneId, roundTx, targets),
        };

        PublishOpStatus(requestId, OrchestrationStatusCode.InProgress);

        FdpLog<ClusterMaster>.Info(
            "[Orchestrator] Zone '{0}' load {1}: PrepareZone fanned out to {2} node(s).",
            zoneId, requestId, targets.Count);
    }

    /// <summary>Phase 2 — every node staged successfully, so tell them all to swap.</summary>
    private void CommitZoneLoadRound(Guid requestId, string zoneId, Guid roundTx, List<int> targets)
    {
        FanOutNodeOp(NodeOpType.CommitZone, roundTx, new ZoneOpPayload(zoneId), targets);
        _pendingTransactions[roundTx] = new GenericTransactionTracker
        {
            RequestId = requestId,
            Expected  = targets.Count,
            // ⭐ Terminal: no continuation ⇒ completion publishes the final status for requestId.
        };

        FdpLog<ClusterMaster>.Info(
            "[Orchestrator] Zone '{0}' load {1}: all nodes staged, CommitZone fanned out.",
            zoneId, requestId);
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

    /// <summary>
    /// ⭐⭐⭐ <c>L8</c> — the resume half of the deterministic staging wait: release the parked transition
    /// when its files are on every node, fail it when the distribution failed, and fail it when the
    /// distribution never reported at all.
    ///
    /// <para>⛔ <b>It costs nothing when nothing is parked</b>, which is the normal case — and the early
    /// return also means the bus is not read for an event a host may never have registered.</para>
    ///
    /// <para>⚠ The expiry is the one bound that remains (§7.3 ⑤). It is not the timeout the change removes:
    /// it FAILS the request rather than proceeding with files that never arrived.
    /// 📄 <c>docs/DESIGN_Cluster_Load_Phase.md</c> §7.</para>
    /// </summary>
    private void ProcessParkedTransition()
    {
        var parked = _parked;
        if (parked == null) return;

        foreach (var ev in _eventBus.ReadManaged<PrefetchDistributionCompletedEvent>())
        {
            if (ev.RequestId != parked.RequestId) continue;

            _parked = null;

            if (!ev.IsSuccess)
            {
                // §7.4 — the second defect this change closes: a failed copy now fans out NOTHING.
                FdpLog<ClusterMaster>.Error(
                    "[Orchestrator] L8: the distribution of '{0}' FAILED — transition {1} is abandoned and "
                  + "nothing was fanned out.",
                    ev.ScenarioId, parked.RequestId);
                PublishOpStatus(parked.RequestId, OrchestrationStatusCode.Failure);
                return;
            }

            FdpLog<ClusterMaster>.Info(
                "[Orchestrator] L8: the staging of '{0}' is on every node — transition {1} resumes.",
                ev.ScenarioId, parked.RequestId);
            ExecuteTransitionTrajectory(parked);
            return;
        }

        if (UtcNowSeconds() - parked.ParkedAtSeconds <= ParkedTransitionExpirySeconds) return;

        _parked = null;
        FdpLog<ClusterMaster>.Error(
            "[Orchestrator] L8: transition {0} expired after {1:F0}s — the staging never completed. "
          + "Nothing was fanned out.",
            parked.RequestId, ParkedTransitionExpirySeconds);
        PublishOpStatus(parked.RequestId, OrchestrationStatusCode.Timeout);
    }

    private void ProcessTransitionStateIntent(TransitionStateIntent intent)
    {
        var requestId = intent.TransactionId;

        // ⭐⭐ L8 §7.2c — AT MOST ONE parked transition. ⛔ Rejected, not queued: the master tracks a single
        //    in-flight transition and queueing would be a new property nobody asked for.
        if (_parked != null)
        {
            FdpLog<ClusterMaster>.Warn(
                "[Orchestrator] L8: transition {0} REJECTED — transition {1} is parked waiting for its "
              + "scenario files.",
                requestId, _parked.RequestId);
            PublishOpStatus(requestId, OrchestrationStatusCode.Rejected);
            return;
        }

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

        // ⚠ L8 §7.2a — the optimistic advance is NOT done here any more: a PARKED transition must not
        //   report the cluster as already in the target state while it is still waiting for files.
        //   It moves to ExecuteTransitionTrajectory, with the transaction record and the id reset.
        var capturedSourceState = _currentDsmState;

        // ⭐⭐⭐ L8 — START THE COPY, THEN PARK. The trajectory is NOT fanned out here.
        //
        // 🔴 What this replaces, measured 2026-09-18: the copy was started and the whole trajectory fanned
        //    out in this SAME pass, so nodes received the load step while their files were still arriving
        //    (the content step was dispatched 6 ms after the copy started and 49 ms before the files were
        //    even fanned out). The node side papered over it with per-file timeouts.
        // 🔒 User: "it cannot depend on timeouts where can easily wait deterministically."
        //
        // ⭐ The fact we wait on already exists: AssetPrefetchProcessManager tracks a PER-NODE
        //   acknowledgement set and completes only when it empties. It now carries the originating request
        //   id so the parked transition can be matched to it.
        // 📄 docs/DESIGN_Cluster_Load_Phase.md §7.
        string? prefetchScenarioId = null;
        foreach (var step in trajectory)
        {
            if (step is OperationStep { Operation: ClusterOpType.PrefetchScenario } ps)
            {
                prefetchScenarioId = (string?)ps.DomainPayload ?? string.Empty;
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

        var planned = new ParkedTransition(
            requestId, intent, trajectory, capturedSourceState, resolvedTarget, totalSteps,
            UtcNowSeconds());

        // ⭐⭐ Park ONLY when a copy was actually started for this transition — i.e. only when the planner
        //    put a PrefetchScenario step in the trajectory, which it does only for a named scenario.
        //    ⇒ replay, preview, idle and every unload execute in this very frame, exactly as before.
        // ⚠ This is a DERIVATION, not a host check: the editor's offline master parks too, and unparks
        //   through its own prefetch saga (§7.2b — the design's first draft claimed it was exempt and that
        //   was measured FALSE).
        if (prefetchScenarioId != null && _eventBus != null)
        {
            _parked = planned;
            _eventBus.PublishManaged(new ExecutePrefetchIntent
            {
                RequestId     = requestId,
                ScenarioId    = prefetchScenarioId,
                ActiveNodeIds = new List<int>(_roster.ActiveNodes.Keys),
            });

            FdpLog<ClusterMaster>.Info(
                "[Orchestrator] L8: transition {0} PARKED until the staging of '{1}' is on every node.",
                requestId, prefetchScenarioId);
            return;
        }

        ExecuteTransitionTrajectory(planned);
    }

    /// <summary>
    /// ⭐⭐⭐ <c>L8</c> — the EXECUTE half of a transition: advance the reported state, record the
    /// transaction, reset the id authority, fan out, and set up the acknowledgement accounting.
    ///
    /// <para>⭐ Unchanged from what this code always did — it simply runs LATER when the transition was
    /// parked. ⛔ The fan-out is still ONE pass, which is why two-phase commit, ack accounting, replay and
    /// preview are untouched. 📄 <c>docs/DESIGN_Cluster_Load_Phase.md</c> §7.1.</para>
    /// </summary>
    private void ExecuteTransitionTrajectory(ParkedTransition parked)
    {
        var intent              = parked.Intent;
        var requestId           = parked.RequestId;
        var trajectory          = parked.Trajectory;
        var capturedSourceState = parked.SourceState;
        var resolvedTarget      = parked.ResolvedTarget;
        int totalSteps          = parked.TotalSteps;

        // ⚠ §7.2a — the optimistic advance belongs HERE, not at planning time.
        _currentDsmState = resolvedTarget;

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

        // ── HN-037: the world boundary resets the ONE id authority ───────────────────────
        // ⭐ BEFORE the fan-out, because PrepareLive is what makes CGF allocate the authored ids.
        ResetIdAuthorityIfWorldBoundary(trajectory, capturedSourceState);

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
                        bool isLoadStep = tStep.TargetState == ClusterState.LoadingLive
                                       || tStep.TargetState == ClusterState.LoadingEdit;

                        // ⭐⭐⭐ L1 — THE SHARED CONTENT NAMES GO ON THE MESSAGE.
                        //   📐 Read ONCE here, from the master scenario on the NAS, and carried to every
                        //   node — because four of the five roles never open the scenario file and so
                        //   cannot read these names out of it, while every ECS node needs the knowledge
                        //   base (Q65-A′) and the movement roles need the terrain.
                        //   ⛔ Before this they travelled as a staged sidecar file written during the
                        //   file copy, which RACES the step that consumes it (measured: the content step
                        //   dispatched 6 ms after the copy started, 49 ms before the files were fanned
                        //   out) — and the knowledge-base and terrain loaders do not retry, so they
                        //   silently concluded "this scenario names none".
                        //   📄 docs/DESIGN_Cluster_Load_Phase.md §2.4, §4.2, L1.
                        var contentNames = isLoadStep
                            ? ReadContentNamesForFanOut(intent.ScenarioId)
                            : default;

                        var preparePayload = new EditLoadHandlerPayload(
                            isLoadStep ? intent.ScenarioId : null,
                            false,
                            (FdpClusterState)(int)tStep.TargetState,
                            ExerciseId:  intent.ExerciseId,
                            TkbName:     contentNames.TkbName,
                            TerrainName: contentNames.TerrainName);

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

    /// <summary>
    /// 🔴🔴 <b><c>HN-037</c> — THE GUARD. This is the whole safety argument, so read it before touching the
    /// state list.</b> 📄 <c>docs/DESIGN_Deterministic_Network_Ids.md</c> §11b/§11c.
    ///
    /// <para>⭐⭐ Resetting the authority BACKWARD to 1000 is safe at a <b>scenario load</b> and nowhere else,
    /// because the world is cleared there — nothing survives the boundary for a re-issued id to collide
    /// with. ⛔ Fired mid-exercise the same call is catastrophic: it fights <c>mgmt-1</c> §5.7's forward
    /// high-water mark and flushes pools other nodes are mid-way through.</para>
    ///
    /// <para>⭐⭐⭐ <b>The two states that qualify, and why the other four do NOT</b> *(§11c's reconciliation
    /// table, in code)*:</para>
    /// <list type="table">
    ///   <item><term><c>LoadingLive</c> · <c>LoadingEdit</c></term><description>⭐ scenario load — the world
    ///   is cleared and re-created from the file. THE world boundary, on both hosts.</description></item>
    ///   <item><term><c>LoadingReplay</c></term><description>⛔ §5.7's own policy: reset FORWARD to the
    ///   recording's high-water mark, not backward to a constant. Different value, different direction, and
    ///   the world is pre-populated.</description></item>
    ///   <item><term><c>LoadingPreview</c></term><description>⛔ §4d: the world is NOT cleared, so each node
    ///   restores its OWN pool locally. Touching the central authority here would re-issue ids that live
    ///   entities still hold.</description></item>
    ///   <item><term>everything else</term><description>⛔ <c>Unloading*</c>, <c>Operating*</c>, <c>Idle</c>
    ///   — mid-exercise or teardown; no world is being created.</description></item>
    /// </list>
    ///
    /// <para>⚠ Keyed on the <b>planned trajectory</b>, not on the requested target: a BFS path from
    /// <c>Idle</c> to <c>OperatingLive</c> passes THROUGH <c>LoadingLive</c>, and the target alone would
    /// miss it.</para>
    ///
    /// <para>🔴🔴 <b>AND A <c>LoadingLive</c> STEP IS NOT SUFFICIENT — the state it is entered FROM decides.</b>
    /// 📐 Found by the guard rail, `2026-08-24`, and §11c's three-policy table does not enumerate it: the
    /// graph carries <c>OperatingReplay → LoadingLive</c>, the <b>live-from-replay branch</b>
    /// *(<c>CGF1-S0305</c>; <c>ReferenceReplayLoadHandler</c> claims <c>PrepareLive</c> while a replay
    /// session is active, so <c>CgfScenarioLoadHandler</c> never runs and no scenario is extracted)*. ⛔ That
    /// world is NOT cleared — it continues from the replayed state, with entities already holding ids in the
    /// 1000-block. ⇒ resetting there is precisely the catastrophic mid-exercise case this guard exists to
    /// prevent, wearing a <c>LoadingLive</c> label.</para>
    ///
    /// <para>⇒ ⭐⭐⭐ <b>the rule is: a <c>Loading{Live,Edit}</c> step entered FROM <c>Idle</c>.</b> Idle is the
    /// only state with no world, so it is the only place a load CREATES one rather than branching from one.
    /// ⭐ A re-load from a live edit session still qualifies — its trajectory goes
    /// <c>OperatingEdit → UnloadingEdit → Idle → LoadingEdit</c>, and the step is entered from Idle there
    /// too.</para>
    /// </summary>
    /// <summary>
    /// ⭐⭐ <c>L1</c> — the shared content names for a load fan-out, read from the MASTER copy on the NAS.
    ///
    /// <para>⛔ <b>A failure here must NOT fail the transition.</b> ⚠ The names are a HINT that saves each
    /// node a disk peek; the loud failure for a named-but-missing artifact belongs to the node's own loader
    /// (<c>docs/DESIGN_Artifact_Staging.md</c> §9.3 made exactly this call for the staging path, and the
    /// two must agree). ⇒ an unreadable or disagreeing scenario logs and yields no names, and the node
    /// falls back to its staged header exactly as before.</para>
    /// </summary>
    private StagedArtifactNames ReadContentNamesForFanOut(string? scenarioId)
    {
        if (string.IsNullOrWhiteSpace(scenarioId)) return default;

        try
        {
            return StorageGatewayModule.ReadScenarioContentNames(
                Path.Combine(
                    _config.NasBasePath,
                    Fdp.Toolkit.Orchestration.OrchestrationConstants.ScenariosDirectoryName,
                    scenarioId!));
        }
        catch (Exception ex)
        {
            FdpLog<ClusterMaster>.Error(
                "[Orchestrator] L1: could not read the content names of scenario '{0}' ({1}). "
              + "The load proceeds and each node falls back to its staged header.",
                scenarioId, ex.Message);
            return default;
        }
    }

    private void ResetIdAuthorityIfWorldBoundary(
        IEnumerable<ISysOpStep> trajectory, ClusterState sourceState)
    {
        if (IdAuthority == null) return;

        // ⭐ Walk the trajectory tracking the state each step is entered FROM, starting at where the
        //   cluster actually was. ⚠ The SOURCE is passed in, not read from _currentDsmState: that field has
        //   already been advanced optimistically to the resolved target by the time this runs, so reading it
        //   here would compare a load against its own destination and never see Idle.
        var previousState = sourceState;
        bool crossesWorldBoundary = false;

        foreach (var step in trajectory)
        {
            if (step is not TransitionStep ts) continue;

            bool isLoad = ts.TargetState == ClusterState.LoadingLive
                       || ts.TargetState == ClusterState.LoadingEdit;

            if (isLoad && previousState == ClusterState.Idle)
            {
                crossesWorldBoundary = true;
                break;
            }

            previousState = ts.TargetState;
        }

        if (!crossesWorldBoundary) return;

        IdAuthority.ResetToBase(WorldIdAuthority.WorldBase);
        WorldIdResetCount++;

        FdpLog<ClusterMaster>.Info(
            "[Orchestrator] HN-037: world boundary — id authority reset to {0}.",
            WorldIdAuthority.WorldBase);
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
            // CE-278: StorageOpType.SaveScenario (=2) retired — the half-built .fdp-archive stub whose only
            // real output (the Orchestrator.json exercise sidecar) is now written on the Export path
            // (AssetInventoryProcessManager §5). The declarative scenario save is SaveScenarioJson below.

            // ⭐⭐⭐ CE-275 ③ — the DECLARATIVE scenario save. Same SerializeLocal fan-out mechanism as the
            //   .fdp archive above, but carrying a ScenarioSaveHandlerPayload so the per-host
            //   HrotScenarioSaveHandler runs the gated ScenarioSerializer and writes each node's owned slice.
            //   Distinct transaction + payload type, so ReferenceArchiveHandler and the scenario save handler
            //   never collide on the shared SerializeLocal op. 📄 DESIGN_Distributed_Scenario_Persistence.md §4.
            case StorageOpType.SaveScenario:
            {
                if (string.IsNullOrWhiteSpace(intent.ScenarioName))
                {
                    FdpLog<ClusterMaster>.Warn("[Orchestrator] SaveScenario missing ScenarioName — rejected (requestId={0}).", intent.RequestId);
                    PublishOpStatus(intent.RequestId, OrchestrationStatusCode.Rejected);
                    return;
                }

                var scnNodeIds = new List<int>(_roster.ActiveNodes.Keys);
                var scnTxId    = Guid.NewGuid();

                // CE-277(c2): map this fan-out's tx → scenario name so StorageProcessManager can merge the
                // pulled per-node slices into the one canonical scenario.json after the NAS pull.
                _eventBus.PublishManaged(new SaveScenarioJsonBegunEvent
                {
                    TransactionId = scnTxId,
                    ScenarioName  = intent.ScenarioName!,
                });

                FanOutSerializeLocal(scnTxId, scnNodeIds,
                    new Fdp.Toolkit.Orchestration.Handlers.ScenarioSaveHandlerPayload(intent.ScenarioName!));

                FdpLog<ClusterMaster>.Info("[Orchestrator] SaveScenario '{0}' → SerializeLocal fan-out to {1} node(s).",
                    intent.ScenarioName, scnNodeIds.Count);
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

        // ⭐⭐ L8 — the ONE transition state that can be cancelled cleanly: PARKED, so nothing has been sent.
        //   ⛔ Cancel was an ARCHIVE-only facility (`_activeCancellations` is written only by the Export and
        //   Import branches, CGF-1-BATCH-28 §C.4) and a transition was never a target — before parking there
        //   was simply no window: the trajectory was fanned out in the pass that admitted it.
        // ⚠ It abandons the TRANSITION, not the copy: the gateway's PrefetchScenarioAsync registers no
        //   cancellation source, so the bytes finish landing in the node staging roots. That is harmless —
        //   nothing loads them — but it is why this says "abandoned", not "stopped".
        // 📄 docs/DESIGN_Cluster_Load_Phase.md §7.7 ④.
        if (targetId != Guid.Empty && _parked?.RequestId == targetId)
        {
            _parked = null;
            FdpLog<ClusterMaster>.Info(
                "[Orchestrator] L8: parked transition {0} CANCELLED — nothing was fanned out. "
              + "The staging copy already in flight is left to finish.",
                targetId);
            PublishOpStatus(targetId, OrchestrationStatusCode.Cancelled);
            return;
        }

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

                    // ⭐ A failed NON-TERMINAL phase must tell the nodes that ACKed to drop what they
                    //   staged — otherwise a prepared-but-never-committed buffer leaks until process
                    //   exit. 📄 docs/designs/mgmt-1/DESIGN.md §11.1 (ABORT).
                    if (tracker.OnPhaseSuccess != null && tracker.Targets is { Count: > 0 })
                        FanOutNodeOp(NodeOpType.AbortTransaction, Guid.NewGuid(),
                            new AbortTransactionPayload(ev.TransactionId), tracker.Targets);

                    PublishOpStatus(tracker.RequestId, tracker.FailureCode);
                }
                else if (tracker.OnPhaseSuccess != null)
                {
                    // ⛔ Deliberately NO PublishOpStatus here: the requester learns the outcome when the
                    //   LAST phase completes, not when the first one does.
                    tracker.OnPhaseSuccess();
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
