using System;
using Fdp.Toolkit.Runner;
using Hrot.Common.Infrastructure;
using Hrot.Presentation.Windows;

namespace Hrot.Editor.AiShared.Rules;

/// <summary>
/// ⭐⭐ CE-4292 / R3 (docs/UX/DESIGN_Rules_Bundle.md §2, §3) — THE rules windows as ONE bundle every host composes: <b>Rules</b>, <b>Mission summary</b>,
/// <b>Trainee debrief</b>, <b>Game summary</b> and — where the host can create entities and is not showing a recording — <b>Game master</b>. Editing is
/// the SEAT's decision (<see cref="RuleEditorServicesFactory"/>), never the host's. Ids stay <c>&lt;prefix&gt;rules</c>, … (the hand-written sites' ids).
/// The windows are built in the constructor so a host can hand <see cref="Rules"/> to its Details bundle before composing (as <c>EntityDetailsBundle</c>).
/// </summary>
public sealed class RulesBundle : IUiBundle
{
    private readonly RulesHostServices _host;

    public RulesBundle(RulesHostServices host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        string p = host.IdPrefix;
        Func<string?> blocked = host.Blocked ?? (() => null);
        Editor = RuleEditorServicesFactory.Build(host);
        Rules = new RulesWindow(host.World, host.TimelineSource, host.Cursor(), p + "rules", host.Perspective, host.Colour,
            selectEntity: host.SelectEntity, showOnMap: host.ShowOnMap, blocked: host.Blocked, editor: Editor) { IsOpen = false };
        MissionSummary = new MissionSummaryWindow(host.World, host.TimelineSource, host.Cursor(), p + "mission_summary", host.Perspective, host.Colour,
            showMoments: n => { Rules.ShowSubtree(n); _windows?.FocusWindow(Rules.Id); }, blocked: host.Blocked) { IsOpen = false };
        TraineeDebrief = new TraineeDebriefWindow(host.World, host.TimelineSource, host.Cursor(), p + "trainee_debrief", host.Perspective, host.Colour) { IsOpen = false };
        GameSummary = new GameSummaryWindow(host.World, host.TimelineSource, host.Cursor(), p + "game_summary", host.Perspective, host.Colour) { IsOpen = false };
        if (!host.Recording && !host.TraineeOnly && Editor != null && host.EntityCreation != null)
            GameMaster = new GameMasterWindow(host.World, p + "game_master", host.Perspective, host.Colour, Editor,
                createOneShot: CreateOneShot,
                selection: host.Selection, blocked: blocked, enabled: host.GamePaletteEnabled) { IsOpen = false };
        BindMenuActions();
        WireInstructorEnd();
    }

    /// <summary>⭐ The ONE way this host creates a one-shot trigger (the Game master's run-now, the instructor's End without a rule): a transient trigger entity
    /// through the host's creation path. False when the host cannot create entities or shows a recording.</summary>
    private bool CreateOneShot(string json)
    {
        if (_host.Recording || _host.EntityCreation?.Invoke() is not { } creation) return false;
        creation.RequestEntityCreation(Hrot.UI.Common.Rules.RuleAdd.TkbOf(Hrot.UI.Common.Rules.RuleAdd.TriggerKind), isTransient: true,
            initialComponents: new object[] { new Fdp.Toolkit.Behavior.Components.InitialBrainIntent { Behavior = new Fdp.Toolkit.Behavior.Components.SavedBrainSlot { Name = Hrot.UI.Common.Rules.RuleAdd.TriggerKind, Params = json } } });
        return true;
    }

    /// <summary>⭐ CE-4322 (L-3, LC-17) — End ALWAYS works for an Instructor / Author: both End controls (Rules toolbar, Mission summary) may end a scenario with no
    /// <c>instructor-end</c> rule through <see cref="CreateOneShot"/> (the seat decides, as for editing rules), and the host's transport gets the Rules window's End
    /// as its ender, so ■ in Live asks "End the exercise? / Keep running".</summary>
    private void WireInstructorEnd()
    {
        if (_host.Recording || _host.EntityCreation == null) return;
        foreach (var end in new[] { Rules.End, MissionSummary.End })
        {
            end.CreateOneShot = CreateOneShot;
            end.SeatRefusal = () => RuleEditorServicesFactory.SeatRefusal(_host.Seat());
            end.RunRefusal = () => Hrot.UI.Common.Rules.InstructorEnd.RunRefusal(_host.TimeTransport?.Invoke());   // ⭐ U-5 (CE-4335) — End only in a running Live exercise, ONE rule for both controls
        }
        AttachEnder();
    }

