using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.ScenarioRules;
using Hrot.UI.Common.Rules;
using Xunit;

namespace Hrot.Presentation.Tests.Rules;

/// <summary>
/// ⭐⭐ D1 + G1 (CE-4231; docs/DESIGN_Player_HUD_And_Debrief.md PH-6, PH-7) — the trainee debrief and the game score screens as DATA from synthetic statuses and lane entries: the member root and its
/// standards as GO / NO-GO cards with the group mean, the levels above him, went well / work on, his moments and the downed card; the scoreboard, the +100 pop-ups, the round summary (banner, subline, your
/// round, awards) and the best moment — every field of every card.
/// </summary>
public sealed class DebriefModelTests : IDisposable
{
    private readonly EntityRepository _world = new();
    private readonly Dictionary<int, Entity> _by = new();
    private delegate void Edit(ref ObjectiveStatus s);

    public DebriefModelTests()
    {
        _world.RegisterComponent<NetworkIdentity>(); _world.RegisterComponent<ObjectiveStatus>(); _world.RegisterComponent<ObjectiveLinks>(); _world.RegisterComponent<EntityInfo>();
    }

    public void Dispose() => _world.Dispose();

    private void Man(int id, string name, ForceId side)
    {
        var e = _world.CreateEntity(); _by[id] = e;
        _world.AddComponent(e, new NetworkIdentity(id));
        _world.AddComponent(e, new EntityInfo { Name = new FixedString64(name), ForceId = side });
    }

    private void Rule(int id, string title, string kind, ObjectiveRole role = ObjectiveRole.Objective, int parent = 0, int subject = 0, ObjectiveState state = ObjectiveState.Active,
                      float score = 0f, float pass = 0f, string group = "", float value = 0f, float target = 0f, float points = 0f, bool required = false, bool scored = true, ForceId owner = ForceId.Neutral)
    {
        var e = _world.CreateEntity(); _by[id] = e;
        _world.AddComponent(e, new NetworkIdentity(id));
        _world.AddComponent(e, new ObjectiveStatus { Title = new FixedString64(title), Kind = new FixedString32(kind), Role = role, Owner = owner, State = state, Score = score, Pass = pass, Group = new FixedString32(group),
                                                     Value = value, Target = target, Points = points, Required = (byte)(required ? 1 : 0), Scored = (byte)(scored ? 1 : 0) });
        _world.AddComponent(e, new ObjectiveLinks { Parent = new EntityRef(parent), Subject = new EntityRef(subject) });
    }

    private IReadOnlyList<RulesReader.Entry> Entries() => RulesReader.Read(_world, includeAuthored: false);
    private string Name(int net) => _by.TryGetValue(net, out var e) && _world.HasComponent<EntityInfo>(e) ? _world.GetComponentRO<EntityInfo>(e).Name.ToString() : "#" + net;
    private ForceId SideOf(RulesReader.Entry e) => e.SubjectNetworkId != 0 && _by.TryGetValue(e.SubjectNetworkId, out var s) && _world.HasComponent<EntityInfo>(s) ? _world.GetComponentRO<EntityInfo>(s).ForceId : e.Status.Owner;
    private static ExerciseResult Ended(SideOutcome blue = SideOutcome.None, SideOutcome red = SideOutcome.None) => new() { Phase = ExercisePhase.Ended, EndedAt = 1122, Friend = new SideResult { Outcome = blue }, Hostile = new SideResult { Outcome = red }, Reason = new FixedString64("tickets") };

    private sealed class History : ISummaryHistory
    {
        public Dictionary<int, List<(double, float)>> Score = new();
        public List<SummaryLaneEntry> E = new();
        public IReadOnlyList<(double Time, float Score)> Scores(int id) => Score.TryGetValue(id, out var l) ? l : new();
        public IReadOnlyList<SummaryLaneEntry> Entries => E;
    }

    // ── D1: a platoon evaluation with a squad, a man with a root, and the group's means

