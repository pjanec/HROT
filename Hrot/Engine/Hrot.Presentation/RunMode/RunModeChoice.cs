using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Presentation.Fonts;
using Fdp.Toolkit.Orchestration;
using NodeEditor.Core.Action;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.Panels;

namespace Hrot.UI.Common.RunMode;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-4097</c> (AQ108 §2.8) — the run-mode choice: ONE object per host that holds what ▶ will run and owns rules R1–R3.</b>
/// 📄 <c>docs/blueprints/Architect_Question_108_Run_Mode_Picker.md</c> §1 (the three rules), §2.8 (classes + sequences), §3 (every move), D1 D5 D10 D12 D14 D22.
///
/// <list type="bullet">
/// <item><b>R1′</b> (<c>CE-3405</c>, AQ108 §0) — <b>switch now</b>: <see cref="Choose"/> (the indicator's menu) SWITCHES the mode at once instead of arming it. From Edit: → Live asks the unsaved question through the ONE door
/// (<i>Save / Discard / Cancel</i>; never saved: <i>Save as… and go live</i>) and loads Live WITHOUT the resume — it lands paused at 0:00, the briefing; → AAR opens the last exercise (or the picker) at its start through the door. ▶ in Edit is
/// ALWAYS the Preview; ▶ in a paused Live resumes (the facade's own toggle). The cluster's armed-mode fact (<see cref="Armed"/>) is still SET by the switch, so a later File ▸ Open goes into the current mode (R-1043); ▶ ⏸ ■ and loads never set it.</item>
/// <item><b>R2′</b> — <see cref="Stop"/> is the start of the SAME mode: Preview → the dry run's exit (back to Edit); DAR → back to live; AAR → seek to 0.</item>
/// <item><b>R2″</b> (<c>CE-3413</c>, D30) — <b>Live ends in two steps</b>: ■ in a running Live asks <i>End the exercise?</i>; the end runs and the world STAYS in Live, paused at the end — <see cref="LiveEnded"/> is DERIVED
/// (<c>OperatingLive</c> + the replicated <c>ExerciseResult.Phase == Ended</c>, read through <see cref="IExerciseEnder.HasEnded"/>; no FSM state). There ■ becomes ⏏ <i>Close the session</i>: <i>Close the live session?</i> → the door's Leave to Idle on every node,
/// then <i>Keep this recording?</i> and the <i>Exercise recorded</i> note. <see cref="RunAgain"/> reloads the same scenario into the briefing. ⛔ Nothing in Live leaves Live by itself (a host with no ender keeps today's ■).</item>
/// <item><b>R3</b> — a confirmation (<see cref="Pending"/>) only where a jump or a loss needs explaining; ■ never asks.</item>
/// </list>
///
/// ⛔ It re-implements NO scenario action: it calls <see cref="IRunScenarioControl"/> (the session's own) and the raw transport
/// (<see cref="IRunTransport"/>, the one facade's today's behaviours). The facade's <c>Stop</c> delegates here, so ■ in the toolbar and
/// <c>POST /sim/stop</c> stay ONE call (D22). ⭐ Part two (D22): the facade's Play / Step stay RAW — a USER control's ▶ calls <see cref="Play"/> through
/// <see cref="UserTransport"/>, an HTTP <c>POST /sim/play</c> never follows the armed mode.
/// </summary>
public sealed class RunModeChoice
{
    private readonly IRunTransport _transport;
    private readonly IRunScenarioControl? _control;
    private readonly IUnsavedChangesQuery _unsaved;
    private readonly Func<DateTime> _now;

    private readonly ScenarioDoor? _door;           // ⭐ CE-4101 — the session's ONE guarded door (every USER move in or out goes through it); null on a host with no scenario session
    private readonly ConfirmingAsker _asker = new(); // ⭐ CE-4101 — the one pending slot the toolbar draws (the door's questions and this choice's own confirmations)

    private ClusterState _lastState = ClusterState.Idle;
    private RunArmed _localArmed;       // ⭐ CE-3341 — what THIS host last armed; shown only until the cluster's value next CHANGES (see Armed)
    private RunArmed? _seenCluster;     // ⭐ CE-3341 — the cluster's armed mode as last observed (the change detector for _localArmed)
    private bool _resumeWhenLive;      // ▶ in Live armed: the load lands paused (Q86 §4-G); ▶ means run, so the clock resumes once Live is up
    private bool _previewWhenEdit;     // ⭐ CE-3393 — ▶ in Edit with a preset the world does not hold: reopened with it, the dry run starts once editing is back
    private RunArmed? _switchWhenEdit; // ⭐ CE-3405 — the indicator chose Live / AAR while a Preview ran: the preview stops (restores), and the switch is made once editing is back
    private bool _ended;               // ⭐ CE-3413 — Live · ended (D30), read once per frame in Update (the ender's world query is not per-call)
    private bool _asking;

    /// <summary>⭐ <c>CE-4101</c> follow-up — the session's ONE guarded door this choice is bound to (<see langword="null"/> on a host with no scenario session). The window composition builds its exit guard over the PICKED owner's door.</summary>
    public ScenarioDoor? Door => _door;

    /// <summary>⭐⭐⭐ <c>CE-1442</c> (U1) — the ONE run gate (see <see cref="RunGate"/>): every control of this host reads it, none reads the cluster view itself.</summary>
    public RunGate Gate => _transport.Gate;

    /// <summary>⭐ CE-1442 — why the controls are closed now, or <see langword="null"/> when the gate is open. What ▶, the mode menu, the shared scenario commands and the door's refusal all say.</summary>
    public string? ClosedReason => Gate is { Open: false } g ? g.Reason ?? "not ready" : null;

    /// <summary>⭐ CE-1442 (§11.7 ⑥) — the loading screen's Cancel: the host's slave cancels the load (it fails everywhere, the cluster returns to Idle).</summary>
    public void CancelLoad(string? reason) => _transport.CancelLoad(reason);

    /// <summary>⭐ CE-4100 ① — the cluster's <i>Record live runs</i> setting (null = no cluster view here).</summary>
    public bool? RecordLiveRunsSetting => _transport.RecordLiveRunsSetting;
    /// <summary>⭐ CE-4100 ① — the cluster's <i>Ask to keep recordings</i> setting (null = no cluster view here).</summary>
    public bool? AskToKeepSetting => _transport.AskToKeepSetting;
    /// <summary>⭐ CE-3415 (AQ108 §0 D31) — the cluster's <i>Start immediately</i> setting (null = no cluster view here).</summary>
    /// <summary>⭐ CE-3414 (AQ108 §0 D31) — the master's scenario inventory (names); the same array until a new inventory arrives.</summary>
    public string[] InventoryScenarios => _transport.InventoryScenarios;
    /// <summary>⭐ CE-3414 (AQ108 §0 D31) — the master's recordings inventory (local, archived, unarchived-local); the same arrays until a new inventory arrives.</summary>
    public (ExerciseInventoryItem[] Local, ExerciseInventoryItem[] Archived, ExerciseInventoryItem[] UnarchivedLocal) InventoryExercises => _transport.InventoryExercises;
    public bool? StartImmediatelySetting => _transport.StartImmediatelySetting;
    /// <summary>⭐ CE-4100 ① — set the cluster's run settings; a null argument leaves that one as it is.</summary>
    public void SendRunSettings(bool? recordLiveRuns, bool? askToKeepRecordings, bool? startLiveImmediately = null) => _transport.SendRunSettings(recordLiveRuns, askToKeepRecordings, startLiveImmediately);

    /// <summary>⭐ <c>CE-1437</c> — this host has a scenario session (an <i>operator</i> host: editor, CGF); false on SimHost / IG. Read by the run-controls pick rule.</summary>
    public bool HasScenarioControl => _control is not null;

    /// <summary>⭐ R-4001 — the master's unsaved-changes round is open (shown as "checking…"; no timeout — the operator may <see cref="CancelCheck"/>).</summary>
    public bool IsCheckingUnsaved => _asking || (_door?.IsCheckingUnsaved ?? false);

    /// <summary>⭐ R-4001 — the operator gives up waiting for the round: nothing proceeds, the move is not made.</summary>
    public void CancelCheck()
    {
        _door?.CancelCheck();
        if (!_asking) return;
        _unsaved.Cancel();
        _asking = false;
    }

    private bool Busy => Pending is not null || _asking || (_door?.IsBusy ?? false);

    /// <summary>How long a <see cref="Toast"/> shows.</summary>
    public static readonly TimeSpan ToastLifetime = TimeSpan.FromSeconds(8);

