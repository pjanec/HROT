using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.ScenarioRules;
using Hrot.UI.Common.Rules;
using Xunit;

namespace Hrot.Presentation.Tests.Rules;

/// <summary>
/// ⭐⭐ H1 + H2 (CE-4226, CE-4227; docs/DESIGN_Player_HUD_And_Debrief.md PH-2 … PH-4) — the player's HUD as DATA from synthetic statuses, through the ONE reader
/// (<see cref="RulesReader"/>): who I am, the objectives my side sees (Owner and VisibleSides), required first in tree order, the active one's progress or time, my
/// standards (else my team's) coloured by score against pass, the game bar (tickets, points, clock — all by Kind), the capture strip, the streak and the bounty.
/// </summary>
public sealed class PlayerHudTests : IDisposable
{
    private readonly EntityRepository _world = new();
    private readonly Dictionary<int, Entity> _byId = new();

    public PlayerHudTests()
    {
        _world.RegisterComponent<NetworkIdentity>();
        _world.RegisterComponent<ObjectiveStatus>();
        _world.RegisterComponent<ObjectiveLinks>();
        _world.RegisterComponent<EntityInfo>();
        _world.RegisterComponent<UnitSubordinate>();
    }

    public void Dispose() => _world.Dispose();

    private delegate void Edit(ref ObjectiveStatus s);

    private Entity Add(int id, string title, string kind = "", ObjectiveRole role = ObjectiveRole.Objective, ObjectiveState state = ObjectiveState.Active, ForceId owner = ForceId.Friend,
                       int parent = 0, int subject = 0, byte visible = 0, bool required = false, Edit? edit = null)
    {
        var e = _world.CreateEntity();
        _byId[id] = e;
        _world.AddComponent(e, new NetworkIdentity(id));
        var s = new ObjectiveStatus { Title = new FixedString64(title), Kind = new FixedString32(kind), Role = role, State = state, Owner = owner, VisibleSides = visible, Required = (byte)(required ? 1 : 0), Scored = 1 };
        edit?.Invoke(ref s);
        _world.AddComponent(e, s);
        _world.AddComponent(e, new ObjectiveLinks { Parent = new EntityRef(parent), Subject = new EntityRef(subject) });
        return e;
    }

    private Entity Man(int id, string name, ForceId side, int commander = 0)
    {
        var e = _world.CreateEntity();
        _byId[id] = e;
        _world.AddComponent(e, new NetworkIdentity(id));
        _world.AddComponent(e, new EntityInfo { Name = new FixedString64(name), ForceId = side });
        if (commander != 0) _world.AddComponent(e, new UnitSubordinate { Commander = _byId[commander] });
        return e;
    }

    private IReadOnlyList<RulesReader.Entry> Entries() => RulesReader.Read(_world, includeAuthored: false);
    private static PlayerView Me(int net, ForceId side, params int[] team) => new(Entity.Null, net, side, team);

    // ── PH-2 PlayerView

    [Fact]
    public void ThePlayerView_IsTheUnitItsSideAndItsCommanderChain()
    {
        Man(1, "Platoon", ForceId.Friend); Man(2, "Squad lead", ForceId.Friend, commander: 1); var me = Man(3, "Kral", ForceId.Friend, commander: 2);
        var view = PlayerView.Of(_world, me);
        Assert.Equal(3, view.UnitNetId);
        Assert.Equal(ForceId.Friend, view.Side);
        Assert.Equal(new[] { 2, 1 }, view.Team);   // nearest commander first
        Assert.True(PlayerView.Of(_world, Entity.Null).IsNone);
        Assert.True(TrainingHud.Build(Entries(), default, 0).Objectives.Count == 0, "nothing possessed ⇒ no player HUD");
    }

    // ── PH-3 hud.objectives

