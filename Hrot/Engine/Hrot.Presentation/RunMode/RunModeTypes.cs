using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Toolkit.Orchestration;
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

    /// <summary>
    /// ⭐ <c>CE-4097</c> part two (AQ108 D21) — <see cref="OpenRecording(Guid)"/> at a MOMENT of the recording's own sim clock (the timeline's
    /// <i>AAR from here</i>); <see langword="null"/> = its start. ⭐ A default member so a fake session need not state it: the one construction of the
    /// replay intent is <c>ReplaySessionIntents.EnterReplay(exerciseId, simSeconds:)</c>, which the real session overrides this with.
    /// </summary>
    void OpenRecording(Guid exerciseId, double? atSimSeconds) => OpenRecording(exerciseId);

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
/// <c>reply</c> (a cluster check round answers later than the call, so <see cref="Poll"/> is driven from the choice's per-frame update).
/// ⛔ ui builds no round of its own: the master's CE-4093 round is what <see cref="ClusterUnsavedChangesQuery"/> asks.
/// </summary>
public interface IUnsavedChangesQuery
{
    /// <summary>Asks; <paramref name="reply"/> is called exactly once (possibly later, possibly synchronously).</summary>
    void Ask(Action<UnsavedAnswer> reply);

    /// <summary>Called once per frame by <see cref="RunModeChoice.Update"/>: delivers an answer that has arrived since (no blocking, ever). Default: nothing to poll.</summary>
    void Poll() { }
}

/// <summary>
/// ⭐⭐ <c>CE-4097</c> part two (AQ108 D20, §4b) — how a host READS a check round's answer: this node's cluster read model
/// (<c>ISubsystemDebugProvider.UnsavedCheck(requestId)</c> / <c>ObservesUnsavedChecks</c>; the editor's in-process master core reads its
/// <c>ClusterUiCache</c>). ⛔ <see cref="Observes"/> false ⇒ the host has no read model: nothing is asked and the scenario counts as unchanged
/// (the HTTP gate's own rule, <c>ALoad_WhereThereIsNothingToLose_OrNoWayToSeeTheAnswer_AsksNoRound</c>).
/// </summary>
public sealed record UnsavedCheckReads(Func<bool> Observes, Func<Guid, UnsavedCheckOutcome?> Outcome)
{
    /// <summary>The reads over a node's debug provider (a <see langword="null"/> provider observes nothing).</summary>
    public static UnsavedCheckReads From(Func<Hrot.Presentation.DebugApi.ISubsystemDebugProvider?> provider)
        => new(() => provider()?.ObservesUnsavedChecks == true, id => provider()?.UnsavedCheck(id));
}

