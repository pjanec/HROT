using System;
using System.Collections.Generic;
using Fdp.Core.Logging;
using Fdp.Toolkit.Orchestration;
using Hrot.NED.Descriptors.Orchestration;
using ClusterState = Hrot.NED.Descriptors.Orchestration.ClusterState;
using NodeOpType = Hrot.NED.Descriptors.Orchestration.NodeOpType;

namespace Hrot.Orchestrator;

/// <summary>
/// ⭐⭐⭐ <c>CE-4093</c> (AQ108 D20) — <b>THE CHECK ROUND: "has the scenario open for editing changed?", asked on demand, answered by every node.</b>
/// A node compares its whole world with the baseline it took when the load committed or its slice was last written
/// (<c>WorldChangeCheck</c>, <c>CE-4093</c> node side) and answers "changed" / "unchanged" / "no-baseline"; this master asks, collects and
/// aggregates (<see cref="UnsavedCheckAggregator"/>) — nothing is checked periodically and nothing is shown (no indicator).
/// 📄 docs/blueprints/Architect_Question_108_Run_Mode_Picker.md §4a "as-built (CE-4093 master)".
/// </summary>
/// <remarks>
/// <para>⭐ <b>Meaningful only while a scenario is open for EDITING</b> (<see cref="ClusterState.OperatingEdit"/>): in Idle there is nothing to
/// lose, in Preview / Live / a replay the world is a run's, not the scenario's — the round is REFUSED there (Rejected + the reason), never answered
/// "unchanged" as if it had looked.</para>
/// <para>⭐ <b>The master's own fact.</b> A node cannot know a save failed AFTER its slice was written (the slice pull to the NAS, or the merge
/// into the one <c>scenario.json</c>, can fail — §4a "open edges"): the node re-baselined at its <c>Commit</c> and reads "unchanged" while the file
/// on disk is stale. So a scenario save round that fails after the last load into editing makes the master itself report <i>Changed</i>
/// (<see cref="SaveFailedSinceLoad"/>), until the next load into editing or a save that completes.</para>
/// </remarks>
public sealed partial class ClusterMaster
{
    /// <summary>The refusal's text — the HTTP 409 and the UI carry it.</summary>
    public const string UnsavedCheckRefusedReason = UnsavedCheckOutcome.NotEditingReason;

    /// <summary>⭐ CE-4093 — the newest check round's answer (also on every host's bus as the op's result); null until one completed, and after
    /// the cluster leaves editing or loads another scenario (the answer no longer describes what is open).</summary>
    public UnsavedCheckResult? LastUnsavedCheck { get; private set; }

    private bool _saveFailedSinceLoad;

    /// <summary>⭐ CE-4093 — a scenario save round failed after the last load into editing and no save has completed since: the file on disk is
    /// stale whatever the nodes' worlds say. Cleared by the next load into editing, or by a save that completes (slices written AND merged).</summary>
    public bool SaveFailedSinceLoad => _saveFailedSinceLoad;

    /// <summary>The scenario-save rounds in flight: their node round (SerializeLocal) has not completed yet.</summary>
    private readonly HashSet<Guid> _saveRounds = new();

    /// <summary>The scenario-save rounds whose nodes all wrote their slice and whose NAS pull + merge (<c>StorageProcessManager</c>) is still to report.</summary>
    private readonly HashSet<Guid> _saveMerges = new();

    private void ProcessUnsavedCheckIntents()
    {
        foreach (var done in _eventBus.ReadManaged<ScenarioSaveFinishedEvent>())
            NoteSaveFinished(done.TransactionId, done.Succeeded);
        foreach (var intent in _eventBus.ReadManaged<CheckUnsavedIntent>())
            StartUnsavedCheck(intent.RequestId);
    }

