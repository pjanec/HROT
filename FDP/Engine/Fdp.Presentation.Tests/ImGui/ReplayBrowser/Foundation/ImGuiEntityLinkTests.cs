using System;
using Fdp.Core;
using Fdp.Presentation.Utils.ReplayBrowser;
using Xunit;

namespace Fdp.Presentation.ReplayBrowser.Foundation;

/// <summary>
/// FND-T13: Valid entity handle patterns parse correctly.
/// FND-T14: Invalid inputs return false.
/// </summary>
public sealed class ImGuiEntityLinkTests
{
    // ── FND-T13: Valid parses ─────────────────────────────────────────────

    [Fact]
    public void TryParse_StandardFormat_ReturnsEntity()
    {
        bool ok = ImGuiEntityLink.TryParse("[42, v3]", out Entity entity);
        Assert.True(ok);
        Assert.Equal(42, entity.Index);
        Assert.Equal((ushort)3, entity.Generation);
    }

    [Fact]
    public void TryParse_NoVPrefix_ReturnsEntity()
    {
        bool ok = ImGuiEntityLink.TryParse("[42, 3]", out Entity entity);
        Assert.True(ok);
        Assert.Equal(42, entity.Index);
        Assert.Equal((ushort)3, entity.Generation);
    }

    [Fact]
    public void TryParse_UppercaseV_ReturnsEntity()
    {
        bool ok = ImGuiEntityLink.TryParse("[42, V3]", out Entity entity);
        Assert.True(ok);
        Assert.Equal(42, entity.Index);
        Assert.Equal((ushort)3, entity.Generation);
    }

    [Fact]
    public void TryParse_WhitespaceTolerant_ReturnsEntity()
    {
        bool ok = ImGuiEntityLink.TryParse("[ 42 , v3 ]", out Entity entity);
        Assert.True(ok);
        Assert.Equal(42, entity.Index);
        Assert.Equal((ushort)3, entity.Generation);
    }

    [Fact]
    public void TryParse_ZeroIndex_ReturnsEntity()
    {
        bool ok = ImGuiEntityLink.TryParse("[0, v0]", out Entity entity);
        Assert.True(ok);
        Assert.Equal(0, entity.Index);
        Assert.Equal((ushort)0, entity.Generation);
    }

    // ── FND-T14: Invalid inputs ───────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("foo")]
    [InlineData("[,v3]")]
    [InlineData("[42]")]
    [InlineData("-1, v3")]
    [InlineData("[-1, v3]")]
    [InlineData("[42 v3]")]  // missing comma
    public void TryParse_InvalidInput_ReturnsFalse(string input)
    {
        bool ok = ImGuiEntityLink.TryParse(input, out _);
        Assert.False(ok);
    }
}
