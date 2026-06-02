using System;
using System.Numerics;
using Hrot.Editor.AiShared.Adapters;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Adapters;

/// <summary>
/// AIE-004 — EngineEditorTheme tests.
/// </summary>
public sealed class AIE004_EngineEditorThemeTests
{
    private static IEditorTheme MakeTheme() => new EngineEditorTheme();

    // ── AIE-004-01: Full surface — colors non-NaN, sizes > 0 ─────────────────

    [Fact]
    public void EngineEditorTheme_Implements_IEditorTheme_FullSurface_ColorsNonNaN()
    {
        var t = MakeTheme();

        AssertColorSane(t.BackgroundColor,        nameof(t.BackgroundColor));
        AssertColorSane(t.GridMinorColor,          nameof(t.GridMinorColor));
        AssertColorSane(t.GridMajorColor,          nameof(t.GridMajorColor));
        AssertColorSane(t.SelectionAccent,         nameof(t.SelectionAccent));
        AssertColorSane(t.PrimarySelectionAccent,  nameof(t.PrimarySelectionAccent));
        AssertColorSane(t.ErrorColor,              nameof(t.ErrorColor));
        AssertColorSane(t.WarningColor,            nameof(t.WarningColor));
        AssertColorSane(t.TextDefault,             nameof(t.TextDefault));
        AssertColorSane(t.TextMuted,               nameof(t.TextMuted));
    }

    [Fact]
    public void EngineEditorTheme_Implements_IEditorTheme_FullSurface_SizesPositive()
    {
        var t = MakeTheme();

        Assert.True(t.NodeCornerRadius    > 0f, "NodeCornerRadius must be > 0");
        Assert.True(t.NodeBorderThickness > 0f, "NodeBorderThickness must be > 0");
        Assert.True(t.NodeHeaderHeight    > 0f, "NodeHeaderHeight must be > 0");
        Assert.True(t.PinGlyphSize        > 0f, "PinGlyphSize must be > 0");
        Assert.True(t.WireThicknessExec   > 0f, "WireThicknessExec must be > 0");
        Assert.True(t.WireThicknessData   > 0f, "WireThicknessData must be > 0");
    }

    [Fact]
    public void EngineEditorTheme_AttachmentDefaults_ArePositive()
    {
        var t = MakeTheme();

        Assert.True(t.AttachmentHeight       > 0f, "AttachmentHeight must be > 0");
        Assert.True(t.AttachmentCornerRadius > 0f, "AttachmentCornerRadius must be > 0");
        Assert.True(t.AttachmentGapAboveHost > 0f, "AttachmentGapAboveHost must be > 0");
        Assert.True(t.AttachmentInterGap     > 0f, "AttachmentInterGap must be > 0");
    }

    // ── AIE-004-02: GetFontForSize — returns zero or non-negative ptr ─────────

    [Fact]
    public void EngineEditorTheme_GetFontForSize_ReturnsZeroOrValidPtr()
    {
        // Without a live ImGui context this must return IntPtr.Zero, not throw.
        var t = MakeTheme();

        nint result = t.GetFontForSize(14f);

        // Zero is the documented fallback when no context is active.
        Assert.True(result >= 0,
            "GetFontForSize must return zero (no context) or a positive handle (valid font).");
    }

    /// <summary>
    /// Corrective Task 0 — guard test: with no ImGui context the method must return
    /// <see cref="IntPtr.Zero"/> deterministically and must NOT crash.
    /// (Previously the managed try/catch was insufficient because
    /// AccessViolationException is a corrupted-state exception.)
    /// </summary>
    [Fact]
    public void EngineEditorTheme_GetFontForSize_NoContext_ReturnsZero()
    {
        // Precondition: no ImGui context is active in this test process.
        var t = MakeTheme();

        nint result = t.GetFontForSize(14f);

        Assert.Equal(IntPtr.Zero, result);
    }

    [Fact]
    public void EngineEditorTheme_GetFontForSize_NeverThrows_ForAnySize()
    {
        var t = MakeTheme();
        float[] sizes = { 0f, 8f, 12f, 14f, 18f, 24f, 48f, float.MaxValue };

        foreach (var s in sizes)
        {
            // Exception here = test failure.
            _ = t.GetFontForSize(s);
        }
    }

    // ── AIE-004-03: GetCategoryHeaderColor — distinct per category ───────────

