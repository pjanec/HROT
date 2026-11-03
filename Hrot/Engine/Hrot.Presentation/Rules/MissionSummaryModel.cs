using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.ReplayBrowser.Timeline;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.ScenarioRules;
using Fdp.Toolkit.Squad;

namespace Hrot.UI.Common.Rules;

/// <summary>Which side(s) the summary shows.</summary>
public enum SummarySide { Blue, Red, Both }

public enum SummaryEventKind { Objective, Trigger, Casualty }

/// <summary>One raw entry of the three lanes the key events and the trend read; <see cref="Ticks"/> = the frame's wall ticks (a seek target).</summary>
public readonly record struct SummaryLaneEntry(double Time, long Ticks, SummaryEventKind Kind, int Subject, long Other, float Value, float X);

/// <summary>The past the summary reads (never computes): the <c>objective.score</c> lane per rule, and the entries of <c>objective.state</c> / <c>rules.fired</c> / <c>combat.downed</c>.</summary>
public interface ISummaryHistory
{
    IReadOnlyList<(double Time, float Score)> Scores(int networkId);
    IReadOnlyList<SummaryLaneEntry> Entries { get; }
}

/// <summary>⭐ RV-4 — <see cref="ISummaryHistory"/> over a <see cref="TimelineIndex"/>, at the frames' sim times (no sim times ⇒ nothing: the summary then shows no trend and no events).</summary>
public sealed class IndexSummaryHistory : ISummaryHistory
{
    private readonly Dictionary<int, List<(double, float)>> _scores = new();
    private readonly List<SummaryLaneEntry> _entries = new();

    public IndexSummaryHistory(TimelineIndex index)
    {
        var sim = index.FrameSim; var ticks = index.FrameTicks;
        double At(int f) => f >= 0 && f < sim.Count ? sim[f] : double.NaN;
        long Tk(int f) => f >= 0 && f < ticks.Count ? ticks[f] : 0;
        void Lane(string id, Action<TimelineEvent, double, long> each)
        {
            if (!index.Has(id)) return;
            foreach (var e in index.Lane(id)) { double t = At(e.Frame); if (!double.IsNaN(t)) each(e, t, Tk(e.Frame)); }
        }
        Lane(ScenarioRulesTimelineLanes.Score.Id, (e, t, _) => (_scores.TryGetValue(e.Subject, out var l) ? l : _scores[e.Subject] = new()).Add((t, e.Value)));
        Lane(ScenarioRulesTimelineLanes.State.Id, (e, t, k) => _entries.Add(new(t, k, SummaryEventKind.Objective, e.Subject, e.Other, e.Value, e.X)));
        Lane(ScenarioRulesTimelineLanes.Fired.Id, (e, t, k) => { if (e.Value > 0) _entries.Add(new(t, k, SummaryEventKind.Trigger, e.Subject, e.Other, e.Value, e.X)); });
        Lane(CombatTimelineLanes.Downed.Id, (e, t, k) => _entries.Add(new(t, k, SummaryEventKind.Casualty, e.Subject, e.Other, e.Value, e.X)));
    }

    public IReadOnlyList<(double Time, float Score)> Scores(int networkId) => _scores.TryGetValue(networkId, out var l) ? l : Array.Empty<(double, float)>();
    public IReadOnlyList<SummaryLaneEntry> Entries => _entries;
}

public readonly record struct SummaryOptions(SummarySide Side = SummarySide.Both);

public sealed record SummaryHeadline(int NetworkId, string Title, float Score, float Pass, float? Trend, bool Decided, bool? Go);
public sealed record RequiredItem(int NetworkId, string Title, ObjectiveState State, string Words, uint Colour, int ProgressCur, int ProgressNeed, bool Required, int Side);
public sealed record ForcesCard(int Total, int InFight, int Wounded, int Down, int Dead, int Inserted, IReadOnlyList<string> InsertedFrom);
public sealed record AttentionItem(int NetworkId, string Text, float Margin);
public sealed record SideResultCard(ExercisePhase Phase, SideOutcome Outcome, float Points, float Score, double EndedAt, string Reason);
public sealed record PlayerRow(int NetworkId, string Name, float Points, IReadOnlyList<(string Title, float Value)> Counts);
public sealed record GameCard(float TeamPoints, IReadOnlyList<PlayerRow> Players, IReadOnlyList<string> Awards);
public sealed record SideCard(ForceId Side, string Name, SummaryHeadline? Headline, IReadOnlyList<RequiredItem> Required, int RequiredDone, ForcesCard Forces,
                              IReadOnlyList<AttentionItem> Attention, SideResultCard Result, GameCard? Game);
