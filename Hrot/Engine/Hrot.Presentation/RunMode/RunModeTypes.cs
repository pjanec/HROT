using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hrot.UI.Common.Facades;

namespace Hrot.UI.Common.RunMode;

/// <summary>
/// ⭐⭐ <c>CE-4097</c> (AQ108 §1, D1) — what ▶ runs next: the user's CHOICE, changed only by <see cref="RunModeChoice.Choose"/> (R1).
/// Code names are unchanged by the words the user sees (§1a): <see cref="AAR"/> is the replay of a recording.
/// </summary>
public enum RunArmed
{
    /// <summary>A dry run of the scenario being edited (nothing recorded, ■ restores).</summary>
    Preview,
    /// <summary>The exercise with live players.</summary>
    Live,
    /// <summary>After Action Review — a recording played back (the code's <see cref="TransportMode.Replay"/>).</summary>
    AAR,
}

/// <summary>
/// ⭐⭐ <c>CE-4097</c> (AQ108 §2.8) — the NARROW seam over the scenario actions every operator host already has. ⛔ Not a parallel interface
/// with a second implementation: <c>IScenarioSession</c> EXTENDS this and its one class (<c>EditorScenarioSession</c>) implements it.
/// It lives here (Hrot.Presentation) because the choice is built here and <c>Hrot.Editor.AiShared</c> already references this assembly.
/// </summary>
public interface IRunScenarioControl
{
    /// <summary>The currently loaded scenario name, or <c>null</c> after a clear.</summary>
    string? LoadedScenarioName { get; }

    /// <summary>
    /// ⭐⭐⭐ <b>Load for RUNNING, cluster-wide</b> — the <c>/scenario/load/live</c> path. Publishes
    /// <c>TransitionStateIntent{OperatingLive}</c> with a <b>fresh</b> <c>ExerciseId</c>: a live load IS a new exercise run, and that id is
    /// what recording/replay keys off.
    /// </summary>
    void LoadForLive(string scenarioName);

    /// <summary>
    /// ⭐⭐⭐ <b>Open for AUTHORING, cluster-wide</b> — the <c>/scenario/load/edit</c> path. Asks for Idle, wipes locally once idle, then
    /// requests <c>OperatingEdit</c>. ⭐ <c>ExerciseId</c> is deliberately fresh-per-transaction here as it always was; authoring is not an
    /// exercise run.
    /// </summary>
    void OpenForEdit(string scenarioName);

    /// <summary>
    /// ⭐⭐ <b>CE-4098 (AQ108 D16) — open a RECORDING, cluster-wide</b> (<c>File › Open Recording</c>): publishes the ONE replay intent
    /// (<c>ReplaySessionIntents.EnterReplay(exerciseId)</c>, the same construction <c>POST /replay {exerciseId}</c> ends in) — the master
    /// plans the unload and the replay from ANY state, so the session adds no Idle hop. ⛔ Not a second open path: the recording's own
    /// opener (<c>IRecordingOpener</c>) is the Replay Browser's, for FILES; this is the cluster's, for EXERCISES the master recorded.
    /// </summary>
    void OpenRecording(Guid exerciseId);

    /// <summary>Saves the world into the scenario most recently loaded. No-op when none is loaded.</summary>
    void SaveCurrent();

    /// <summary>
    /// ⭐⭐ <b>Saves the live running state</b> — publishes the existing <c>TakeCheckpointIntent</c> to the master
    /// (<c>File/Checkpoint/Take Checkpoint</c>). One home for the publish: the session already holds the orchestration bus.
    /// </summary>
    void TakeCheckpoint();
}

/// <summary>⭐ <c>CE-4097</c> (AQ108 D20) — the cluster's answer to "are there unsaved changes?".</summary>
public enum UnsavedAnswer
{
    /// <summary>The world differs from the saved scenario.</summary>
    Changed,
    /// <summary>The world equals the saved scenario.</summary>
    Unchanged,
    /// <summary>There is no saved scenario yet (a new scenario from a recipe) — it must be saved AS something first.</summary>
    NeverSaved,
}

