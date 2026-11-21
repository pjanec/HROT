using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.ScenarioRules;

namespace Hrot.UI.Common.Rules;

/// <summary>A man (or the unit) the debrief can be about: the subject of a member root.</summary>
public sealed record DebriefMan(int SubjectNet, string Name);

/// <summary>One standard of the chosen man as a card: his value against the target, the group's mean of the same standard, and GO / NO-GO by <c>Score ≥ Pass</c>.</summary>
public sealed record StandardCard(int NetworkId, string Title, string Group, string ValueText, string MeanText, float Score, float Pass, bool? Go, float Margin, bool Required);

/// <summary>A score at one level of the chain above him (his root, his squad, his platoon …).</summary>
public sealed record LevelScore(string Label, float Score, float Pass);

public enum MomentKind { Standard, Suffered, Caused }

/// <summary>A tick on the moments strip: <see cref="Ticks"/> = the frame's wall ticks (a seek target).</summary>
public sealed record Moment(double Time, long Ticks, string Text, MomentKind Kind);

/// <summary>The "you were downed by X at m:ss" card — and where it was read from.</summary>
public sealed record DownedCard(double Time, long Ticks, string Text, string Source);

/// <summary>The trainee debrief as data. <see cref="Available"/> false ⇒ <see cref="WhyNot"/> says why.</summary>
public sealed record TraineeDebriefData(bool Available, string WhyNot, IReadOnlyList<DebriefMan> Men, DebriefMan? Chosen, string Header, float Score, float Pass, bool? Go, int Met, int Total,
                                        IReadOnlyList<LevelScore> Levels, IReadOnlyList<(string Group, IReadOnlyList<StandardCard> Cards)> Groups,
                                        IReadOnlyList<string> WentWell, IReadOnlyList<string> WorkOn, IReadOnlyList<Moment> Moments, DownedCard? Downed, IReadOnlyList<string> Later);

/// <summary>
/// ⭐⭐ D1 / PH-6 (CE-4231; docs/DESIGN_Player_HUD_And_Debrief.md) — the TRAINEE DEBRIEF as DATA, pure. ⛔ The ui sums nothing (behaviors' S4 owns the aggregation): every figure is a read of a replicated status
/// — the man's <b>member root</b> (a child of a <c>MemberEvaluation</c> whose <c>Subject</c> is him), its children as his standards, the evaluation's <c>"&lt;title&gt; (mean)"</c> <c>SelectionMean</c> as the group's mean (S4 ① / ⑤ as built), the
/// <c>MemberEvaluation</c> ancestors as the levels — or a lane entry of the timeline index. A man with no root of his own is shown by the nearest unit of his commander chain that has one (said in the header).
/// </summary>
public static class TraineeDebrief
{
    public static readonly IReadOnlyList<string> LaterLines = new[]
    {
        "Replay from my eyes — later: needs the film director (docs/DESIGN_Film_Production.md)",
        "Compared to your earlier runs — later: needs the trainee record across sessions (gap G7)",
    };

    private static bool Skipped(string kind) => TrainingHud.AggregateKinds.Contains(kind);

    /// <summary>The member roots: children of a <c>MemberEvaluation</c> that are about somebody and are not a game score root or a mean.</summary>
    public static IReadOnlyList<RulesReader.Entry> Roots(IReadOnlyList<RulesReader.Entry> entries)
    {
        var byNet = entries.ToDictionary(e => e.NetworkId);
        return entries.Where(e => !e.Authored && e.SubjectNetworkId != 0 && e.Status.Role == ObjectiveRole.Objective && !Skipped(e.Status.Kind.ToString())
                                  && !e.Status.Kind.ToString().Equals(MissionSummaryModel.MemberEvaluation, StringComparison.OrdinalIgnoreCase)
                                  && byNet.TryGetValue(e.ParentNetworkId, out var p) && p.Status.Kind.ToString().Equals(MissionSummaryModel.MemberEvaluation, StringComparison.OrdinalIgnoreCase))
                      .OrderBy(e => e.NetworkId).ToList();
    }

