using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Presentation.Panels.Timeline;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Behavior.Possession;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.ReplayBrowser.Timeline;
using Fdp.Toolkit.ScenarioRules;
using Fdp.Toolkit.Squad.Command;
using Hrot.UI.Common.Rules;
using ImGuiNET;

namespace Hrot.Presentation.Windows;

/// <summary>What a debrief window reads each frame, shared by <see cref="TraineeDebriefWindow"/> and <see cref="GameSummaryWindow"/>: the rows (the ONE reader), the exercise result, who is possessed, the lanes' history.</summary>
internal sealed class DebriefInputs
{
    private readonly Func<EntityRepository?> _world; private readonly Func<ITimelineSource?> _source; private readonly Func<double>? _now;
    private TimelineIndex? _historyOf; private int _historyFrames = -1;
    public DebriefInputs(Func<EntityRepository?> world, Func<ITimelineSource?> source, Func<double>? now) { _world = world; _source = source; _now = now; }

    public EntityRepository? World;
    public IReadOnlyList<RulesReader.Entry> Entries = Array.Empty<RulesReader.Entry>();
    public ExerciseResult Result;
    public PlayerView View;
    public IndexSummaryHistory? History;
    public double Now;
    /// <summary>A timeline index exists (a recording, or the live one being indexed).</summary>
    public bool Recording => _source()?.Index != null;

    public void Refresh()
    {
        World = _world();
        Now = _now?.Invoke() ?? (World == null ? 0d : CommandPictureOps.Now(World));
        var index = _source()?.Index;
        if (index != null && (!ReferenceEquals(index, _historyOf) || index.FrameTicks.Count != _historyFrames))
        { History = new IndexSummaryHistory(index); _historyOf = index; _historyFrames = index.FrameTicks.Count; }
        else if (index == null) { History = null; _historyOf = null; _historyFrames = -1; }
        Result = default; View = default; Entries = Array.Empty<RulesReader.Entry>();
        if (World == null) return;
        MissionSummaryModel.ExercisePhaseOf(World, out Result);
        Entries = RulesReader.Read(World, includeAuthored: false);
        if (World.IsComponentTypeRegistered<PossessedBy>())
            foreach (var e in World.Query().With<PossessedBy>().Build()) { View = PlayerView.Of(World, e); if (!View.IsNone) break; }
    }

    public string NameOf(int net) => World == null ? $"#{net}" : RulesModel.NameOf(World, NetworkIdResolver.ResolveNetworkId(World, net), net);
    public ForceId SideOf(RulesReader.Entry e) => World == null ? e.Status.Owner : MissionSummaryModel.SideOfRule(World, e);
}

/// <summary>What the <see cref="TraineeDebriefWindow"/> drew this frame, as text — the rails read it.</summary>
public sealed record TraineeDebriefProbe(string Availability, string Header, IReadOnlyList<string> Men, IReadOnlyList<string> Summary, IReadOnlyList<string> Cards, IReadOnlyList<string> WentWell,
                                         IReadOnlyList<string> WorkOn, IReadOnlyList<string> Moments, string Downed, IReadOnlyList<string> Later);

/// <summary>The panel snapshot of the trainee debrief.</summary>
public sealed record TraineeDebriefViewModel(string PanelId, string PanelKind, TraineeDebriefProbe Shown) : IPanelViewModel
{
    public JsonNode Dump() => PanelDump.Of(this);
}

