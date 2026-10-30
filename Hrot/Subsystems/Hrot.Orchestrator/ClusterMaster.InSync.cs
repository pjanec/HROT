using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.NED.Descriptors.Orchestration;
using Hrot.Network.Orchestration;
using Fdp.Core.Logging;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using ClusterState  = Hrot.NED.Descriptors.Orchestration.ClusterState;
using NodeOpType    = Hrot.NED.Descriptors.Orchestration.NodeOpType;
using FdpClusterState = Fdp.Toolkit.Orchestration.ClusterState;

namespace Hrot.Orchestrator;

/// <summary>
/// ⭐⭐⭐ <c>C3</c> + <c>C7</c> (Q91 §4-M) — the <c>InSync</c> set and the late joiner's catch-up.
/// <list type="bullet">
///   <item>I1 (<c>R-4006</c>): a node is in sync once it has ACKed the state the cluster is at; it leaves on ejection.
///     A node joining while the cluster is at rest in <c>Idle</c> is in sync at once (Idle is empty everywhere, <c>R-4019</c>).</item>
///   <item>J1 (<c>R-4013</c>): an OPERATOR round targets the in-sync nodes; a trajectory INTO <c>Idle</c> targets the whole
///     roster and leaves every node that answered it in sync — <c>Idle</c> is the universal re-sync point.</item>
///   <item>I2 + K4/K5′/K7 (<c>R-4014</c>, <c>R-4015</c>, <c>R-4017</c>): a node in the roster and not in sync, while the cluster
///     is operating, is caught up by per-node ops only — reset to <c>Idle</c>, the universal load parts, the operating state —
///     once per node per cluster commit; the scenario is never read.</item>
/// </list>
/// 📄 <c>docs/blueprints/Architect_Question_91_Player_Nodes.md</c> §4-M.
/// </summary>
public sealed partial class ClusterMaster
{
    private readonly HashSet<int> _inSync = new();

    /// <summary>The nodes in sync with the cluster — the targets of every operator round.</summary>
    public IReadOnlyCollection<int> InSyncNodeIds => _inSync;

    /// <summary>The scenario and exercise of the world the cluster runs — what a catch-up stages and records under.</summary>
    private string? _currentScenarioId;
    private Guid _currentExerciseId;
    /// <summary>⭐ CE-3299 D17 — the live session's recording mode (0 = FULL), set by its load and told to a late joiner.</summary>
    private double _currentCheckpointingSeconds;

    /// <summary>One catch-up in flight per node.</summary>
    private sealed class CatchUp
    {
        public int NodeId;
        public int StartedAtEpoch;
        public Guid RequestId;
        public Guid TransactionId;
        public FdpClusterState Operating;
        public bool Staged;
        public int Answered;
        public int Expected;
    }

    private readonly Dictionary<int, CatchUp> _catchUps = new();

    /// <summary>⭐ K7 — the cluster commit (epoch) at which each node's last catch-up was attempted: one attempt per commit.</summary>
    private readonly Dictionary<int, int> _catchUpAttemptedAt = new();

    /// <summary>The nodes currently being caught up — for rails and the panel.</summary>
    public IReadOnlyCollection<int> CatchingUpNodeIds => _catchUps.Keys;

    /// <summary>⭐ J1 — the targets of an operator round: the roster's in-sync nodes.</summary>
    private List<int> Participants() => _roster.ActiveNodes.Keys.Where(_inSync.Contains).ToList();

    /// <summary>
    /// ⭐⭐ <c>CE-3345</c> (R-3070, R-3072, AQ110 B/C) — <b>the cluster level of readiness</b>, combined from what each node reported about ITSELF on its heartbeat. Over the PARTICIPANTS only — the nodes
    /// <see cref="Participants"/> already names (in sync) that are not an observer (<see cref="IsObserver"/>, R-4014 K6) — so a joiner still catching up, or an observer, never holds anyone else.
    /// <para><c>PlayReady</c> = there is at least one participant and every one reports <see cref="Fdp.Toolkit.Orchestration.NodePhase.PlayReady"/> (so false in Idle, where nobody has a world);
    /// <c>Progress</c> = who is still loading and on which step (<i>"CGF (node 2): Terrain"</i>), empty when none is. ⭐ While a scenario-load round is in flight but no participant has yet reported
    /// loading, the text says the load is starting — the gate closes with the fan-out, not a heartbeat later.</para>
    /// </summary>
    private (bool PlayReady, string Progress) Readiness()
    {
        bool any = false, all = true;
        System.Text.StringBuilder? loading = null;
        foreach (var p in _roster.ActiveNodes.Values)
        {
            if (!_inSync.Contains(p.NodeId) || IsObserver(p.NodeId)) continue;
            any = true;
            if (p.Phase != Fdp.Toolkit.Orchestration.NodePhase.PlayReady) all = false;
            if (p.Phase is Fdp.Toolkit.Orchestration.NodePhase.Loading or Fdp.Toolkit.Orchestration.NodePhase.CatchingUp)
            {
                loading ??= new System.Text.StringBuilder();
                if (loading.Length > 0) loading.Append("; ");
                loading.Append(string.IsNullOrEmpty(p.SubsystemName) ? "node" : p.SubsystemName).Append(" (node ").Append(p.NodeId).Append(')');
                if (p.Step.Length > 0) loading.Append(": ").Append(p.Step);
            }
        }
        bool loadInFlight = false;
        foreach (var t in _pendingTransactions.Values) if (t.IsLoadRound) { loadInFlight = true; break; }
        string progress = loading?.ToString() ?? (loadInFlight ? "the load is starting" : string.Empty);
        return (any && all && !loadInFlight, progress);
    }

