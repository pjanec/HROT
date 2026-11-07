using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.ReplayBrowser.Timeline;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.ScenarioRules;
using Hrot.UI.Common.Rules;
using Xunit;

namespace Hrot.Presentation.Tests.Rules;

/// <summary>
/// ⭐⭐ CE-4216 (rules view R2; docs/DESIGN_Rules_View_UI.md RV-3 / RV-5 / RV-6) — the rules view's DATA from synthetic statuses: the tree from <c>ObjectiveLinks.Parent</c>, the three groupings, the side filter (Owner and VisibleSides), the search,
/// every state word, the past spans from the lanes, the fires, Waits on / Starts, the action reasons, and the Objectives-about-a-unit read.
/// </summary>
public sealed class RulesModelTests : IDisposable
{
    private readonly EntityRepository _world = new();
    private readonly Dictionary<int, Entity> _byId = new();

    public RulesModelTests()
    {
        _world.RegisterComponent<NetworkIdentity>();
        _world.RegisterComponent<ObjectiveStatus>();
        _world.RegisterComponent<ObjectiveLinks>();
        _world.RegisterComponent<MacroOrigin>();
        _world.RegisterManagedComponent<MacroInstance>();
        _world.RegisterManagedComponent<Fdp.Toolkit.Behavior.Components.BehaviorStartRecord>();
        _world.RegisterComponent<EntityInfo>();
    }

    public void Dispose() => _world.Dispose();

    private Entity Add(int id, string title, int parent = 0, int subject = 0, ObjectiveState state = ObjectiveState.Active, ObjectiveRole role = ObjectiveRole.Objective,
                       ForceId owner = ForceId.Friend, byte visible = 0, Edit? edit = null)
    {
        var e = _world.CreateEntity();
        _byId[id] = e;
        _world.AddComponent(e, new NetworkIdentity(id));
        var s = new ObjectiveStatus { Title = new FixedString64(title), State = state, Role = role, Owner = owner, VisibleSides = visible, ChangedAt = (SimInstant)0 };
        edit?.Invoke(ref s);
        _world.AddComponent(e, s);
        _world.AddComponent(e, new ObjectiveLinks { Parent = new EntityRef(parent), Subject = new EntityRef(subject) });
        return e;
    }

    private delegate void Edit(ref ObjectiveStatus s);

    private void Macro(int ruleId, int instanceId, string macro)
    {
        if (!_byId.ContainsKey(instanceId))
        {
            var inst = _world.CreateEntity();
            _byId[instanceId] = inst;
            _world.AddComponent(inst, new NetworkIdentity(instanceId));
            _world.SetManagedComponent(inst, new MacroInstance { Macro = macro });
        }
        _world.AddComponent(_byId[ruleId], new MacroOrigin { Instance = new EntityRef(instanceId) });
    }

    private RulesView Build(RulesOptions? o = null, IRulesHistory? h = null, double now = 100) => RulesModel.Build(_world, o ?? new RulesOptions(), h, now);

    private sealed class History : IRulesHistory
    {
        public Dictionary<int, List<(double, ObjectiveState)>> States = new();
        public Dictionary<int, List<(double, int)>> Fired = new();
        public IReadOnlyList<(double Time, ObjectiveState State)> StateChanges(int id) => States.TryGetValue(id, out var l) ? l : new();
        public IReadOnlyList<(double Time, int Count)> Fires(int id) => Fired.TryGetValue(id, out var l) ? l : new();
    }

    // ── the tree ────────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The tree comes from <c>ObjectiveLinks.Parent</c>: parent first, children indented, <c>HasChildren</c> on the parent only.
    /// ✅ Red-proof: the depth taken as 0 for every row (the parent walk dropped) ⇒ red.</summary>
    [Fact]
    public void TheTree_ComesFromParent_ChildrenIndentedUnderTheirParent()
    {
        Add(12, "Hold", parent: 10); Add(10, "Mission"); Add(11, "Reach", parent: 10); Add(13, "Deep", parent: 11);
        var rows = Build().Rows.ToList();
        Assert.Equal(new[] { 10, 11, 13, 12 }, rows.Select(r => r.NetworkId));            // depth-first, siblings by id
        Assert.Equal(new[] { 0, 1, 2, 1 }, rows.Select(r => r.Depth));
        Assert.Equal(new[] { true, true, false, false }, rows.Select(r => r.HasChildren));
    }

