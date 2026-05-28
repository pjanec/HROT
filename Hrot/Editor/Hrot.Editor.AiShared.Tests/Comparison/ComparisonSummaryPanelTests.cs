using System;
using System.Linq;
using Hrot.Editor.AiShared.Comparison;
using Hrot.Editor.AiShared.Comparison.UI;

namespace Hrot.Editor.AiShared.Tests.Comparison;

public sealed class ComparisonSummaryPanelTests
{
    private static ComparisonResponse MakeResponse(
        string topSummary = "Top summary.",
        string? humanSummary = null) =>
        new ComparisonResponse(humanSummary, topSummary, Array.Empty<ComparisonChange>(), Array.Empty<string>());

    // ---- AssetName shows what was passed in ---------------------------------

    [Fact]
    public void AssetName_ReturnsExpectedName()
    {
        var session = new ComparisonSessionState(Guid.NewGuid(), MakeResponse());
        var state   = new ComparisonSummaryPanelState(session, "MyAsset");

        Assert.Equal("MyAsset", state.AssetName);
    }

    // ---- Migration notice ---------------------------------------------------

    [Fact]
    public void HasMigrationNotice_NullNotice_ReturnsFalse()
    {
        var session = new ComparisonSessionState(Guid.NewGuid(), MakeResponse(), migrationNotice: null);
        var state   = new ComparisonSummaryPanelState(session, "A");

        Assert.False(state.HasMigrationNotice);
    }

    [Fact]
    public void HasMigrationNotice_NonNullNotice_ReturnsTrueAndCorrectText()
    {
        var session = new ComparisonSessionState(Guid.NewGuid(), MakeResponse(), migrationNotice: "v1 -> v2");
        var state   = new ComparisonSummaryPanelState(session, "A");

        Assert.True(state.HasMigrationNotice);
        Assert.Equal("v1 -> v2", state.MigrationNotice);
    }

    // ---- TopSummary ---------------------------------------------------------

    [Fact]
    public void TopSummary_ReturnsResponseTopLevelSummary()
    {
        var session = new ComparisonSessionState(Guid.NewGuid(), MakeResponse(topSummary: "Expected summary."));
        var state   = new ComparisonSummaryPanelState(session, "A");

        Assert.Equal("Expected summary.", state.TopSummary);
    }

    // ---- ToggleSeverity delegates to session --------------------------------

    [Fact]
    public void ToggleSeverity_CosmeticOff_TogglesOnInSession()
    {
        var session = new ComparisonSessionState(Guid.NewGuid(), MakeResponse());
        var state   = new ComparisonSummaryPanelState(session, "A");

        // cosmetic is disabled by default
        Assert.DoesNotContain("cosmetic", state.EnabledSeverities);

        state.ToggleSeverity("cosmetic");

        Assert.Contains("cosmetic", state.EnabledSeverities);
    }
}
