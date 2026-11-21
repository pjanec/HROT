using System;
using System.Linq;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Replication.Services;
using Hrot.Editor.DebugApi;
using Hrot.Gameplay;
using Hrot.Presentation.DebugApi;
using Xunit;

namespace Hrot.Editor.Tests.DebugApi;

/// <summary>
/// ⭐ Player Join S1 — <c>GET /deploy</c> and <c>POST /deploy</c>, the live run's handle on the Player's <see cref="DeployFlowSystem"/>: the routes exist (and are documented), a node WITH a flow reports its
/// state / offers / slot and takes a request, a node with none answers 404 "no deploy flow on this node". The flow runs here over a null participant (no DDS): the routes only look it up.
/// </summary>
public sealed class TheDeployRoutesTests
{
    private static DebugApiService Host(EntityRepository? world)
        => new(new PerspectiveScopedDispatcher(
                   new ISubsystemDebugProvider[] { new SubsystemDebugProvider("Player", "Player", world: world is null ? null : () => world) },
                   currentPerspective: () => "Player", acksPending: null));

    private static DeployFlowSystem Flow(EntityRepository world, bool live = true)
    {
        var flow = new DeployFlowSystem(new GameplayWire(null), world, new NetworkEntityMap(), station: 30,
            new DeployFlowEnvironment(() => true, () => live, () => true, unit => (true, "")));
        flow.Execute(world, 0.016f);
        return flow;
    }

    /// <summary>✅ Red-proof: either route removed from <c>DebugApiHost</c> ⇒ red.</summary>
    [Fact]
    public void BothRoutes_AreServed()
    {
        var routes = DebugApiHost.EnumerateRouteTemplates().Select(r => (r.Method, r.Template)).ToHashSet();
        Assert.Contains(("GET", "/deploy"), routes);
        Assert.Contains(("POST", "/deploy"), routes);
    }

    /// <summary>A node with a flow reports its state and the slot; with the run not Live it says why. ✅ Red-proof: <c>Get</c> not carrying the reason ⇒ red.</summary>
    [Fact]
    public void Get_ReportsTheFlow()
    {
        var world = new EntityRepository();
        Flow(world, live: false);
        var (result, error, _, status) = Host(world).Deploy(null, post: false);
        Assert.Null(error); Assert.Equal(200, status);
        var doc = (JsonObject)result!;
        Assert.Equal("Waiting", (string?)doc["state"]);
        Assert.Equal("Waiting for the server to load a scenario", (string?)doc["reason"]);
        Assert.Equal(30, (int?)doc["station"]);
        Assert.NotNull(doc["offers"]); Assert.NotNull(doc["slot"]);
    }

    /// <summary>POST: only while Choosing — a body is validated (400), a request while Waiting is 409, a good one moves the flow to Requesting. ✅ Red-proof: the state check dropped ⇒ the 409 case sends ⇒ red.</summary>
    [Fact]
    public void Post_TakesARequestOnlyWhileChoosing()
    {
        var waiting = new EntityRepository(); Flow(waiting, live: false);
        Assert.Equal(409, Host(waiting).Deploy(new JsonObject { ["side"] = "Friend", ["unitNetId"] = 7 }, post: true).Status);

        var world = new EntityRepository();
        var flow = Flow(world);
        Assert.Equal(DeployFlowState.Choosing, flow.State);
        var svc = Host(world);
        Assert.Equal(400, svc.Deploy(new JsonObject { ["side"] = "Purple", ["unitNetId"] = 7 }, post: true).Status);
        Assert.Equal(400, svc.Deploy(new JsonObject { ["side"] = "Friend" }, post: true).Status);
        var ok = svc.Deploy(new JsonObject { ["side"] = "Friend", ["unitNetId"] = 7 }, post: true);
        Assert.Equal(200, ok.Status);
        Assert.Equal(DeployFlowState.Requesting, flow.State);
        Assert.Equal("Requesting", (string?)((JsonObject)ok.Result!)["state"]);
    }

    /// <summary>A node with no flow answers 404 "no deploy flow on this node" on both verbs. ✅ Red-proof: the lookup falling back to some other world ⇒ red.</summary>
    [Fact]
    public void ANodeWithNoFlow_Answers404()
    {
        foreach (var post in new[] { false, true })
        {
            var (_, error, _, status) = Host(new EntityRepository()).Deploy(new JsonObject { ["side"] = "Friend", ["observe"] = true }, post);
            Assert.Equal(404, status); Assert.Equal("no deploy flow on this node", error);
        }
        Assert.Equal(404, Host(null).Deploy(null, post: false).Status);

    }

    /// <summary>S3 — <c>POST /deploy</c> accepts an optional <c>kit</c> (0…3) and <c>areaNetId</c> and hands them to the flow; a wrong type or range is a 400 and sends nothing. ✅ Red-proof: the fields not read ⇒ the flow sees 0 ⇒ red;
    /// the range / type checks dropped ⇒ the 400 cases red.</summary>
    [Fact]
    public void Post_CarriesTheKitAndTheArea_AndRefusesBadTypes()
    {
        var world = new EntityRepository();
        var flow = Flow(world);
        var svc = Host(world);
        var ok = svc.Deploy(new JsonObject { ["side"] = "Friend", ["unitNetId"] = 300, ["kit"] = 2, ["areaNetId"] = 401 }, post: true);
        Assert.Equal(200, ok.Status);
        Assert.Equal((byte)2, flow.RequestedKit); Assert.Equal(401, flow.RequestedAreaNetId);

        var world2 = new EntityRepository(); var flow2 = Flow(world2); var svc2 = Host(world2);
        foreach (var bad in new JsonObject[]
        {
            new() { ["side"] = "Friend", ["unitNetId"] = 300, ["kit"] = "x" },
            new() { ["side"] = "Friend", ["unitNetId"] = 300, ["kit"] = 4 },
            new() { ["side"] = "Friend", ["unitNetId"] = 300, ["kit"] = -1 },
            new() { ["side"] = "Friend", ["unitNetId"] = 300, ["areaNetId"] = "church" },
        })
            Assert.Equal(400, svc2.Deploy(bad, post: true).Status);
        Assert.Equal(DeployFlowState.Choosing, flow2.State);   // nothing was sent
    }
}
