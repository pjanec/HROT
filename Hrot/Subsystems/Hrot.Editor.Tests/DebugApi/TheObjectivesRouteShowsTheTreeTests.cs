using System;
using System.Linq;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.ScenarioRules;
using Hrot.Editor.DebugApi;
using Hrot.Presentation.DebugApi;
using Xunit;

namespace Hrot.Editor.Tests.DebugApi;

/// <summary>⭐ rules S1-g — <c>GET /objectives</c>: the statuses on this node's world as rows, the tree by <c>ObjectiveLinks.Parent</c>.</summary>
public sealed class TheObjectivesRouteShowsTheTreeTests : IDisposable
{
    private readonly EntityRepository _world = new();
    private readonly NetworkEntityMap _map = new();
    private readonly DebugApiService _svc;

    public TheObjectivesRouteShowsTheTreeTests()
    {
        _world.RegisterComponent<NetworkIdentity>();
        _world.RegisterComponent<ObjectiveStatus>();
        _world.RegisterComponent<ObjectiveLinks>();
        _world.RegisterComponent<RuleControl>();
        _world.RegisterComponent<RuleLeaves>();
        _svc = new DebugApiService(new PerspectiveScopedDispatcher(
            new ISubsystemDebugProvider[] { new SubsystemDebugProvider("CGF", "Scenario", world: () => _world, entityMap: () => _map) },
            currentPerspective: () => "Scenario", acksPending: null));
    }

    public void Dispose() => _world.Dispose();

    private void Add(int id, string title, string kind, ObjectiveState state, int parent = 0, float score = 0f, float pass = 0f, byte stepCur = 0, byte stepCount = 0)
    {
        var e = _world.CreateEntity();
        _map.Register(id, e);
        _world.AddComponent(e, new NetworkIdentity(id));
        _world.AddComponent(e, new ObjectiveStatus { Title = new FixedString64(title), Kind = new FixedString32(kind), State = state, Score = score, Pass = pass, ProgressCur = 1, ProgressNeed = 4, Reason = new FixedString64("1/4"), Group = new FixedString32(kind == "ReachArea" ? "fire" : ""), StepCur = stepCur, StepCount = stepCount });
        _world.AddComponent(e, new ObjectiveLinks { Parent = new EntityRef(parent), Subject = new EntityRef(900) });
    }

    /// <summary>A root WeightedMean with two children: three rows, the root alone in <c>roots</c>, its <c>children</c> the two ids, the state as a name.
    /// ✅ Red-proof: a child listed as a root (the parent test dropped) ⇒ red.</summary>
    [Fact]
    public void TheRoute_AnswersTheThreeRows_TheRootAndItsChildren_WithStateNames()
    {
        Add(10, "Squad mission", "WeightedMean", ObjectiveState.Active, pass: 0.6f);
        Add(12, "Hold", "HoldArea", ObjectiveState.Failed, parent: 10);
        Add(11, "Reach", "ReachArea", ObjectiveState.Achieved, parent: 10, score: 1f, pass: 1f, stepCur: 2, stepCount: 3);

        var body = _svc.GetObjectives();
        var rows = body["objectives"]!.AsArray();
        Assert.Equal(3, rows.Count);
        Assert.Equal(new[] { 10 }, body["roots"]!.AsArray().Select(n => n!.GetValue<int>()));
        Assert.Equal(new[] { 10, 12, 11 }.Select(i => i).OrderBy(i => i == 10 ? 0 : i == 12 ? 1 : 2), rows.Select(r => r!["networkId"]!.GetValue<int>()));   // root first, then the children by title (Hold, Reach)
        Assert.Equal(new[] { 12, 11 }, rows[0]!["children"]!.AsArray().Select(n => n!.GetValue<int>()));
        Assert.Equal("Active", (string)rows[0]!["state"]!);
        Assert.Equal(("Achieved", "ReachArea", 1f, 10, 900), ((string)rows[2]!["state"]!, (string)rows[2]!["kind"]!, (float)rows[2]!["score"]!, (int)rows[2]!["parent"]!, (int)rows[2]!["subject"]!));
        Assert.Equal(("fire", ""), ((string)rows[2]!["group"]!, (string)rows[1]!["group"]!));   // ⭐ rules S4 E-3: the evaluation group
        Assert.Equal((1, 4, "1/4"), ((int)rows[2]!["progress"]!["cur"]!, (int)rows[2]!["progress"]!["need"]!, (string)rows[2]!["reason"]!));
        Assert.Equal((0.6f, 1f), ((float)rows[0]!["pass"]!, (float)rows[2]!["pass"]!));   // ⭐ rules S4 ②: the pass mark — GO is score >= pass ✅ Red-proof: "pass" dropped from the row ⇒ red
        Assert.Equal((2, 3, 0, 0), ((int)rows[2]!["stepCur"]!, (int)rows[2]!["stepCount"]!, (int)rows[0]!["stepCur"]!, (int)rows[0]!["stepCount"]!));   // ⭐ rule editor ①: the running do step ✅ Red-proof: "stepCur" dropped from the row ⇒ red
        Assert.Null(rows[0]!["parent"]);

        var nested = _svc.GetObjectives(tree: true);
        Assert.Equal(2, nested["objectives"]![0]!["children"]!.AsArray().Count);
        Assert.Equal("Hold", (string)nested["objectives"]![0]!["children"]![0]!["title"]!);
    }

