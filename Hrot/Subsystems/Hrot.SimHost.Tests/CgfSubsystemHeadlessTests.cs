using Fdp.Toolkit.Runner;
using Hrot.CGF;
using Xunit;

namespace Hrot.SimHost.Tests;

/// <summary>
/// Verifies that <see cref="CgfSubsystem"/> can be initialised in headless mode
/// without loading any native graphics DLL (Raylib).
/// </summary>
public class CgfSubsystemHeadlessTests
{
    [Fact(Skip = "CgfSubsystem.Initialize blocks on DdsIdAllocator waiting for live Orchestrator; verify as integration test")]
    public void CgfSubsystem_InitializeHeadless_DoesNotThrow()
    {
        var subsystem = new CgfSubsystem();
        var config    = new SubsystemConfig { Headless = true, DomainId = 223 };
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