    private void Training()
    {
        Man(900, "1st Platoon", ForceId.Friend); Man(901, "Squad 2", ForceId.Friend); Man(902, "Rifleman Kral", ForceId.Friend); Man(903, "Rifleman Vlk", ForceId.Friend); Man(950, "Raven", ForceId.Hostile);
        Rule(5, "Platoon attack", "WeightedMean", subject: 900, score: 0.42f, pass: 0.6f);
        Rule(6, "Members", "MemberEvaluation", parent: 5, subject: 900, score: 0.42f, pass: 0.6f);
        Rule(7, "Member standards", "WeightedMean", parent: 6, subject: 902, score: 0.22f, pass: 0.6f);
        Rule(8, "Hit ratio", "HitRatio", ObjectiveRole.Measure, 7, 902, score: 0.48f, pass: 0.7f, group: "fire", value: 0.12f, target: 0.25f);
        Rule(9, "Exposure", "Sample", ObjectiveRole.Measure, 7, 902, score: 0.3f, pass: 0.7f, group: "cover", value: 34f, target: 20f);
        Rule(10, "Contact report", "ReportLatency", ObjectiveRole.Measure, 7, 902, score: 1f, pass: 0.7f, group: "reports", value: 38f, target: 60f);
        Rule(11, "No harm", "NoHarm", ObjectiveRole.Objective, 7, 902, state: ObjectiveState.Achieved, score: 1f, pass: 1f, group: "conduct", required: true);
        Rule(12, "Member standards", "WeightedMean", parent: 6, subject: 903, score: 0.5f, pass: 0.6f);
        Rule(13, "Hit ratio", "HitRatio", ObjectiveRole.Measure, 12, 903, score: 0.6f, pass: 0.7f, group: "fire", value: 0.15f, target: 0.25f);
        Rule(14, "Hit ratio (mean)", "SelectionMean", ObjectiveRole.Objective, 6, 0, score: 0.54f, pass: 0.6f, group: "fire", value: 0.135f, target: 0.25f, scored: false);
        Rule(15, "All Rifleman", "SelectionMean", ObjectiveRole.Objective, 6, 0, score: 0.5f, pass: 0.6f, group: "roles", scored: false);
    }

    [Fact]
    public void TheDebrief_IsUnavailableUntilTheExerciseEnds_AndOnARecordingAlways()
    {
        Training();
        var running = TraineeDebrief.Build(Entries(), new ExerciseResult { Phase = ExercisePhase.Running }, recording: false, 0, 902, Array.Empty<int>(), Name, null);
        Assert.False(running.Available);
        Assert.Contains("ended", running.WhyNot);
        Assert.Contains("Running", running.WhyNot);
        Assert.True(TraineeDebrief.Build(Entries(), new ExerciseResult { Phase = ExercisePhase.Running }, recording: true, 0, 902, Array.Empty<int>(), Name, null).Available);
        Assert.True(TraineeDebrief.Build(Entries(), Ended(), false, 0, 902, Array.Empty<int>(), Name, null).Available);
    }

    [Fact]
    public void TheDebrief_ShowsEachStandardAsACard_GoWhenScoreReachesPass_GroupedWithTheGroupsMean()
    {
        Training();
        var d = TraineeDebrief.Build(Entries(), Ended(), false, 0, 902, Array.Empty<int>(), Name, null);
        Assert.Equal("Rifleman Kral", d.Chosen!.Name);
        Assert.Equal("Rifleman Kral", d.Header);
        Assert.Equal(0.22f, d.Score); Assert.Equal(false, d.Go);
        Assert.Equal(new[] { "conduct", "cover", "fire", "reports" }, d.Groups.Select(g => g.Group));
        var cards = d.Groups.SelectMany(g => g.Cards).ToDictionary(c => c.Title);
        Assert.Equal("0.12 · standard 0.25", cards["Hit ratio"].ValueText);
        Assert.Equal(false, cards["Hit ratio"].Go);                                         // 0.48 < 0.7
        Assert.Equal("1st Platoon mean 0.14", cards["Hit ratio"].MeanText);   // the SelectionMean "<title> (mean)" of the evaluation
        Assert.Equal(true, cards["Contact report"].Go);
        Assert.Equal("", cards["Contact report"].MeanText);                                  // no mean made for it
        Assert.True(cards["No harm"].Required); Assert.Equal(true, cards["No harm"].Go);
        Assert.Equal((2, 4), (d.Met, d.Total));                                              // Contact report + No harm
        Assert.DoesNotContain(cards.Keys, k => k.Contains("(mean)"));                       // the means are not the man's own standards
    }

    [Fact]
    public void TheDebrief_PutsHisScoreAgainstTheLevelsAboveHim()
    {
        Training();
        var d = TraineeDebrief.Build(Entries(), Ended(), false, 0, 902, Array.Empty<int>(), Name, null);
        Assert.Equal(new[] { ("1st Platoon", 0.42f), ("1st Platoon", 0.42f) }, d.Levels.Select(l => (l.Label, l.Score)));   // Members (evaluation), then the exercise root — both about the platoon
    }

