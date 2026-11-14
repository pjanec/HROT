using Fdp.Core;
using Hrot.Orchestrator;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// ⭐ CE-3347 (R-3069) — an in-process test cluster IS a deployment: it requires only the roles of the nodes it boots. ONE rule for every
/// test composition root (the harness, the script executors), so none can drift from it.
/// </summary>
/// <remarks>⛔ The default configuration requires Brain | MuscleGround. On a cluster with no CGF the latch never released, so every cluster
/// op was refused: the harness's <c>ResumeTime</c> (a stopped clock) and the script tests' transitions (the master stayed in Idle).</remarks>
internal static class TestDeployment
{
    /// <summary>The working directory's configuration, requiring only the roles the booted nodes provide.</summary>
    public static ClusterConfiguration For(bool simHost, bool cgf)
    {
        var roles = NodeRole.None;
        if (simHost) roles |= NodeRole.MuscleGround;
        if (cgf)     roles |= NodeRole.Brain;
        var defaults = ClusterConfiguration.LoadFromWorkingDirectory();
        return defaults with { MandatoryRoles = defaults.MandatoryRoles & roles };
    }
}