    /// <param name="transport">The one facade's raw transport.</param>
    /// <param name="control">The host's scenario session; <see langword="null"/> on a host with none (SimHost, IG) — Live-armed ▶ is then disabled with a reason.</param>
    /// <param name="unsaved">The unsaved-changes seam; default <see cref="ClusterUnsavedChangesQuery"/> with no master and no reads: observes nothing, so a named scenario counts as unchanged (the adapter passes the real one).</param>
    /// <param name="now">Clock of the toast (tests inject one).</param>
    /// <param name="toasts">⭐ CE-1441 — the shared <see cref="ToastQueue"/> the notes go to (the host may pass its own); default a private one.</param>
    public RunModeChoice(IRunTransport transport, IRunScenarioControl? control,
                         IUnsavedChangesQuery? unsaved = null, Func<DateTime>? now = null, ToastQueue? toasts = null)
    {
        Toasts     = toasts ?? new ToastQueue();
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _control   = control;
        // ⭐ part two: the production query is the master's check round (ClusterUnsavedChangesQuery, built by the adapter). A choice built WITHOUT one
        //   (a bare rail) observes nothing: a named scenario counts as unchanged, no name = NeverSaved — the same rule a host with no read model gets.
        _unsaved   = unsaved ?? new ClusterUnsavedChangesQuery(() => control?.LoadedScenarioName, master: null, reads: null);
        _now       = now ?? (() => DateTime.UtcNow);
        _localArmed  = transport.ClusterArmed ?? MasterDefaultArmed;   // ⭐ CE-1443 (R-1043): no host default — the cluster's armed mode (the master's, set by ITS bootstrap), or the master's own default before one is heard
        _seenCluster = transport.ClusterArmed;   // the cluster's value at construction is the baseline: it shows until it CHANGES
        // ⭐ AQ108 D8 — Save in Live / DAR / AAR is a checkpoint: routed by construction for every save path of the host's ONE session
        if (control is ISaveInterception saves) saves.SaveIntercept = TryCheckpointInsteadOfSave;
        // ⭐⭐ CE-4101 (AQ108 §2.11a) — the decision "a run ⇒ Stop & Load · unsaved ⇒ Save / Discard / Cancel" lives in the session's ONE door; this choice binds it to its transport, its check round and its
        //   pending slot. A control with no door of its own (a rail's fake) gets one over the same raw verbs, so the choice calls the door either way.
        if (control is not null)
        {
            _door = (control as IHasScenarioDoor)?.Door ?? new ScenarioDoor(control, () => transport.Mode);
            _door.Bind(() => transport.Mode, _unsaved, _asker);
            _door.RecordingOpening = exercise => LastExercise = exercise;   // bookkeeping for EVERY caller
            // ⭐⭐ CE-1443 (R-1043) — a scenario LOAD goes into the cluster's CURRENT armed mode: the door asks THIS choice (one resolver, below). ⛔ No open sends an armed mode (CE-3341 decision ③ withdrawn); only the indicator's Choose does.
            _door.LoadTarget = () => LoadTarget;
            _door.ClosedReason = () => ClosedReason;   // ⭐ CE-1442 — the backstop: the door refuses every non-leave move while the gate is closed
            _door.AlsoBusy = () => _asking;   // ▶ Live's own check round shares the one unsaved seam
        }
    }

    private long? _pendingBranchSeekWall;   // ⭐ D5: ■ in a branched run — seek the parent's AAR here once its session is known
    private int _branchInFrames;   // ⭐ D21 "Go live from here": the branch intent goes out this many frames AFTER the seek's (see GoLiveFromHere)

    /// <summary>
    /// ⭐ <c>CE-4097</c> part two — once per frame (the adapter's <c>Update</c>): delivers a check-round answer that has arrived (<see cref="IUnsavedChangesQuery.Poll"/>,
    /// no blocking) and sends a deferred branch. ⛔ Nothing here waits.
    /// </summary>
    public void Update()
    {
        _unsaved.Poll();
        if (_pendingBranchSeekWall is { } wall && _transport.Mode == TransportMode.Replay && _transport.ReplayZeroWallTicks is not null)
        {
            _pendingBranchSeekWall = null;
            _transport.SeekWall(wall);
        }
        if (_branchInFrames > 0 && --_branchInFrames == 0 && _transport.Mode == TransportMode.Replay) _transport.BranchLive();
        _ended = _transport.Mode == TransportMode.Live && (_transport.Ender?.HasEnded ?? false);   // ⭐ CE-3413 (D30) — derived once per frame; the toolbar reads it several times
    }

    /// <summary>⭐ CE-3413 (D30) — <b>Live · ended</b>: the cluster is still in Live and the replicated <c>ExerciseResult.Phase</c> is Ended (derived from the ender's world, refreshed once per frame in <see cref="Update"/>; ⛔ no FSM state). ▶ is greyed, ■ is ⏏ Close the session.</summary>
    public bool LiveEnded => _ended && _transport.Mode == TransportMode.Live;

    /// <summary>⭐ CE-3413 — Live, never ran: the briefing (paused, the sim clock at 0). ■ has nothing to stop there and ⏪ Review nothing to look at; ▾ presets reload it.</summary>
    public bool LiveBriefing => _transport.Mode == TransportMode.Live && _transport.IsPaused && _transport.TotalTime <= 0 && !_ended;

    /// <summary>⭐ CE-3413 — why ■ cannot be pressed now (null = it can): the briefing has nothing to stop (greyed, like a ■ in Edit).</summary>
    public string? StopDisabledReason => LiveBriefing ? "nothing to stop — the exercise has not started" : null;

    /// <summary>⭐ CE-3413 — why ⏪ Review is greyed now (null = it can show): during the briefing there is nothing to look back at.</summary>
    public string? ReviewDisabledReason => LiveBriefing ? "nothing to review yet" : null;

    /// <summary>
    /// What ▶ runs from Edit. ⭐ CE-3341 (AQ108 §2.14, D12a) — the CLUSTER's armed mode (<see cref="IRunTransport.ClusterArmed"/>): one fact on every node. The value this host last armed (<see cref="Arm"/>)
    /// shows only until the cluster's value next CHANGES (so the click shows at once; a later arm from another host wins). With no cluster value, the local one. R1: only <see cref="Choose"/> changes it (R-1043: an open never does).
    /// </summary>
    public RunArmed Armed
    {
        get
        {
            if (_transport.ClusterArmed is { } cluster)
            {
                if (cluster != _seenCluster) { _seenCluster = cluster; _localArmed = cluster; }
                return _localArmed;
            }
            return _localArmed;
        }
    }

    /// <summary>⭐ CE-1443 — the master's own armed mode before anything else says: <see cref="RunArmed.Live"/> (<c>ClusterMaster</c>'s default; ⛔ the editor's in-process master is BOOTSTRAPPED to Preview, which reaches every node through <c>ClusterView</c>, not through a host table here).</summary>
    public const RunArmed MasterDefaultArmed = RunArmed.Live;

    /// <summary>
    /// ⭐⭐⭐ <c>CE-1443</c> (U2a, R-1043) — <b>the ONE resolution of "what does a scenario LOAD go into"</b>: the CLUSTER's current armed mode, on every host. Live ⇒ a load into Live (paused at the start); Preview ⇒ a load into editing;
    /// AAR (or DAR) ⇒ the mode the LAST scenario load went into (<see cref="IRunTransport.ClusterLastLoad"/> = <c>ClusterView.LastLoadMode</c>, the master's — R-1043, user-approved), so every host resolves it alike. Called through <see cref="ScenarioDoor.Load"/> by the File menu, Start's Recent rows and every picker.
    /// </summary>
    public DoorOpen LoadTarget => Armed switch
    {
        RunArmed.Live    => DoorOpen.Live,
        RunArmed.Preview => DoorOpen.Scenario,
        _                => _transport.ClusterLastLoad == RunArmed.Live ? DoorOpen.Live : DoorOpen.Scenario,
    };

    /// <summary>⭐ CE-3341 — the ONE writer of the armed mode: sets the local (optimistic) value and sends it to the cluster as <c>SetRunSettingsIntent { Armed }</c>. ⭐ CE-1443: written ONLY by the indicator's moves (<see cref="Choose"/>) and the moves that name their mode (AAR from here, go live from here, ■ ...); never by a load.</summary>
    private void Arm(RunArmed target)
    {
        _ = Armed;                       // adopt a cluster change heard before this arm, so it is not mistaken for a later one
        _localArmed = target;
        _transport.SendArmed(target);
    }

    /// <summary>The exercise of the last live run this choice ended (■ or → AAR) — what AAR armed opens at its start; <see langword="null"/> before any.</summary>
    public Guid? LastExercise { get; private set; }

    /// <summary>The confirmation waiting for a button (R3); <see langword="null"/> when none. The toolbar draws it as a modal.</summary>
    public RunModeConfirmation? Pending => _asker.Pending;

    /// <summary>
    /// ⭐ Host seam for "Save as… and go live": asks the user for a scenario NAME, SAVES the scenario under it, then calls back with the name.
    /// ⚠ Contract: the save has happened when the callback runs (the host's Save-As dialog saves; this choice then loads that name live).
    /// <see langword="null"/> = this host has no Save-As dialog (the button then does nothing).
    /// </summary>
    public Action<Action<string>>? SaveAsPrompt { get; set; }

    /// <summary>⭐ Host seam: opens the shared recording picker — what ▶ does in AAR armed when no exercise has run yet. <see langword="null"/> = none.</summary>
    public Action? PickRecording { get; set; }

    /// <summary>
    /// ⭐ CE-1441 (audit #19) — the notes of this choice go through the repo's ONE toast mechanism: an <see cref="EditorNotification"/>
    /// enqueued on a <see cref="ToastQueue"/> (there is no second toast type). The toolbar is this queue's drainer, via <see cref="Toast"/>.
    /// </summary>
    public ToastQueue Toasts { get; }

