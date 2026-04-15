using System.IO;
using Xunit;

namespace Hrot.ClusterRunner.Tests;

public class ExConSubsystemClusterTests
{
    // Walks up from the test binary directory until IOS-IG-SimHost.sln is found.
    private static string FindWorkspaceRoot()
    {
        var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "IOS-IG-SimHost.sln")))
            dir = dir.Parent;
        if (dir == null)
            throw new InvalidOperationException(
                "Could not locate workspace root (IOS-IG-SimHost.sln not found).");
        return dir.FullName;
    }

    [Fact]
    public void ExConSubsystem_HasNoDirectClusterMasterReference()
    {
        // Static analysis guard: ExConSubsystem must not instantiate ClusterMaster directly.
        // It is allowed to reference Hrot.Orchestrator.Panels and Hrot.Orchestrator.Windows
        // for shared panel types, but must not hold a ClusterMaster field/reference.
        var source = File.ReadAllText(
            Path.Combine(
                FindWorkspaceRoot(),
                "Hrot", "Subsystems", "Hrot.ExCon", "ExConSubsystem.cs"));

        Assert.DoesNotContain("ClusterMaster", source);
    }
}