    /// <summary>Only the objectives my side may see: its Owner, or a side named by VisibleSides. Measures, triggers, game kinds and capture points are no mission objective.</summary>
    [Fact]
    public void TheObjectives_AreTheOnesMySideMaySee_AndNothingElse()
    {
        Add(10, "Reach the village");                                   // Friend owner
        Add(11, "Red secret", owner: ForceId.Hostile);                  // Hostile, nobody asked
        Add(12, "Shown to both", owner: ForceId.Hostile, visible: (byte)((1 << (int)ForceId.Friend) | (1 << (int)ForceId.Hostile)));
        Add(13, "A measure", role: ObjectiveRole.Measure);
        Add(14, "A trigger", role: ObjectiveRole.Trigger);
        Add(15, "Blue tickets", kind: "Counter", edit: (ref ObjectiveStatus s) => { s.Target = 150; s.Value = 150; });
        Add(16, "Point A", kind: "HoldArea", edit: (ref ObjectiveStatus s) => s.Capture = 1);
        var model = TrainingHud.Build(Entries(), Me(1, ForceId.Friend), 10);
        Assert.Equal(new[] { "Reach the village", "Shown to both" }, model.Objectives.Select(o => o.Title));
        Assert.Equal(new[] { "Red secret", "Shown to both" }, TrainingHud.Build(Entries(), Me(1, ForceId.Hostile), 10).Objectives.Select(o => o.Title));
    }

    [Fact]
    public void TheObjectives_AreRequiredFirst_ThenInTreeOrder()
    {
        Add(20, "Mission");
        Add(23, "Optional late", parent: 20);
        Add(22, "Required second", parent: 20, required: true);
        Add(21, "Required first", parent: 20, required: true);
        var model = TrainingHud.Build(Entries(), Me(1, ForceId.Friend), 0);
        Assert.Equal(new[] { "Required first", "Required second", "Optional late" }, model.Objectives.Select(o => o.Title));   // "Mission" has children: a composite is not a line of the list
    }

    /// <summary>⭐ Measured on showcase-bridgehead: every running rule row says Owner <i>Neutral</i>; the side is its SUBJECT's. The live caller passes <c>SideOfRule</c>; a rule with no side at all is for everyone.</summary>
    [Fact]
    public void TheSideOfARule_IsItsSubjects_WhenTheOwnerIsNeutral()
    {
        Man(5, "Blue Platoon", ForceId.Friend); Man(6, "Red Platoon", ForceId.Hostile);
        Add(150, "Blue's", owner: ForceId.Neutral, subject: 5);
        Add(151, "Red's", owner: ForceId.Neutral, subject: 6);
        Add(152, "For all", owner: ForceId.Neutral);
        ForceId SideOf(RulesReader.Entry e) => e.SubjectNetworkId != 0 && _byId.TryGetValue(e.SubjectNetworkId, out var s) ? _world.GetComponentRO<EntityInfo>(s).ForceId : e.Status.Owner;
        Assert.Equal(new[] { "Blue's", "For all" }, TrainingHud.Build(Entries(), Me(1, ForceId.Friend), 0, SideOf).Objectives.Select(o => o.Title));
        Assert.Equal(new[] { "Red's", "For all" }, TrainingHud.Build(Entries(), Me(1, ForceId.Hostile), 0, SideOf).Objectives.Select(o => o.Title));
    }

    /// <summary>The summary's leaf rule, and no conduct standard (<c>NoHarm</c>), per-man evaluation or game score tree among the MISSION lines.</summary>
    [Fact]
    public void TheMissionLines_AreTheLeafObjectivesOfTheExercise_NotCompositesEvaluationsOrConductStandards()
    {
        Add(160, "Bridgehead", kind: "WeightedMean");
        Add(161, "Mission", kind: "WeightedMean", parent: 160);
        Add(162, "Reach phase line RED", kind: "ReachArea", parent: 161);
        Add(163, "Conduct", kind: "WeightedMean", parent: 160);
        Add(164, "No fratricide", kind: "NoHarm", parent: 163);
        Add(165, "Members", kind: "MemberEvaluation", parent: 160);
        Add(166, "Member standards", kind: "WeightedMean", parent: 165);
        Add(167, "No harm", kind: "NoHarm", parent: 166);
        Add(168, "Score", kind: "Points", parent: 165);
        Assert.Equal(new[] { "Reach phase line RED" }, TrainingHud.Build(Entries(), Me(1, ForceId.Friend), 0).Objectives.Select(o => o.Title));
    }

