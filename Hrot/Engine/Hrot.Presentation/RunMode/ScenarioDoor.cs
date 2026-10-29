using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.UI.Common.Facades;

namespace Hrot.UI.Common.RunMode;

/// <summary>
/// ⭐⭐⭐ <c>CE-4101</c> (AQ108 §2.11a) — <b>why the user is LEAVING the open scenario for Idle</b> (the OUT door). The reason words the question ("<i>Starting a new exercise</i> leaves editing")
/// and tells a rail which entry point it drove. ⚠ <see cref="BackToStart"/> and <see cref="Exit"/> are the callers CE-4100 ② wires (the Start-perspective switch and the app exit); the door already
/// decides for them.
/// </summary>
public enum LeaveReason
{
    /// <summary><i>File › Live › New Exercise</i> (the cluster-wide reset).</summary>
    NewExercise,
    /// <summary>HTTP <c>POST /replay/stop</c> — explicit, answers 409 instead of asking.</summary>
    HttpStop,
    /// <summary>The film director's stop of its live run (no unsaved scenario can be in play there — it ends a run).</summary>
    DirectorStop,
    /// <summary>Back to the Start perspective (CE-4100 ②).</summary>
    BackToStart,
    /// <summary>Exit the application (CE-4100 ②).</summary>
    Exit,
}

/// <summary>What a <see cref="DoorQuestion"/> asks (a host may answer the kinds differently: HTTP stops a run and refuses a loss).</summary>
public enum DoorQuestionKind
{
    /// <summary>A run is running and the move stops it (<i>Stop &amp; Load</i>).</summary>
    StopAndLoad,
    /// <summary>The scenario being edited has unsaved changes (<i>Save / Discard</i>).</summary>
    Unsaved,
    /// <summary>The scenario being edited was never saved (<i>Discard</i>).</summary>
    NeverSaved,
}

/// <summary>
/// ⭐ One question the door puts to its asker: its words and its buttons (each carries the move that continues — the asker adds Cancel, which does nothing). <see cref="Run"/> names
/// the run a <see cref="DoorQuestionKind.StopAndLoad"/> would stop ("a live run" / "a dry run").
/// </summary>
public sealed record DoorQuestion(DoorQuestionKind Kind, string Title, string Text, IReadOnlyList<RunModeConfirmation.Button> Buttons, string? Run = null);

/// <summary>
/// ⭐⭐ <c>CE-4101</c> — <b>who answers the door's questions</b>, injected per host role: a UI host asks the user (<see cref="ConfirmingAsker"/>), HTTP never prompts
/// (<see cref="RefusingAsker"/>: it stops a run and refuses a loss). The DECISION is the door's; only the answer is the host's.
/// </summary>
public interface IDoorAsker
{
    /// <summary>The caller has already said it does not care about unsaved edits (a harness's <c>discardUnsaved</c>): the door then asks no check round at all.</summary>
    bool DiscardsUnsaved { get; }

    /// <summary>Puts <paramref name="question"/> to whoever answers; the button pressed runs its action (that is how the move continues).</summary>
    void Ask(DoorQuestion question);
}

/// <summary>
/// ⭐⭐ <c>CE-4101</c> — the UI's asker: a question becomes the ONE <see cref="RunModeConfirmation"/> in a single pending slot, which the toolbar already draws through
/// <see cref="ConfirmationModal"/>. ⭐ The run choice's own mode confirmations (R3) go through the SAME slot (<see cref="Show"/>), so the toolbar draws one thing.
/// </summary>
public sealed class ConfirmingAsker : IDoorAsker
{
    /// <summary>The confirmation waiting for a button; <see langword="null"/> when none.</summary>
    public RunModeConfirmation? Pending { get; private set; }

    /// <inheritdoc/>
    public bool DiscardsUnsaved => false;

    /// <inheritdoc/>
    public void Ask(DoorQuestion question) => Show(question.Title, question.Text, question.Buttons.ToArray());

    /// <summary>Sets <see cref="Pending"/> to <paramref name="actions"/> followed by Cancel.</summary>
    public void Show(string title, string text, params RunModeConfirmation.Button[] actions)
        => Pending = RunModeConfirmation.WithCancel(text, done => { if (ReferenceEquals(Pending, done)) Pending = null; }, title, "mt_run_confirm", actions);
}

