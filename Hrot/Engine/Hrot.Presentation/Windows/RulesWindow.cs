using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Presentation.Panels.Timeline;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.ReplayBrowser.Timeline;
using Fdp.Toolkit.ScenarioRules;
using Fdp.Toolkit.Squad.Command;
using Hrot.UI.Common.Rules;
using ImGuiNET;

namespace Hrot.Presentation.Windows;

/// <summary>What the <see cref="RulesWindow"/> drew this frame — the rails read it (and an agent reads it through the panel snapshot): the rows (title · state words), the Overview lines and the action buttons with the reason each disabled one carries.</summary>
public sealed record RulesWindowProbe(
    IReadOnlyList<string> Rows, int? Selected, IReadOnlyList<string> Details, IReadOnlyList<(string Label, string? Reason)> Buttons, string GroupBy, string Side, string Search);

/// <summary>The panel snapshot of the rules view.</summary>
public sealed record RulesWindowViewModel(string PanelId, string PanelKind, RulesWindowProbe Shown, string? Message) : IPanelViewModel
{
    /// <inheritdoc/>
    public JsonNode Dump() => PanelDump.Of(this);
}

/// <summary>
/// ⭐⭐ CE-4216 (rules view R2; docs/DESIGN_Rules_View_UI.md §3 RV-3, RV-5, RV-6, §2) — <b>Window ▸ Rules</b>: the scenario's rules as a tree on the ONE timeline panel (<see cref="RuleTrack"/> per row, lanes none), a toolbar (Group by · Side · Follow now · search), and the
/// Overview of the selected row on the right with the instructor's actions. Rows come from <see cref="RulesModel"/> over the replicated rule tree, the past from the timeline source's lanes (<see cref="IndexRulesHistory"/>) — so it opens on a LIVE node and on a RECORDING
/// alike (the source seam). A click on a row selects the row AND its subject (the ONE selection); an action is written through <see cref="RuleCommands"/> — the same path the DebugApi uses. Nothing here owns state the world does not.
/// </summary>
public sealed class RulesWindow : ManagedWindow
{
    /// <summary>The panel kind in the snapshot.</summary>
    public const string Kind = "rules-view";

    private static readonly string[] GroupNames = { "Macro", "Subject", "Side" };
    private static readonly string[] SideNames = { "All sides", "Blue", "Red", "Neutral" };

    private readonly Func<EntityRepository?> _world;
    private readonly Func<ITimelineSource?> _source;
    private readonly ITimelineCursor? _cursor;
    private readonly Func<double>? _now;
    private readonly Func<string?> _blocked;
    private readonly Action<int> _selectEntity, _showOnMap;
    private readonly TimelinePanel _panel = new();
    private readonly RuleTrackHooks _hooks;
    private IReadOnlyList<ITimelineTrack> _tracks = Array.Empty<ITimelineTrack>();
    private RulesView _view = new(Array.Empty<RuleGroup>(), 0);
    private TimelineIndex? _historyOf; private int _historyFrames = -1; private IndexRulesHistory? _history;
    private int _groupBy, _side, _ruleSeq;
    private string _search = "";
    private bool _follow;
    private int? _selected;
    private RulesWindowProbe _probe = new(Array.Empty<string>(), null, Array.Empty<string>(), Array.Empty<(string, string?)>(), "Macro", "All sides", "");

    /// <param name="world">The host's world (a live node's, or a recording's replayed one), or null where it has none. Read each frame.</param>
    /// <param name="source">The timeline source whose <c>objective.state</c> / <c>rules.fired</c> lanes are the rules' past; null = the status's own current span only.</param>
    /// <param name="cursor">The host's timeline cursor (it places the sim-time items on the axis).</param>
    /// <param name="selectEntity">⭐ The ONE selection: a network id (the row's SUBJECT, or a linked entity) — the host publishes <c>SelectEntityCommand</c>.</param>
    /// <param name="showOnMap">Centre the map on a network id — the host publishes <c>CenterOnEntityCommand</c>.</param>
    /// <param name="blocked">Why asking a rule is not possible on this host now (a recording is read-only), or null.</param>
    /// <param name="now">Sim seconds now; null = the world's clock.</param>
    public RulesWindow(Func<EntityRepository?> world, Func<ITimelineSource?> source, ITimelineCursor? cursor, string id, string perspective, Vector4 titleBarColor,
        Action<int> selectEntity, Action<int> showOnMap, Func<string?>? blocked = null, Func<double>? now = null)
        : base(id, "Rules", perspective, WindowScope.PerspectiveBound)
    {
        _world = world; _source = source; _cursor = cursor; _now = now;
        _selectEntity = selectEntity; _showOnMap = showOnMap; _blocked = blocked ?? (() => null);
        _hooks = new RuleTrackHooks(r => Select(r), Ask, r => { if (r.SubjectNetworkId != 0) _showOnMap(r.SubjectNetworkId); }, null);
        _panel.Tracks = () => _tracks;
        _panel.ExtentWithoutItems = true;   // ⭐ RV-3a: authored rows have no spans — the ruler and the headers still draw
        TitleBarColor = titleBarColor;
        IsOpen = true;
        PanelSnapshot.DeclareInstrumented(Id);
    }

