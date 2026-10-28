using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Presentation.Fonts;
using Fdp.Toolkit.Orchestration;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.Panels;

namespace Hrot.UI.Common.RunMode;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-4097</c> (AQ108 §2.8) — the run-mode choice: ONE object per host that holds what ▶ will run and owns rules R1–R3.</b>
/// 📄 <c>docs/blueprints/Architect_Question_108_Run_Mode_Picker.md</c> §1 (the three rules), §2.8 (classes + sequences), §3 (every move), D1 D5 D10 D12 D14 D22.
///
/// <list type="bullet">
/// <item><b>R1</b> — only <see cref="Choose"/> changes <see cref="Armed"/>; ▶ ⏸ ■ never do.</item>
/// <item><b>R2</b> — <see cref="Stop"/> returns to where the run came from and the armed mode stays: Preview → the dry run's exit; DAR → back to live;
/// Live → <c>OpenForEdit(scenario of the active exercise)</c> (the cluster is NEVER sent to plain Idle); AAR → seek to 0, still AAR.</item>
/// <item><b>R3</b> — a confirmation (<see cref="Pending"/>) only where a jump or a loss needs explaining; ■ never asks.</item>
/// </list>
///
/// ⛔ It re-implements NO scenario action: it calls <see cref="IRunScenarioControl"/> (the session's own) and the raw transport
/// (<see cref="IRunTransport"/>, the one facade's today's behaviours). The facade's <c>Stop</c> / <c>TogglePlayPause</c> / <c>StepBy</c> delegate
/// here, so ■ in the toolbar and <c>POST /sim/stop</c> stay ONE call (D22).
/// </summary>
public sealed class RunModeChoice
{
    private readonly IRunTransport _transport;
    private readonly IRunScenarioControl? _control;
    private readonly IUnsavedChangesQuery _unsaved;
    private readonly Func<DateTime> _now;

    private ClusterState _lastState = ClusterState.Idle;
    private bool _keepArmedOnEdit;     // set by OUR OWN return-to-editing moves (■ in Live, → Preview): the Edit that follows is not a fresh open
    private bool _resumeWhenLive;      // ▶ in Live armed: the load lands paused (Q86 §4-G); ▶ means run, so the clock resumes once Live is up
    private bool _asking;

    /// <summary>How long a <see cref="Toast"/> shows.</summary>
    public static readonly TimeSpan ToastLifetime = TimeSpan.FromSeconds(8);

    /// <param name="transport">The one facade's raw transport.</param>
    /// <param name="hostDefault">⭐ The HOST's armed mode (AQ108 D12): Preview in the editor, Live on CGF / the all-in-one / the server.</param>
    /// <param name="control">The host's scenario session; <see langword="null"/> on a host with none (SimHost, IG) — Live-armed ▶ is then disabled with a reason.</param>
    /// <param name="unsaved">The unsaved-changes seam; default <see cref="ScenarioNameUnsavedChangesQuery"/> over <paramref name="control"/> (⚠ the CE-4093 TODO lives there).</param>
    /// <param name="now">Clock of the toast (tests inject one).</param>
    public RunModeChoice(IRunTransport transport, RunArmed hostDefault, IRunScenarioControl? control,
                         IUnsavedChangesQuery? unsaved = null, Func<DateTime>? now = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _control   = control;
        _unsaved   = unsaved ?? new ScenarioNameUnsavedChangesQuery(() => control?.LoadedScenarioName);
        _now       = now ?? (() => DateTime.UtcNow);
        HostDefault = hostDefault;
        Armed       = hostDefault;
    }

    /// <summary>What ▶ runs from Edit (R1: only <see cref="Choose"/> changes it; D12: reset to <see cref="HostDefault"/> when a scenario is opened).</summary>
    public RunArmed Armed { get; private set; }

