using System;
using System.Collections.Generic;
using Fdp.Core;

namespace Fdp.Toolkit.Orchestration
{
    /// <summary>
    /// ⭐⭐⭐ <c>CE-1436</c> (<c>R-3066</c>) — <b>the ONE cluster read model of a node</b>: what the master last said about the cluster, folded from the
    /// FACT events on the node's bus (<see cref="ClusterStateUpdateEvent"/>, <see cref="AssetInventoryUpdateEvent"/>, <see cref="ReplaySessionUpdateEvent"/>,
    /// <see cref="ClusterOpCompletedEvent"/>) — the events <c>ClusterFactsIngress</c> publishes on every node from one decoding.
    /// <para>⭐ Owned by the node's <see cref="ClusterSlave"/> (one per node, by construction — no host wires it) and COMPOSED by the master core's
    /// <c>ClusterUiCache</c>, whose fact members delegate here; ⛔ nothing else keeps a second copy of these facts.</para>
    /// <para>⛔ Facts only — no heartbeats, no transaction history, no planner (the monitoring half stays in <c>ClusterUiCache</c>, design V2/V3).</para>
    /// 📄 docs/DESIGN_Cluster_View.md.
    /// </summary>
    public sealed class ClusterView
    {
        /// <summary>How many op outcomes are remembered (by request id); the oldest is forgotten first.</summary>
        public const int OutcomeCapacity = 32;

        private readonly FdpEventBus? _bus;
        private readonly Dictionary<Guid, (OrchestrationStatusCode Status, object? Payload)> _outcomes = new();
        private readonly Queue<Guid> _outcomeOrder = new();

        /// <summary>
        /// A view over <paramref name="bus"/>; <see langword="null"/> (the test / offline shape) folds nothing.
        /// ⭐ <paramref name="nodeId"/> (<c>CE-3344</c>) is the node this view belongs to — the one <see cref="CanOperate"/> looks for in the master's in-sync set;
        /// <see langword="null"/> (an observer with no node identity, a test) skips that clause.
        /// </summary>
        public ClusterView(FdpEventBus? bus, int? nodeId = null) { _bus = bus; NodeId = nodeId; }

        /// <summary>⭐ <c>CE-3344</c> — the node this view belongs to (<see langword="null"/> = none: <see cref="CanOperate"/> then does not ask for it to be in sync).</summary>
        public int? NodeId { get; }

        /// <summary>⭐ <c>CE-3354</c> ② — the unique name of the node this view belongs to (set by its <c>ClusterSlave</c>); null on a view with no slave.</summary>
        public string? NodeName { get; internal set; }

        // ── the cluster state broadcast ────────────────────────────────────────

        /// <summary>The cluster state the master LAST ANNOUNCED (<c>Idle</c> until one arrives, and after <see cref="MasterGone"/>).
        /// ⚠ Not a node's own committed state.</summary>
        public ClusterState State { get; private set; } = ClusterState.Idle;

        /// <summary>⭐ <c>CE-4096</c> — a real (non-MasterGone) broadcast has reached this node, so <see cref="State"/> is a fact and not the
        /// <c>Idle</c> default of "no master seen yet".</summary>
        public bool HasFollowedState { get; private set; }

        /// <summary>⭐ <c>CE-4096</c> — the master's last word was <c>MasterGone</c>: set by that broadcast, cleared by the next real one.</summary>
        public bool MasterGone { get; private set; }

        /// <summary>The exercise the master's last broadcast named (empty when none).</summary>
        public Guid ExerciseId { get; private set; }

        /// <summary>⭐ <c>CE-3329</c> R-3058 — the replay session's time axis, as the last broadcast said (Sim until one arrives).</summary>
        public ReplayAxis ReplayAxis { get; private set; } = ReplayAxis.Sim;

        /// <summary>⭐ <c>CE-4090</c> (AQ108 D14) — the run the cluster shows was started with recording OFF (false until a broadcast says so).</summary>
        public bool RunNotRecorded { get; private set; }

