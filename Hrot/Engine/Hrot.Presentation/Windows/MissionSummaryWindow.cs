using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Presentation.Panels.Timeline;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.ReplayBrowser.Timeline;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Squad.Command;
using Hrot.UI.Common.Rules;
using ImGuiNET;

namespace Hrot.Presentation.Windows;

/// <summary>What the <see cref="MissionSummaryWindow"/> drew this frame, as text — the rails read it (and an agent through the panel snapshot).</summary>
public sealed record MissionSummaryProbe(string Side, IReadOnlyList<string> Cards, IReadOnlyList<string> Levels, string Level, IReadOnlyList<string> Why,
                                         IReadOnlyList<string> Objectives, IReadOnlyList<string> Events, string Export, int? Selected);

/// <summary>The panel snapshot of the mission summary.</summary>
public sealed record MissionSummaryViewModel(string PanelId, string PanelKind, MissionSummaryProbe Shown) : IPanelViewModel
{
    /// <inheritdoc/>
    public JsonNode Dump() => PanelDump.Of(this);
}

/// <summary>
/// ⭐⭐ CE-4217 (rules view R3; docs/DESIGN_Rules_View_UI.md §3 RV-8) — <b>Window ▸ Mission summary</b>: read-only. A side switch (Blue / Red / Both), a card per side (headline · required objectives · forces · attention · result, and the game card where S7 scores exist),
/// <i>scores by level</i> in three groupings with one column per measure group, <i>why this score</i> for the selected row with <i>Show the moments on the timeline →</i>, the mission objectives, and the key events (each a seek on the host's timeline).
/// Everything is <see cref="MissionSummaryModel"/>'s — ⛔ the window sums nothing. Opens on a recording too (the source seam: world + timeline source are the host's). <i>Export report</i> is listed, disabled: later.
/// </summary>
public sealed class MissionSummaryWindow : ManagedWindow
{
    public const string Kind = "mission-summary";
    public const string ExportLater = "later";

    private static readonly string[] LevelNames = { "Command chain", "Objectives", "Roles" };

