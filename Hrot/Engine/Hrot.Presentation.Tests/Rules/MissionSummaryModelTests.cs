using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.ReplayBrowser.Timeline;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.ScenarioRules;
using Hrot.UI.Common.Rules;
using Xunit;

namespace Hrot.Presentation.Tests.Rules;

/// <summary>
/// ⭐⭐ CE-4217 (rules view R3; docs/DESIGN_Rules_View_UI.md RV-8) — the mission summary's DATA from synthetic statuses and lane entries: every card field, the three groupings, the group columns, <i>why</i> = the children with their pass, the attention order,
/// the trend, the key events from the three lanes, the game card. ⛔ Every expected number is a status field itself — the model sums nothing, and these rails would show it.
/// </summary>
public sealed class MissionSummaryModelTests : IDisposable
{
    private readonly EntityRepository _world = new();
    private readonly Dictionary<int, Entity> _by = new();
    private delegate void Edit(ref ObjectiveStatus s);

    public MissionSummaryModelTests()
    {
        _world.RegisterComponent<NetworkIdentity>();
        _world.RegisterComponent<ObjectiveStatus>();
        _world.RegisterComponent<ObjectiveLinks>();
        _world.RegisterComponent<MacroOrigin>();
        _world.RegisterManagedComponent<MacroInstance>();
        _world.RegisterComponent<EntityInfo>();
        _world.RegisterComponent<Health>();
        _world.RegisterComponent<InjuryThresholds>();
        _world.RegisterComponent<ExerciseResult>();
    }

    public void Dispose() => _world.Dispose();

    private void Rule(int id, string title, string kind, ObjectiveRole role = ObjectiveRole.Objective, int parent = 0, int subject = 0, ForceId owner = ForceId.Friend,
                      ObjectiveState state = ObjectiveState.Active, float score = 0f, float pass = 0f, string group = "", Edit? edit = null)
    {
        var e = _world.CreateEntity(); _by[id] = e;
        _world.AddComponent(e, new NetworkIdentity(id));
        var s = new ObjectiveStatus { Title = new FixedString64(title), Kind = new FixedString32(kind), Role = role, Owner = owner, State = state, Score = score, Pass = pass, Group = new FixedString32(group), Scored = 1 };
        edit?.Invoke(ref s);
        _world.AddComponent(e, s);
        _world.AddComponent(e, new ObjectiveLinks { Parent = new EntityRef(parent), Subject = new EntityRef(subject) });
    }

    private Entity Man(int id, ForceId side, string name, float health = 100f)
    {
        var e = _world.CreateEntity(); _by[id] = e;
        _world.AddComponent(e, new NetworkIdentity(id));
        _world.AddComponent(e, new EntityInfo { Name = new FixedString64(name), ForceId = side });
        _world.AddComponent(e, new Health { Current = health, Max = 100f });
        _world.AddComponent(e, new InjuryThresholds { WoundedBelow = 0.7f, IncapacitatedBelow = 0.3f });
        return e;
    }

    private sealed class History : ISummaryHistory
    {
        public Dictionary<int, List<(double, float)>> Score = new();
        public List<SummaryLaneEntry> E = new();
        public IReadOnlyList<(double Time, float Score)> Scores(int id) => Score.TryGetValue(id, out var l) ? l : new();
        public IReadOnlyList<SummaryLaneEntry> Entries => E;
    }

