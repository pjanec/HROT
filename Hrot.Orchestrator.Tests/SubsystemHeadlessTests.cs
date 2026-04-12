using Fdp.Engine.Runner;
using Hrot.Orchestrator;
using Xunit;

namespace Hrot.Orchestrator.Tests;

/// <summary>
/// Verifies that <see cref="OrchestratorSubsystem"/> can be initialised in headless mode
/// without loading any native graphics DLL (Raylib).
/// </summary>
public class SubsystemHeadlessTests
{
    [Fact]
    public void OrchestratorSubsystem_InitializeHeadless_DoesNotThrow()
    {
        var subsystem = new OrchestratorSubsystem();
        var config    = new SubsystemConfig { Headless = true, DomainId = 224 };
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
