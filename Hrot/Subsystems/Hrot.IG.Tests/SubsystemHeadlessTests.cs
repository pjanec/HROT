using Fdp.Toolkit.Runner;
using Hrot.IG;
using Xunit;

namespace Hrot.IG.Tests;

/// <summary>
/// Verifies that <see cref="IgSubsystem"/> can be initialised in headless mode
/// without loading any native graphics DLL (Raylib).
/// </summary>
public class SubsystemHeadlessTests
{
    [Fact]
    public void IgSubsystem_InitializeHeadless_DoesNotThrow()
    {
        var subsystem = new IgSubsystem();
        var config    = new SubsystemConfig { Headless = true, DomainId = 221 };
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
