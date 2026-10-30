using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Toolkit.Orchestration;
using Hrot.UI.Common.Facades;
using ImGuiNET;

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

    /// <summary>⭐ R-4001 — the operator gives up on an open question: <c>reply</c> is then never called. ⛔ There is no timeout.</summary>
    void Cancel() { }
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
/// ⭐⭐⭐ <c>CE-1440</c> (R-3066 audit #13) — <b>the ONE asker of the master's unsaved-changes check round</b>: it publishes the ONE
/// <see cref="CheckUnsavedIntent"/> (<see cref="Start"/>) and reads the outcome through <see cref="UnsavedCheckReads"/> (<see cref="Read"/>). The UI move
/// (<see cref="ClusterUnsavedChangesQuery"/>) polls <see cref="Read"/> once per frame; HTTP (<c>POST /scenario/check-unsaved</c> and the load gate) awaits
/// <see cref="WaitAsync"/> — the same object polled through the host's main-thread queue. ⛔ No second publish path, no second read path, and
/// (R-4001) no deadline: the master's round always ends — a node that dies is ejected by its heartbeat — so the wait is bounded only by the caller's token.
/// </summary>
public sealed class UnsavedCheckAsker
{
    private readonly Func<Hrot.Presentation.DebugApi.IClusterMasterControl?> _master;
    private readonly UnsavedCheckReads _reads;

    /// <param name="master">Where the round is asked (resolved per call: HTTP's master may be on another node); <see langword="null"/> ⇒ no master reachable.</param>
    /// <param name="reads">How the answer is read.</param>
    public UnsavedCheckAsker(Func<Hrot.Presentation.DebugApi.IClusterMasterControl?> master, UnsavedCheckReads reads)
    {
        _master = master ?? throw new ArgumentNullException(nameof(master));
        _reads  = reads  ?? throw new ArgumentNullException(nameof(reads));
    }

    /// <summary>This host can see check-round answers; a host that cannot must not gate on them.</summary>
    public bool Observes => _reads.Observes();

    /// <summary>Publishes ONE <see cref="CheckUnsavedIntent"/> and returns its request id; <see langword="null"/> when no master is reachable. A master that refuses to take it throws (<see cref="InvalidOperationException"/>).</summary>
    public Guid? Start()
    {
        var master = _master();
        if (master is null) return null;
        var id = Guid.NewGuid();
        master.CheckUnsaved(new CheckUnsavedIntent { RequestId = id });
        return id;
    }

    /// <summary>How round <paramref name="requestId"/> ended (the master's answer, or its refusal); <see langword="null"/> while it has not. Never blocks.</summary>
    public UnsavedCheckOutcome? Read(Guid requestId) => _reads.Outcome(requestId);