    [Fact]
    public void ACycleInTheLinks_DoesNotLoopTheView()
    {
        Add(1, "A", parent: 2); Add(2, "B", parent: 1);
        Assert.Equal(0, Build().Rows.Count());   // a pure cycle has no root: shown by nobody, and — above all — it terminates
    }

    // ── the three groupings ─────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void GroupByMacro_PutsMacroRowsUnderTheirMacro_AndTheRestUnderScenario_ScenarioFirst()
    {
        Add(1, "Authored"); Add(2, "Expanded A"); Add(3, "Expanded B");
        Macro(2, 500, "test/fire-team"); Macro(3, 500, "test/fire-team");
        var groups = Build(new RulesOptions(RulesGroupBy.Macro)).Groups;
        Assert.Equal(new[] { "Scenario", "test/fire-team" }, groups.Select(g => g.Name));
        Assert.Equal(new[] { 1 }, groups[0].Rows.Select(r => r.NetworkId));
        Assert.Equal(new[] { 2, 3 }, groups[1].Rows.Select(r => r.NetworkId));
    }

    [Fact]
    public void GroupBySubject_GroupsByTheSubjectsName_AndNoSubjectLast()
    {
        Add(1, "About 900", subject: 900); Add(2, "Also 900", subject: 900); Add(3, "About 901", subject: 901); Add(4, "Free");
        var groups = Build(new RulesOptions(RulesGroupBy.Subject)).Groups;
        Assert.Equal(RulesModel.NoSubjectGroup, groups[^1].Name);
        Assert.Equal(new[] { 4 }, groups[^1].Rows.Select(r => r.NetworkId));
        Assert.Equal(new[] { 2, 1 }, groups.Where(g => g.Rows.Any(r => r.NetworkId is 1 or 2)).Single().Rows.Select(r => r.NetworkId).OrderByDescending(i => i));
        Assert.Equal(3, groups.Count);
    }

    [Fact]
    public void GroupBySide_IsBlueRedNeutral_ByOwner()
    {
        Add(1, "R", owner: ForceId.Hostile); Add(2, "B", owner: ForceId.Friend); Add(3, "N", owner: ForceId.Neutral);
        Assert.Equal(new[] { "Blue", "Red", "Neutral" }, Build(new RulesOptions(RulesGroupBy.Side)).Groups.Select(g => g.Name));
    }

    // ── the side filter (Owner and VisibleSides) and the search ─────────────────────────────────────────────────────────────

    /// <summary>A side sees its own rules AND the ones made visible to it (<c>VisibleSides</c> bit). ✅ Red-proof: the VisibleSides clause dropped from <c>ShownTo</c> ⇒ the Red-owned, Blue-visible row vanishes ⇒ red.</summary>
    [Fact]
    public void TheSideFilter_ShowsWhatTheSideOwns_AndWhatIsVisibleToIt()
    {
        Add(1, "Blue own", owner: ForceId.Friend);
        Add(2, "Red own", owner: ForceId.Hostile);
        Add(3, "Red but seen by Blue", owner: ForceId.Hostile, visible: (byte)(1 << (int)ForceId.Friend));
        var blue = Build(new RulesOptions(Side: ForceId.Friend)).Rows.Select(r => r.NetworkId).OrderBy(i => i).ToList();
        Assert.Equal(new[] { 1, 3 }, blue);
        Assert.Equal(new[] { 2, 3 }.Concat(Array.Empty<int>()), Build(new RulesOptions(Side: ForceId.Hostile)).Rows.Select(r => r.NetworkId).OrderBy(i => i));
    }

    [Fact]
    public void TheSearch_IsByTitle_AndKeepsTheAncestorsSoTheTreeStaysWhole()
    {
        Add(10, "Mission"); Add(11, "Reach the bridge", parent: 10); Add(12, "Hold", parent: 10);
        var rows = Build(new RulesOptions(Search: "BRIDGE")).Rows.ToList();
        Assert.Equal(new[] { 10, 11 }, rows.Select(r => r.NetworkId));
        Assert.Equal(new[] { 0, 1 }, rows.Select(r => r.Depth));
    }