    [Fact]
    public void TheMark_FollowsTheState_AndOnlyTheActiveOneHasProgress()
    {
        Add(30, "Done", state: ObjectiveState.Achieved, edit: (ref ObjectiveStatus s) => { s.ProgressNeed = 10; s.ProgressCur = 10; });
        Add(31, "Hold the bridge", kind: "HoldArea", edit: (ref ObjectiveStatus s) => { s.ProgressCur = 370; s.ProgressNeed = 600; });
        Add(32, "Soon", state: ObjectiveState.Pending);
        Add(33, "Lost", state: ObjectiveState.Failed);
        Add(34, "Kill five", edit: (ref ObjectiveStatus s) => { s.ProgressCur = 2; s.ProgressNeed = 5; });
        var o = TrainingHud.Build(Entries(), Me(1, ForceId.Friend), 0).Objectives.ToDictionary(x => x.Title);
        Assert.Equal(HudMark.Done, o["Done"].Mark); Assert.Equal(-1f, o["Done"].Fraction);
        Assert.Equal(HudMark.Active, o["Hold the bridge"].Mark);
        Assert.Equal("6:10 / 10:00", o["Hold the bridge"].Detail);          // a HoldArea counts seconds
        Assert.Equal(370f / 600f, o["Hold the bridge"].Fraction, 3);
        Assert.Equal("2 / 5", o["Kill five"].Detail);                       // any other progress is a plain count
        Assert.Equal(HudMark.Waiting, o["Soon"].Mark);
        Assert.Equal(HudMark.Failed, o["Lost"].Mark);
    }

    [Fact]
    public void AnActiveObjectiveWithADeadline_ShowsTheTimeLeft()
    {
        Add(40, "Cross before dawn", edit: (ref ObjectiveStatus s) => { s.ChangedAt = 100; s.Deadline = 400; });
        var line = TrainingHud.Build(Entries(), Me(1, ForceId.Friend), 250).Objectives.Single();
        Assert.Equal("2:30", line.Detail);                 // 400 − 250
        Assert.Equal(0.5f, line.Fraction, 3);              // half of the window gone
    }

    // ── PH-3 hud.own-evaluation

    [Fact]
    public void MyStandards_AreTheMeasuresAboutMe_ColouredByScoreAgainstPass()
    {
        Man(7, "Kral", ForceId.Friend);
        Add(50, "Hit ratio", kind: "HitRatio", role: ObjectiveRole.Measure, subject: 7, edit: (ref ObjectiveStatus s) => { s.Value = 0.12f; s.Target = 0.25f; s.Score = 0.5f; s.Pass = 0.7f; });
        Add(51, "Exposure", kind: "Sample", role: ObjectiveRole.Measure, subject: 7, edit: (ref ObjectiveStatus s) => { s.Value = 34; s.Target = 20; s.Score = 0.2f; s.Pass = 0.7f; });
        Add(52, "Contact report", kind: "ReportLatency", role: ObjectiveRole.Measure, subject: 7, edit: (ref ObjectiveStatus s) => { s.Value = 38; s.Score = 0.9f; s.Pass = 0.7f; });
        Add(53, "Someone else's", kind: "HitRatio", role: ObjectiveRole.Measure, subject: 99);
        Add(54, "Unscored", kind: "Count", role: ObjectiveRole.Measure, subject: 7, edit: (ref ObjectiveStatus s) => s.Scored = 0);
        var model = TrainingHud.Build(Entries(), Me(7, ForceId.Friend), 0);
        Assert.False(model.StandardsAreTeams);
        Assert.Equal(new[] { "Hit ratio", "Exposure", "Contact report" }, model.Standards.Select(s => s.Title));
        Assert.Equal("0.12 · target 0.25", model.Standards[0].Text);
        Assert.Equal("38", model.Standards[2].Text);
        Assert.Equal(TrainingHud.Fair, model.Standards[0].Rgb);   // 0.5 of a 0.7 pass: short of it, but within 60 %
        Assert.Equal(TrainingHud.Poor, model.Standards[1].Rgb);
        Assert.Equal(TrainingHud.Good, model.Standards[2].Rgb);
    }

