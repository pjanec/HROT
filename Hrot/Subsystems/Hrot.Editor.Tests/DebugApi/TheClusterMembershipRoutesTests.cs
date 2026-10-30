using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fdp.Toolkit.Orchestration;
using Hrot.Editor.DebugApi;
using Hrot.Presentation.DebugApi;
using Xunit;

namespace Hrot.Editor.Tests.DebugApi;

/// <summary>
/// ⭐⭐ <c>CE-4002</c> — the routes the cluster-membership use cases are tested through
/// (<c>docs/DESIGN_Cluster_Membership_Use_Cases.md</c> §3.1). ⛔ They add no behaviour: the roster is the master's, and a
/// transition / cancel is the operator panel's own intent. ⇒ these rails pin exactly that — what reaches the bus, and
/// that a host WITHOUT a master or a bus says so instead of pretending.
/// </summary>
public sealed class TheClusterMembershipRoutesTests
{
    private readonly RecordingMasterControl _master = new();
    private List<TransitionStateIntent> _transitions => _master.Transitions;
    private List<CancelOperationIntent> _cancels => _master.Cancels;

    // ⭐ Two providers, the shape of a real multi-subsystem host: the ACTIVE one has no master (an IG), the other
    //   hosts it (the orchestrator) — so the any-node fallback is what is under test, not a lucky active perspective.
    private DebugApiService Cluster(IReadOnlyList<ClusterRosterEntry>? roster, bool withBus = true)
        => new(new PerspectiveScopedDispatcher(
                   new ISubsystemDebugProvider[]
                   {
                       new SubsystemDebugProvider("IG", "IG"),
                       new SubsystemDebugProvider("Orchestrator", "Orchestrator",
                           master:            withBus ? () => _master : null,
                           clusterRoster:     roster is null ? null : () => roster),
                   },
                   currentPerspective: () => "IG",
                   acksPending: null));

    [Fact]
    public void The_roster_is_read_from_the_node_that_hosts_the_master_even_when_another_perspective_is_active()
    {
        var svc = Cluster(new[]
        {
            new ClusterRosterEntry(100, "IG", "Idle", "Map2D", 0.4),
            new ClusterRosterEntry(400, "CGF", "OperatingLive", "Brain", 0.2),
        });

        var (result, error, _) = svc.GetClusterRoster();

        Assert.Null(error);
        Assert.Equal(2, (int)result!["count"]!);
        var first = result["nodes"]![0]!;
        Assert.Equal(100, (int)first["nodeId"]!);
        Assert.Equal("Idle", (string)first["reportedState"]!);
        Assert.Equal("Map2D", (string)first["roles"]!);
    }

    /// <summary>
    /// ⭐ <c>CE-3354</c> ② — <c>GET /cluster/roster</c> shows each node's <c>name</c>, and lists a heartbeat the master REFUSED for a name another live node holds under
    /// <c>nameConflicts</c> (not among <c>nodes</c>, not in <c>count</c>). 🔴 Mutations (each compiles): the route not writing <c>name</c> ⇒ the name cell reds; the conflict row left in <c>nodes</c> ⇒ the count cell reds.
    /// </summary>
    [Fact]
    public void CE3354_The_roster_shows_the_node_names_and_the_refused_name_conflicts()
    {
        var svc = Cluster(new[]
        {
            new ClusterRosterEntry(100, "IG", "Idle", "Map2D", 0.4, "film-cam"),
            new ClusterRosterEntry(101, "IG", "Refused", "None", 0.1, "film-cam", "name 'film-cam' is held by live node 100"),
        });

        var (result, error, _) = svc.GetClusterRoster();

        Assert.Null(error);
        Assert.Equal(1, (int)result!["count"]!);
        Assert.Equal("film-cam", (string)result["nodes"]![0]!["name"]!);
        var conflict = result["nameConflicts"]![0]!;
        Assert.Equal((101, "film-cam"), ((int)conflict["nodeId"]!, (string)conflict["name"]!));
        Assert.Contains("100", (string)conflict["reason"]!);
    }

