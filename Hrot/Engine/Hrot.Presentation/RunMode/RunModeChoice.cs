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
/// <item><b>R1</b> — only <see cref="Choose"/> (the indicator) changes <see cref="Armed"/>; ▶ ⏸ ■ and every load never do (R-1043).</item>
/// <item><b>R2</b> — <see cref="Stop"/> returns to where the run came from and the armed mode stays: Preview → the dry run's exit; DAR → back to live;
/// Live → <c>OpenForEdit(scenario of the active exercise)</c> (the cluster is NEVER sent to plain Idle); AAR → seek to 0, still AAR.</item>
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
    /// <summary>⭐ CE-4100 ① — set the cluster's run settings; a null argument leaves that one as it is.</summary>
    public void SendRunSettings(bool? recordLiveRuns, bool? askToKeepRecordings) => _transport.SendRunSettings(recordLiveRuns, askToKeepRecordings);

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
    }

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

    /// <summary>Why ▶ (from Edit) is disabled, or <see langword="null"/> when it can run (AQ108 §2.8 ⑥: Live armed on a host with no scenario session).</summary>
    public string? PlayDisabledReason
    {
        get
        {
            if (ClosedReason is { } closed) return closed;   // ⭐ CE-1442 — the gate first
            if (Armed == RunArmed.Live && _control is null) return "No scenario session on this host — Live cannot be started from here";
            if (Armed == RunArmed.AAR && LastExercise is not null && !_lastExerciseRecorded) return RunRecordingGates.NotRecordedReason;   // D14: the last run left nothing to review
            if (Armed == RunArmed.AAR && LastExercise is null && PickRecording is null) return "No exercise for an AAR yet — open a recording first";
            return null;
        }
    }

    /// <summary>
    /// ▶ (or Step) from Edit — what the ARMED mode does: Preview → the dry run (today's); Live → the unsaved check, then <c>LoadForLive</c>;
    /// AAR → <c>OpenRecording(LastExercise)</c> at its start. ⛔ Never changes <see cref="Armed"/> (R1).
    /// </summary>
    /// <param name="startPaused">Step from Edit: the dry run paused at its first frame (Live and AAR load paused anyway).</param>
    public void Play(bool startPaused = false)
    {
        if (Busy) return;   // a question is open: ▶ does not stack another
        if (PlayDisabledReason is not null) return;
        switch (Armed)
        {
            case RunArmed.Preview:
                _transport.EnterDryRun(startPaused);
                break;
            case RunArmed.Live:
                PlayLive();
                break;
            case RunArmed.AAR:
                if (LastExercise is { } exercise) OpenRecording(exercise);   // PlayDisabledReason guarantees a control or a picker
                else PickRecording?.Invoke();
                break;
        }
    }

    /// <summary>
    /// ⭐ <c>CE-4097</c> part two (AQ108 §3 "Preview → AAR, or File › Open Recording", D20) — opens a recording into AAR (at its start, or at
    /// <paramref name="atSimSeconds"/>). ⭐ A move that LEAVES EDITING asks the cluster's check round first: unsaved edits → <i>Save / Discard / Cancel</i>,
    /// a never-saved scenario → <i>Discard / Cancel</i>. ⛔ Asked ONLY from editing (the master refuses a round elsewhere, and a run holds nothing of the scenario).
    /// Every host's File › Open Recording and ▶ in AAR armed end here.
    /// </summary>
    public void OpenRecording(Guid exerciseId, double? atSimSeconds = null)
    {
        if (_door is null || exerciseId == Guid.Empty) return;
        _door.OpenRecording(exerciseId, atSimSeconds);   // ⭐ CE-4101 — the door asks (Stop & Load / Save-Discard-Cancel); its RecordingOpening hook arms AAR when the open goes out
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

    /// <summary>The session's save intercept: in a Live / DAR / AAR run a Save is a checkpoint (the scenario is NEVER written); elsewhere the save goes through.</summary>
    private bool TryCheckpointInsteadOfSave()
    {
        if (_transport.Mode is not (TransportMode.Live or TransportMode.Review or TransportMode.Replay)) return false;
        TakeCheckpoint();
        return true;
    }

    private void PlayLive()
    {
        _asking = true;
        _unsaved.Ask(answer =>
        {
            _asking = false;
            switch (answer)
            {
                case UnsavedAnswer.Unchanged:
                    if (!string.IsNullOrWhiteSpace(_control!.LoadedScenarioName)) GoLive(_control.LoadedScenarioName!);
                    break;
                case UnsavedAnswer.Changed:
                    Ask("This scenario has unsaved changes. Live starts from the saved scenario.", new RunModeConfirmation.Button("Save and go live", () =>
                    {
                        _control!.SaveCurrent();
                        if (!string.IsNullOrWhiteSpace(_control.LoadedScenarioName)) GoLive(_control.LoadedScenarioName!);
                    }));
                    break;
                case UnsavedAnswer.NeverSaved:
                    Ask("This scenario has never been saved. Live starts from a saved scenario.", new RunModeConfirmation.Button("Save as… and go live", () =>
                        SaveAsPrompt?.Invoke(name => { if (!string.IsNullOrWhiteSpace(name)) GoLive(name); })));
                    break;
            }
        });
    }

    private void GoLive(string scenario)
    {
        _resumeWhenLive = true;
        _control!.LoadForLive(scenario);
    }

    // ── ■ (R2) ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ■ per R2 — back to where this run came from, and the armed mode stays. Preview → the dry run's exit; DAR → back to live (both TODAY's
    /// behaviour, through the raw transport); Live → <c>OpenForEdit(scenario of the active exercise)</c>, <see cref="LastExercise"/> = the run, a
    /// toast, and ⛔ never plain Idle; AAR → seek to 0, still AAR. A host with no scenario session keeps today's ■ in Live (to Idle).
    /// </summary>
    public void Stop()
    {
        switch (_transport.Mode)
        {
            case TransportMode.Live when _control is not null:
                StopLive();
                break;
            case TransportMode.Replay:
                _transport.Seek(0);
                break;
            default:
                _transport.StopRaw();   // dry-run exit · review exit · (no session: Live / Idle as before)
                break;
        }
    }

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
        // ⭐ CE-4100 (AQ108 D15) — with "ask to keep recordings" on, a RECORDED run's ■ then asks. Keep (the default, first button) does nothing more; Discard deletes the exercise;
        //   Cancel / Esc closes it like every confirmation (= keep). The toast below still follows.
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
        var current = ModeTarget(mode);
        var closed = ClosedReason;   // ⭐ CE-1442 — every move is disabled with the gate's reason (Close is not a move)
        var moves = new List<RunModeMove>(3);
        foreach (var target in new[] { RunArmed.Preview, RunArmed.Live, RunArmed.AAR })
        {
            var label = target.ToString();
            if (closed is not null) { moves.Add(new(target, label, null, false, closed, Armed == target)); continue; }
            if (target == current) { moves.Add(new(target, label, null, false, null, Armed == target)); continue; }
            var plan = Plan(target);
            moves.Add(new(target, label, plan.ConfirmText, plan.Enabled, plan.DisabledReason, Armed == target));
        }
        return moves;
    }

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
    public void Choose(RunArmed target)
    {
        if (Pending is not null || ClosedReason is not null) return;
        var mode = _transport.Mode;
        if (mode == TransportMode.Review || ModeTarget(mode) == target) return;
        var plan = Plan(target);
        if (!plan.Enabled) return;
        if (plan.ConfirmText is null) { plan.Execute(); return; }
        if (plan.AltButton is not null) Ask(plan.ConfirmText, new RunModeConfirmation.Button(plan.ConfirmButton, plan.Execute), new RunModeConfirmation.Button(plan.AltButton, plan.AltExecute));
        else Ask(plan.ConfirmText, new RunModeConfirmation.Button(plan.ConfirmButton, plan.Execute));
    }

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
                // §3: Preview running → Live / AAR — the dry run restores, then the chosen mode is armed
                return new("Stops the preview and returns to the scenario's start", $"Stop preview, arm {target}", true, null, () =>
                {
                    _transport.StopRaw();
                    Arm(target);
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
                // Edit / Idle — nothing running: arming loses nothing (R3: no confirmation)
                return new(null, "", true, null, () => Arm(target));
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
        else if (state == ClusterState.OperatingReplay || state == ClusterState.Idle && previous == ClusterState.OperatingLive)
        {
            _resumeWhenLive = false;
        }
    }

    // ── the indicator ───────────────────────────────────────────────────────────────────

    // ONE colour table: the toolbar's (§9a — Preview violet, Review amber, Replay blue; Live's green is the not-recording one); only the recording RED is new
    private static Vector4 ColorOf(TransportMode mode) => MainToolbarTimeControlSection.ModeGlyph(mode)!.Color;
    private static readonly Vector4 RecordingRed = new(0.90f, 0.22f, 0.22f, 1f);

    /// <summary>
    /// ⭐ What the toolbar draws (AQ108 D1, §1a): DIM in Edit (the armed mode, what ▶ will start), BRIGHT steady while a run is paused, BLINKING while it
    /// plays. Live is RED when the broadcast says runs are recorded and GREEN when not (CE-4090). The words are the user's: Preview / Live / DAR / AAR.
    /// </summary>
    public RunModeIndicator Indicator
    {
        get
        {
            var mode = _transport.Mode;
            var level = mode is TransportMode.Edit or TransportMode.Idle
                ? IndicatorLevel.Dim
                : _transport.IsPaused ? IndicatorLevel.Bright : IndicatorLevel.Blink;
            return mode switch
            {
                TransportMode.Preview => Of(RunArmed.Preview, level),
                TransportMode.Live    => Of(RunArmed.Live, level),
                TransportMode.Review  => new(IconsFontAwesome6.ClockRotateLeft, ColorOf(TransportMode.Review), "DAR", level),
                TransportMode.Replay  => Of(RunArmed.AAR, level),
                _                     => Of(Armed, level),
            };
        }
    }

    private RunModeIndicator Of(RunArmed mode, IndicatorLevel level) => mode switch
    {
        RunArmed.Preview => new(IconsFontAwesome6.CircleHalfStroke, ColorOf(TransportMode.Preview), "Preview", level),
        RunArmed.Live    => new(IconsFontAwesome6.Circle, _transport.RecordsLiveRuns ? RecordingRed : ColorOf(TransportMode.Live), "Live", level),
        _                => new(IconsFontAwesome6.Play, ColorOf(TransportMode.Replay), "AAR", level),
    };
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

    /// <summary>▶ is enabled: the facade says so, and — from Edit — the armed mode can run here (Live armed on a host with no scenario session cannot).</summary>
    public static bool IsUserPlayEnabled(this ITimeTransportFacade f, RunModeChoice? choice = null) => f.IsPlayPauseEnabled && (choice ?? f.RunChoice)?.ClosedReason is null && EditChoice(f, choice)?.PlayDisabledReason is null;

    /// <summary>Step is enabled (same rule as <see cref="IsUserPlayEnabled"/>).</summary>
    public static bool IsUserStepEnabled(this ITimeTransportFacade f, RunModeChoice? choice = null) => f.IsStepEnabled && (choice ?? f.RunChoice)?.ClosedReason is null && EditChoice(f, choice)?.PlayDisabledReason is null;

    /// <summary>A user's ▶ / ⏸: from Edit the armed mode's start, else the facade's toggle. Does nothing when <see cref="IsUserPlayEnabled"/> is false.</summary>
    public static void UserPlayPause(this ITimeTransportFacade f, RunModeChoice? choice = null)
    {
        if (!f.IsUserPlayEnabled(choice)) return;
        if (EditChoice(f, choice) is { } c) c.Play(startPaused: false);
        else f.TogglePlayPause();
    }

    /// <summary>A user's Step: from Edit the armed mode's start, paused at its first frame; else the facade's step.</summary>
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
