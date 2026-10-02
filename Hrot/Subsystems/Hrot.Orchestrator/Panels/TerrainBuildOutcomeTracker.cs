using System;
using System.Collections.Generic;
using Fdp.Toolkit.Orchestration;

namespace Hrot.Orchestrator.Panels;

/// <summary>
/// ⭐⭐⭐ <c>E4</c> — the PER-NODE outcome of a terrain-asset build, so the panel can show which node
/// failed instead of one global OK.
///
/// <para>⛔⛔ <b>"Never one global OK" is a design requirement, not a nicety</b> (§8.3 N5, §9.2 U3). A
/// build either succeeded everywhere or it did not, and an operator who sees a single green tick cannot
/// tell WHICH node is now missing terrain — which is precisely the state that makes a node render and
/// path incorrectly while the cluster reports healthy. ⚠ <c>ClusterMaster</c> aggregates the round into
/// ONE <c>ClusterOpStatus</c>, so the per-node detail has to be collected from the per-node ACKs.</para>
///
/// <para>⭐ <b>Why the REQUESTER collects it.</b> Same reason as <c>ZoneLoadProgressTracker</c>: the
/// master's per-node responses live in a private tracker that is REMOVED the moment the round
/// completes, so by the time a panel could ask, the detail is gone. ⇒ the panel watches
/// <c>NodeOpCompletedEvent</c> as the round runs and keeps what it saw.</para>
///
/// <para>⚠ <b>Stated limit:</b> this shows the outcomes of the build THIS panel requested. It is cleared
/// when a new build starts, deliberately — a mixture of two rounds' outcomes would be worse than none.
/// </para>
///
/// 📄 docs/DESIGN_Terrain_Zones_And_Assets.md §8.3 N5, §9.2 U3, §3.1.
/// </summary>
public sealed class TerrainBuildOutcomeTracker
{
    /// <summary>One node's answer for the round being watched.</summary>
    /// <param name="Failed">
    ///   ⭐ Classified HERE, not by the panel. The panel's job is to render; deciding what counts as a
    ///   failure is domain knowledge, and putting it in the view would mean every future view re-deciding.
    /// </param>
    public readonly record struct NodeOutcome(
        int NodeId, OrchestrationStatusCode Status, NodeOpType Phase, bool Failed);

    private readonly Dictionary<int, NodeOutcome> _outcomes = new();

    /// <summary>The request this tracker is following, or <see cref="Guid.Empty"/> when idle.</summary>
    public Guid WatchedRequestId { get; private set; }

    /// <summary>Per-node outcomes seen so far, in node-id order for a stable display.</summary>
    public IReadOnlyList<NodeOutcome> Outcomes
    {
        get
        {
            var list = new List<NodeOutcome>(_outcomes.Values);
            list.Sort((a, b) => a.NodeId.CompareTo(b.NodeId));
            return list;
        }
    }

    /// <summary>⭐ True when at least one node reported a failure — what turns the summary red.</summary>
    public bool AnyFailed
    {
        get
        {
            foreach (var o in _outcomes.Values) if (o.Failed) return true;
            return false;
        }
    }

    /// <summary>
    /// Start watching a new build. ⚠ Clears the previous round's outcomes on purpose: showing two
    /// rounds' answers mixed together would be worse than showing none.
    /// </summary>
    public void Watch(Guid requestId)
    {
        WatchedRequestId = requestId;
        _outcomes.Clear();
    }

    /// <summary>
    /// Record one node's ACK.
    /// <para>⭐ Only the terrain-asset ops are recorded — the panel shares a bus with every other round,
    /// and folding an unrelated op's ACK into this list would misreport which node failed at what.</para>
    /// <para>⚠ The COMMIT phase overwrites the PREPARE phase for the same node, which is what an
    /// operator wants: the last thing that happened to that node is its outcome.</para>
    /// </summary>
    public void Record(int nodeId, NodeOpType phase, OrchestrationStatusCode status)
    {
        if (phase is not (NodeOpType.PrepareTerrainAsset or NodeOpType.CommitTerrainAsset)) return;
        _outcomes[nodeId] = new NodeOutcome(nodeId, status, phase, status.IsError());
    }

    /// <summary>Drop everything — a cluster boundary, or the operator dismissing the result.</summary>
    public void Reset()
    {
        WatchedRequestId = Guid.Empty;
        _outcomes.Clear();
    }
}