    /// <summary>
    /// ⭐ <c>CE-3354</c> ② — <c>GET /status.node</c> is the ANSWERING node's <c>{name, nodeId}</c>, read from its cluster view; absent (null) where the host keeps none.
    /// 🔴 Mutation: <c>NodeOrNull</c> reading the wrong field ⇒ the name cell reds.
    /// </summary>
    [Fact]
    public void CE3354_Status_names_the_node_that_answers()
    {
        using var slave = new Fdp.Toolkit.Orchestration.ClusterSlave(7, "IG", new Fdp.Core.FdpEventBus(), nodeName: "film-cam");
        var svc = new DebugApiService(new PerspectiveScopedDispatcher(
            new ISubsystemDebugProvider[] { new SubsystemDebugProvider("IG", "IG", clusterView: () => slave.View) },
            currentPerspective: () => "IG",
            acksPending: null));

        var node = svc.GetStatus()["node"]!;

        Assert.Equal(("film-cam", 7), ((string)node["name"]!, (int)node["nodeId"]!));
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4087</c> — a scenario load that waits for a Brain / MuscleGround node (or was refused) is READABLE over HTTP: the
    /// roster answer carries the master's load-gate text as <c>load.notice</c>, from the node that hosts the master even though
    /// another perspective is active. <c>null</c> when no load is held — an absent notice is not an empty string.
    /// </summary>
    [Fact]
    public void The_roster_carries_the_load_gate_notice_from_the_node_that_hosts_the_master()
    {
        string? notice = "waiting for: MuscleGround (SimHost)";
        var svc = new DebugApiService(new PerspectiveScopedDispatcher(
            new ISubsystemDebugProvider[]
            {
                new SubsystemDebugProvider("IG", "IG"),
                new SubsystemDebugProvider("Orchestrator", "Orchestrator",
                    master:            () => _master,
                    clusterRoster:     () => Array.Empty<ClusterRosterEntry>(),
                    clusterLoadNotice: () => notice),
            },
            currentPerspective: () => "IG",
            acksPending: null));

        var (waiting, _, _) = svc.GetClusterRoster();
        Assert.Equal("waiting for: MuscleGround (SimHost)", (string)waiting!["load"]!["notice"]!);

        notice = null;   // the SimHost arrived, the load started
        var (running, _, _) = svc.GetClusterRoster();
        Assert.Null(running!["load"]!["notice"]);
    }

    [Fact]
    public void A_host_with_no_master_says_so_rather_than_answering_an_empty_roster()
    {
        var (result, error, _) = Cluster(roster: null).GetClusterRoster();

        Assert.Null(result);
        Assert.Contains("ClusterMaster", error);   // ⛔ an empty list would read as "the cluster has no nodes"
    }

    [Fact]
    public void A_transition_publishes_one_intent_carrying_every_field_the_caller_named()
    {
        var exercise = Guid.NewGuid();
        var (result, error, _) = Cluster(roster: null).RequestClusterTransition(new JsonObject
        {
            ["target"] = "OperatingReplay", ["exerciseId"] = exercise.ToString(),
            ["scenario"] = "hill-attack", ["timeMode"] = "Deterministic", ["targetWallTicks"] = 42,
        });

        Assert.Null(error);
        var intent = Assert.Single(_transitions);
        Assert.Equal(ClusterState.OperatingReplay, intent.TargetState);
        Assert.Equal(exercise, intent.ExerciseId);
        Assert.Equal("hill-attack", intent.ScenarioId);
        Assert.Equal("Deterministic", intent.TimeMode);
        Assert.Equal(42, intent.TargetWallTicks);
        // ⭐ The returned id IS the intent's — that is what POST /cluster/cancel takes.
        Assert.Equal(intent.TransactionId.ToString(), (string)result!["transactionId"]!);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"target\":\"NotAState\"}")]
    [InlineData("{\"target\":\"OperatingLive\",\"exerciseId\":\"not-a-guid\"}")]
    public void A_malformed_transition_is_refused_and_publishes_nothing(string json)
    {
        var (result, error, _) = Cluster(roster: null).RequestClusterTransition(JsonNode.Parse(json));

        Assert.Null(result);
        Assert.NotNull(error);
        Assert.Empty(_transitions);
    }

    [Fact]
    public void A_cancel_publishes_the_operators_intent_for_the_named_request()
    {
        var target = Guid.NewGuid();
        var (_, error, _) = Cluster(roster: null).RequestClusterCancel(new JsonObject { ["targetId"] = target.ToString() });

        Assert.Null(error);
        Assert.Equal(target, Assert.Single(_cancels).TargetRequestId);
    }

    [Fact]
    public void A_host_with_no_bus_refuses_a_transition_and_a_cancel_as_unsupported()
    {
        var svc = Cluster(roster: null, withBus: false);

        var (_, transitionError, _) = svc.RequestClusterTransition(new JsonObject { ["target"] = "Idle" });
        var (_, cancelError, _)     = svc.RequestClusterCancel(new JsonObject { ["targetId"] = Guid.NewGuid().ToString() });

        Assert.StartsWith("No subsystem", transitionError);   // ⭐ the host maps this prefix to 503, not 400
        Assert.StartsWith("No subsystem", cancelError);
    }

    [Fact]
    public void The_roster_and_cancel_cells_are_measured_from_what_the_provider_was_given()
    {
        var bare = new SubsystemDebugProvider("IG", "IG").DescribeCapabilities();
        var master = new SubsystemDebugProvider("Orchestrator", "Orchestrator",
            clusterRoster: () => Array.Empty<ClusterRosterEntry>(),
            master:        () => _master).DescribeCapabilities();

        Assert.False(bare[DebugCapabilities.ClusterRoster]);
        Assert.False(bare[DebugCapabilities.ClusterCancel]);
        Assert.True(master[DebugCapabilities.ClusterRoster]);
        Assert.True(master[DebugCapabilities.ClusterCancel]);
    }
}