        /// <summary>⭐ <c>CE-4090</c> — the cluster's <i>Record live runs</i> setting is OFF (false = ON, also before a broadcast).</summary>
        public bool RecordLiveRunsOff { get; private set; }

        /// <summary>⭐ AQ108 D15 (CE-4100 ①) — the cluster's <i>Ask to keep recordings</i> setting is ON, as the master's last broadcast said (false = OFF, also before a broadcast and for a sender that does not set it).</summary>
        public bool AskToKeepRecordings { get; private set; }

        /// <summary>⭐⭐ <c>CE-3341</c> (AQ108 D12a, R-3068) — the cluster's ARMED run mode, as the master's last broadcast said: ONE fact, the same on every node
        /// (<see cref="ClusterArmedMode.Live"/> before a broadcast arrives and for a sender that does not set it).</summary>
        public ClusterArmedMode ArmedMode { get; private set; } = ClusterArmedMode.Live;

        /// <summary>⭐ R-1043 — the mode the LAST scenario load went into (Live / Preview), as the master's last broadcast said; Live before a broadcast and for a sender that does not set it.</summary>
        public ClusterArmedMode LastLoadMode { get; private set; } = ClusterArmedMode.Live;

        // ── cluster ready (CE-3344, R-3069) ─────────────────────────────────────

        /// <summary>⭐⭐ <c>CE-3344</c> (R-3069) — what the cluster is still WAITING FOR before it is ready, as the master's last broadcast said
        /// (<i>"SimHost, Brain (CGF)"</i>: mandatory names and roles no node has reported); empty = nothing is missing.</summary>
        public string WaitingFor { get; private set; } = string.Empty;

        /// <summary>⭐⭐ <c>CE-3345</c> (R-3073, <c>DESIGN_Cluster_Load_Phase.md</c> §11.5) — the cluster ERROR the master last announced (<i>"Brain (CGF, node 3) lost at sim 00:12:31"</i>); empty = no error.
        /// Sticky on the master: only the cluster reaching Idle clears it (a returning node does not).</summary>
        public string ErrorReason { get; private set; } = string.Empty;

        /// <summary>⭐ <c>CE-3345</c> — the cluster is in its ERROR state: a required node / role was lost with a world loaded. Every operation is refused except the ways back to Idle.</summary>
        public bool IsInError => ErrorReason.Length > 0;

        // ── play ready (CE-3345 part B, R-3070 / R-3072 / AQ110) — two levels, one object ──────────────────

        /// <summary>⭐⭐ <c>CE-3345</c> — THIS node's own phase, as its slave reports it on its heartbeat (<see cref="NodePhase.Booting"/> until the slave's first <c>Tick</c>). The LOCAL level: read only by this node's host.</summary>
        public NodePhase LocalPhase { get; private set; } = NodePhase.Booting;

        /// <summary>The load step this node is on (while <see cref="LocalPhase"/> is <c>Loading</c> / <c>CatchingUp</c>), else empty.</summary>
        public string LocalStep { get; private set; } = string.Empty;

        /// <summary>Why this node's last prepare failed (while <see cref="LocalPhase"/> is <c>Failed</c>), else empty.</summary>
        public string LocalReason { get; private set; } = string.Empty;

        /// <summary>⭐ <c>CE-3345</c> — this node committed an <c>Operating*</c> state: its world is loaded.</summary>
        public bool IsLocalPlayReady => LocalPhase == NodePhase.PlayReady;

        /// <summary>⭐ <c>CE-3345</c> — this node is loading (or catching up): the Operator policy keeps its controls shut.</summary>
        public bool IsLocalLoading => LocalPhase is NodePhase.Loading or NodePhase.CatchingUp;