    /// <summary>The host's transport, when it exists, ends the exercise through the Rules window's End (idempotent; called at construction and at compose).</summary>
    private void AttachEnder()
    {
        if (!_host.Recording && _host.EntityCreation != null && _host.TimeTransport?.Invoke() is { } transport && transport.Ender == null) transport.Ender = Rules.End;
    }

    private Fdp.Presentation.WindowManager.WindowManager? _windows;

    /// <summary>The rule editor services this host's seat gets (null on a recording).</summary>
    public Hrot.UI.Common.Rules.RuleEditorServices? Editor { get; }
    public RulesWindow Rules { get; }
    public MissionSummaryWindow MissionSummary { get; }
    public TraineeDebriefWindow TraineeDebrief { get; }
    public GameSummaryWindow GameSummary { get; }
    /// <summary>Null where the host cannot create entities or shows a recording.</summary>
    public GameMasterWindow? GameMaster { get; }

    /// <inheritdoc/>
    public string Name => "rules";

    /// <inheritdoc/>
    public void RegisterInto(UiBundleContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        _windows = ctx.Windows;
        if (_host.TraineeOnly)
        {
            // ⭐ L-2 (LC-18) + R-6 / CE-3418 (D32) — the Player: its OWN summary only (no rules, no instructor's summary): the trainee debrief (training profile) and the game summary (game profile), opened when the exercise ends
            ctx.Windows.RegisterWindow(TraineeDebrief);
            ctx.Windows.RegisterWindow(GameSummary);
            AttachObserver();
            ctx.Windows.RegisterFrameOverlay(WatchTheRun);
            return;
        }
        ctx.Windows.RegisterWindow(Rules);
        if (GameMaster != null) ctx.Windows.RegisterWindow(GameMaster);
        ctx.Windows.RegisterWindow(MissionSummary);
        ctx.Windows.RegisterWindow(TraineeDebrief);
        ctx.Windows.RegisterWindow(GameSummary);
        if (!_host.Recording) ctx.Windows.RegisterFrameOverlay(WatchTheRun);
        AttachEnder();   // ⭐ CE-4322 — the transport may exist only by now   // ⭐ L-2 — every frame, on every host that composes the bundle (a recording opens nothing)
    }

    // ── ⭐ L-2 / CE-4320 (Scenario lifecycle LC-15, R-4166) — Mission summary opens when Live STARTS and again when the exercise ENDS, on EVERY host that composes
    //    this bundle (was the editor's alone, CE-4240). Never on first sight (a host joining a running exercise is not "starting" it); 0 B per frame. ──
    private Hrot.Common.Editing.EditingMode? _lastMode;
    private Fdp.Toolkit.ScenarioRules.ExercisePhase _lastPhase = Fdp.Toolkit.ScenarioRules.ExercisePhase.Running;
    private Fdp.Core.EntityRepository? _resultWorld;
    private Fdp.Core.EntityQuery? _results;

    /// <summary>How many times the watcher opened Mission summary (a rail reads it).</summary>
    public int SummaryOpenings { get; private set; }

    /// <summary>The per-frame watch (a frame overlay that draws nothing): the station entering Live, the exercise going Running → Ended.</summary>
    public void WatchTheRun()
    {
        if (_host.World() is not { } w) return;
        var mode = Hrot.Common.Editing.StationMode.Of(w);
        var phase = PhaseOf(w);
        bool started = _lastMode is { } was && was != Hrot.Common.Editing.EditingMode.Live && mode == Hrot.Common.Editing.EditingMode.Live;
        bool ended = phase == Fdp.Toolkit.ScenarioRules.ExercisePhase.Ended && _lastPhase != Fdp.Toolkit.ScenarioRules.ExercisePhase.Ended;
        if (_host.TraineeOnly) { OpenPlayersSummary(w); return; }   // the Player: its own summary, at the end only (R-6)
        else if (started || ended) { _windows?.FocusWindow(MissionSummary.Id); SummaryOpenings++; }
        _lastMode = mode;
        _lastPhase = phase;
    }

