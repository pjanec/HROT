using System.Numerics;
using Hrot.Editor.AiShared.Comparison.Rendering;

namespace Hrot.Editor.AiShared.Tests.Comparison;

public sealed class ComparisonStyleMapTests
{
    // ---- ColorForSeverity ---------------------------------------------------

    [Theory]
    [InlineData("cosmetic",  0.5f, 0.5f, 0.5f, 0.6f)]
    [InlineData("tuning",    0.3f, 0.5f, 1.0f, 1.0f)]
    [InlineData("feature",   0.2f, 0.8f, 0.2f, 1.0f)]
    [InlineData("removal",   0.9f, 0.2f, 0.2f, 1.0f)]
    [InlineData("behavior",  1.0f, 0.55f, 0.1f, 1.0f)]
    public void ColorForSeverity_KnownSeverity_ReturnsExpectedColor(
        string severity, float r, float g, float b, float a)
    {
        var color = ComparisonStyleMap.ColorForSeverity(severity);

        Assert.Equal(r, color.X, 4);
        Assert.Equal(g, color.Y, 4);
        Assert.Equal(b, color.Z, 4);
        Assert.Equal(a, color.W, 4);
    }

    [Fact]
    public void ColorForSeverity_UnknownSeverity_ReturnsGrayFallback()
    {
        var color = ComparisonStyleMap.ColorForSeverity("UNKNOWN");

        // Neutral gray: (0.5, 0.5, 0.5, 0.6)
        Assert.Equal(0.5f, color.X, 4);
        Assert.Equal(0.5f, color.Y, 4);
        Assert.Equal(0.5f, color.Z, 4);
        Assert.Equal(0.6f, color.W, 4);
    }

    [Fact]
    public void ColorForSeverity_CaseInsensitive_BehaviorEqualsUpperCase()
    {
        var lower = ComparisonStyleMap.ColorForSeverity("behavior");
        var upper = ComparisonStyleMap.ColorForSeverity("BEHAVIOR");

        Assert.Equal(lower, upper);
    }

    [Fact]
    public void ColorForSeverity_IntentShift_SameColorAsBehavior()
    {
        var intentShift = ComparisonStyleMap.ColorForSeverity("intent_shift");
        var behavior    = ComparisonStyleMap.ColorForSeverity("behavior");

        Assert.Equal(intentShift, behavior);
    }

    // ---- GlyphForKind -------------------------------------------------------

    [Theory]
    [InlineData("node_added",         "+")]
    [InlineData("node_removed",       "-")]
    [InlineData("node_modified",      "~")]
    [InlineData("variable_added",     "+v")]
    [InlineData("variable_removed",   "-v")]
    [InlineData("variable_renamed",   ">>>")]
    [InlineData("variable_retyped",   "[]")]
    [InlineData("connection_changed", "~>")]
    [InlineData("comment_changed",    "\"")]
    [InlineData("intent_shift",       "!!")]
    public void GlyphForKind_KnownKind_ReturnsExpectedGlyph(string kind, string expectedGlyph)
    {
        var glyph = ComparisonStyleMap.GlyphForKind(kind);

        Assert.Equal(expectedGlyph, glyph);
    }

    [Fact]
    public void GlyphForKind_UnknownKind_ReturnsQuestionMark()
    {
        var glyph = ComparisonStyleMap.GlyphForKind("unknown_thing");

        Assert.Equal("?", glyph);
    }

    [Fact]
    public void GlyphForKind_CaseInsensitive_UpperCaseMatchesLower()
    {
        var lower = ComparisonStyleMap.GlyphForKind("node_added");
        var upper = ComparisonStyleMap.GlyphForKind("NODE_ADDED");

        Assert.Equal(lower, upper);
    }
}