    // ── the state in words and colour ───────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ObjectiveState.Pending, "Pending")]
    [InlineData(ObjectiveState.Achieved, "Achieved")]
    [InlineData(ObjectiveState.Failed, "Failed")]
    [InlineData(ObjectiveState.Inactive, "Inactive")]
    [InlineData(ObjectiveState.Skipped, "Skipped")]
    public void AnObjectiveState_IsItsName(ObjectiveState state, string words)
    {
        var s = new ObjectiveStatus { State = state };
        Assert.Equal(words, RulesModel.StateWords(s, 50));
    }

    [Fact]
    public void AnActiveObjective_ShowsHowLongItHasBeenActive_AsMinutesSeconds()
        => Assert.Equal("Active 1:05", RulesModel.StateWords(new ObjectiveStatus { State = ObjectiveState.Active, ChangedAt = (SimInstant)10 }, 75));

    [Theory]
    [InlineData(TriggerState.Armed, "Armed")]
    [InlineData(TriggerState.Holding, "Holding")]
    [InlineData(TriggerState.Waiting, "Waiting")]
    [InlineData(TriggerState.Done, "Done")]
    [InlineData(TriggerState.Disarmed, "Disarmed")]
    public void AnActiveTrigger_ShowsItsMachine(TriggerState state, string words)
        => Assert.Equal(words, RulesModel.StateWords(new ObjectiveStatus { Role = ObjectiveRole.Trigger, State = ObjectiveState.Active, TriggerState = state }, 50));

    [Fact]
    public void ATriggerActing_ShowsTheStep_AndACoolingOneShowsItsFires()
    {
        var acting = new ObjectiveStatus { Role = ObjectiveRole.Trigger, State = ObjectiveState.Active, TriggerState = TriggerState.Acting, StepCur = 2, StepCount = 3 };
        var cooling = new ObjectiveStatus { Role = ObjectiveRole.Trigger, State = ObjectiveState.Active, TriggerState = TriggerState.Cooldown, FireCount = 2 };
        Assert.Equal("Acting 2/3", RulesModel.StateWords(acting, 50));
        Assert.Equal("Cooldown · 2×", RulesModel.StateWords(cooling, 50));
        Assert.Equal((RulesModel.Acting, RulesModel.Cooldown), (RulesModel.ColourOf(acting), RulesModel.ColourOf(cooling)));
    }

    [Fact]
    public void TheColours_FollowTheMockupsLegend()
    {
        uint C(ObjectiveState s) => RulesModel.ColourOf(new ObjectiveStatus { State = s });
        Assert.Equal((RulesModel.Achieved, RulesModel.Active, RulesModel.Failed, RulesModel.Grey), (C(ObjectiveState.Achieved), C(ObjectiveState.Active), C(ObjectiveState.Failed), C(ObjectiveState.Inactive)));
    }

    // ── the past ────────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The lane's state changes become spans change→change, the last one runs to now in the status's own state. ✅ Red-proof: the last span not extended to now ⇒ red.</summary>
    [Fact]
    public void ThePastSpans_AreTheLanesStateChanges_TheLastRunningToNow()
    {
        Add(10, "Reach", state: ObjectiveState.Achieved);
        var h = new History { States = { [10] = new() { (0, ObjectiveState.Inactive), (20, ObjectiveState.Active), (80, ObjectiveState.Achieved) } } };
        var spans = Build(h: h, now: 100).Find(10)!.Spans;
        Assert.Equal(new[] { (0d, 20d, ObjectiveState.Inactive), (20d, 80d, ObjectiveState.Active), (80d, 100d, ObjectiveState.Achieved) }, spans.Select(s => (s.From, s.To, s.State)));
    }

    [Fact]
    public void WithNoHistory_TheOneSpanIsChangedAtToNow_InTheStatusState()
    {
        var e = Add(10, "Reach");
        ref var s = ref _world.GetComponentRW<ObjectiveStatus>(e); s.ChangedAt = (SimInstant)30;
        var span = Assert.Single(Build(now: 100).Find(10)!.Spans);
        Assert.Equal((30d, 100d, ObjectiveState.Active, false), (span.From, span.To, span.State, span.Hatched));
    }

