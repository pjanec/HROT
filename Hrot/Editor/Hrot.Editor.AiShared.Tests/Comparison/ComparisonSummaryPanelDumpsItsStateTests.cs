using System;
using Fdp.Diagnostics.Contracts.Panels;
using Hrot.Editor.AiShared.Comparison;
using Hrot.Editor.AiShared.Comparison.UI;
using Hrot.Editor.AiShared.Tests.Windows;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Comparison;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 — <c>ComparisonSummaryPanel</c> converted to the <c>PanelSnapshot</c> contract.</b>
/// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example.
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class ComparisonSummaryPanelDumpsItsStateTests : IDisposable
{
    public ComparisonSummaryPanelDumpsItsStateTests()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    public void Dispose()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    private static ComparisonResponse MakeResponse(string topSummary = "Top summary.", string? humanSummary = null)
        => new(humanSummary, topSummary, Array.Empty<ComparisonChange>(), Array.Empty<string>());

    [Fact]
    public void ConstructingThePanel_DeclaresItInstrumented_BeforeItHasEverDrawn()
    {
        Assert.DoesNotContain("ai_comparison_summary", PanelSnapshot.RegisteredPanels);   // ⛔ anti-vacuity

        var panel = new ComparisonSummaryPanel(new ComparisonSessionRegistry());

        Assert.Contains("ai_comparison_summary", PanelSnapshot.RegisteredPanels);
        Assert.DoesNotContain("ai_comparison_summary", PanelSnapshot.CapturedPanels);
        Assert.Null(PanelSnapshot.TryGet("ai_comparison_summary"));
        Assert.NotNull(panel);
    }

    [Fact]
    public void AfterABuild_TheDumpCarriesTheSummary()
    {
        PanelSnapshot.CaptureEnabled = true;
        var assetId  = Guid.NewGuid();
        var registry = new ComparisonSessionRegistry();
        registry.SetSession(new ComparisonSessionState(assetId, MakeResponse(topSummary: "Expected summary.")));

        var panel = new ComparisonSummaryPanel(registry);
        panel.SetActiveAsset(assetId, "MyAsset");

        panel.SimulateDrawClientArea();

        var vm = PanelSnapshot.TryGet("ai_comparison_summary");
        Assert.NotNull(vm);
        Assert.Equal("ai_comparison_summary",  vm!.PanelId);
        Assert.Equal(ComparisonSummaryPanel.Kind, vm.PanelKind);

        var dump = vm.Dump();
        Assert.True(dump["hasSession"]!.GetValue<bool>());
        Assert.Equal("MyAsset",            dump["assetName"]!.GetValue<string>());
        Assert.Equal("Expected summary.",  dump["topSummary"]!.GetValue<string>());
        // ⭐ "cosmetic" is disabled by default (ComparisonSessionState's own default set).
        Assert.DoesNotContain(dump["enabledSeverities"]!.AsArray(), n => n!.GetValue<string>() == "cosmetic");
        Assert.Contains(dump["enabledSeverities"]!.AsArray(), n => n!.GetValue<string>() == "behavior");
    }

    [Fact]
    public void WithNoActiveSession_TheDumpSaysSo()
    {
        PanelSnapshot.CaptureEnabled = true;
        var panel = new ComparisonSummaryPanel(new ComparisonSessionRegistry());

        panel.SimulateDrawClientArea();

        var dump = PanelSnapshot.TryGet("ai_comparison_summary")!.Dump();
        Assert.False(dump["hasSession"]!.GetValue<bool>());
    }

    [Fact]
    public void WithCaptureOff_TheProductionPathPublishesNothing()
    {
        var assetId  = Guid.NewGuid();
        var registry = new ComparisonSessionRegistry();
        registry.SetSession(new ComparisonSessionState(assetId, MakeResponse()));
        var panel = new ComparisonSummaryPanel(registry);
        panel.SetActiveAsset(assetId, "MyAsset");   // CaptureEnabled stays false

        var vm = panel.SimulateDrawClientArea();

        Assert.Empty(PanelSnapshot.CapturedPanels);
        Assert.Contains("ai_comparison_summary", PanelSnapshot.RegisteredPanels);
        Assert.NotNull(vm);
        Assert.True(vm.HasSession);
    }
}
