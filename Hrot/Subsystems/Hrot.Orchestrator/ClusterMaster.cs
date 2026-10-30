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
using Fdp.Toolkit.Terrain;
using Fdp.Core;
using Fdp.Toolkit.Time.Domain;
using Fdp.Toolkit.Time.Controllers;
using Fdp.Toolkit.NetworkSpawning;
using ClusterState  = Hrot.NED.Descriptors.Orchestration.ClusterState;
using ClusterOpType = Hrot.NED.Descriptors.Orchestration.ClusterOpType;
using NodeOpType    = Hrot.NED.Descriptors.Orchestration.NodeOpType;
using FdpClusterState = Fdp.Toolkit.Orchestration.ClusterState;
using Fdp.Core.Serialization;

namespace Hrot.Orchestrator;

/// <summary>
/// Orchestrator control-plane host: system state, node heartbeats, DDS network ID allocation server,
/// bootstrap latch, heartbeat-timeout eviction, and 2PC transaction history ring buffer.
/// </summary>
public sealed partial class ClusterMaster : IDisposable
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
        /// <summary>⭐ <c>CE-3345</c> (R-3070) — the REASON the first failing node's ack carried (<i>"cancelled by the user on node 2"</i>); reaches the requester through the op status.</summary>
        public string? FailureReason;
        /// <summary>⭐ <c>CE-3345</c> (R-3070, AQ91 §4-H) — this round is a scenario LOAD (it enters <c>LoadingLive</c> / <c>LoadingEdit</c>): a failure fails it AT ONCE and sends the cluster back to Idle.</summary>
        public bool IsLoadRound;
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
        /// <summary>⭐ CE-3299 B3 — what a SUCCESSFUL round reports to its requester when the master already knows it (a checkpoint's label and time).</summary>
        public object? SuccessResult;
        /// <summary>⭐ CE-3299 B3 — report each node's answer (node id → its result) on success: a checkpoint export's file per node.</summary>
        public bool ResultFromResponses;
        /// <summary>⭐ CE-4093 — turns the aggregator's result into the one the requester gets (the check round folds in the master's own fact).</summary>
        public Func<object?, object?>? RefineResult;
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

        /// <summary>⭐ C3 (Q91 §4-M) — a trajectory through Idle: on success its <see cref="Targets"/> become the in-sync set.</summary>
        public bool ResyncsOnSuccess;
        /// <summary>Per-node response JSON strings fed into the aggregator pipeline.</summary>
        public readonly Dictionary<int, Dictionary<Fdp.Toolkit.Orchestration.NodeOpType, string>> NodeResponses = new();
    }

    /// <summary>
    /// Single unified dictionary that tracks all in-flight 2PC rounds keyed by transaction ID.
    /// Replaces the former bespoke <c>_pendingSerializeTasks</c>,
    /// <c>_pendingManageEpisodeTasks</c>, and <c>_pendingBusTransitionAcks</c> dictionaries.
    /// </summary>
    private readonly Dictionary<Guid, GenericTransactionTracker> _pendingTransactions = new();

    /// <summary>
    /// ⭐⭐ P1 (Q91 §4-B, B1) — per round, the ACKs each target node still OWES. Recorded at fan-out (every op but
    /// <c>CommitState</c>, which the slave applies without an ACK — <c>ClusterSlave.cs:411-422</c>), paid off by each ACK,
    /// and subtracted from the round when the node is EJECTED, so a round never waits for a node that is gone.
    /// ⭐ Per node and per round, not "minus one": a multi-step transition owes one ACK per prepare step.
    /// </summary>
    private readonly Dictionary<Guid, Dictionary<int, int>> _owedAcks = new();

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
    /// ⭐ CE-3021 — the explicit asset operations (publish / refresh, docs/DESIGN_Asset_Management.md §5). ⛔ A composition
    /// root that holds the gateway and the NAS path MUST set it (the silent-default rule) — null rejects both ops loudly.
    /// </summary>
    public AssetSyncService? AssetSync { get; set; }

    /// <summary>The active nodes' advertised capability tokens — what <see cref="AssetSyncService"/> reads.</summary>
    public IReadOnlyDictionary<int, string[]> ActiveNodeCapabilitySnapshot() => ActiveNodeCapabilities();

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
        ArmedMode    = _config.InitialArmedMode;   // ⭐ R-1043 — the bootstrap parameter; set before PublishStandby below
        LastLoadMode = _config.InitialArmedMode;
        _history  = new DistributedTransaction[Math.Max(1, _config.TransactionHistoryCapacity)];

        // ⭐⭐ CE-3344 (R-3069) — the latch is over names AND roles: with nothing mandatory at all it is open from the start; otherwise it is CLOSED and the
        //   first Tick opens it once every mandatory name and role has a node that reported (the host's own roles — LocalRoles — are set after construction).
        if (_config.Mandatory.Length == 0 && _config.MandatoryRoles == NodeRole.None) { _bootstrapLatch = true; PublishStandby(); }
    }

    // ── Per-frame tick ────────────────────────────────────────────────────

    public void Tick()
    {
        IngestCapabilities();   // CE-285: gather static capability tokens BEFORE heartbeats derive roles from them.
        IngestHeartbeats();
        ProcessNodeIdRequests();   // ⭐ C1 — master-assigned node ids
        CheckBootstrapLatch();
        DetectAndEjectTimedOutNodes();
        PublishWaitingForIfChanged();   // ⭐ CE-3344 — what the cluster is waiting for rides the cluster state, so every node can say it
        PublishStepRosterIfChanged();   // ⭐ P3
        DrainInjectedRequests();

        // ⭐⭐⭐ L8 — resume or expire a PARKED transition BEFORE new intents are admitted, so a transition
        //    whose files landed this frame does not cause the next request to be rejected as "busy".
        ProcessParkedTransition();
        ProcessHeldLoad();   // ⭐⭐ CE-4087 — a load waiting for its roles starts (or is refused) before new intents are admitted
        ReleaseHeldReplayExit();   // ⭐ CE-3329 F8 — an exit from a replay / review waits for the seeks in flight

        // Bus-based intent drain (CMC-S008).
        ProcessRunSettingsIntents();   // ⭐ CE-4090 D14 — BEFORE the transitions: a setting and a load in one frame honour the setting
        ProcessTransitionStateIntents();
        ProcessManageEpisodeIntents();
        ProcessStorageOpIntents();
        ProcessTakeCheckpointIntents();
        ProcessUnsavedCheckIntents();   // ⭐ CE-4093 D20
        ProcessSeekReplayIntents();
        ProcessCancelOperationIntents();
        ProcessDiagnosticDumpIntents();
        ProcessLoadZoneIntents();
        ProcessBuildTerrainAssetIntents();
        ProcessAssetOpIntents();   // ⭐ CE-3021 — publish / refresh arriving on the bus (remote panel)

        ConsumeNodeOpStatuses();
        TickCatchUps();    // ⭐ C7 — late joiners caught up by per-node ops (Q91 §4-M)
        PruneOwedAcks();   // ⭐ P1
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
            // ⭐ CE-4087 — a scenario LOAD against a cluster that is not ready WAITS (and is refused with the reason after the wait);
            //   every other request keeps today's rejection.
            if (req.OperationType == ClusterOpType.TransitionState
                && ClusterOpRequestAdapter.ToTransitionStateIntent(req) is var load && !LatchRefuses(load))   // a scenario load (waits) — or, in ERROR, the way back to Idle (CE-3345)
            {
                ProcessTransitionStateIntent(load);
                return;
            }
            // ⭐ CE-3345 — a cancel always works (CE-3344's HTTP cancel was never gated; this path rejected it while the latch was closed)
            if (req.OperationType == ClusterOpType.CancelOperation)
            {
                ProcessCancelOperationIntent(ClusterOpRequestAdapter.ToCancelOperationIntent(req));
                return;
            }
            PublishOpStatus(req.RequestId, OrchestrationStatusCode.Rejected, IsInError ? ErrorReason : null);
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
                    PublishClusterPause();
                    break;
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
            case ClusterOpType.DeleteExercise:   // ⭐ CE-4090 D15
                ProcessStorageOpIntent(ClusterOpRequestAdapter.ToExecuteStorageOpIntent(req));
                break;

            case ClusterOpType.TakeCheckpoint:
                StartCheckpoint(req.RequestId, LabelOf(req.PayloadJson));
                break;

            case ClusterOpType.CollectCheckpoint:
                CollectCheckpoint(req.RequestId, LabelOf(req.PayloadJson));
                break;
            case ClusterOpType.RestoreCheckpoint:   // ⭐ CE-3299 B4
                StartRestore(req.RequestId, LabelOf(req.PayloadJson));
                break;

            case ClusterOpType.CheckUnsavedChanges:   // ⭐ CE-4093 D20 — the unsaved-changes check round (no payload)
                StartUnsavedCheck(req.RequestId);
                break;

            case ClusterOpType.ReplaySeek:
                ProcessSeekReplayIntent(ClusterOpRequestAdapter.ToSeekReplayIntent(req));
                break;

            case ClusterOpType.SetReplayAxis:   // ⭐ CE-3329 R-3058
                ProcessSetReplayAxisIntent(SetReplayAxisPayloadDto.Decode(req.RequestId, req.PayloadJson));
                break;

            case ClusterOpType.SetRunSettings:   // ⭐ CE-4090 D14
                ProcessSetRunSettingsIntent(SetRunSettingsPayloadDto.Decode(req.RequestId, req.PayloadJson));
                break;

            case ClusterOpType.SetReplayLoop:   // ⭐ CE-3329 §12n
                ProcessSetReplayLoopIntent(SetReplayLoopPayloadDto.Decode(req.RequestId, req.PayloadJson));
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

            // ⭐ CE-3021 — the user-triggered asset operations, any time (C4/C5).
            case ClusterOpType.PublishAssets:
            case ClusterOpType.RefreshAssets:
            {
                var dto = ClusterOpRequestAdapter.ToAssetOpPayload(req);
                ProcessAssetOp(req.RequestId, req.OperationType == ClusterOpType.RefreshAssets, dto?.Kind, dto?.NodeId ?? 0);
                break;
            }
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
            bool joined = !_roster.ActiveNodes.ContainsKey(hb.NodeId);
            var profile = new NodeHealthProfile
            {
                NodeId                  = hb.NodeId,
                SubsystemName           = hb.SubsystemName ?? string.Empty,
                LocalClusterState       = (ClusterState)(int)hb.LocalStateId,
                LastHeartbeatUtcSeconds = UtcNowSeconds(),
                SessionId               = hb.SessionId,   // ⭐ C2 — the session the node follows (telemetry)
                Phase                   = hb.Phase,       // ⭐ CE-3345 (AQ110 A) — what the node says about ITSELF
                Step                    = hb.Step ?? string.Empty,
                Reason                  = hb.Reason ?? string.Empty,
                Capabilities            = new System.Collections.Generic.HashSet<string>(tokens),
                Roles                   = NodeRoleTokens.MaskFromTokens(tokens),
            };
            _roster.Upsert(profile);

            // ⭐⭐ R-4018 / CE-4003 step 2 — a node ENTERING the roster gets the deployment's TKB synced (the saga does
            //   the copy). ⭐ Nothing is sent to the node: its first world build loads the file. 📄 tkb-1 DESIGN §0a.
            if (joined)
            {
                _eventBus.PublishManaged(new NodeJoinedRosterEvent { NodeId = hb.NodeId });
                AdmitJoinedNode(hb.NodeId);   // ⭐ C3 — in sync at once only while the cluster is at rest in Idle
            }
        }
    }

    /// <summary>CE-285 (C-cap): gather each node's durable capability token set. Stored in a side-map so every
    /// heartbeat can re-apply it (the heartbeat rebuilds the profile). A late advert also immediately corrects
    /// an already-present profile's <see cref="NodeHealthProfile.Capabilities"/> + derived
    /// <see cref="NodeHealthProfile.Roles"/> without waiting for the next heartbeat.</summary>
    /// <summary>⭐ CE-3020 — every active node's capability tokens, for the prefetch saga's asset sync
    /// (docs/DESIGN_Asset_Management.md §4).</summary>
    private Dictionary<int, string[]> ActiveNodeCapabilities()
    {
        var map = new Dictionary<int, string[]>();
        foreach (var id in _roster.ActiveNodes.Keys)
            map[id] = _nodeCapabilities.TryGetValue(id, out var t) ? t : System.Array.Empty<string>();
        return map;
    }

    /// <summary>
    /// ⭐ CE-3021 — run a publish or refresh off the main thread and report it on the op-status channel. ⛔ Never
    /// automatic: only a <see cref="ClusterOpRequest"/> reaches here (docs/DESIGN_Asset_Management.md §7.5, Q72-I).
    /// </summary>
    private void ProcessAssetOpIntents()
    {
        foreach (var intent in _eventBus.ReadManaged<AssetOpIntent>())
            ProcessAssetOp(intent.RequestId, intent.Refresh, intent.Kind, intent.NodeId);
    }

    private void ProcessAssetOp(Guid requestId, bool refresh, string? kind, int nodeId)
    {
        string op = refresh ? "RefreshAssets" : "PublishAssets";
        if (AssetSync == null || string.IsNullOrWhiteSpace(kind))
        {
            FdpLog<ClusterMaster>.Warn(
                $"[Orchestrator] {op} {requestId} rejected: "
              + (AssetSync == null ? "this orchestrator composes no AssetSyncService." : "payload must be {\"Kind\":…, \"NodeId\":…}."));
            PublishOpStatus(requestId, OrchestrationStatusCode.Rejected);
            return;
        }

        PublishOpStatus(requestId, OrchestrationStatusCode.InProgress);
        System.Threading.Tasks.Task<bool> work = !refresh
            ? AssetSync.PublishToNasAsync(kind, nodeId).ContinueWith(t => t.Result.FailureCount == 0,
                  System.Threading.Tasks.TaskContinuationOptions.OnlyOnRanToCompletion)
            : AssetSync.RefreshFromNasAsync(kind, nodeId).ContinueWith(_ => true,
                  System.Threading.Tasks.TaskContinuationOptions.OnlyOnRanToCompletion);

        work.ContinueWith(t =>
        {
            bool ok = t.Status == System.Threading.Tasks.TaskStatus.RanToCompletion && t.Result;
            if (!ok)
                FdpLog<ClusterMaster>.Error(
                    $"[Orchestrator] asset op {requestId} failed: {t.Exception?.GetBaseException().Message ?? "copy failures"}");
            PublishOpStatus(requestId, ok ? OrchestrationStatusCode.Success : OrchestrationStatusCode.Failure);
        }, System.Threading.Tasks.TaskScheduler.Default);
    }

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
        if (IsInError) return;   // ⭐ CE-3345 — a cluster in ERROR stays "not ready" until it reaches Idle, whatever returns meanwhile (sticky)

        // ⭐⭐ CE-3344 (R-3069) — CLUSTER READY = every mandatory NAME and ROLE has a node that reported. ⭐ CE-4087 — each test lives in ONE place
        //   (ClusterMaster.Bootstrap.cs), shared with the load wait's "waiting for" text: names = a roster entry in Idle, roles = a node whose capability advert
        //   landed (or this host's own LocalRoles).
        if (MissingMandatoryNames().Count > 0 || MissingMandatoryRoles().Count > 0) return;

        // Every mandatory name and role is present — latch released.
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
        ForgetSync(nodeId);   // ⭐ C3 — I1: a node that left is no longer in sync
        FdpLog<ClusterMaster>.Warn("[Orchestrator] Node {0} ({1}) ejected (heartbeat timeout).",
            nodeId, profile.SubsystemName);

        // ⭐⭐ P1 — for EVERY node, mandatory or not: a round never waits for a node that is gone.
        DropFromInFlightRounds(nodeId);

        // ⭐⭐ CE-3344 (R-3069, user 2026-10-29: "yes, role loss means Degraded too") — losing a mandatory NAME or leaving a mandatory ROLE with no node degrades the cluster
        //   through the same path. ⚠ Roles are tested only while the latch is OPEN: with it closed the cluster is not ready anyway and a role is missing for a reason
        //   that is not this ejection (a booting cluster).
        bool isMandatory = Array.IndexOf(_config.Mandatory, profile.SubsystemName) >= 0;
        bool lostRole    = _bootstrapLatch && MissingMandatoryRoles().Count > 0;
        // ⭐⭐ CE-3345 (R-3073) — a mandatory node / role lost while a scenario is LOADED or RUNNING is the cluster ERROR state, not a teardown: Degraded + the reason, the clock paused, the
        //   world kept on every node, the live recording closed and kept — and no abort, no Idle to the survivors. ⭐ A second loss while already in error is also handled there. In Idle there is
        //   no world to lose: the old behaviour below stays (only "not ready", the latch re-engaged, no error).
        lostRole = lostRole || (IsInError && MissingMandatoryRoles().Count > 0);
        if (IsInError || ((isMandatory || lostRole) && HasWorldLoaded))
        {
            EnterClusterError(nodeId, profile.SubsystemName, isMandatory);
            return;
        }
        if (!isMandatory && !lostRole) return;

        // Abort any in-flight transaction.
        if (_activeTransaction != null)
        {
            _activeTransaction.IsAborted = true;
            AppendToHistory(_activeTransaction);
            _activeTransaction = null;
        }

        // ⭐ CE-3344 — the latch is re-engaged BEFORE the broadcast so the Degraded state carries what is now missing (WaitingFor).
        _bootstrapLatch = false;

        // Publish Degraded system state.
        PublishClusterState(ClusterState.Degraded);
        FdpLog<ClusterMaster>.Warn("[Orchestrator] System entered Degraded state (mandatory {0} lost: node {1}, {2}).",
            lostRole && !isMandatory ? "role" : "node", nodeId, profile.SubsystemName);

        // Broadcast AbortTransaction + PrepareState(Standby) to surviving nodes only.
        var survivingIds = new List<int>(_roster.ActiveNodes.Keys);
        FanOutNodeOp(NodeOpType.AbortTransaction, Guid.NewGuid(), null, survivingIds);
        FanOutNodeOp(NodeOpType.PrepareState,     Guid.NewGuid(), (int)ClusterState.Idle, survivingIds);

        // The bootstrap latch stays engaged (set above) until the mandatory node / role returns (CheckBootstrapLatch).
    }

    // ── Bus-path intent drain methods (CMC-S008) ──────────────────────────

    private void ProcessTransitionStateIntents()
    {
        foreach (var intent in _eventBus.ReadManaged<TransitionStateIntent>())
        {
            // ⭐ CE-4087 — a scenario load WAITS on a closed latch (ProcessTransitionStateIntent holds it); anything else is rejected as before.
            //   ⭐ CE-3345 — and, while the cluster is in ERROR, a way back to Idle (Close): a way out is never gated.
            if (LatchRefuses(intent)) { PublishOpStatus(intent.TransactionId, OrchestrationStatusCode.Rejected, IsInError ? ErrorReason : null); continue; }
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
            StartCheckpoint(intent.RequestId, intent.Label);
        foreach (var intent in _eventBus.ReadManaged<CollectCheckpointIntent>())
            CollectCheckpoint(intent.RequestId, intent.Label);
        foreach (var intent in _eventBus.ReadManaged<RestoreCheckpointIntent>())
            StartRestore(intent.RequestId, intent.Label);
    }

    // ── ⭐ CE-3299 B3 (R-3044) — a checkpoint is a named keyframe every node's recording writes at ONE wall time ──

    /// <summary>
    /// ⭐ CE-3299 B3 (D3) — how far ahead of NOW the master names a checkpoint's wall time: long enough for the request to reach
    /// every node first, so each writes it on its first frame at or past that time — within one frame of each other.
    /// </summary>
    public static readonly long CheckpointLeadTicks = TimeSpan.FromMilliseconds(500).Ticks;

    /// <summary>
    /// ⭐ CE-3299 B3 — the cluster's frame-locked wall clock (<c>GlobalTime.TotalWallTicks</c>, which the slaves follow), set by the
    /// host that owns the time controller (<c>OrchestratorCore</c>). ⚠ Null ⇒ a checkpoint is written at each node's NEXT frame
    /// (logged): correct, but the nodes then differ by the message latency.
    /// </summary>
    public Func<long>? WallTicks { get; set; }

    /// <summary>⭐ R-3055 — the newest FINISHED exercise the master can replay (its asset inventory), set by the host that owns the
    /// inventory (<c>OrchestratorCore</c>); <see langword="null"/> ⇒ none known.</summary>
    public Func<Guid?>? NewestExercise { get; set; }

    /// <summary>⭐ R-3055 — the exercise a replay with no named exercise replays: the live run in progress, else the newest finished one.</summary>
    internal Guid ResolveReplayExercise()
        => _currentDsmState == ClusterState.OperatingLive && _currentExerciseId != Guid.Empty
            ? _currentExerciseId
            : NewestExercise?.Invoke() ?? Guid.Empty;

    private int _checkpointSeq;

    private void StartCheckpoint(Guid requestId, string? label)
    {
        // ⭐ CE-4090 (AQ108 D14, §5a) — a checkpoint is a keyframe of the run's recording: no recording, no checkpoint — refused here,
        //   ⛔ not a throw on every node (ReferenceCheckpointHandler).
        if (_runNotRecorded) { RefuseNotRecorded(requestId, "checkpoint"); return; }
        var nodeIds = Participants();   // ⭐ C3 — operator rounds: in-sync nodes only
        long now = WallTicks?.Invoke() ?? 0;
        if (WallTicks == null)
            FdpLog<ClusterMaster>.Warn("[Orchestrator] TakeCheckpoint: no wall clock — each node writes the checkpoint at its next frame.");
        long at = now == 0 ? 0 : now + CheckpointLeadTicks;
        // an unnamed checkpoint is numbered as well as timed: two in one second must not share a name (the recording refuses it)
        var payload = new CheckpointPayload(
            string.IsNullOrWhiteSpace(label)
                ? $"cp{++_checkpointSeq}-{new DateTime(at == 0 ? DateTime.UtcNow.Ticks : at, DateTimeKind.Utc):HHmmss}"
                : label!,
            at);
        if (nodeIds.Count == 0)
        {
            PublishOpStatus(requestId, OrchestrationStatusCode.Success, payload);
            return;
        }
        var txId = Guid.NewGuid();
        FanOutNodeOp(NodeOpType.TakeSnapshot, txId, payload, nodeIds);
        _pendingTransactions[txId] = new GenericTransactionTracker
        {
            RequestId                       = requestId,
            Expected                        = nodeIds.Count,
            BroadcastClusterStateOnComplete = true,
            SuccessResult                   = payload,
        };
    }

    private void CollectCheckpoint(Guid requestId, string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            FdpLog<ClusterMaster>.Warn("[Orchestrator] CollectCheckpoint: no label — nothing to collect.");
            PublishOpStatus(requestId, OrchestrationStatusCode.Rejected);
            return;
        }
        var nodeIds = Participants();
        if (nodeIds.Count == 0)
        {
            PublishOpStatus(requestId, OrchestrationStatusCode.Success);
            return;
        }
        var txId = Guid.NewGuid();
        FanOutNodeOp(NodeOpType.ExportCheckpoint, txId, new ExportCheckpointPayload(label!), nodeIds);
        _pendingTransactions[txId] = new GenericTransactionTracker
        {
            RequestId           = requestId,
            Expected            = nodeIds.Count,
            ResultFromResponses = true,
        };
    }

    // ── ⭐ CE-3299 B4 — restore a checkpoint: every node goes back to its keyframe X and records on as a NEW exercise ──

    /// <summary>
    /// ⭐ CE-3299 B4 (§1h) — phase 1 of a restore. Only from <c>OperatingLive</c> (a replay already has seek + go-live). The cluster
    /// pauses first; every node then reads and STAGES its keyframe X (nothing changes yet). Only when ALL staged does phase 2
    /// restore — a node that cannot (not recording, no mark X) fails the round and the others drop what they staged
    /// (<c>AbortTransaction</c>), so no node is left restored alone. The exercise the cluster records on is minted HERE (D18).
    /// </summary>
    private void StartRestore(Guid requestId, string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            FdpLog<ClusterMaster>.Warn("[Orchestrator] RestoreCheckpoint: no label — nothing to restore.");
            PublishOpStatus(requestId, OrchestrationStatusCode.Rejected);
            return;
        }
        if (_runNotRecorded) { RefuseNotRecorded(requestId, "restore of checkpoint"); return; }   // ⭐ CE-4090 D14 — nothing to restore from
        if (_currentDsmState != ClusterState.OperatingLive)
        {
            FdpLog<ClusterMaster>.Warn(
                "[Orchestrator] RestoreCheckpoint '{0}' refused in {1}: a restore is from a live cluster (in a replay, seek and go live).",
                label, _currentDsmState);
            PublishOpStatus(requestId, OrchestrationStatusCode.Rejected);
            return;
        }
        var targets = Participants();   // ⭐ C3 — operator rounds: in-sync nodes only
        if (targets.Count == 0)
        {
            PublishOpStatus(requestId, OrchestrationStatusCode.Success);
            return;
        }

        PublishClusterPause();   // a restore replaces the world: nothing may run under it
        var payload = new RestoreCheckpointPayload(label!, Guid.NewGuid());
        var roundTx = Guid.NewGuid();   // ONE id for both phases — the nodes stage under it (the zone round's rule)
        FanOutNodeOp(NodeOpType.PrepareRestore, roundTx, payload, targets);
        _pendingTransactions[roundTx] = new GenericTransactionTracker
        {
            RequestId      = requestId,
            Expected       = targets.Count,
            Targets        = targets,
            OnPhaseSuccess = () => CommitRestore(requestId, payload, roundTx, targets),
        };
        PublishOpStatus(requestId, OrchestrationStatusCode.InProgress);
        FdpLog<ClusterMaster>.Info("[Orchestrator] RestoreCheckpoint '{0}' {1}: PrepareRestore to {2} node(s).", label, requestId, targets.Count);
    }

    /// <summary>⭐ CE-3299 B4 — phase 2: every node staged keyframe X, so all restore it and record on as the new exercise.</summary>
    private void CommitRestore(Guid requestId, RestoreCheckpointPayload payload, Guid roundTx, List<int> targets)
    {
        _activeExerciseId  = payload.NewExerciseId;   // D18 — the cluster records a new exercise from here
        _currentExerciseId = payload.NewExerciseId;
        FanOutNodeOp(NodeOpType.RestoreSnapshot, roundTx, payload, targets);
        _pendingTransactions[roundTx] = new GenericTransactionTracker
        {
            RequestId                       = requestId,
            Expected                        = targets.Count,
            BroadcastClusterStateOnComplete = true,   // carries the new exercise id
            // ⭐ terminal: CheckpointRestoreAggregator reports the earliest restored time; LiveBranchProcessManager snaps to it (D5)
        };
        FdpLog<ClusterMaster>.Info("[Orchestrator] RestoreCheckpoint '{0}' {1}: every node staged — RestoreSnapshot, exercise {2}.",
            payload.Label, requestId, payload.NewExerciseId);
    }

    /// <summary>
    /// ⭐ CE-3299 B3 — a checkpoint request's label: the payload's <c>label</c> field when it is a JSON object, else the payload
    /// text itself; null when there is none.
    /// </summary>
    internal static string? LabelOf(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson)) return null;
        var text = payloadJson.Trim();
        if (!text.StartsWith("{")) return text.Trim('"');
        try
        {
            using var doc = JsonDocument.Parse(text);
            foreach (var p in doc.RootElement.EnumerateObject())
                if (string.Equals(p.Name, "label", StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.String)
                    return p.Value.GetString();
        }
        catch (JsonException) { }
        return null;
    }

    private static Dictionary<int, string> ResponsesByNode(Dictionary<int, Dictionary<Fdp.Toolkit.Orchestration.NodeOpType, string>> responses)
    {
        var byNode = new Dictionary<int, string>();
        foreach (var (node, ops) in responses)
            foreach (var answer in ops.Values) byNode[node] = answer;
        return byNode;
    }

    private void ProcessSeekReplayIntents()
    {
        foreach (var intent in _eventBus.ReadManaged<SeekReplayIntent>())
        {
            ProcessSeekReplayIntent(intent);
        }
        foreach (var intent in _eventBus.ReadManaged<SetReplayAxisIntent>())
            ProcessSetReplayAxisIntent(intent);   // ⭐ CE-3329 R-3058
        foreach (var intent in _eventBus.ReadManaged<SetReplayLoopIntent>())
            ProcessSetReplayLoopIntent(intent);   // ⭐ CE-3329 §12n
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
        var targets = Participants();   // ⭐ C3 — operator rounds: in-sync nodes only
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
        var targets = Participants();   // ⭐ C3 — operator rounds: in-sync nodes only
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

    // ⭐⭐ CE-3329 F8 — a seek still in flight when the cluster leaves a replay / review would land AFTER the exit and snap the live clock
    //   back into history (ReplaySeekProcessManager snaps on the seek's completion). ⇒ the exit WAITS for the seeks (one held exit, like
    //   L8's one parked transition; a second is refused), and no new seek is admitted while it waits or outside a replay / review.
    private readonly HashSet<Guid> _seekTx = new();
    private TransitionStateIntent? _heldReplayExit;

    /// <summary>⭐ CE-3329 F8 — a seek round is still pending. ⭐ The release runs at the START of the master's tick, before it consumes the
    /// round's acks, so a held exit fans out a tick after the seek completed — after its clock snap was requested.</summary>
    private bool SeeksInFlight()
    {
        _seekTx.RemoveWhere(id => !_pendingTransactions.ContainsKey(id));
        return _seekTx.Count > 0;
    }

    private void ReleaseHeldReplayExit()
    {
        if (_heldReplayExit is not { } exit || SeeksInFlight()) return;
        _heldReplayExit = null;
        FdpLog<ClusterMaster>.Info("[Orchestrator] CE-3329 F8: the seeks have landed — exit {0} to {1} proceeds.", exit.TransactionId, exit.TargetState);
        ProcessTransitionStateIntent(exit);
    }

    private void ProcessTransitionStateIntent(TransitionStateIntent intent)
    {
        var requestId = intent.TransactionId;


        // ⭐ CE-4087 — a newer request replaces a load that waits on the mandatory set (as ScenarioLoadSequence does caller-side) …
        SupersedeHeldLoad(requestId);
        // … and a scenario load WAITS on the mandatory set — subsystem names (the bootstrap latch) and roles — instead of being rejected
        //   or started into a cluster nobody can run. Held before any state is touched. 📄 ClusterMaster.Bootstrap.cs.
        if (HoldLoadUntilMandatory(intent)) return;

        // ⭐ CE-3329 F8 — leaving a replay / review drains the seeks first
        var showing = (FdpClusterState)(int)_currentDsmState;
        if (ClusterStates.IsReplaying(showing) && !ClusterStates.IsReplaying(intent.TargetState) && SeeksInFlight())
        {
            if (_heldReplayExit != null)
            {
                FdpLog<ClusterMaster>.Warn("[Orchestrator] CE-3329 F8: transition {0} REJECTED — exit {1} already waits for the seeks in flight.",
                    requestId, _heldReplayExit.Value.TransactionId);
                PublishOpStatus(requestId, OrchestrationStatusCode.Rejected);
                return;
            }
            _heldReplayExit = intent;
            FdpLog<ClusterMaster>.Info("[Orchestrator] CE-3329 F8: exit {0} to {1} waits for the seeks in flight.", requestId, intent.TargetState);
            return;
        }

        // ⭐ CE-4065 — LoadingLive is the branch's PASSAGE, not a place to stop: asked for from a replay it parked the cluster there with the
        //   clock frozen. Refused, naming the branch's real target — one way to branch, OperatingLive (ReplaySessionIntents.BranchLive()).
        if (ClusterStates.IsReplaying(showing) && intent.TargetState == FdpClusterState.LoadingLive)
        {
            FdpLog<ClusterMaster>.Warn("[Orchestrator] transition {0} REJECTED — LoadingLive is passed through, not stopped in: to branch a replay live, ask for OperatingLive.", requestId);
            PublishOpStatus(requestId, OrchestrationStatusCode.Rejected);
            return;
        }

        // ⭐⭐ R-3055 (the R-3049 rule, for a replay) — a replay whose request names no exercise gets one from the MASTER: the run in
        //    progress when the cluster is live (the planner ends it on the way in), else the newest finished one. ⛔ It used to be chosen
        //    by the asker from a cached inventory, so the same request worked on the editor and was refused on CGF.
        if (intent.TargetState == FdpClusterState.OperatingReplay && intent.ExerciseId == Guid.Empty)
        {
            // ⭐ CE-4090 (AQ108 D14, §5a) — "replay the last run" when the last run was not recorded: refused, naming why — ⛔ never
            //   silently the newest OLDER exercise, which the asker did not mean.
            if (_runNotRecorded) { RefuseNotRecorded(requestId, "replay of the last run"); return; }
            var resolved = ResolveReplayExercise();
            if (resolved == Guid.Empty)
            {
                FdpLog<ClusterMaster>.Warn("[Orchestrator] replay {0} REJECTED — it names no exercise and none has run.", requestId);
                PublishOpStatus(requestId, OrchestrationStatusCode.Rejected);
                return;
            }
            FdpLog<ClusterMaster>.Info("[Orchestrator] R-3055: the replay names no exercise — the master picks {0}.", resolved);
            intent.ExerciseId = resolved;
        }

        // ⭐ CE-3329 R11 — a replay entered at a RECORDED sim time: resolved when the prepare round brings the nodes' maps
        if (intent.TargetState == FdpClusterState.OperatingReplay) _replayEntrySimSeconds = intent.TargetSimSeconds;

        // ⭐⭐ CE-3329 P3 (R5, §3a) — a REVIEW of the live run: only from OperatingLive (the planner would otherwise LOAD a live run from
        //   Idle to reach it); a review requested during a review is a seek. Where it enters: the request's moment, else the live point.
        if (intent.TargetState == FdpClusterState.OperatingReview)
        {
            var current = (FdpClusterState)(int)_currentDsmState;
            if (current == FdpClusterState.OperatingReview)
            {
                ProcessSeekReplayIntent(new SeekReplayIntent
                {
                    RequestId = requestId, TargetSimSeconds = intent.TargetSimSeconds,
                    TargetWallTicks = intent.TargetWallTicks != 0 || intent.TargetSimSeconds != null ? intent.TargetWallTicks : long.MaxValue,
                });
                return;
            }
            if (current != FdpClusterState.OperatingLive)
            {
                FdpLog<ClusterMaster>.Warn("[Orchestrator] review {0} REJECTED — a review starts only from a live run (the cluster is {1}).",
                    requestId, current);
                PublishOpStatus(requestId, OrchestrationStatusCode.Rejected);
                return;
            }
            // ⭐ CE-4090 (AQ108 D14, §5a) — DAR needs the run's recording: refused HERE, at the master, before anything pauses the live
            //   clock (ReviewProcessManager skips the pause for the same reason) — ⛔ it used to pause first and throw in every node's prepare.
            if (_runNotRecorded) { RefuseNotRecorded(requestId, "review (DAR)"); return; }
            _replayEntrySimSeconds = intent.TargetSimSeconds;
            _reviewEntryWallTicks  = intent.TargetWallTicks != 0 || intent.TargetSimSeconds != null ? intent.TargetWallTicks : long.MaxValue;
            if (intent.ExerciseId == Guid.Empty) intent.ExerciseId = _currentExerciseId;   // the run being reviewed
        }

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

        // ⭐ Q86 §4-F (R-215) — a request that asks to start paused pauses the cluster through the SAME path as Stop
        //   (PublishClusterPause), every time. ⛔ It used to set PendingTimeMode, which the orchestrator HOST turned into a
        //   direct clock call — and only on a CHANGE of value, so a second start-paused load in a row did not pause.
        if (passesSimulationStart && intent.TimeMode == "Deterministic")
            PublishClusterPause();

        // ⭐⭐ CE-3068 — STOP PAUSES THE CLOCK. 🔒 User, `2026-10-05`: *"Stop should pause the clock, for sure."*
        //    📐 Measured before: after Live → Play → Stop the master clock kept running through Idle and into the
        //    next Edit (dt ≈ 0.01, units moving while the author edits) — the only pause producer was the explicit
        //    PauseTime op. ⭐ The SAME path the Pause button takes, so the slaves see an ordinary pause; Play stays
        //    an explicit operator action, exactly as after boot (CE-101). 📄 mgmt-1 DESIGN §8.12.
        //    ⚠ A pause while already paused only re-arms the barrier (dt stays 0) — harmless, as the replay
        //    managers' unconditional pauses already rely on.
        //    ⭐ Both stops: Live → Idle, and Preview → Edit (the editor's own ExitPreviewMode re-pauses the same way,
        //    EditorSubsystem.cs:675). Entering Edit at all pauses too — §8.12's invariant, not a per-path rule.
        if ((resolvedTarget == ClusterState.Idle || resolvedTarget == ClusterState.OperatingEdit)
            && _currentDsmState != resolvedTarget)
            PublishClusterPause();

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
                NodeCapabilities = ActiveNodeCapabilities(),
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
        NoteLoadForUnsavedCheck(resolvedTarget, intent.ScenarioId);   // ⭐ CE-4093 — a new scenario (or none): the old save failure / answer no longer apply
        // ⭐ CE-3329 §12n — a new session starts unlooped: a stale bracket from the last exercise is never enforced.
        if (!ClusterStates.IsReplaying((FdpClusterState)(int)resolvedTarget)) _replayLoop = null;

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

        // ⭐⭐ D18 ① (R-3047, CE-3299 B4) — a branch from a replay records a NEW exercise, and the MASTER mints its id. ⛔ The
        //   request's id was forwarded unchanged, so a client sending the REPLAYED exercise's id truncated that recording, and an
        //   empty one sent every branch to one folder. The node copies its history up to the branch point into it.
        var exerciseId = intent.ExerciseId;
        if (capturedSourceState == ClusterState.OperatingReplay
            && trajectory.OfType<TransitionStep>().Any(t => t.TargetState == ClusterState.LoadingLive))
        {
            exerciseId = Guid.NewGuid();
            if (intent.ExerciseId != Guid.Empty)
                FdpLog<ClusterMaster>.Info(
                    "[Orchestrator] D18: the branch records exercise {0}; the request's id {1} is ignored (the master mints a branch's id).",
                    exerciseId, intent.ExerciseId);
            _activeExerciseId  = exerciseId;
            _currentExerciseId = exerciseId;
        }
        // ⭐⭐ CE-3310 (R-3049, user 2026-10-22: "master mints the id when missing") — a LIVE LOAD whose request names no exercise
        //   gets one from the master (mgmt-1 step 5: "Master → Master: Generate ExerciseId"). ⛔ It used to go live with an empty id,
        //   and a node records nothing without one — so no checkpoint either. A request that names its id keeps it (a script replays
        //   the exercise it named).
        else if (exerciseId == Guid.Empty && LoadsLiveFromIdle(capturedSourceState, trajectory))
        {
            exerciseId = Guid.NewGuid();
            FdpLog<ClusterMaster>.Info("[Orchestrator] CE-3310: the live load names no exercise — the master mints {0}.", exerciseId);
            _activeExerciseId  = exerciseId;
            _currentExerciseId = exerciseId;
        }

        // ⭐ CE-3299 D17 (R-3045) — a live LOAD sets the session's recording mode; a branch keeps the session's (the nodes inherit the
        //   parent recording's), and a late joiner's catch-up is told the current one.
        if (LoadsLiveFromIdle(capturedSourceState, trajectory)) _currentCheckpointingSeconds = Math.Max(0, intent.CheckpointingSeconds);

        // ⭐⭐ CE-4090 (AQ108 D14, §5a) — LIVE DOES NOT IMPLY RECORDING: a live LOAD, and a branch from a replay ("the branch follows the
        //   setting like any live run"), take the cluster's "Record live runs" setting — or the verifier's force — as of NOW. It rides the
        //   payload below to every node and every cluster-state publication to every host.
        NoteRunRecording(intent,
            startsLiveRun: LoadsLiveFromIdle(capturedSourceState, trajectory)
                        || (capturedSourceState == ClusterState.OperatingReplay
                            && trajectory.OfType<TransitionStep>().Any(t => t.TargetState == ClusterState.LoadingLive)),
            resolvedTarget: (FdpClusterState)(int)resolvedTarget);

        // ── HN-037: the world boundary resets the ONE id authority ───────────────────────
        // ⭐ BEFORE the fan-out, because PrepareLive is what makes CGF allocate the authored ids.
        ResetIdAuthorityIfWorldBoundary(trajectory, capturedSourceState);

        // S0502: Fan out PrepareXxx + CommitState.
        // ⭐⭐ C3 (Q91 §4-M, J1) — to the IN-SYNC nodes; a trajectory that passes through Idle goes to the whole roster and
        //   leaves every node that answers it in sync (Idle is the universal re-sync point, R-4019).
        bool throughIdle = trajectory.OfType<TransitionStep>().Any(t => t.TargetState == ClusterState.Idle);
        var activeNodeIds = throughIdle ? new List<int>(_roster.ActiveNodes.Keys) : Participants();
        // ⭐ R-1043 — the mode this load round goes into (a FACT, never interpreted by the master): a LoadingEdit step => Preview, a LoadingLive step => Live; set as the round STARTS, rides the next publication
        if (trajectory.OfType<TransitionStep>().Any(t => t.TargetState is ClusterState.LoadingLive or ClusterState.LoadingEdit))
            LastLoadMode = trajectory.OfType<TransitionStep>().Any(t => t.TargetState == ClusterState.LoadingEdit) ? ClusterArmedMode.Preview : ClusterArmedMode.Live;
        if (intent.ScenarioId != null && trajectory.OfType<TransitionStep>().Any(t =>
                t.TargetState is ClusterState.LoadingLive or ClusterState.LoadingEdit))
        {
            _currentScenarioId = intent.ScenarioId;   // ⭐ C7 — what a late joiner's catch-up stages
            _currentExerciseId = intent.ExerciseId;
        }
        if (activeNodeIds.Count > 0)
        {
                var enteredFrom = capturedSourceState;   // CE-2101 — the state each step is entered FROM
                foreach (var step in trajectory)
                {
                    if (step is TransitionStep tStep)
                    {
                        bool worldBoundary = IsWorldBoundaryLoad(enteredFrom, tStep.TargetState);
                        enteredFrom = tStep.TargetState;
                        NodeOpType prepareOp = tStep.TargetState switch
                        {
                            ClusterState.LoadingLive     => NodeOpType.PrepareLive,
                            ClusterState.UnloadingLive   => NodeOpType.FinalizeLive,
                            ClusterState.LoadingReplay   => NodeOpType.PrepareReplay,
                            ClusterState.UnloadingReplay => NodeOpType.FinalizeReplay,
                            ClusterState.LoadingReview   => NodeOpType.PrepareReview,    // ⭐ CE-3329 P3
                            ClusterState.UnloadingReview => NodeOpType.FinalizeReview,
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

                        // ⭐ AQ107 C — a world-boundary load parks the terrain's origin; it is published when this round succeeds.
                        if (isLoadStep && worldBoundary) NoteLoadGeoReference(tx.TransactionId, contentNames.TerrainName);

                        var preparePayload = new EditLoadHandlerPayload(
                            isLoadStep ? intent.ScenarioId : null,
                            false,
                            (FdpClusterState)(int)tStep.TargetState,
                            ExerciseId:  exerciseId,
                            TerrainName: contentNames.TerrainName,
                            // ⭐ CE-2101 — every node clears its world before this load (the world boundary).
                            IsWorldBoundary: worldBoundary,
                            CheckpointingSeconds: _currentCheckpointingSeconds,   // D17 — the session's recording mode
                            // ⭐ CE-4090 D14 — the live session's recording decision, the same on every node (only a live step reads it)
                            RecordingOff: _runNotRecorded
                                       && tStep.TargetState is ClusterState.LoadingLive or ClusterState.OperatingLive);

                        FanOutNodeOp(prepareOp,             tx.TransactionId, preparePayload,              activeNodeIds);
                        FanOutNodeOp(NodeOpType.CommitState, tx.TransactionId,
                            new CommitStatePayload((FdpClusterState)(int)tStep.TargetState), activeNodeIds);
                    }
                    // ⭐⭐ C9 (Q91 §4-P P2) — a ReplaySeek step is NOT fanned out here: its payload would be built before any
                    //   node answered the prepare round (no replay zero yet), and with a throw-away transaction it never ran
                    //   as a round, so nothing snapped the clock (measured live). NoteReplayEntry carries the moment to the
                    //   seek round that starts when this transition completes.
                }
                NoteReplayEntry(tx.TransactionId, trajectory);

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
                    Targets                      = activeNodeIds,
                    ResyncsOnSuccess             = throughIdle,   // ⭐ C3
                    IsLoadRound                  = trajectory.OfType<TransitionStep>().Any(t => t.TargetState is ClusterState.LoadingLive or ClusterState.LoadingEdit),   // ⭐ CE-3345
                };
            }
            else
            {
                // No nodes registered or no prepare steps — complete immediately.
                if (resolvedTarget == ClusterState.Idle && ClearClusterError()) PublishClusterState(ClusterState.Idle);   // ⭐ CE-3345 — Idle reached with no node to ask
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
                Fdp.Toolkit.Orchestration.ContentLayout.ResolveScenarioDirectory(_config.NasBasePath, scenarioId!));
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

    /// <summary>
    /// ⭐ THE world-boundary rule (DESIGN_Deterministic_Network_Ids §11g.4): a <c>Loading{Live,Edit}</c> step entered FROM
    /// <c>Idle</c> — the only state with no world, so the only place a load CREATES one. ⛔ Not live-from-replay
    /// (<c>OperatingReplay → LoadingLive</c> continues the replayed world). ⭐ ONE rule for both consumers: the id
    /// authority reset (HN-037) and the node world clear (CE-2101).
    /// </summary>
    // ⭐ CE-3310 — does this trajectory LOAD a live session (a LoadingLive step entered from Idle)?
    private static bool LoadsLiveFromIdle(ClusterState source, IEnumerable<ISysOpStep> trajectory)
    {
        var from = source;
        foreach (var step in trajectory)
        {
            if (step is not TransitionStep t) continue;
            if (from == ClusterState.Idle && t.TargetState == ClusterState.LoadingLive) return true;
            from = t.TargetState;
        }
        return false;
    }

    internal static bool IsWorldBoundaryLoad(ClusterState enteredFrom, ClusterState target)
        => enteredFrom == ClusterState.Idle
        && (target == ClusterState.LoadingLive || target == ClusterState.LoadingEdit);

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

            if (IsWorldBoundaryLoad(previousState, ts.TargetState))
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
                    NodeCapabilities = ActiveNodeCapabilities(),
                });
            }
            else if (step is OperationStep { Operation: ClusterOpType.ManageEpisode })
            {
                var nodeOp  = intent.IsStart ? NodeOpType.StartEpisode : NodeOpType.StopEpisode;
                var txId    = Guid.NewGuid();
                var nodeIds = Participants();   // ⭐ C3 — operator rounds: in-sync nodes only

                var episodePayload = new EpisodeHandlerPayload(intent.EpisodeId, intent.ScenarioId, intent.IsStart);
                FanOutNodeOp(nodeOp, txId, episodePayload, nodeIds);

                if (nodeIds.Count > 0)
                {
                    var syntheticJson = System.Text.Json.JsonSerializer.Serialize(
                        new EpisodeConsensusPayload { EpisodeId = intent.EpisodeId, IsStart = intent.IsStart }, FdpJsonOptionsRegistry.DefaultRelaxed);
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
        // ⭐ CE-3345 (R-3073) — a cluster in ERROR cannot save the scenario: the world may be inconsistent (a node of it is gone). Refused with the error text.
        if (intent.Operation == StorageOpType.SaveScenario && IsInError)
        {
            FdpLog<ClusterMaster>.Warn("[Orchestrator] save {0} REJECTED — the cluster is in error: {1}.", intent.RequestId, ErrorReason);
            PublishOpStatus(intent.RequestId, OrchestrationStatusCode.Rejected, $"the cluster is in error — {ErrorReason}");
            return;
        }
        // ⭐ CE-3329 P3 (D-E, F7) — a scenario save while the cluster shows a RECORDING would save the replayed world as the scenario
        if (intent.Operation == StorageOpType.SaveScenario && ClusterStates.IsReplaying((FdpClusterState)(int)_currentDsmState))
        {
            FdpLog<ClusterMaster>.Warn("[Orchestrator] save {0} REJECTED — the cluster shows a recording ({1}); return to live first.",
                intent.RequestId, _currentDsmState);
            PublishOpStatus(intent.RequestId, OrchestrationStatusCode.Rejected, SaveRefusedInRunReason);
            return;
        }
        // ⭐ CE-4090 (AQ108 D22) — the same refusal in LIVE: a live run's world is not the scenario (take a checkpoint, then make a
        //   scenario from it — D8). ⭐ One rule, the one CE-3329 P3 already built for the recording: extended, not duplicated.
        if (intent.Operation == StorageOpType.SaveScenario && _currentDsmState == ClusterState.OperatingLive)
        {
            FdpLog<ClusterMaster>.Warn("[Orchestrator] save {0} REJECTED — a live run does not save the scenario; take a checkpoint instead.", intent.RequestId);
            PublishOpStatus(intent.RequestId, OrchestrationStatusCode.Rejected, SaveRefusedInRunReason);
            return;
        }
        // ⭐ CE-4090 (AQ108 D14, §5a) — keep / export of the run that was not recorded: there is nothing to archive
        if (intent.Operation == StorageOpType.Export && _runNotRecorded
            && intent.ExerciseId != Guid.Empty && intent.ExerciseId == _activeExerciseId)
        {
            RefuseNotRecorded(intent.RequestId, "export");
            return;
        }
        // ⭐ CE-4090 (AQ108 D15) — discard an unarchived recording
        if (intent.Operation == StorageOpType.Delete) { ProcessDeleteExercise(intent); return; }
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

                var scnNodeIds = Participants();   // ⭐ C3 — operator rounds: in-sync nodes only
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
                if (scnNodeIds.Count > 0) NoteSaveRoundStarted(scnTxId);   // ⭐ CE-4093 — a failed save makes the scenario unsaved (the check round)

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

                var exportNodeIds = Participants();   // ⭐ C3 — operator rounds: in-sync nodes only
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
        // ⭐ CE-3329 F8 — a seek moves the clock: only inside a replay / review (its entry included), never while the exit waits
        var showing = (FdpClusterState)(int)_currentDsmState;
        bool replaying = ClusterStates.IsReplaying(showing) || showing is FdpClusterState.LoadingReplay or FdpClusterState.LoadingReview;
        if (!replaying || _heldReplayExit != null)
        {
            FdpLog<ClusterMaster>.Warn("[Orchestrator] CE-3329 F8: seek {0} REJECTED — {1}.", intent.RequestId,
                replaying ? "the cluster is leaving the recording" : $"the cluster shows no recording ({showing})");
            PublishOpStatus(intent.RequestId, OrchestrationStatusCode.Rejected);
            return;
        }
        // ⭐ CE-3329 P1 (R11) — a target in RECORDED sim time becomes wall ticks here, with the cluster's map; nodes are unchanged
        if (intent.TargetSimSeconds is { } sim)
        {
            if (RelativeTicksForSim(sim, landOnFrame: true) is not { } rel)
            {
                FdpLog<ClusterMaster>.Warn("[Orchestrator] seek {0} REJECTED — atSimSeconds {1}, but this replay has no sim axis (a v6 recording).",
                    intent.RequestId, sim);
                PublishOpStatus(intent.RequestId, OrchestrationStatusCode.Rejected);
                return;
            }
            intent.TargetWallTicks = rel;
        }
        // ⭐ CE-3333 — "the end" (long.MaxValue, what scripts send) or anything past it is the replay's LAST moment, resolved here where the
        //   cluster's length is known. ⛔ Sent through as is, a node found the target past its own recording, took itself for a listener
        //   (R-4012) and emptied its world: a seek to the end showed nothing.
        long endTicks = (long)Math.Round(_replayDurationSeconds * TimeSpan.TicksPerSecond);
        if (_replayDurationSeconds > 0 && intent.TargetWallTicks > endTicks) intent.TargetWallTicks = endTicks;

        // RT-008: fan-out with ACK tracker; immediate Success when roster is empty.
        // SlaveNodeSetUpdatedEvent and PauseTimeIntent are published by ReplaySeekProcessManager (TASK-T002).
        var seekNodeIds = Participants();   // ⭐ C3 — operator rounds: in-sync nodes only
        if (seekNodeIds.Count == 0)
        {
            PublishOpStatus(intent.RequestId, OrchestrationStatusCode.Success);
            return;
        }
        var txId = Guid.NewGuid();
        // ⭐ C9 (R-4012) — every node measures the target from the SAME zero.
        FanOutNodeOp(NodeOpType.NodeReplaySeek, txId,
            new ReplaySeekPayload(intent.TargetWallTicks, _replayAnchorWallTicks), seekNodeIds);
        _seekTx.Add(txId);   // ⭐ F8
        _pendingTransactions[txId] = new GenericTransactionTracker
        {
            RequestId                       = intent.RequestId,
            Expected                        = seekNodeIds.Count,
            BroadcastClusterStateOnComplete = true,
        };
    }

    /// <summary>
    /// ⭐⭐ P8 (Q91 §4-H.2) — cancel is TWO rounds: an <c>AbortTransaction</c> naming the transition's own transaction
    /// to every roster node (each node's load chain undoes its load — it may even preempt a stuck prepare, H8), then a
    /// round to <c>Idle</c> through the ordinary transition path, which reports the cluster in <c>Idle</c> when every
    /// node has answered. ⛔ A cancel that sent only the abort would leave every node reporting <c>Loading*</c> with
    /// nothing loading. Returns <c>false</c> when <paramref name="targetId"/> names no transition still in flight.
    /// </summary>
    private bool TryCancelInFlightTransition(Guid targetId)
    {
        var tx = _inflightTransitionTx;
        if (tx == null || tx.IsAborted) return false;
        if (tx.OriginRequestId != targetId && tx.TransactionId != targetId) return false;
        if (!_pendingTransactions.TryGetValue(tx.TransactionId, out var tracker)) return false;   // already complete

        _pendingTransactions.Remove(tx.TransactionId);
        _owedAcks.Remove(tx.TransactionId);
        tx.IsAborted = true;

        PublishOpStatus(tracker.RequestId, OrchestrationStatusCode.Cancelled);
        FdpLog<ClusterMaster>.Info(
            "[Orchestrator] P8: transition {0} (transaction {1}) CANCELLED — aborting on {2} node(s); returning to Idle.",
            targetId, tx.TransactionId, _roster.ActiveNodes.Count);
        AbortAndReturnToIdle(tx.TransactionId);
        return true;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-3345</c> (R-3070) — <b>the ONE "abort the transaction, then round 2: back to Idle"</b> (P8 / Q91 §4-H.2): an <c>AbortTransaction</c> naming <paramref name="abortedTransactionId"/> to every roster node
    /// (each node's load chain undoes its load — it may even preempt a stuck prepare, H8), then a round to <c>Idle</c> through the ordinary transition path, which reports the cluster in <c>Idle</c> when every node has
    /// answered. Used by the master-side CANCEL of an in-flight transition and by a FAILED LOAD round (a node's own cancel, a missing terrain) — ⛔ two copies of round 2 would be two places for it to drift.
    /// </summary>
    private void AbortAndReturnToIdle(Guid abortedTransactionId)
    {
        var nodes = new List<int>(_roster.ActiveNodes.Keys);
        if (nodes.Count > 0)
            FanOutNodeOp(NodeOpType.AbortTransaction, Guid.NewGuid(), new AbortTransactionPayload(abortedTransactionId), nodes);

        // ── round 2: back to Idle, the state every load starts from (R-4004, H2).
        var idleRequest = Guid.NewGuid();
        var toIdle = new Queue<ISysOpStep>();
        toIdle.Enqueue(new TransitionStep(ClusterState.Idle));
        _activeExerciseId = Guid.Empty;
        ExecuteTransitionTrajectory(new ParkedTransition(
            idleRequest,
            new TransitionStateIntent
            {
                TransactionId = idleRequest,
                TargetState   = Fdp.Toolkit.Orchestration.ClusterState.Idle,
            },
            toIdle, _currentDsmState, ClusterState.Idle, toIdle.Count, UtcNowSeconds()));
    }

    private void ProcessCancelOperationIntent(CancelOperationIntent intent)
    {
        var targetId = intent.TargetRequestId;

        // ⭐ CE-4087 — a load still WAITING for its roles has sent nothing and staged nothing: dropping it is the whole cancel.
        if (targetId != Guid.Empty && TryCancelHeldLoad(targetId)) return;

        // ⭐⭐ L8 — the ONE transition state that can be cancelled cleanly: PARKED, so nothing has been sent.
        //   ⛔ Cancel was an ARCHIVE-only facility (`_activeCancellations` is written only by the Export and
        //   Import branches, CGF-1-BATCH-28 §C.4) and a transition was never a target — before parking there
        //   was simply no window: the trajectory was fanned out in the pass that admitted it.
        // ⭐ P8 H5 (Q91 §4-H) — and the staging copy its prefetch started is STOPPED: the saga cancels the transfer
        //   (P6's IFileTransfer.Cancel). ⛔ Before P6 it was abandoned and the bytes finished landing.
        // 📄 docs/DESIGN_Cluster_Load_Phase.md §7.7 ④.
        if (targetId != Guid.Empty && _parked?.RequestId == targetId)
        {
            _parked = null;
            _eventBus?.PublishManaged(new PrefetchCancelRequestedEvent { RequestId = targetId });
            FdpLog<ClusterMaster>.Info(
                "[Orchestrator] L8: parked transition {0} CANCELLED — nothing was fanned out; its staging copy is stopped.",
                targetId);
            PublishOpStatus(targetId, OrchestrationStatusCode.Cancelled);
            return;
        }

        // ⭐⭐ P8 (Q91 §4-H, R-4004) — a transition already fanned out and not yet complete: abort it on every node, then
        //   return the cluster to Idle so the same load can simply be issued again. ⛔ Still no timer (R-4001, H4).
        if (targetId != Guid.Empty && TryCancelInFlightTransition(targetId))
            return;

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
        if (IsInError) state = ClusterState.Degraded;   // ⭐ CE-3345 — what the master announces while in error; _currentDsmState keeps the state the world is really in
        var waitingFor = WaitingFor() ?? string.Empty;   // ⭐ CE-3344 — empty when nothing mandatory is missing
        var (playReady, loadProgress) = Readiness();     // ⭐ CE-3345 — the participants' combined phases
        _lastPublishedState = state;
        _publishedWaitingFor = waitingFor;
        _publishedPlayReady = playReady;
        _publishedLoadProgress = loadProgress;
        _eventBus.PublishManaged(new ClusterStateTransitionedEvent
        {
            NewStateId       = (Fdp.Toolkit.Orchestration.ClusterState)(int)state,
            SubsystemName    = "Cluster",
            ExerciseId       = _activeExerciseId,
            SessionId        = SessionId,          // ⭐ C2
            TransactionEpoch = _transactionEpoch,  // ⭐ C4 — telemetry
            ReplayAxis       = _replayAxis,        // ⭐ CE-3329 R-3058
            RunNotRecorded   = _runNotRecorded,    // ⭐ CE-4090 D14
            RecordLiveRunsOff = !RecordLiveRuns,
            AskToKeepRecordings = AskToKeepRecordings,   // ⭐ AQ108 D15 (CE-4100 ①)
            ArmedMode = ArmedMode,   // ⭐ CE-3341 R-3068
            LastLoadMode = LastLoadMode,   // ⭐ R-1043
            WaitingFor = waitingFor,   // ⭐ CE-3344 R-3069
            ErrorReason = ErrorReason,   // ⭐ CE-3345 R-3073
            ClusterPlayReady = playReady,   // ⭐ CE-3345 R-3070
            LoadProgress = loadProgress,
        });
        _eventBus.PublishManaged(new ClusterStateUpdateEvent
        {
            CurrentState = (Fdp.Toolkit.Orchestration.ClusterState)(int)state,
            ExerciseId   = _activeExerciseId,
            SessionId    = SessionId,
            ReplayAxis   = _replayAxis,
            RunNotRecorded = _runNotRecorded,   // ⭐ CE-4090 D14
            RecordLiveRunsOff = !RecordLiveRuns,
            AskToKeepRecordings = AskToKeepRecordings,   // ⭐ AQ108 D15 (CE-4100 ①)
            ArmedMode = ArmedMode,   // ⭐ CE-3341 R-3068
            LastLoadMode = LastLoadMode,   // ⭐ R-1043
            WaitingFor = waitingFor,   // ⭐ CE-3344 R-3069
            ErrorReason = ErrorReason,   // ⭐ CE-3345 R-3073
            ClusterPlayReady = playReady,   // ⭐ CE-3345 R-3070
            LoadProgress = loadProgress,
        });
    }

    private ClusterState _lastPublishedState = ClusterState.Idle;
    private string? _publishedWaitingFor;   // null = nothing published yet
    private bool _publishedPlayReady;
    private string _publishedLoadProgress = string.Empty;

    /// <summary>
    /// ⭐⭐ CE-3344 (R-3069) — while the cluster is not ready (the latch is closed: booting, or after losing a mandatory name / role) the master used to publish NO state at all, so a node
    /// could not say what was missing. Now it re-publishes the state it last announced (<c>Idle</c> before the first) whenever the missing text CHANGES, so every node's
    /// <see cref="Fdp.Toolkit.Orchestration.ClusterView.WaitingFor"/> follows. ⛔ It never invents a state — only the text moves.
    /// </summary>
    private void PublishWaitingForIfChanged()
    {
        // ⭐ CE-3345 — the same re-publish carries the readiness the nodes' heartbeats decide (cluster play-ready, who is still loading): it changes with a node's phase, not with a transition.
        var (playReady, loadProgress) = Readiness();
        bool readyAndSaid = _bootstrapLatch && _publishedWaitingFor is { Length: 0 };   // ready and already said so: no per-frame text building
        var waiting = readyAndSaid ? _publishedWaitingFor! : WaitingFor() ?? string.Empty;
        if (waiting == _publishedWaitingFor && playReady == _publishedPlayReady && loadProgress == _publishedLoadProgress) return;
        PublishClusterState(_lastPublishedState);
    }

    /// <summary>
    /// ⭐⭐ C2 (Q91 §4-I I5, R-4006) — THE CLUSTER SESSION: minted once, when this master is created. One master = one session
    /// (the master is not restartable), so it needs no counter. It rides every cluster-state publication; a node that sees
    /// a different one — or this master's writer gone — drops to <c>Idle</c>.
    /// </summary>
    public Guid SessionId { get; } = Guid.NewGuid();

    /// <summary>⭐ C4 (Q91 §4-I I4) — telemetry only: rounds completed successfully so far. ⛔ Never decides membership.</summary>
    public int TransactionEpoch => _transactionEpoch;
    private int _transactionEpoch;

    /// <summary>
    /// Publishes a node-level operation to each target node via the event bus (bus path)
    /// or per-node DDS writers (DDS path, legacy).
    /// </summary>
    private void FanOutNodeOp(NodeOpType operation, Guid transactionId, object? domainPayload, IEnumerable<int> targetNodeIds)
    {
        foreach (var nodeId in targetNodeIds)
        {
            if (operation != NodeOpType.CommitState) OweAck(transactionId, nodeId);   // ⭐ P1
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
            // ⭐⭐ P4 (Q91 §4-B2, R-4001) — a node still PREPARING reports progress, not an answer: never counted as the
            //   node's ACK, never paid against what it owes. ⛔ Before, every status was counted, so an InProgress report
            //   would have completed a round the node had not finished.
            if (ev.StatusCode is OrchestrationStatusCode.InProgress or OrchestrationStatusCode.Pending)
                continue;

            // S0501: Mirror ACKs into _inflightTransitionTx.NodeResponses for history.
            if (_inflightTransitionTx != null && _inflightTransitionTx.TransactionId == ev.TransactionId)
            {
                string mirrorJson = ev.ResultPayload is null ? string.Empty
                    : ev.ResultPayload is string ms ? ms
                    : JsonSerializer.Serialize(ev.ResultPayload, FdpJsonOptionsRegistry.DefaultRelaxed);
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
            // ⭐⭐ P2 / C1 (Q91 §4-C, R-4014) — an OBSERVER's error is recorded in the round's responses but never fails it.
            bool authoritativeError = ev.StatusCode.IsError() && !IsObserver(ev.NodeId);
            if (ev.StatusCode.IsError() && !authoritativeError)
                FdpLog<ClusterMaster>.Warn(
                    "[Orchestrator] P2: observer node {0} answered {1} for round {2} — recorded, the round goes on.",
                    ev.NodeId, ev.StatusCode, ev.TransactionId);

            if (authoritativeError && tracker.AbortOnFirstFailure)
            {
                _pendingTransactions.Remove(ev.TransactionId);
                _pendingGeoReferences.Remove(ev.TransactionId);   // ⭐ AQ107 C
                _owedAcks.Remove(ev.TransactionId);
                FdpLog<ClusterMaster>.Warn(
                    "[Orchestrator] 2PC transaction {0} aborted: node {1} returned error {2}.",
                    ev.TransactionId, ev.NodeId, ev.StatusCode);
                PublishOpStatus(tracker.RequestId, OrchestrationStatusCode.Rejected);
                continue;
            }

            if (authoritativeError)
            {
                tracker.HasFailure  = true;
                tracker.FailureCode = ev.StatusCode;
                tracker.FailureReason ??= ev.ResultPayload as string;   // ⭐ CE-3345 — the node's own reason (first failure wins)
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

            PayAck(ev.TransactionId, ev.NodeId);   // ⭐ P1
            tracker.Received++;

            // ⭐⭐ CE-4103 — a node that answered an ERROR answers nothing more in this round: its slave discards the transaction's
            //   remaining steps (ClusterSlave: a faulted prepare ⇒ "Commit skipped", pending intents cleared). ⛔ The round used to keep
            //   waiting for those ACKs — a load whose terrain is missing on a node parked the master forever, with the failure only in
            //   the node's log. ⭐ Like an ejected node (DropFromInFlightRounds): what it still owed comes off, so the round completes
            //   NOW with the failure. R-4001: no timer — the node's own answer is the only signal.
            if (authoritativeError && _owedAcks.TryGetValue(ev.TransactionId, out var owedByRound)
                && owedByRound.Remove(ev.NodeId, out var stillOwed) && stillOwed > 0)
            {
                tracker.Expected -= stillOwed;
                FdpLog<ClusterMaster>.Warn(
                    "[Orchestrator] CE-4103: node {0} answered {1} for round {2}; the {3} step(s) it still owed are dropped — the round fails now.",
                    ev.NodeId, ev.StatusCode, ev.TransactionId, stillOwed);
            }
            // ⭐⭐ CE-3345 (R-3070) — a LOAD round with a failed node fails NOW, whatever the OTHER nodes still owe: a user's cancel (or a missing terrain) on one node must not wait for another node's
            //   long load. What they hold is undone by the abort + the round to Idle CompleteRound sends (their late acks find no round and are ignored).
            if (authoritativeError && tracker.IsLoadRound && tracker.Received < tracker.Expected)
            {
                FdpLog<ClusterMaster>.Warn("[Orchestrator] CE-3345: load round {0} failed on node {1} — it does not wait for the other nodes' {2} outstanding ACK(s).",
                    ev.TransactionId, ev.NodeId, tracker.Expected - tracker.Received);
                tracker.Expected = tracker.Received;
            }
            if (tracker.Received >= tracker.Expected)
                CompleteRound(ev.TransactionId, tracker);
        }
    }

    /// <summary>
    /// ⭐ P1 — the ONE place a round completes: from its last ACK, or from the ejection of the last node that still
    /// owed one. ⛔ Two copies of this block would be two places for the outcome rules to drift.
    /// </summary>
    private void CompleteRound(Guid txId, GenericTransactionTracker tracker)
    {
        _pendingTransactions.Remove(txId);
        // ⭐ AQ107 C — a load's parked origin is published on success only; a failed round drops it (the nodes never adopted it).
        (TerrainGeoOrigin Origin, string TerrainName)? parkedGeo =
            _pendingGeoReferences.Remove(txId, out var parked) ? parked : null;
        // ⚠ BEFORE the outcome: a phased round (zone, terrain asset) re-uses this id for its commit phase, and
        //   OnPhaseSuccess below records that phase's owed ACKs under the SAME id.
        _owedAcks.Remove(txId);

        if (tracker.HasFailure)
        {
            FdpLog<ClusterMaster>.Error(
                "[Orchestrator] 2PC transaction {0} completed with failures (code={1}).",
                txId, tracker.FailureCode);

            // ⭐ A failed NON-TERMINAL phase must tell the nodes that ACKed to drop what they
            //   staged — otherwise a prepared-but-never-committed buffer leaks until process
            //   exit. 📄 docs/designs/mgmt-1/DESIGN.md §11.1 (ABORT).
            if (tracker.OnPhaseSuccess != null && tracker.Targets is { Count: > 0 })
                FanOutNodeOp(NodeOpType.AbortTransaction, Guid.NewGuid(),
                    new AbortTransactionPayload(txId), tracker.Targets);

            NoteRoundCompletedForUnsavedCheck(txId, failed: true, aggregated: null);   // ⭐ CE-4093
            PublishOpStatus(tracker.RequestId, tracker.FailureCode, tracker.FailureReason);   // ⭐ CE-3345 — with the failing node's reason
            // ⭐⭐ CE-3345 (R-3070, AQ91 §4-H: "the cluster goes to the initial default state") — a failed LOAD ends in Idle: abort on every node, then the round to Idle. ⭐ BEFORE the broadcast below,
            //   so it says Idle (the optimistic advance had moved the master to the load's target). ⛔ Not for any other round: a failed checkpoint or save leaves the world as it is.
            if (tracker.IsLoadRound)
                AbortAndReturnToIdle(txId);
        }
        else if (tracker.OnPhaseSuccess != null)
        {
            // ⛔ Deliberately NO PublishOpStatus here: the requester learns the outcome when the
            //   LAST phase completes, not when the first one does.
            tracker.OnPhaseSuccess();
        }
        else
        {
            _transactionEpoch++;   // ⭐ C4 — telemetry: one more round completed successfully
            if (_currentDsmState == ClusterState.Idle) ClearClusterError();   // ⭐ CE-3345 — the cluster reached Idle: the error ends (the broadcast below says so)
            if (tracker.ResyncsOnSuccess && tracker.Targets != null)
            {
                // ⭐ C3 — everyone who answered a trajectory through Idle is in sync with what it reached.
                _inSync.Clear();
                foreach (var id in tracker.Targets)
                    if (_roster.ActiveNodes.ContainsKey(id)) _inSync.Add(id);
            }
            var aggregated = tracker.SuccessResult
                ?? (tracker.ResultFromResponses ? ResponsesByNode(tracker.NodeResponses) : TryAggregate(txId, tracker.NodeResponses));
            if (tracker.RefineResult != null) aggregated = tracker.RefineResult(aggregated);   // ⭐ CE-4093
            NoteRoundCompletedForUnsavedCheck(txId, failed: false, aggregated);
            PublishOpStatus(tracker.RequestId, OrchestrationStatusCode.Success, aggregated);
            OnRoundSucceededForReplay(txId, aggregated);   // ⭐ C9 — the replay zero, and the seek that enters a replay
            OnRoundSucceededForGeoReference(parkedGeo, aggregated);   // ⭐ AQ107 C — the NED wire's frame origin (load: terrain; replay: recording)
        }

        if (tracker.BroadcastClusterStateOnComplete)
            PublishClusterState(_currentDsmState);
    }

    private void OweAck(Guid txId, int nodeId)
    {
        if (!_owedAcks.TryGetValue(txId, out var owed)) _owedAcks[txId] = owed = new Dictionary<int, int>();
        owed[nodeId] = owed.TryGetValue(nodeId, out var n) ? n + 1 : 1;
    }

    private void PayAck(Guid txId, int nodeId)
    {
        if (_owedAcks.TryGetValue(txId, out var owed) && owed.TryGetValue(nodeId, out var n) && n > 0)
            owed[nodeId] = n - 1;
    }

    /// <summary>
    /// ⭐⭐ P1 (Q91 §4-B, B1, R-4001) — a node that left owes no ACK any more: take what it still owed off every
    /// in-flight round, and complete each round that was waiting only for it. ⛔ No timer: the heartbeat timeout that
    /// ejected the node is the only time limit. ⚠ A round that completes this way completes on the nodes that remain.
    /// </summary>
    private void DropFromInFlightRounds(int nodeId)
    {
        foreach (var (txId, tracker) in _pendingTransactions.ToList())
        {
            if (!_owedAcks.TryGetValue(txId, out var owed) || !owed.Remove(nodeId, out var stillOwed) || stillOwed <= 0)
                continue;
            tracker.Expected -= stillOwed;
            tracker.Targets?.Remove(nodeId);
            FdpLog<ClusterMaster>.Warn(
                "[Orchestrator] P1: node {0} left; round {1} no longer waits for its {2} ACK(s).", nodeId, txId, stillOwed);
            if (tracker.Received >= tracker.Expected)
                CompleteRound(txId, tracker);
        }
    }

    /// <summary>⭐ P1 — forget the owed counts of fan-outs that never had a round (aborts, standby broadcasts).</summary>
    private void PruneOwedAcks()
    {
        if (_owedAcks.Count == 0) return;
        foreach (var txId in _owedAcks.Keys.ToList())
            if (!_pendingTransactions.ContainsKey(txId)) _owedAcks.Remove(txId);
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

    /// <summary>
    /// Pauses cluster time: the slave roster (simulation nodes only — ExCon never acks a frame) then the
    /// pause intent the master's time controller drains. ⭐ ONE implementation for the Pause op and for
    /// Stop (CE-3068).
    /// </summary>
    private void PublishClusterPause()
    {
        // ⭐ P3 — the step roster by ROLE, never by subsystem name (an observer never gates a step, R-4014).
        var slaveIds = StepRoster();
        _lastStepRoster = new HashSet<int>(slaveIds);
        _eventBus.PublishManaged(new SlaveNodeSetUpdatedEvent { SlaveNodeIds = slaveIds });
        _eventBus.PublishManaged(new PauseTimeIntent());
    }

    private void AppendToHistory(DistributedTransaction tx)
    {
        _history[_historyHead] = tx;
        _historyHead = (_historyHead + 1) % _history.Length;
    }

    private static double UtcNowSeconds() =>
        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

    /// <summary>The roles that make a node AUTHORITATIVE (Q91 §4-A). ⚠ The role → policy map is application code (R-4000).</summary>
    private const Fdp.Core.NodeRole SimulationRoles =
        Fdp.Core.NodeRole.Brain | Fdp.Core.NodeRole.MuscleGround | Fdp.Core.NodeRole.Perception | Fdp.Core.NodeRole.NavigationSolver;

    /// <summary>
    /// ⭐⭐⭐ P2 (Q91 §4-A′, R-4014) — is this node an OBSERVER (formerly "best-effort", L4)? Nothing waits for an observer
    /// and nothing fails because of it. ⛔ FAILS SAFE — an observer iff ALL THREE hold:
    /// ① its capability advert HAS landed (the mask reads <c>None</c> until it does, <c>IngestHeartbeats</c>);
    /// ② its roles hold no simulation role;
    /// ③ the roster holds at least ONE authoritative node — otherwise nothing could ever fail a round (anti-vacuity).
    /// Unknown ⇒ authoritative; alone ⇒ authoritative.
    /// </summary>
    public bool IsObserver(int nodeId)
    {
        if (!_roster.ActiveNodes.TryGetValue(nodeId, out var profile) || !IsObserverByRole(profile))
            return false;
        foreach (var other in _roster.ActiveNodes.Values)
            if (!IsObserverByRole(other)) return true;   // ③ someone authoritative remains
        return false;
    }

    private bool IsObserverByRole(NodeHealthProfile p)
        => _nodeCapabilities.ContainsKey(p.NodeId) && (p.Roles & SimulationRoles) == 0;

    /// <summary>The node this master runs IN (the orchestrator's own node, or the editor's) — set by the core.</summary>
    public int LocalNodeId { get; set; } = int.MinValue;

    private HashSet<int> _lastStepRoster = new();

    /// <summary>
    /// ⭐⭐ P3 (Q91 §4-D D1, §4-D′) — the nodes the clock's step barrier waits for: advert landed ∧ holds a simulation
    /// role ∧ is in sync (CE-4087, K6) ∧ is not the node this master runs in. ⚠ DELIBERATELY NOT <see cref="IsObserver"/>'s clause ③: in a cluster of
    /// observers only, ③ would make ExCon and the orchestrator "authoritative", and neither ever ACKs a frame ⇒ every step
    /// would hang. An EMPTY roster means nobody is waited for, which is safe. ⛔ Never the master's own node: it runs the
    /// MASTER clock (the editor, R-4005) and never ACKs a frame to itself. Replaces the name match
    /// <c>"SimHost" or "IG" or "CGF"</c> at BOTH sites (here and <c>ReplaySeekProcessManager</c>).
    /// </summary>
    public IReadOnlySet<int> StepRoster()
    {
        var roster = new HashSet<int>();
        foreach (var p in _roster.ActiveNodes.Values)
            if (p.NodeId != LocalNodeId && _nodeCapabilities.ContainsKey(p.NodeId) && (p.Roles & SimulationRoles) != 0
                // ⭐⭐ CE-4087 (R-4014, Q91 §4-K K6) — "a node not in sync never participates", in the step barrier too. A SimHost that
                //   registers while the cluster runs (or restarts mid-run) is NOT in the barrier while it is still being caught up —
                //   it cannot answer a step for a world it has not got yet — and enters it, with the roster republished, the moment
                //   its catch-up completes (`PublishStepRosterIfChanged` runs every tick). ⛔ Before, the barrier admitted it on its
                //   advert, and a node still loading gated every step.
                && _inSync.Contains(p.NodeId))
                roster.Add(p.NodeId);
        return roster;
    }

    /// <summary>
    /// ⭐ P3 ③ — publish the step roster whenever it CHANGES (a join, an advert landing, an ejection), so
    /// <c>SlaveNodeSetUpdatedEvent</c> means what its own header says — and a departed node stops gating a step. ⭐ This IS
    /// the departure channel to the clock: one event with an existing consumer, ⛔ not a second copy of <c>NodeDeparted</c>.
    /// </summary>
    private void PublishStepRosterIfChanged()
    {
        var now = StepRoster();
        if (now.SetEquals(_lastStepRoster)) return;
        _lastStepRoster = new HashSet<int>(now);
        _eventBus.PublishManaged(new SlaveNodeSetUpdatedEvent { SlaveNodeIds = now });
    }

    /// <summary>
    /// ⭐ <c>CE-4002</c> — the master's roster as plain rows for the debug API (<c>GET /cluster/roster</c>), aged by the
    /// SAME clock the ejection uses, so a row's age and the 5 s timeout can never disagree. ⛔ A read, no new state.
    /// </summary>
    public IReadOnlyList<Hrot.Presentation.DebugApi.ClusterRosterEntry> DescribeRoster()
    {
        double now = UtcNowSeconds();
        var rows = new List<Hrot.Presentation.DebugApi.ClusterRosterEntry>(_roster.ActiveNodes.Count);
        foreach (var p in _roster.ActiveNodes.Values)
            rows.Add(new Hrot.Presentation.DebugApi.ClusterRosterEntry(
                p.NodeId, p.SubsystemName, p.LocalClusterState.ToString(), p.Roles.ToString(),
                Math.Round(now - p.LastHeartbeatUtcSeconds, 3)));
        rows.Sort((a, b) => a.NodeId.CompareTo(b.NodeId));
        return rows;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var cts in _activeCancellations.Values)
            cts.Dispose();
        _activeCancellations.Clear();
    }
}
