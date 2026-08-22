using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core.Diagnostics;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Presentation.Panels;
using Fdp.Presentation.Windows.ReplayBrowser;
using Xunit;

namespace Fdp.Presentation.Tests.ImGui.Panels;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 — <c>EventBrowserPanel</c>/<c>FdpEventBrowserWindow</c> converted to the
/// <c>PanelSnapshot</c> contract.</b> 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example ·
/// <c>docs/blueprints/batches/QUEUE_Panel_Observability_Sweep.md</c> group 4 ("the HOST registers" gotcha
/// — <c>EventBrowserPanel</c> is the plain panel, <c>FdpEventBrowserWindow</c> the host).
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class EventBrowserPanelDumpsItsEventsTests : IDisposable
{
    public EventBrowserPanelDumpsItsEventsTests()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    public void Dispose()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    private sealed class FakeHistoryService : IDiagnosticEventHistoryService
    {
        private readonly CapturedEventDto[] _events;
        public FakeHistoryService(params CapturedEventDto[] events) => _events = events;
        public void Capture(string providerName, Fdp.Core.FdpEventBus eventBus, uint currentFrame) { }
        public CapturedEventDto[] GetHistory(IReadOnlyList<string>? providerFilter = null) => _events;
        public void ClearHistory() { }
        public void RewindHistory(uint frame) { }
    }

    // ── Rail 1 — instrumented at construction, on the PRODUCTION object ─────────────────────────

    [Fact]
    public void ConstructingTheWindow_DeclaresItInstrumented_BeforeItHasEverDrawn()
    {
        Assert.DoesNotContain("fdp_event_browser_test", PanelSnapshot.RegisteredPanels);   // ⛔ anti-vacuity

        var window = MakeWindow(new EventBrowserPanel());

        Assert.Contains("fdp_event_browser_test", PanelSnapshot.RegisteredPanels);
        Assert.DoesNotContain("fdp_event_browser_test", PanelSnapshot.CapturedPanels);
        Assert.Null(PanelSnapshot.TryGet("fdp_event_browser_test"));
        Assert.NotNull(window);
    }

    // ── Rail 2 — the dump carries a real field ───────────────────────────────────────────────────

    [Fact]
    public void AfterABuild_TheDumpCarriesTheHistoryServicesEvent()
    {
        PanelSnapshot.CaptureEnabled = true;
        var svc = new FakeHistoryService(new CapturedEventDto(7, "World", "DamageEvent", false, "boom", null));
        var panel = new EventBrowserPanel(svc);
        var window = MakeWindow(panel);

        window.SimulateDrawClientArea();

        var vm = PanelSnapshot.TryGet("fdp_event_browser_test");
        Assert.NotNull(vm);
        Assert.Equal("fdp_event_browser_test", vm!.PanelId);
        Assert.Equal(FdpEventBrowserWindow.Kind, vm.PanelKind);

        var dump = vm.Dump();
        Assert.Equal(1, dump["totalEventCount"]!.GetValue<int>());
        Assert.Equal(1, dump["visibleEventCount"]!.GetValue<int>());
        var rows = dump["events"]!.AsArray();
        Assert.Single(rows);
        Assert.Equal("DamageEvent", rows[0]!["typeName"]!.GetValue<string>());
        Assert.Equal(7u, rows[0]!["frame"]!.GetValue<uint>());
    }

    /// <summary>⭐⭐ A disabled type removes the row from the dump — the SAME filter
    /// <c>DrawEventList</c> applies, exercised headless.</summary>
    [Fact]
    public void ADisabledType_IsFilteredOutOfTheDump()
    {
        PanelSnapshot.CaptureEnabled = true;
        var svc = new FakeHistoryService(new CapturedEventDto(1, "World", "NoisyEvent", false, "noisy", null));
        var panel = new EventBrowserPanel(svc);
        // Reach the disabled-types set via the same field the filter popup mutates.
        var disabledTypes = (HashSet<string>)typeof(EventBrowserPanel)
            .GetField("_disabledTypes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(panel)!;
        disabledTypes.Add("NoisyEvent");
        var window = MakeWindow(panel);

        window.SimulateDrawClientArea();

        var dump = PanelSnapshot.TryGet("fdp_event_browser_test")!.Dump();
        Assert.Equal(1, dump["totalEventCount"]!.GetValue<int>());
        Assert.Equal(0, dump["visibleEventCount"]!.GetValue<int>());
        Assert.Empty(dump["events"]!.AsArray());
    }

    // ── Rail 3 — the flag gates the DUMP, not the BUILD ──────────────────────────────────────────

    [Fact]
    public void WithCaptureOff_TheProductionPathPublishesNothing()
    {
        var svc = new FakeHistoryService(new CapturedEventDto(1, "World", "E", false, "s", null));
        var window = MakeWindow(new EventBrowserPanel(svc));   // CaptureEnabled stays false

        var vm = window.SimulateDrawClientArea();

        Assert.Empty(PanelSnapshot.CapturedPanels);
        Assert.Contains("fdp_event_browser_test", PanelSnapshot.RegisteredPanels);
        Assert.NotNull(vm);   // ⭐ the BUILD is unaffected by the flag
        Assert.Equal(1, vm.TotalEventCount);
    }

    private static FdpEventBrowserWindow MakeWindow(EventBrowserPanel panel) =>
        new FdpEventBrowserWindow("fdp_event_browser_test", "Event Browser", "test-perspective", panel, new Vector4(1, 1, 1, 1));
}
