using System;
using Fdp.Core.Logging;
using Fdp.Toolkit.Orchestration;
using Hrot.NED.Descriptors.Orchestration;
using ClusterState = Hrot.NED.Descriptors.Orchestration.ClusterState;
using FdpClusterState = Fdp.Toolkit.Orchestration.ClusterState;

namespace Hrot.Orchestrator;

/// <summary>
/// ⭐⭐⭐ <c>CE-4090</c> (AQ108 D14, D15, §5a, D22) — <b>LIVE DOES NOT IMPLY RECORDING.</b> The cluster-wide setting
/// <i>Record live runs</i> (default ON) is held HERE, by the master — ⛔ never per host, two hosts would disagree about whether a run
/// records — and takes effect at the next live LOAD: the master puts the decision on that load's payload
/// (<c>EditLoadHandlerPayload.RecordingOff</c>), so every node sees the same one, and on every cluster-state publication
/// (<c>RunNotRecorded</c>), so every host's HTTP gate and Live icon read the same fact.
/// </summary>
/// <remarks>
/// <para>⭐ <b>Every gate refuses at the TOP, here</b> — never a throw deep in a node. Before this, a run with no recorder paused the
/// live clock for a review and only THEN threw in the prepare round (<c>ReferenceReviewHandler</c>), threw on every node for a
/// checkpoint, and left the inventory ledger offering a recording that did not exist (AQ108 §5a, M5). 📄
/// <c>docs/blueprints/Architect_Question_108_Run_Mode_Picker.md</c> §5a "as-built (CE-4090)" names each gate's site.</para>
/// <para>⭐ <b>Inverse polarity</b> — the flag is <i>not recorded</i>, so a publisher that never sets it (a test, an older peer) keeps
/// today's behaviour: record.</para>
/// <para>⭐ <b>Sticky on purpose:</b> the flag describes the LAST live run and stays until the next live load or a replay entry, so a
/// <c>POST /replay</c> that names no exercise, or a keep, still knows "the last run" was not recorded after the run has ended —
/// instead of silently replaying an older exercise.</para>
/// </remarks>
public sealed partial class ClusterMaster
{
    /// <summary>The reason every gate answers — the text the HTTP 409 and the menu's grey-out carry (AQ108 D14).</summary>
    public const string RunNotRecordedReason = RunRecordingGates.NotRecordedReason;

    /// <summary>⭐ CE-4090 (AQ108 D22) — why <c>POST /scenario/save</c> answers 409 in Live and AAR: that world is not the scenario.</summary>
    public const string SaveRefusedInRunReason = RunRecordingGates.SaveRefusedInRunReason;

    /// <summary>⭐ CE-4090 D14 — the cluster-wide <i>Record live runs</i> setting. Default ON. Read at the next live load.</summary>
    public bool RecordLiveRuns { get; set; } = true;

    /// <summary>⭐ AQ108 D15 (CE-4100 ①) — the cluster-wide <i>Ask to keep recordings</i> setting: when a live run ends, the operator UI asks Keep / Discard (default OFF — keep silently). Read by the UI off the broadcast; the master never acts on it.</summary>
    public bool AskToKeepRecordings { get; set; }

    /// <summary>⭐ <c>CE-3354</c> ③ — the NAMES (case-insensitive, de-duplicated, in the order given) of the nodes whose scenario load also waits for their built 3-D map. A setting of the MASTER, keyed by NAME (it survives a node restart and a new node id); a name no node holds now is kept. Broadcast with the cluster state; stamped on each LOAD's per-node payload.</summary>
    public IReadOnlyList<string> MapInLoadNodes { get; private set; } = Array.Empty<string>();

