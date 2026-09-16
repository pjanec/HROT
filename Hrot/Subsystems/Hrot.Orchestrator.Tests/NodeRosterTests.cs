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
