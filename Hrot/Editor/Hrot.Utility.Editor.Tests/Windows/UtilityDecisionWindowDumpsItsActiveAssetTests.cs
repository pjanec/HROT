using System;
using Fdp.Diagnostics.Contracts.Panels;
using Hrot.Editor.AiShared.Selection;
using Hrot.Utility.Editor.Model;
using Hrot.Utility.Editor.Windows;
using Xunit;

namespace Hrot.Utility.Editor.Tests.Windows;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 — <c>UtilityDecisionWindow</c> converted to the <c>PanelSnapshot</c> contract.</b>
/// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example ·
/// <c>docs/blueprints/batches/QUEUE_Panel_Observability_Sweep.md</c> group 6.
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class UtilityDecisionWindowDumpsItsActiveAssetTests : IDisposable
{
    public UtilityDecisionWindowDumpsItsActiveAssetTests()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    public void Dispose()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    // ── Rail 1 — instrumented at construction, on the PRODUCTION object ─────────────────────────

    [Fact]
    public void ConstructingTheWindow_DeclaresItInstrumented_BeforeItHasEverDrawn()
    {
        Assert.DoesNotContain("utility_decision_editor", PanelSnapshot.RegisteredPanels);   // ⛔ anti-vacuity

        var window = new UtilityDecisionWindow(new EditorSelectionStore());

        Assert.Contains("utility_decision_editor", PanelSnapshot.RegisteredPanels);
        Assert.DoesNotContain("utility_decision_editor", PanelSnapshot.CapturedPanels);
        Assert.Null(PanelSnapshot.TryGet("utility_decision_editor"));
        Assert.NotNull(window);
    }

    // ── Rail 2 — the dump carries a real field ───────────────────────────────────────────────────

    [Fact]
    public void AfterOpeningAnAsset_TheDumpCarriesItsDisplayName()
    {
        PanelSnapshot.CaptureEnabled = true;
        var window = new UtilityDecisionWindow(new EditorSelectionStore());
        window.OpenAsset(new UtilityDecisionAsset { DisplayName = "Combat Priorities" });

        window.SimulateDrawClientArea();

        var vm = PanelSnapshot.TryGet("utility_decision_editor");
        Assert.NotNull(vm);
        Assert.Equal(UtilityDecisionWindow.Kind, vm!.PanelKind);
        var dump = vm.Dump();
        Assert.True(dump["hasActiveAsset"]!.GetValue<bool>());
        Assert.Equal("Combat Priorities", dump["activeAssetName"]!.GetValue<string>());
    }

    // ── Rail 3 — the flag gates the DUMP, not the BUILD ──────────────────────────────────────────

    [Fact]
    public void WithCaptureOff_TheProductionPathPublishesNothing()
    {
        var window = new UtilityDecisionWindow(new EditorSelectionStore());   // CaptureEnabled stays false

        var vm = window.SimulateDrawClientArea();

        Assert.Empty(PanelSnapshot.CapturedPanels);
        Assert.Contains("utility_decision_editor", PanelSnapshot.RegisteredPanels);
        Assert.NotNull(vm);   // ⭐ the BUILD is unaffected by the flag
        Assert.False(vm.HasActiveAsset);
    }
}