    [Fact]
    public void WhenNoStandardIsMine_TheTeamsAreShown_AndAScoreTreesChildrenNever()
    {
        Man(1, "Squad lead", ForceId.Friend); Man(7, "Kral", ForceId.Friend, commander: 1);
        Add(60, "Squad hit ratio", kind: "HitRatio", role: ObjectiveRole.Measure, subject: 1, edit: (ref ObjectiveStatus s) => { s.Value = 0.3f; s.Score = 0.9f; s.Pass = 0.7f; });
        Add(61, "Score", kind: "Points", role: ObjectiveRole.Measure, subject: 7);
        Add(62, "Streak", kind: "Count", role: ObjectiveRole.Measure, subject: 7, parent: 61);
        var model = TrainingHud.Build(Entries(), Me(7, ForceId.Friend, 1), 0);
        Assert.True(model.StandardsAreTeams);
        Assert.Equal(new[] { "Squad hit ratio" }, model.Standards.Select(s => s.Title));   // the Points aggregate and its Streak child are the game's, not a standard
    }

    // ── PH-4 the game bar

    private void Crossfire()
    {
        Add(70, "Blue tickets", kind: "Counter", owner: ForceId.Friend, edit: (ref ObjectiveStatus s) => { s.Value = 87; s.Target = 150; });
        Add(71, "Red tickets", kind: "Counter", owner: ForceId.Hostile, edit: (ref ObjectiveStatus s) => { s.Value = 12; s.Target = 150; });
        Add(72, "Blue lead", kind: "Counter", owner: ForceId.Friend, edit: (ref ObjectiveStatus s) => { s.Value = 5; s.Target = 0; });      // starts at 0 ⇒ not tickets
        Add(73, "Point A", kind: "HoldArea", owner: ForceId.Friend, edit: (ref ObjectiveStatus s) => { s.Capture = 1; s.AreaCenter = new System.Numerics.Vector3(400, 400, 0); s.AreaRadius = 15; s.ProgressNeed = 10; s.ProgressCur = 10; });
        Add(74, "Point B", kind: "HoldArea", owner: ForceId.Neutral, edit: (ref ObjectiveStatus s) => { s.Capture = 1; s.Challenger = ForceId.Friend; s.AreaCenter = new System.Numerics.Vector3(525, 400, 0); s.AreaRadius = 15; s.ProgressNeed = 10; s.ProgressCur = 6; });
        Add(75, "Point C", kind: "HoldArea", owner: ForceId.Hostile, edit: (ref ObjectiveStatus s) => { s.Capture = 1; s.AreaCenter = new System.Numerics.Vector3(650, 400, 0); s.AreaRadius = 15; s.ProgressNeed = 10; s.ProgressCur = 10; });
    }

    [Fact]
    public void TheBar_ReadsTicketsPointsAndTheClock_ByKind()
    {
        Crossfire();
        var g = GameHud.Build(Entries(), Me(1, ForceId.Friend), now: 378);
        Assert.Equal(new[] { (ForceId.Friend, 87f), (ForceId.Hostile, 12f) }, g.Tickets.Select(t => (t.Side, t.Value)));   // the lead counter is no tickets
        Assert.Equal(new[] { "A", "B", "C" }, g.Points.Select(p => p.Letter));
        Assert.Equal(new[] { ForceId.Friend, ForceId.Neutral, ForceId.Hostile }, g.Points.Select(p => p.Owner));
        Assert.Equal("6:18", g.Clock);   // no deadline anywhere ⇒ elapsed
        Assert.False(g.ClockIsCountdown);
        Assert.True(g.HasBar);
    }

    /// <summary>⭐ Measured on showcase-crossfire: a running Counter says Owner <i>Neutral</i>; whose tickets they are is its subject's side.</summary>
    [Fact]
    public void TheTickets_AreOfTheSideOfTheirSubject_WhenTheOwnerIsNeutral()
    {
        Man(5, "Blue", ForceId.Friend); Man(6, "Red", ForceId.Hostile);
        Add(200, "Red tickets", kind: "Counter", owner: ForceId.Neutral, subject: 6, edit: (ref ObjectiveStatus s) => { s.Value = 40; s.Target = 150; });
        Add(201, "Blue tickets", kind: "Counter", owner: ForceId.Neutral, subject: 5, edit: (ref ObjectiveStatus s) => { s.Value = 90; s.Target = 150; });
        ForceId SideOf(RulesReader.Entry e) => e.SubjectNetworkId != 0 && _byId.TryGetValue(e.SubjectNetworkId, out var s) ? _world.GetComponentRO<EntityInfo>(s).ForceId : e.Status.Owner;
        var g = GameHud.Build(Entries(), Me(1, ForceId.Friend), 0, sideOf: SideOf);
        Assert.Equal(new[] { (ForceId.Friend, 90f), (ForceId.Hostile, 40f) }, g.Tickets.Select(t => (t.Side, t.Value)));
    }

