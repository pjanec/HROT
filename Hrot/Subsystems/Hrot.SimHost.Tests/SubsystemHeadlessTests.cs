using Fdp.Toolkit.Runner;
using Hrot.SimHost;
using Xunit;

namespace Hrot.SimHost.Tests;

/// <summary>
/// Verifies that <see cref="SimHostSubsystem"/> can be initialised in headless mode
/// without loading any native graphics DLL (Raylib).
/// </summary>
public class SubsystemHeadlessTests
{
    [Fact(Skip = "SimHostSubsystem.Initialize blocks on DdsIdAllocator waiting for live Orchestrator; verify as integration test")]
    public void SimHostSubsystem_InitializeHeadless_DoesNotThrow()
    {
        var subsystem = new SimHostSubsystem();
        var config    = new SubsystemConfig { Headless = true, DomainId = 220 };
        Exception? ex = null;
        try
        {
            ex = Record.Exception(() => subsystem.Initialize(config));
        }
        finally
        {
            subsystem.Shutdown();
        }
        Assert.Null(ex); // in particular: no DllNotFoundException for native graphics
    }
}