/// <summary>
/// ⭐⭐ D1 / PH-6 (CE-4231) — <b>Window ▸ Trainee debrief</b>: for the chosen man (default: the possessed one) each standard as a GO / NO-GO card with the group's mean, his score against the levels above him,
/// <i>went well / work on</i>, and his moments from the lanes (each a seek). Available once <c>ExerciseResult.Phase == Ended</c> and on any recording. Everything is <see cref="TraineeDebrief"/>'s — ⛔ the window sums nothing.
/// </summary>
public sealed class TraineeDebriefWindow : ManagedWindow
{
    public const string Kind = "trainee-debrief";
    private readonly DebriefInputs _in; private readonly ITimelineCursor? _cursor;
    private int _chosen;
    private TraineeDebriefData _data = TraineeDebrief.Build(Array.Empty<RulesReader.Entry>(), default, false, 0, 0, Array.Empty<int>(), n => "#" + n, null);
    private TraineeDebriefProbe _probe = new("", "", Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), "", Array.Empty<string>());

    public TraineeDebriefWindow(Func<EntityRepository?> world, Func<ITimelineSource?> source, ITimelineCursor? cursor, string id, string perspective, Vector4 titleBarColor, Func<double>? now = null)
        : base(id, "Trainee debrief", perspective, WindowScope.PerspectiveBound)
    {
        _in = new DebriefInputs(world, source, now); _cursor = cursor;
        TitleBarColor = titleBarColor; IsOpen = true;
        PanelSnapshot.DeclareInstrumented(Id);
    }

    /// <summary>The man chosen in the picker (a subject net id); 0 = the possessed one.</summary>
    public int Chosen { get => _chosen; set => _chosen = value; }
    /// <summary>⭐ R-6 — the debrief has something to show NOW (rebuilds from the world first): what the Player's end-of-exercise open asks, so a window with nothing to say is not opened beside the one that has.</summary>
    public bool HasContent() { Rebuild(); return _data.Available; }
    public TraineeDebriefData Data => _data;
    public TraineeDebriefProbe Probe => _probe;

    public bool SeekTo(Moment m)
    {
        if (_cursor is not { CanSeek: true }) return false;
        _cursor.Seek(m.Ticks);
        return true;
    }

    internal void Rebuild()
    {
        _in.Refresh();
        _data = TraineeDebrief.Build(_in.Entries, _in.Result, _in.Recording, _chosen, _in.View.UnitNetId, _in.View.Team ?? Array.Empty<int>(), _in.NameOf, _in.History);
        string F(float v) => v.ToString("0.00", CultureInfo.InvariantCulture);
        _probe = new TraineeDebriefProbe(_data.Available ? "available" : _data.WhyNot, _data.Header, _data.Men.Select(m => m.Name).ToList(),
            _data.Available ? new[] { $"score {F(_data.Score)}{(_data.Pass > 0 ? $" · pass {F(_data.Pass)} {(_data.Go == true ? "GO" : "NO-GO")}" : "")}", $"{_data.Met} of {_data.Total} standards met", string.Join(" · ", _data.Levels.Select(l => $"{l.Label} {F(l.Score)}")) } : Array.Empty<string>(),
            _data.Groups.SelectMany(g => g.Cards.Select(c => $"{g.Group} | {c.Title} | {(c.Go is { } go ? (go ? "GO" : "NO-GO") : "—")} | {c.ValueText}{(c.MeanText.Length > 0 ? " | " + c.MeanText : "")}")).ToList(),
            _data.WentWell, _data.WorkOn, _data.Moments.Select(m => $"{RulesModel.Clock(m.Time)} {m.Text}").ToList(), _data.Downed is { } d ? $"{d.Text} ({d.Source})" : "", _data.Later);
    }

    private TraineeDebriefViewModel BuildAndPublish()
    {
        Rebuild();
        var vm = new TraineeDebriefViewModel(Id, Kind, _probe);
        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return vm;
    }

    internal TraineeDebriefViewModel SimulateDrawClientArea() => BuildAndPublish();
    internal void DrawContent() => DrawClientArea();

    protected override void BeforeBegin()
    {
        ImGui.SetNextWindowSize(new Vector2(900f, 760f), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowBgAlpha(1f);
    }

    private static Vector4 Col(uint abgr) => ImGui.ColorConvertU32ToFloat4(abgr);

    protected override void DrawClientArea()
    {
        BuildAndPublish();
        ImGui.Text("Debrief for"); ImGui.SameLine();
        ImGui.SetNextItemWidth(260f);
        if (ImGui.BeginCombo("##td-man", _data.Chosen?.Name ?? "—"))
        {
            if (ImGui.Selectable("the possessed man", _chosen == 0)) _chosen = 0;
            foreach (var m in _data.Men) if (ImGui.Selectable(m.Name + "##m" + m.SubjectNet, _chosen == m.SubjectNet)) _chosen = m.SubjectNet;
            ImGui.EndCombo();
        }
        if (!_data.Available) { ImGui.Spacing(); ImGui.TextDisabled(_data.WhyNot); DrawLater(); return; }
        ImGui.BeginChild("##td-scroll", new Vector2(0, 0), ImGuiChildFlags.None, ImGuiWindowFlags.None);
        ImGui.TextDisabled(_data.Header);
        ImGui.TextColored(Col(_data.Go == false ? RulesModel.Failed : _data.Go == true ? RulesModel.Achieved : RulesModel.Grey), _data.Go == true ? "GO" : _data.Go == false ? "NO-GO" : "—");
        ImGui.SameLine(); ImGui.Text($"{_data.Met} of {_data.Total} standards met");
        ImGui.Text($"Your score {_data.Score:0.00}" + (_data.Levels.Count > 0 ? "  ·  " + string.Join(" · ", _data.Levels.Select(l => $"{l.Label} {l.Score:0.00}")) : ""));
        foreach (var (group, cards) in _data.Groups)
        {
            ImGui.Spacing(); ImGui.TextDisabled(group.ToUpperInvariant());
            foreach (var c in cards)
            {
                ImGui.TextColored(Col(c.Go == true ? RulesModel.Achieved : c.Go == false ? RulesModel.Failed : RulesModel.Grey), c.Go == true ? "GO   " : c.Go == false ? "NO-GO" : "  —  ");
                ImGui.SameLine(); ImGui.TextUnformatted($"{c.Title}{(c.Required ? " (required)" : "")}  {c.ValueText}");
                if (c.MeanText.Length > 0) { ImGui.SameLine(); ImGui.TextDisabled("· " + c.MeanText); }
            }
        }
        ImGui.Spacing(); ImGui.Separator();
        ImGui.Text("Went well"); foreach (var w in _data.WentWell) ImGui.TextColored(Col(RulesModel.Achieved), "+ " + w); if (_data.WentWell.Count == 0) ImGui.TextDisabled("—");
        ImGui.Text("Work on"); foreach (var w in _data.WorkOn) ImGui.TextColored(Col(RulesModel.Cooldown), "! " + w); if (_data.WorkOn.Count == 0) ImGui.TextDisabled("—");
        ImGui.Spacing(); ImGui.Separator();
        ImGui.Text("Your exercise, moment by moment");
        if (_data.Downed is { } d) { ImGui.TextColored(Col(RulesModel.Failed), d.Text); ImGui.TextDisabled("  " + d.Source); }
        if (_data.Moments.Count == 0) ImGui.TextDisabled("no moments in the lanes (a live run before it is indexed has none)");
        foreach (var m in _data.Moments.Take(80))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Col(m.Kind == MomentKind.Suffered ? RulesModel.Failed : m.Kind == MomentKind.Caused ? RulesModel.Achieved : RulesModel.Active));
            if (ImGui.Selectable($"{RulesModel.Clock(m.Time)}  {m.Text}##mo{m.Ticks}{m.Text}")) SeekTo(m);
            ImGui.PopStyleColor();
        }
        DrawLater();
        ImGui.EndChild();
    }

    private void DrawLater()
    {
        ImGui.Spacing();
        foreach (var l in _data.Later) { ImGui.BeginDisabled(); ImGui.Button(l.Split(" — ")[0] + "##later" + l.Length); ImGui.EndDisabled(); if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(l); ImGui.SameLine(); }
        ImGui.NewLine();
    }
}