    /// <summary>A blue platoon with an evaluation, a mission with three leaf objectives, measures, role / standard means.</summary>
    private void Scenario()
    {
        Man(900, ForceId.Friend, "1st Platoon");
        Man(901, ForceId.Friend, "Rifleman Kral");
        Man(902, ForceId.Friend, "Rifleman Wound", 60f);       // wounded
        Man(903, ForceId.Friend, "Rifleman Down", 20f);        // incapacitated
        Man(904, ForceId.Friend, "Rifleman Svoboda", 0f);      // dead
        Man(950, ForceId.Hostile, "Red 1");
        Rule(5, "Bridgehead", "WeightedMean", subject: 900, score: 0.58f, pass: 0.6f);   // the OVERALL root over Mission and Members
        Rule(10, "Mission", "WeightedMean", parent: 5, score: 0.5f, pass: 0.6f);
        Rule(11, "Reach the village", "ReachArea", parent: 10, state: ObjectiveState.Achieved, score: 1f, edit: (ref ObjectiveStatus s) => s.Required = 1);
        Rule(12, "Hold the bridge", "HoldArea", parent: 10, state: ObjectiveState.Active, score: 0.62f, edit: (ref ObjectiveStatus s) => { s.Required = 1; s.ProgressCur = 3; s.ProgressNeed = 10; });
        Rule(13, "Clear the village", "ClearArea", parent: 10, state: ObjectiveState.Pending);
        Rule(20, "Members", "MemberEvaluation", parent: 5, subject: 900, score: 0.42f, pass: 0.6f);
        Rule(21, "Member standards", "WeightedMean", parent: 20, subject: 901, score: 0.61f);
        Rule(22, "Hit ratio", "HitRatio", ObjectiveRole.Measure, parent: 21, subject: 901, score: 0.56f, pass: 0.6f, group: "fire", edit: (ref ObjectiveStatus s) => { s.Value = 0.14f; s.Target = 0.25f; s.Weight = 30f; });
        Rule(23, "Report latency", "ReportLatency", ObjectiveRole.Measure, parent: 21, subject: 901, score: 0.9f, pass: 0.6f, group: "reports", edit: (ref ObjectiveStatus s) => { s.Value = 58f; s.Target = 60f; });
        Rule(26, "Exposure", "Sample", ObjectiveRole.Measure, parent: 21, subject: 900, score: 0.1f, pass: 0.6f, group: "fire", edit: (ref ObjectiveStatus s) => { s.Value = 31f; s.Target = 20f; });
        Rule(24, "All Rifleman", "SelectionMean", parent: 20, score: 0.3f, group: "roles");
        Rule(25, "Hit ratio (mean)", "SelectionMean", ObjectiveRole.Measure, parent: 20, score: 0.48f, group: "fire");
        Rule(30, "Counter-attack", "Trigger", ObjectiveRole.Trigger, owner: ForceId.Hostile);
    }

    // ── the card ────────────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheHeadline_IsTheSidesOverallRoot_NotItsEvaluationPart_ScorePassAndDecided()
    {
        Scenario();
        var blue = MissionSummaryModel.Build(_world, new SummaryOptions(SummarySide.Blue), null, 100).Cards.Single();
        var h = blue.Headline!;
        Assert.Equal((5, 0.58f, 0.6f, false, false), (h.NetworkId, h.Score, h.Pass, h.Decided, h.Go == true));   // the ROOT, not its part "Members" (0.42); 0.58 < 0.6 and not decided yet
        Assert.Null(h.Trend);                                                                                    // no history ⇒ no trend, never a guess
        var red = MissionSummaryModel.Build(_world, new SummaryOptions(SummarySide.Red), null, 100).Cards.Single();
        Assert.Null(red.Headline);                                                                               // Red owns no evaluation
        var closed = _world.CreateEntity(); _world.AddComponent(closed, new ExerciseResult { Phase = ExercisePhase.Ended, EndedAt = (SimInstant)90, Reason = new FixedString64("time up"), Friend = new SideResult { Outcome = SideOutcome.Win, Points = 3f, Score = 0.7f } });
        var after = MissionSummaryModel.Build(_world, new SummaryOptions(SummarySide.Blue), null, 100).Cards.Single();
        Assert.True(after.Headline!.Decided);
        Assert.Equal((ExercisePhase.Ended, SideOutcome.Win, 3f, 0.7f, "time up"), (after.Result.Phase, after.Result.Outcome, after.Result.Points, after.Result.Score, after.Result.Reason));
    }

    [Fact]
    public void TheHeadline_WithNoEvaluation_FallsBackToTheTopMostScoredCompositeTheSideOwns()
    {
        Rule(10, "Mission", "WeightedMean", score: 0.5f, pass: 0.6f);
        Rule(11, "Reach", "ReachArea", parent: 10);
        var h = MissionSummaryModel.Build(_world, new SummaryOptions(SummarySide.Blue), null, 100).Cards.Single().Headline!;
        Assert.Equal((10, 0.5f), (h.NetworkId, h.Score));
    }

    [Fact]
    public void TheTrend_IsScoreNowMinusScoreTenMinutesAgo_OrSinceTheStart()
    {
        Scenario();
        var h = new History { Score = { [5] = new() { (0, 0.30f), (100, 0.36f), (700, 0.40f) } } };
        Assert.Equal(0.58f - 0.36f, MissionSummaryModel.Build(_world, new SummaryOptions(SummarySide.Blue), h, 1000).Cards.Single().Headline!.Trend!.Value, 3);   // the last entry at or before now − 600 s
        Assert.Equal(0.58f - 0.30f, MissionSummaryModel.Build(_world, new SummaryOptions(SummarySide.Blue), h, 300).Cards.Single().Headline!.Trend!.Value, 3);     // shorter than 10 min ⇒ since the start
    }

