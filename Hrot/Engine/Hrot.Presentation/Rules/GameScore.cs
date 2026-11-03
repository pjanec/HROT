using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Toolkit.ScenarioRules;

namespace Hrot.UI.Common.Rules;

/// <summary>One man's line of the scoreboard: the columns are READS of his <c>Score</c> root's <c>Count</c> children by title (backend S7), the score is the root's own <c>Value</c>.</summary>
public sealed record ScoreRow(int SubjectNet, string Name, bool Me, float Score, float Kills, float Assists, float Deaths, float Captures, float TeamKills, float Streak, float BestStreak)
{
    public string KdText => Deaths > 0 ? (Kills / Deaths).ToString("0.0") : Kills.ToString("0");
}

/// <summary>One team: its name, its total (the side <c>MemberEvaluation</c>'s <c>Value</c>, <c>aggregate: points</c>) and its men by score, the best first.</summary>
public sealed record ScoreTeam(ForceId Side, string Name, float Total, IReadOnlyList<ScoreRow> Rows, int MvpSubject);

/// <summary>The game scoreboard as data. <see cref="IsGame"/> false ⇒ the scenario has no <c>Points</c> roots (a training scenario) ⇒ nothing to show.</summary>
public sealed record ScoreboardModel(IReadOnlyList<ScoreTeam> Teams)
{
    public bool IsGame => Teams.Count > 0;
    public ScoreRow? Mine => Teams.SelectMany(t => t.Rows).FirstOrDefault(r => r.Me);
}

/// <summary>
/// ⭐ G1 / PH-7 (CE-4231) — the game scoreboard from the ONE reader. ⛔ The ui sums nothing: a row's columns are the children of the man's <c>Points</c> root read by <c>Title</c>
/// (<i>Kills · Assists · Deaths · Captures · Team kills · Streak · Best streak</i>, backend S7 P-1 / P-8), his score is the root's <c>Value</c>, the team total the evaluation's <c>Value</c>.
/// The roots are <see cref="MissionSummaryModel.PointsRoots"/> — the same selection the summary's game card uses.
/// </summary>
public static class Scoreboard
{
    public static ScoreboardModel Build(IReadOnlyList<RulesReader.Entry> entries, int viewerUnitNet, Func<RulesReader.Entry, ForceId> sideOf, Func<int, string> nameOf)
    {
        var live = entries.Where(e => !e.Authored).ToList();
        var byNet = live.ToDictionary(e => e.NetworkId);
        var kids = live.Where(e => e.ParentNetworkId != 0).GroupBy(e => e.ParentNetworkId).ToDictionary(g => g.Key, g => g.ToList());
        var teams = new List<ScoreTeam>();
        foreach (var side in new[] { ForceId.Friend, ForceId.Hostile })
        {
            var roots = MissionSummaryModel.PointsRoots(live, byNet, side, sideOf);
            if (roots.Count == 0) continue;
            float Of(RulesReader.Entry root, string title)
                => kids.TryGetValue(root.NetworkId, out var k) && k.FirstOrDefault(c => c.Status.Role == ObjectiveRole.Measure && c.Status.Title.ToString().Equals(title, StringComparison.OrdinalIgnoreCase)) is { } c ? c.Status.Value : 0f;
            var rows = roots.Select(r => new ScoreRow(r.SubjectNetworkId, r.SubjectNetworkId != 0 ? nameOf(r.SubjectNetworkId) : MissionSummaryModel.TitleOf(r),
                                                      viewerUnitNet != 0 && r.SubjectNetworkId == viewerUnitNet, r.Status.Value,
                                                      Of(r, "Kills"), Of(r, "Assists"), Of(r, "Deaths"), Of(r, "Captures"), Of(r, "Team kills"), Of(r, "Streak"), Of(r, "Best streak")))
                            .OrderByDescending(r => r.Score).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
            var team = byNet[roots[0].ParentNetworkId];
            teams.Add(new ScoreTeam(side, RulesModel.SideName(side), team.Status.Value, rows, rows.Count > 0 && rows[0].Score > 0 ? rows[0].SubjectNet : 0));
        }
        return new ScoreboardModel(teams);
    }
}

/// <summary>One floating "+100 Kills".</summary>
public sealed record ScorePopup(string Title, float Points, double At);

/// <summary>
/// ⭐ G1 / PH-7 (CE-4231) — the <c>+100</c> pop-ups, P-9: a child of the possessed man's <c>Points</c> root whose <c>Value</c> ROSE since the last look, × its <c>Points</c>, named by the
/// child's <c>Title</c>. A presentation memory (the last values seen); the first sight of a child only sets its baseline, a fall (a reset, a replay seeked back) re-baselines without a pop-up.
/// </summary>
public sealed class ScorePopups
{
    public const double ShowSeconds = 2.5;
    private readonly Dictionary<int, float> _last = new();
    private readonly List<ScorePopup> _live = new();
    private int _forMan;