    /// <summary>⭐ The host's default (AQ108 D12) — what <see cref="Armed"/> returns to whenever a scenario is opened for editing.</summary>
    public RunArmed HostDefault { get; }

    /// <summary>The exercise of the last live run this choice ended (■ or → AAR) — what AAR armed opens at its start; <see langword="null"/> before any.</summary>
    public Guid? LastExercise { get; private set; }

    /// <summary>The confirmation waiting for a button (R3); <see langword="null"/> when none. The toolbar draws it as a modal.</summary>
    public RunModeConfirmation? Pending { get; private set; }

    /// <summary>
    /// ⭐ Host seam for "Save as… and go live": asks the user for a scenario NAME, SAVES the scenario under it, then calls back with the name.
    /// ⚠ Contract: the save has happened when the callback runs (the host's Save-As dialog saves; this choice then loads that name live).
    /// <see langword="null"/> = this host has no Save-As dialog (the button then does nothing).
    /// </summary>
    public Action<Action<string>>? SaveAsPrompt { get; set; }

    /// <summary>⭐ Host seam: opens the shared recording picker — what ▶ does in AAR armed when no exercise has run yet. <see langword="null"/> = none.</summary>
    public Action? PickRecording { get; set; }

    /// <summary>The note after ■ in Live (AQ108 §3); <see langword="null"/> when none or expired.</summary>
    public RunModeToast? Toast => _toast is { } t && _now() < t.ExpiresAt ? t : null;
    private RunModeToast? _toast;

    // ── ▶ ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Why ▶ (from Edit) is disabled, or <see langword="null"/> when it can run (AQ108 §2.8 ⑥: Live armed on a host with no scenario session).</summary>
    public string? PlayDisabledReason
    {
        get
        {
            if (Armed == RunArmed.Live && _control is null) return "No scenario session on this host — Live cannot be started from here";
            if (Armed == RunArmed.AAR && LastExercise is null && PickRecording is null) return "No exercise to review yet — open a recording first";
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
        if (Pending is not null || _asking) return;   // a question is open: ▶ does not stack another
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
                if (LastExercise is { } exercise) _control!.OpenRecording(exercise);   // PlayDisabledReason guarantees a control or a picker
                else PickRecording?.Invoke();
                break;
        }
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
        var scenario = ScenarioOfRun();
        if (string.IsNullOrWhiteSpace(scenario)) return;   // nothing names the scenario to return to: ⛔ not Idle either

        var exercise = _transport.ActiveExercise;
        var recorded = _transport.RecordsLiveRuns;
        var length = _transport.TotalTime;
        if (exercise != Guid.Empty) LastExercise = exercise;
        _keepArmedOnEdit = true;                       // R2: the armed mode stays (D12's reset is for a FRESH open)
        _control!.OpenForEdit(scenario!);
        _toast = recorded && exercise != Guid.Empty
            ? new($"Exercise recorded ({MainToolbarTimeControlSection.FormatSpan(length)}) — Replay it", exercise, _now() + ToastLifetime)
            : new("Live run ended", null, _now() + ToastLifetime);
    }