        /// <summary>Called by the node's <see cref="ClusterSlave"/> each frame with what it reports; ⛔ nothing else writes the local level.</summary>
        public void NoteLocalPhase(NodePhase phase, string? step, string? reason)
        {
            LocalPhase = phase;
            LocalStep = step ?? string.Empty;
            LocalReason = reason ?? string.Empty;
        }

        /// <summary>⭐⭐ <c>CE-3345</c> (R-3070) — CLUSTER PLAY READY, as the master's last broadcast said: every PARTICIPANT (in sync ∧ not an observer, K6) reports <see cref="NodePhase.PlayReady"/>.
        /// ⛔ A node outside the in-sync set (a joiner) is never part of it. False in Idle and while any participant loads.</summary>
        public bool ClusterPlayReady { get; private set; }

        /// <summary>⭐ <c>CE-3345</c> — which participants are still loading and their steps (<i>"CGF (node 2): Terrain"</i>), as the master's last broadcast said; empty = none.</summary>
        public string LoadProgress { get; private set; } = string.Empty;

        /// <summary>⭐ <c>CE-3345</c> — some participant is loading (<see cref="LoadProgress"/> is not empty).</summary>
        public bool IsClusterLoading => LoadProgress.Length > 0;

        /// <summary>
        /// ⭐⭐⭐ <c>CE-3345</c> (R-3072, AQ110 D) — <b>is this host's gate open under <paramref name="policy"/>?</b> The ONE combination of the facts above; ⛔ no host re-derives it.
        /// <see cref="HostGatePolicy.Operator"/> = <see cref="CanOperate"/> ∧ this node is not loading ∧ (the cluster is not loading ∨ <see cref="ClusterPlayReady"/>);
        /// <see cref="HostGatePolicy.Player"/> = <see cref="IsLocalPlayReady"/> ∧ <see cref="CanOperate"/> — no cluster-level play-ready clause: what the others are still loading is irrelevant to a player.
        /// </summary>
        public bool GateOpen(HostGatePolicy policy) => policy switch
        {
            HostGatePolicy.Player => IsLocalPlayReady && CanOperate,
            _                     => CanOperate && !IsLocalLoading && (!IsClusterLoading || ClusterPlayReady),
        };

        /// <summary>⭐ <c>CE-3344</c> — the master's in-sync set (R-4006) has reached this node; until it has, no node is known to be in sync.</summary>
        public bool HasInSyncSet { get; private set; }

        private int[] _inSyncNodeIds = Array.Empty<int>();

        /// <summary>⭐ <c>CE-3344</c> — the nodes the master last said are in sync (empty until <see cref="HasInSyncSet"/>).</summary>
        public IReadOnlyList<int> InSyncNodeIds => _inSyncNodeIds;

        /// <summary>⭐ <c>CE-3344</c> — is THIS view's node in the master's in-sync set? <see langword="true"/> when the view has no <see cref="NodeId"/>.</summary>
        public bool IsInSync => NodeId is not { } id || (HasInSyncSet && Array.IndexOf(_inSyncNodeIds, id) >= 0);

        /// <summary>
        /// ⭐⭐⭐ <c>CE-3344</c> (R-3069) — <b>why the cluster cannot be operated from this node right now</b>, or <see langword="null"/> when it can. The ONE text the
        /// HTTP 409 and every grey-out read (<see cref="CanOperate"/> is exactly <c>NotOperableReason == null</c>).
        /// </summary>
        public string? NotOperableReason
        {
            get
            {
                if (MasterGone) return "the cluster's master is gone";
                if (!HasFollowedState) return "no cluster state has reached this node yet";
                if (IsInError) return $"the cluster is in error — {ErrorReason}";   // ⭐ CE-3345 — the error first: it says WHY, and it is what the user must act on
                if (State == ClusterState.Degraded) return WaitingFor.Length > 0 ? $"the cluster is Degraded — waiting for: {WaitingFor}" : "the cluster is Degraded";
                if (WaitingFor.Length > 0) return $"the cluster is not ready — waiting for: {WaitingFor}";
                if (!IsInSync) return $"this node ({NodeId}) is not in the master's in-sync set";
                return null;
            }
        }