/// <summary>
/// ⭐⭐ <c>CE-4101</c> (AQ108 D22) — HTTP's asker (and any non-interactive caller's): <b>nothing is prompted</b>. A run is stopped (CE-1025, ruling 53: the load's own unload ends it — <see cref="StoppedRun"/>
/// says which); a LOSS of unsaved edits is refused — <see cref="Refusal"/> is the text the route answers 409 with. <paramref name="refusalText"/> words it (HTTP's own text, e.g. with the changed nodes);
/// <paramref name="discardUnsaved"/> is the caller saying it will lose them (the door then does not even ask the cluster).
/// </summary>
public sealed class RefusingAsker : IDoorAsker
{
    private readonly Func<DoorQuestion, string?>? _refusalText;
    private readonly bool _discard;

    /// <param name="refusalText">The refusal's words for a question; <see langword="null"/> (or a null return) ⇒ the question's own text.</param>
    /// <param name="discardUnsaved">The caller accepts losing unsaved edits (HTTP <c>discardUnsaved: true</c>, the director's own world).</param>
    public RefusingAsker(Func<DoorQuestion, string?>? refusalText = null, bool discardUnsaved = false)
    {
        _refusalText = refusalText;
        _discard = discardUnsaved;
    }

    /// <inheritdoc/>
    public bool DiscardsUnsaved => _discard;

    /// <summary>The text of the refusal, or <see langword="null"/> when nothing was refused (the move went, or is still deciding).</summary>
    public string? Refusal { get; private set; }

    /// <summary>The run the move stopped ("a live run" / "a dry run"), or <see langword="null"/>.</summary>
    public string? StoppedRun { get; private set; }

    /// <inheritdoc/>
    public void Ask(DoorQuestion question)
    {
        if (question.Kind == DoorQuestionKind.StopAndLoad)
        {
            StoppedRun = question.Run;
            question.Buttons[0].Action?.Invoke();   // non-interactive: the run is stopped, as HTTP always did
            return;
        }
        Refusal = _refusalText?.Invoke(question) ?? question.Text;
    }
}

/// <summary>⭐ What a move through the door is called in its questions, and how a run it would stop is worded (<see langword="null"/> = the move stops a run without asking: the OUT door, ■ never asks).</summary>
public readonly record struct DoorMove(string What, Func<string, string>? RunText)
{
    /// <summary>Opening <paramref name="scenario"/> for editing.</summary>
    public static DoorMove OpeningScenario(string scenario) => new("Opening another scenario", run => RunModeRunGuard.LoadPrompt(run, scenario));
    /// <summary>Loading <paramref name="scenario"/> live.</summary>
    public static DoorMove LoadingLive(string scenario) => new("Loading a scenario live", run => RunModeRunGuard.LoadPrompt(run, scenario));
    /// <summary>Opening a recording into AAR.</summary>
    public static DoorMove OpeningRecording => new("Opening a recording", RunModeRunGuard.RecordingPrompt);
    /// <summary>Leaving the open scenario for Idle.</summary>
    public static DoorMove Leaving(LeaveReason reason) => new(reason switch
    {
        LeaveReason.NewExercise => "Starting a new exercise",
        LeaveReason.BackToStart => "Going back to the start",
        LeaveReason.Exit        => "Exiting",
        _                       => "Stopping",
    }, null);
}

/// <summary>A scenario session that owns its <see cref="ScenarioDoor"/>; the run choice binds to it (<see cref="ScenarioDoor.Bind"/>), HTTP asks through it.</summary>
public interface IHasScenarioDoor
{
    /// <summary>The ONE door in and out of the open scenario on this host.</summary>
    ScenarioDoor Door { get; }
}

/// <summary>
/// ⭐⭐⭐ <b><c>CE-4101</c> (AQ108 §2.11a) — THE guarded door: the ONE decision for every way of opening a scenario or a recording (IN) and of leaving the open scenario for Idle (OUT).</b>
/// 📄 <c>docs/blueprints/Architect_Question_108_Run_Mode_Picker.md</c> §2.11a (the classes and sequences), §2.11 (CE-1438, the decision this moves), §2.13 (CE-1440, the one unsaved asker it reuses).
/// <list type="bullet">
/// <item>a run is running (<see cref="RunModeRunGuard"/>) ⇒ <i>Stop &amp; Load</i> (the OUT door stops it without asking — ■ never asks, R3);</item>
/// <item>editing (<see cref="TransportMode.Edit"/>) ⇒ the cluster's check round (<see cref="IUnsavedChangesQuery"/>, no deadline, <see cref="CancelCheck"/> while it runs): unsaved ⇒ <i>Save / Discard / Cancel</i>, never saved ⇒ <i>Discard / Cancel</i>;</item>
/// <item>otherwise the move goes out at once.</item>
/// </list>
/// ⭐ The session COMPOSES it: its public <c>OpenForEdit</c> / <c>LoadForLive</c> / <c>OpenRecording</c> / <c>NewExercise</c> are these calls, and its RAW verbs are explicit
/// <see cref="IRunScenarioControl"/> members handed only to the <see cref="RunModeChoice"/> (the same shape as <c>WindowManager.SwitchPerspective</c> over <c>TrySwitchPerspective</c>).
/// ⛔ No unguarded public open remains: a caller can only reach this.
/// </summary>
public sealed class ScenarioDoor
{
    private readonly IRunScenarioControl? _raw;
    private readonly Action<LeaveReason>? _leave;
    private Func<TransportMode> _mode;
    private IUnsavedChangesQuery _unsaved;
    private IDoorAsker _asker = new RefusingAsker();   // until a run choice binds a UI asker: nothing can prompt, so a loss is refused
    private bool _asking;