    // ── ⭐ R-6 / CE-3418 (AQ108 §0 D32, story ⑩) — the PLAYER's own summary opens the moment the exercise ENDS: the transition into <c>LiveEnded</c> (the run choice's derived fact — Live + the replicated result's
    //    phase Ended — read through the observer ender, no second query; a host with no run controls falls back to the result's phase). Which windows: the scenario's PROFILE (PH-8: a profile is a feature set,
    //    <see cref="HudFeatures.TrainingProfile"/> / <see cref="HudFeatures.GameProfile"/> — the HUD's own answer) → the trainee debrief under training, the game summary under game; a window with nothing to say is not
    //    opened beside one that has, and when NEITHER has content the first profile-on window opens so its why-not shows (never an exercise ending in silence). Once per end; the user may close it. ──
    private bool _lastEnded;

    private void AttachObserver()
    {
        if (_host.TimeTransport?.Invoke() is { } transport && transport.Ender == null) transport.Ender = new Hrot.UI.Common.RunMode.ExerciseObserver(_host.World);
    }

    private void OpenPlayersSummary(Fdp.Core.EntityRepository w)
    {
        AttachObserver();
        bool ended = _host.TimeTransport?.Invoke()?.RunChoice is { } choice ? choice.LiveEnded : PhaseOf(w) == Fdp.Toolkit.ScenarioRules.ExercisePhase.Ended;
        bool rising = ended && !_lastEnded;
        _lastEnded = ended;
        if (!rising) return;
        Func<string, bool> on = _host.FeatureOn ?? (_ => true);
        bool training = HudFeatures.TrainingProfile(on), game = HudFeatures.GameProfile(on);
        bool t = training && TraineeDebrief.HasContent(), g = game && GameSummary.HasContent();
        if (!t && !g) { if (training) t = true; else if (game) g = true; }
        if (t) { _windows?.FocusWindow(TraineeDebrief.Id); SummaryOpenings++; }
        if (g) { _windows?.FocusWindow(GameSummary.Id); SummaryOpenings++; }
    }

    private Fdp.Toolkit.ScenarioRules.ExercisePhase PhaseOf(Fdp.Core.EntityRepository w)
    {
        if (!w.IsComponentTypeRegistered<Fdp.Toolkit.ScenarioRules.ExerciseResult>()) return Fdp.Toolkit.ScenarioRules.ExercisePhase.Running;
        if (!ReferenceEquals(w, _resultWorld))
        { _resultWorld = w; _results = w.Query().With<Fdp.Toolkit.ScenarioRules.ExerciseResult>().WithLifecycle(Fdp.Core.EntityLifecycle.All).Build(); }
        foreach (var e in _results!) return w.GetComponentRO<Fdp.Toolkit.ScenarioRules.ExerciseResult>(e).Phase;   // the one result entity (ExerciseResult.PhaseOf's rule)
        return Fdp.Toolkit.ScenarioRules.ExercisePhase.Running;
    }

    /// <summary>⭐ CE-4308 (RX6, DESIGN_Rule_Editor_UX §2.5) — the rules' map-menu entries (declared by <c>RuleMenuActions</c>) bound HERE, once for every host that composes the
    /// bundle: each opens the Rules window's gallery with the unit as the rule's subject / the area filled in. A host that passes no <see cref="RulesHostServices.Actions"/> has no
    /// handler, so its map greys the row (A2).</summary>
    private void BindMenuActions()
    {
        if (_host.Actions?.Invoke() is not { } actions) return;
        actions.Actions.RegisterLocal(Hrot.Common.Constants.GlobalActionIds.AddRuleAboutUnit, (view, target) => OpenGalleryFor(view, target, area: false));
        actions.Actions.RegisterLocal(Hrot.Common.Constants.GlobalActionIds.AddRuleForArea, (view, target) => OpenGalleryFor(view, target, area: true));
    }

    /// <summary>Opens the Rules window on its gallery, pre-filled with <paramref name="target"/> (a unit as the subject, or an area).</summary>
    internal void OpenGalleryFor(Fdp.ModuleHost.Abstractions.ISimulationView view, Fdp.Core.Entity target, bool area)
    {
        int net = !target.IsNull && view.HasComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>(target)
            ? view.GetComponentRO<Fdp.Toolkit.Replication.Components.NetworkIdentity>(target).Value : 0;
        Rules.IsOpen = true;
        Rules.OpenGallery(subjectNetworkId: area ? 0 : net, areaNetworkId: area ? net : 0);
        _windows?.FocusWindow(Rules.Id);
    }

    /// <summary>RV-7 — the Details panel's Objectives tab opens a rule in the Rules window.</summary>
    public void OpenRule(int networkId)
    {
        Rules.SelectNetworkId(networkId);
        _windows?.FocusWindow(Rules.Id);
    }
}
