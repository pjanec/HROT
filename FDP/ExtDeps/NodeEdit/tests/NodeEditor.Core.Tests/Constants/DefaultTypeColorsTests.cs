using NodeEditor.Core;
using NodeEditor.Primitives;
using FluentAssertions;
using Xunit;

namespace NodeEditor.Core.Tests.Constants;

public class DefaultTypeColorsTests
{
    [Fact]
    public void GetColor_KnownTypes_ReturnsExpectedAndFallback()
    {
        // System.Single should return the green color (0xA6, 0xE2, 0x2E, 0xFF)
        var singleColor = DefaultTypeColors.GetColor(new TypeKey("System.Single"));
        singleColor.X.Should().BeApproximately(0xA6 / 255f, 0.01f);
        singleColor.Y.Should().BeApproximately(0xE2 / 255f, 0.01f);

        // ExecColor should be white
        var execColor = DefaultTypeColors.ExecColor;
        execColor.X.Should().Be(1f);
        execColor.Y.Should().Be(1f);
        execColor.Z.Should().Be(1f);
        execColor.W.Should().Be(1f);

        // Unknown key should return a fallback (not throw)
        var unknown = DefaultTypeColors.GetColor(new TypeKey("SomeUnknown.Type"));
        unknown.W.Should().Be(1f); // fully opaque fallback
    }
}