    /// <param name="raw">The raw verbs (the session's explicit <see cref="IRunScenarioControl"/> members); <see langword="null"/> for a door that only DECIDES (<see cref="Admit"/> — HTTP's own, on a host with no scenario session).</param>
    /// <param name="mode">The facade's mode for the cluster state (<see cref="ClusterReplayControls.ModeOf"/> of the session's state until a choice binds its transport).</param>
    /// <param name="leave">The raw leave to Idle (the session's); <see langword="null"/> on a host whose door has no OUT.</param>
    public ScenarioDoor(IRunScenarioControl? raw, Func<TransportMode> mode, Action<LeaveReason>? leave = null)
    {
        _raw   = raw;
        _mode  = mode ?? throw new ArgumentNullException(nameof(mode));
        _leave = leave;
        _unsaved = new NoCheck();
    }

    /// <summary>A door nobody bound (no run choice, so no check round to ask) cannot tell: nothing is asked and the answer is "unchanged".</summary>
    private sealed class NoCheck : IUnsavedChangesQuery
    {
        public void Ask(Action<UnsavedAnswer> reply) => reply(UnsavedAnswer.Unchanged);
        public void Poll() { }
        public void Cancel() { }
    }

    /// <summary>⭐ Called by the <see cref="RunModeChoice"/> constructor: the transport's mode, the host's unsaved seam and the UI's asker.</summary>
    public void Bind(Func<TransportMode> mode, IUnsavedChangesQuery unsaved, IDoorAsker asker)
    {
        _mode     = mode     ?? throw new ArgumentNullException(nameof(mode));
        _unsaved  = unsaved  ?? throw new ArgumentNullException(nameof(unsaved));
        _asker    = asker    ?? throw new ArgumentNullException(nameof(asker));
    }

    /// <summary>⭐ Set by the run choice: a recording is about to be opened (its <c>LastExercise</c> / AAR-armed bookkeeping) — so EVERY way of opening one, the picker's included, leaves the choice right.</summary>
    public Action<Guid>? RecordingOpening { get; set; }

    /// <summary>The check round is open (shown as "checking…"; no timeout — the operator may <see cref="CancelCheck"/>).</summary>
    public bool IsCheckingUnsaved => _asking;

    /// <summary>A question is open or a check round is running: another move does not stack on it.</summary>
    public bool IsBusy => _asking || (_asker is ConfirmingAsker { Pending: not null }) || (AlsoBusy?.Invoke() ?? false);

    /// <summary>⭐ Set by the run choice: its own check round (▶ in Live armed) shares the one unsaved seam, so a door move must not start a second round over it.</summary>
    public Func<bool>? AlsoBusy { get; set; }

    /// <summary>The operator gives up waiting for the check round: nothing proceeds.</summary>
    public void CancelCheck()
    {
        if (!_asking) return;
        _unsaved.Cancel();
        _asking = false;
    }

    private IRunScenarioControl Raw => _raw ?? throw new InvalidOperationException("this door only decides (Admit): it has no scenario session to open with");

    // ── IN ─────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Opens <paramref name="scenarioName"/> for EDITING. <paramref name="asker"/> (default: the host's UI asker) answers the questions; <paramref name="answered"/> is a check round the caller already ran (HTTP's);
    /// <paramref name="then"/> runs once the open went out.
    /// </summary>
    public void Open(string scenarioName, IDoorAsker? asker = null, UnsavedAnswer? answered = null, Action? then = null)
    {
        if (string.IsNullOrWhiteSpace(scenarioName)) return;
        var raw = Raw;
        Admit(DoorMove.OpeningScenario(scenarioName), asker, answered, () => { raw.OpenForEdit(scenarioName); then?.Invoke(); });
    }