public sealed record LevelRow(int NetworkId, int Depth, string Label, float Score, float? Trend, bool? Go, IReadOnlyList<float?> Groups);
public sealed record LevelTable(string Name, IReadOnlyList<string> GroupColumns, IReadOnlyList<LevelRow> Rows);
public sealed record WhyRow(int NetworkId, string Title, bool IsMeasure, float Value, float Target, float Weight, float Score, float Pass, bool? Go, bool Required);
public sealed record KeyEvent(double Time, string Text, SummaryEventKind Kind, long Ticks, ForceId Side);

/// <summary>The whole summary this frame. ⛔ Every number in it is a READ of a replicated status or a lane entry — nothing is summed or averaged here.</summary>
public sealed record MissionSummary(double Now, ExercisePhase Phase, IReadOnlyList<SideCard> Cards, IReadOnlyList<LevelTable> Levels,
                                    IReadOnlyList<RequiredItem> Objectives, IReadOnlyList<KeyEvent> Events)
{
    internal IReadOnlyList<RulesReader.Entry> All { get; init; } = Array.Empty<RulesReader.Entry>();

    /// <summary>⭐ RV-8 <i>why this score</i> — the rule's children (by <c>Parent</c>), each as the status itself reads: value vs target, weight, score, pass / fail, required.</summary>
    public IReadOnlyList<WhyRow> Why(int networkId)
        => All.Where(e => e.ParentNetworkId == networkId)
              .OrderBy(e => e.Status.Role == ObjectiveRole.Measure ? 0 : 1).ThenBy(e => e.NetworkId)
              .Select(e => new WhyRow(e.NetworkId, MissionSummaryModel.TitleOf(e), e.Status.Role == ObjectiveRole.Measure, e.Status.Value, e.Status.Target,
                                      e.Status.Weight == 0f ? 1f : e.Status.Weight, e.Status.Score, e.Status.Pass, e.Status.Pass > 0f ? e.Status.Score >= e.Status.Pass : null, e.Status.Required != 0))
              .ToList();
}

/// <summary>
/// ⭐⭐ CE-4217 (rules view R3; docs/DESIGN_Rules_View_UI.md §3 RV-8) — the MISSION SUMMARY as DATA, pure (no ImGui). ⛔ <b>The ui sums nothing</b>: behaviors' S4 and backend's S7 own the aggregation, and every number here is a read of a replicated
/// <see cref="ObjectiveStatus"/> (<c>Score</c>, <c>Pass</c>, <c>Value</c>, <c>Target</c>), of the <see cref="ExerciseResult"/>, of the lanes' entries, or a COUNT of men by their injury state (the forces card — a state read, so a revived man is alive again).
/// The rows are <see cref="RulesReader"/>'s (the second consumer of the one reader); the past is <see cref="ISummaryHistory"/>.
/// </summary>
public static class MissionSummaryModel
{
    public const string MemberEvaluation = "MemberEvaluation", PointsKind = "Points", BestKind = "Best", SelectionMean = "SelectionMean", RolesGroup = "roles";
    /// <summary>The trend window: Score now minus Score this long ago (or since the start of what is known).</summary>
    public const double TrendSeconds = 600d;

    internal static string TitleOf(RulesReader.Entry e) => e.Status.Title.ToString() is { Length: > 0 } t ? t : $"rule #{e.NetworkId}";

    public static string SideName(ForceId s) => RulesModel.SideName(s);