    [Fact]
    public void ATriggerWaiting_HasItsCurrentSpanHatched()
    {
        var e = Add(10, "T", role: ObjectiveRole.Trigger);
        ref var s = ref _world.GetComponentRW<ObjectiveStatus>(e); s.TriggerState = TriggerState.Cooldown; s.ChangedAt = (SimInstant)40;
        Assert.True(Assert.Single(Build(now: 100).Find(10)!.Spans).Hatched);
    }

    [Fact]
    public void TheFires_AreTheLanesDiamonds_ElseTheOneTheStatusRemembers()
    {
        var e = Add(10, "T", role: ObjectiveRole.Trigger);
        ref var s = ref _world.GetComponentRW<ObjectiveStatus>(e); s.FireCount = 2; s.LastFired = 75;
        Assert.Equal(new[] { 75d }, Build().Find(10)!.Fires);
        var h = new History { Fired = { [10] = new() { (30, 1), (75, 2) } } };
        Assert.Equal(new[] { 30d, 75d }, Build(h: h).Find(10)!.Fires);
    }

    /// <summary>The ONE history: <see cref="IndexRulesHistory"/> reads the <c>objective.state</c> and <c>rules.fired</c> lanes at the frames' sim times, a fire of Value 0 skipped.</summary>
    [Fact]
    public void TheIndexHistory_ReadsTheTwoLanesAtSimTimes_SkippingAZeroFire()
    {
        var lanes = new Dictionary<string, SharedList<TimelineEvent>>
        {
            [ScenarioRulesTimelineLanes.State.Id] = new(new[] { new TimelineEvent(0, ScenarioRulesTimelineLanes.State.Id, 10, 0, 0, 0, (float)ObjectiveState.Inactive), new TimelineEvent(2, ScenarioRulesTimelineLanes.State.Id, 10, 0, 0, 0, (float)ObjectiveState.Active) }),
            [ScenarioRulesTimelineLanes.Fired.Id] = new(new[] { new TimelineEvent(0, ScenarioRulesTimelineLanes.Fired.Id, 10, 0, 0, 0, 0f), new TimelineEvent(3, ScenarioRulesTimelineLanes.Fired.Id, 10, 0, 0, 0, 1f) }),
        };
        var index = new TimelineIndex(lanes, new SharedList<long>(new long[] { 0, 10, 20, 30 }), 4, 4, null, new SharedList<double>(new[] { 0d, 5d, 10d, 15d }));
        var h = new IndexRulesHistory(index);
        Assert.Equal(new[] { (0d, ObjectiveState.Inactive), (10d, ObjectiveState.Active) }, h.StateChanges(10));
        Assert.Equal(new[] { (15d, 1) }, h.Fires(10));
    }

    // ── links, actions, overview ────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void WaitsOn_AndStarts_AreTheLinkIds_ARuleLinkSelectsItsRow_AnEntityLinkDoesNot()
    {
        Add(20, "Prerequisite");
        Add(21, "Do it", role: ObjectiveRole.Trigger, edit: (ref ObjectiveStatus r) =>
        {
            r.DependsOnCount = 1; r.DependsOn[0] = 20;
            r.ActsOnCount = 2; r.ActsOn[0] = 20; r.ActsOn[1] = 777;
        });
        var row = Build().Find(21)!;
        Assert.Equal(new[] { (20, true) }, row.WaitsOn.Select(l => (l.NetworkId, l.IsRule)));
        Assert.Equal("Prerequisite", row.WaitsOn[0].Text);
        Assert.Equal(new[] { (20, true), (777, false) }, row.Starts.Select(l => (l.NetworkId, l.IsRule)));
    }

    [Fact]
    public void AMacroInstanceStarted_IsNamedWithItsMacroAndUnitCount()
    {
        Add(30, "Spawn the team", role: ObjectiveRole.Trigger, edit: (ref ObjectiveStatus r) => { r.ActsOnCount = 1; r.ActsOn[0] = 500; });
        Add(31, "Member"); Add(32, "Member 2");
        Macro(31, 500, "test/fire-team"); Macro(32, 500, "test/fire-team");
        Assert.Contains("test/fire-team", Build().Find(30)!.Starts[0].Text);
        Assert.Contains("2 units", Build().Find(30)!.Starts[0].Text);
    }

