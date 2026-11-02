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
        _svc = new DebugApiService(new PerspectiveScopedDispatcher(
            new ISubsystemDebugProvider[] { new SubsystemDebugProvider("CGF", "Scenario", world: () => _world, entityMap: () => _map) },
            currentPerspective: () => "Scenario", acksPending: null));
    }

    public void Dispose() => _world.Dispose();

    private void Add(int id, string title, string kind, ObjectiveState state, int parent = 0, float score = 0f)
    {
        var e = _world.CreateEntity();
        _map.Register(id, e);
        _world.AddComponent(e, new NetworkIdentity(id));
        _world.AddComponent(e, new ObjectiveStatus { Title = new FixedString64(title), Kind = new FixedString32(kind), State = state, Score = score, ProgressCur = 1, ProgressNeed = 4, Reason = new FixedString64("1/4") });
        _world.AddComponent(e, new ObjectiveLinks { Parent = new EntityRef(parent), Subject = new EntityRef(900) });
    }

    /// <summary>A root WeightedMean with two children: three rows, the root alone in <c>roots</c>, its <c>children</c> the two ids, the state as a name.
    /// ✅ Red-proof: a child listed as a root (the parent test dropped) ⇒ red.</summary>
    [Fact]
    public void TheRoute_AnswersTheThreeRows_TheRootAndItsChildren_WithStateNames()
    {
        Add(10, "Squad mission", "WeightedMean", ObjectiveState.Active);
        Add(12, "Hold", "HoldArea", ObjectiveState.Failed, parent: 10);
        Add(11, "Reach", "ReachArea", ObjectiveState.Achieved, parent: 10, score: 1f);

        var body = _svc.GetObjectives();
        var rows = body["objectives"]!.AsArray();
        Assert.Equal(3, rows.Count);
        Assert.Equal(new[] { 10 }, body["roots"]!.AsArray().Select(n => n!.GetValue<int>()));
        Assert.Equal(new[] { 10, 12, 11 }.Select(i => i).OrderBy(i => i == 10 ? 0 : i == 12 ? 1 : 2), rows.Select(r => r!["networkId"]!.GetValue<int>()));   // root first, then the children by title (Hold, Reach)
        Assert.Equal(new[] { 12, 11 }, rows[0]!["children"]!.AsArray().Select(n => n!.GetValue<int>()));
        Assert.Equal("Active", (string)rows[0]!["state"]!);
        Assert.Equal(("Achieved", "ReachArea", 1f, 10, 900), ((string)rows[2]!["state"]!, (string)rows[2]!["kind"]!, (float)rows[2]!["score"]!, (int)rows[2]!["parent"]!, (int)rows[2]!["subject"]!));
        Assert.Equal((1, 4, "1/4"), ((int)rows[2]!["progress"]!["cur"]!, (int)rows[2]!["progress"]!["need"]!, (string)rows[2]!["reason"]!));
        Assert.Null(rows[0]!["parent"]);

        var nested = _svc.GetObjectives(tree: true);
        Assert.Equal(2, nested["objectives"]![0]!["children"]!.AsArray().Count);
        Assert.Equal("Hold", (string)nested["objectives"]![0]!["children"]![0]!["title"]!);
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
}