    /// <summary>The toast's "Replay it" (and the indicator's "AAR the last exercise"): arms AAR and opens <see cref="LastExercise"/> at its START.</summary>
    public void ReplayLastExercise()
    {
        if (_control is null || LastExercise is not { } exercise) return;
        Armed = RunArmed.AAR;
        _toast = null;
        _control.OpenRecording(exercise);
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
        var moves = new List<RunModeMove>(3);
        foreach (var target in new[] { RunArmed.Preview, RunArmed.Live, RunArmed.AAR })
        {
            var label = target.ToString();
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
        if (Pending is not null) return;
        var mode = _transport.Mode;
        if (mode == TransportMode.Review || ModeTarget(mode) == target) return;
        var plan = Plan(target);
        if (!plan.Enabled) return;
        if (plan.ConfirmText is null) { plan.Execute(); return; }
        Ask(plan.ConfirmText, new RunModeConfirmation.Button(plan.ConfirmButton, plan.Execute));
    }

    private readonly record struct MovePlan(string? ConfirmText, string ConfirmButton, bool Enabled, string? DisabledReason, Action Execute);

    private MovePlan Plan(RunArmed target)
    {
        switch (_transport.Mode)
        {
            case TransportMode.Preview:
                // §3: Preview running → Live / AAR — the dry run restores, then the chosen mode is armed
                return new("Stops the preview and returns to the scenario's start", $"Stop preview, arm {target}", true, null, () =>
                {
                    _transport.StopRaw();
                    Armed = target;
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
                        Armed = RunArmed.AAR;
                        _control.OpenRecording(exercise);
                    });
                }
                // → Preview: the scenario at its START (§2.4), a branched run too
                var scenario = ScenarioOfRun();
                if (string.IsNullOrWhiteSpace(scenario)) return Disabled("the scenario of this run is not known");
                return new("Ends the live session; you return to editing the scenario at its start", "End live, edit scenario", true, null, () =>
                {
                    if (_transport.ActiveExercise != Guid.Empty) LastExercise = _transport.ActiveExercise;
                    Armed = RunArmed.Preview;
                    _keepArmedOnEdit = true;
                    _control.OpenForEdit(scenario!);
                });

            case TransportMode.Replay:
                if (target == RunArmed.Live)
                {
                    // §3: AAR → Live from the indicator explains the branch (the timeline's menu would branch at once — a later part)
                    var at = MainToolbarTimeControlSection.FormatSpan(_transport.TotalTime);
                    return new($"A new exercise starts from {at}; this recording stays unchanged", "Go live from here", true, null, () =>
                    {
                        Armed = RunArmed.Live;
                        _transport.BranchLive();
                    });
                }
                if (_control is null) return Disabled("This host has no scenario session");
                var recorded = ScenarioOfRun();
                if (string.IsNullOrWhiteSpace(recorded)) return Disabled("the scenario of this recording is not known");
                return new($"Ends the AAR; you return to editing {recorded} at its start", "End AAR, edit scenario", true, null, () =>
                {
                    Armed = RunArmed.Preview;
                    _keepArmedOnEdit = true;
                    _control.OpenForEdit(recorded!);
                });

            default:
                // Edit / Idle — nothing running: arming loses nothing (R3: no confirmation)
                return new(null, "", true, null, () => Armed = target);
        }

        static MovePlan Disabled(string reason) => new(null, "", false, reason, () => { });
    }

    private void Ask(string text, RunModeConfirmation.Button confirm)
        => Pending = new RunModeConfirmation(text, new[] { confirm, new RunModeConfirmation.Button("Cancel", null) },
                                             done => { if (ReferenceEquals(Pending, done)) Pending = null; });

    // ── D12 + the cluster's state ───────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ AQ108 D12 — called by the adapter for every cluster-state broadcast. <b>The hook:</b> the cluster reaching <c>OperatingEdit</c> from anything but
    /// a dry run means a scenario was opened for editing ⇒ <see cref="Armed"/> returns to <see cref="HostDefault"/>. One hook for every way in
    /// (the File menu, the Start surface, <c>POST /scenario/load/edit</c>) — ⛔ not per opener. The exception is OUR OWN return to editing (■ in Live,
    /// → Preview): R2 says the armed mode stays. Also resumes the clock once a ▶-started live load is up.
    /// </summary>
    public void OnClusterState(ClusterState state)
    {
        var previous = _lastState;
        if (state == previous) return;
        _lastState = state;

        if (state == ClusterState.OperatingEdit
            && previous is not (ClusterState.LoadingPreview or ClusterState.OperatingPreview or ClusterState.UnloadingPreview))
        {
            if (_keepArmedOnEdit) _keepArmedOnEdit = false;
            else Armed = HostDefault;
        }

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
