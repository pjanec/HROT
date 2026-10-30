using Fdp.Core;

namespace Hrot.Orchestrator.Tests;

/// <summary>
/// ⭐ <c>CE-3344</c> (R-3069) — the cluster-ready latch is over names AND roles, so a master built from the DEFAULT configuration (roles <c>Brain | MuscleGround</c>)
/// holds every non-load request until a Brain and a MuscleGround have reported. A suite that is not about readiness builds its master from this: nothing mandatory at all,
/// the latch open from construction (the shape every such suite had before the roles joined the latch).
/// </summary>
internal static class TestClusterConfig
{
    public static ClusterConfiguration NothingMandatory() => new() { MandatoryRoles = NodeRole.None };
}