    /// <summary>The <see cref="NotificationAction.CommandId"/> of the "open the AAR" button of the exercise-recorded note.</summary>
    public const string ReplayActionId = "run.replay-last";

    /// <summary>The note after ■ in Live / a checkpoint (AQ108 §3): the newest notification on <see cref="Toasts"/> that has not expired; <see langword="null"/> when none.
    /// Drains the queue (latest wins, as the old single slot did).</summary>
    public EditorNotification? Toast
    {
        get
        {
            while (Toasts.TryDequeue(out var n)) _current = (n, _now() + (n.AutoDismiss ?? ToastLifetime));
            return _current is { } c && _now() < c.ExpiresAt ? c.Notification : null;
        }
    }
    private (EditorNotification Notification, DateTime ExpiresAt)? _current;

    private void Notify(string text, bool offersReplay = false)
        => Toasts.Enqueue(new EditorNotification("run-mode", NotificationSeverity.Info, text, null, ToastLifetime,
            offersReplay ? new[] { new NotificationAction("AAR", ReplayActionId) } : null));

    // ── ▶ ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Why ▶ is disabled, or <see langword="null"/> when it can run: the gate first (CE-1442); ⭐ CE-3413 — in <see cref="LiveEnded"/> ▶ is greyed (the exercise is over). ⭐ CE-3405: ▶ in Edit is ALWAYS the Preview, so no armed mode can make it unrunnable; a host with no scenario session keeps ▶ greyed in Edit
    /// (Live / AAR need a scenario session or a recording only at the SWITCH — <see cref="Moves"/> says why a switch is greyed).
    /// </summary>
    public string? PlayDisabledReason
    {
        get
        {
            if (RunPlayDisabledReason is { } why) return why;
            if (_control is null && _transport.Mode is TransportMode.Edit or TransportMode.Idle) return NoSessionPlayReason;   // a host with no scenario session (SimHost, IG) has nothing of its own to preview: ▶ stays greyed there, as it was
            return null;
        }
    }

    /// <summary>The reasons that hold in EVERY mode: the gate first (CE-1442), then ⭐ CE-3413 — Live · ended (the exercise is over). What a USER's ▶ outside Edit asks (<see cref="UserTransport"/>).</summary>
    public string? RunPlayDisabledReason => ClosedReason is { } closed ? closed : LiveEnded ? LiveEndedPlayReason : null;

    /// <summary>The tooltip of ▶ in Edit on a host with no scenario session (SimHost, IG).</summary>
    public const string NoSessionPlayReason = "No scenario session on this host — nothing to preview from here";

    /// <summary>⭐ CE-3413 — the tooltip of the greyed ▶ in Live · ended.</summary>
    public const string LiveEndedPlayReason = "the exercise has ended — restart from a checkpoint to train again";

    /// <summary>
    /// ▶ (or Step) from Edit — ⭐ CE-3405 (R1′): ALWAYS the Preview (the dry run of the scenario being edited); the mode is switched at the indicator, never by ▶. In Live ▶ is the facade's own resume
    /// (<see cref="UserTransport"/>). ⛔ Never changes <see cref="Armed"/>.
    /// </summary>
    /// <param name="startPaused">Step from Edit: the dry run paused at its first frame.</param>
    public void Play(bool startPaused = false)
    {
        if (Busy) return;   // a question is open: ▶ does not stack another
        if (PlayDisabledReason is not null) return;
        // ⭐ CE-3393 — a preset the world does not hold: reopen with it (the door asks first), the dry run starts once editing is back (presets §3)
        if (ArmedPreset is { } preset && !IsLoaded(preset) && _door is not null && _control?.LoadedScenarioName is { Length: > 0 } scenario)
        {
            _previewWhenEdit = true;
            _door.Open(scenario, argsJson: IsDefault(preset) ? null : preset.ArgsJson(Fdp.Toolkit.Scenario.ScenarioPresets.RandomVariant));
            return;
        }
        _transport.EnterDryRun(startPaused);
    }

    /// <summary>
    /// ⭐ W-4 (LC-10 <i>Run to …</i> from Edit) — the dry run, PAUSED at its first frame, whatever is armed (Run to is a Preview by definition,
    /// R-4166; ⛔ the armed mode is not changed, R1). False when a question is open, the gate is closed or a run is already on.
    /// </summary>
    public bool EnterPreviewPaused()
    {
        if (Busy || ClosedReason is not null || _transport.Mode != TransportMode.Edit) return false;
        _transport.EnterDryRun(true);
        return true;
    }

    /// <summary>
    /// ⭐ <c>CE-4097</c> part two (AQ108 §3 "Preview → AAR, or File › Open Recording", D20) — opens a recording into AAR (at its start, or at
    /// <paramref name="atSimSeconds"/>). ⭐ A move that LEAVES EDITING asks the cluster's check round first: unsaved edits → <i>Save / Discard / Cancel</i>,
    /// a never-saved scenario → <i>Discard / Cancel</i>. ⛔ Asked ONLY from editing (the master refuses a round elsewhere, and a run holds nothing of the scenario).
    /// Every host's File › Open Recording and ▶ in AAR armed end here.
    /// </summary>
    public void OpenRecording(Guid exerciseId, double? atSimSeconds = null, Action? then = null)
    {
        if (_door is null || exerciseId == Guid.Empty) return;
        _door.OpenRecording(exerciseId, atSimSeconds, then: then);   // ⭐ CE-4101 — the door asks (Stop & Load / Save-Discard-Cancel); its RecordingOpening hook only records LastExercise — ⛔ it arms nothing (R-1043: no open sends or sets an armed mode, the recording open too)
    }

    /// <summary>
    /// ⭐ <c>CE-4097</c> part two (AQ108 §3 "open another scenario", D20) — opens <paramref name="scenarioName"/> for EDITING; asks <i>Save / Discard / Cancel</i> first when the scenario
    /// being edited has unsaved changes (the cluster's check round). ⛔ It sends no armed mode (CE-1443). ⭐ An operator's LOAD is <see cref="ScenarioDoor.Load"/> (the armed mode decides); this verb names EDITING.
    /// ⭐⭐ <c>CE-1438</c> → <c>CE-4101</c> — "a run is running" (<i>Stop &amp; Load</i>) and the unsaved round are the session's ONE <see cref="ScenarioDoor"/>'s decision, ⛔ not this choice's: a picker, a recipe, a panel button
    /// and HTTP reach the same door without going through here.
    /// </summary>
    public void OpenScenario(string scenarioName)
    {
        if (_door is null || string.IsNullOrWhiteSpace(scenarioName)) return;
        _door.Open(scenarioName);
    }

    /// <summary>
    /// ⭐ <c>CE-4141</c> (AQ108 D25) — why <i>Open and go live</i> (the scenario picker's second button) cannot run now, or <see langword="null"/>: the run gate first, then a host with no scenario session
    /// (an Orchestrator-less viewer). Independent of the ARMED mode — the button names its own mode.
    /// </summary>
    public string? OpenAndGoLiveDisabledReason
        => ClosedReason ?? (_door is null ? "No scenario session on this host — Live cannot be started from here" : null);

    /// <summary>
    /// ⭐ <c>CE-4141</c> (AQ108 D25, D4) — opens <paramref name="scenarioName"/> straight into LIVE: the session's ONE guarded door asks what it asks (<i>Stop &amp; Load</i> over a run,
    /// <i>Save / Discard / Cancel</i> over unsaved edits — D4), then loads for live; the load lands paused and the clock resumes once Live is up, as ▶ does. ⛔ Sends no armed mode.
    /// Does nothing when <see cref="OpenAndGoLiveDisabledReason"/> says why not.
    /// </summary>
    public void OpenAndGoLive(string scenarioName, string? argsJson = null)
    {
        if (OpenAndGoLiveDisabledReason is not null || string.IsNullOrWhiteSpace(scenarioName)) return;
        _lastRunSetup[scenarioName] = argsJson;   // ⭐ AQ108 D24 — ▶ on this scenario later reuses it
        _door!.LoadLive(scenarioName, then: () => _resumeWhenLive = true, argsJson: argsJson);
    }

    private readonly Dictionary<string, string?> _lastRunSetup = new(StringComparer.Ordinal);

    /// <summary>⭐ CE-3392 (presets §5b) — the load dialog's <i>Open</i> with a preset: opens for EDITING with the preset's args through the ONE door (its questions first). An eye preset opens a world
    /// that cannot be saved (the save gate says so). Null args ⇒ the plain open.</summary>
    public void OpenForEdit(string scenarioName, string? argsJson)
    {
        if (string.IsNullOrWhiteSpace(scenarioName) || _door is null) return;
        _door.Open(scenarioName, argsJson: argsJson);
    }

    /// <summary>
    /// ⭐ AQ108 D24 — the run setup (the load's args: changed params and a chosen <c>$variant</c>) this scenario was last started with from this session, or <see langword="null"/> (the defaults). ▶ on an already-open scenario reuses it
    /// (<see cref="GoLive"/>); the picker's <i>Run setup</i> expander starts from it. Session memory only — not persisted.
    /// </summary>
    public string? LastRunSetupOf(string scenarioName) => _lastRunSetup.TryGetValue(scenarioName, out var args) ? args : null;