    [Fact]
    public void WentWell_AndWorkOn_AreTheBestAndWorstMargins_ScoreMinusPass()
    {
        Training();
        var d = TraineeDebrief.Build(Entries(), Ended(), false, 0, 902, Array.Empty<int>(), Name, null);
        Assert.Equal(new[] { "Contact report: 38 · standard 60", "No harm: Achieved" }, d.WentWell);   // +0.30 then +0.00
        Assert.Equal(new[] { "Exposure: 34 · standard 20", "Hit ratio: 0.12 · standard 0.25" }, d.WorkOn);   // −0.40 then −0.22
    }

    [Fact]
    public void AManWithNoRoot_IsShownByTheNearestOfHisCommanderChain_AndTheHeaderSaysSo()
    {
        Training();
        Man(904, "Rifleman Bobr", ForceId.Friend);
        var d = TraineeDebrief.Build(Entries(), Ended(), false, 0, 904, new[] { 903, 901 }, Name, null);
        Assert.Equal(903, d.Chosen!.SubjectNet);
        Assert.Contains("Rifleman Bobr", d.Header); Assert.Contains("no root of his own", d.Header);
        Assert.Equal(new[] { "Hit ratio" }, d.Groups.SelectMany(g => g.Cards).Select(c => c.Title));
        // the picker lists the men who have a member root; a chosen one wins over the possessed
        Assert.Equal(new[] { "Rifleman Kral", "Rifleman Vlk" }, d.Men.Select(m => m.Name));
        Assert.Equal(902, TraineeDebrief.Build(Entries(), Ended(), false, 902, 904, Array.Empty<int>(), Name, null).Chosen!.SubjectNet);
    }

    [Fact]
    public void HisMoments_AreHisStandardsChangesAndTheDowningsHeSufferedOrCaused_EachWithItsSeekTicks()
    {
        Training();
        var h = new History();
        h.E.Add(new(120, 1200, SummaryEventKind.Objective, 11, 0, (int)ObjectiveState.Achieved, 0));          // his standard "No harm"
        h.E.Add(new(130, 1300, SummaryEventKind.Objective, 13, 0, (int)ObjectiveState.Failed, 0));            // another man's standard: not his
        h.E.Add(new(200, 2000, SummaryEventKind.Casualty, 902, 950, (int)InjuryState.Incapacitated, 1));     // Raven downed him
        h.E.Add(new(260, 2600, SummaryEventKind.Casualty, 950, 902, (int)InjuryState.Dead, 2));              // he killed Raven
        h.E.Add(new(300, 3000, SummaryEventKind.Casualty, 903, 950, (int)InjuryState.Dead, 1));               // someone else's
        var d = TraineeDebrief.Build(Entries(), Ended(), false, 0, 902, Array.Empty<int>(), Name, h);
        Assert.Equal(new[] { "No harm achieved", "downed by Raven", "you killed Raven" }, d.Moments.Select(m => m.Text));
        Assert.Equal(new long[] { 1200, 2000, 2600 }, d.Moments.Select(m => m.Ticks));
        Assert.Equal(new[] { MomentKind.Standard, MomentKind.Suffered, MomentKind.Caused }, d.Moments.Select(m => m.Kind));
        Assert.Equal("you were downed by Raven at 3:20", d.Downed!.Text);
        Assert.Contains("lane", d.Downed.Source); Assert.Contains("body part", d.Downed.Source);   // said: the lane has no body part
        Assert.Equal(2000, d.Downed.Ticks);
        Assert.Contains(d.Later, l => l.StartsWith("Replay from my eyes")); Assert.Contains(d.Later, l => l.Contains("G7"));
        Assert.Null(TraineeDebrief.Build(Entries(), Ended(), false, 0, 902, Array.Empty<int>(), Name, null).Downed);   // no lanes ⇒ no card
    }

    // ── G1: the game score tree — a platoon team evaluation per side, a Score root per man with Count children