    public static MissionSummary Build(EntityRepository world, SummaryOptions options, ISummaryHistory? history, double now)
    {
        var entries = RulesReader.Read(world).Where(e => !e.Authored).ToList();   // a summary reads RUNNING statuses; authored rows have nothing to say yet
        var byNet = entries.ToDictionary(e => e.NetworkId);
        var children = entries.Where(e => e.ParentNetworkId != 0).GroupBy(e => e.ParentNetworkId).ToDictionary(g => g.Key, g => g.OrderBy(e => e.NetworkId).ToList());
        var phase = ExercisePhaseOf(world, out var result);
        var sides = options.Side switch { SummarySide.Blue => new[] { ForceId.Friend }, SummarySide.Red => new[] { ForceId.Hostile }, _ => new[] { ForceId.Friend, ForceId.Hostile } };

        bool UnderEvaluation(RulesReader.Entry e)
        {
            for (int p = e.ParentNetworkId, g = 0; p != 0 && g < 64 && byNet.TryGetValue(p, out var par); p = par.ParentNetworkId, g++)
                if (par.Status.Kind.ToString() == MemberEvaluation) return true;
            return false;
        }
        bool IsEvaluation(RulesReader.Entry e) => e.Status.Kind.ToString() == MemberEvaluation;
        bool Leaf(RulesReader.Entry e) => !children.ContainsKey(e.NetworkId);
        float? TrendOf(RulesReader.Entry e) => Trend(history?.Scores(e.NetworkId), e.Status.Score, now);
        bool? GoOf(in ObjectiveStatus s) => s.Pass > 0f ? s.Score >= s.Pass : null;

        // mission objectives: the leaf objectives of the exercise itself — not the per-man evaluations, not measures, not triggers
        var missionObjectives = entries.Where(e => e.Status.Role == ObjectiveRole.Objective && !IsEvaluation(e) && !UnderEvaluation(e) && Leaf(e) && e.Status.Kind.ToString() != PointsKind)
                                       .Select(e => ItemOf(e)).ToList();

        var cards = new List<SideCard>();
        foreach (var side in sides)
        {
            var req = missionObjectives.Where(i => i.Required && i.Side == (int)side).ToList();
            var headline = HeadlineOf(side);
            cards.Add(new SideCard(side, SideName(side), headline, req, req.Count(i => i.State == ObjectiveState.Achieved), ForcesOf(world, side),
                                   AttentionOf(side), ResultOf(result, phase, side), GameOf(side)));
        }

        // ── scores by level
        var shown = entries.Where(e => sides.Contains(SubjectSide(e)) || SubjectSide(e) == ForceId.Neutral && options.Side == SummarySide.Both).ToList();
        var shownIds = shown.Select(e => e.NetworkId).ToHashSet();
        var levels = new List<LevelTable>
        {
            Table("Command chain", shown.Where(e => IsEvaluation(e) || (byNet.TryGetValue(e.ParentNetworkId, out var par) && IsEvaluation(par) && e.SubjectNetworkId != 0)).ToList(), subjectLabel: true),
            Table("Objectives", shown.Where(e => e.Status.Role != ObjectiveRole.Trigger && !IsEvaluation(e) && !UnderEvaluation(e)).ToList(), subjectLabel: false),
            RolesTable(shown),
        };

        // ── key events: the three lanes, newest first
        var events = new List<KeyEvent>();
        if (history != null)
            foreach (var h in history.Entries)
                if (EventOf(world, byNet, h) is { } ev && (ev.Side == ForceId.Neutral || sides.Contains(ev.Side))) events.Add(ev);
        events = events.OrderByDescending(e => e.Time).ThenByDescending(e => e.Ticks).Take(200).ToList();

        return new MissionSummary(now, phase, cards, levels, missionObjectives.Where(i => sides.Contains((ForceId)i.Side) || i.Side == (int)ForceId.Neutral && options.Side == SummarySide.Both).ToList(), events) { All = entries };

        // ── local helpers (they close over the rows above)
        RequiredItem ItemOf(RulesReader.Entry e)
            => new(e.NetworkId, TitleOf(e), e.Status.State, RulesModel.StateWords(e.Status, now), RulesModel.ColourOf(e.Status), e.Status.ProgressCur, e.Status.ProgressNeed, e.Status.Required != 0, (int)SubjectSide(e));

        ForceId SubjectSide(RulesReader.Entry e) => SideOfRule(world, e);

        SummaryHeadline? HeadlineOf(ForceId side)
        {
            // ⭐ precisely: among the MemberEvaluation rows whose SUBJECT belongs to the side (EntityInfo.ForceId), the TOP-MOST — the one whose Parent is not itself such a row (lowest id on a tie).
            // None (a scenario with no per-man evaluation): the top-most scored composite the side OWNS (Parent 0, Scored, an objective that has children).
            var evals = entries.Where(e => IsEvaluation(e) && SubjectSide(e) == side).ToList();
            RulesReader.Entry? top = evals.Where(e => !evals.Any(x => x.NetworkId == e.ParentNetworkId)).OrderBy(e => e.NetworkId).Cast<RulesReader.Entry?>().FirstOrDefault();
            top ??= entries.Where(e => e.ParentNetworkId == 0 && e.Status.Owner == side && e.Status.Scored != 0 && e.Status.Role == ObjectiveRole.Objective && children.ContainsKey(e.NetworkId))
                           .OrderBy(e => e.NetworkId).Cast<RulesReader.Entry?>().FirstOrDefault();
            if (top is not { } h) return null;
            bool decided = h.Status.IsFinal || phase == ExercisePhase.Ended;
            return new SummaryHeadline(h.NetworkId, TitleOf(h), h.Status.Score, h.Status.Pass, TrendOf(h), decided, GoOf(h.Status));
        }

        IReadOnlyList<AttentionItem> AttentionOf(ForceId side)
            => entries.Where(e => e.Status.Role == ObjectiveRole.Measure && !(byNet.TryGetValue(e.ParentNetworkId, out var attPar) && attPar.Status.Kind.ToString() == PointsKind) && e.Status.Pass > 0f && e.Status.Score < e.Status.Pass && SubjectSide(e) == side && e.Status.Kind.ToString() != PointsKind)
                      .OrderByDescending(e => e.Status.Pass - e.Status.Score).ThenBy(e => e.NetworkId)
                      .Select(e => new AttentionItem(e.NetworkId, $"{TitleOf(e)} {e.Status.Value:0.##} (target {e.Status.Target:0.##})" + (e.SubjectNetworkId != 0 ? " · " + RulesModel.NameOf(world, NetworkIdResolver.FindEntityByNetworkId(world, e.SubjectNetworkId), e.SubjectNetworkId) : ""), e.Status.Pass - e.Status.Score)).ToList();

        GameCard? GameOf(ForceId side)
        {
            // ⭐ a GAME scenario = a Points-kind root exists (S7 §00.13): the card's headline is the side's team total = the Value of the MemberEvaluation (members: side) whose children are those roots.
            var roots = entries.Where(e => e.Status.Kind.ToString() == PointsKind && byNet.TryGetValue(e.ParentNetworkId, out var par) && IsEvaluation(par) && SubjectSide(e) == side).ToList();
            if (roots.Count == 0) return null;
            var team = byNet[roots[0].ParentNetworkId];
            var players = roots.Select(r => new PlayerRow(r.SubjectNetworkId == 0 ? r.NetworkId : r.SubjectNetworkId,
                                                          r.SubjectNetworkId != 0 ? RulesModel.NameOf(world, NetworkIdResolver.FindEntityByNetworkId(world, r.SubjectNetworkId), r.SubjectNetworkId) : TitleOf(r),
                                                          r.Status.Value,
                                                          (children.TryGetValue(r.NetworkId, out var kids) ? kids : new()).Where(k => k.Status.Role == ObjectiveRole.Measure).Select(k => (TitleOf(k), k.Status.Value)).ToList()))
                               .OrderByDescending(p => p.Points).ThenBy(p => p.Name).ToList();
            var awards = entries.Where(e => e.Status.Kind.ToString() == BestKind && SubjectSide(e) == side && e.SubjectNetworkId != 0).Select(e => $"{TitleOf(e)}: {RulesModel.NameOf(world, NetworkIdResolver.FindEntityByNetworkId(world, e.SubjectNetworkId), e.SubjectNetworkId)}").ToList();
            return new GameCard(team.Status.Value, players, awards);
        }

        LevelTable Table(string name, List<RulesReader.Entry> rows, bool subjectLabel)
        {
            var ids = rows.Select(r => r.NetworkId).ToHashSet();
            var kids = rows.Where(r => r.ParentNetworkId != 0 && ids.Contains(r.ParentNetworkId)).GroupBy(r => r.ParentNetworkId).ToDictionary(g => g.Key, g => g.OrderBy(r => r.NetworkId).ToList());
            var roots = rows.Where(r => r.ParentNetworkId == 0 || !ids.Contains(r.ParentNetworkId)).OrderBy(r => r.NetworkId).ToList();
            var ordered = new List<(RulesReader.Entry E, int Depth)>(); var seen = new HashSet<int>();
            void Walk(RulesReader.Entry e, int d) { if (!seen.Add(e.NetworkId)) return; ordered.Add((e, d)); if (kids.TryGetValue(e.NetworkId, out var k)) foreach (var c in k) Walk(c, d + 1); }
            foreach (var r in roots) Walk(r, 0);
            return Rows(name, ordered, subjectLabel);
        }

        LevelTable RolesTable(List<RulesReader.Entry> rows)
        {
            // role means = SelectionMean children titled "All <role>", group "roles"; standard means "<title> (mean)" — flat, roles first (S4 ① / ⑤ as built)
            var means = rows.Where(r => r.Status.Kind.ToString() == SelectionMean).OrderBy(r => r.Status.Group.ToString() == RolesGroup ? 0 : 1).ThenBy(r => TitleOf(r), StringComparer.OrdinalIgnoreCase).ToList();
            return Rows("Roles", means.Select(m => (m, 0)).ToList(), subjectLabel: false);
        }

        LevelTable Rows(string name, List<(RulesReader.Entry E, int Depth)> ordered, bool subjectLabel)
        {
            var groups = ordered.SelectMany(o => children.TryGetValue(o.E.NetworkId, out var k) ? k : new()).Select(k => k.Status.Group.ToString())
                                .Where(g => g.Length > 0 && g != RolesGroup).Distinct().OrderBy(g => g, StringComparer.OrdinalIgnoreCase).ToList();
            var rows = ordered.Select(o =>
            {
                var e = o.E;
                string label = subjectLabel && e.SubjectNetworkId != 0 ? RulesModel.NameOf(world, NetworkIdResolver.FindEntityByNetworkId(world, e.SubjectNetworkId), e.SubjectNetworkId) : TitleOf(e);
                var kids = children.TryGetValue(e.NetworkId, out var k) ? k : new();
                // a column cell = the Score of the row's child in that group (a READ of one status; first by id) — ⛔ never a sum
                var cells = groups.Select(g => kids.FirstOrDefault(c => c.Status.Group.ToString() == g) is { } c && c.Status.Group.Length > 0 ? (float?)c.Status.Score : null).ToList();
                return new LevelRow(e.NetworkId, o.Depth, label, e.Status.Score, TrendOf(e), GoOf(e.Status), cells);
            }).ToList();
            return new LevelTable(name, groups, rows);
        }
    }

