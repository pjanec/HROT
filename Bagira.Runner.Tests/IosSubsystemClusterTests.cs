using System.IO;
using Xunit;

namespace Bagira.Runner.Tests;

public class IosSubsystemClusterTests
{
    [Fact]
    public void IosSubsystem_HasNoDirectDrillMasterReference()
    {
        // Static analysis guard: IosSubsystem must not import Bagira.Orchestrator namespace.
        var source = File.ReadAllText(
            Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "..", "..", "..", "..",
                "Bagira.Runner", "Services", "IosSubsystem.cs"));

        Assert.DoesNotContain("Bagira.Orchestrator", source);
        Assert.DoesNotContain("DrillMaster",         source);
    }
}
