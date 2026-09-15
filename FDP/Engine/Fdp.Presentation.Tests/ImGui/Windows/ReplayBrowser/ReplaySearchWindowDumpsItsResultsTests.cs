using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using Fdp.Core;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Presentation.Editing;
using Fdp.Presentation.Panels.ReplayBrowser;
using Fdp.Presentation.Tests.ImGui.Panels;
using Fdp.Presentation.Windows.ReplayBrowser;
using Fdp.Toolkit.ReplayBrowser.Search;
using StructEdit.Core;
using Xunit;

namespace Fdp.Presentation.Tests.ImGui.Windows.ReplayBrowser;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 — <c>ReplaySearchPanel</c>/<c>ReplaySearchWindow</c> converted to the
/// <c>PanelSnapshot</c> contract.</b> 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example ·
/// <c>docs/blueprints/batches/QUEUE_Panel_Observability_Sweep.md</c> group 4 (the ReplayBrowser set).
/// Mirrors <c>ReplaySearchPanelDecouplingTests</c>'s Nop service stubs.
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class ReplaySearchWindowDumpsItsResultsTests : IDisposable
{
    public ReplaySearchWindowDumpsItsResultsTests()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    public void Dispose()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    private sealed class NopEditService : IComponentEditService
    {
        public IEditSession Open(object component, Type componentType, EditScope? scope = null, EditContext? context = null)
            => throw new NotSupportedException("BuildViewModel does not open the StructEdit session.");
    }

    private sealed class NopSearchService : IRecordingSearchService
    {
        public IReadOnlyList<SearchResultDto> ExecuteSearch(string fdpPath, SearchPredicateDto root, TargetEntityFilter? entityFilter = null, CancellationToken ct = default)
            => Array.Empty<SearchResultDto>();
        public IReadOnlyList<LifecycleSearchResultDto> ExecuteLifecycleSearch(string fdpPath, LifecyclePredicateDto criteria, TargetEntityFilter? entityFilter = null, CancellationToken ct = default)
            => Array.Empty<LifecycleSearchResultDto>();
    }

    private static ReplaySearchPanel MakePanel() => new ReplaySearchPanel(
        new NopEditService(), new NopSearchService(),
        _ => { }, _ => { }, (_, _) => { });

    // ── Rail 1 — instrumented at construction, on the PRODUCTION object ─────────────────────────

    [Fact]
    public void ConstructingTheWindow_DeclaresItInstrumented_BeforeItHasEverDrawn()
    {
        Assert.DoesNotContain("replay_search_test", PanelSnapshot.RegisteredPanels);   // ⛔ anti-vacuity

        var window = new ReplaySearchWindow("replay_search_test", "Search", "test-perspective", MakePanel(), new Vector4(1, 1, 1, 1));

        Assert.Contains("replay_search_test", PanelSnapshot.RegisteredPanels);
        Assert.DoesNotContain("replay_search_test", PanelSnapshot.CapturedPanels);
        Assert.Null(PanelSnapshot.TryGet("replay_search_test"));
        Assert.NotNull(window);
    }

    // ── Rail 2 — the dump carries a real field ───────────────────────────────────────────────────

    [Fact]
    public void AfterABuild_TheDumpCarriesTheStatusLineAndMode()
    {
        PanelSnapshot.CaptureEnabled = true;
        var panel = MakePanel();
        panel.IsMergedViewActive = true;   // BuildViewModel reads this directly; never touches ImGui
        var window = new ReplaySearchWindow("replay_search_test", "Search", "test-perspective", panel, new Vector4(1, 1, 1, 1));

        window.SimulateDrawClientArea();

        var vm = PanelSnapshot.TryGet("replay_search_test");
        Assert.NotNull(vm);
        Assert.Equal("replay_search_test", vm!.PanelId);
        Assert.Equal(ReplaySearchWindow.Kind, vm.PanelKind);

        var dump = vm.Dump();
        Assert.True(dump["isMergedViewActive"]!.GetValue<bool>());
        Assert.Equal("Component", dump["mode"]!.GetValue<string>());
        Assert.Empty(dump["results"]!.AsArray());
    }

    // ── Rail 3 — the flag gates the DUMP, not the BUILD ──────────────────────────────────────────

    [Fact]
    public void WithCaptureOff_TheProductionPathPublishesNothing()
    {
        var window = new ReplaySearchWindow("replay_search_test", "Search", "test-perspective", MakePanel(), new Vector4(1, 1, 1, 1));

        var vm = window.SimulateDrawClientArea();

        Assert.Empty(PanelSnapshot.CapturedPanels);
        Assert.Contains("replay_search_test", PanelSnapshot.RegisteredPanels);
        Assert.NotNull(vm);   // ⭐ the BUILD is unaffected by the flag
    }
}
