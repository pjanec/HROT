using System.IO;
using Xunit;

namespace Hrot.ClusterRunner.Tests;

public class ExConSubsystemClusterTests
{
    [Fact]
    public void ExConSubsystem_HasNoDirectClusterMasterReference()
    {
        // Static analysis guard: IosSubsystem must not import Hrot.Orchestrator namespace.
        var source = File.ReadAllText(
            Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "..", "..", "..", "..",
                "Hrot.ClusterRunner", "Services", "ExConSubsystem.cs"));

        Assert.DoesNotContain("Hrot.Orchestrator", source);
        Assert.DoesNotContain("ClusterMaster",         source);
    }
}
