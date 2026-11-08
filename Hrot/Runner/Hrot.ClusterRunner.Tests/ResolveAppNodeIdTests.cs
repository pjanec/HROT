using Xunit;

namespace Hrot.ClusterRunner.Tests;

/// <summary>
/// ⭐ CE-4332 (📄 docs/DESIGN_Recording_Storage.md §9b) — the application node-id offset, one cell per arm, in any casing the subsystem name arrives in.
/// 🔴 Red-proof (compiles): the arm back to <c>"Player"</c> ⇒ a Player gets 600 ⇒ the Player cells red.
/// </summary>
public sealed class ResolveAppNodeIdTests
{
    [Theory]
    [InlineData("SimHost", 1)]
    [InlineData("Player", 100)]
    [InlineData("player", 100)]
    [InlineData("Orchestrator", 300)]
    [InlineData("CGF", 400)]
    [InlineData("CI", 500)]
    [InlineData("Traffic", 800)]
    [InlineData("Editor", 600)]   // everything else
    public void EachSubsystem_GetsItsOwnOffset(string subsystem, int offset)
        => Assert.Equal(10 + offset, Hrot.Runner.Program.ResolveAppNodeId(subsystem, 10));
}