    /// <summary>Reads this frame's rows and returns the pop-ups alive at <paramref name="now"/>.</summary>
    public IReadOnlyList<ScorePopup> Update(IReadOnlyList<RulesReader.Entry> entries, int unitNet, double now)
    {
        if (unitNet != _forMan) { _forMan = unitNet; _last.Clear(); _live.Clear(); }   // another man: his own baseline
        var live = entries.Where(e => !e.Authored).ToList();
        var root = unitNet == 0 ? null : live.FirstOrDefault(e => e.SubjectNetworkId == unitNet && e.Status.Kind.ToString() == MissionSummaryModel.PointsKind);
        if (root != null)
            foreach (var c in live.Where(e => e.ParentNetworkId == root.NetworkId && e.Status.Role == ObjectiveRole.Measure && e.Status.Scored != 0))
            {
                float pts = c.Status.Points == 0f ? 1f : c.Status.Points;
                if (_last.TryGetValue(c.NetworkId, out var before) && c.Status.Value > before)
                    _live.Add(new ScorePopup(MissionSummaryModel.TitleOf(c), (c.Status.Value - before) * pts, now));
                _last[c.NetworkId] = c.Status.Value;
            }
        _live.RemoveAll(p => now - p.At > ShowSeconds || p.At > now + 0.001);
        return _live.ToList();
    }

    private static readonly ConditionalWeakTable<EntityRepository, ScorePopups> ByWorld = new();
    public static ScorePopups In(EntityRepository world) => ByWorld.GetValue(world, _ => new ScorePopups());
}

public sealed record AwardLine(string Title, string Winner, float Value, int WinnerNet);
public sealed record RoundLine(string Title, string Value);
public sealed record BestMoment(double Time, long Ticks, string Text, string Source);

/// <summary>The end-of-round screen as data.</summary>
public sealed record GameSummaryData(bool Available, string WhyNot, ExercisePhase Phase, ForceId Winner, string Banner, string Subline, ScoreboardModel Board,
                                     IReadOnlyList<RoundLine> YourRound, IReadOnlyList<AwardLine> Awards, BestMoment? Best, double EndedAt);

/// <summary>
/// ⭐ G1 / PH-7 (CE-4231) — the round summary from the replicated statuses, the <see cref="ExerciseResult"/> and the lanes: the winner banner (<c>SideResult.Outcome</c>), the scoreboard,
/// <i>your round</i> (his <c>Score</c> root's children), the awards (<c>Best</c> statuses once Achieved: title, <c>Subject</c> = the winner, <c>Value</c>) and <i>your best moment</i>.
/// </summary>
public static class GameSummary
{
    public static GameSummaryData Build(IReadOnlyList<RulesReader.Entry> entries, in ExerciseResult result, int viewerUnitNet, double now, bool recording,
                                        Func<RulesReader.Entry, ForceId> sideOf, Func<int, string> nameOf, ISummaryHistory? history)
    {
        var board = Scoreboard.Build(entries, viewerUnitNet, sideOf, nameOf);
        bool ended = result.Phase == ExercisePhase.Ended;
        if (!board.IsGame) return new GameSummaryData(false, "this scenario has no game score (no Points roots)", result.Phase, ForceId.Neutral, "", "", board, Array.Empty<RoundLine>(), Array.Empty<AwardLine>(), null, result.EndedAt);
        string whyNot = ended || recording ? "" : $"available once the round has ended — now {result.Phase}";
        var winner = result.Friend.Outcome == SideOutcome.Win ? ForceId.Friend : result.Hostile.Outcome == SideOutcome.Win ? ForceId.Hostile : ForceId.Neutral;
        string banner = !ended ? "ROUND IN PROGRESS" : winner != ForceId.Neutral ? $"{RulesModel.SideName(winner).ToUpperInvariant()} WINS" : result.Friend.Outcome == SideOutcome.Draw || result.Hostile.Outcome == SideOutcome.Draw ? "DRAW" : "ROUND OVER";

        // the subline: the points held and the tickets, read from the same HUD model (one reading of the statuses)
        var hud = GameHud.Build(entries, new PlayerView(Entity.Null, viewerUnitNet, ForceId.Neutral, Array.Empty<int>()), now, sideOf: sideOf);
        int held(ForceId s) => hud.Points.Count(p => p.Owner == s);
        string tickets(ForceId s) => hud.Tickets.FirstOrDefault(t => t.Side == s) is { } t ? ((int)Math.Round(t.Value)).ToString() : "—";
        var sub = new List<string>();
        if (hud.Points.Count > 0) sub.Add($"{held(ForceId.Friend)} : {held(ForceId.Hostile)} points held");
        if (hud.Tickets.Count > 0) sub.Add($"{tickets(ForceId.Friend)} : {tickets(ForceId.Hostile)} tickets");
        if (ended && result.Reason.Length > 0) sub.Add(result.Reason.ToString());

        var live = entries.Where(e => !e.Authored).ToList();
        var mine = viewerUnitNet == 0 ? null : live.FirstOrDefault(e => e.SubjectNetworkId == viewerUnitNet && e.Status.Kind.ToString() == MissionSummaryModel.PointsKind);
        var round = new List<RoundLine>();
        if (mine != null)
        {
            var row = board.Mine;
            if (row != null) { round.Add(new RoundLine("K/D", row.KdText)); round.Add(new RoundLine("best streak", row.BestStreak.ToString("0"))); round.Add(new RoundLine("team kills", row.TeamKills.ToString("0"))); }
            foreach (var c in live.Where(e => e.ParentNetworkId == mine.NetworkId && e.Status.Role == ObjectiveRole.Measure && e.Status.Scored != 0).OrderBy(e => e.NetworkId))
                round.Add(new RoundLine(MissionSummaryModel.TitleOf(c), $"{c.Status.Value:0.##}" + (c.Status.Points != 0f ? $" × {c.Status.Points:0.##}" : "")));
        }
        var awards = live.Where(e => e.Status.Kind.ToString() == MissionSummaryModel.BestKind && e.Status.State == ObjectiveState.Achieved && e.SubjectNetworkId != 0)
                         .OrderBy(e => e.NetworkId).Select(e => new AwardLine(MissionSummaryModel.TitleOf(e), nameOf(e.SubjectNetworkId), e.Status.Value, e.SubjectNetworkId)).ToList();
        return new GameSummaryData(whyNot.Length == 0, whyNot, result.Phase, winner, banner, string.Join(" · ", sub), board, round, awards, mine == null ? null : BestMomentOf(mine, viewerUnitNet, history, nameOf), result.EndedAt);
    }