    [Fact]
    public void TheContestedPoint_NamesWhoTakesIt_AndHowFar()
    {
        Crossfire();
        var p = GameHud.Build(Entries(), Me(1, ForceId.Friend), 0).Points;
        Assert.False(p[0].Contested); Assert.False(p[2].Contested);                       // full clock = held
        Assert.True(p[1].Contested);
        Assert.Equal(ForceId.Friend, p[1].Challenger);                                    // the status's Challenger field (backend G-8)
        Assert.Equal(0.6f, p[1].Fraction, 3);
    }

    [Fact]
    public void TheRoundClock_IsACountdown_ToTheEarliestTriggerDeadlineStillAhead()
    {
        Crossfire();
        Add(80, "End", role: ObjectiveRole.Trigger, kind: "Trigger", edit: (ref ObjectiveStatus s) => s.Deadline = 900);
        Add(81, "Old", role: ObjectiveRole.Trigger, kind: "Trigger", edit: (ref ObjectiveStatus s) => s.Deadline = 100);   // already past
        var g = GameHud.Build(Entries(), Me(1, ForceId.Friend), 600);
        Assert.Equal("5:00", g.Clock);
        Assert.True(g.ClockIsCountdown);
    }

    [Fact]
    public void TheCaptureStrip_AppearsOnlyWhileIStandInAPointChangingHands()
    {
        Crossfire();
        var inB = GameHud.Build(Entries(), Me(1, ForceId.Friend), 0, viewerAt: new System.Numerics.Vector2(525, 405));
        Assert.Equal("Capturing B · 60 %", inB.Strip!.Text);
        Assert.Equal(ForceId.Friend, inB.Strip.Side);
        Assert.Null(GameHud.Build(Entries(), Me(1, ForceId.Friend), 0, viewerAt: new System.Numerics.Vector2(400, 400)).Strip);   // A is held, not changing hands
        Assert.Null(GameHud.Build(Entries(), Me(1, ForceId.Friend), 0, viewerAt: new System.Numerics.Vector2(0, 0)).Strip);   // outside every point
        Assert.Equal("Contested B · 60 %", GameHud.Build(Entries(), Me(1, ForceId.Hostile), 0, viewerAt: new System.Numerics.Vector2(525, 405)).Strip!.Text);   // Friend takes it, I am Red
    }

    [Fact]
    public void ATrainingScenario_HasNoGameBar()
    {
        Add(90, "Reach the village");
        Assert.False(GameHud.Build(Entries(), Me(1, ForceId.Friend), 50).HasBar);
    }

    // ── S7 P-8 the streak and the bounty

    [Fact]
    public void TheStreak_IsMyCountTitledStreak_AndTheBounty_AnyActiveBountyOnItsSubject()
    {
        Man(7, "Kral", ForceId.Friend); Man(8, "Hawk", ForceId.Hostile);
        Add(100, "Streak", kind: "Count", role: ObjectiveRole.Measure, subject: 7, edit: (ref ObjectiveStatus s) => s.Value = 3);
        Add(101, "Streak", kind: "Count", role: ObjectiveRole.Measure, subject: 8, edit: (ref ObjectiveStatus s) => s.Value = 9);      // his, not mine
        Add(102, "Best streak", kind: "Count", role: ObjectiveRole.Measure, subject: 7, edit: (ref ObjectiveStatus s) => s.Value = 5);  // not the live one
        Add(103, "Bounty", kind: "Bounty", subject: 8, state: ObjectiveState.Active);
        Add(104, "Bounty", kind: "Bounty", subject: 7, state: ObjectiveState.Pending);                                                   // Pending = no bounty
        var g = GameHud.Build(Entries(), Me(7, ForceId.Friend), 0);
        Assert.Equal(3, g.Streak);
        Assert.Equal(new[] { 8 }, g.BountyOn);
        Assert.Equal(0, GameHud.Build(Entries(), Me(99, ForceId.Friend), 0).Streak);
    }