    /// <summary>The group-by in force (the toolbar sets it).</summary>
    public RulesGroupBy GroupBy { get => (RulesGroupBy)_groupBy; set => _groupBy = (int)value; }

    /// <summary>The side filter in force; null = all.</summary>
    public Fdp.Core.ForceId? Side
    {
        get => _side == 0 ? null : _side == 1 ? Fdp.Core.ForceId.Friend : _side == 2 ? Fdp.Core.ForceId.Hostile : Fdp.Core.ForceId.Neutral;
        set => _side = value is not { } s ? 0 : s == Fdp.Core.ForceId.Friend ? 1 : s == Fdp.Core.ForceId.Hostile ? 2 : 3;
    }

    public string Search { get => _search; set => _search = value ?? ""; }
    public int? SelectedNetworkId => _selected;

    /// <summary>The last answer to an action (what was written or why it was refused), for the line under the buttons.</summary>
    public string? Message { get; private set; }

    /// <summary>What the last draw showed.</summary>
    public RulesWindowProbe Probe => _probe;

    /// <summary>The timeline panel (the rails read its hit boxes).</summary>
    internal TimelinePanel Panel => _panel;

    /// <summary>⭐ Picks a row: the row is selected and so is its subject (the ONE selection) — nothing is published for a rule with no subject.</summary>
    public void Select(RuleRow row)
    {
        _selected = row.NetworkId;
        if (row.SubjectNetworkId != 0) _selectEntity(row.SubjectNetworkId);
    }

    /// <summary>⭐ RV-7 — the Details panel's Objectives tab opens a rule here: the row is selected (its subject already is the Details' unit).</summary>
    public void SelectNetworkId(int networkId) { _selected = networkId; IsOpen = true; }

    /// <summary>⭐ RV-5 — asks a rule for something through the shared <see cref="RuleCommands"/> write; refused where this host cannot ask (a recording).</summary>
    public void Ask(RuleRow row, RuleRequest request)
    {
        if (_blocked() is { } why) { Message = why; return; }
        var world = _world();
        if (world == null || !world.IsAlive(row.Entity)) { Message = "the rule is gone"; return; }
        var route = RuleCommands.Control(world, row.Entity, request, RuleOrigin.Instructor, ref _ruleSeq, out _);
        Message = route == Fdp.Toolkit.Replication.Patching.EntityWriteRoute.Refused
            ? $"{request}: nothing was written — this node has no way to ask the owner"
            : $"{request}: {(route == Fdp.Toolkit.Replication.Patching.EntityWriteRoute.Direct ? "done here" : "requested of the owner")}";
    }

    /// <summary>⭐ Test hook — the BUILD (view, tracks, probe) with no ImGui context.</summary>
    internal void Rebuild()
    {
        var world = _world();
        double now = _now?.Invoke() ?? (world == null ? 0d : CommandPictureOps.Now(world));
        var index = _source()?.Index;
        if (index != null && (!ReferenceEquals(index, _historyOf) || index.FrameTicks.Count != _historyFrames))
        { _history = new IndexRulesHistory(index); _historyOf = index; _historyFrames = index.FrameTicks.Count; }
        else if (index == null) { _history = null; _historyOf = null; _historyFrames = -1; }
        _view = world == null ? new RulesView(Array.Empty<RuleGroup>(), now) : RulesModel.Build(world, new RulesOptions(GroupBy, Side, _search), _history, now);
        string? block = _blocked();
        var hooks = block == null ? _hooks : _hooks with { Blocked = block };
        var tracks = new List<ITimelineTrack>();
        foreach (var g in _view.Groups)
        {
            tracks.Add(new RuleTrack(g.Name, g.Rows.Count, hooks));
            foreach (var r in g.Rows) tracks.Add(new RuleTrack(r, 1, r.NetworkId == _selected, hooks, now));
        }
        _tracks = tracks;
        var sel = _selected is { } s ? _view.Find(s) : null;
        var details = new List<string>(); var buttons = new List<(string, string?)>();
        if (sel != null)
        {
            foreach (var l in RulesModel.OverviewOf(sel, now)) details.Add($"{l.Label}: {l.Value}");
            foreach (var l in sel.WaitsOn) details.Add($"Waits on: {l.Text}");
            foreach (var l in sel.Starts) details.Add($"Starts: {l.Text}");
            foreach (var a in RulesModel.ActionsOf(sel)) buttons.Add((a.Label, block ?? a.DisabledReason));
            buttons.Add(("Show on map", sel.SubjectNetworkId != 0 ? null : "this rule has no subject"));
        }
        _probe = new RulesWindowProbe(_view.Rows.Select(r => $"{new string(' ', r.Depth * 2)}{r.Title} | {r.StateWords}").ToList(), _selected, details, buttons, GroupNames[_groupBy], SideNames[_side], _search);
    }