/// <summary>
/// ⭐⭐⭐ <c>CE-4097</c> part two (AQ108 D20) — <b>the production <see cref="IUnsavedChangesQuery"/>: the master's ONE check round</b> (CE-4093),
/// asked ONLY through the members the coordinator's NOTE lists — <c>IClusterMasterControl.CheckUnsaved(new CheckUnsavedIntent{…})</c>, answered by
/// <c>ISubsystemDebugProvider.UnsavedCheck(requestId)</c> → <see cref="UnsavedCheckOutcome"/> (null until it arrives; polled, never waited on).
/// ⛔ Never decided on <c>ScenarioDirty</c> (the last verdict, display only). One <see cref="Ask"/> publishes ONE intent.
/// </summary>
/// <remarks>
/// <para>NeverSaved stays decided HERE by an empty <see cref="IRunScenarioControl.LoadedScenarioName"/> (the master's round answers "changed" for a
/// world, not "there is no file"). A host that observes nothing answers Unchanged for a named scenario — the same rule the HTTP gate uses.</para>
/// <para>A refusal (the master refuses outside editing) reads as Unchanged: nothing of the SCENARIO can be lost outside editing. ⚠ No answer within
/// <see cref="Deadline"/> reads as Changed — fail closed to the Save-first prompt (the HTTP gate fails closed with a 504).</para>
/// </remarks>
public sealed class ClusterUnsavedChangesQuery : IUnsavedChangesQuery
{
    /// <summary>How long a round may take before the answer is taken as "changed" (the HTTP gate's own 15 s).</summary>
    public static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);

    private readonly Func<string?> _loadedScenarioName;
    private readonly Hrot.Presentation.DebugApi.IClusterMasterControl? _master;
    private readonly UnsavedCheckReads? _reads;
    private readonly Func<DateTime> _now;

    private (Guid Id, Action<UnsavedAnswer> Reply, DateTime Due)? _pending;

    /// <param name="loadedScenarioName">The session's loaded scenario name (empty ⇒ NeverSaved).</param>
    /// <param name="master">Where the round is asked (<c>OrchestrationBusMasterControl</c> on the host's bus); <see langword="null"/> ⇒ nothing is asked.</param>
    /// <param name="reads">How the answer is read; <see langword="null"/> ⇒ this host observes nothing (a named scenario counts as unchanged).</param>
    /// <param name="now">Clock of the deadline (tests inject one).</param>
    public ClusterUnsavedChangesQuery(Func<string?> loadedScenarioName, Hrot.Presentation.DebugApi.IClusterMasterControl? master,
                                      UnsavedCheckReads? reads, Func<DateTime>? now = null)
    {
        _loadedScenarioName = loadedScenarioName ?? throw new ArgumentNullException(nameof(loadedScenarioName));
        _master = master;
        _reads  = reads;
        _now    = now ?? (() => DateTime.UtcNow);
    }

    /// <inheritdoc/>
    public void Ask(Action<UnsavedAnswer> reply)
    {
        if (string.IsNullOrWhiteSpace(_loadedScenarioName())) { reply(UnsavedAnswer.NeverSaved); return; }
        if (_master is null || _reads is null || !_reads.Observes()) { reply(UnsavedAnswer.Unchanged); return; }   // no way to see the answer: ask nothing
        var id = Guid.NewGuid();
        _pending = (id, reply, _now() + Deadline);
        _master.CheckUnsaved(new CheckUnsavedIntent { RequestId = id });
    }

    /// <inheritdoc/>
    public void Poll()
    {
        if (_pending is not { } p) return;
        UnsavedAnswer? answer = null;
        if (_reads!.Outcome(p.Id) is { } outcome)
            answer = outcome.Result is { Changed: true } ? UnsavedAnswer.Changed : UnsavedAnswer.Unchanged;   // a refusal (Result null): outside editing, nothing to lose
        else if (_now() >= p.Due)
            answer = UnsavedAnswer.Changed;
        if (answer is null) return;
        _pending = null;
        p.Reply(answer.Value);
    }
}

/// <summary>
/// ⭐⭐ <c>CE-4097</c> part two (AQ108 D8, D22) — a scenario session whose <c>SaveCurrent</c> a run choice may intercept: in a Live / DAR / AAR
/// run a Save is a CHECKPOINT, never a scenario write. ⭐ Set by <see cref="RunModeChoice"/>'s constructor, so EVERY save path of a host
/// (File › Save, Ctrl+S, the toolbar) is routed by construction — ⛔ not three seams each remembering to ask. HTTP's <c>POST /scenario/save</c>
/// stays explicit (409 in Live and AAR, D22) and never reaches this.
/// </summary>
public interface ISaveInterception
{
    /// <summary>Returns <see langword="true"/> when it handled the save (the scenario is then NOT written).</summary>
    Func<bool>? SaveIntercept { get; set; }
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
    /// <summary>⭐ CE-4097 D5 — where the active run branched from (the inventory's <c>ParentExerciseId</c> + <c>BranchWallTicks</c>, the parent's WALL ticks at the branch frame); <see langword="null"/> when it began at a load.</summary>
    (Guid Parent, long BranchWallTicks)? ActiveExerciseBranch { get; }
    /// <summary>⭐ The open replay's wall zero (<c>ReplaySession.ZeroWallTicks</c>); <see langword="null"/> while no session is active — the moment a wall-tick target can be seeked.</summary>
    long? ReplayZeroWallTicks { get; }
    /// <summary>⭐ Seek the open replay to a WALL-ticks moment (converted to the session's axis, the timeline's own conversion).</summary>
    void SeekWall(long wallTicks);
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