        /// <summary>
        /// ⭐⭐⭐ <c>CE-3344</c> (R-3069) — <b>CLUSTER READY, from this node</b>: the master's state has reached it, the state is not <c>Degraded</c>, nothing mandatory is
        /// missing (<see cref="WaitingFor"/>), the master is present, and this node is in the master's in-sync set. ⛔ The one fact every control's enabled-state and
        /// every cluster HTTP route reads — never a second copy.
        /// </summary>
        public bool CanOperate => NotOperableReason == null;

        // ── the asset inventory ───────────────────────────────────────────────

        /// <summary>An inventory has arrived (<see cref="AvailableExercises"/> is then the master's last word, not the empty default).</summary>
        public bool HasInventory { get; private set; }

        /// <summary>The scenarios the cluster can load, as the master's last inventory said.</summary>
        public string[] AvailableScenarios { get; private set; } = Array.Empty<string>();

        /// <summary>The exercises the cluster can replay.</summary>
        public ExerciseInventoryItem[] AvailableExercises { get; private set; } = Array.Empty<ExerciseInventoryItem>();

        /// <summary>The archived exercises.</summary>
        public ExerciseInventoryItem[] ArchivedExercises { get; private set; } = Array.Empty<ExerciseInventoryItem>();

        /// <summary>The local exercises not yet archived.</summary>
        public ExerciseInventoryItem[] UnarchivedLocalExercises { get; private set; } = Array.Empty<ExerciseInventoryItem>();

        // ── the replay session layer ──────────────────────────────────────────

        /// <summary>⭐ <c>CE-3329</c> — the replay's SESSION LAYER as the master last published it (inactive outside a replay).</summary>
        public ReplaySession ReplaySession { get; private set; } = ReplaySession.None(Guid.Empty);

        // ── op outcomes ───────────────────────────────────────────────────────

        /// <summary>
        /// ⭐ <c>CE-4093</c> (AQ108 D20) — the newest check round's answer, as the master published it; null before the first, and once the cluster
        /// leaves editing (the answer no longer describes what is open). ⛔ Never produced by reading this view: only a round the master ran.
        /// </summary>
        public UnsavedCheckResult? LastUnsavedCheck { get; private set; }

        /// <summary>⭐ <c>CE-4093</c> — how check round <paramref name="requestId"/> ended; null while it has not (or it is older than the last
        /// <see cref="OutcomeCapacity"/> ops seen).</summary>
        public UnsavedCheckOutcome? UnsavedCheckOutcomeFor(Guid requestId)
        {
            if (!_outcomes.TryGetValue(requestId, out var o)) return null;
            if (o.Status == OrchestrationStatusCode.Success && o.Payload is UnsavedCheckResult r) return new UnsavedCheckOutcome(r, null);
            return new UnsavedCheckOutcome(null, o.Payload as string ?? $"the cluster answered {o.Status}");
        }

        /// <summary>
        /// ⭐ <c>CE-3345</c> (R-3070) — how op <paramref name="requestId"/> ended and, for a failure, WHY: the reason the failing node's ack carried (<i>"cancelled by the user on node 2"</i>), through the master's op status.
        /// <see langword="null"/> while it has not ended (or it is older than the last <see cref="OutcomeCapacity"/> ops seen).
        /// </summary>
        public (OrchestrationStatusCode Status, string? Reason)? OutcomeOf(Guid requestId)
            => _outcomes.TryGetValue(requestId, out var o) ? (o.Status, o.Payload as string) : null;

        /// <summary>Drains the fact events of this frame. Call once per frame after the bus swap; folding the same frame twice changes nothing
        /// (every fact is last-wins, the outcome ring keys on request id) — so the node's slave and a composing cache may both call it.</summary>
        public void Update()
        {
            if (_bus == null) return;
            DrainClusterState();
            DrainInSync();
            DrainInventory();
            DrainReplaySession();
            DrainOpOutcomes();
        }