    /// <summary>RV-5: an item is ALWAYS listed; an invalid one carries its reason. ✅ Red-proof: <c>DisabledReason</c> returning null for a final objective's Deactivate ⇒ red.</summary>
    [Fact]
    public void TheActions_AreAlwaysListed_AnInvalidOneCarriesItsReason()
    {
        var done = new ObjectiveStatus { Role = ObjectiveRole.Objective, State = ObjectiveState.Achieved };
        var actions = RulesModel.ActionsOf(done);
        Assert.Equal(new[] { "Activate", "Deactivate", "Reset" }, actions.Select(a => a.Label));
        Assert.Contains("Achieved", actions[0].DisabledReason);   // needs Reset
        Assert.Null(actions[2].DisabledReason);                   // a Reset restarts any rule, a final one too

        var armed = new ObjectiveStatus { Role = ObjectiveRole.Trigger, State = ObjectiveState.Active, TriggerState = TriggerState.Armed };
        var t = RulesModel.ActionsOf(armed);
        Assert.Equal(new[] { "Fire now", "Disarm", "Re-arm", "Reset" }, t.Select(a => a.Label));
        Assert.Null(t[0].DisabledReason);
        Assert.NotNull(RuleCommands.DisabledReason(new ObjectiveStatus { Role = ObjectiveRole.Trigger, State = ObjectiveState.Active, TriggerState = TriggerState.Done }, RuleRequest.Fire));
        Assert.NotNull(RuleCommands.DisabledReason(new ObjectiveStatus { Closed = 1 }, RuleRequest.Reset));
    }

    [Fact]
    public void TheOverview_CarriesTitleKindStateOwnerSubjectProgressScoreReasonAndFires()
    {
        Add(10, "Reach the bridge", subject: 900, edit: (ref ObjectiveStatus r) =>
        {
            r.Kind = new FixedString32("ReachArea"); r.ProgressCur = 1; r.ProgressNeed = 4; r.Score = 0.5f; r.Pass = 0.6f;
            r.Required = 1; r.Scored = 1; r.Reason = new FixedString64("1/4 men");
        });
        var lines = RulesModel.OverviewOf(Build().Find(10)!, 100).ToDictionary(l => l.Label, l => l.Value);
        Assert.Equal("Reach the bridge", lines["Title"]);
        Assert.Equal("Objective · ReachArea", lines["Kind"]);
        Assert.Equal("Blue", lines["Owner"]);
        Assert.Equal("1 / 4", lines["Progress"]);
        Assert.Equal("0.5", lines["Score"]);
        Assert.Contains("NO-GO", lines["Pass"]);
        Assert.Equal("required · scored", lines["Required / scored"]);
        Assert.Equal("1/4 men", lines["Reason"]);
        Assert.True(lines.ContainsKey("Subject"));
        var trig = Add(11, "T", role: ObjectiveRole.Trigger, edit: (ref ObjectiveStatus r) => { r.FireCount = 3; r.LastFired = 65; });
        Assert.Equal("3× · last at 1:05", RulesModel.OverviewOf(Build().Find(11)!, 100).Single(l => l.Label == "Fired").Value);
    }

    // ── RV-7: objectives about this unit ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ObjectivesAboutAUnit_AreTheRowsWhoseSubjectItIs_TheSameRowsTheViewDraws()
    {
        var unit = _world.CreateEntity(); _world.AddComponent(unit, new NetworkIdentity(900));
        Add(1, "About it", subject: 900); Add(2, "About another", subject: 901); Add(3, "Also about it", subject: 900, state: ObjectiveState.Achieved);
        var rows = ObjectivesAboutModel.Of(_world, unit, 100);
        Assert.Equal(new[] { 1, 3 }, rows.Select(r => r.NetworkId).OrderBy(i => i));
        Assert.Equal(Build().Find(3)!.StateWords, rows.Single(r => r.NetworkId == 3).StateWords);
        var other = _world.CreateEntity(); _world.AddComponent(other, new NetworkIdentity(950));
        Assert.Empty(ObjectivesAboutModel.Of(_world, other, 100));   // empty ⇒ the section is not offered
    }