    /// <summary>The trend: <paramref name="current"/> minus the score <see cref="TrendSeconds"/> ago (the last entry at or before it), else minus the first entry (since the start); null with fewer than two points of history.</summary>
    public static float? Trend(IReadOnlyList<(double Time, float Score)>? scores, float current, double now)
    {
        if (scores == null || scores.Count < 2) return null;
        var sorted = scores.OrderBy(s => s.Time).ToList();
        double cut = now - TrendSeconds;
        float past = sorted.Any(s => s.Time <= cut) ? sorted.Last(s => s.Time <= cut).Score : sorted[0].Score;
        return current - past;
    }

    /// <summary>The side a rule is about: its subject's <c>EntityInfo.ForceId</c> when the subject has one, else the status's <c>Owner</c> (statuses of exercise-level rules carry no owner side).</summary>
    internal static ForceId SideOfRule(EntityRepository world, RulesReader.Entry e)
        => e.SubjectNetworkId != 0 && NetworkIdResolver.FindEntityByNetworkId(world, e.SubjectNetworkId) is { IsNull: false } s && world.IsAlive(s) && world.HasComponent<EntityInfo>(s)
            ? world.GetComponentRO<EntityInfo>(s).ForceId : e.Status.Owner;

    private static KeyEvent? EventOf(EntityRepository world, Dictionary<int, RulesReader.Entry> byNet, SummaryLaneEntry h)
    {
        switch (h.Kind)
        {
            case SummaryEventKind.Objective:
            {
                var st = (ObjectiveState)(int)h.Value;
                if (st is not (ObjectiveState.Achieved or ObjectiveState.Failed or ObjectiveState.Skipped)) return null;   // an objective starting is not news; its verdict is
                if (!byNet.TryGetValue(h.Subject, out var e)) return null;
                return new KeyEvent(h.Time, $"{TitleOf(e)} {st.ToString().ToLowerInvariant()}", h.Kind, h.Ticks, SideOfRule(world, e));
            }
            case SummaryEventKind.Trigger:
            {
                if (!byNet.TryGetValue(h.Subject, out var e)) return null;
                return new KeyEvent(h.Time, $"{TitleOf(e)} fired", h.Kind, h.Ticks, ForceId.Neutral);   // a trigger belongs to the exercise, not a side: shown under both
            }
            default:
            {
                var side = (ForceId)(int)h.X;
                string who = RulesModel.NameOf(world, NetworkIdResolver.FindEntityByNetworkId(world, h.Subject), h.Subject);
                bool dead = (int)h.Value >= (int)InjuryState.Dead;
                return new KeyEvent(h.Time, $"{SideName(side)} {(dead ? "KIA" : "down")}: {who}", h.Kind, h.Ticks, side);
            }
        }
    }