    /// <summary>⭐ rules S4 ① — the role / standard means an evaluation inserted are plain rows: children of the evaluation (Parent = it), kind <c>SelectionMean</c>, group <c>roles</c> for a role mean, the pass mark 0.6.
    /// (A pin: the route is kind-agnostic, so the means need no code of its own — not red-proved.)</summary>
    [Fact]
    public void TheSelectionMeans_AreChildrenOfTheEvaluation_WithTheirKindGroupAndPass()
    {
        Add(50, "Evaluation", "MemberEvaluation", ObjectiveState.Active, pass: 0.6f);
        Add(51, "Member standards", "WeightedMean", ObjectiveState.Active, parent: 50, score: 0.5f, pass: 0.6f);
        Add(52, "All SquadLeader", "SelectionMean", ObjectiveState.Active, parent: 50, score: 0.5f, pass: 0.6f);
        _world.GetComponentRW<ObjectiveStatus>(_map.TryGetEntity(52, out var m) ? m : default).Group = new FixedString32("roles");

        var tree = _svc.GetObjectives(tree: true)["objectives"]!.AsArray();
        var root = Assert.Single(tree)!;
        var kids = root["children"]!.AsArray();
        Assert.Equal(new[] { "All SquadLeader", "Member standards" }, kids.Select(k => (string)k!["title"]!));
        var mean = kids[0]!;
        Assert.Equal(("SelectionMean", "roles", 0.6f, 50), ((string)mean["kind"]!, (string)mean["group"]!, (float)mean["pass"]!, (int)mean["parent"]!));
    }

    /// <summary>⭐ G-8 (CE-4230) — a capture point's facts are on its row: <c>capture</c>, <c>challenger</c> (a ForceId name), <c>areaCenter</c> [x,y,z], <c>areaRadius</c>; a plain objective reads false / Neutral / 0.
    /// ✅ Red-proof: <c>"areaCenter"</c> dropped from the row ⇒ red.</summary>
    [Fact]
    public void ACapturePointRow_CarriesItsFacts()
    {
        Add(60, "Hold B", "HoldArea", ObjectiveState.Active);
        Add(61, "Reach", "ReachArea", ObjectiveState.Active);
        ref var s = ref _world.GetComponentRW<ObjectiveStatus>(_map.TryGetEntity(60, out var p) ? p : default);
        s.Capture = 1; s.Challenger = ForceId.Hostile; s.AreaCenter = new WorldPoint(10f, 20f, 3f); s.AreaRadius = 25f;
        var rows = _svc.GetObjectives()["objectives"]!.AsArray().ToDictionary(r => (int)r!["networkId"]!);
        var row = rows[60]!;
        Assert.Equal((true, "Hostile", 25f), ((bool)row["capture"]!, (string)row["challenger"]!, (float)row["areaRadius"]!));
        Assert.Equal(new[] { 10f, 20f, 3f }, row["areaCenter"]!.AsArray().Select(n => n!.GetValue<float>()));
        Assert.Equal((false, "Neutral", 0f), ((bool)rows[61]!["capture"]!, (string)rows[61]!["challenger"]!, (float)rows[61]!["areaRadius"]!));
    }

