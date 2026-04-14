using Fdp.Toolkit.Runner;
using Hrot.ExCon;
using Xunit;

namespace Hrot.ExCon.Tests;

/// <summary>
/// Verifies that <see cref="ExConSubsystem"/> can be initialised in headless mode
/// without loading any native graphics DLL (Raylib).
/// </summary>
public class SubsystemHeadlessTests
{
    [Fact]
    public void ExConSubsystem_InitializeHeadless_DoesNotThrow()
    {
        var subsystem = new ExConSubsystem();
        var config    = new SubsystemConfig { Headless = true, DomainId = 222 };
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