    [Fact]
    public void TheRequiredObjectives_AreTheRequiredLeavesOfTheMission_WithTheirStatesAndProgress()
    {
        Scenario();
        var c = MissionSummaryModel.Build(_world, new SummaryOptions(SummarySide.Blue), null, 100).Cards.Single();
        Assert.Equal(new[] { "Reach the village", "Hold the bridge" }, c.Required.Select(r => r.Title));
        Assert.Equal((1, 3, 10), (c.RequiredDone, c.Required[1].ProgressCur, c.Required[1].ProgressNeed));
        Assert.Contains("Active", c.Required[1].Words);
    }

    /// <summary>The forces are a COUNT by injury state. ✅ Red-proof: <c>Injury.StateOf</c> replaced by "always Ok" ⇒ wounded / down / dead read 0 ⇒ red.</summary>
    [Fact]
    public void TheForces_AreACountByInjuryState_AndInsertedMenAreNamedByTheirMacro()
    {
        Scenario();
        var inst = _world.CreateEntity(); _world.AddComponent(inst, new NetworkIdentity(500)); _world.SetManagedComponent(inst, new MacroInstance { Macro = "test/qrf" });
        _world.AddComponent(_by[901], new MacroOrigin { Instance = new EntityRef(500) });
        var f = MissionSummaryModel.Build(_world, new SummaryOptions(SummarySide.Blue), null, 100).Cards.Single().Forces;
        Assert.Equal((5, 3, 1, 1, 1, 1), (f.Total, f.InFight, f.Wounded, f.Down, f.Dead, f.Inserted));   // five blue bodies (Health + thresholds, the commander entity included in this fixture); three still in the fight (ok + wounded)
        Assert.Equal(new[] { "test/qrf ×1" }, f.InsertedFrom);
    }

    [Fact]
    public void TheAttention_IsTheMeasuresBelowTheirPass_WorstMarginFirst_WithValueAndTarget()
    {
        Scenario();
        var a = MissionSummaryModel.Build(_world, new SummaryOptions(SummarySide.Blue), null, 100).Cards.Single().Attention;
        Assert.Equal(new[] { "Exposure 31 (target 20) · 1st Platoon", "Hit ratio 0.14 (target 0.25) · Rifleman Kral" }, a.Select(x => x.Text));   // margins 0.5 then 0.04; "Report latency" passes and is absent
    }

    // ── scores by level ─────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheThreeGroupings_CommandChainByParent_ObjectivesTree_RolesFlat()
    {
        Scenario();
        var s = MissionSummaryModel.Build(_world, new SummaryOptions(SummarySide.Blue), null, 100);
        Assert.Equal(new[] { "Command chain", "Objectives", "Roles" }, s.Levels.Select(l => l.Name));
        var chain = s.Levels[0].Rows;
        Assert.Equal(new[] { ("1st Platoon", 0), ("Rifleman Kral", 1) }, chain.Select(r => (r.Label, r.Depth)));   // the evaluation (named by its subject) and its member root
        var objectives = s.Levels[1].Rows;
        Assert.Equal(new[] { ("Bridgehead", 0), ("Mission", 1), ("Reach the village", 2), ("Hold the bridge", 2), ("Clear the village", 2) }, objectives.Select(r => (r.Label, r.Depth)));
        Assert.Equal(new[] { "All Rifleman", "Hit ratio (mean)" }, s.Levels[2].Rows.Select(r => r.Label));          // role means first
    }

    [Fact]
    public void TheGroupColumns_AreOnePerMeasureGroup_EachCellOneChildsOwnScore_NeverASum()
    {
        Scenario();
        var t = MissionSummaryModel.Build(_world, new SummaryOptions(SummarySide.Blue), null, 100).Levels[0];
        Assert.Equal(new[] { "fire", "reports" }, t.GroupColumns);
        var kral = t.Rows.Single(r => r.Label == "Rifleman Kral");
        Assert.Equal(new float?[] { 0.56f, 0.9f }, kral.Groups);                    // Hit ratio and Report latency: the children themselves (Exposure, also "fire", is a second child — the first by id is read, nothing is added)
        var platoon = t.Rows.Single(r => r.Label == "1st Platoon");
        Assert.Equal(new float?[] { 0.48f, null }, platoon.Groups);                  // the standard mean "Hit ratio (mean)" is its fire cell; no reports child ⇒ a dash
    }