    // ── ⭐ AQ108 D21 — the timeline's menu: a mode change at the clicked moment, without asking ─────────────────────────

    /// <summary>Why "AAR from here" cannot be offered now (null = it can): only a live run, and only a RECORDED one, has a recording to open.</summary>
    public string? AarFromHereDisabledReason
        => _control is null ? "This host has no scenario session"
         : _transport.Mode != TransportMode.Live ? "Only a live run can end into an AAR"
         : !_transport.RecordsLiveRuns ? "this run was not recorded"
         : _transport.ActiveExercise == Guid.Empty ? "the run's exercise is not known yet"
         : null;

    /// <summary>
    /// ⭐ D21 <i>AAR from here</i> (Live): ends the live run and opens its recording at <paramref name="simSeconds"/> of the run's own clock. No confirmation — the
    /// user picked the moment. Does nothing when <see cref="AarFromHereDisabledReason"/> says why not.
    /// </summary>
    public void AarFromHere(double simSeconds)
    {
        if (AarFromHereDisabledReason is not null) return;
        var exercise = _transport.ActiveExercise;
        LastExercise = exercise;
        Arm(RunArmed.AAR);
        _control!.OpenRecording(exercise, Math.Max(0, simSeconds));
    }

    /// <summary>Why "Go live from here" cannot be offered now (null = it can): only an AAR can branch.</summary>
    public string? GoLiveFromHereDisabledReason
        => _transport.Mode != TransportMode.Replay ? "Only an AAR can go live from a moment" : null;

    /// <summary>
    /// ⭐ D21 <i>Go live from here</i> (AAR): <paramref name="seekToMoment"/> moves the replay to the clicked moment, then the built branch
    /// (<see cref="IRunTransport.BranchLive"/>, D18) goes out TWO frames later — the master reads the seek first and, leaving a replay, drains the seeks in
    /// flight before it branches (<c>ClusterMaster</c> CE-3329 F8), so the new run starts where the user clicked and not where the replay stood. No confirmation.
    /// </summary>
    public void GoLiveFromHere(Action seekToMoment)
    {
        if (GoLiveFromHereDisabledReason is not null) return;
        seekToMoment();
        Arm(RunArmed.Live);
        _branchInFrames = 2;
    }

    // ── ⭐ AQ108 D8 — Save in a run is a checkpoint ───────────────────────────────────────────────────────────────────

    /// <summary>Why a checkpoint cannot be taken now (null = it can): a live run (or its DAR) that is recorded has a recorder to take it with; an AAR has none.</summary>
    public string? CheckpointDisabledReason
        => _control is null ? "This host has no scenario session"
         : _transport.Mode == TransportMode.Replay ? "a recording cannot take a checkpoint — go live from a moment first"
         : _transport.Mode is TransportMode.Live or TransportMode.Review && !_transport.RecordsLiveRuns ? "this run was not recorded"
         : _transport.Mode is TransportMode.Live or TransportMode.Review ? null
         : "a checkpoint is taken of a live run";

    /// <summary>
    /// ⭐ A checkpoint of the live run (the timeline's <i>Take checkpoint here</i>, and every Save in a run), with the toast <i>Checkpoint taken</i>. In an AAR / an unrecorded
    /// run nothing is published (the nodes have no recorder to take it with — the master / <c>ReferenceCheckpointHandler</c> would refuse it) and the toast says why.
    /// </summary>
    public void TakeCheckpoint()
    {
        if (CheckpointDisabledReason is { } why) { Notify($"No checkpoint: {why}"); return; }
        _control!.TakeCheckpoint();
        Notify("Checkpoint taken");
    }

    /// <summary>The session's save intercept: in a Live / DAR / AAR run a Save is a checkpoint (the scenario is NEVER written); in a Preview it is refused with a note; elsewhere the save goes through.</summary>
    private bool TryCheckpointInsteadOfSave()
    {
        // ⭐ CE-4250 (ui half) — a Preview runs a COPY of the edit world (Stop restores the authored one): saving then would serialise the previewed world (spawned entities, a unit's target memory pointing at one — it killed the editor). Not saved, and it says why
        if (_transport.Mode == TransportMode.Preview) { Notify("Not saved: " + Hrot.UI.Common.Panels.RunNoteStatusBarSection.PreviewText); return true; }   // ⭐ L-1 (R-4166): the status bar's own reason; a checkpoint is Live only
        if (_transport.Mode is not (TransportMode.Live or TransportMode.Review or TransportMode.Replay)) return false;
        TakeCheckpoint();
        return true;
    }

    /// <summary>
    /// ⭐ CE-3405 (R1′) — the switch Edit → Live: the unsaved check through the ONE door (<i>Save / Discard / Cancel</i>), then Live loads WITHOUT the resume — it lands paused at 0:00, the briefing; ▶ then resumes. A scenario never saved keeps
    /// today's <i>Save as… and go live</i> (the door would only offer Discard). The armed mode is set once the load went out.
    /// </summary>
    private void SwitchToLive(IDoorAsker? asker = null, UnsavedAnswer? answered = null)
    {
        var scenario = _control!.LoadedScenarioName;
        if (string.IsNullOrWhiteSpace(scenario))
        {
            // ⭐ CE-3411 (backend's REQUEST) — a CALLER's asker (HTTP) never gets the Save-As dialog: refused with the reason, nothing left standing in this host's UI
            if (asker is not null) { asker.Ask(new DoorQuestion(DoorQuestionKind.Refused, "Not saved", NeverSavedLiveText, Array.Empty<RunModeConfirmation.Button>())); return; }
            Ask(NeverSavedLiveText, new RunModeConfirmation.Button("Save as… and go live", () =>
                SaveAsPrompt?.Invoke(name => { if (!string.IsNullOrWhiteSpace(name)) GoLive(name); })));
            return;
        }
        _door!.LoadLive(scenario, asker, answered, then: () => Arm(RunArmed.Live), argsJson: LiveArgsOf(scenario));
    }

    /// <summary>What a switch to Live says when the scenario has never been saved.</summary>
    public const string NeverSavedLiveText = "This scenario has never been saved. Live starts from a saved scenario.";

    /// <summary>The load args a Live of <paramref name="scenario"/> starts with: its armed Live preset (CE-3393), else the last run setup (AQ108 D24), else null (as authored).</summary>
    private string? LiveArgsOf(string scenario)
    {
        if (ArmedPresetOf(scenario, RunArmed.Live) is { } armed && !IsDefault(armed)) return armed.ArgsJson(Fdp.Toolkit.Scenario.ScenarioPresets.RandomVariant);
        return LastRunSetupOf(scenario);
    }

    /// <summary>Loads <paramref name="scenario"/> Live, paused at the start (the briefing) — no resume (⭐ CE-3405: ▶ is the one that runs).</summary>
    private void GoLive(string scenario)
    {
        Arm(RunArmed.Live);
        if (LiveArgsOf(scenario) is { } args) _control!.LoadForLive(scenario, args);
        else _control!.LoadForLive(scenario);
    }

    /// <summary>
    /// ⭐ CE-3405 (R1′) — the switch Edit → AAR: the last exercise (or the recording picker when none) opens at its start, through the door (its unsaved question). The armed mode is set once the open went out; with the picker it is
    /// set at once (harmless if the pick is cancelled: an AAR-armed load goes where the last load went).
    /// </summary>
    private void SwitchToAar(IDoorAsker? asker = null, UnsavedAnswer? answered = null)
    {
        if (LastExercise is { } exercise) { if (_door is not null && exercise != Guid.Empty) _door.OpenRecording(exercise, null, asker, answered, then: () => Arm(RunArmed.AAR)); }
        else { Arm(RunArmed.AAR); PickRecording?.Invoke(); }   // a cancelled pick leaves only the armed fact: a File ▸ Open then goes where the last load went (LoadTarget)
    }

    /// <summary>Why a switch from Edit / Idle to <paramref name="target"/> cannot be made (null = it can).</summary>
    private string? SwitchDisabledReason(RunArmed target)
    {
        if (target == RunArmed.Live && _control is null) return "No scenario session on this host — Live cannot be started from here";
        if (target == RunArmed.AAR && _door is null) return "No scenario session on this host — an AAR cannot be opened from here";
        if (target == RunArmed.AAR && LastExercise is not null && !_lastExerciseRecorded) return RunRecordingGates.NotRecordedReason;   // D14: the last run left nothing to review
        if (target == RunArmed.AAR && LastExercise is null && PickRecording is null) return "No exercise for an AAR yet — open a recording first";
        return null;
    }

    /// <summary>The switch out of Edit (the unsaved question first, always through the door) — also what a Preview's switch does once editing is back.</summary>
    private void SwitchFromEdit(RunArmed target, IDoorAsker? asker = null, UnsavedAnswer? answered = null)
    {
        if (target == RunArmed.Live) SwitchToLive(asker, answered);
        else if (target == RunArmed.AAR) SwitchToAar(asker, answered);
        else Arm(target);
    }