    /// <summary>Loads <paramref name="scenarioName"/> LIVE (the Orchestrator panel's <i>Load into Live</i>) — the same decision as <see cref="Open"/>.</summary>
    public void LoadLive(string scenarioName, IDoorAsker? asker = null, UnsavedAnswer? answered = null, Action? then = null)
    {
        if (string.IsNullOrWhiteSpace(scenarioName)) return;
        var raw = Raw;
        Admit(DoorMove.LoadingLive(scenarioName), asker, answered, () => { raw.LoadForLive(scenarioName); then?.Invoke(); });
    }

    /// <summary>Opens a recording into AAR (at its start, or at <paramref name="atSimSeconds"/> of its own clock).</summary>
    public void OpenRecording(Guid exerciseId, double? atSimSeconds = null, IDoorAsker? asker = null, UnsavedAnswer? answered = null, Action? then = null)
    {
        if (exerciseId == Guid.Empty) return;   // Empty means "the master picks" on the wire; an open names one
        var raw = Raw;
        Admit(DoorMove.OpeningRecording, asker, answered, () =>
        {
            RecordingOpening?.Invoke(exerciseId);
            if (atSimSeconds is { } at) raw.OpenRecording(exerciseId, at);
            else raw.OpenRecording(exerciseId);
            then?.Invoke();
        });
    }

    // ── OUT ────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ The OUT door: leave the open scenario for Idle. A run is simply stopped (■ never asks); editing with unsaved edits asks <i>Save / Discard / Cancel</i> first — so New Exercise no longer discards silently.
    /// </summary>
    public void Leave(LeaveReason reason, IDoorAsker? asker = null, UnsavedAnswer? answered = null, Action? then = null)
    {
        if (_leave is null) throw new InvalidOperationException("this door has no leave (the host's session did not give one)");
        Admit(DoorMove.Leaving(reason), asker, answered, () => { _leave(reason); then?.Invoke(); });
    }

    // ── the decision ───────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>The decision alone</b> — <paramref name="go"/> runs once <paramref name="move"/> is let through: at once, or when the answer to the question the door asked says so. ⭐ The named verbs above are this with the raw verb as
    /// <paramref name="go"/>; HTTP's <c>POST /scenario/load/live</c> and <c>/replay/stop</c> use it with THEIR OWN publish (their intents carry what the session's raw verbs do not — <c>ForceRecording</c>, the route's reply), so
    /// ⛔ the decision is still not theirs to make twice.
    /// </summary>
    public void Admit(DoorMove move, IDoorAsker? asker, UnsavedAnswer? answered, Action go)
    {
        var runText = move.RunText;
        var what = move.What;
        var proceed = go ?? throw new ArgumentNullException(nameof(go));
        var a = asker ?? _asker;
        if (ReferenceEquals(a, _asker) && IsBusy) return;   // a question is already open: the move is not made twice

        var mode = _mode();
        if (runText is not null && RunModeRunGuard.Of(mode) is { } run)
        {
            a.Ask(new DoorQuestion(DoorQuestionKind.StopAndLoad, RunModeRunGuard.Title, runText(run),
                                   new[] { new RunModeConfirmation.Button(RunModeRunGuard.StopAndLoad, proceed) }, run));
            return;
        }
        if (mode != TransportMode.Edit || a.DiscardsUnsaved) { proceed(); return; }   // nothing of a scenario is being edited (a run holds none; the master refuses a round there)

        void Decide(UnsavedAnswer answer)
        {
            switch (answer)
            {
                case UnsavedAnswer.Unchanged:
                    proceed();
                    break;
                case UnsavedAnswer.Changed:
                    a.Ask(new DoorQuestion(DoorQuestionKind.Unsaved, "Confirm", $"This scenario has unsaved changes. {what} leaves editing.",
                        new[]
                        {
                            new RunModeConfirmation.Button("Save", () => { _raw?.SaveCurrent(); proceed(); }),
                            new RunModeConfirmation.Button("Discard", proceed),
                        }));
                    break;
                case UnsavedAnswer.NeverSaved:
                    a.Ask(new DoorQuestion(DoorQuestionKind.NeverSaved, "Confirm", $"This scenario has never been saved. {what} discards it.",
                        new[] { new RunModeConfirmation.Button("Discard", proceed) }));
                    break;
            }
        }

        if (answered is { } known) { Decide(known); return; }
        _asking = true;
        _unsaved.Ask(answer => { _asking = false; Decide(answer); });
    }
}
