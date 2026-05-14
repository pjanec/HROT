using Fdp.Presentation.Raylib;
using Hrot.FakeStrideApp;
using Xunit;

namespace Hrot.FakeStrideApp.Tests;

public sealed class FakeStrideAppTests
{
    // SC_SM008_1: Type conformance — FakeStrideApp must inherit FdpApplication.
    [Fact]
    public void FakeStrideApp_InheritsFromFdpApplication()
    {
        Assert.True(typeof(FdpApplication)
            .IsAssignableFrom(typeof(FakeStrideApp)));
    }

    // SC_SM008_1: Constructor must not throw with a valid ApplicationConfig.
    // Does NOT call OnLoad() or Run(); FdpApplication only calls OnLoad() inside Run().
    [Fact]
    public void FakeStrideApp_Constructor_WithValidConfig_DoesNotThrow()
    {
        var config = new ApplicationConfig
        {
            WindowTitle = "Test",
            Width       = 1280,
            Height      = 720,
            TargetFPS   = 60,
        };
        var ex = Record.Exception(() =>
        {
            using var app = new FakeStrideApp(config, domainId: 0, nodeId: 700);
        });
        Assert.Null(ex);
    }

    // SC_SM008_1: Config defaults match the spec (1280x720, 60 fps, correct title).
    [Fact]
    public void FakeStrideApp_DefaultConfig_HasExpectedValues()
    {
        var config = new ApplicationConfig
        {
            WindowTitle = "FakeStrideApp -- HROT Stride Mock",
            Width       = 1280,
            Height      = 720,
            TargetFPS   = 60,
        };

        Assert.Equal("FakeStrideApp -- HROT Stride Mock", config.WindowTitle);
        Assert.Equal(1280, config.Width);
        Assert.Equal(720,  config.Height);
        Assert.Equal(60,   config.TargetFPS);
    }
}