    // ── RV-3a: the rows BEFORE a run (authored params, no status) ─────────────────────────────────────────────────────────

    private Entity Authored(int id, string kind, string json)
    {
        var e = _world.CreateEntity();
        _byId[id] = e;
        _world.AddComponent(e, new NetworkIdentity(id));
        _world.SetManagedComponent(e, new Fdp.Toolkit.Behavior.Components.BehaviorStartRecord { BehaviorName = kind, JsonParams = json, InstanceId = 1 });
        return e;
    }

    private void Unit(int id, ForceId side, string name)
    {
        var e = _world.CreateEntity();
        _byId[id] = e;
        _world.AddComponent(e, new NetworkIdentity(id));
        _world.AddComponent(e, new EntityInfo { Name = new FixedString64(name), ForceId = side });
    }

    /// <summary>One row type, two sources: with no status the row is read from the authored <c>common</c> block — tree from <c>parent</c>, title, subject, group, state <i>Authored</i> (grey), no past, every action disabled with the reason. ✅ Red-proof: the authored pass dropped from <c>RulesReader.Read</c> ⇒ no rows ⇒ red.</summary>
    [Fact]
    public void AnAuthoredRule_HasARow_FromItsCommonBlock_TreeFromParent_AuthoredState_ActionsDisabledWithTheReason()
    {
        Authored(10, "WeightedMean", """{"common":{"title":"Mission","subject":{"NetworkId":900,"Ref":"@Blue"},"group":"mission","required":true}}""");
        Authored(11, "ReachArea", """{"common":{"title":"Reach the bridge","parent":{"NetworkId":10,"Ref":"@M"},"subject":901}}""");
        var rows = Build().Rows.ToList();
        Assert.Equal(new[] { 10, 11 }, rows.Select(r => r.NetworkId));
        Assert.Equal(new[] { 0, 1 }, rows.Select(r => r.Depth));
        var m = rows[0];
        Assert.Equal(("Mission", RulesModel.AuthoredWords, RulesModel.Grey, true, 900), (m.Title, m.StateWords, m.StateColour, m.Authored, m.SubjectNetworkId));
        Assert.Empty(m.Spans);
        Assert.Equal(("WeightedMean", "mission", (byte)1), (m.Status.Kind.ToString(), m.Status.Group.ToString(), m.Status.Required));
        var actions = RulesModel.ActionsOf(m);
        Assert.Equal(new[] { "Activate", "Deactivate", "Reset" }, actions.Select(a => a.Label));
        Assert.All(actions, a => Assert.Equal(RulesModel.NotRunning, a.DisabledReason));
    }

    [Theory]
    [InlineData("Trigger", ObjectiveRole.Trigger)]
    [InlineData("Count", ObjectiveRole.Measure)]
    [InlineData("SelectionMean", ObjectiveRole.Measure)]
    [InlineData("ReachArea", ObjectiveRole.Objective)]
    public void TheRoleOfAnAuthoredRule_IsDecidedByItsKind(string kind, ObjectiveRole role)
    {
        Authored(10, kind, """{"common":{"title":"x"}}""");
        var row = Build().Find(10)!;
        Assert.Equal((role, kind), (row.Status.Role, row.Status.Kind.ToString()));
        Assert.Equal(RulesModel.IconOf(role), row.Icon);
        if (role == ObjectiveRole.Trigger) Assert.Equal(new[] { "Fire now", "Disarm", "Re-arm", "Reset" }, RulesModel.ActionsOf(row).Select(a => a.Label));
    }