    /// <summary>
    /// Waits for the round's outcome, polling <see cref="Read"/> every <paramref name="pollMs"/> ms through <paramref name="onMainThread"/> (the master and the read model
    /// are ticked there). Returns <see langword="null"/> only when <paramref name="ct"/> is cancelled first. ⛔ No deadline of its own (R-4001).
    /// </summary>
    public async System.Threading.Tasks.Task<UnsavedCheckOutcome?> WaitAsync(
        Guid requestId,
        Func<Func<UnsavedCheckOutcome?>, System.Threading.Tasks.Task<UnsavedCheckOutcome?>> onMainThread,
        System.Threading.CancellationToken ct, int pollMs = 20)
    {
        while (!ct.IsCancellationRequested)
        {
            var outcome = await onMainThread(() => Read(requestId)).ConfigureAwait(false);
            if (outcome is not null) return outcome;
            try { await System.Threading.Tasks.Task.Delay(pollMs, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
        return null;
    }
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
/// <para>A refusal (the master refuses outside editing) reads as Unchanged: nothing of the SCENARIO can be lost outside editing.</para>
/// <para>⭐⭐ <b>R-4001 — no timeout</b> (backend CE-3340 ⑤ removes the HTTP gate's the same way): the master's round ends by itself — a node whose
/// heartbeat stops is removed from it — so the answer always comes; a stuck-but-alive round is shown ("checking…") and the operator cancels it
/// (<see cref="Cancel"/>). ⛔ SUPERSEDED: a fixed 15 s <c>Deadline</c> that read as Changed.</para>
/// </remarks>
public sealed class ClusterUnsavedChangesQuery : IUnsavedChangesQuery
{
    private readonly Func<string?> _loadedScenarioName;
    private readonly UnsavedCheckAsker? _asker;
    private (Guid Id, Action<UnsavedAnswer> Reply)? _pending;

    /// <param name="loadedScenarioName">The session's loaded scenario name (empty ⇒ NeverSaved).</param>
    /// <param name="master">Where the round is asked (<c>OrchestrationBusMasterControl</c> on the host's bus); <see langword="null"/> ⇒ nothing is asked.</param>
    /// <param name="reads">How the answer is read; <see langword="null"/> ⇒ this host observes nothing (a named scenario counts as unchanged).</param>
    public ClusterUnsavedChangesQuery(Func<string?> loadedScenarioName, Hrot.Presentation.DebugApi.IClusterMasterControl? master,
                                      UnsavedCheckReads? reads)
        : this(loadedScenarioName, master is null || reads is null ? null : new UnsavedCheckAsker(() => master, reads)) { }

    /// <summary>⭐ CE-1440 — over the shared asker (the same object HTTP's check awaits); <see langword="null"/> ⇒ this host observes nothing.</summary>
    public ClusterUnsavedChangesQuery(Func<string?> loadedScenarioName, UnsavedCheckAsker? asker)
    {
        _loadedScenarioName = loadedScenarioName ?? throw new ArgumentNullException(nameof(loadedScenarioName));
        _asker = asker;
    }

    /// <inheritdoc/>
    public void Ask(Action<UnsavedAnswer> reply)
    {
        if (string.IsNullOrWhiteSpace(_loadedScenarioName())) { reply(UnsavedAnswer.NeverSaved); return; }
        if (_asker is null || !_asker.Observes) { reply(UnsavedAnswer.Unchanged); return; }   // no way to see the answer: ask nothing
        if (_asker.Start() is not { } id) { reply(UnsavedAnswer.Unchanged); return; }
        _pending = (id, reply);
    }

    /// <inheritdoc/>
    public void Poll()
    {
        if (_pending is not { } p) return;
        if (_asker!.Read(p.Id) is not { } outcome) return;   // R-4001: wait — the round ends by itself (heartbeat), or the operator cancels
        _pending = null;
        p.Reply(outcome.Result is { Changed: true } ? UnsavedAnswer.Changed : UnsavedAnswer.Unchanged);   // a refusal (Result null): outside editing, nothing to lose
    }

    /// <inheritdoc/>
    public void Cancel() => _pending = null;
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
/// ⭐⭐ <c>CE-4097</c> (AQ108 R3, D10) — <b>the ONE confirmation shape</b>: a text and its buttons, resolved by pressing one. Drawn by the ONE helper
/// <see cref="ConfirmationModal"/>; its model is testable without ImGui. The last button of a confirmation is always Cancel (no action).
/// ⭐⭐ <c>CE-1438</c> (R-3066 audit #5) — the editor's app-exit prompt and the run choice's own confirmations are ALL instances of this
/// type (⭐ CE-1443: the <i>New Exercise</i> prompt left with the item): <c>AppExitPromptController</c> is a thin PRODUCER of it and hold no button list of their own.
/// </summary>
public sealed class RunModeConfirmation
{
    /// <summary>One button: its label and what pressing it does (<see langword="null"/> = nothing — Cancel).</summary>
    public sealed record Button(string Label, Action? Action);

    /// <summary>The label of the closing button every confirmation ends with.</summary>
    public const string CancelLabel = "Cancel";

    private readonly Action<RunModeConfirmation>? _done;
    private bool _resolved;

    /// <param name="text">What will happen, in the user's words.</param>
    /// <param name="buttons">The buttons, left to right; the LAST one is Cancel (closing the modal presses it).</param>
    /// <param name="done">Called when a button is pressed, BEFORE its action — the producer's slot clears itself here.</param>
    /// <param name="title">The modal's title.</param>
    /// <param name="popupId">The ImGui id of the modal (unique per producer: two confirmations may be up in one frame).</param>
    public RunModeConfirmation(string text, IReadOnlyList<Button> buttons, Action<RunModeConfirmation>? done = null,
                               string title = "Confirm", string popupId = "mt_run_confirm")
    {
        if (buttons is null || buttons.Count == 0) throw new ArgumentException("a confirmation has at least its Cancel button", nameof(buttons));
        Text = text;
        Buttons = buttons;
        _done = done;
        Title = title;
        PopupId = popupId;
    }

    /// <summary>⭐ The one way to build one: <paramref name="actions"/> then a Cancel that does nothing.</summary>
    public static RunModeConfirmation WithCancel(string text, Action<RunModeConfirmation>? done, string title, string popupId, params Button[] actions)
        => new(text, actions.Append(new Button(CancelLabel, null)).ToArray(), done, title, popupId);

    /// <summary>What will happen, in the user's words (AQ108 §3).</summary>
    public string Text { get; }

    /// <summary>The buttons, left to right; the last one is Cancel.</summary>
    public IReadOnlyList<Button> Buttons { get; }

    /// <summary>The modal's title.</summary>
    public string Title { get; }

    /// <summary>The ImGui id of the modal.</summary>
    public string PopupId { get; }

    /// <summary>Set by <see cref="ConfirmationModal"/> once it opened the popup for this confirmation (view state: one open per model).</summary>
    internal bool PopupShown { get; set; }

    /// <summary>Presses <paramref name="button"/>: the confirmation is gone, then its action (if any) runs. A second call is ignored.</summary>
    public void Resolve(Button button)
    {
        if (button is null) throw new ArgumentNullException(nameof(button));
        if (!Buttons.Contains(button)) throw new ArgumentException("not a button of this confirmation", nameof(button));
        if (_resolved) return;
        _resolved = true;
        _done?.Invoke(this);
        button.Action?.Invoke();
    }

    /// <summary>Presses the closing Cancel button (Esc / ✕ / a producer's cancel).</summary>
    public void Cancel() => Resolve(Buttons[^1]);
}

/// <summary>
/// ⭐⭐ <c>CE-1438</c> — <b>the ONE draw of a <see cref="RunModeConfirmation"/></b>: a modal with its text and buttons; closing it (Esc / ✕) is Cancel, the last button.
/// The toolbar draws the run choice's pending one, the editor draws its exit and <i>New Exercise</i> prompts — all through here, ⛔ no per-prompt ImGui.
/// </summary>
public static class ConfirmationModal
{
    /// <summary>Draws <paramref name="pending"/> (nothing when <see langword="null"/>); call once per frame while a confirmation may be up.</summary>
    public static void Draw(RunModeConfirmation? pending)
    {
        if (pending is null) return;
        var id = $"{pending.Title}###{pending.PopupId}";
        if (!pending.PopupShown) { ImGui.OpenPopup(id); pending.PopupShown = true; }

        ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        bool open = true;
        if (!ImGui.BeginPopupModal(id, ref open, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings))
        {
            if (!open) pending.Cancel();   // (a modal that was closed without a button)
            return;
        }
        ImGui.TextWrapped(pending.Text);
        ImGui.Separator();
        RunModeConfirmation.Button? pressed = null;
        foreach (var button in pending.Buttons)
        {
            if (ImGui.Button(button.Label)) pressed = button;
            ImGui.SameLine();
        }
        if (!open) pressed = pending.Buttons[^1];
        if (pressed is not null) { ImGui.CloseCurrentPopup(); pending.Resolve(pressed); }
        ImGui.EndPopup();
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
    /// <summary>⭐ CE-4100 (AQ108 D15) — discard an UNARCHIVED recording: publishes the one storage-delete request (the same <c>ExecuteStorageOpIntent</c> <c>DELETE /exercises/{id}</c> publishes). The ONE construction.</summary>
    void DiscardRecording(Guid exerciseId);
    /// <summary>⭐ CE-3341 (AQ108 §2.14, D12a) — the CLUSTER's armed mode as this node last heard it (<c>ClusterView.ArmedMode</c>, mapped by name); <see langword="null"/> when the adapter has no view (no node yet).</summary>
    RunArmed? ClusterArmed { get; }
    /// <summary>⭐ R-1043 — the mode the cluster's LAST scenario load went into (<c>ClusterView.LastLoadMode</c>, held and broadcast by the master; before any load its initial armed mode); <see langword="null"/> with no view. What a Load goes into while AAR / DAR is armed.</summary>
    RunArmed? ClusterLastLoad { get; }
    /// <summary>⭐ CE-3341 — publish the armed mode as the cluster-wide setting (<c>SetRunSettingsIntent { Armed }</c> on the bus). The ONE caller is <see cref="RunModeChoice"/>'s <c>Arm</c>.</summary>
    void SendArmed(RunArmed armed);

    /// <summary>⭐⭐⭐ <c>CE-1442</c> (U1, §11.7 ①) — the ONE run gate: built by the adapter from the node's <c>ClusterView</c> and the host's <see cref="HostGatePolicy"/> (<see cref="RunGate.From"/>); no view ⇒ <see cref="RunGate.AlwaysOpen"/>.</summary>
    RunGate Gate { get; }
    /// <summary>⭐ CE-1442 (§11.7 ⑥) — cancel the load in progress on this node (the host's <c>ClusterSlave.CancelLoad</c>): it fails everywhere and the cluster returns to Idle with <paramref name="reason"/>. A host with no slave does nothing.</summary>
    void CancelLoad(string? reason);
}