    [Fact]
    public void AnObjectiveWhoseParentIsNotHere_IsARoot_AndANodeWithoutTheComponentAnswersEmpty()
    {
        Add(20, "Orphan", "ReachArea", ObjectiveState.Pending, parent: 999);
        Assert.Equal(new[] { 20 }, _svc.GetObjectives()["roots"]!.AsArray().Select(n => n!.GetValue<int>()));
        using var bare = new EntityRepository();
        var svc = new DebugApiService(new PerspectiveScopedDispatcher(
            new ISubsystemDebugProvider[] { new SubsystemDebugProvider("CGF", "Scenario", world: () => bare, entityMap: () => _map) },
            currentPerspective: () => "Scenario", acksPending: null));
        Assert.Empty(svc.GetObjectives()["objectives"]!.AsArray());
    }

    /// <summary>⭐ rules S2-c — a <c>Role = Trigger</c> row shows its <c>TriggerState</c> as a NAME plus <c>fireCount</c> / <c>lastFired</c>; an objective row does not carry them.
    /// ✅ Red-proof: the <c>Role == Trigger</c> branch removed from <c>GetObjectives</c> ⇒ red.</summary>
    [Fact]
    public void ATriggerRow_ShowsItsTriggerStateAsAString_AnObjectiveRowDoesNot()
    {
        Add(30, "Start the attack", "Trigger", ObjectiveState.Active);
        Add(31, "Hold", "HoldArea", ObjectiveState.Active);
        _world.GetComponentRW<ObjectiveStatus>(_map.TryGetEntity(30, out var t) ? t : default).Role = ObjectiveRole.Trigger;
        ref var s = ref _world.GetComponentRW<ObjectiveStatus>(t);
        s.TriggerState = TriggerState.Cooldown; s.FireCount = 2; s.LastFired = 12.5;

        var rows = _svc.GetObjectives()["objectives"]!.AsArray();
        var trig = rows.Single(r => (int)r!["networkId"]! == 30)!;
        Assert.Equal(("Trigger", "Cooldown", 2, 12.5), ((string)trig["role"]!, (string)trig["triggerState"]!, (int)trig["fireCount"]!, (double)trig["lastFired"]!));
        Assert.Null(rows.Single(r => (int)r!["networkId"]! == 31)!["triggerState"]);
    }

    /// <summary>⭐ rules V-1 / V-2 — every row carries <c>dependsOn</c> / <c>actsOn</c> (net-id arrays) and <c>timing</c> {holdFor, delay, cooldown, deadline, expectedAt}, read off the status (so any node answers).
    /// ✅ Red-proof: <c>actsOn</c> not written ⇒ red.</summary>
    [Fact]
    public void ARowShowsWhatTheRuleNames_AndItsTiming()
    {
        Add(30, "Fire when", "Trigger", ObjectiveState.Active);
        var e = _map.TryGetEntity(30, out var en) ? en : default;
        var ids = default(NetIds8); ids[0] = 7; ids[1] = 8;
        var acts = default(NetIds8); acts[0] = 9;
        ref var s = ref _world.GetComponentRW<ObjectiveStatus>(e);
        s.DependsOnCount = 2; s.DependsOn = ids; s.ActsOnCount = 1; s.ActsOn = acts;
        s.HoldFor = 1.5f; s.Delay = 2f; s.Cooldown = 3f; s.Deadline = 60f; s.ExpectedAt = (SimInstant)12.5;
        var row = _svc.GetObjectives()["objectives"]![0]!;
        Assert.Equal(new[] { 7, 8 }, row["dependsOn"]!.AsArray().Select(n => n!.GetValue<int>()));
        Assert.Equal(new[] { 9 }, row["actsOn"]!.AsArray().Select(n => n!.GetValue<int>()));
        var t = row["timing"]!;
        Assert.Equal((1.5f, 2f, 3f, 60f, 12.5), ((float)t["holdFor"]!, (float)t["delay"]!, (float)t["cooldown"]!, (float)t["deadline"]!, (double)t["expectedAt"]!));
    }

