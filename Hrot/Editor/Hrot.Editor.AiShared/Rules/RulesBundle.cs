using System;
using Fdp.Toolkit.Runner;
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
        if (!host.Recording && Editor != null && host.EntityCreation != null)
            GameMaster = new GameMasterWindow(host.World, p + "game_master", host.Perspective, host.Colour, Editor,
                createOneShot: json =>
                {
                    if (host.EntityCreation() is not { } creation) return false;
                    creation.RequestEntityCreation(Hrot.UI.Common.Rules.RuleAdd.TkbOf(Hrot.UI.Common.Rules.RuleAdd.TriggerKind), isTransient: true,
                        initialComponents: new object[] { new Fdp.Toolkit.Behavior.Components.InitialBrainIntent { Behavior = new Fdp.Toolkit.Behavior.Components.SavedBrainSlot { Name = Hrot.UI.Common.Rules.RuleAdd.TriggerKind, Params = json } } });
                    return true;
                },
                selection: host.Selection, blocked: blocked, enabled: host.GamePaletteEnabled) { IsOpen = false };
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
        ctx.Windows.RegisterWindow(Rules);
        if (GameMaster != null) ctx.Windows.RegisterWindow(GameMaster);
        ctx.Windows.RegisterWindow(MissionSummary);
        ctx.Windows.RegisterWindow(TraineeDebrief);
        ctx.Windows.RegisterWindow(GameSummary);
        if (!_host.Recording) ctx.Windows.RegisterFrameOverlay(WatchTheRun);   // ⭐ L-2 — every frame, on every host that composes the bundle (a recording opens nothing)
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
        if (started || ended) { _windows?.FocusWindow(MissionSummary.Id); SummaryOpenings++; }
        _lastMode = mode;
        _lastPhase = phase;
    }

    private Fdp.Toolkit.ScenarioRules.ExercisePhase PhaseOf(Fdp.Core.EntityRepository w)
    {
        if (!w.IsComponentTypeRegistered<Fdp.Toolkit.ScenarioRules.ExerciseResult>()) return Fdp.Toolkit.ScenarioRules.ExercisePhase.Running;
        if (!ReferenceEquals(w, _resultWorld))
        { _resultWorld = w; _results = w.Query().With<Fdp.Toolkit.ScenarioRules.ExerciseResult>().WithLifecycle(Fdp.Core.EntityLifecycle.All).Build(); }
        foreach (var e in _results!) return w.GetComponentRO<Fdp.Toolkit.ScenarioRules.ExerciseResult>(e).Phase;   // the one result entity (ExerciseResult.PhaseOf's rule)
        return Fdp.Toolkit.ScenarioRules.ExercisePhase.Running;
    }

    /// <summary>RV-7 — the Details panel's Objectives tab opens a rule in the Rules window.</summary>
    public void OpenRule(int networkId)
    {
        Rules.SelectNetworkId(networkId);
        _windows?.FocusWindow(Rules.Id);
    }
}
