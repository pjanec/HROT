using FluentAssertions;
using Raylib_cs;
using Xunit;

namespace FDP.Framework.Raylib.Tests;

public class ApplicationConfigTests
{
    [Fact]
    public void ApplicationConfig_DefaultConstructor_HasDefaults()
    {
        // Arrange & Act
        var config = new ApplicationConfig();

        // Assert
        config.Width.Should().Be(1280);
        config.Height.Should().Be(720);
        config.TargetFPS.Should().Be(60);
        config.WindowTitle.Should().Be("FDP Application");
        config.Flags.Should().HaveFlag(ConfigFlags.ResizableWindow);
        config.Flags.Should().HaveFlag(ConfigFlags.Msaa4xHint);
        config.PersistenceEnabled.Should().BeTrue();
    }
}