    private void Game()
    {
        Man(1, "Blue", ForceId.Friend); Man(2, "Red", ForceId.Hostile);
        Man(10, "Hawk", ForceId.Friend); Man(11, "Kral", ForceId.Friend); Man(20, "Raven", ForceId.Hostile);
        Rule(100, "Blue score", "MemberEvaluation", subject: 1, value: 3150f);
        Rule(101, "Red score", "MemberEvaluation", subject: 2, value: 1420f);
        void Player(int rootId, int man, int parent, float score, float kills, float assists, float deaths, float capt, float tk, float streak, float best)
        {
            Rule(rootId, "Score", "Points", ObjectiveRole.Measure, parent, man, value: score, group: "score");
            int i = rootId * 10;
            Rule(i + 1, "Kills", "Count", ObjectiveRole.Measure, rootId, man, value: kills, points: 100, group: "score");
            Rule(i + 2, "Assists", "Count", ObjectiveRole.Measure, rootId, man, value: assists, points: 50, group: "score");
            Rule(i + 3, "Deaths", "Count", ObjectiveRole.Measure, rootId, man, value: deaths, scored: false, group: "score");
            Rule(i + 4, "Captures", "Count", ObjectiveRole.Measure, rootId, man, value: capt, points: 150, group: "score");
            Rule(i + 5, "Team kills", "Count", ObjectiveRole.Measure, rootId, man, value: tk, points: -200, group: "score");
            Rule(i + 6, "Streak", "Count", ObjectiveRole.Measure, rootId, man, value: streak, scored: false, group: "score");
            Rule(i + 7, "Best streak", "Count", ObjectiveRole.Measure, rootId, man, value: best, scored: false, group: "score");
        }
        Player(30, 10, 100, 1840, 11, 4, 3, 2, 0, 3, 4);
        Player(31, 11, 100, 1310, 8, 2, 4, 1, 0, 0, 4);
        Player(32, 20, 101, 1420, 9, 2, 5, 1, 1, 1, 3);
        Rule(200, "Top scorer", "Best", ObjectiveRole.Objective, 0, 10, state: ObjectiveState.Achieved, value: 1840f, scored: false);
        Rule(201, "Sharpshooter", "Best", ObjectiveRole.Objective, 0, 20, state: ObjectiveState.Active, value: 0.5f, scored: false);   // not decided yet
        Rule(400, "Blue tickets", "Counter", subject: 1, value: 87f, target: 150f);
        Rule(401, "Red tickets", "Counter", subject: 2, value: 0f, target: 150f);
        Rule(402, "Point A", "HoldArea", state: ObjectiveState.Active, owner: ForceId.Friend, scored: false);
        _world.GetComponentRW<ObjectiveStatus>(_by[402]).Reason = new FixedString64("F 3 / H 0 men, owner Friend");
    }

    [Fact]
    public void TheScoreboard_ListsEachTeamsMenByScore_ColumnsFromTheCountChildren_TheTotalFromTheEvaluation()
    {
        Game();
        var b = Scoreboard.Build(Entries(), viewerUnitNet: 11, SideOf, Name);
        Assert.True(b.IsGame);
        Assert.Equal(new[] { "Blue", "Red" }, b.Teams.Select(t => t.Name));
        Assert.Equal(new float[] { 3150f, 1420f }, b.Teams.Select(t => t.Total));
        var blue = b.Teams[0];
        Assert.Equal(new[] { "Hawk", "Kral" }, blue.Rows.Select(r => r.Name));            // by score
        Assert.Equal(10, blue.MvpSubject);
        var hawk = blue.Rows[0];
        Assert.Equal((11f, 4f, 3f, 2f, 0f, 3f, 4f, 1840f), (hawk.Kills, hawk.Assists, hawk.Deaths, hawk.Captures, hawk.TeamKills, hawk.Streak, hawk.BestStreak, hawk.Score));
        Assert.Equal("3.7", hawk.KdText);
        Assert.False(hawk.Me); Assert.True(blue.Rows[1].Me);                              // the viewer
        Assert.Equal(1f, b.Teams[1].Rows[0].TeamKills);
        Assert.Same(blue.Rows[1], b.Mine);
        Assert.False(Scoreboard.Build(Entries().Where(e => e.Status.Kind.ToString() != "Points").ToList(), 0, SideOf, Name).IsGame);   // no Points roots ⇒ not a game
    }

