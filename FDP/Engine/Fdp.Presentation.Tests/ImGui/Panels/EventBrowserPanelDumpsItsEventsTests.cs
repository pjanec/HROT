using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core.Diagnostics;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Presentation.Panels;
using Xunit;

namespace Fdp.Presentation.Tests.ImGui.Panels;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 — <c>EventBrowserPanel</c>/<c>FdpEventBrowserWindow</c> converted to the
/// <c>PanelSnapshot</c> contract.</b> 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example ·
/// <c>docs/blueprints/batches/QUEUE_Panel_Observability_Sweep.md</c> group 4 ("the HOST registers" gotcha
/// — <c>EventBrowserPanel</c> is the plain panel, <c>FdpEventBrowserWindow</c> the host).
/// <para>⭐ <c>CE-1244</c> (DESIGN_Subsystem_Composition_Unification §5c.7.8) — the ReplayBrowser's copy of the window is gone (it
/// composes the shared diagnostics bundle). The window rails (instrumented at construction, capture off) live with the one window in
/// <c>Hrot.Presentation.Tests/Windows/FdpPanelWindowsDumpTheirModelsTests</c>; these keep the PANEL's dump content.</para>
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

    // ── Rail 2 — the dump carries a real field ───────────────────────────────────────────────────

    [Fact]
    public void AfterABuild_TheDumpCarriesTheHistoryServicesEvent()
    {
        PanelSnapshot.CaptureEnabled = true;
        var svc = new FakeHistoryService(new CapturedEventDto(7, "World", "DamageEvent", false, "boom", null));
        var panel = new EventBrowserPanel(svc);
        var vm = panel.BuildViewModel("fdp_event_browser_test", PanelIds.EventBrowser);
        Assert.NotNull(vm);
        Assert.Equal("fdp_event_browser_test", vm!.PanelId);
        Assert.Equal(PanelIds.EventBrowser, vm.PanelKind);

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
        var dump = panel.BuildViewModel("fdp_event_browser_test", PanelIds.EventBrowser).Dump();
        Assert.Equal(1, dump["totalEventCount"]!.GetValue<int>());
        Assert.Equal(0, dump["visibleEventCount"]!.GetValue<int>());
        Assert.Empty(dump["events"]!.AsArray());
    }

}