/// <summary>What the <see cref="GameSummaryWindow"/> drew this frame, as text.</summary>
public sealed record GameSummaryProbe(string Banner, string Subline, string Availability, IReadOnlyList<string> Teams, IReadOnlyList<string> YourRound, IReadOnlyList<string> Awards, string BestMoment);

/// <summary>The panel snapshot of the game summary.</summary>
public sealed record GameSummaryViewModel(string PanelId, string PanelKind, GameSummaryProbe Shown) : IPanelViewModel
{
    public JsonNode Dump() => PanelDump.Of(this);
}

/// <summary>
/// ⭐⭐ G1 / PH-7 (CE-4231) — <b>Window ▸ Game summary</b> (<i>End of round</i>): the winner banner, the scoreboard per team, <i>your round</i>, the awards and <i>your best moment</i> (a seek). Opens on a recording too.
/// Everything is <see cref="GameSummary"/>'s. ⛔ Level / XP is not designed anywhere and is not here.
/// </summary>
public sealed class GameSummaryWindow : ManagedWindow
{
    public const string Kind = "game-summary";
    private readonly DebriefInputs _in; private readonly ITimelineCursor? _cursor;
    private GameSummaryData _data = new(false, "", ExercisePhase.Running, ForceId.Neutral, "", "", new ScoreboardModel(Array.Empty<ScoreTeam>()), Array.Empty<RoundLine>(), Array.Empty<AwardLine>(), null, 0);
    private GameSummaryProbe _probe = new("", "", "", Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), "");

    public GameSummaryWindow(Func<EntityRepository?> world, Func<ITimelineSource?> source, ITimelineCursor? cursor, string id, string perspective, Vector4 titleBarColor, Func<double>? now = null)
        : base(id, "Game summary", perspective, WindowScope.PerspectiveBound)
    {
        _in = new DebriefInputs(world, source, now); _cursor = cursor;
        TitleBarColor = titleBarColor; IsOpen = true;
        PanelSnapshot.DeclareInstrumented(Id);
    }

    /// <summary>⭐ R-6 — the summary has something to show NOW (rebuilds from the world first).</summary>
    public bool HasContent() { Rebuild(); return _data.Available; }
    public GameSummaryData Data => _data;
    public GameSummaryProbe Probe => _probe;

    public bool SeekToBest()
    {
        if (_data.Best is not { } b || _cursor is not { CanSeek: true }) return false;
        _cursor.Seek(b.Ticks);
        return true;
    }

    internal static string TeamLine(ScoreTeam t) => $"{t.Name} {t.Total:0.##}: " + string.Join("; ", t.Rows.Select(r => $"{r.Name} K{r.Kills:0} A{r.Assists:0} D{r.Deaths:0} C{r.Captures:0} TK{r.TeamKills:0} S{r.Streak:0}/{r.BestStreak:0} = {r.Score:0.##}"));

    internal void Rebuild()
    {
        _in.Refresh();
        _data = GameSummary.Build(_in.Entries, _in.Result, _in.View.UnitNetId, _in.Now, _in.Recording, _in.SideOf, _in.NameOf, _in.History);
        _probe = new GameSummaryProbe(_data.Banner, _data.Subline, _data.Available ? "available" : _data.WhyNot, _data.Board.Teams.Select(TeamLine).ToList(),
            _data.YourRound.Select(r => $"{r.Title} {r.Value}").ToList(), _data.Awards.Select(a => $"{a.Title}: {a.Winner} ({a.Value:0.##})").ToList(), _data.Best is { } b ? $"{b.Text} [{b.Source}]" : "");
    }

    private GameSummaryViewModel BuildAndPublish()
    {
        Rebuild();
        var vm = new GameSummaryViewModel(Id, Kind, _probe);
        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return vm;
    }

    internal GameSummaryViewModel SimulateDrawClientArea() => BuildAndPublish();
    internal void DrawContent() => DrawClientArea();

    protected override void BeforeBegin()
    {
        ImGui.SetNextWindowSize(new Vector2(960f, 780f), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowBgAlpha(1f);
    }

    private static Vector4 Col(uint abgr) => ImGui.ColorConvertU32ToFloat4(abgr);

    protected override void DrawClientArea()
    {
        BuildAndPublish();
        if (_data.Board.Teams.Count == 0) { ImGui.TextDisabled(_data.WhyNot.Length > 0 ? _data.WhyNot : "no game score in this scenario"); return; }
        ImGui.BeginChild("##gs-scroll", new Vector2(0, 0), ImGuiChildFlags.None, ImGuiWindowFlags.None);
        ImGui.SetWindowFontScale(1.8f);
        ImGui.TextColored(Col(_data.Winner == ForceId.Friend ? RulesModel.Active : _data.Winner == ForceId.Hostile ? RulesModel.Failed : RulesModel.Grey), _data.Banner);
        ImGui.SetWindowFontScale(1f);
        if (_data.Subline.Length > 0) ImGui.TextDisabled(_data.Subline);
        if (!_data.Available) ImGui.TextDisabled(_data.WhyNot);
        foreach (var t in _data.Board.Teams)
        {
            ImGui.Spacing();
            ImGui.TextColored(Col(t.Side == ForceId.Friend ? RulesModel.Active : RulesModel.Failed), $"{t.Name.ToUpperInvariant()} — {t.Total:0.##}");
            if (ImGui.BeginTable("##gs-" + t.Side, 9, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
            {
                foreach (var h in new[] { "#", "", "K", "D", "A", "Capt", "TK", "Streak", "Score" }) ImGui.TableSetupColumn(h);
                ImGui.TableHeadersRow();
                int i = 0;
                foreach (var r in t.Rows)
                {
                    ImGui.TableNextRow(); i++;
                    ImGui.TableNextColumn(); ImGui.TextUnformatted(r.SubjectNet == t.MvpSubject ? "★1" : i.ToString());
                    ImGui.TableNextColumn(); ImGui.TextUnformatted(r.Me ? $"You · {r.Name}" : r.Name);
                    foreach (var v in new[] { r.Kills.ToString("0"), r.Deaths.ToString("0"), r.Assists.ToString("0"), r.Captures.ToString("0"), r.TeamKills.ToString("0"), $"{r.Streak:0} / {r.BestStreak:0}", r.Score.ToString("0.##") })
                    { ImGui.TableNextColumn(); ImGui.TextUnformatted(v); }
                }
                ImGui.EndTable();
            }
        }
        if (_data.YourRound.Count > 0)
        {
            ImGui.Spacing(); ImGui.Separator(); ImGui.Text("Your round");
            foreach (var r in _data.YourRound) { ImGui.TextUnformatted($"{r.Title}  {r.Value}"); }
        }
        ImGui.Spacing(); ImGui.Separator(); ImGui.Text("Awards");
        if (_data.Awards.Count == 0) ImGui.TextDisabled("none yet — awards are decided at the close");
        foreach (var a in _data.Awards) ImGui.TextColored(Col(RulesModel.Cooldown), $"★ {a.Title} — {a.Winner}  {a.Value:0.##}");
        ImGui.Spacing(); ImGui.Separator(); ImGui.Text("Your best moment");
        if (_data.Best is { } b) { if (ImGui.Selectable($"▶ {b.Text}##best")) SeekToBest(); ImGui.TextDisabled("from " + b.Source); }
        else ImGui.TextDisabled("none (no kills of yours in the lanes)");
        ImGui.EndChild();
    }
}