    /// <param name="chosen">The man to show (a subject net id); 0 ⇒ <paramref name="viewerUnitNet"/>.</param>
    /// <param name="team">The viewer's commander chain, nearest first (<see cref="PlayerView.Team"/>), for the man who has no root of his own.</param>
    public static TraineeDebriefData Build(IReadOnlyList<RulesReader.Entry> entries, in ExerciseResult result, bool recording, int chosen, int viewerUnitNet, IReadOnlyList<int> team,
                                           Func<int, string> nameOf, ISummaryHistory? history)
    {
        var roots = Roots(entries);
        var men = roots.Select(r => new DebriefMan(r.SubjectNetworkId, nameOf(r.SubjectNetworkId))).DistinctBy(m => m.SubjectNet).ToList();
        bool ended = result.Phase == ExercisePhase.Ended;
        if (!ended && !recording)
            return Empty(false, $"available once the exercise has ended (it is {result.Phase}) — and on any recording", men);
        if (roots.Count == 0) return Empty(false, "no member standards in this scenario (a MemberEvaluation with a root per man)", men);

        int man = chosen != 0 ? chosen : viewerUnitNet;
        var candidates = new List<int>();
        if (man != 0) { candidates.Add(man); if (team != null && chosen == 0) candidates.AddRange(team); }
        var root = candidates.Select(c => roots.FirstOrDefault(r => r.SubjectNetworkId == c)).FirstOrDefault(r => r != null) ?? roots[0];
        var who = new DebriefMan(root.SubjectNetworkId, nameOf(root.SubjectNetworkId));
        string header = man != 0 && root.SubjectNetworkId != man ? $"{nameOf(man)} — his standards are {who.Name}'s (his commander's: he has no root of his own)" : who.Name;

        var byNet = entries.ToDictionary(e => e.NetworkId);
        var evaluation = byNet.TryGetValue(root.ParentNetworkId, out var ev) ? ev : null;
        var kids = entries.Where(e => !e.Authored && e.ParentNetworkId == root.NetworkId && !Skipped(e.Status.Kind.ToString())).OrderBy(e => e.NetworkId).ToList();
        var means = evaluation == null ? new List<RulesReader.Entry>() : entries.Where(e => !e.Authored && e.ParentNetworkId == evaluation.NetworkId && e.Status.Kind.ToString().Equals(MissionSummaryModel.SelectionMean, StringComparison.OrdinalIgnoreCase)).ToList();
        string meanName = evaluation != null && evaluation.SubjectNetworkId != 0 ? nameOf(evaluation.SubjectNetworkId) : evaluation != null ? MissionSummaryModel.TitleOf(evaluation) : "group";

        var cards = kids.Select(k =>
        {
            var s = k.Status;
            string title = MissionSummaryModel.TitleOf(k);
            string value = s.Role == ObjectiveRole.Measure ? (s.Target != 0f ? $"{s.Value:0.##} · standard {s.Target:0.##}" : $"{s.Value:0.##}") : s.State.ToString();
            var mean = means.FirstOrDefault(m => m.Status.Title.ToString().Equals(title + " (mean)", StringComparison.OrdinalIgnoreCase));
            string meanText = mean == null ? "" : $"{meanName} mean {mean.Status.Value:0.##}";
            bool? go = s.Pass > 0f ? s.Score >= s.Pass : null;
            return new StandardCard(k.NetworkId, title, s.Group.ToString(), value, meanText, s.Score, s.Pass, go, s.Pass > 0f ? s.Score - s.Pass : 0f, s.Required != 0);
        }).ToList();
        var groups = cards.GroupBy(c => c.Group).OrderBy(g => g.Key.Length == 0 ? 1 : 0).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                          .Select(g => (g.Key.Length == 0 ? "other" : g.Key, (IReadOnlyList<StandardCard>)g.ToList())).ToList();
        int total = cards.Count(c => c.Go != null), met = cards.Count(c => c.Go == true);

        var levels = new List<LevelScore>();
        for (int p = root.ParentNetworkId, g = 0; p != 0 && g < 16 && byNet.TryGetValue(p, out var a); p = a.ParentNetworkId, g++)
            if (a.Status.Scored != 0 && a.Status.Role == ObjectiveRole.Objective)
                levels.Add(new LevelScore(a.SubjectNetworkId != 0 ? nameOf(a.SubjectNetworkId) : MissionSummaryModel.TitleOf(a), a.Status.Score, a.Status.Pass));

        var well = cards.Where(c => c.Go == true).OrderByDescending(c => c.Margin).Take(3).Select(c => $"{c.Title}: {c.ValueText}").ToList();
        var work = cards.Where(c => c.Go == false).OrderBy(c => c.Margin).Take(3).Select(c => $"{c.Title}: {c.ValueText}").ToList();

        // ── his moments (the lanes): his standards' state changes, the downings he suffered or caused
        var moments = new List<Moment>(); DownedCard? downed = null;
        if (history != null)
        {
            var mine = kids.Select(k => k.NetworkId).Append(root.NetworkId).ToHashSet();
            int self = man != 0 ? man : root.SubjectNetworkId;
            foreach (var h in history.Entries)
            {
                if (h.Kind == SummaryEventKind.Objective && mine.Contains(h.Subject) && byNet.TryGetValue(h.Subject, out var e))
                {
                    var st = (ObjectiveState)(int)h.Value;
                    if (st is ObjectiveState.Pending or ObjectiveState.Inactive) continue;
                    moments.Add(new Moment(h.Time, h.Ticks, $"{MissionSummaryModel.TitleOf(e)} {st.ToString().ToLowerInvariant()}", MomentKind.Standard));
                }
                else if (h.Kind == SummaryEventKind.Casualty)
                {
                    bool dead = (int)h.Value >= (int)InjuryState.Dead;
                    if (h.Subject == self) moments.Add(new Moment(h.Time, h.Ticks, $"{(dead ? "killed" : "downed")} by {(h.Other != 0 ? nameOf((int)h.Other) : "unknown")}", MomentKind.Suffered));
                    else if ((int)h.Other == self && self != 0) moments.Add(new Moment(h.Time, h.Ticks, $"you {(dead ? "killed" : "downed")} {nameOf(h.Subject)}", MomentKind.Caused));
                }
            }
            moments = moments.OrderBy(m => m.Time).ThenBy(m => m.Ticks).ToList();
            var last = moments.LastOrDefault(m => m.Kind == MomentKind.Suffered);
            if (last != null)
                downed = new DownedCard(last.Time, last.Ticks, $"you were {last.Text} at {RulesModel.Clock(last.Time)}",
                                        "read from the combat.downed lane — the body part is not in the lane, so no (head) mark; the recorded ManDownedEvent is not readable from the index");
        }
        return new TraineeDebriefData(true, "", men, who, header, root.Status.Score, root.Status.Pass, root.Status.Pass > 0f ? root.Status.Score >= root.Status.Pass : null, met, total,
                                      levels, groups, well, work, moments, downed, LaterLines);
    }

    private static TraineeDebriefData Empty(bool available, string why, IReadOnlyList<DebriefMan> men)
        => new(available, why, men, null, "", 0, 0, null, 0, 0, Array.Empty<LevelScore>(), Array.Empty<(string, IReadOnlyList<StandardCard>)>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<Moment>(), null, LaterLines);
}