    private static ExercisePhase ExercisePhaseOf(EntityRepository world, out ExerciseResult result)
    {
        result = default;
        if (!world.IsComponentTypeRegistered<ExerciseResult>()) return ExercisePhase.Running;
        foreach (var e in world.Query().With<ExerciseResult>().WithLifecycle(EntityLifecycle.All).Build()) { result = world.GetComponentRO<ExerciseResult>(e); return result.Phase; }
        return ExercisePhase.Running;
    }

    private static SideResultCard ResultOf(in ExerciseResult r, ExercisePhase phase, ForceId side)
    {
        var s = r.Of(side);
        return new SideResultCard(phase, s.Outcome, s.Points, s.Score, r.EndedAt, r.Reason.ToString());
    }

    /// <summary>
    /// ⭐ The forces card: a COUNT of the side's men by their injury state (<see cref="Injury.StateOf"/>, derived from the replicated Health — a revived man is alive again). A man = an entity with <c>Health</c> + <c>InjuryThresholds</c> and an
    /// <c>EntityInfo</c> side, that is not a unit commander (<see cref="CombatEffective.IsUnit"/>) and not a rule entity — <c>ObjectiveCensus.IsMan</c>'s test (Hrot.AI.Behaviors, not reachable from here) without its in-the-fight clause, because this card counts the down and the dead.
    /// Inserted = carries <see cref="MacroOrigin"/>, named by its instance's macro.
    /// </summary>
    public static ForcesCard ForcesOf(EntityRepository world, ForceId side)
    {
        int total = 0, wounded = 0, down = 0, dead = 0, inserted = 0;
        var from = new Dictionary<string, int>();
        if (!world.IsComponentTypeRegistered<EntityInfo>() || !world.IsComponentTypeRegistered<Health>() || !world.IsComponentTypeRegistered<InjuryThresholds>())
            return new ForcesCard(0, 0, 0, 0, 0, 0, Array.Empty<string>());
        bool hasStatus = world.IsComponentTypeRegistered<ObjectiveStatus>(), origin = world.IsComponentTypeRegistered<MacroOrigin>();
        foreach (var e in world.Query().With<EntityInfo>().With<Health>().With<InjuryThresholds>().Build())
        {
            if (world.GetComponentRO<EntityInfo>(e).ForceId != side || CombatEffective.IsUnit(world, e) || (hasStatus && world.HasComponent<ObjectiveStatus>(e))) continue;
            total++;
            switch (Injury.StateOf(world, e)) { case InjuryState.Wounded: wounded++; break; case InjuryState.Incapacitated: down++; break; case InjuryState.Dead: dead++; break; }
            if (origin && world.HasComponent<MacroOrigin>(e))
            {
                inserted++;
                string name = RulesReader.MacroOf(world, world.GetComponentRO<MacroOrigin>(e).Instance.NetworkId) ?? "macro";
                from[name] = from.GetValueOrDefault(name) + 1;
            }
        }
        return new ForcesCard(total, total - down - dead, wounded, down, dead, inserted, from.OrderBy(k => k.Key).Select(k => $"{k.Key} ×{k.Value}").ToList());
    }
}