    [Fact]
    public void ThePointArea_IsTheStatusesBoundingCircle_AndAPointIsWhatSaysCapture()
    {
        Crossfire();
        var b = GameHud.Build(Entries(), Me(1, ForceId.Friend), 0).Points[1];
        Assert.True(b.Contains(525, 400)); Assert.True(b.Contains(537, 408)); Assert.False(b.Contains(545, 400));   // 15 m radius
        Assert.False(new PointLine(1, "X", ForceId.Neutral, ForceId.Neutral, 0, false, 0, 0, 0).Contains(0, 0));    // no area known ⇒ contains nothing
        // a HoldArea WITHOUT the Capture flag is a mission objective, not a point — whatever its reason says
        Add(95, "Hold the bridge", kind: "HoldArea", edit: (ref ObjectiveStatus s) => s.Reason = new FixedString64("held 3 of 600 s, owner of the hill"));
        Assert.Equal(3, GameHud.Build(Entries(), Me(1, ForceId.Friend), 0).Points.Count);
        Assert.Contains("Hold the bridge", TrainingHud.Build(Entries(), Me(1, ForceId.Friend), 0).Objectives.Select(o => o.Title));
    }

    // ── W-H3: a rule's side

    [Fact]
    public void ARulesSide_IsItsOwnEntitysForceId_ThenItsSubjects_ThenOwner()
    {
        Man(5, "Blue Platoon", ForceId.Friend);
        var own = Add(300, "Red tickets", subject: 5);                                                      // subject Blue, but the rule itself is Red's
        _world.AddComponent(own, new EntityInfo { Name = new FixedString64("Red tickets"), ForceId = ForceId.Hostile });
        var neutralOwn = Add(301, "Neutral-entity rule", subject: 5);                                       // an entity of no side: the subject decides
        _world.AddComponent(neutralOwn, new EntityInfo { Name = new FixedString64("n"), ForceId = ForceId.Neutral });
        Add(302, "Subject only", subject: 5);
        Add(303, "Owner only", owner: ForceId.Hostile);
        var by = Entries().ToDictionary(e => e.NetworkId);
        Assert.Equal(ForceId.Hostile, MissionSummaryModel.SideOfRule(_world, by[300]));
        Assert.Equal(ForceId.Friend, MissionSummaryModel.SideOfRule(_world, by[301]));
        Assert.Equal(ForceId.Friend, MissionSummaryModel.SideOfRule(_world, by[302]));
        Assert.Equal(ForceId.Hostile, MissionSummaryModel.SideOfRule(_world, by[303]));
        // a rule expanded from a MACRO carries the template's side (Friend for every man): its subject's side wins — Red's score root is Red's
        Man(6, "Red man", ForceId.Hostile);
        var macroRule = Add(304, "Score", kind: "Points", subject: 6);
        _world.AddComponent(macroRule, new EntityInfo { Name = new FixedString64("Score"), ForceId = ForceId.Friend });
        var entry = Entries().First(e => e.NetworkId == 304);
        Assert.Equal(ForceId.Friend, MissionSummaryModel.SideOfRule(_world, entry));                         // authored (no MacroInstance): its own entity
        Assert.Equal(ForceId.Hostile, MissionSummaryModel.SideOfRule(_world, entry with { MacroInstance = 99 }));   // macro-instanced: the subject
        // the HUD's tickets use it: Red's counter is Red's although its subject is Blue
        Add(310, "Blue tickets", kind: "Counter", owner: ForceId.Neutral, subject: 5, edit: (ref ObjectiveStatus s) => { s.Value = 90; s.Target = 150; });
        var red = Add(311, "Red tickets", kind: "Counter", owner: ForceId.Neutral, subject: 5, edit: (ref ObjectiveStatus s) => { s.Value = 40; s.Target = 150; });
        _world.AddComponent(red, new EntityInfo { Name = new FixedString64("Red tickets"), ForceId = ForceId.Hostile });
        var g = GameHud.Build(Entries(), Me(1, ForceId.Friend), 0, sideOf: e => MissionSummaryModel.SideOfRule(_world, e));
        Assert.Equal(new[] { (ForceId.Friend, 90f), (ForceId.Hostile, 40f) }, g.Tickets.Select(t => (t.Side, t.Value)));
    }
}
