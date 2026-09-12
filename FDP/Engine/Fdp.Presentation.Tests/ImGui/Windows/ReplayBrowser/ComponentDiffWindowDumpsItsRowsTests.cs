using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Presentation.Panels.ReplayBrowser;
using Fdp.Presentation.Tests.ImGui.Panels;
using Fdp.Presentation.Windows.ReplayBrowser;
using Fdp.Toolkit.ReplayBrowser.Diff;
using Xunit;

namespace Fdp.Presentation.Tests.ImGui.Windows.ReplayBrowser;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 — <c>ComponentDiffPanel</c>/<c>ComponentDiffWindow</c> converted to the
/// <c>PanelSnapshot</c> contract.</b> 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example ·
/// <c>docs/blueprints/batches/QUEUE_Panel_Observability_Sweep.md</c> group 4 (the ReplayBrowser set).
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class ComponentDiffWindowDumpsItsRowsTests : IDisposable
{
    public ComponentDiffWindowDumpsItsRowsTests()
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
        Assert.DoesNotContain("component_diff_test", PanelSnapshot.RegisteredPanels);   // ⛔ anti-vacuity

        var window = MakeWindow(new ComponentDiffPanel());

        Assert.Contains("component_diff_test", PanelSnapshot.RegisteredPanels);
        Assert.DoesNotContain("component_diff_test", PanelSnapshot.CapturedPanels);
        Assert.Null(PanelSnapshot.TryGet("component_diff_test"));
        Assert.NotNull(window);
    }

    // ── Rail 2 — the dump carries a real field ───────────────────────────────────────────────────

    [Fact]
    public void AfterABuild_TheDumpCarriesTheModifiedLeaf()
    {
        PanelSnapshot.CaptureEnabled = true;
        var group = new DiffObject("Transform");
        var leaf = new DiffValue("PosX", "1.0", "2.0", JsonValueKind.Number, isModified: true);
        group.Children.Add(leaf);
        group.EvaluateModificationState();

        var panel = new ComponentDiffPanel { CurrentDiffs = new List<DiffNode> { group } };
        var window = MakeWindow(panel);

        window.SimulateDrawClientArea();

        var vm = PanelSnapshot.TryGet("component_diff_test");
        Assert.NotNull(vm);
        Assert.Equal("component_diff_test", vm!.PanelId);
        Assert.Equal(ComponentDiffWindow.Kind, vm.PanelKind);

        var dump = vm.Dump();
        var rows = dump["rows"]!.AsArray();
        // Group row + the modified leaf; an unmodified sibling would be excluded by HideUnchanged.
        Assert.Contains(rows, r => r!["name"]!.GetValue<string>() == "PosX" && r["oldValue"]!.GetValue<string>() == "1.0");
    }

    /// <summary>⭐⭐ An unmodified leaf is filtered out of the dump when <c>HideUnchanged</c> is on — the
    /// SAME <c>CollectVisibleNodes</c> filter the draw uses.</summary>
    [Fact]
    public void AnUnmodifiedLeaf_IsFilteredOutOfTheDumpWhenHideUnchangedIsOn()
    {
        PanelSnapshot.CaptureEnabled = true;
        var group = new DiffObject("Transform");
        group.Children.Add(new DiffValue("PosX", "1.0", "2.0", JsonValueKind.Number, isModified: true));
        group.Children.Add(new DiffValue("PosY", "3.0", "3.0", JsonValueKind.Number, isModified: false));
        group.EvaluateModificationState();

        var panel = new ComponentDiffPanel { CurrentDiffs = new List<DiffNode> { group } };
        var window = MakeWindow(panel);

        window.SimulateDrawClientArea();

        var dump = PanelSnapshot.TryGet("component_diff_test")!.Dump();
        var rows = dump["rows"]!.AsArray();
        Assert.DoesNotContain(rows, r => r!["name"]!.GetValue<string>() == "PosY");
    }

    // ── Rail 3 — the flag gates the DUMP, not the BUILD ──────────────────────────────────────────

    [Fact]
    public void WithCaptureOff_TheProductionPathPublishesNothing()
    {
        var window = MakeWindow(new ComponentDiffPanel());   // CaptureEnabled stays false

        var vm = window.SimulateDrawClientArea();

        Assert.Empty(PanelSnapshot.CapturedPanels);
        Assert.Contains("component_diff_test", PanelSnapshot.RegisteredPanels);
        Assert.NotNull(vm);   // ⭐ the BUILD is unaffected by the flag
    }

    private static ComponentDiffWindow MakeWindow(ComponentDiffPanel panel) =>
        new ComponentDiffWindow("component_diff_test", "Component Diff", "test-perspective", panel, new Vector4(1, 1, 1, 1));
}