    /// <summary>Why = the children as the statuses read: value vs target, weight, score, pass / fail. ✅ Red-proof: <c>Go</c> inverted ⇒ red.</summary>
    [Fact]
    public void Why_IsTheChildrenWithPassFail_MeasuresFirst()
    {
        Scenario();
        var why = MissionSummaryModel.Build(_world, new SummaryOptions(SummarySide.Blue), null, 100).Why(21);
        Assert.Equal(new[] { "Hit ratio", "Report latency", "Exposure" }, why.Select(w => w.Title));
        Assert.Equal((0.14f, 0.25f, 30f, false), (why[0].Value, why[0].Target, why[0].Weight, why[0].Go == true));     // 0.56 < 0.6 ⇒ fail
        Assert.True(why[1].Go);                                                                                        // 0.9 ≥ 0.6 ⇒ pass
    }

    // ── objectives, key events ──────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheKeyEvents_ComeFromTheThreeLanes_NewestFirst_ObjectiveVerdictsTriggerFiresCasualties()
    {
        Scenario();
        var h = new History();
        h.E.Add(new(50, 5000, SummaryEventKind.Objective, 11, 0, (float)ObjectiveState.Achieved, 0));
        h.E.Add(new(20, 2000, SummaryEventKind.Objective, 12, 0, (float)ObjectiveState.Active, 0));       // an objective starting is not news
        h.E.Add(new(80, 8000, SummaryEventKind.Trigger, 30, 0, 1f, 0));
        h.E.Add(new(90, 9000, SummaryEventKind.Casualty, 904, 950, 3f, (float)ForceId.Friend));
        h.E.Add(new(95, 9500, SummaryEventKind.Casualty, 950, 901, 3f, (float)ForceId.Hostile));
        var both = MissionSummaryModel.Build(_world, new SummaryOptions(SummarySide.Both), h, 100).Events;
        Assert.Equal(new[] { "Red KIA: Red 1", "Blue KIA: Rifleman Svoboda", "Counter-attack fired", "Reach the village achieved" }, both.Select(e => e.Text));
        Assert.Equal(new long[] { 9500, 9000, 8000, 5000 }, both.Select(e => e.Ticks));                    // each carries its frame's ticks: the seek target
        var blue = MissionSummaryModel.Build(_world, new SummaryOptions(SummarySide.Blue), h, 100).Events.Select(e => e.Text);
        Assert.DoesNotContain("Red KIA: Red 1", blue);
    }

    [Fact]
    public void TheIndexHistory_ReadsTheScoreStateFiredAndDownedLanes()
    {
        string S = ScenarioRulesTimelineLanes.Score.Id, St = ScenarioRulesTimelineLanes.State.Id, Fi = ScenarioRulesTimelineLanes.Fired.Id, Dn = Fdp.Toolkit.Combat.CombatTimelineLanes.Downed.Id;
        var lanes = new Dictionary<string, SharedList<TimelineEvent>>
        {
            [S] = new(new[] { new TimelineEvent(1, S, 20, 0, 0.6f, 0, 0.30f) }),
            [St] = new(new[] { new TimelineEvent(2, St, 11, 0, 0, 0, (float)ObjectiveState.Achieved) }),
            [Fi] = new(new[] { new TimelineEvent(0, Fi, 30, 0, 0, 0, 0f), new TimelineEvent(3, Fi, 30, 0, 0, 0, 1f) }),
            [Dn] = new(new[] { new TimelineEvent(3, Dn, 904, 950, 1f, 0, 3f) }),
        };
        var index = new TimelineIndex(lanes, new SharedList<long>(new long[] { 0, 10, 20, 30 }), 4, 4, null, new SharedList<double>(new[] { 0d, 5d, 10d, 15d }));
        var h = new IndexSummaryHistory(index);
        Assert.Equal(new[] { (5d, 0.30f) }, h.Scores(20));
        Assert.Equal(new[] { (SummaryEventKind.Objective, 10d, 20L), (SummaryEventKind.Trigger, 15d, 30L), (SummaryEventKind.Casualty, 15d, 30L) }, h.Entries.Select(e => (e.Kind, e.Time, e.Ticks)));   // the 0-valued first sight of a trigger is skipped
    }