    // ── ⭐ CE-3393 (📄 docs/UX/DESIGN_Scenario_Presets.md §5c, D8, D11) — the split ▶: ▾ ARMS a preset (per scenario, per mode, remembered), ▶ plays it ──────────

    /// <summary>Where the armed presets are remembered (the host's: <see cref="ArmedPresets.Shared"/>; a bare choice keeps them in memory).</summary>
    public ArmedPresets ArmedPresets { get; set; } = ArmedPresets.Open(null);

    /// <summary>The presets of <paramref name="scenario"/>, Default first — read from the scenarios folder by construction (every host), re-read at most every 2 s; none on a host with no local folder.</summary>
    public Func<string, IReadOnlyList<Fdp.Toolkit.Scenario.ScenarioPreset>> PresetsOf { get; set; } = DefaultPresetsOf;

    private static readonly Dictionary<string, (DateTime ReadAt, IReadOnlyList<Fdp.Toolkit.Scenario.ScenarioPreset> Presets)> PresetCache = new(StringComparer.Ordinal);
    private static readonly IReadOnlyList<Fdp.Toolkit.Scenario.ScenarioPreset> DefaultOnly = Fdp.Toolkit.Scenario.ScenarioPresets.Of(null);

    private static IReadOnlyList<Fdp.Toolkit.Scenario.ScenarioPreset> DefaultPresetsOf(string scenario)
    {
        lock (PresetCache)
        {
            var now = DateTime.UtcNow;
            if (PresetCache.TryGetValue(scenario, out var hit) && (now - hit.ReadAt).TotalSeconds < 2) return hit.Presets;
            IReadOnlyList<Fdp.Toolkit.Scenario.ScenarioPreset> presets = DefaultOnly;
            try
            {
                var file = System.IO.Path.Combine(Fdp.Toolkit.Orchestration.ContentLayout.ResolveUnderScenariosRoot(Fdp.Toolkit.Orchestration.OrchestrationConstants.GetSharedScenariosRoot(), scenario),
                    Fdp.Toolkit.Orchestration.ContentLayout.ScenarioFileName);
                presets = Fdp.Toolkit.Scenario.ScenarioPresets.Of(Fdp.Toolkit.Scenario.ScenarioPresets.HeaderOf(file));
            }
            catch (Exception) { }   // no scenarios folder on this host ⇒ Default only
            PresetCache[scenario] = (now, presets);
            return presets;
        }
    }

    private static bool IsDefault(Fdp.Toolkit.Scenario.ScenarioPreset p) => string.Equals(p.Name, Fdp.Toolkit.Scenario.ScenarioPreset.DefaultName, StringComparison.OrdinalIgnoreCase);

    /// <summary>The presets of the LOADED scenario (Default only when none is loaded).</summary>
    public IReadOnlyList<Fdp.Toolkit.Scenario.ScenarioPreset> LoadedPresets
        => _control?.LoadedScenarioName is { Length: > 0 } s ? PresetsOf(s) : DefaultOnly;

    /// <summary>The preset armed for <paramref name="scenario"/> in <paramref name="mode"/> (an armed name the file no longer has ⇒ Default).</summary>
    public Fdp.Toolkit.Scenario.ScenarioPreset ArmedPresetOf(string scenario, RunArmed mode)
    {
        var name = ArmedPresets.Armed(scenario, mode);
        var all = PresetsOf(scenario);
        for (int i = 0; i < all.Count; i++) if (string.Equals(all[i].Name, name, StringComparison.OrdinalIgnoreCase)) return all[i];
        return all[0];
    }

    /// <summary>⭐ CE-3405 — the mode ▾ presets belong to: Live while in Live (the briefing reloads with it), else Preview (Edit: ▶ previews with it).</summary>
    private RunArmed PresetMode => _transport.Mode == TransportMode.Live ? RunArmed.Live : RunArmed.Preview;

    /// <summary>The preset ▶ will play now (the loaded scenario, the mode the ▾ belongs to — <see cref="PresetMode"/>); null with no scenario loaded.</summary>
    public Fdp.Toolkit.Scenario.ScenarioPreset? ArmedPreset
        => _control?.LoadedScenarioName is { Length: > 0 } s ? ArmedPresetOf(s, PresetMode) : null;

    /// <summary>▾ — arms <paramref name="preset"/> for the loaded scenario. ⛔ Never the mode. In Edit it is what ▶ (the Preview) reopens with; ⭐ CE-3405 in the Live briefing (paused at the start, nothing run) it RELOADS Live with the preset.</summary>
    public void ArmPreset(string preset)
    {
        if (_control?.LoadedScenarioName is not { Length: > 0 } s) return;
        ArmedPresets.Arm(s, PresetMode, preset);
        if (LiveBriefing && !Busy && ClosedReason is null) GoLive(s);   // nothing has run: a plain reload (the door's Stop & Load question is for a run with something to lose)
    }

    /// <summary>The host's <i>Edit presets…</i> (Details ▸ Scenario ▸ Parameters, CE-3391); null ⇒ the menu item is greyed.</summary>
    public Action? EditPresets { get; set; }

    /// <summary>True when the world holds <paramref name="preset"/> (its args match the resident load's).</summary>
    public bool IsLoaded(Fdp.Toolkit.Scenario.ScenarioPreset preset) => PresetText.IsLoaded(preset, _control?.LoadedArgsJson);

    // ── ■ (R2) ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ■ — the start of the SAME mode (R2′): Preview → the dry run's exit (back to Edit, restored); DAR → back to live; AAR → seek to 0. ⭐ CE-3413 (R2″, D30) — Live ends in two steps and NEVER leaves by itself:
    /// a running Live asks <i>End the exercise?</i> (Cancel = keep running) and the end runs, the world staying in Live (<see cref="LiveEnded"/>); there ■ is ⏏ and asks <i>Close the live session?</i>.
    /// A host with no ender (or one that cannot end now) keeps today's ■ (<see cref="StopLive"/>, to editing). ⛔ The briefing's ■ is greyed at the TOOLBAR (<see cref="StopDisabledReason"/>); the call itself (<c>POST /sim/stop</c>) still answers.
    /// </summary>
    public void Stop()
    {
        switch (_transport.Mode)
        {
            case TransportMode.Live when _control is not null:
                if (LiveEnded) { AskToClose(); break; }
                if (_transport.Ender is { CanEnd: true } ender)
                    Ask("End the exercise?", "End the exercise? The objectives are evaluated and the results shown; the world stays paused at the end for the debrief. Cancel keeps it running.",
                        new RunModeConfirmation.Button("End the exercise", () => ender.End()));
                    // ⚠ LC-17 words the second button "Keep running": the house confirmation's own last button (Cancel) IS that — a second do-nothing button would duplicate it
                else StopLive();
                break;
            case TransportMode.Replay:
                _transport.Seek(0);
                break;
            default:
                _transport.StopRaw();   // dry-run exit · review exit · (no session: Live / Idle as before)
                break;
        }
    }

    // ── ⭐ CE-3408 (R-5, AQ108 §0 D27) — the Save SLOT: ONE button whose glyph changes ────────────────────────────────────────────────

    /// <summary>In Live / DAR / AAR the Save slot is the amber FLAG — it takes a checkpoint (Ctrl+S does too, through the save intercept); in Edit and Preview it is the floppy.</summary>
    public bool SaveSlotTakesCheckpoint => _transport.Mode is TransportMode.Live or TransportMode.Review or TransportMode.Replay;

    /// <summary>
    /// Why the Save slot is greyed now (null = it can act): in a run the checkpoint's reason (<see cref="CheckpointDisabledReason"/> — an AAR: <i>a recording cannot take a checkpoint…</i>, an unrecorded run), else the scenario's —
    /// <see cref="IRunScenarioControl.SaveDisabledReason"/>, the ONE answer the indicator's pen and File › Save read (a Preview: <i>a preview runs a copy of the edit world</i>). 0 B per read.
    /// </summary>
    public string? SaveSlotDisabledReason => SaveSlotTakesCheckpoint ? CheckpointDisabledReason : _control?.SaveDisabledReason;

    /// <summary>⭐ CE-3406 (story ②) — the Edit menu's <i>Close</i>: back to the Start screen through the ONE door (unsaved edits ask first). False when this host's session has no door OUT.</summary>
    public bool CanCloseScenario => _door is { CanLeave: true } && ClosedReason is null;

    /// <summary>⭐ CE-3406 — <i>Close</i> (see <see cref="CanCloseScenario"/>).</summary>
    public void CloseScenario() { if (CanCloseScenario) _door!.Leave(LeaveReason.Close); }

    /// <summary>⭐ CE-3413 — ⏏ in Live · ended: <i>Close the live session?</i> The exercise is kept; every station returns to the Start screen.</summary>
    private void AskToClose()
    {
        var scenario = ScenarioOfRun();
        var recorded = _transport.RecordsLiveRuns;
        var kept = recorded ? $"This exercise of {scenario ?? "the scenario"} is kept ({MainToolbarTimeControlSection.FormatSpan(_transport.TotalTime)})." : "This run was not recorded.";
        Ask("Close the live session?", kept + " Every station returns to the Start screen.", new RunModeConfirmation.Button("Close the session", CloseSession));
    }