    /// <summary>⭐ rules V-4 — <c>POST /objectives/{id}/control</c> writes <c>RuleControl</c> through the attribute write path: on the owner (the component and its authority bit are here) it lands directly, with
    /// <c>Seq</c> = the last + 1 and the debug API as the origin; a bad request name is refused. ✅ Red-proof: the route not writing <c>Rule.Request</c> ⇒ red.</summary>
    [Fact]
    public void TheControlRoute_WritesRuleControlThroughTheRouter_OnTheOwner()
    {
        Add(40, "T", "Trigger", ObjectiveState.Active);
        var e = _map.TryGetEntity(40, out var en) ? en : default;
        _world.AddComponent(e, new RuleControl { Seq = 4, Done = 4 });
        _world.SetAuthority(e, ComponentTypeRegistry.GetOrRegisterManaged(typeof(RuleControl)), true);

        var (ok, err) = _svc.ControlObjective(40, "Disarm");
        Assert.Null(err);
        Assert.Equal(("direct", 5), ((string)ok!["route"]!, (int)ok["seq"]!));
        var rc = _world.GetComponentRO<RuleControl>(e);
        Assert.Equal(((byte)RuleRequest.Disarm, 5, (byte)RuleOrigin.DebugApi), (rc.Request, rc.Seq, rc.Origin));
        Assert.Equal(4, rc.Done);                                          // honoured by the owner's next tick, not here

        Assert.NotNull(_svc.ControlObjective(40, "Explode").error);
        Assert.NotNull(_svc.ControlObjective(999, "Fire").error);
    }

    /// <summary>⭐ rules V-4 — on a node that holds only a replica (no <c>RuleControl</c>) the same route answers <c>requested</c> — a change-request for the owner, nothing applied here.</summary>
    [Fact]
    public void TheControlRoute_OnAReplica_IsAChangeRequest()
    {
        Add(41, "T", "Trigger", ObjectiveState.Active);
        var (ok, err) = _svc.ControlObjective(41, "Fire");
        Assert.Null(err);
        Assert.Equal("requested", (string)ok!["route"]!);
        var e = _map.TryGetEntity(41, out var en) ? en : default;
        Assert.False(_world.HasComponent<RuleControl>(e));
    }
    /// <summary>⭐ rules V-3 — a TRIGGER row carries <c>leaves</c> (label, 0/1, measured) once the owner has sampled them, and none before; an objective row never does.
    /// ✅ Red-proof: the <c>leaves</c> block removed from <c>GetObjectives</c> ⇒ red.</summary>
    [Fact]
    public void ATriggerRow_CarriesItsLeaves_OnceSampled_AndNoneBefore()
    {
        Add(30, "Gate", "Trigger", ObjectiveState.Active);
        Add(31, "Obj", "ReachArea", ObjectiveState.Active);
        _map.TryGetEntity(30, out var trig); _map.TryGetEntity(31, out var obj);
        _world.GetComponentRW<ObjectiveStatus>(trig).Role = ObjectiveRole.Trigger;
        Assert.Null(_svc.GetObjectives()["objectives"]!.AsArray().Single(r => (int)r!["networkId"]! == 30)!["leaves"]);   // not sampled yet

        var c = new RuleLeaves { Count = 2 };
        c.Leaves[0] = new RuleLeaf { Label = new FixedString32("@Bridge Health.Current"), Value = 1, Measured = 37.5f };
        c.Leaves[1] = new RuleLeaf { Label = new FixedString32("ScenarioTime"), Value = 0, Measured = 12.25f };
        _world.AddComponent(trig, c);
        _world.AddComponent(obj, c);
        var rows = _svc.GetObjectives()["objectives"]!.AsArray();
        var leaves = rows.Single(r => (int)r!["networkId"]! == 30)!["leaves"]!.AsArray();
        Assert.Equal(2, leaves.Count);
        Assert.Equal(("@Bridge Health.Current", 1, 37.5f), ((string)leaves[0]!["label"]!, (int)leaves[0]!["value"]!.GetValue<byte>(), (float)leaves[0]!["measured"]!));
        Assert.Equal(("ScenarioTime", 0, 12.25f), ((string)leaves[1]!["label"]!, (int)leaves[1]!["value"]!.GetValue<byte>(), (float)leaves[1]!["measured"]!));
        Assert.Null(rows.Single(r => (int)r!["networkId"]! == 31)!["leaves"]);   // only a trigger has when-leaves
    }
}
