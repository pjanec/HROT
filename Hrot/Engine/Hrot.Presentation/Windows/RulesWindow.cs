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
    IReadOnlyList<string> Rows, int? Selected, IReadOnlyList<string> Details, IReadOnlyList<(string Label, string? Reason)> Buttons, string GroupBy, string Side, string Search, RuleDetailsProbe? Editor = null,
    IReadOnlyList<string>? Arrows = null, IReadOnlyList<string>? ComingUp = null);

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
public sealed class RulesWindow : ManagedWindow, IRuleDetailsHost
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
    private readonly InstructorEndControl _end;
    private readonly RuleTrackHooks _hooks;
    private IReadOnlyList<ITimelineTrack> _tracks = Array.Empty<ITimelineTrack>();
    private RulesView _view = new(Array.Empty<RuleGroup>(), 0);
    private TimelineIndex? _historyOf; private int _historyFrames = -1; private IndexRulesHistory? _history;
    private int _groupBy, _side, _ruleSeq;
    private string _search = "";
    private bool _follow;
    private readonly RuleDetails? _details;
    private readonly RuleEditorServices? _editorSvc;
    private (string Title, HashSet<int> Known, string Kind, int Frames)? _addWatch;
    private int _subtree;
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
        Action<int> selectEntity, Action<int> showOnMap, Func<string?>? blocked = null, Func<double>? now = null, RuleEditorServices? editor = null)
        : base(id, "Rules", perspective, WindowScope.PerspectiveBound)
    {
        _world = world; _source = source; _cursor = cursor; _now = now;
        _end = new InstructorEndControl(world, blocked ?? (() => null));   // ⭐ CE-4240 — the instructor's End
        _selectEntity = selectEntity; _showOnMap = showOnMap; _blocked = blocked ?? (() => null);
        _hooks = new RuleTrackHooks(r => Select(r), Ask, r => { if (r.SubjectNetworkId != 0) _showOnMap(r.SubjectNetworkId); }, null,
            CanConnect: editor == null ? null : ConnectRefusal, Connect: editor == null ? null : ConnectRows);   // ⭐ CE-4222 RE-9 — only a host with the rule editor connects
        _editorSvc = editor;
        _details = editor == null ? null : new RuleDetails(editor, world, _blocked, selectEntity, (row, n) => DrawOverviewBody(row));
        if (_details != null)   // ⭐ CE-2423 — Edit / Show history are bound where the host has the rule editor (the Details pane): they select the row and open its tab
            _hooks = _hooks with { Edit = r => Select(r), ShowHistory = r => { Select(r); _details.RequestTab(RuleTab.History); } };
        _panel.Tracks = () => _tracks;
        _panel.ExtentWithoutItems = true;
        _panel.ComingUpCount = 5;                      // ⭐ CE-4221 X5 — the rules view turns Coming up on
        _panel.ComingUpNow = () => _view.Now;   // ⭐ RV-3a: authored rows have no spans — the ruler and the headers still draw
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

    /// <summary>⭐ R5a — the rule editor (null where the host gave no editor services: the pane then shows the Overview only).</summary>
    public RuleDetails? Details => _details;

    /// <summary>⭐ CE-4240 — the instructor's End (toolbar); the Mission summary draws its own over the same trigger.</summary>
    public InstructorEndControl End => _end;

    public string Search { get => _search; set => _search = value ?? ""; }
    public int? SelectedNetworkId => _selected;

    /// <summary>The last answer to an action (what was written or why it was refused), for the line under the buttons.</summary>
    public string? Message { get; private set; }

    /// <summary>What the last draw showed.</summary>
    public RulesWindowProbe Probe => _probe;

    /// <summary>The tracks of the last build (the rails read their links, items and handles).</summary>
    internal IReadOnlyList<ITimelineTrack> TracksForRail => _tracks;

    /// <summary>The timeline panel (the rails read its hit boxes).</summary>
    internal TimelinePanel Panel => _panel;

    /// <summary>⭐ Picks a row: the row is selected and so is its subject (the ONE selection) — nothing is published for a rule with no subject.</summary>
    public void Select(RuleRow row)
    {
        _selected = row.NetworkId;
        if (_detailsInPanel) _selectEntity(row.NetworkId);   // ⭐ U-18 / RX2 — the Details panel shows what is selected: the RULE
        else if (row.SubjectNetworkId != 0) _selectEntity(row.SubjectNetworkId);
    }

    /// <summary>⭐ R3 — <i>Show the moments on the timeline →</i>: only this rule and everything under it (by <c>Parent</c>); <c>0</c> clears. The toolbar shows the filter and a way out.</summary>
    public void ShowSubtree(int networkId) { _subtree = networkId; _selected = networkId == 0 ? _selected : networkId; IsOpen = true; }
    public int SubtreeOf => _subtree;

    /// <summary>⭐ RV-7 — the Details panel's Objectives tab opens a rule here: the row is selected (its subject already is the Details' unit).</summary>
    public void SelectNetworkId(int networkId) { _selected = networkId; IsOpen = true; if (_detailsInPanel) _selectEntity(networkId); }

    // ── ⭐ U-18 / RX2 (DESIGN_Rule_Editor_UX §2.2.1) — the detail in the Details panel, through the ONE RuleDetails ─────────────────────────────

    private bool _detailsInPanel;
    private int _builtFrame = -1;

    /// <summary>Whether the Details panel hosts the rule detail (the window then draws no pane of its own).</summary>
    public bool DetailsInPanel => _detailsInPanel;

    public void AttachToDetailsPanel() => _detailsInPanel = true;

    /// <summary>The view is current for this frame — the window may be closed while Details shows a rule (a rule opened from a unit's Objectives tab).</summary>
    private void EnsureBuilt()
    {
        int f = ImGui.GetCurrentContext() == IntPtr.Zero ? -2 : ImGui.GetFrameCount();
        if (f == _builtFrame && f >= 0) return;
        Rebuild();
        _builtFrame = f;
    }

    private RuleRow? RowOf(Entity e)
    {
        if (_world() is not { } w || !w.IsAlive(e)) return null;
        int net = Fdp.Toolkit.Replication.Services.NetworkIdResolver.RuntimeNetworkIdOf(w, e);
        return net == 0 ? null : _view.Find(net);
    }

    public bool Offers(Entity e)
    {
        EnsureBuilt();
        return RowOf(e) != null;
    }

    public void DrawDetails(Entity e)
    {
        if (RowOf(e) is not { } row) { EnsureBuilt(); if (RowOf(e) is not { } again) return; row = again; }
        if (_selected != row.NetworkId) { _selected = row.NetworkId; _builtFrame = -1; EnsureBuilt(); row = _view.Find(row.NetworkId) ?? row; }   // Prepare runs for the selected row
        if (_details != null) _details.Draw(row, _view.Now); else DrawOverviewBody(row);
    }

    private (Guid Id, string Request)? _control;

    /// <summary>⭐ CE-2380 part 2 — each frame: a refused <i>Fire / Disarm / Re-arm / Reset</i> says why, in the owner's WHOLE words (read from the edit-outcome log by the request id the write returned); an applied one clears.</summary>
    internal void ExpectControl(Guid id, string request) => _control = (id, request);

    internal void PollControl()
    {
        if (_control is not { } c || _world() is not { } w) return;
        if (!Fdp.Toolkit.Replication.Edits.EditOutcomeLog.Of(w).TryGet(c.Id, out var o) || o.Status == Fdp.Toolkit.Replication.Edits.EditStatus.Pending) return;
        Message = o.Status == Fdp.Toolkit.Replication.Edits.EditStatus.Refused ? $"{c.Request}: refused - {o.Reason}" : $"{c.Request}: done";
        _control = null;
    }

    /// <summary>⭐ RV-5 — asks a rule for something through the shared <see cref="RuleCommands"/> write; refused where this host cannot ask (a recording).</summary>
    public void Ask(RuleRow row, RuleRequest request)
    {
        if (_blocked() is { } why) { Message = why; return; }
        var world = _world();
        if (world == null || !world.IsAlive(row.Entity)) { Message = "the rule is gone"; return; }
        var route = RuleCommands.Control(world, row.Entity, request, RuleOrigin.Instructor, ref _ruleSeq, out _, out var requestId);
        _control = (requestId, request.ToString());
        Message = route == Fdp.Toolkit.Replication.Patching.EntityWriteRoute.Refused
            ? $"{request}: nothing was written — this node has no way to ask the owner"
            : $"{request}: {(route == Fdp.Toolkit.Replication.Patching.EntityWriteRoute.Direct ? "done here" : "requested of the owner")}";
    }

    /// <summary>⭐ CE-4222 RE-9 — why dropping <paramref name="from"/> on <paramref name="to"/> is not offered, or null: edit mode (an authored target), a trigger target, not a macro's rule (A1), not a recording.</summary>
    private string? ConnectRefusal(RuleRow from, RuleRow to)
    {
        if (_blocked() is { } b) return b;
        if (to.Status.Role != ObjectiveRole.Trigger) return "only a trigger waits on another rule";
        if (!to.Authored) return "connecting changes the scenario: edit mode only";
        if (to.Macro != null) return $"from macro {to.Macro} - edit the macro or its args";
        return from.NetworkId == to.NetworkId ? "a rule cannot wait on itself" : null;
    }

    private void ConnectRows(RuleRow from, RuleRow to)
    {
        if (_details == null || ConnectRefusal(from, to) != null) return;
        Select(to);
        _details.ConnectInto(to, from);
        Message = _details.Message;
    }

    /// <summary>⭐ CE-4222 RE-8 — why <b>+ Add</b> is not offered now, or null: it needs the host's creation path, edit mode (every row authored — a live insert is a macro insertion, SR-171) and a writable world.</summary>
    public string? AddRefusal()
    {
        if (_blocked() is { } b) return b;
        if (_editorSvc?.CreateRule == null) return "this host cannot create entities";
        return _view.Rows.Any(r => !r.Authored) ? "adding a rule changes the scenario: edit mode only" : null;
    }

    /// <summary>
    /// ⭐ CE-4222 RE-8 — creates a rule of <paramref name="kind"/> through the host's ONE creation path (<c>EntityCreation.RequestEntityCreation</c>): <c>common.title</c> = <i>New &lt;kind&gt;</i>, <c>common.parent</c> = the selected row. The entity arrives a few frames later;
    /// the window then selects it and opens its form (<see cref="WatchAdded"/>). False (and a message) when refused.
    /// </summary>
    public bool AddRule(string kind) => Create(kind, RuleAdd.TitleOf(kind), parent => RuleAdd.NewParamsJson(kind, parent));

    /// <summary>⭐ CE-4308 (RX6) — creates the gallery's starting point <paramref name="t"/> for <paramref name="c"/> through the same ONE path as <see cref="AddRule"/>.</summary>
    public bool AddFromGallery(RuleGallery.Template t, RuleGallery.Context c)
        => Create(t.Kind, RuleGallery.TitleOf(t, c), parent => RuleGallery.ParamsJson(t, c, parent));

    private bool Create(string kind, string title, Func<System.Text.Json.Nodes.JsonNode?, string> paramsFor)
    {
        if (AddRefusal() is { } why) { Message = why; return false; }

        var known = _view.Rows.Select(r => r.NetworkId).ToHashSet();
        var parent = _selected is { } sel && _view.Find(sel) is { } parentRow && _world() is { } w ? RuleConnect.LinkOf(parentRow, AuthoredRules.ScopeOf(w)) : null;   // ⭐ CE-4225 — the STABLE link (the entity's GUID), not a bare network id
        string json = paramsFor(parent);
        if (!_editorSvc!.CreateRule!(RuleAdd.TkbOf(kind), kind, json)) { Message = $"the host could not create a {kind}"; return false; }
        _addWatch = (title, known, kind, 0);
        Message = $"adding {title}…";
        return true;
    }

    /// <summary>⭐ CE-4308 (RX6) — what the gallery is pre-filled with (the unit / area a map menu came from); reset when the gallery closes.</summary>
    private RuleGallery.Context _galleryCtx = RuleGallery.Context.Empty;
    private bool _openGallery;
    private string _gallerySearch = string.Empty;

    /// <summary>The gallery's context while open (rails).</summary>
    public RuleGallery.Context GalleryContext => _galleryCtx;

    /// <summary>⭐ CE-4308 (RX6, §2.5) — opens the gallery on the next frame, pre-filled with the unit (<paramref name="subjectNetworkId"/>, the rule's subject) and / or the
    /// area (<paramref name="areaNetworkId"/>) the author came from (0 = none). The map's <i>Add rule about this unit…</i> / <i>When someone enters this area…</i> land here.</summary>
    public void OpenGallery(int subjectNetworkId = 0, int areaNetworkId = 0)
    {
        var scope = _world() is { } w ? AuthoredRules.ScopeOf(w) : null;
        int phases = _view.Rows.Count(r => r.Status.Phase > 0);
        _galleryCtx = new RuleGallery.Context(RuleGallery.LinkTo(subjectNetworkId, scope), RuleGallery.LinkTo(areaNetworkId, scope), phases + 1);
        _gallerySearch = string.Empty;
        _openGallery = true;
    }

    private RuleRow? AddedRow(string title, HashSet<int> known) => _view.Rows.FirstOrDefault(r => r.Title == title && !known.Contains(r.NetworkId));

    /// <summary>The entity a <see cref="AddRule"/> asked for has appeared (a new row with the new title): select it and open its form on the tab that matters (a trigger's When, an objective's Settings).</summary>
    private void WatchAdded()
    {
        if (_addWatch is not { } w) return;
        var row = AddedRow(w.Title, w.Known);   // ⛔ no lambda over `w` here: its closure would be allocated on EVERY call, watch or not (CE-1465)
        if (row != null)
        {
            Select(row);
            _details?.RequestTab(w.Kind == RuleAdd.TriggerKind ? RuleTab.When : RuleTab.Settings);
            _addWatch = null;
            Message = $"added {row.Title}";
        }
        else _addWatch = w.Frames > 600 ? null : w with { Frames = w.Frames + 1 };
    }

    /// <summary>⭐ CE-4221 (R4) — the arrows: X → the trigger that waits on X (<c>DependsOn</c>) and a rule → what it starts (<c>ActsOn</c>), between the tracks that exist; one arrow per pair.</summary>
    internal static void LinkTracks(IReadOnlyList<ITimelineTrack> tracks)
    {
        var byNet = new Dictionary<int, RuleTrack>();
        foreach (var t in tracks) if (t is RuleTrack { Row: { } r } rt) byNet[r.NetworkId] = rt;
        var done = new HashSet<(int, int)>();
        void Link(RuleTrack from, RuleTrack to)
        {
            if (from.AnchorOut is not { } a || to.AnchorIn is not { } b || from.Row == null || to.Row == null || !done.Add((from.Row.NetworkId, to.Row.NetworkId))) return;
            from.AddLink(new TrackLink(a, to.Id, b));
        }
        foreach (var rt in byNet.Values)
        {
            foreach (var w in rt.Row!.WaitsOn) if (w.IsRule && byNet.TryGetValue(w.NetworkId, out var x)) Link(x, rt);
            foreach (var s in rt.Row!.Starts) if (s.IsRule && byNet.TryGetValue(s.NetworkId, out var y)) Link(rt, y);
        }
    }

    // ── CE-1465: rebuild only when something changed ────────────────────────────────────────────────────────────────────────
    // The tables the model reads (RulesModel.Build and the sentence texts): a write bumps the table's chunk version to the world's GlobalVersion, so "changed since the last build" is HasChanges(that GlobalVersion).
    private static readonly Type[] KeyTables =
    {
        typeof(ObjectiveStatus), typeof(ObjectiveLinks), typeof(Fdp.Toolkit.Behavior.Components.BehaviorStartRecord), typeof(Fdp.Toolkit.Replication.Components.NetworkIdentity),
        typeof(Fdp.Core.EntityInfo), typeof(Fdp.Toolkit.Scenario.MacroOrigin), typeof(Fdp.Toolkit.Scenario.MacroInstance),
    };
    private bool _keyValid;
    private EntityRepository? _keyWorld;
    private readonly bool[] _keyPresent = new bool[KeyTables.Length];
    private uint _keyVersion;
    private int _keyEntities, _keyGroup, _keySide, _keySubtree, _keyMessages;
    private double _keyNow;
    private string _keySearch = "", _keyBlock = "";
    private bool _keyBlocked;
    private int? _keySelected;
    private object? _keyMessageStore;

    /// <summary>⭐ CE-1465 — true when anything the BUILD reads differs from the last build (world · the tables' versions · entity count · time · the toolbar's choices · selection · the blocked reason · the history index · the sent-message count).
    /// ⚠ The edit-outcome log is NOT in the key: nothing the build reads comes from it (<see cref="PollControl"/> reads it every frame). ⚠ A table write inside the SAME <c>GlobalVersion</c> as the last build is seen once the world ticks.</summary>
    private bool KeyChanged(EntityRepository? world, double now, TimelineIndex? index, string? block)
    {
        // ⭐ HasChanges is strictly '>': a write stamped with the build's OWN version (an Apply inside the frame's draw, after the build) is caught by checking
        //   from one below once the world has ticked past it — at most one redundant rebuild per write; a world that never ticks stays steady (0 B).
        uint since = world != null && world.GlobalVersion > _keyVersion && _keyVersion > 0 ? _keyVersion - 1 : _keyVersion;
        if (!_keyValid || !ReferenceEquals(world, _keyWorld) || now != _keyNow || _groupBy != _keyGroup || _side != _keySide || _subtree != _keySubtree || _selected != _keySelected
            || !string.Equals(_search, _keySearch, StringComparison.Ordinal) || (block != null) != _keyBlocked || (block != null && !string.Equals(block, _keyBlock, StringComparison.Ordinal))) return true;
        if (index != null ? (!ReferenceEquals(index, _historyOf) || index.FrameTicks.Count != _historyFrames) : _historyOf != null) return true;
        if (world == null) return false;
        if (world.EntityCount != _keyEntities) return true;
        for (int i = 0; i < KeyTables.Length; i++)
        {
            bool present = world.TryGetTable(KeyTables[i], out var t);
            if (present != _keyPresent[i] || (present && t.HasChanges(since))) return true;   // a table missing at build and present now counts as a change
        }
        var store = Hrot.UI.Common.Map3D.PlayerMessages.Of(world);
        return !ReferenceEquals(store, _keyMessageStore) || (store != null && store.All.Count != _keyMessages);
    }

    private void RememberKey(EntityRepository? world, double now, string? block)
    {
        _keyValid = true; _keyWorld = world; _keyNow = now; _keyGroup = _groupBy; _keySide = _side; _keySubtree = _subtree; _keySelected = _selected; _keySearch = _search;
        _keyBlocked = block != null; _keyBlock = block ?? "";
        if (world == null) return;
        _keyEntities = world.EntityCount; _keyVersion = world.GlobalVersion;
        for (int i = 0; i < KeyTables.Length; i++) _keyPresent[i] = world.TryGetTable(KeyTables[i], out _);
        var store = Hrot.UI.Common.Map3D.PlayerMessages.Of(world);
        _keyMessageStore = store; _keyMessages = store?.All.Count ?? 0;
    }

    /// <summary>⭐ Test hook — the BUILD (view, tracks, probe) with no ImGui context. ⭐ CE-1465: it builds only when <see cref="KeyChanged"/> says something did; the per-frame bits (the added-rule watch, the control poll, the open form's Prepare) always run.</summary>
    internal void Rebuild()
    {
        var world = _world();
        double now = _now?.Invoke() ?? (world == null ? 0d : CommandPictureOps.Now(world));
        var index = _source()?.Index;
        string? block0 = _blocked();
        if (!KeyChanged(world, now, index, block0))
        {
            WatchAdded(); PollControl();
            RefreshPerFrame(now);
            return;
        }
        if (index != null && (!ReferenceEquals(index, _historyOf) || index.FrameTicks.Count != _historyFrames))
        { _history = new IndexRulesHistory(index); _historyOf = index; _historyFrames = index.FrameTicks.Count; }
        else if (index == null) { _history = null; _historyOf = null; _historyFrames = -1; }
        _view = world == null ? new RulesView(Array.Empty<RuleGroup>(), now) : RulesModel.Build(world, new RulesOptions(GroupBy, Side, _search, _subtree), _history, now);
        string? block = block0;
        var hooks = block == null ? _hooks : _hooks with { Blocked = block };
        var tracks = new List<ITimelineTrack>();
        foreach (var g in _view.Groups)
        {
            tracks.Add(new RuleTrack(g.Name, g.Rows.Count, hooks));
            foreach (var r in g.Rows) tracks.Add(new RuleTrack(r, 1, r.NetworkId == _selected, hooks, now));
        }
        WatchAdded(); PollControl();
        LinkTracks(tracks);
        _tracks = tracks;
        var sel = _selected is { } s ? _view.Find(s) : null;
        var details = new List<string>(); var buttons = new List<(string, string?)>();
        if (sel != null)
        {
            foreach (var l in RulesModel.OverviewOf(sel, now)) details.Add($"{l.Label}: {l.Value}");
            foreach (var l in sel.WaitsOn) details.Add($"Waits on: {l.Text}");
            foreach (var l in sel.Starts) details.Add($"Starts: {l.Text}");
            foreach (var l in MessageDelivery.LinesOf(world, sel.NetworkId)) details.Add($"Sent: {l}");   // ⭐ CE-4241
            foreach (var a in ActionRowsOf(sel, block)) buttons.Add((a.Label, a.Why));
        }
        _probe = new RulesWindowProbe(_view.Rows.Select(r => $"{new string(' ', r.Depth * 2)}{r.Title} | {r.StateWords}").ToList(), _selected, details, buttons, GroupNames[_groupBy], SideNames[_side], _search, null,
            _panel.LastConnectors.Select(c => $"{c.FromTrack} -> {c.ToTrack}").ToList(), _panel.LastComingUp.Select(c => $"{c.At:0} {c.TrackLabel} {c.ItemLabel}").ToList());   // ⭐ CE-4221 — what the last frame drew (an agent reads it)
        if (_details != null && sel != null) { _details.Prepare(sel, now); _probe = _probe with { Editor = _details.Probe }; }
        RememberKey(world, now, block);
    }

    /// <summary>What a steady frame still does: the open form's Prepare (it holds the person's edit state) and the arrows / coming-up lists the timeline drew last frame (they appear after the first draw).</summary>
    private void RefreshPerFrame(double now)
    {
        var sel = _selected is { } s ? _view.Find(s) : null;
        if (_details != null && sel != null) { _details.Prepare(sel, now); _probe = _probe with { Editor = _details.Probe }; }
        if (_panel.LastConnectors.Count != (_probe.Arrows?.Count ?? 0) || _panel.LastComingUp.Count != (_probe.ComingUp?.Count ?? 0))
            _probe = _probe with { Arrows = _panel.LastConnectors.Select(c => $"{c.FromTrack} -> {c.ToTrack}").ToList(), ComingUp = _panel.LastComingUp.Select(c => $"{c.At:0} {c.TrackLabel} {c.ItemLabel}").ToList() };
    }

    private RulesWindowViewModel? _vm;

    private RulesWindowViewModel BuildAndPublish()
    {
        Rebuild();
        // ⭐ CE-1465 — the view model is reused while the probe and the message are the same objects
        var vm = _vm is { } v && ReferenceEquals(v.Shown, _probe) && string.Equals(v.Message, Message, StringComparison.Ordinal) ? v : (_vm = new RulesWindowViewModel(Id, Kind, _probe, Message));
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

    /// <summary>⭐ CE-4220 (R5b, RE-3 live) — a closed window wants no live leaf values: the node feature the When tab switched on goes off again.</summary>
    protected override void OnRenderWhileClosed() => _details?.Idle();

    protected override void DrawClientArea()
    {
        BuildAndPublish();
        _builtFrame = ImGui.GetFrameCount();
        DrawToolbar();
        var avail = ImGui.GetContentRegionAvail();
        float details = _detailsInPanel ? 0f : Math.Min(_details != null ? 560f : 340f, avail.X * (_details != null ? 0.5f : 0.4f));
        ImGui.BeginChild("##rules-timeline", new Vector2(_detailsInPanel ? avail.X : avail.X - details - 6f, avail.Y), ImGuiChildFlags.None, ImGuiWindowFlags.None);
        if (ListView) DrawList();   // ⭐ U-18 / RX3 — the List view: one sentence per rule
        else _panel.Draw(_source(), _cursor, Array.Empty<TimelineLaneDescriptor>());
        ImGui.EndChild();
        if (_detailsInPanel) return;   // ⭐ U-18 / RX2 — the detail lives in the Details panel (⛔ two copies of the form)
        ImGui.SameLine();
        ImGui.BeginChild("##rules-details", new Vector2(0, avail.Y), ImGuiChildFlags.Borders, ImGuiWindowFlags.None);
        DrawDetails();
        ImGui.EndChild();
    }

    // ── ⭐ U-18 / RX3 (DESIGN_Rule_Editor_UX §2.1) — the List view ───────────────────────────────────────────────────────────

    private bool? _listView;

    /// <summary>List (one sentence per rule — the authoring default in edit mode) or Timeline (the default with a live run or a recording). The person's pick sticks.</summary>
    public bool ListView
    {
        get => _listView ?? AllAuthored();
        set => _listView = value;
    }

    /// <summary>No live or recorded row (index loops — the getter runs every frame, 0 B).</summary>
    private bool AllAuthored()
    {
        var gs = _view.Groups;
        for (int i = 0; i < gs.Count; i++) { var rs = gs[i].Rows; for (int j = 0; j < rs.Count; j++) if (!rs[j].Authored) return false; }
        return true;
    }

    private readonly Dictionary<int, (string Key, string Text, bool Incomplete, RuleSentence.Seg[] Segs)> _sentences = new();
    private static readonly RuleSentence.Seg[] NoSegs = Array.Empty<RuleSentence.Seg>();

    /// <summary>A rule's line (text + segments), re-built only when its params change (the list draws every frame).</summary>
    internal (string Text, bool Incomplete, RuleSentence.Seg[] Segs) LineOf(RuleRow row)
    {
        var p = RuleSentence.ParamsOf(_world(), row.Entity);
        string key = p is { } k ? k.Json : row.Title;
        if (_sentences.TryGetValue(row.NetworkId, out var c) && ReferenceEquals(c.Key, key)) return (c.Text, c.Incomplete, c.Segs);
        if (p is not { } prm) { var plain = (key, row.Title, false, new[] { new RuleSentence.Seg(row.Title, false) }); _sentences[row.NetworkId] = plain; return (plain.Item2, false, plain.Item4); }
        var refs = _world() is { } w ? AuthoredRules.ScopeOf(w) : null;
        var (text, incomplete, segs) = RuleSentence.Parts(prm.Name, prm.Json, row.Title, row.Status.Required != 0, refs);
        _sentences[row.NetworkId] = (key, text, incomplete, segs);
        return (text, incomplete, segs);
    }

    /// <summary>A rule's line, re-built only when its params change (the list draws every frame).</summary>
    internal (string Text, bool Incomplete) SentenceOf(RuleRow row) { var l = LineOf(row); return (l.Text, l.Incomplete); }

    /// <summary>What the List view shows, line by line (rails, the panel probe).</summary>
    internal IReadOnlyList<string> ListRows => _view.Rows.Select(r => SentenceOf(r).Text).ToList();

    /// <summary>Whether the List marks rule <paramref name="networkId"/> incomplete (rails).</summary>
    internal bool IncompleteOf(int networkId) => _view.Find(networkId) is { } r && SentenceOf(r).Incomplete;

    /// <summary>What the last List frame drew for one row — the kind glyph and its colour, the muted <i>required</i>, the incomplete ✖ and where the text and the mark started (rails: the mockup's measurable points).</summary>
    internal readonly record struct ListMark(int NetworkId, string Glyph, Vector4 Colour, bool Required, bool Incomplete, float TextX, float MarkX);
    private readonly List<ListMark> _listMarks = new();
    internal IReadOnlyList<ListMark> ListMarks => _listMarks;

    // the kind glyph in front of a rule (rule-list-visual.svg): trigger = bolt amber · objective = bullseye green · measure = flag amber · else a neutral dot
    private static (string Glyph, Vector4 Colour) KindGlyph(ObjectiveRole role) => role switch
    {
        ObjectiveRole.Trigger   => ("\uf0e7", NodeEditor.Core.Widgets.IconPalette.Amber),
        ObjectiveRole.Objective => ("\uf140", NodeEditor.Core.Widgets.IconPalette.Green),
        ObjectiveRole.Measure   => ("\uf11e", NodeEditor.Core.Widgets.IconPalette.Amber),
        _                       => ("\uf111", NodeEditor.Core.Widgets.IconPalette.Muted),
    };
    private const string ArrowDrawn = " \uf061 ";   // Font Awesome arrow-right: the UI font has no U+2192
    private readonly Dictionary<int, string> _countText = new();
    private readonly List<string> _groupIds = new();

    private string CountText(int n)
    {
        if (!_countText.TryGetValue(n, out var t)) _countText[n] = t = n == 1 ? "1 rule" : $"{n} rules";
        return t;
    }

    private void DrawList()
    {
        _listMarks.Clear();
        var gs = _view.Groups;
        bool any = false;
        float fs = ImGui.GetFontSize(), scale = fs / 13f;
        var dl = ImGui.GetWindowDrawList();
        uint muted = ImGui.GetColorU32(NodeEditor.Core.Widgets.IconPalette.Muted);
        for (int gi = 0; gi < gs.Count; gi++)
        {
            var g = gs[gi];
            var rs = g.Rows;
            if (g.Name.Length > 0)
            {
                // the shared collapsible header bar (U-5), the count right-aligned and muted
                while (_groupIds.Count <= gi) _groupIds.Add("rules-group-" + _groupIds.Count);
                bool phasesGroup = g.Name == RulesModel.PhasesGroup;
                bool open = NodeEditor.Core.Widgets.SectionHeader.Draw(_groupIds[gi], g.Name, phasesGroup ? "rule/phase" : "tab/rules", ImGuiTreeNodeFlags.DefaultOpen);
                string count = CountText(rs.Count);
                var hmin = ImGui.GetItemRectMin(); var hmax = ImGui.GetItemRectMax();
                dl.AddText(new Vector2(hmax.X - ImGui.CalcTextSize(count).X - 8f * scale, hmin.Y + (hmax.Y - hmin.Y - fs) * 0.5f), muted, count);
                if (!open) { any |= rs.Count > 0; continue; }
            }
            bool phases = g.Name == RulesModel.PhasesGroup;
            for (int ri = 0; ri < rs.Count; ri++)
            {
                var row = rs[ri];
                any = true;
                if (phases) { DrawPhaseRow(row, scale, dl, muted); continue; }   // ⭐ W-1 (P-8) — mission-plan-detail-visual.svg B
                var (text, incomplete, segs) = LineOf(row);
                ImGui.PushID(row.NetworkId);
                // ONE click target over the whole row; the sentence is drawn over it (no widget of its own steals the click)
                if (ImGui.Selectable("##row", _selected == row.NetworkId, ImGuiSelectableFlags.AllowOverlap)) Select(row);
                var rmin = ImGui.GetItemRectMin(); var rmax = ImGui.GetItemRectMax();
                float x = rmin.X + (8f + row.Depth * 14f) * scale, textX = x + 22f * scale;
                var (glyph, glyphColour) = KindGlyph(row.Status.Role);
                ImGui.SetCursorScreenPos(new Vector2(x, rmin.Y));
                ImGui.TextColored(glyphColour, glyph);
                ImGui.SetCursorScreenPos(new Vector2(textX, rmin.Y));
                for (int si = 0; si < segs.Length; si++)
                {
                    if (si > 0) ImGui.SameLine(0f, 0f);
                    var seg = segs[si];
                    string shown = seg.Grammar && ReferenceEquals(seg.Text, RuleSentence.Arrow) ? ArrowDrawn : seg.Text;
                    if (seg.Grammar) ImGui.TextColored(NodeEditor.Core.Widgets.IconPalette.Info, shown); else ImGui.TextUnformatted(shown);
                }
                float right = rmax.X - 10f * scale;
                float markX = 0f;
                if (incomplete)
                {
                    var mark = NodeEditor.Core.Widgets.Icons.Get("form/error");   // the red ✖ on the RIGHT
                    var msz = ImGui.CalcTextSize(mark.Glyph);
                    markX = right - msz.X;
                    dl.AddText(new Vector2(markX, rmin.Y), ImGui.GetColorU32(mark.Color), mark.Glyph);
                    if (ImGui.IsMouseHoveringRect(new Vector2(markX, rmin.Y), new Vector2(right, rmax.Y))) ImGui.SetTooltip("incomplete — a blank is not filled yet");
                    right = markX - 10f * scale;
                }
                bool required = row.Status.Required != 0;
                if (required) dl.AddText(new Vector2(right - ImGui.CalcTextSize("required").X, rmin.Y), muted, "required");
                _listMarks.Add(new ListMark(row.NetworkId, glyph, glyphColour, required, incomplete, textX, markX));
                ImGui.SetCursorScreenPos(new Vector2(rmin.X, rmax.Y));
                ImGui.Dummy(Vector2.Zero);   // ImGui asserts when the cursor was moved and no item follows; this also puts the next row one ItemSpacing down
                ImGui.PopID();
            }
        }
        if (!any) ImGui.TextDisabled("No rules yet.");
    }

    // ── ⭐ W-1 (P-8, Mission plan §5.5 M6, D5; mission-plan-detail-visual.svg B) — a phase row: ⚑ N  Title   begins …   [▶ Begin now] / begun m:ss / waiting ──

    private readonly Dictionary<int, (string Key, string Number, string Begins)> _phaseLines = new();
    private readonly Dictionary<double, string> _begunText = new();
    private static readonly Vector4 BegunGreen = new(0.36f, 0.82f, 0.42f, 1f);

    /// <summary>A phase row's number and <i>"begins …"</i>, re-built only when its params change (the list draws every frame).</summary>
    internal (string Number, string Begins) PhaseLineOf(RuleRow row)
    {
        var p = RuleSentence.ParamsOf(_world(), row.Entity);
        string key = p is { } k ? k.Json : row.Title;
        if (_phaseLines.TryGetValue(row.NetworkId, out var c) && ReferenceEquals(c.Key, key)) return (c.Number, c.Begins);
        var refs = _world() is { } w ? AuthoredRules.ScopeOf(w) : null;
        string begins = p is { } prm ? RuleSentence.PhaseBegins(prm.Json, row.Status.Deadline, refs) : "";
        var line = (key, row.Status.Phase.ToString(System.Globalization.CultureInfo.InvariantCulture), begins);
        _phaseLines[row.NetworkId] = line;
        return (line.Item2, line.Item3);
    }

    /// <summary>What a phase row offers on its right (rails): <i>Begin now</i> in a run while it has not begun (greyed with the reason when the rule refuses
    /// a fire), <i>begun m:ss</i> once it has, <i>waiting</i> otherwise; nothing before a run (an authored row).</summary>
    internal static (bool BeginNow, string? Why, string? State) PhaseRightOf(RuleRow row)
    {
        if (row.Authored) return (false, null, null);
        if (row.Status.FireCount > 0) return (false, null, "begun " + RulesModel.Clock(row.Status.LastFired));
        var why = RuleCommands.DisabledReason(row.Status, Fdp.Toolkit.ScenarioRules.RuleRequest.Fire);
        return (true, why, null);
    }

    private void DrawPhaseRow(RuleRow row, float scale, ImDrawListPtr dl, uint muted)
    {
        var (number, begins) = PhaseLineOf(row);
        ImGui.PushID(row.NetworkId);
        if (ImGui.Selectable("##row", _selected == row.NetworkId, ImGuiSelectableFlags.AllowOverlap)) Select(row);
        var rmin = ImGui.GetItemRectMin(); var rmax = ImGui.GetItemRectMax();
        float x = rmin.X + 8f * scale, textX = x + 22f * scale;
        var flag = NodeEditor.Core.Widgets.Icons.Get("rule/phase");
        ImGui.SetCursorScreenPos(new Vector2(x, rmin.Y));
        ImGui.TextColored(flag.Color, flag.Glyph);
        ImGui.SetCursorScreenPos(new Vector2(textX, rmin.Y));
        ImGui.TextUnformatted(number);
        ImGui.SetCursorScreenPos(new Vector2(textX + 16f * scale, rmin.Y));
        ImGui.TextUnformatted(row.Title);
        ImGui.SetCursorScreenPos(new Vector2(textX + 136f * scale, rmin.Y));   // the "begins …" column (the mockup's second column)
        ImGui.TextColored(NodeEditor.Core.Widgets.IconPalette.Muted, begins);
        var (beginNow, why, state) = PhaseRightOf(row);
        float right = rmax.X - 10f * scale;
        if (state != null)
        {
            var sz = ImGui.CalcTextSize(state);
            dl.AddText(new Vector2(right - sz.X, rmin.Y), ImGui.GetColorU32(BegunGreen), state);
        }
        else if (beginNow)
        {
            string label = $"{Fdp.Presentation.Fonts.IconsFontAwesome6.Play} Begin now";
            float w = ImGui.CalcTextSize(label).X + ImGui.GetStyle().FramePadding.X * 2;
            float waitW = ImGui.CalcTextSize("waiting").X;
            dl.AddText(new Vector2(right - waitW, rmin.Y), muted, "waiting");
            ImGui.SetCursorScreenPos(new Vector2(right - waitW - 16f * scale - w, rmin.Y - ImGui.GetStyle().FramePadding.Y));
            ImGui.BeginDisabled(why != null);
            if (ImGui.SmallButton(label)) Ask(row, Fdp.Toolkit.ScenarioRules.RuleRequest.Fire);   // ⭐ D5 — Begin now IS the trigger's Fire, the instructor's (RuleOrigin.Instructor)
            ImGui.EndDisabled();
            if (why != null && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(why);
        }
        _listMarks.Add(new ListMark(row.NetworkId, flag.Glyph, flag.Color, false, false, textX, 0f));
        ImGui.SetCursorScreenPos(new Vector2(rmin.X, rmax.Y));
        ImGui.Dummy(Vector2.Zero);
        ImGui.PopID();
    }

    private void DrawToolbar()
    {
        // ⭐ U-18 / RX3 — List | Timeline (two views of the same rows)
        bool list = ListView;
        if (NodeEditor.Core.Widgets.GlyphWidgets.GlyphToggle("##rules-list", new NodeEditor.Core.Widgets.IconGlyph("\uf03a", NodeEditor.Core.Widgets.IconPalette.Blue), list, "List: one sentence per rule")) _listView = true;   // List
        ImGui.SameLine(0f, 2f);
        if (NodeEditor.Core.Widgets.GlyphWidgets.GlyphToggle("##rules-timeline", new NodeEditor.Core.Widgets.IconGlyph("\uf1da", NodeEditor.Core.Widgets.IconPalette.Amber), !list, "Timeline")) _listView = false;   // ClockRotateLeft
        ImGui.SameLine();
        ImGui.SetNextItemWidth(90f);
        ImGui.Combo("##rules-group", ref _groupBy, GroupNames, GroupNames.Length);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Group by");
        ImGui.SameLine(); ImGui.SetNextItemWidth(100f);
        ImGui.Combo("##rules-side", ref _side, SideNames, SideNames.Length);
        ImGui.SameLine();
        if (ImGui.Checkbox("Follow now", ref _follow) && _follow) _panel.View.Fit();
        ImGui.SameLine(); ImGui.SetNextItemWidth(160f);
        ImGui.InputTextWithHint("##rules-search", "search title", ref _search, 64);
        if (_details != null)   // ⭐ CE-4222 RE-8 — + Add (edit mode)
        {
            ImGui.SameLine();
            string? refusal = AddRefusal();
            if (refusal != null) ImGui.BeginDisabled();
            if (ImGui.Button("+ Add##rules-add")) OpenGallery();
            if (refusal != null) { ImGui.EndDisabled(); if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(refusal); }
            if (_openGallery) { _openGallery = false; ImGui.OpenPopup("##rules-add-menu"); }
            if (ImGui.BeginPopup("##rules-add-menu")) { DrawGallery(); ImGui.EndPopup(); }
            else if (!_galleryCtx.Equals(RuleGallery.Context.Empty) && !_openGallery) _galleryCtx = RuleGallery.Context.Empty;
        }
        ImGui.SameLine();
        _end.Draw("rules-end");   // ⭐ CE-4240 — never hidden: disabled with the reason when the scenario has no instructor-end trigger
        if (_subtree != 0)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton($"only #{_subtree} and below  ✕##rules-subtree")) _subtree = 0;   // ⭐ R3 — the filter the summary set, with its way out
        }
    }

    /// <summary>⭐ CE-4308 (RX6, rule-list-visual.svg "New rule — choose a starting point") — the gallery: a search, the groups, each starting point; the engine's
    /// kinds under <i>More…</i> (nothing removed). The rows drawn last frame, for rails: <see cref="GalleryRows"/>.</summary>
    private void DrawGallery()
    {
        ImGui.TextDisabled("New rule - choose a starting point");
        ImGui.SetNextItemWidth(260f);
        ImGui.InputTextWithHint("##rules-gallery-search", "search", ref _gallerySearch, 48);
        var shown = RuleGallery.For(_galleryCtx, _gallerySearch);
        _galleryRows.Clear();
        string? group = null;
        foreach (var t in shown)
        {
            if (t.Group != group) { group = t.Group; ImGui.SeparatorText(group); }
            _galleryRows.Add(t.Label);
            if (ImGui.MenuItem(t.Label)) { AddFromGallery(t, _galleryCtx); ImGui.CloseCurrentPopup(); }
        }
        ImGui.Separator();
        if (ImGui.BeginMenu("More... (every kind)"))
        {
            foreach (var kind in RuleAdd.Kinds()) if (ImGui.MenuItem(kind)) { AddRule(kind); ImGui.CloseCurrentPopup(); }
            ImGui.EndMenu();
        }
    }

    private readonly List<string> _galleryRows = new();
    /// <summary>The gallery rows drawn last frame (rails).</summary>
    public IReadOnlyList<string> GalleryRows => _galleryRows;

    private void DrawDetails()
    {
        var sel = _selected is { } s ? _view.Find(s) : null;
        if (sel == null) { ImGui.TextDisabled("Select a rule to see its overview."); return; }
        if (_details != null) _details.Draw(sel, _view.Now); else DrawOverviewBody(sel);
    }

    /// <summary>The Overview / Status tab: the RV-6 lines, the links, the RV-5 buttons.</summary>
    private void DrawOverviewBody(RuleRow sel)
    {
        double now = _view.Now;
        foreach (var l in RulesModel.OverviewOf(sel, now))
        {
            ImGui.TextDisabled(l.Label); ImGui.SameLine(110f);
            if (l.Colour != 0) ImGui.TextColored(ImGui.ColorConvertU32ToFloat4(l.Colour), l.Value); else ImGui.TextWrapped(l.Value);
        }
        DrawLinks("Waits on", sel.WaitsOn);
        DrawLinks("Starts", sel.Starts);
        var sent = MessageDelivery.LinesOf(_world(), sel.NetworkId);   // ⭐ CE-4241 — what went to whom
        if (sent.Count > 0) { ImGui.TextDisabled("Sent"); foreach (var l in sent) { ImGui.SameLine(110f); ImGui.TextWrapped(l); } }
        ImGui.Separator();
        foreach (var a in ActionRowsOf(sel, _blocked()))
        {
            Button(a.Label, a.Why, a.Run);
            ImGui.SameLine();
        }
        if (Message != null) ImGui.TextDisabled(Message);
    }

    private (RuleRow Row, string? Block, bool Editor, List<RuleActionBinding.Row> Rows)? _actionRows;

    /// <summary>⭐ CE-2423 — the selected rule's action rows, composed from the catalogue (<see cref="RuleActionBinding"/>) and cached per row object and block, so the drawn window builds nothing in a steady frame; the buttons and the probe read the same list.</summary>
    private List<RuleActionBinding.Row> ActionRowsOf(RuleRow sel, string? block)
    {
        if (_actionRows is { } c && ReferenceEquals(c.Row, sel) && c.Block == block && c.Editor == (_details != null)) return c.Rows;
        var surface = new RuleActionBinding.Surface(_hooks.Edit == null ? null : () => _hooks.Edit(sel), _hooks.ShowHistory == null ? null : () => _hooks.ShowHistory(sel));
        var rows = RuleActionBinding.Rows(sel, RuleActionBinding.ModeOf(sel, block), req => Ask(sel, req), () => _showOnMap(sel.SubjectNetworkId), block, surface);
        _actionRows = (sel, block, _details != null, rows);
        return rows;
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
        bool first = true;
        foreach (var l in links)
        {
            if (first) ImGui.SameLine(110f); else { ImGui.SameLine(); ImGui.TextDisabled("·"); ImGui.SameLine(); }   // ⭐ CE-4248 — every link sat at 110 px and drew over the one before
            first = false;
            if (ImGui.Selectable($"{l.Text}##{title}{l.NetworkId}", false, ImGuiSelectableFlags.None, new Vector2(0, 0)))
            {
                if (l.IsRule && _view.Find(l.NetworkId) is { } row) Select(row);   // a rule link selects THAT row
                else _selectEntity(l.NetworkId);
            }
        }
    }
}
