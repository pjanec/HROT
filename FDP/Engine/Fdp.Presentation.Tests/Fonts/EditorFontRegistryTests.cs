using System;
using System.Collections.Generic;
using Fdp.Presentation.Fonts;
using Xunit;

namespace Fdp.Presentation.Tests.Fonts;

/// <summary>
/// Verifies the canvas-font selection policy published via <see cref="EditorFontRegistry"/>:
/// "smallest baked size that is still &gt;= target, else the largest" (avoids upscaling blur),
/// and the empty-registry fallback to <see cref="IntPtr.Zero"/>. Pure logic — no ImGui context.
/// </summary>
[Collection("EditorFontRegistry")] // serialize: the registry is process-global mutable state
public sealed class EditorFontRegistryTests : IDisposable
{
    public EditorFontRegistryTests() => EditorFontRegistry.Reset();
    public void Dispose() => EditorFontRegistry.Reset();

    private static Dictionary<float, nint> Ladder() => new()
    {
        { 16f, (nint)1 },
        { 24f, (nint)2 },
        { 32f, (nint)3 },
    };

    [Fact]
    public void ResolveCanvasFont_EmptyRegistry_ReturnsZero()
    {
        Assert.Equal(IntPtr.Zero, EditorFontRegistry.ResolveCanvasFont(24f));
        Assert.False(EditorFontRegistry.IsPopulated);
    }

    [Fact]
    public void ResolveCanvasFont_ExactMatch_ReturnsThatFont()
    {
        EditorFontRegistry.Publish((nint)99, Ladder(), 1f);
        Assert.Equal((nint)2, EditorFontRegistry.ResolveCanvasFont(24f));
    }

    [Fact]
    public void ResolveCanvasFont_NoExactMatch_ReturnsNextLarger()
    {
        EditorFontRegistry.Publish((nint)99, Ladder(), 1f);
        // 20px → smallest baked >= 20 is 24px
        Assert.Equal((nint)2, EditorFontRegistry.ResolveCanvasFont(20f));
    }

    [Fact]
    public void ResolveCanvasFont_TargetExceedsAll_ReturnsLargest()
    {
        EditorFontRegistry.Publish((nint)99, Ladder(), 1f);
        Assert.Equal((nint)3, EditorFontRegistry.ResolveCanvasFont(48f));
    }

    [Fact]
    public void ResolveCanvasFont_TargetBelowAll_ReturnsSmallest()
    {
        EditorFontRegistry.Publish((nint)99, Ladder(), 1f);
        Assert.Equal((nint)1, EditorFontRegistry.ResolveCanvasFont(4f));
    }

    [Fact]
    public void Publish_ExposesDefaultFontAndScale()
    {
        EditorFontRegistry.Publish((nint)77, Ladder(), 1.5f);
        Assert.Equal((nint)77, EditorFontRegistry.DefaultFont);
        Assert.Equal(1.5f, EditorFontRegistry.CurrentScale, 4);
        Assert.True(EditorFontRegistry.IsPopulated);
    }

    [Fact]
    public void Reset_ClearsBackToEmptyFallback()
    {
        EditorFontRegistry.Publish((nint)77, Ladder(), 1.5f);
        EditorFontRegistry.Reset();

        Assert.Equal(IntPtr.Zero, EditorFontRegistry.ResolveCanvasFont(24f));
        Assert.Equal(IntPtr.Zero, EditorFontRegistry.DefaultFont);
        Assert.False(EditorFontRegistry.IsPopulated);
    }
}