    [Fact]
    public void AuthoredRules_GroupBySubject_BySide_AndByMacro_LikeRunningOnes()
    {
        Unit(900, ForceId.Hostile, "Red squad"); Unit(901, ForceId.Friend, "Blue squad");
        Authored(10, "ReachArea", """{"common":{"title":"A","subject":900}}""");
        Authored(11, "ReachArea", """{"common":{"title":"B","subject":901}}""");
        Authored(12, "ReachArea", """{"common":{"title":"C"}}""");
        Macro(12, 500, "test/fire-team");
        Assert.Equal(new[] { "Blue", "Red", "Neutral" }, Build(new RulesOptions(RulesGroupBy.Side)).Groups.Select(g => g.Name));
        Assert.Equal(new[] { "Blue squad", "Red squad", RulesModel.NoSubjectGroup }, Build(new RulesOptions(RulesGroupBy.Subject)).Groups.Select(g => g.Name).OrderBy(n => n == RulesModel.NoSubjectGroup ? 1 : 0).ThenBy(n => n));
        Assert.Equal(new[] { "Scenario", "test/fire-team" }, Build(new RulesOptions(RulesGroupBy.Macro)).Groups.Select(g => g.Name));
        Assert.Equal(new[] { 11 }, Build(new RulesOptions(Side: ForceId.Friend)).Rows.Select(r => r.NetworkId));
    }

    /// <summary>Waits on = every <c>ref</c> in the <c>when</c> (recursively); Starts = every <c>target</c> / <c>ref</c> of the <c>do</c> steps. ✅ Red-proof: <c>Collect</c> not recursing into arrays ⇒ the nested ref is lost ⇒ red.</summary>
    [Fact]
    public void WaitsOnAndStarts_AreDerivedFromTheAuthoredWhenAndDo()
    {
        Authored(20, "ReachArea", """{"common":{"title":"Prerequisite"}}""");
        Authored(21, "Trigger", """
            {"common":{"title":"Counter-attack"},
             "when":{"$type":"And","all":[{"$type":"OnEntity","ref":{"NetworkId":20,"Ref":"@P"},"predicate":{}},{"$type":"OnEntity","ref":1038,"predicate":{}}]},
             "do":[{"act":"InsertMacro","target":{"NetworkId":500}},{"act":"Say","ref":20},{"act":"Wait"}]}
            """);
        var row = Build().Find(21)!;
        Assert.Equal(new[] { (20, true), (1038, false) }, row.WaitsOn.Select(l => (l.NetworkId, l.IsRule)));
        Assert.Equal(new[] { (500, false), (20, true) }, row.Starts.Select(l => (l.NetworkId, l.IsRule)));
    }

    [Fact]
    public void WhenARuleHasAStatus_TheStatusWins_NoSecondRow()
    {
        Add(10, "Running title", state: ObjectiveState.Active);
        _world.SetManagedComponent(_byId[10], new Fdp.Toolkit.Behavior.Components.BehaviorStartRecord { BehaviorName = "ReachArea", JsonParams = """{"common":{"title":"Authored title"}}""", InstanceId = 1 });   // the SAME entity carries both
        var row = Assert.Single(Build().Rows);
        Assert.False(row.Authored);
        Assert.Equal("Running title", row.Title);
    }

    [Fact]
    public void AParamsBlobWithoutACommonBlock_IsNotARule()
    {
        Authored(10, "SomeTask", """{"speed":4}""");
        Authored(11, "Broken", "{not json");
        Assert.Empty(Build().Rows);
    }
    /// <summary>⭐ W-H3 — the side filter and the Side grouping read the rule's OWN side (its entity's <c>EntityInfo.ForceId</c>, else its subject's, else Owner), not <c>Owner</c> — a capture field that is Neutral on every other rule.</summary>
    [Fact]
    public void TheSideFilterAndGrouping_ReadTheRulesOwnSide_NotOwner()
    {
        var red = Add(60, "Red tickets", owner: ForceId.Neutral); _world.AddComponent(red, new EntityInfo { Name = new FixedString64("Red tickets"), ForceId = ForceId.Hostile });
        var blue = Add(61, "Blue tickets", owner: ForceId.Neutral); _world.AddComponent(blue, new EntityInfo { Name = new FixedString64("Blue tickets"), ForceId = ForceId.Friend });
        Add(62, "Seen by both", owner: ForceId.Neutral, visible: (byte)((1 << (int)ForceId.Friend) | (1 << (int)ForceId.Hostile)));
        Assert.Equal(new[] { "Red tickets", "Seen by both" }, Build(new RulesOptions(Side: ForceId.Hostile)).Rows.Select(r => r.Title).OrderBy(x => x));
        Assert.Equal(new[] { "Blue tickets", "Seen by both" }, Build(new RulesOptions(Side: ForceId.Friend)).Rows.Select(r => r.Title).OrderBy(x => x));
        var groups = Build(new RulesOptions(GroupBy: RulesGroupBy.Side)).Groups.ToDictionary(g => g.Name, g => g.Rows.Select(r => r.Title).ToList());
        Assert.Contains("Red tickets", groups["Red"]); Assert.Contains("Blue tickets", groups["Blue"]);
    }

