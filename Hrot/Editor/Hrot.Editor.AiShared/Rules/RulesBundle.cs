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
        BindMenuActions();
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