    /// <summary>I1 — a node entering the roster while the cluster is at rest in Idle has nothing to catch up.</summary>
    private void AdmitJoinedNode(int nodeId)
    {
        // ⭐ A PARKED transition has not chosen its targets yet (they are read when it resumes) — a node joining meanwhile is
        //   admitted. 📐 Measured live: SimHost's first heartbeat landed while a load was parked for its files; it was left out
        //   of the load and caught up afterwards. Had it been the Brain, the load would have run with no scenario creator.
        //   ⭐ CE-3346 (AQ110 E) — that admission is the deployment's choice: under Lobby a node joining while the load is parked is NOT admitted — it waits,
        //   not in sync, and C7 catches it up once the cluster operates. At rest in Idle (nothing parked) both policies admit.
        //   ⛔ RefuseUntilLoaded (AQ110 E's third value) is not built — only when a deployment needs it.
        bool parkedAndLobby = _parked != null && _config.AdmissionPolicy == Fdp.Toolkit.Orchestration.AdmissionPolicy.Lobby;
        if (_currentDsmState == ClusterState.Idle && !TransitionInFlight && !parkedAndLobby)
            _inSync.Add(nodeId);
    }

    /// <summary>
    /// A transition's round is still open. ⚠ <c>_inflightTransitionTx</c> alone does not say so: it keeps the LAST transition
    /// (history, cancel) after its round completed — only its pending tracker means it is still in flight.
    /// </summary>
    private bool TransitionInFlight =>
        _inflightTransitionTx is { } tx && _pendingTransactions.ContainsKey(tx.TransactionId);

    /// <summary>I1 — a node that left is no longer in sync, and its catch-up (if any) is dropped.</summary>
    private void ForgetSync(int nodeId)
    {
        _inSync.Remove(nodeId);
        _catchUps.Remove(nodeId);
    }

    // ── C7: the catch-up saga ─────────────────────────────────────────────────────────────────

    private static bool IsOperating(ClusterState s) => s is ClusterState.OperatingLive or ClusterState.OperatingEdit;

    /// <summary>
    /// ⭐⭐ C7 — every frame: start a catch-up for each roster node that is not in sync while the cluster is operating and
    /// stable, and advance the ones in flight. ⛔ Never through the operator's transition path (K4): node ops only, with
    /// their own transaction, never a parked slot (J2).
    /// </summary>
    private void TickCatchUps()
    {
        if (_eventBus == null) return;
        AdvanceCatchUps();
        PublishInSyncIfChanged();

        if (!IsOperating(_currentDsmState) || TransitionInFlight || _parked != null) return;
        foreach (var nodeId in _roster.ActiveNodes.Keys.ToList())
        {
            if (_inSync.Contains(nodeId) || _catchUps.ContainsKey(nodeId)) continue;
            if (_catchUpAttemptedAt.TryGetValue(nodeId, out var at) && at == _transactionEpoch) continue;   // ⭐ K7
            StartCatchUp(nodeId);
        }
    }

    private void StartCatchUp(int nodeId)
    {
        var c = new CatchUp
        {
            NodeId         = nodeId,
            StartedAtEpoch = _transactionEpoch,
            RequestId      = Guid.NewGuid(),
            TransactionId  = Guid.NewGuid(),
            Operating      = (FdpClusterState)(int)_currentDsmState,
        };
        _catchUps[nodeId]           = c;
        _catchUpAttemptedAt[nodeId] = _transactionEpoch;

        FdpLog<ClusterMaster>.Info(
            "[Orchestrator] C7: catching up node {0} into {1} (cluster epoch {2}, scenario '{3}').",
            nodeId, _currentDsmState, _transactionEpoch, _currentScenarioId ?? "(none)");

        // ① the files the universal parts need (terrain, TKB) — to THIS node only, through the ordinary prefetch saga.
        if (!string.IsNullOrEmpty(_currentScenarioId))
        {
            _eventBus!.PublishManaged(new ExecutePrefetchIntent
            {
                RequestId        = c.RequestId,
                ScenarioId       = _currentScenarioId!,
                ActiveNodeIds    = new List<int> { nodeId },
                NodeCapabilities = ActiveNodeCapabilities(),
            });
        }
        else
        {
            c.Staged = true;
            SendCatchUpOps(c);
        }
    }

