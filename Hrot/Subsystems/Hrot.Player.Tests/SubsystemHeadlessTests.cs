using Fdp.Toolkit.Runner;
using Hrot.Player;
using Xunit;

namespace Hrot.Player.Tests;

/// <summary>
/// Verifies that <see cref="PlayerSubsystem"/> can be initialised in headless mode
/// without loading any native graphics DLL (Raylib).
/// </summary>
public class SubsystemHeadlessTests
{
    [Fact]
    public void PlayerSubsystem_InitializeHeadless_DoesNotThrow()
    {
        var subsystem = new PlayerSubsystem();
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