    /// <summary>⭐ CE-3413 — the Close step: leave to Idle on every node (the door's <see cref="LeaveReason.Close"/>; the master finalises the live recording), then <i>Keep this recording?</i> and the <i>Exercise recorded</i> note — moved here from ■.</summary>
    public void CloseSession()
    {
        if (_transport.Mode != TransportMode.Live) return;
        var exercise = _transport.ActiveExercise;
        var recorded = _transport.RecordsLiveRuns;
        var length = _transport.TotalTime;
        var scenario = ScenarioOfRun();
        if (exercise != Guid.Empty) { LastExercise = exercise; _lastExerciseRecorded = recorded; }
        void After() => AfterLiveEnded(exercise, recorded, length, scenario);
        if (_door is { CanLeave: true } door) door.Leave(LeaveReason.Close, then: After);
        else { _transport.StopRaw(); After(); }   // no door OUT on this host: the same Idle intent
    }

    /// <summary>⭐ CE-3413 — the exercise of a Live · ended world is run again: the same scenario loads into the briefing (a NEW exercise; the ended one stays recorded). Raw load — there is nothing of the ended run to lose.</summary>
    public void RunAgain()
    {
        if (!LiveEnded || _control is null || Busy || ClosedReason is not null) return;
        var scenario = ScenarioOfRun();
        if (string.IsNullOrWhiteSpace(scenario)) return;
        if (_transport.ActiveExercise != Guid.Empty) { LastExercise = _transport.ActiveExercise; _lastExerciseRecorded = _transport.RecordsLiveRuns; }
        GoLive(scenario);
    }

    /// <summary>⭐ CE-3413 — Live · ended → AAR: the full replay of THIS exercise, at its start (no question — the exercise is over).</summary>
    public void ReplayEndedExercise()
    {
        if (!LiveEnded || _control is null || Busy || ClosedReason is not null) return;
        var exercise = _transport.ActiveExercise;
        if (exercise == Guid.Empty || !_transport.RecordsLiveRuns) return;
        LastExercise = exercise;
        _lastExerciseRecorded = true;
        Arm(RunArmed.AAR);
        _control.OpenRecording(exercise);
    }

    /// <summary>⭐ CE-3413 — why the ended menu's AAR is greyed (null = it can open).</summary>
    public string? EndedAarDisabledReason => !_transport.RecordsLiveRuns ? "this run was not recorded" : _transport.ActiveExercise == Guid.Empty ? "the run's exercise is not known yet" : null;

    /// <summary>⭐ CE-3413 — the checkpoints of the live run the ended menu can restart from, by label (a host that can list them sets it; today none does — the live run's marks are not on the cluster view, only a replay session carries them).</summary>
    public Func<IReadOnlyList<string>>? CheckpointLabels { get; set; }

    /// <summary>⭐ CE-3413 — restores the checkpoint <paramref name="label"/> of the live run (the session's <c>RestoreCheckpoint</c>: live, paused at the checkpointed state); set by a host that has the session.</summary>
    public Action<string>? RestoreCheckpoint { get; set; }

    private void StopLive()
    {
        var branch = _transport.ActiveExerciseBranch;   // ⭐ D5: a run branched from a recording returns THERE, not to the scenario's start
        var scenario = ScenarioOfRun();
        if (branch is null && string.IsNullOrWhiteSpace(scenario)) return;   // nothing names where to return to: ⛔ not Idle either

        var exercise = _transport.ActiveExercise;
        var recorded = _transport.RecordsLiveRuns;
        var length = _transport.TotalTime;
        if (exercise != Guid.Empty) { LastExercise = exercise; _lastExerciseRecorded = recorded; }
        if (branch is { } b)
        {
            // R2, branch case: back to the parent's AAR at the branch point, Live stays armed. The parent opens at its start; once its session (the wall zero) is
            // known, Update seeks to the branch wall ticks (the inventory's unit — no sim twin).
            _pendingBranchSeekWall = b.BranchWallTicks;
            _control!.OpenRecording(b.Parent);
        }
        else
        {
            _control!.OpenForEdit(scenario!);          // R2: the RAW verb — sends no armed mode, the cluster's stays
        }
        AfterLiveEnded(exercise, recorded, length, scenario);
    }

    /// <summary>
    /// ⭐ CE-4100 (AQ108 D15) — with "ask to keep recordings" on, a RECORDED run's end then asks. Keep (the default, first button) does nothing more; Discard deletes the exercise;
    /// Cancel / Esc closes it like every confirmation (= keep). The toast follows. ⭐ CE-3413: after the CLOSE step of an ended Live (and, on a host with no ender, after ■).
    /// </summary>
    private void AfterLiveEnded(Guid exercise, bool recorded, double length, string? scenario)
    {
        if (AskToKeep() && recorded && exercise != Guid.Empty)
            Ask("Keep this recording?", $"Keep this recording? ({MainToolbarTimeControlSection.FormatSpan(length)}, {scenario ?? "scenario"})",
                new RunModeConfirmation.Button("Keep", null), new RunModeConfirmation.Button("Discard", () => _transport.DiscardRecording(exercise)));
        // §1a: the user's words are DAR / AAR — "Replay" is gone from the vocabulary (D17). A run that was not recorded offers NO AAR button (D14).
        if (recorded && exercise != Guid.Empty)
            Notify($"Exercise recorded ({MainToolbarTimeControlSection.FormatSpan(length)}) — open the AAR", offersReplay: true);
        else
            Notify("Live run ended");
    }

    /// <summary>
    /// ⭐ CE-4100 (AQ108 D15) seam — whether ■ in Live asks "Keep this recording?". Default <c>() =&gt; false</c>: nothing changes until the cluster's setting is wired
    /// (set by <c>ClusterTimeTransportAdapter.AttachRunChoice</c>, the one construction every host passes through).
    /// </summary>
    public Func<bool> AskToKeep { get; set; } = () => false;

    private bool _lastExerciseRecorded = true;   // whether LastExercise's run was recorded (D14): an unrecorded one is never offered for AAR

    /// <summary>Why DAR (a review inside the live run) cannot be offered now (null = it can): the run was not recorded (D14, <c>CE-4090</c>'s <c>RunNotRecorded</c>).</summary>
    public string? DarDisabledReason => _transport.Mode == TransportMode.Live && !_transport.RecordsLiveRuns ? RunRecordingGates.NotRecordedReason : null;

    /// <summary>The toast's AAR button (and the indicator's "AAR the last exercise"): arms AAR and opens <see cref="LastExercise"/> at its START (D6), asking first when that leaves unsaved edits.</summary>
    public void ReplayLastExercise()
    {
        if (_control is null || LastExercise is not { } exercise || !_lastExerciseRecorded) return;
        _ = Toast;          // drain first, so a queued note cannot outlive the click
        _current = null;
        OpenRecording(exercise);
    }

    private string? ScenarioOfRun() => _transport.ActiveExerciseScenario ?? _control?.LoadedScenarioName;

    // ── the indicator's menu (R1: the ONLY place Armed changes) ──────────────────────────

    /// <summary>
    /// ⭐ The moves of AQ108 §3 that START from the current state, each with its confirmation text (R3). Edit / Idle: the three modes, no
    /// confirmation. Preview running → Live / AAR, Live running → Preview / AAR, AAR → Live / Preview: each asks. A DAR (review) has none
    /// (its own menu — back to live — stays the section's).
    /// </summary>
    public IReadOnlyList<RunModeMove> Moves()
    {
        var mode = _transport.Mode;
        if (mode == TransportMode.Review) return Array.Empty<RunModeMove>();
        var closed = ClosedReason;   // ⭐ CE-1442 — every move is disabled with the gate's reason (Close is not a move)
        var moves = new List<RunModeMove>(3);
        foreach (var target in new[] { RunArmed.Preview, RunArmed.Live, RunArmed.AAR })
        {
            // ⭐ CE-3405 — "Preview" the MODE is the dry run (running); the move that gets you back to authoring is "Edit" (the armed-mode fact it sets is still Preview)
            var label = target == RunArmed.Preview && mode != TransportMode.Preview ? "Edit"
                      : target == RunArmed.AAR && mode is TransportMode.Edit or TransportMode.Idle && LastExercise is null ? "AAR — open a recording…"   // ⭐ CE-3406 (story ②): no exercise yet — the pick is the recording picker
                      : target.ToString();
            // ⭐ CE-4111 — the SHOWN mode (what the indicator draws) is checked AND enabled; choosing it does nothing (the menu just closes). ⛔ No greyed "where you are" item.
            //    (A CLOSED gate still disables every item, the shown one included — CE-1442: the host takes no move at all.)
            if (closed is not null) { moves.Add(new(target, label, null, false, closed, target == Shown)); continue; }
            if (target == Shown) { moves.Add(new(target, label, null, true, null, true)); continue; }
            var plan = Plan(target);
            moves.Add(new(target, label, plan.ConfirmText, plan.Enabled, plan.DisabledReason, false));
        }
        return moves;
    }