    /// <summary>② reset to Idle (I2), the universal load parts (K5′), then the cluster's operating state — to THIS node only.</summary>
    private void SendCatchUpOps(CatchUp c)
    {
        var targets = new List<int> { c.NodeId };
        bool live = c.Operating == FdpClusterState.OperatingLive;
        var loading = live ? FdpClusterState.LoadingLive : FdpClusterState.LoadingEdit;
        var names = ReadContentNamesForFanOut(_currentScenarioId);

        FanOutNodeOp(NodeOpType.PrepareState, c.TransactionId,
            new EditLoadHandlerPayload(null, false, FdpClusterState.Idle), targets);
        FanOutNodeOp(NodeOpType.CommitState, c.TransactionId, new CommitStatePayload(FdpClusterState.Idle), targets);

        FanOutLoadPrepare(live ? NodeOpType.PrepareLive : NodeOpType.PrepareEdit, c.TransactionId,   // ⭐ CE-3354 ③ — a joiner named in the map-in-load set waits for its map too
            new EditLoadHandlerPayload(null, false, loading,
                ExerciseId: _currentExerciseId, TerrainName: names.TerrainName,
                // ⛔ NOT a world boundary. 📐 Measured live 2026-10-15: a joiner takes in the current world in the frames before it
                //   learns the master's session (its gate is not closed yet); a boundary clear here destroyed that entity and DDS
                //   never re-delivers a taken sample — the joiner lost entity 1000. A joiner already in Idle holds only the CURRENT
                //   world; a node with a stale one is wiped by the Idle step above (F2, R-4019).
                IsWorldBoundary: false, IsCatchUp: true, CheckpointingSeconds: _currentCheckpointingSeconds,
                RecordingOff: _runNotRecorded && live), targets);
        FanOutNodeOp(NodeOpType.CommitState, c.TransactionId, new CommitStatePayload(loading), targets);

        FanOutNodeOp(NodeOpType.PrepareState, c.TransactionId,
            new EditLoadHandlerPayload(null, false, c.Operating, ExerciseId: _currentExerciseId), targets);
        FanOutNodeOp(NodeOpType.CommitState, c.TransactionId, new CommitStatePayload(c.Operating), targets);

        c.Expected = 3;   // one answer per prepare; CommitState sends none
    }

    private void AdvanceCatchUps()
    {
        if (_catchUps.Count == 0) return;

        foreach (var staged in _eventBus!.ReadManaged<PrefetchDistributionCompletedEvent>())
        {
            var c = _catchUps.Values.FirstOrDefault(x => x.RequestId == staged.RequestId && !x.Staged);
            if (c == null) continue;
            if (!staged.IsSuccess) { FailCatchUp(c, "its files could not be staged"); continue; }
            c.Staged = true;
            SendCatchUpOps(c);
        }

        foreach (var status in _eventBus.ReadManaged<NodeOpCompletedEvent>())
        {
            var c = _catchUps.Values.FirstOrDefault(x => x.TransactionId == status.TransactionId && x.NodeId == status.NodeId);
            if (c == null) continue;
            if (status.StatusCode is OrchestrationStatusCode.InProgress or OrchestrationStatusCode.Pending) continue;   // P4
            if (status.StatusCode != OrchestrationStatusCode.Success)
            {
                FailCatchUp(c, $"{status.Operation} answered {status.StatusCode}");
                continue;
            }
            if (++c.Answered < c.Expected) continue;

            _catchUps.Remove(c.NodeId);
            if (c.StartedAtEpoch == _transactionEpoch && IsOperating(_currentDsmState))
            {
                _inSync.Add(c.NodeId);
                FdpLog<ClusterMaster>.Info("[Orchestrator] C7: node {0} caught up — in sync.", c.NodeId);
            }
            else
            {
                // ⭐ E3 — the cluster moved on during the catch-up: the node is not in sync; the next tick tries again at
                //   the new epoch (K7 allows one attempt per commit).
                FdpLog<ClusterMaster>.Info(
                    "[Orchestrator] C7: node {0} finished a catch-up for epoch {1}, but the cluster is at {2} — again.",
                    c.NodeId, c.StartedAtEpoch, _transactionEpoch);
            }
        }
    }

    private int[] _publishedInSync = Array.Empty<int>();

    /// <summary>
    /// ⭐⭐ C6 (Q91 §4-N) — every node's reliable-init barrier waits only for in-sync peers, so the set is published whenever it
    /// changes (the network adapter writes it to the durable <c>OrchestratorContextTopic.RequiredNodeIdsJson</c>).
    /// </summary>
    private void PublishInSyncIfChanged()
    {
        var now = _inSync.OrderBy(id => id).ToArray();
        if (now.SequenceEqual(_publishedInSync)) return;
        _publishedInSync = now;
        _eventBus!.PublishManaged(new InSyncSetChangedEvent { NodeIds = now });
    }

    private void FailCatchUp(CatchUp c, string why)
    {
        _catchUps.Remove(c.NodeId);
        FdpLog<ClusterMaster>.Error(
            "[Orchestrator] C7: the catch-up of node {0} FAILED ({1}). It stays out of sync until the next cluster commit "
          + "or a new process (K7).", c.NodeId, why);
    }
}