    private RulesWindowViewModel BuildAndPublish()
    {
        Rebuild();
        var vm = new RulesWindowViewModel(Id, Kind, _probe, Message);
        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return vm;
    }

    /// <summary>⭐ Test hook — the BUILD + CAPTURE portion, with no live ImGui context.</summary>
    internal RulesWindowViewModel SimulateDrawClientArea() => BuildAndPublish();

    /// <summary>⭐ Test hook — draws the client area inside the caller's ImGui window (no title bar, no atlas).</summary>
    internal void DrawContent() => DrawClientArea();

    /// <summary>⭐ CE-4216 (live check, `2026-11-03`) — first open at a usable size (it opened as a toolbar-only 470×60 box) and opaque
    /// (the map showed through the details pane). <see cref="ImGuiCond.FirstUseEver"/>: a size the user dragged is kept.</summary>
    protected override void BeforeBegin()
    {
        ImGui.SetNextWindowSize(new Vector2(1300f, 720f), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowBgAlpha(1f);
    }

    protected override void DrawClientArea()
    {
        BuildAndPublish();
        DrawToolbar();
        var avail = ImGui.GetContentRegionAvail();
        float details = Math.Min(340f, avail.X * 0.4f);
        ImGui.BeginChild("##rules-timeline", new Vector2(avail.X - details - 6f, avail.Y), ImGuiChildFlags.None, ImGuiWindowFlags.None);
        _panel.Draw(_source(), _cursor, Array.Empty<TimelineLaneDescriptor>());
        ImGui.EndChild();
        ImGui.SameLine();
        ImGui.BeginChild("##rules-details", new Vector2(0, avail.Y), ImGuiChildFlags.Borders, ImGuiWindowFlags.None);
        DrawDetails();
        ImGui.EndChild();
    }

    private void DrawToolbar()
    {
        ImGui.SetNextItemWidth(90f);
        ImGui.Combo("##rules-group", ref _groupBy, GroupNames, GroupNames.Length);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Group by");
        ImGui.SameLine(); ImGui.SetNextItemWidth(100f);
        ImGui.Combo("##rules-side", ref _side, SideNames, SideNames.Length);
        ImGui.SameLine();
        if (ImGui.Checkbox("Follow now", ref _follow) && _follow) _panel.View.Fit();
        ImGui.SameLine(); ImGui.SetNextItemWidth(160f);
        ImGui.InputTextWithHint("##rules-search", "search title", ref _search, 64);
    }

    private void DrawDetails()
    {
        var sel = _selected is { } s ? _view.Find(s) : null;
        if (sel == null) { ImGui.TextDisabled("Select a rule to see its overview."); return; }
        double now = _view.Now;
        foreach (var l in RulesModel.OverviewOf(sel, now))
        {
            ImGui.TextDisabled(l.Label); ImGui.SameLine(110f);
            if (l.Colour != 0) ImGui.TextColored(ImGui.ColorConvertU32ToFloat4(l.Colour), l.Value); else ImGui.TextWrapped(l.Value);
        }
        DrawLinks("Waits on", sel.WaitsOn);
        DrawLinks("Starts", sel.Starts);
        ImGui.Separator();
        string? block = _blocked();
        foreach (var a in RulesModel.ActionsOf(sel))
        {
            string? why = block ?? a.DisabledReason;
            Button(a.Label, why, () => Ask(sel, a.Request));
            ImGui.SameLine();
        }
        Button("Show on map", sel.SubjectNetworkId != 0 ? null : "this rule has no subject", () => _showOnMap(sel.SubjectNetworkId));
        if (Message != null) ImGui.TextDisabled(Message);
    }

    private static void Button(string label, string? why, Action run)
    {
        if (why != null) ImGui.BeginDisabled();
        if (ImGui.Button(label) && why == null) run();
        if (why != null) ImGui.EndDisabled();
        if (why != null && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(why);
    }

    private void DrawLinks(string title, IReadOnlyList<RuleLink> links)
    {
        if (links.Count == 0) return;
        ImGui.TextDisabled(title);
        foreach (var l in links)
        {
            ImGui.SameLine(110f);
            if (ImGui.Selectable($"{l.Text}##{title}{l.NetworkId}", false, ImGuiSelectableFlags.None, new Vector2(0, 0)))
            {
                if (l.IsRule && _view.Find(l.NetworkId) is { } row) Select(row);   // a rule link selects THAT row
                else _selectEntity(l.NetworkId);
            }
        }
    }
}
