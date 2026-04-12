using System.IO;
using Xunit;

namespace Hrot.ClusterRunner.Tests;

public class ExConSubsystemClusterTests
{
    [Fact]
    public void ExConSubsystem_HasNoDirectClusterMasterReference()
    {
        // Static analysis guard: ExConSubsystem must not instantiate ClusterMaster directly.
        // It is allowed to reference Hrot.Orchestrator.Panels and Hrot.Orchestrator.Windows
        // for shared panel types, but must not hold a ClusterMaster field/reference.
        var source = File.ReadAllText(
            Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "..", "..", "..", "..",
                "Hrot.ExCon", "ExConSubsystem.cs"));

        Assert.DoesNotContain("ClusterMaster", source);
    }
}