/// <summary>
/// ⭐⭐ <c>CE-4097</c> (AQ108 D20) — <b>ONE seam: "ask the cluster whether there are unsaved edits".</b> The answer comes through
/// <c>reply</c> (a cluster check round answers later than the call). ⛔ ui builds no round of its own: backend's CE-4093 master
/// round (<c>NodeOpType.CheckUnsaved</c>) is wired HERE when it lands; until then <see cref="ScenarioNameUnsavedChangesQuery"/> answers.
/// </summary>
public interface IUnsavedChangesQuery
{
    /// <summary>Asks; <paramref name="reply"/> is called exactly once (possibly later, possibly synchronously).</summary>
    void Ask(Action<UnsavedAnswer> reply);
}

/// <summary>
/// ⭐ The PRODUCTION DEFAULT of <see cref="IUnsavedChangesQuery"/> until backend's master check round exists: <see cref="UnsavedAnswer.NeverSaved"/>
/// when no scenario name is loaded, else <see cref="UnsavedAnswer.Unchanged"/>.
/// </summary>
/// <remarks>
/// ⚠ <b>TODO — CE-4093 (backend's master check round, AQ108 D20 / §4a).</b> The node side exists (<c>NodeOpType.CheckUnsaved = 35</c>, each node
/// answers changed / unchanged / no-baseline); the MASTER's round that folds the answers is not built. When it lands, replace this class with an
/// implementation that publishes the round and calls <c>reply</c> with <see cref="UnsavedAnswer.Changed"/> when any node (or a missing / "changed"
/// answer — only an explicit "unchanged" means saved, <c>WorldChangeCheckHandler.MeansChanged</c>) says so. ⛔ Do not build a round here.
/// Until then "Unchanged" is a LIE for an edited, saved scenario: ▶ in Live armed goes live from the file on disk without the Save-first prompt.
/// </remarks>
public sealed class ScenarioNameUnsavedChangesQuery : IUnsavedChangesQuery
{
    private readonly Func<string?> _loadedScenarioName;

    /// <param name="loadedScenarioName">The loaded scenario's name (the session's <see cref="IRunScenarioControl.LoadedScenarioName"/>).</param>
    public ScenarioNameUnsavedChangesQuery(Func<string?> loadedScenarioName)
        => _loadedScenarioName = loadedScenarioName ?? throw new ArgumentNullException(nameof(loadedScenarioName));

    /// <inheritdoc/>
    public void Ask(Action<UnsavedAnswer> reply)
        => reply(string.IsNullOrWhiteSpace(_loadedScenarioName()) ? UnsavedAnswer.NeverSaved : UnsavedAnswer.Unchanged);
}

/// <summary>
/// ⭐⭐ <c>CE-4097</c> (AQ108 R3, D10) — <b>the ONE confirmation shape</b>: a text and its buttons, resolved by pressing one. Drawn as a modal
/// by the toolbar section; its model is testable without ImGui. The last button of a confirmation is always Cancel (no action).
/// </summary>
public sealed class RunModeConfirmation
{
    /// <summary>One button: its label and what pressing it does (<see langword="null"/> = nothing — Cancel).</summary>
    public sealed record Button(string Label, Action? Action);

    private readonly Action<RunModeConfirmation> _done;
    private bool _resolved;

    internal RunModeConfirmation(string text, IReadOnlyList<Button> buttons, Action<RunModeConfirmation> done)
    {
        Text = text;
        Buttons = buttons;
        _done = done;
    }

    /// <summary>What will happen, in the user's words (AQ108 §3).</summary>
    public string Text { get; }

    /// <summary>The buttons, left to right; the last one is Cancel.</summary>
    public IReadOnlyList<Button> Buttons { get; }