    // ── ⭐ W-1 (P-8, Mission plan §5.5 M6, D5) — the Phases group ─────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ The phase triggers lead in their own group <b>Phases</b>, ordered by phase number, and leave the group they would otherwise be in; an
    /// AUTHORED phase is grouped too (its <c>common.phase</c> projected before a run). 🔴 Red-proof: the authored projection of <c>phase</c> dropped
    /// ⇒ "Approach" stays under Scenario ⇒ red.
    /// </summary>
    [Fact]
    public void W1_PhaseTriggersLeadInThePhasesGroup_ByNumber_AuthoredOnesToo()
    {
        Add(1, "Assault", role: ObjectiveRole.Trigger, edit: (ref ObjectiveStatus s) => s.Phase = 2);
        Add(2, "Hold the bridge");
        Add(3, "Brief Blue", role: ObjectiveRole.Trigger);
        Authored(4, "Trigger", """{"common":{"title":"Approach","phase":1}}""");
        var v = Build(new RulesOptions());
        Assert.Equal(RulesModel.PhasesGroup, v.Groups[0].Name);
        Assert.Equal(new[] { "Approach", "Assault" }, v.Groups[0].Rows.Select(r => r.Title));
        Assert.DoesNotContain(v.Groups.Skip(1).SelectMany(g => g.Rows), r => r.Status.Phase > 0);
        Assert.Contains(v.Groups.Skip(1).SelectMany(g => g.Rows), r => r.Title == "Brief Blue");
    }

    /// <summary>⭐ A phase row's right side: nothing before a run · <i>Begin now</i> while it has not fired · <i>begun m:ss</i> once it has (mockup B).</summary>
    [Fact]
    public void W1_APhaseRow_OffersBeginNow_UntilItBegins()
    {
        Add(1, "Assault", role: ObjectiveRole.Trigger, edit: (ref ObjectiveStatus s) => s.Phase = 2);
        Add(2, "Approach", role: ObjectiveRole.Trigger, edit: (ref ObjectiveStatus s) => { s.Phase = 1; s.FireCount = 1; s.LastFired = 0; });
        Authored(3, "Trigger", """{"common":{"title":"Consolidate","phase":3}}""");
        var rows = Build(new RulesOptions()).Groups[0].Rows;
        Assert.Equal((false, (string?)null, "begun 0:00"), Hrot.Presentation.Windows.RulesWindow.PhaseRightOf(rows.Single(r => r.Title == "Approach")));
        var waiting = Hrot.Presentation.Windows.RulesWindow.PhaseRightOf(rows.Single(r => r.Title == "Assault"));
        Assert.True(waiting.BeginNow); Assert.Null(waiting.State);
        Assert.Equal((false, (string?)null, (string?)null), Hrot.Presentation.Windows.RulesWindow.PhaseRightOf(rows.Single(r => r.Title == "Consolidate")));
    }

    /// <summary>⭐ How a phase begins, in words: at the start (scenario time 0), when … (the ONE condition sentence), with a no-later-than.</summary>
    [Fact]
    public void W1_HowAPhaseBegins_InWords()
    {
        Assert.Equal("begins at the start", Hrot.UI.Common.Rules.RuleSentence.PhaseBegins("""{"when":{"$type":"ScenarioTime","fromSeconds":0}}""", 0, null));
        Assert.StartsWith("begins when ", Hrot.UI.Common.Rules.RuleSentence.PhaseBegins("""{"when":{"$type":"ScenarioTime","fromSeconds":600}}""", 0, null));
        Assert.EndsWith(" · latest 20:00", Hrot.UI.Common.Rules.RuleSentence.PhaseBegins("""{"when":{"$type":"ScenarioTime","fromSeconds":0}}""", 1200, null));
        Assert.Equal("begins when (no condition yet)", Hrot.UI.Common.Rules.RuleSentence.PhaseBegins("{}", 0, null));
    }
}