    /// <summary>
    /// ⭐ <c>CE-4111</c> / ⭐ CE-3405 (R1′) — THE one mode the indicator and its menu show: the mode the cluster IS in (Preview / Live / AAR); in Edit / Idle the authoring item (<see cref="RunArmed.Preview"/> stands for Edit — ▶ there is the
    /// Preview), ⛔ no longer the mode ▶ "will start". <see cref="Indicator"/> and <see cref="RunModeMove.IsShown"/> both read it — ⛔ no second read of the mode (the pen glyph is R-3's).
    /// </summary>
    public RunArmed Shown => ModeTarget(_transport.Mode) ?? RunArmed.Preview;

    private static RunArmed? ModeTarget(TransportMode mode) => mode switch
    {
        TransportMode.Preview => RunArmed.Preview,
        TransportMode.Live    => RunArmed.Live,
        TransportMode.Replay  => RunArmed.AAR,
        _                     => null,
    };

    /// <summary>
    /// ⭐ The ONLY way <see cref="Armed"/> changes (R1). Either arms <paramref name="target"/> at once (nothing running) or, where AQ108 §3 asks,
    /// sets <see cref="Pending"/> — the mode changes when its button is pressed. A move that cannot be made (<see cref="RunModeMove.Enabled"/> false) does nothing.
    /// </summary>
    public void Choose(RunArmed target) => Choose(target, null, null);

    /// <summary>
    /// ⭐ <c>CE-3411</c> (backend's REQUEST, for <c>POST /run-mode</c>) — <see cref="Choose(RunArmed)"/> with the CALLER's door asker and its answer to the unsaved question: the door calls a switch out of Edit makes
    /// (<c>LoadLive</c>, <c>OpenRecording</c>) take them instead of this host's own, so an HTTP caller never leaves a question standing in this host's UI. The confirmations this choice asks ITSELF stay on <see cref="Pending"/>.
    /// </summary>
    public void Choose(RunArmed target, IDoorAsker? asker, UnsavedAnswer? answered)
    {
        if (Pending is not null || ClosedReason is not null) return;
        var mode = _transport.Mode;
        if (mode == TransportMode.Review || ModeTarget(mode) == target) return;
        _callerAsker = asker; _callerAnswered = answered;
        MovePlan plan;
        try { plan = Plan(target); }
        finally { _callerAsker = null; _callerAnswered = null; }
        if (!plan.Enabled) return;
        if (plan.ConfirmText is null) { plan.Execute(); return; }
        if (plan.AltButton is not null) Ask(plan.ConfirmText, new RunModeConfirmation.Button(plan.ConfirmButton, plan.Execute), new RunModeConfirmation.Button(plan.AltButton, plan.AltExecute));
        else Ask(plan.ConfirmText, new RunModeConfirmation.Button(plan.ConfirmButton, plan.Execute));
    }

    private IDoorAsker? _callerAsker;          // ⭐ CE-3411 — set only while Choose builds its plan: captured by the plan's switch
    private UnsavedAnswer? _callerAnswered;
    private IDoorAsker? _switchAsker;          // ⭐ CE-3411 — the caller's asker for a switch deferred until a Preview has stopped
    private UnsavedAnswer? _switchAnswered;

    private readonly record struct MovePlan(string? ConfirmText, string ConfirmButton, bool Enabled, string? DisabledReason, Action Execute,
                                             string? AltButton = null, Action? AltExecute = null);

    /// <summary>
    /// ⭐ CE-4091 host seam (AQ108 §3 "AAR → Preview"): <c>(exerciseId, scenarioName)</c> → whether the SAVED scenario's hash differs from the one the recording ran
    /// (<c>RecordedScenarioStamp</c> in the recording's copy). <see langword="null"/> = this host cannot tell ⇒ the plain confirmation. ⚠ The inventory carries no hash and the copy
    /// lives in the master's staging, so no host supplies it yet.
    /// </summary>
    public Func<Guid, string, bool>? RecordedVersionDiffers { get; set; }

    private MovePlan Plan(RunArmed target)
    {
        switch (_transport.Mode)
        {
            case TransportMode.Preview:
                // §3: Preview running → Live / AAR — the dry run restores (back to Edit), then the switch is made from there (the unsaved question included)
                if (SwitchDisabledReason(target) is { } previewWhy) return Disabled(previewWhy);
                var previewAsker = _callerAsker; var previewAnswered = _callerAnswered;
                return new("Stops the preview and returns to the scenario's start", $"Stop preview, go {target}", true, null, () =>
                {
                    _switchWhenEdit = target;
                    _switchAsker = previewAsker; _switchAnswered = previewAnswered;   // ⭐ CE-3411 — the caller's asker follows the deferred switch
                    _transport.StopRaw();
                });

            case TransportMode.Live:
                if (_control is null) return Disabled("This host has no scenario session");
                if (target == RunArmed.AAR)
                {
                    var exercise = _transport.ActiveExercise;
                    if (!_transport.RecordsLiveRuns) return Disabled("this run was not recorded");
                    if (exercise == Guid.Empty) return Disabled("the run's exercise is not known yet");
                    return new("Ends the live session; its recording opens for AAR", "End live, open AAR", true, null, () =>
                    {
                        LastExercise = exercise;
                        _lastExerciseRecorded = true;
                        Arm(RunArmed.AAR);
                        _control.OpenRecording(exercise);
                    });
                }
                // → Preview: the scenario at its START (§2.4), a branched run too
                var scenario = ScenarioOfRun();
                if (string.IsNullOrWhiteSpace(scenario)) return Disabled("the scenario of this run is not known");
                return new("Ends the live session; you return to editing the scenario at its start", "End live, edit scenario", true, null, () =>
                {
                    if (_transport.ActiveExercise != Guid.Empty) LastExercise = _transport.ActiveExercise;
                    Arm(RunArmed.Preview);
                    _control.OpenForEdit(scenario!);
                });

            case TransportMode.Replay:
                if (target == RunArmed.Live)
                {
                    // §3: AAR → Live from the indicator explains the branch (the timeline's menu would branch at once — a later part)
                    var at = MainToolbarTimeControlSection.FormatSpan(_transport.TotalTime);
                    return new($"A new exercise starts from {at}; this recording stays unchanged", "Go live from here", true, null, () =>
                    {
                        Arm(RunArmed.Live);
                        _transport.BranchLive();
                    });
                }
                if (_control is null) return Disabled("This host has no scenario session");
                var recorded = ScenarioOfRun();
                if (string.IsNullOrWhiteSpace(recorded)) return Disabled("the scenario of this recording is not known");
                var replayed = _transport.ActiveExercise;
                void OpenCurrent() { Arm(RunArmed.Preview); _control!.OpenForEdit(recorded!); }
                if (replayed != Guid.Empty && RecordedVersionDiffers?.Invoke(replayed, recorded!) == true)
                    // ⭐ CE-4091: the saved scenario is not the one this recording ran — say so and offer both
                    return new($"Ends the AAR. The saved {recorded} has changed since this recording was made: edit the version the recording ran (as a copy), or the current scenario?",
                               "Open the recorded version (as a copy)", true, null, () =>
                    {
                        Arm(RunArmed.Preview);
                        _control!.OpenForEdit(RecordedScenarioNames.ForExercise(replayed));
                    }, "Open the current scenario", OpenCurrent);
                return new($"Ends the AAR; you return to editing {recorded} at its start", "End AAR, edit scenario", true, null, OpenCurrent);

            default:
                // ⭐ CE-3405 (R1′) — Edit / Idle: the mode SWITCHES at once; the unsaved question is the door's (R3: no confirmation of our own). Idle holds no scenario: only the armed fact (the next load goes into it).
                if (target == RunArmed.Preview) return new(null, "", true, null, () => Arm(target));
                if (SwitchDisabledReason(target) is { } why) return Disabled(why);
                if (_transport.Mode == TransportMode.Idle && target == RunArmed.Live) return new(null, "", true, null, () => Arm(target));
                { var asker = _callerAsker; var answered = _callerAnswered; return new(null, "", true, null, () => SwitchFromEdit(target, asker, answered)); }
        }

        static MovePlan Disabled(string reason) => new(null, "", false, reason, () => { });
    }

    private void Ask(string text, params RunModeConfirmation.Button[] buttons) => Ask("Confirm", text, buttons);

    private void Ask(string title, string text, params RunModeConfirmation.Button[] buttons) => _asker.Show(title, text, buttons);

    // ── D12 + the cluster's state ───────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ Called by the adapter every frame with the cluster state it holds. ⭐ CE-3341 (AQ108 §2.14, D12a): the per-host D12 reset that lived here is GONE — the armed mode is the cluster's one fact (⭐ CE-1443: no open sends it
    /// either; the master's bootstrap sets the initial one). Resumes the clock once a ▶-started live load is up.
    /// </summary>
    public void OnClusterState(ClusterState state)
    {
        var previous = _lastState;
        if (state == previous) return;
        _lastState = state;

        if (state == ClusterState.OperatingLive && _resumeWhenLive)
        {
            _resumeWhenLive = false;
            _transport.Resume();
        }
        else if (state == ClusterState.OperatingEdit && _previewWhenEdit)
        {
            _previewWhenEdit = false;   // ⭐ CE-3393 — the reopen with the armed preset landed: the dry run it was asked for
            _transport.EnterDryRun(false);
        }
        else if (state == ClusterState.OperatingEdit && _switchWhenEdit is { } target)
        {
            _switchWhenEdit = null;     // ⭐ CE-3405 — the preview stopped and editing is back: the switch the user chose
            SwitchFromEdit(target, _switchAsker, _switchAnswered);
            _switchAsker = null; _switchAnswered = null;
        }
        else if (state == ClusterState.OperatingReplay || state == ClusterState.Idle && previous == ClusterState.OperatingLive)
        {
            _resumeWhenLive = false;
        }
        if (state is ClusterState.OperatingLive or ClusterState.OperatingReplay) { _previewWhenEdit = false; _switchWhenEdit = null; }
    }