    /// <summary>The window of seconds a burst of kills is measured over.</summary>
    public const double BurstSeconds = 15.0;

    /// <summary>
    /// ⭐ <i>your best moment</i>: the largest RISE of his root's <c>objective.score</c> lane (the lane carries <c>Score</c>); a <c>Points</c> root has NO target, so its Score stays 0 and the lane never rises
    /// (measured, CE-4232) — then the densest <see cref="BurstSeconds"/> of the kills he CAUSED (<c>combat.downed</c>, <c>Other</c> = him). The text says which source it came from.
    /// </summary>
    public static BestMoment? BestMomentOf(RulesReader.Entry root, int man, ISummaryHistory? history, Func<int, string> nameOf)
    {
        if (history == null) return null;
        var scores = history.Scores(root.NetworkId).OrderBy(s => s.Time).ToList();
        float bestRise = 0f; int at = -1;
        for (int i = 1; i < scores.Count; i++) if (scores[i].Score - scores[i - 1].Score > bestRise) { bestRise = scores[i].Score - scores[i - 1].Score; at = i; }
        if (at >= 0)
        {
            double t = scores[at].Time;
            long ticks = history.Entries.Where(e => Math.Abs(e.Time - t) < 0.5).Select(e => e.Ticks).DefaultIfEmpty(0).First();
            return new BestMoment(t, ticks, $"score +{bestRise:0.##} at {RulesModel.Clock(t)}", "the objective.score lane");
        }
        var kills = history.Entries.Where(e => e.Kind == SummaryEventKind.Casualty && (int)e.Other == man && e.Subject != man).OrderBy(e => e.Time).ToList();
        if (kills.Count == 0) return null;
        int best = 0, from = 0, bestAt = 0;
        for (int i = 0; i < kills.Count; i++)
        {
            while (kills[i].Time - kills[from].Time > BurstSeconds) from++;
            if (i - from + 1 > best) { best = i - from + 1; bestAt = from; }
        }
        var first = kills[bestAt];
        string what = best >= 3 ? "triple kill" : best == 2 ? "double kill" : "kill";
        return new BestMoment(first.Time, first.Ticks, $"{(best >= 3 ? (best == 3 ? "triple" : $"{best}×") + " kill" : what)} — {nameOf(first.Subject)} at {RulesModel.Clock(first.Time)}", "the combat.downed lane (his Points root has no target, so the score lane never rises)");
    }
}
