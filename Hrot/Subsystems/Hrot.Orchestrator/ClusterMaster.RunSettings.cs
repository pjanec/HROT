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

    /// <summary>
    /// ⭐⭐ <c>CE-3341</c> (AQ108 D12a, R-3068) — the cluster's ARMED run mode: what ▶ runs next, ONE fact held HERE and broadcast with every cluster-state
    /// publication (every node reads it through its <c>ClusterView</c>). Default <see cref="ClusterArmedMode.Live"/> — D12's host default for every host but the
    /// editor, and the editor sends its own (Preview) with the load that opens a scenario. ⛔ <b>Never reset by a load</b>: only a <c>SetRunSettings{Armed}</c> changes it.
    /// </summary>
    public ClusterArmedMode ArmedMode { get; private set; } = ClusterArmedMode.Live;

    private bool _runNotRecorded;

    /// <summary>
    /// ⭐ CE-3345 (AQ108 §2.15) — <b>LOAD FOLLOWS THE ARMED MODE</b>: a scenario load with <see cref="TransitionStateIntent.FollowArmedMode"/> takes its target from <see cref="ArmedMode"/> —
    /// <c>Live</c> ⇒ <c>OperatingLive</c> (live, paused at the start), <c>Preview</c> ⇒ <c>OperatingEdit</c> (editing). <c>AAR</c> is no scenario load: the request's own target stands. A request that
    /// names its mode (the flag unset — HTTP) is returned unchanged.
    /// </summary>
    internal TransitionStateIntent ResolveArmedTarget(TransitionStateIntent intent)
    {
        if (!intent.FollowArmedMode || string.IsNullOrEmpty(intent.ScenarioId)) return intent;
        var target = ArmedMode switch
        {
            ClusterArmedMode.Live    => FdpClusterState.OperatingLive,
            ClusterArmedMode.Preview => FdpClusterState.OperatingEdit,
            _                        => intent.TargetState,
        };
        FdpLog<ClusterMaster>.Info("[Orchestrator] CE-3345: load {0} of '{1}' names no mode — the armed mode {2} picks {3}.", intent.TransactionId, intent.ScenarioId, ArmedMode, target);
        intent.TargetState = target;
        intent.FollowArmedMode = false;
        return intent;
    }

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
        PublishClusterState(_currentDsmState);   // every host reads the settings off the broadcast, so tell them now
        FdpLog<ClusterMaster>.Info("[Orchestrator] CE-4090/CE-3341: run settings now: Record live runs {0} (from the next live load), armed mode {1}.", RecordLiveRuns, ArmedMode);
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