    [Fact]
    public void EngineEditorTheme_GetCategoryHeaderColor_DistinctPerCategory()
    {
        var t = MakeTheme();
        var categories = (NodeCategory[])Enum.GetValues(typeof(NodeCategory));

        var colors = categories
            .Where(c => c != NodeCategory.Custom) // Custom/default may share a color
            .Select(c => (category: c, color: t.GetCategoryHeaderColor(c)))
            .ToList();

        // Each known category should have a non-NaN color.
        foreach (var (cat, color) in colors)
            AssertColorSane(color, cat.ToString());

        // The named categories (Function, Event, Pure, etc.) should have distinct colors
        // from each other (they are mapped explicitly in DefaultTheme).
        var distinctNamedColors = new[]
        {
            NodeCategory.Function,
            NodeCategory.Event,
            NodeCategory.Pure,
            NodeCategory.VariableGet,
            NodeCategory.FlowControl,
            NodeCategory.Macro,
        }.Select(c => t.GetCategoryHeaderColor(c)).ToList();

        // At least 3 of the 6 named categories must have distinct colors.
        int distinctCount = distinctNamedColors.Distinct().Count();
        Assert.True(distinctCount >= 3,
            $"Expected at least 3 distinct header colors for named categories, got {distinctCount}.");
    }

    // ── AIE-004-04: Demo literal values (BCP-BATCH-01 Task C) ────────────────

    /// <summary>
    /// Verifies the exact selection-accent color matches the FakeEditorTheme specimen:
    /// (0.21, 0.52, 0.89, 1).  This is the primary regression guard for the
    /// yellow-marquee bug fix (wrong color was forwarded from DefaultTheme).
    /// </summary>
    [Fact]
    public void EngineEditorTheme_SelectionAccent_IsBlue_MatchesDemoValue()
    {
        var t = MakeTheme();
        // FakeEditorTheme: new(0.21f, 0.52f, 0.89f, 1f)
        Assert.Equal(0.21f, t.SelectionAccent.X, 4);
        Assert.Equal(0.52f, t.SelectionAccent.Y, 4);
        Assert.Equal(0.89f, t.SelectionAccent.Z, 4);
        Assert.Equal(1.00f, t.SelectionAccent.W, 4);
    }

    [Fact]
    public void EngineEditorTheme_PrimarySelectionAccent_MatchesDemoValue()
    {
        var t = MakeTheme();
        // FakeEditorTheme: new(0.26f, 0.65f, 0.99f, 1f)
        Assert.Equal(0.26f, t.PrimarySelectionAccent.X, 4);
        Assert.Equal(0.65f, t.PrimarySelectionAccent.Y, 4);
        Assert.Equal(0.99f, t.PrimarySelectionAccent.Z, 4);
        Assert.Equal(1.00f, t.PrimarySelectionAccent.W, 4);
    }

    [Fact]
    public void EngineEditorTheme_NodeCornerRadius_Is4()
    {
        Assert.Equal(4f, MakeTheme().NodeCornerRadius, 4);
    }

    [Fact]
    public void EngineEditorTheme_WireThicknessExec_Is3()
    {
        Assert.Equal(3f, MakeTheme().WireThicknessExec, 4);
    }

    [Fact]
    public void EngineEditorTheme_WireThicknessData_Is2()
    {
        Assert.Equal(2f, MakeTheme().WireThicknessData, 4);
    }

    [Fact]
    public void EngineEditorTheme_GetCategoryHeaderColor_Event_IsRed()
    {
        var color = MakeTheme().GetCategoryHeaderColor(NodeCategory.Event);
        // FakeEditorTheme: new Vector4(0.65f, 0.07f, 0.07f, 1f)
        Assert.Equal(0.65f, color.X, 4);
        Assert.Equal(0.07f, color.Y, 4);
        Assert.Equal(0.07f, color.Z, 4);
        Assert.Equal(1.00f, color.W, 4);
    }

    [Fact]
    public void EngineEditorTheme_GetCategoryHeaderColor_Function_IsBlue()
    {
        var color = MakeTheme().GetCategoryHeaderColor(NodeCategory.Function);
        // FakeEditorTheme: new Vector4(0.07f, 0.30f, 0.60f, 1f)
        Assert.Equal(0.07f, color.X, 4);
        Assert.Equal(0.30f, color.Y, 4);
        Assert.Equal(0.60f, color.Z, 4);
    }

    [Fact]
    public void EngineEditorTheme_GetCategoryHeaderColor_VariableGet_IsGreen()
    {
        var color = MakeTheme().GetCategoryHeaderColor(NodeCategory.VariableGet);
        // FakeEditorTheme: new Vector4(0.07f, 0.40f, 0.20f, 1f)
        Assert.Equal(0.07f, color.X, 4);
        Assert.Equal(0.40f, color.Y, 4);
        Assert.Equal(0.20f, color.Z, 4);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void AssertColorSane(Vector4 color, string name)
    {
        Assert.False(float.IsNaN(color.X), $"{name}.X is NaN");
        Assert.False(float.IsNaN(color.Y), $"{name}.Y is NaN");
        Assert.False(float.IsNaN(color.Z), $"{name}.Z is NaN");
        Assert.False(float.IsNaN(color.W), $"{name}.W is NaN");
    }
}
