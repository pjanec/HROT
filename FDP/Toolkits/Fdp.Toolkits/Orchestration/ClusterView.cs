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

        /// <summary>A view over <paramref name="bus"/>; <see langword="null"/> (the test / offline shape) folds nothing.</summary>
        public ClusterView(FdpEventBus? bus) { _bus = bus; }

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

        /// <summary>⭐⭐ <c>CE-3341</c> (AQ108 D12a, R-3068) — the cluster's ARMED run mode, as the master's last broadcast said: ONE fact, the same on every node
        /// (<see cref="ClusterArmedMode.Live"/> before a broadcast arrives and for a sender that does not set it).</summary>
        public ClusterArmedMode ArmedMode { get; private set; } = ClusterArmedMode.Live;

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

        /// <summary>Drains the fact events of this frame. Call once per frame after the bus swap; folding the same frame twice changes nothing
        /// (every fact is last-wins, the outcome ring keys on request id) — so the node's slave and a composing cache may both call it.</summary>
        public void Update()
        {
            if (_bus == null) return;
            DrainClusterState();
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
                }
                else
                {
                    MasterGone = false;
                    HasFollowedState = true;
                    ReplayAxis = ev.ReplayAxis;                // ⭐ CE-3329 R-3058
                    RunNotRecorded = ev.RunNotRecorded;        // ⭐ CE-4090 D14
                    RecordLiveRunsOff = ev.RecordLiveRunsOff;
                    ArmedMode = ev.ArmedMode;                  // ⭐ CE-3341 R-3068
                }
                if (State != ClusterState.OperatingEdit) LastUnsavedCheck = null;   // ⭐ CE-4093 — the answer described an open scenario
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