    [Fact]
    public void ThePopup_IsAChildCountThatROSE_TimesItsPoints_NamedByTheChild_AndFadesAfterAFewSeconds()
    {
        Game();
        var pop = new ScorePopups();
        Assert.Empty(pop.Update(Entries(), 10, 100));                                      // first sight: a baseline, no pop-up
        _world.GetComponentRW<ObjectiveStatus>(_by[301]).Value = 12;                       // Hawk's Kills 11 → 12
        var a = pop.Update(Entries(), 10, 101);
        Assert.Equal(new[] { ("Kills", 100f) }, a.Select(p => (p.Title, p.Points)));
        _world.GetComponentRW<ObjectiveStatus>(_by[304]).Value = 3;                        // Captures 2 → 3 (× 150)
        Assert.Equal(new[] { ("Kills", 100f), ("Captures", 150f) }, pop.Update(Entries(), 10, 102).Select(p => (p.Title, p.Points)));
        _world.GetComponentRW<ObjectiveStatus>(_by[303]).Value = 4;                        // Deaths: unscored ⇒ no pop-up
        Assert.Equal(2, pop.Update(Entries(), 10, 103).Count);
        Assert.Equal(new[] { ("Captures", 150f) }, pop.Update(Entries(), 10, 102 + ScorePopups.ShowSeconds - 0.1).Select(p => (p.Title, p.Points)));   // the first has faded
        Assert.Empty(pop.Update(Entries(), 10, 120));
        _world.GetComponentRW<ObjectiveStatus>(_by[301]).Value = 2;                        // a fall (reset / seek back): re-baselined, no pop-up
        Assert.Empty(pop.Update(Entries(), 10, 121));
        Assert.Empty(pop.Update(Entries(), 11, 122));                                      // another man: his own baseline
    }

    [Fact]
    public void TheRoundSummary_HasTheBannerTheSublineYourRoundTheAwardsAndTheBoard()
    {
        Game();
        var d = GameSummary.Build(Entries(), Ended(SideOutcome.Win, SideOutcome.Lose), 11, now: 1122, recording: false, SideOf, Name, null);
        Assert.True(d.Available);
        Assert.Equal("BLUE WINS", d.Banner); Assert.Equal(ForceId.Friend, d.Winner);
        Assert.Equal("1 : 0 points held · 87 : 0 tickets · tickets", d.Subline);
        Assert.Equal(new[] { "K/D 2.0", "best streak 4", "team kills 0", "Kills 8 × 100", "Assists 2 × 50", "Captures 1 × 150", "Team kills 0 × -200" }, d.YourRound.Select(r => $"{r.Title} {r.Value}"));
        Assert.Equal(new[] { ("Top scorer", "Hawk") }, d.Awards.Select(a => (a.Title, a.Winner)));      // only the Achieved one
        Assert.Equal(2, d.Board.Teams.Count);
        Assert.Equal("DRAW", GameSummary.Build(Entries(), Ended(SideOutcome.Draw, SideOutcome.Draw), 11, 0, false, SideOf, Name, null).Banner);
        var running = GameSummary.Build(Entries(), new ExerciseResult { Phase = ExercisePhase.Running }, 11, 0, false, SideOf, Name, null);
        Assert.False(running.Available); Assert.Equal("ROUND IN PROGRESS", running.Banner); Assert.Contains("Running", running.WhyNot);
        Assert.True(GameSummary.Build(Entries(), new ExerciseResult { Phase = ExercisePhase.Running }, 11, 0, true, SideOf, Name, null).Available);   // a recording is always readable
        Assert.False(GameSummary.Build(Entries().Where(e => e.Status.Kind.ToString() != "Points").ToList(), Ended(), 11, 0, false, SideOf, Name, null).Board.IsGame);
    }

    [Fact]
    public void YourBestMoment_IsTheLargestRiseOfHisScoreLane_ElseTheDensestBurstOfHisKills_AndSaysWhich()
    {
        Game();
        var root = Entries().First(e => e.NetworkId == 31);
        var h = new History();
        // no score lane rise (a Points root has no target: Score stays 0) ⇒ the kills he caused: three within 15 s beat a lone one
        foreach (var (t, victim) in new[] { (100d, 20), (300d, 20), (305d, 20), (310d, 20) }) h.E.Add(new(t, (long)t * 10, SummaryEventKind.Casualty, victim, 11, (int)InjuryState.Dead, 2));
        h.E.Add(new(400, 4000, SummaryEventKind.Casualty, 11, 20, (int)InjuryState.Dead, 1));   // he was killed: not a kill of his
        var best = GameSummary.BestMomentOf(root, 11, h, Name)!;
        Assert.Equal(300, best.Time); Assert.Equal(3000, best.Ticks);
        Assert.StartsWith("triple kill", best.Text); Assert.Contains("combat.downed", best.Source);
        // a rising score lane wins, as the design says
        h.Score[31] = new() { (10, 0f), (20, 0.1f), (30, 0.5f), (40, 0.52f) };
        var rise = GameSummary.BestMomentOf(root, 11, h, Name)!;
        Assert.Equal(30, rise.Time); Assert.Contains("objective.score", rise.Source);
        Assert.Null(GameSummary.BestMomentOf(root, 11, null, Name));
        Assert.Null(GameSummary.BestMomentOf(root, 11, new History(), Name));
    }
}
