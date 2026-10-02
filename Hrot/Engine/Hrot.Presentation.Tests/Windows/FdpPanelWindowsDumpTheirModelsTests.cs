using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Core.Diagnostics;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Adapters;
using Fdp.Presentation.Panels;
using Hrot.Presentation.Windows;
using Xunit;

namespace Hrot.Presentation.Tests.Windows;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 — the three <c>Hrot.Presentation</c> hosts of <c>Fdp.Presentation</c> panels,
/// converted to the <c>PanelSnapshot</c> contract.</b> 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c>
/// §Example · <c>docs/blueprints/batches/QUEUE_Panel_Observability_Sweep.md</c> group 5 (the two twin
/// hosts of <c>EntityInspectorPanel</c>/<c>EventBrowserPanel</c> agree on kind via
/// <c>PanelIds.EntityInspector</c>/<c>PanelIds.EventBrowser</c>) and group 4 (<c>FdpEntityWatchWindow</c>
/// is the only production host of <c>EntityWatchPanel</c>).
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class FdpPanelWindowsDumpTheirModelsTests : IDisposable
{
    public FdpPanelWindowsDumpTheirModelsTests()
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
        public void Capture(string providerName, FdpEventBus eventBus, uint currentFrame) { }
        public CapturedEventDto[] GetHistory(System.Collections.Generic.IReadOnlyList<string>? providerFilter = null) => _events;
        public void ClearHistory() { }
        public void RewindHistory(uint frame) { }
    }

    // ── FdpEntityInspectorWindow (Hrot.Presentation twin) ────────────────────────────────────────

    [Fact]
    public void EntityInspectorWindow_RegistersUnderTheSharedKind_AndDumpsTheEntityCount()
    {
        PanelSnapshot.CaptureEnabled = true;
        using var repo = new EntityRepository();
        repo.CreateEntity();
        var adapter = new RepositoryAdapter(repo);
        var window = new FdpEntityInspectorWindow(
            "hrot_entity_inspector_test", "Entity Inspector", "test-perspective",
            new EntityInspectorPanel(), () => adapter, () => new InspectorState());

        Assert.Contains("hrot_entity_inspector_test", PanelSnapshot.RegisteredPanels);   // declared at ctor

        window.SimulateDrawClientArea();

        var vm = PanelSnapshot.TryGet("hrot_entity_inspector_test");
        Assert.NotNull(vm);
        Assert.Equal(PanelIds.EntityInspector, vm!.PanelKind);
        Assert.Equal(1, vm.Dump()["totalEntityCount"]!.GetValue<int>());
    }

    // ── FdpEventBrowserWindow (Hrot.Presentation twin) ───────────────────────────────────────────

    [Fact]
    public void EventBrowserWindow_RegistersUnderTheSharedKind_AndDumpsAnEvent()
    {
        PanelSnapshot.CaptureEnabled = true;
        var svc = new FakeHistoryService(new CapturedEventDto(3, "World", "E", false, "s", null));
        var window = new FdpEventBrowserWindow(
            "hrot_event_browser_test", "Event Browser", "test-perspective", new EventBrowserPanel(svc));

        window.SimulateDrawClientArea();

        var vm = PanelSnapshot.TryGet("hrot_event_browser_test");
        Assert.NotNull(vm);
        Assert.Equal(PanelIds.EventBrowser, vm!.PanelKind);
        Assert.Equal(1, vm.Dump()["totalEventCount"]!.GetValue<int>());
    }

    // ── FdpEntityWatchWindow (only host of EntityWatchPanel) ─────────────────────────────────────

    [ComponentId(350)]
    private struct WatchTag { public int Value; }

    [Fact]
    public void EntityWatchWindow_DeclaresInstrumentedBeforeDraw_AndDumpsComponentNamesWhenCaptureOn()
    {
        Assert.DoesNotContain("hrot_entity_watch_test", PanelSnapshot.RegisteredPanels);   // ⛔ anti-vacuity

        using var repo = new EntityRepository();
        repo.RegisterComponent<WatchTag>();
        var entity = repo.CreateEntity();
        repo.SetComponent(entity, new WatchTag { Value = 1 });
        var adapter = new RepositoryAdapter(repo);
        var window = new FdpEntityWatchWindow(
            "hrot_entity_watch_test", "Watch", "test-perspective",
            new EntityWatchPanel(entity), () => adapter);

        Assert.Contains("hrot_entity_watch_test", PanelSnapshot.RegisteredPanels);
        Assert.Null(PanelSnapshot.TryGet("hrot_entity_watch_test"));   // not captured — flag is off

        PanelSnapshot.CaptureEnabled = true;
        window.SimulateDrawClientArea();

        var vm = PanelSnapshot.TryGet("hrot_entity_watch_test");
        Assert.NotNull(vm);
        Assert.Equal("hrot_entity_watch_test", vm!.PanelId);
        Assert.Equal(FdpEntityWatchWindow.Kind, vm.PanelKind);
        var dump = vm.Dump();
        Assert.True(dump["entityAlive"]!.GetValue<bool>());
        Assert.Contains(dump["componentTypeNames"]!.AsArray(), n => n!.GetValue<string>() == nameof(WatchTag));
    }

    [Fact]
    public void WithCaptureOff_TheProductionPathPublishesNothing_ForAllThreeHosts()
    {
        using var repo = new EntityRepository();
        var entity = repo.CreateEntity();
        var adapter = new RepositoryAdapter(repo);

        var inspector = new FdpEntityInspectorWindow(
            "hrot_ei_off", "Entity Inspector", "p", new EntityInspectorPanel(), () => adapter, () => new InspectorState());
        var browser = new FdpEventBrowserWindow("hrot_eb_off", "Event Browser", "p", new EventBrowserPanel());
        var watch = new FdpEntityWatchWindow("hrot_ew_off", "Watch", "p", new EntityWatchPanel(entity), () => adapter);

        inspector.SimulateDrawClientArea();
        browser.SimulateDrawClientArea();
        watch.SimulateDrawClientArea();

        Assert.Empty(PanelSnapshot.CapturedPanels);
        Assert.Contains("hrot_ei_off", PanelSnapshot.RegisteredPanels);
        Assert.Contains("hrot_eb_off", PanelSnapshot.RegisteredPanels);
        Assert.Contains("hrot_ew_off", PanelSnapshot.RegisteredPanels);
    }
}