    // ── the game card ───────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheGameCard_ExistsWhenAPointsRootDoes_TeamPointsAndEachMansCounts()
    {
        Man(901, ForceId.Friend, "Bot 1"); Man(902, ForceId.Friend, "Bot 2");
        Rule(40, "Blue score", "MemberEvaluation", subject: 901, edit: (ref ObjectiveStatus s) => s.Value = 700f);
        Rule(41, "Score", "Points", ObjectiveRole.Measure, parent: 40, subject: 901, edit: (ref ObjectiveStatus s) => s.Value = 250f);
        Rule(42, "kills", "Count", ObjectiveRole.Measure, parent: 41, subject: 901, edit: (ref ObjectiveStatus s) => s.Value = 2f);
        Rule(43, "deaths", "Count", ObjectiveRole.Measure, parent: 41, subject: 901, edit: (ref ObjectiveStatus s) => s.Value = 1f);
        Rule(44, "Score", "Points", ObjectiveRole.Measure, parent: 40, subject: 902, edit: (ref ObjectiveStatus s) => s.Value = 400f);
        Rule(45, "kills", "Count", ObjectiveRole.Measure, parent: 44, subject: 902, score: 0f, pass: 0.6f);   // a score COUNTER under a Points root is no standard: never "attention"
        var card = MissionSummaryModel.Build(_world, new SummaryOptions(SummarySide.Blue), null, 100).Cards.Single();
        Assert.Empty(card.Attention);
        var g = card.Game!;
        Assert.Equal(700f, g.TeamPoints);                                                                              // the evaluation's own Value — not 250 + 400
        Assert.Equal(new[] { ("Bot 2", 400f), ("Bot 1", 250f) }, g.Players.Select(p => (p.Name, p.Points)));           // ranked by what each root says
        Assert.Equal(new[] { ("kills", 2f), ("deaths", 1f) }, g.Players[1].Counts);
    }

    [Fact]
    public void ANonGameScenario_HasNoGameCard()
    {
        Scenario();
        Assert.Null(MissionSummaryModel.Build(_world, new SummaryOptions(SummarySide.Blue), null, 100).Cards.Single().Game);
    }

    [Fact]
    public void TheSubtreeFilter_OfTheRulesView_KeepsTheRuleAndWhatIsUnderIt()
    {
        Scenario();
        var ids = RulesModel.Build(_world, new RulesOptions(SubtreeOf: 21), null, 100).Rows.Select(r => r.NetworkId).OrderBy(i => i);
        Assert.Equal(new[] { 21, 22, 23, 26 }, ids);   // the member root and its three measures — not the evaluation above, not the mission
    }

    /// <summary>Exercise-level rules carry no owner side on the wire (measured live): a rule belongs to the side of its SUBJECT. ✅ Red-proof: the subject's side ignored ⇒ red.</summary>
    [Fact]
    public void ARuleWithNoOwnerSide_BelongsToTheSideOfItsSubject()
    {
        Man(900, ForceId.Friend, "1st Platoon");
        Rule(10, "Hold", "HoldArea", owner: ForceId.Neutral, subject: 900, edit: (ref ObjectiveStatus s) => s.Required = 1);
        var c = MissionSummaryModel.Build(_world, new SummaryOptions(SummarySide.Blue), null, 100).Cards.Single();
        Assert.Equal(new[] { "Hold" }, c.Required.Select(r => r.Title));
        Assert.Empty(MissionSummaryModel.Build(_world, new SummaryOptions(SummarySide.Red), null, 100).Cards.Single().Required);
    }

    /// <summary>An evaluation IS the headline only when it is the root; several roots list each. ✅ Red-proof: the old "MemberEvaluation first" rule (the evaluation beats the root) ⇒ <c>TheHeadline_IsTheSidesOverallRoot…</c> red.</summary>
    [Fact]
    public void TheHeadline_IsTheEvaluationWhenItIsTheOnlyRoot_AndSeveralRootsAreAllListed()
    {
        Man(900, ForceId.Friend, "1st Platoon");
        Rule(20, "Members", "MemberEvaluation", subject: 900, score: 0.42f, pass: 0.6f);
        var only = MissionSummaryModel.Build(_world, new SummaryOptions(SummarySide.Blue), null, 100).Cards.Single().Headline!;
        Assert.Equal((20, 0.42f), (only.NetworkId, only.Score));
        Rule(21, "Training", "WeightedMean", subject: 900, score: 0.7f, pass: 0.5f, edit: (ref ObjectiveStatus s) => { s.Required = 1; s.Weight = 2f; });
        Rule(22, "Safety", "WeightedMean", subject: 900, score: 0.9f, pass: 0.5f, edit: (ref ObjectiveStatus s) => { s.Required = 1; s.Weight = 1f; });
        var many = MissionSummaryModel.Build(_world, new SummaryOptions(SummarySide.Blue), null, 100).Cards.Single().Headline!;
        Assert.Equal((21, 0.5f), (many.NetworkId, many.Pass));                                        // required with the largest weight; ITS pass mark
        Assert.Equal(new[] { ("Safety", 0.9f), ("Members", 0.42f) }, many.Others);                   // the rest, each with its score
    }
}