    private readonly Func<EntityRepository?> _world;
    private readonly Func<ITimelineSource?> _source;
    private readonly ITimelineCursor? _cursor;
    private readonly Action<int> _showMoments;
    private readonly Func<double>? _now;
    private TimelineIndex? _historyOf; private int _historyFrames = -1; private IndexSummaryHistory? _history;
    private MissionSummary _summary = new(0, Fdp.Toolkit.ScenarioRules.ExercisePhase.Running, Array.Empty<SideCard>(), Array.Empty<LevelTable>(), Array.Empty<RequiredItem>(), Array.Empty<KeyEvent>());
    private SummarySide _side = SummarySide.Both;
    private int _level;
    private int? _selected;
    private MissionSummaryProbe _probe = new("Both", Array.Empty<string>(), Array.Empty<string>(), "Command chain", Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), ExportLater, null);

    /// <param name="showMoments">⭐ <i>Show the moments on the timeline →</i>: the host opens the Rules window filtered to the subtree of this rule (network id).</param>
    public MissionSummaryWindow(Func<EntityRepository?> world, Func<ITimelineSource?> source, ITimelineCursor? cursor, string id, string perspective, Vector4 titleBarColor,
        Action<int> showMoments, Func<double>? now = null)
        : base(id, "Mission summary", perspective, WindowScope.PerspectiveBound)
    {
        _world = world; _source = source; _cursor = cursor; _showMoments = showMoments; _now = now;
        TitleBarColor = titleBarColor;
        IsOpen = true;
        PanelSnapshot.DeclareInstrumented(Id);
    }

    public SummarySide Side { get => _side; set => _side = value; }
    public int? SelectedNetworkId { get => _selected; set => _selected = value; }
    public int Level { get => _level; set => _level = Math.Clamp(value, 0, LevelNames.Length - 1); }
    public MissionSummary Summary => _summary;
    public MissionSummaryProbe Probe => _probe;

    /// <summary>Seeks the host's timeline to a key event's frame (no-op where the cursor cannot seek).</summary>
    public bool SeekTo(KeyEvent e)
    {
        if (_cursor is not { CanSeek: true }) return false;
        _cursor.Seek(e.Ticks);
        return true;
    }

    /// <summary>⭐ Test hook — the BUILD (summary + probe) with no ImGui context.</summary>
    internal void Rebuild()
    {
        var world = _world();
        double now = _now?.Invoke() ?? (world == null ? 0d : CommandPictureOps.Now(world));
        var index = _source()?.Index;
        if (index != null && (!ReferenceEquals(index, _historyOf) || index.FrameTicks.Count != _historyFrames))
        { _history = new IndexSummaryHistory(index); _historyOf = index; _historyFrames = index.FrameTicks.Count; }
        else if (index == null) { _history = null; _historyOf = null; _historyFrames = -1; }
        _summary = world == null ? new MissionSummary(now, Fdp.Toolkit.ScenarioRules.ExercisePhase.Running, Array.Empty<SideCard>(), Array.Empty<LevelTable>(), Array.Empty<RequiredItem>(), Array.Empty<KeyEvent>())
                                 : MissionSummaryModel.Build(world, new SummaryOptions(_side), _history, now);
        var level = _summary.Levels.Count > _level ? _summary.Levels[_level] : null;
        _probe = new MissionSummaryProbe(_side.ToString(), _summary.Cards.SelectMany(CardLines).ToList(),
            level?.Rows.Select(r => $"{new string(' ', r.Depth * 2)}{r.Label} | {F(r.Score)} | {(r.Trend is { } t ? Signed(t) : "—")}{string.Concat(r.Groups.Select(g => " | " + (g is { } v ? F(v) : "—")))}").ToList() ?? new List<string>(),
            level?.Name ?? "", _selected is { } s ? _summary.Why(s).Select(w => WhyLine(w)).ToList() : new List<string>(),
            _summary.Objectives.Select(o => $"{o.Title} | {o.Words}{(o.ProgressNeed > 0 ? $" {o.ProgressCur}/{o.ProgressNeed}" : "")}{(o.Required ? " | required" : "")}").ToList(),
            _summary.Events.Select(e => $"{RulesModel.Clock(e.Time)} {e.Text}").ToList(), ExportLater, _selected);
    }

    internal static string F(float v) => v.ToString("0.00", CultureInfo.InvariantCulture);
    internal static string Signed(float v) => (v >= 0 ? "+" : "") + v.ToString("0.00", CultureInfo.InvariantCulture);
    internal static string WhyLine(WhyRow w)
        => $"{w.Title} | {(w.IsMeasure ? $"{w.Value:0.##} vs {w.Target:0.##}" : F(w.Score))} | weight {w.Weight:0.##} | {(w.Go is { } g ? (g ? "pass" : "fail") : "—")}{(w.Required ? " | required ✓" : "")}";

    internal static IEnumerable<string> CardLines(SideCard c)
    {
        yield return $"{c.Name}: {(c.Headline is { } h ? $"{h.Title} {F(h.Score)}{(h.Trend is { } t ? " " + Signed(t) : "")}{(h.Pass > 0 ? $" · pass mark {F(h.Pass)} {(h.Decided ? (h.Go == true ? "GO" : "NO-GO") : "· not yet decided")}" : "")}" : "no headline")}";
        yield return $"{c.Name}: required {c.RequiredDone} / {c.Required.Count}";
        yield return $"{c.Name}: forces {c.Forces.InFight} / {c.Forces.Total} · {c.Forces.Wounded} wounded · {c.Forces.Down} down · {c.Forces.Dead} dead · {c.Forces.Inserted} inserted";
        foreach (var a in c.Attention) yield return $"{c.Name}: attention {a.Text}";
        yield return $"{c.Name}: result {c.Result.Phase}{(c.Result.Outcome != Fdp.Toolkit.ScenarioRules.SideOutcome.None ? $" · {c.Result.Outcome}" : "")}";
        if (c.Game is { } g) yield return $"{c.Name}: game team points {g.TeamPoints:0.##} · {g.Players.Count} players";
    }

    private MissionSummaryViewModel BuildAndPublish()
    {
        Rebuild();
        var vm = new MissionSummaryViewModel(Id, Kind, _probe);
        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return vm;
    }

    internal MissionSummaryViewModel SimulateDrawClientArea() => BuildAndPublish();

    /// <summary>⭐ Test hook — draws the client area inside the caller's ImGui window.</summary>
    internal void DrawContent() => DrawClientArea();

    /// <summary>Opens at a usable size and opaque (as the Rules window: a toolbar-only box and a see-through pane were the first live check's findings).</summary>
    protected override void BeforeBegin()
    {
        ImGui.SetNextWindowSize(new Vector2(1100f, 760f), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowBgAlpha(1f);
    }

    protected override void DrawClientArea()
    {
        BuildAndPublish();
        DrawToolbar();
        ImGui.BeginChild("##ms-scroll", new Vector2(0, 0), ImGuiChildFlags.None, ImGuiWindowFlags.None);
        DrawCards();
        ImGui.Separator();
        DrawLevels();
        ImGui.Separator();
        DrawObjectivesAndEvents();
        ImGui.EndChild();
    }

    private void DrawToolbar()
    {
        foreach (var s in new[] { SummarySide.Blue, SummarySide.Red, SummarySide.Both })
        {
            bool on = _side == s;
            if (on) ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetColorU32(ImGuiCol.ButtonActive));
            if (ImGui.Button(s.ToString())) _side = s;
            if (on) ImGui.PopStyleColor();
            ImGui.SameLine();
        }
        ImGui.TextDisabled($"  {_summary.Phase} · {RulesModel.Clock(_summary.Now)}");
        ImGui.SameLine();
        ImGui.BeginDisabled();
        ImGui.Button("Export report");
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(ExportLater);
    }

    private static Vector4 Col(uint abgr) => ImGui.ColorConvertU32ToFloat4(abgr);

    private void DrawCards()
    {
        if (_summary.Cards.Count == 0) { ImGui.TextDisabled("No running rules yet — the summary reads the replicated objective statuses."); return; }
        if (!ImGui.BeginTable("##ms-cards", _summary.Cards.Count, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.BordersInnerV)) return;
        ImGui.TableNextRow();
        foreach (var c in _summary.Cards)
        {
            ImGui.TableNextColumn();
            ImGui.TextColored(Col(c.Side == ForceId.Friend ? RulesModel.Active : RulesModel.Failed), $"{c.Name.ToUpperInvariant()} — OVERALL");
            if (c.Game is { } game)   // ⭐ a game scenario (S7): the card's headline is the side's TEAM POINTS (the evaluation's own Value)
                ImGui.Text($"{game.TeamPoints:0.##} team points");
            else if (c.Headline is { } h)
            {
                ImGui.Text($"{F(h.Score)}"); ImGui.SameLine();
                if (h.Trend is { } t) ImGui.TextColored(Col(t >= 0 ? RulesModel.Achieved : RulesModel.Failed), $"{(t >= 0 ? "↗" : "↘")} {Math.Abs(t):0.00} / 10 min");
                if (h.Pass > 0) ImGui.TextDisabled($"pass mark {F(h.Pass)} · {(h.Decided ? (h.Go == true ? "GO" : "NO-GO") : "not yet decided")}");
            }
            else ImGui.TextDisabled("no evaluation yet");
            ImGui.Spacing(); ImGui.TextDisabled("REQUIRED OBJECTIVES"); ImGui.Text($"{c.RequiredDone} / {c.Required.Count}");
            foreach (var r in c.Required) { ImGui.TextColored(Col(r.Colour), r.State == ObjectiveStateAchieved ? "✓" : "•"); ImGui.SameLine(); ImGui.TextUnformatted($"{r.Title} — {r.Words}{(r.ProgressNeed > 0 ? $" ({r.ProgressCur}/{r.ProgressNeed})" : "")}"); }
            ImGui.Spacing(); ImGui.TextDisabled("FORCES");
            ImGui.Text($"{c.Forces.InFight} / {c.Forces.Total} · {c.Forces.Wounded} wounded · {c.Forces.Down} down · {c.Forces.Dead} dead");
            if (c.Forces.Inserted > 0) ImGui.TextDisabled($"{c.Forces.Inserted} inserted: {string.Join(", ", c.Forces.InsertedFrom)}");
            if (c.Attention.Count > 0) { ImGui.Spacing(); ImGui.TextDisabled("ATTENTION"); foreach (var a in c.Attention.Take(5)) ImGui.TextColored(Col(RulesModel.Cooldown), "⚠ " + a.Text); if (c.Attention.Count > 5) ImGui.TextDisabled($"… and {c.Attention.Count - 5} more"); }
            ImGui.Spacing(); ImGui.TextDisabled("RESULT");
            ImGui.Text($"{c.Result.Phase}{(c.Result.Outcome != Fdp.Toolkit.ScenarioRules.SideOutcome.None ? $" · {c.Result.Outcome} · {c.Result.Points:0.##} pts · {F(c.Result.Score)}" : "")}");
            if (c.Game is { } g)
            {
                ImGui.Spacing(); ImGui.TextDisabled("GAME"); ImGui.Text($"team points {g.TeamPoints:0.##}");
                foreach (var p in g.Players.Take(8)) ImGui.TextUnformatted($"{p.Name}  {p.Points:0.##}  ({string.Join(" · ", p.Counts.Select(k => $"{k.Title} {k.Value:0.##}"))})");
                foreach (var a in g.Awards) ImGui.TextColored(Col(RulesModel.Cooldown), "★ " + a);
            }
        }
        ImGui.EndTable();
    }

    private const Fdp.Toolkit.ScenarioRules.ObjectiveState ObjectiveStateAchieved = Fdp.Toolkit.ScenarioRules.ObjectiveState.Achieved;

    private void DrawLevels()
    {
        ImGui.Text("Scores by level");
        for (int i = 0; i < LevelNames.Length; i++) { ImGui.SameLine(); if (ImGui.RadioButton(LevelNames[i], _level == i)) _level = i; }
        ImGui.TextDisabled("click a row to see why");
        if (_summary.Levels.Count <= _level) return;
        var table = _summary.Levels[_level];
        if (ImGui.BeginTable("##ms-levels", 3 + table.GroupColumns.Count, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Unit / person"); ImGui.TableSetupColumn("Score"); ImGui.TableSetupColumn("Trend");
            foreach (var g in table.GroupColumns) ImGui.TableSetupColumn(g);
            ImGui.TableHeadersRow();
            foreach (var r in table.Rows)
            {
                ImGui.TableNextRow(); ImGui.TableNextColumn();
                if (ImGui.Selectable(new string(' ', r.Depth * 3) + r.Label + "##lv" + r.NetworkId, _selected == r.NetworkId, ImGuiSelectableFlags.SpanAllColumns)) _selected = r.NetworkId;
                ImGui.TableNextColumn(); ImGui.TextColored(Col(r.Go is { } go ? (go ? RulesModel.Achieved : RulesModel.Failed) : RulesModel.Grey), F(r.Score));
                ImGui.TableNextColumn(); ImGui.TextUnformatted(r.Trend is { } t ? Signed(t) : "—");
                foreach (var g in r.Groups) { ImGui.TableNextColumn(); ImGui.TextUnformatted(g is { } v ? F(v) : "—"); }
            }
            ImGui.EndTable();
        }
        if (_selected is { } sel)
        {
            var why = _summary.Why(sel);
            ImGui.Spacing();
            ImGui.Text($"Why {_summary.All.FirstOrDefault(e => e.NetworkId == sel)?.Status.Title.ToString() ?? "#" + sel} is at {F(_summary.All.FirstOrDefault(e => e.NetworkId == sel)?.Status.Score ?? 0f)}");
            if (why.Count == 0) ImGui.TextDisabled("nothing under it");
            foreach (var w in why) ImGui.TextColored(Col(w.Go is { } g ? (g ? RulesModel.Achieved : RulesModel.Failed) : RulesModel.Grey), WhyLine(w));
            if (ImGui.Button("Show the moments on the timeline →")) _showMoments(sel);
        }
    }

    private void DrawObjectivesAndEvents()
    {
        ImGui.Text("Mission objectives");
        foreach (var o in _summary.Objectives)
        {
            ImGui.TextColored(Col(o.Colour), o.State == ObjectiveStateAchieved ? "✓" : "•"); ImGui.SameLine();
            ImGui.TextUnformatted($"{o.Title} — {o.Words}{(o.ProgressNeed > 0 ? $" ({o.ProgressCur}/{o.ProgressNeed})" : "")}{(o.Required ? " · required" : "")}");
        }
        ImGui.Spacing();
        ImGui.Text("Key events");
        foreach (var e in _summary.Events.Take(60))
            if (ImGui.Selectable($"{RulesModel.Clock(e.Time)}  {e.Text}##ev{e.Ticks}{e.Text}")) SeekTo(e);
        ImGui.Spacing();
        ImGui.TextDisabled("Read-only. Every number is a replicated objective status; the same view opens on a recording.");
    }
}