    // ── the indicator ───────────────────────────────────────────────────────────────────

    // ONE colour table: the toolbar's (§9a — Preview violet, Review amber, Replay blue; Live's green is the not-recording one); only the recording RED is new
    private static Vector4 ColorOf(TransportMode mode) => MainToolbarTimeControlSection.ModeGlyph(mode)!.Color;
    private static readonly Vector4 RecordingRed = new(0.90f, 0.22f, 0.22f, 1f);

    private static readonly List<RunModeIndicator> Interned = new();

    /// <summary>⭐ CE-3406 — the indicators are a handful of values: each is built once and handed out again, so reading <see cref="Indicator"/> every frame allocates nothing.</summary>
    private static RunModeIndicator Intern(string glyph, Vector4 color, string word, IndicatorLevel level)
    {
        lock (Interned)
        {
            foreach (var i in Interned)
                if (i.Level == level && i.Color == color && ReferenceEquals(i.Glyph, glyph) && ReferenceEquals(i.Word, word)) return i;
            var made = new RunModeIndicator(glyph, color, word, level);
            Interned.Add(made);
            return made;
        }
    }

    /// <summary>
    /// ⭐ What the toolbar draws (AQ108 §0 D1′, CE-3406) — <b>the mode the cluster IS in</b>, no dim "armed" level. Edit: the yellow PEN on a world that can be saved, the violet EYE (steady) on one that cannot — a variant world, an error
    /// (<see cref="IRunScenarioControl.SaveDisabledReason"/>, the ONE answer the Save slot and File › Save read too). Preview: violet eye, blinking while it runs. Live: ● RED when recorded / GREEN when not (CE-4090), steady paused, blinking running;
    /// ⭐ Live · ended: the checkered FLAG in Live's colour. DAR amber ⟲. AAR blue ▶. The words are the user's: Edit / Preview / Live / DAR / AAR. Interned: 0 B per read.
    /// </summary>
    public RunModeIndicator Indicator
    {
        get
        {
            var mode = _transport.Mode;
            var level = _transport.IsPaused ? IndicatorLevel.Bright : IndicatorLevel.Blink;
            switch (mode)
            {
                case TransportMode.Review:  return Intern(IconsFontAwesome6.ClockRotateLeft, ColorOf(TransportMode.Review), "DAR", level);
                case TransportMode.Preview: return Intern(IconsFontAwesome6.Eye, ColorOf(TransportMode.Preview), "Preview", level);
                case TransportMode.Replay:  return Intern(IconsFontAwesome6.Play, ColorOf(TransportMode.Replay), "AAR", level);
                case TransportMode.Live:
                    var live = _transport.RecordsLiveRuns ? RecordingRed : ColorOf(TransportMode.Live);
                    return LiveEnded ? Intern(IconsFontAwesome6.FlagCheckered, live, "Live", IndicatorLevel.Bright) : Intern(IconsFontAwesome6.Circle, live, "Live", level);
                default:   // Edit / Idle — the pen where the world can be saved, the eye where it cannot
                    return mode == TransportMode.Edit && _control?.SaveDisabledReason is not null
                        ? Intern(IconsFontAwesome6.Eye, ColorOf(TransportMode.Preview), "Edit", IndicatorLevel.Bright)
                        : Intern(IconsFontAwesome6.Pen, ColorOf(TransportMode.Edit), "Edit", IndicatorLevel.Bright);
            }
        }
    }

    /// <summary>
    /// ⭐ CE-3406 — the indicator's tooltip (ind.png's third column). Built on hover only (allocates then, not per frame). <paramref name="behindLive"/>: a DAR's distance behind live, when known.
    /// </summary>
    public string IndicatorTooltip(double? behindLive = null)
    {
        var scenario = ScenarioOfRun() ?? _control?.LoadedScenarioName ?? "the scenario";
        var recorded = _transport.RecordsLiveRuns ? "recorded" : "not recorded";
        switch (_transport.Mode)
        {
            case TransportMode.Review:
                return behindLive is { } b ? $"DAR — {MainToolbarTimeControlSection.FormatSpan(b)} behind live. ⏏ goes back to live" : "DAR — looking back inside the live run. ⏏ goes back to live";
            case TransportMode.Preview:
                return "Preview — a copy of the edit world: nothing is kept. ■ goes back to Edit";
            case TransportMode.Replay:
                return $"AAR — {scenario}. Click: Run again from here · Edit · Close";
            case TransportMode.Live:
                if (LiveEnded) return "Exercise ended — results ready. Click: Run again · checkpoint · DAR · AAR · Close";
                return LiveBriefing ? $"Live — {recorded}. ▶ starts the exercise. Click: switch mode"
                     : _transport.IsPaused ? $"Live — {recorded}, paused. Click: switch mode"
                     : $"Live — {recorded}, running. Click: switch mode";
            default:
                return _control?.SaveDisabledReason is { } why && _transport.Mode == TransportMode.Edit
                    ? $"Editing a variant (preview only) — not saveable: {why}"
                    : $"Editing — your changes save to {scenario}. ▶ previews them. Click: switch mode";
        }
    }
}

/// <summary>
/// ⭐⭐ <c>CE-4097</c> part two (AQ108 D22) — <b>what a USER control's ▶ / Step do</b>: from Edit they are the host's <see cref="RunModeChoice.Play"/> (the armed mode),
/// everywhere else the facade's own transport. ⭐ ONE place, shared by the main toolbar, the status-bar copy and the timeline's Space — so no control can follow the armed
/// mode differently. ⛔ The facade's own <c>TogglePlayPause</c> / <c>Step</c> stay RAW (from Edit: the dry run) because <c>POST /sim/play</c> and <c>/sim/step</c> call them and
/// a route does what it names; <c>Stop</c> needs no helper — it delegates to the choice inside the facade (■ = <c>POST /sim/stop</c>).
/// </summary>
public static class UserTransport
{
    private static RunModeChoice? EditChoice(ITimeTransportFacade f, RunModeChoice? choice) => f.Mode == TransportMode.Edit ? choice ?? f.RunChoice : null;

    /// <summary>▶ is enabled: the facade says so, and the choice does not say otherwise (the gate; ⭐ CE-3413 a Live · ended is over — ▶ is greyed).</summary>
    public static bool IsUserPlayEnabled(this ITimeTransportFacade f, RunModeChoice? choice = null) => f.IsPlayPauseEnabled && ReasonOf(f, choice) is null;

    // from Edit the choice's whole answer (a host with no session has nothing to preview); in every other mode the reasons that hold anywhere (the gate, a Live · ended)
    private static string? ReasonOf(ITimeTransportFacade f, RunModeChoice? choice) => f.Mode == TransportMode.Edit ? (choice ?? f.RunChoice)?.PlayDisabledReason : (choice ?? f.RunChoice)?.RunPlayDisabledReason;

    /// <summary>Step is enabled (same rule as <see cref="IsUserPlayEnabled"/>).</summary>
    public static bool IsUserStepEnabled(this ITimeTransportFacade f, RunModeChoice? choice = null) => f.IsStepEnabled && ReasonOf(f, choice) is null;

    /// <summary>A user's ▶ / ⏸: from Edit the choice's ▶ (⭐ CE-3405: always the Preview), else the facade's toggle (a paused Live resumes). Does nothing when <see cref="IsUserPlayEnabled"/> is false.</summary>
    public static void UserPlayPause(this ITimeTransportFacade f, RunModeChoice? choice = null)
    {
        if (!f.IsUserPlayEnabled(choice)) return;
        if (EditChoice(f, choice) is { } c) c.Play(startPaused: false);
        else f.TogglePlayPause();
    }

    /// <summary>A user's Step: from Edit the Preview, paused at its first frame; else the facade's step.</summary>
    public static void UserStep(this ITimeTransportFacade f, RunModeChoice? choice = null)
    {
        if (!f.IsUserStepEnabled(choice)) return;
        if (EditChoice(f, choice) is { } c) c.Play(startPaused: true);
        else f.Step();
    }

    /// <summary>⭐ <c>CE-1439</c> — <see cref="UserStep"/> with a step SIZE (the Orchestrator panel's <i>Step (s)</i> box): from Edit the armed mode's start, else the facade's <c>StepBy</c>.</summary>
    public static void UserStepBy(this ITimeTransportFacade f, float seconds, RunModeChoice? choice = null)
    {
        if (!f.IsUserStepEnabled(choice)) return;
        if (EditChoice(f, choice) is { } c) c.Play(startPaused: true);
        else f.StepBy(seconds);
    }
}