    /// <summary>Presses <paramref name="button"/>: the confirmation is gone, then its action (if any) runs. A second call is ignored.</summary>
    public void Resolve(Button button)
    {
        if (button is null) throw new ArgumentNullException(nameof(button));
        if (!Buttons.Contains(button)) throw new ArgumentException("not a button of this confirmation", nameof(button));
        if (_resolved) return;
        _resolved = true;
        _done(this);
        button.Action?.Invoke();
    }
}

/// <summary>How the mode indicator draws: dim before a run, bright steady while a run is paused, blinking while it plays (AQ108 D1).</summary>
public enum IndicatorLevel { Dim, Bright, Blink }

/// <summary>
/// ⭐ The mode indicator as the toolbar draws it: a FontAwesome 6 glyph, its colour, the word the user sees (§1a: Preview / Live / DAR / AAR)
/// and its <see cref="IndicatorLevel"/>.
/// </summary>
public sealed record RunModeIndicator(string Glyph, Vector4 Color, string Word, IndicatorLevel Level)
{
    /// <summary>The opacity to draw at, <paramref name="seconds"/> into the session: Dim 0.4, Bright 1, Blink pulses 0.3–1 at ~1.2 Hz.</summary>
    public static float Alpha(IndicatorLevel level, double seconds) => level switch
    {
        IndicatorLevel.Dim    => 0.4f,
        IndicatorLevel.Bright => 1f,
        _                     => 0.3f + 0.7f * (float)(0.5 + 0.5 * Math.Sin(seconds * Math.PI * 2 * 1.2)),
    };
}

/// <summary>One entry of the indicator's mode menu: the mode it arms, whether it can be chosen now (and why not), and the confirmation it asks first.</summary>
public sealed record RunModeMove(RunArmed Target, string Label, string? ConfirmText, bool Enabled, string? DisabledReason, bool IsArmed);

/// <summary>A short note the toolbar shows after ■ in Live (AQ108 §3): <c>"Exercise recorded (12:31) — Replay it"</c> or <c>"Live run ended"</c>.</summary>
public sealed record RunModeToast(string Text, Guid? ReplayExercise, DateTime ExpiresAt);

/// <summary>
/// ⭐⭐ <c>CE-4097</c> (AQ108 §2.8) — the raw transport the <see cref="RunModeChoice"/> stands on: the cases that keep TODAY's behaviour (dry-run
/// exit, review exit, seek) and the readings of the cluster it needs. Implemented by <c>ClusterTimeTransportAdapter</c>, the one facade.
/// </summary>
public interface IRunTransport
{
    /// <summary>The facade's mode for the cluster state as this host last heard it.</summary>
    TransportMode Mode { get; }
    /// <summary>The cluster's last pause decision.</summary>
    bool IsPaused { get; }
    /// <summary>The run's sim clock, seconds.</summary>
    double TotalTime { get; }
    /// <summary>⭐ CE-4090: this run is recorded (in a live run / DAR / AAR: not flagged <c>RunNotRecorded</c>); in Edit / Preview: the "Record live runs" setting (not <c>RecordLiveRunsOff</c>).</summary>
    bool RecordsLiveRuns { get; }
    /// <summary>The cluster state's <c>ExerciseId</c> (<see cref="Guid.Empty"/> before any).</summary>
    Guid ActiveExercise { get; }
    /// <summary>⭐ The scenario of <see cref="ActiveExercise"/>, from the inventory (<c>ExerciseInventoryItem.ScenarioId</c>); <see langword="null"/> when unknown.</summary>
    string? ActiveExerciseScenario { get; }
    /// <summary>■ exactly as before the choice existed: a review back to live, a dry run back to edit, anything else to Idle.</summary>
    void StopRaw();
    /// <summary>The dry run (Preview), running or paused at its first frame.</summary>
    void EnterDryRun(bool startPaused);
    /// <summary>Resume the cluster clock.</summary>
    void Resume();
    /// <summary>Seek the review / replay (on the active axis).</summary>
    void Seek(double t);
    /// <summary>From a replay: a new live run from the moment it stands at (the built branch, D18).</summary>
    void BranchLive();
}