    /// <summary>
    /// ⭐ CE-4093 — one check round: <see cref="NodeOpType.CheckUnsaved"/> to every participant, the aggregate (plus the master's own fact) as the
    /// op's result. Modelled on <c>CollectCheckpoint</c>: the same fan-out, the same <see cref="GenericTransactionTracker"/>; no new round mechanism.
    /// </summary>
    private void StartUnsavedCheck(Guid requestId)
    {
        if (_currentDsmState != ClusterState.OperatingEdit)
        {
            FdpLog<ClusterMaster>.Warn("[Orchestrator] CE-4093: check-unsaved {0} REJECTED — the cluster is in {1}, not editing.", requestId, _currentDsmState);
            PublishOpStatus(requestId, OrchestrationStatusCode.Rejected, UnsavedCheckRefusedReason);
            return;
        }
        var nodeIds = Participants();   // ⭐ C3 — operator rounds: in-sync nodes only
        if (nodeIds.Count == 0)
        {
            // nobody to ask: only the master's own fact can say "changed"
            PublishOpStatus(requestId, OrchestrationStatusCode.Success, RememberUnsavedCheck(null, nobodyAsked: true));
            return;
        }
        var txId = Guid.NewGuid();
        FanOutNodeOp(NodeOpType.CheckUnsaved, txId, null, nodeIds);
        _pendingTransactions[txId] = new GenericTransactionTracker
        {
            RequestId     = requestId,
            Expected      = nodeIds.Count,
            RefineResult  = aggregated => RememberUnsavedCheck(aggregated),
        };
    }

    /// <summary>The round's aggregate with the master's own fact folded in, remembered as <see cref="LastUnsavedCheck"/> and returned as the result.
    /// ⛔ An aggregate that is missing although nodes were asked (no aggregator registered) reads as CHANGED — nothing said "unchanged".</summary>
    private UnsavedCheckResult RememberUnsavedCheck(object? aggregated, bool nobodyAsked = false)
    {
        var nodes = aggregated as UnsavedCheckResult
            ?? (nobodyAsked ? UnsavedCheckResult.Unchanged : new UnsavedCheckResult(true, Array.Empty<int>()));
        var result = _saveFailedSinceLoad && !nodes.Changed ? nodes with { Changed = true } : nodes;
        LastUnsavedCheck = result;
        FdpLog<ClusterMaster>.Info("[Orchestrator] CE-4093: the scenario is {0} (nodes changed: [{1}], a save failed since the load: {2}).",
            result.Changed ? "CHANGED" : "unchanged", string.Join(",", result.ChangedNodes), _saveFailedSinceLoad);
        return result;
    }

    /// <summary>A scenario save began its node round (<c>SerializeLocal</c>) under <paramref name="txId"/>.</summary>
    private void NoteSaveRoundStarted(Guid txId) => _saveRounds.Add(txId);

    /// <summary>
    /// ⭐ CE-4093 — a node round completed: a scenario save's failed ⇒ the scenario is unsaved; one that succeeded waits for the NAS pull + merge
    /// (<see cref="ScenarioSaveFinishedEvent"/>) unless it carries no slices to pull (then it is complete now).
    /// </summary>
    private void NoteRoundCompletedForUnsavedCheck(Guid txId, bool failed, object? aggregated)
    {
        if (!_saveRounds.Remove(txId)) return;
        if (failed) { _saveFailedSinceLoad = true; return; }
        if (aggregated is List<Hrot.Network.Orchestration.FileManifestEntry> { Count: > 0 }) _saveMerges.Add(txId);
        else _saveFailedSinceLoad = false;   // nothing to pull: the save is complete
    }

    /// <summary>⭐ CE-4093 — the pull + merge of a scenario save reported: complete ⇒ the file on disk is current; failed ⇒ it is not.</summary>
    private void NoteSaveFinished(Guid txId, bool succeeded)
    {
        if (!_saveMerges.Remove(txId)) return;
        _saveFailedSinceLoad = !succeeded;
    }

    /// <summary>⭐ CE-4093 — the cluster loads a scenario into editing, or goes to Idle: the scenario is a different one (or none), so a failed
    /// save of the old one and the old answer no longer apply.</summary>
    private void NoteLoadForUnsavedCheck(ClusterState resolvedTarget, string? scenarioId)
    {
        bool loadsIntoEditing = resolvedTarget == ClusterState.OperatingEdit && !string.IsNullOrEmpty(scenarioId);
        if (!loadsIntoEditing && resolvedTarget != ClusterState.Idle) return;
        _saveFailedSinceLoad = false;
        _saveRounds.Clear();
        _saveMerges.Clear();
        LastUnsavedCheck = null;
    }
}