    /// <summary>⭐ CE-3354 ③ — whether the node named <paramref name="nodeName"/> waits for its map in a load.</summary>
    public bool MapInLoadFor(string? nodeName)
        => !string.IsNullOrEmpty(nodeName) && MapInLoadNodes.Contains(nodeName, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// ⭐⭐ <c>CE-3341</c> (AQ108 D12a, R-3068) — the cluster's ARMED run mode: what ▶ runs next, ONE fact held HERE and broadcast with every cluster-state
    /// publication (every node reads it through its <c>ClusterView</c>). ⭐ <c>CE-1443</c> (R-1043): its INITIAL value is a BOOTSTRAP parameter (<c>ClusterConfiguration.InitialArmedMode</c>, default <see cref="ClusterArmedMode.Live"/>;
    /// the editor's one-node config (<c>ForOneNodeCluster</c>) sets Preview — the ONE difference between the editor and every other cluster; ⛔ no file config). ⛔ <b>Never reset by a load</b>: only a <c>SetRunSettings{Armed}</c> changes it.
    /// </summary>
    public ClusterArmedMode ArmedMode { get; private set; }

    /// <summary>⭐ R-1043 — the mode the LAST scenario load went into (<see cref="ClusterArmedMode.Live"/> for <c>LoadingLive</c>, <see cref="ClusterArmedMode.Preview"/> for <c>LoadingEdit</c>),
    /// set when the load round STARTS; before any load it equals the initial armed mode. A fact the master holds and broadcasts beside <see cref="ArmedMode"/> — it never interprets it for a load.</summary>
    public ClusterArmedMode LastLoadMode { get; private set; } = ClusterArmedMode.Live;

    /// <summary>⭐ <c>CE-3354</c> ⑦ — the request id of the LAST scenario load the master started (<see cref="TransitionStateIntent.TransactionId"/>; whoever sent it), broadcast beside <see cref="LastLoadMode"/> so any host's <c>/status.load</c> follows it.</summary>
    public Guid LastLoadRequestId { get; private set; }

    /// <summary>⭐ CE-3354 ⑦ — the scenario that load named.</summary>
    public string? LastLoadScenario { get; private set; }

    /// <summary>
    /// ⭐ <c>CE-3354</c> ⑦ — a scenario load request is TAKEN by the master (held for the mandatory set, parked for its staging, planned, or refused): from this moment it IS the cluster's current load, announced at once, so
    /// <c>/status.load</c> never shows the PREVIOUS load's outcome after a new request. Whatever then happens to it — a refusal, a failed staging, a timeout, a failed round — publishes its op status for THIS id.
    /// </summary>
    private void NoteLoadAccepted(TransitionStateIntent intent)
    {
        if (!IsScenarioLoad(intent) || LastLoadRequestId == intent.TransactionId) return;
        LastLoadRequestId = intent.TransactionId;
        LastLoadScenario  = intent.ScenarioId;
        PublishClusterState(_currentDsmState);
    }

    private bool _runNotRecorded;


    /// <summary>⭐ CE-4090 — the last live run was started with recording OFF (see the remarks). Announced on every cluster-state publication.</summary>
    public bool RunNotRecorded => _runNotRecorded;

    private void ProcessRunSettingsIntents()
    {
        foreach (var intent in _eventBus.ReadManaged<SetRunSettingsIntent>())
            ProcessSetRunSettingsIntent(intent);
    }

    /// <summary>⭐ CE-4090 D14 — any host sets the cluster's run settings. Takes effect at the NEXT live load; the run on screen is not touched.</summary>
    private void ProcessSetRunSettingsIntent(SetRunSettingsIntent intent)
    {
        // ⭐ CE-3341: a field the op does not carry (null) is "no change" — an older sender, or one that sets only the other setting.
        if (intent.RecordLiveRuns is { } record) RecordLiveRuns = record;
        if (intent.Armed is { } armed) ArmedMode = armed;
        if (intent.AskToKeepRecordings is { } ask) AskToKeepRecordings = ask;   // ⭐ AQ108 D15 (CE-4100 ①)
        if (intent.MapInLoadNodes is { } mapNodes)   // ⭐ CE-3354 ③ — null = no change, empty = none; a blank name or one with a comma cannot be a node name
            MapInLoadNodes = mapNodes.Select(n => n?.Trim() ?? "").Where(n => n.Length > 0 && !n.Contains(','))
                                     .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        PublishClusterState(_currentDsmState);   // every host reads the settings off the broadcast, so tell them now
        FdpLog<ClusterMaster>.Info("[Orchestrator] CE-4090/CE-3341: run settings now: Record live runs {0} (from the next live load), armed mode {1}, ask to keep recordings {2}.", RecordLiveRuns, ArmedMode, AskToKeepRecordings);
        PublishOpStatus(intent.RequestId, OrchestrationStatusCode.Success);
    }

    /// <summary>
    /// ⭐ CE-4090 — decides, for a trajectory about to run, whether the live run it LOADS (from Idle) or BRANCHES (from a replay)
    /// records: the setting, or the verifier's <see cref="TransitionStateIntent.ForceRecording"/>. A replay entry clears the flag (a
    /// recording is open); every other trajectory leaves it as it was.
    /// </summary>
    private void NoteRunRecording(TransitionStateIntent intent, bool startsLiveRun, FdpClusterState resolvedTarget)
    {
        if (startsLiveRun)
        {
            _runNotRecorded = !(RecordLiveRuns || intent.ForceRecording);
            if (_runNotRecorded)
                FdpLog<ClusterMaster>.Info("[Orchestrator] CE-4090: this live run is NOT recorded (Record live runs is off).");
        }
        else if (resolvedTarget == FdpClusterState.OperatingReplay)
            _runNotRecorded = false;
    }

    /// <summary>⭐ CE-4090 (AQ108 D15) — discards an UNARCHIVED recording by id (the inventory's ledger entry and staging copies); set by the host that
    /// owns the inventory (<c>OrchestratorCore</c>), like <see cref="NewestExercise"/>. Returns false for an id it does not hold.</summary>
    public Func<Guid, bool>? ExerciseDeleter { get; set; }

    /// <summary>⭐ CE-4090 (AQ108 D15) — <c>DELETE /exercises/{id}</c> at the master: never the run still recording; an unknown id is refused.</summary>
    private void ProcessDeleteExercise(ExecuteStorageOpIntent intent)
    {
        if (intent.ExerciseId == Guid.Empty || ExerciseDeleter is null)
        {
            PublishOpStatus(intent.RequestId, OrchestrationStatusCode.Rejected, "an exercise id is required");
            return;
        }
        if (intent.ExerciseId == _activeExerciseId && !_runNotRecorded
            && (_currentDsmState == ClusterState.OperatingLive || _currentDsmState == ClusterState.OperatingReview))
        {
            FdpLog<ClusterMaster>.Warn("[Orchestrator] CE-4090: delete {0} REJECTED — exercise {1} is still recording.", intent.RequestId, intent.ExerciseId);
            PublishOpStatus(intent.RequestId, OrchestrationStatusCode.Rejected, "the exercise is still recording: stop the run first");
            return;
        }
        bool deleted = ExerciseDeleter(intent.ExerciseId);
        FdpLog<ClusterMaster>.Info("[Orchestrator] CE-4090: discard of exercise {0}: {1}.", intent.ExerciseId, deleted ? "deleted" : "unknown here");
        PublishOpStatus(intent.RequestId, deleted ? OrchestrationStatusCode.Success : OrchestrationStatusCode.Rejected,
                        deleted ? null : "no such unarchived exercise");
    }

    /// <summary>⭐ CE-4090 — refuses <paramref name="requestId"/> with <see cref="RunNotRecordedReason"/>; the caller returns after it.</summary>
    private void RefuseNotRecorded(Guid requestId, string what)
    {
        FdpLog<ClusterMaster>.Warn("[Orchestrator] CE-4090: {0} {1} REJECTED — {2}.", what, requestId, RunNotRecordedReason);
        PublishOpStatus(requestId, OrchestrationStatusCode.Rejected, RunNotRecordedReason);
    }
}
