using System.Linq;
using Fdp.Core;
using Xunit;

namespace Hrot.Orchestrator.Tests;

/// <summary>
/// P2 + P3 (cross-node construction barrier prerequisites): the orchestrator roster stores each node's
/// declared <see cref="NodeRole"/> mask and resolves membership by role via
/// <see cref="NodeRoster.NodesWithRole"/>. Because <see cref="NodeRole"/> is <c>[Flags]</c>, a multi-role
/// node must match a query for EITHER of its roles — the property that a lossy single-role mapping breaks.
/// </summary>
public sealed class NodeRosterTests
{
    private static NodeHealthProfile Node(int id, NodeRole roles) =>
        new() { NodeId = id, Roles = roles, LastHeartbeatUtcSeconds = 1.0 };

    [Fact]
    public void NodesWithRole_ResolvesSingleAndMultiRoleMasks()
    {
        var roster = new NodeRoster();
        roster.Upsert(Node(400, NodeRole.Brain));                                 // CGF
        roster.Upsert(Node(1,   NodeRole.MuscleGround | NodeRole.Perception));    // SimHost, multi-role
        roster.Upsert(Node(100, NodeRole.Map2D));                                 // IG
        roster.Upsert(Node(300, NodeRole.None));                                  // Orchestrator — no ECS role

        Assert.Equal(new[] { 400 }, roster.NodesWithRole(NodeRole.Brain).OrderBy(x => x).ToArray());
        Assert.Equal(new[] { 100 }, roster.NodesWithRole(NodeRole.Map2D).OrderBy(x => x).ToArray());

        // ⭐ The multi-role node (1) matches a query for EITHER of its flags — the [Flags] property.
        Assert.Contains(1, roster.NodesWithRole(NodeRole.MuscleGround));
        Assert.Contains(1, roster.NodesWithRole(NodeRole.Perception));

        // The None node matches nothing; a role no node holds returns empty.
        Assert.DoesNotContain(300, roster.NodesWithRole(NodeRole.Brain));
        Assert.Empty(roster.NodesWithRole(NodeRole.NavigationSolver));
    }

    private static NodeHealthProfile Named(int id, string name, double at = 1.0) =>
        new() { NodeId = id, NodeName = name, SubsystemName = "IG", LastHeartbeatUtcSeconds = at };

    /// <summary>
    /// ⭐ <c>CE-3354</c> ② — a node name is unique among LIVE nodes: a second node id taking a held name is refused (not added, reported once), the holder re-heartbeating is
    /// fine, and once the holder's entry is pruned (heartbeat timeout) the name is free. 🔴 Mutations (each compiles): the name compare removed ⇒ the second node is admitted;
    /// the node-id exemption removed ⇒ the holder's own heartbeat is refused; the conflict kept after the holder left ⇒ the last cell reds.
    /// </summary>
    [Fact]
    public void CE3354_ANameHeldByALiveNode_RefusesAnotherNode_TheHolderIsFine_AndAnExpiredHolderFreesIt()
    {
        var roster = new NodeRoster();
        Assert.True(roster.TryAdmit(Named(100, "film-cam", 1.0), out _, out _));
        Assert.True(roster.TryAdmit(Named(100, "film-cam", 2.0), out _, out _));            // the same node again: normal

        Assert.False(roster.TryAdmit(Named(101, "Film-Cam", 2.0), out var c, out bool first));   // another node, the name case-insensitively
        Assert.True(first);
        Assert.Equal((100, 101), (c!.HolderNodeId, c.RefusedNodeId));
        Assert.False(roster.ActiveNodes.ContainsKey(101));
        Assert.False(roster.TryAdmit(Named(101, "film-cam", 3.0), out _, out first));
        Assert.False(first);                                                                 // reported once per (name, node)
        Assert.Single(roster.NameConflicts);

        roster.PruneStale(nowUtcSeconds: 20.0, maxSilenceSeconds: 5.0);                      // the holder timed out
        Assert.Empty(roster.NameConflicts);
        Assert.True(roster.TryAdmit(Named(101, "film-cam", 20.0), out _, out _));            // the name is free
        Assert.True(roster.ActiveNodes.ContainsKey(101));
    }

    [Fact]
    public void NodesWithRole_MatchesAnyIntersectingFlag_NotAnExactMask()
    {
        var roster = new NodeRoster();
        roster.Upsert(Node(1, NodeRole.MuscleGround | NodeRole.Perception));

        // Querying with a combined mask returns the node when ANY flag intersects — a `mask == role`
        // implementation would miss it (the red-proof for the & vs == choice).
        Assert.Contains(1, roster.NodesWithRole(NodeRole.Perception | NodeRole.NavigationSolver));
    }
}