        private void DrainClusterState()
        {
            foreach (var ev in _bus!.ReadManaged<ClusterStateUpdateEvent>())
            {
                State = ev.CurrentState;
                ExerciseId = ev.ExerciseId;
                if (ev.MasterGone)
                {
                    MasterGone = true;
                    ClusterPlayReady = false;   // ⭐ CE-3345 — no master, no cluster level
                    LoadProgress = string.Empty;
                }
                else
                {
                    MasterGone = false;
                    HasFollowedState = true;
                    ReplayAxis = ev.ReplayAxis;                // ⭐ CE-3329 R-3058
                    RunNotRecorded = ev.RunNotRecorded;        // ⭐ CE-4090 D14
                    RecordLiveRunsOff = ev.RecordLiveRunsOff;
                    AskToKeepRecordings = ev.AskToKeepRecordings;   // ⭐ AQ108 D15 (CE-4100 ①)
                    ArmedMode = ev.ArmedMode;                  // ⭐ CE-3341 R-3068
                    LastLoadMode = ev.LastLoadMode;            // ⭐ R-1043
                    WaitingFor = ev.WaitingFor ?? string.Empty; // ⭐ CE-3344 R-3069
                    ErrorReason = ev.ErrorReason ?? string.Empty; // ⭐ CE-3345 R-3073
                    ClusterPlayReady = ev.ClusterPlayReady;        // ⭐ CE-3345 R-3070
                    LoadProgress = ev.LoadProgress ?? string.Empty;
                }
                if (State != ClusterState.OperatingEdit) LastUnsavedCheck = null;   // ⭐ CE-4093 — the answer described an open scenario
            }
        }

        private void DrainInSync()
        {
            foreach (var ev in _bus!.ReadManaged<InSyncSetChangedEvent>())
            {
                HasInSyncSet = true;
                _inSyncNodeIds = ev.NodeIds ?? Array.Empty<int>();
            }
        }

        private void DrainInventory()
        {
            foreach (var ev in _bus!.ReadManaged<AssetInventoryUpdateEvent>())
            {
                HasInventory = true;
                AvailableScenarios       = ev.LocalScenarios           ?? Array.Empty<string>();
                AvailableExercises       = ev.LocalExercises           ?? Array.Empty<ExerciseInventoryItem>();
                ArchivedExercises        = ev.ArchivedExercises        ?? Array.Empty<ExerciseInventoryItem>();
                UnarchivedLocalExercises = ev.UnarchivedLocalExercises ?? Array.Empty<ExerciseInventoryItem>();
            }
        }

        private void DrainReplaySession()
        {
            foreach (var ev in _bus!.ReadManaged<ReplaySessionUpdateEvent>())
                if (ev.Session != null) ReplaySession = ev.Session;   // TransientLocal, KeepLast 1: the newest is the session
        }

        private void DrainOpOutcomes()
        {
            foreach (var ev in _bus!.ReadManaged<ClusterOpCompletedEvent>())
            {
                if (ev.StatusCode == OrchestrationStatusCode.InProgress) continue;   // not terminal

                // remember how each op ended (the last few): a requester that asked through the bus finds its answer by request id
                if (_outcomes.TryAdd(ev.RequestId, (ev.StatusCode, ev.ResultPayload)))
                {
                    _outcomeOrder.Enqueue(ev.RequestId);
                    while (_outcomeOrder.Count > OutcomeCapacity) _outcomes.Remove(_outcomeOrder.Dequeue());
                }
                else _outcomes[ev.RequestId] = (ev.StatusCode, ev.ResultPayload);

                if (ev.StatusCode == OrchestrationStatusCode.Success && ev.ResultPayload is UnsavedCheckResult check && State == ClusterState.OperatingEdit)
                    LastUnsavedCheck = check;
            }
        }
    }
}
