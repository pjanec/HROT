using System;
using System.Collections.Generic;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.ModuleHost.Diagnostics;
using Fdp.Presentation.Panels;
using Hrot.Presentation.Windows;
using Xunit;

namespace Hrot.Presentation.Tests.Windows;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 — <c>ArchitectureDiagnosticsPanel</c>/<c>ArchitectureDiagnosticsWindow</c> converted to
/// the <c>PanelSnapshot</c> contract.</b> 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example ·
/// <c>docs/blueprints/batches/QUEUE_Panel_Observability_Sweep.md</c> group 4/5 ("the HOST registers"
/// gotcha — <c>ArchitectureDiagnosticsPanel</c> lives in <c>Fdp.Presentation</c> (group 4);
/// <c>ArchitectureDiagnosticsWindow</c>, its only production host, lives in <c>Hrot.Presentation</c>
/// (group 5) — converted together since they are one unit, not a twin duplicate).
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class ArchitectureDiagnosticsWindowDumpsItsSnapshotTests : IDisposable
{
    public ArchitectureDiagnosticsWindowDumpsItsSnapshotTests()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    public void Dispose()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    private sealed class FakeService : IArchitectureDiagnosticsService
    {
        private readonly ArchitectureSnapshotDto _snapshot;
        public FakeService(ArchitectureSnapshotDto snapshot) => _snapshot = snapshot;
        public ArchitectureSnapshotDto GetSnapshot() => _snapshot;
    }

    // ── Rail 1 — instrumented at construction, on the PRODUCTION object ─────────────────────────

    [Fact]
    public void ConstructingTheWindow_DeclaresItInstrumented_BeforeItHasEverDrawn()
    {
        Assert.DoesNotContain("archdiag_test", PanelSnapshot.RegisteredPanels);   // ⛔ anti-vacuity

        var window = MakeWindow(new ArchitectureSnapshotDto());

        Assert.Contains("archdiag_test", PanelSnapshot.RegisteredPanels);
        Assert.DoesNotContain("archdiag_test", PanelSnapshot.CapturedPanels);
        Assert.Null(PanelSnapshot.TryGet("archdiag_test"));
        Assert.NotNull(window);
    }

    // ── Rail 2 — the dump carries a real field ───────────────────────────────────────────────────

    [Fact]
    public void AfterABuild_TheDumpCarriesTheModuleAndSystemRows()
    {
        PanelSnapshot.CaptureEnabled = true;
        var snapshot = new ArchitectureSnapshotDto
        {
            Modules = new List<ModuleDiagnosticsDto>
            {
                new() { ModuleName = "Physics", ModuleTypeName = "PhysicsModule", CircuitState = "Closed" },
            },
        };
        var window = MakeWindow(snapshot);

        window.SimulateDrawClientArea();

        var vm = PanelSnapshot.TryGet("archdiag_test");
        Assert.NotNull(vm);
        Assert.Equal("archdiag_test", vm!.PanelId);
        Assert.Equal(ArchitectureDiagnosticsWindow.Kind, vm.PanelKind);

        var dump = vm.Dump();
        var modules = dump["modules"]!.AsArray();
        Assert.Single(modules);
        Assert.Equal("Physics", modules[0]!["moduleName"]!.GetValue<string>());
    }

    // ── Rail 3 — the flag gates the DUMP, not the BUILD ──────────────────────────────────────────

    [Fact]
    public void WithCaptureOff_TheProductionPathPublishesNothing()
    {
        var window = MakeWindow(new ArchitectureSnapshotDto());   // CaptureEnabled stays false

        var vm = window.SimulateDrawClientArea();

        Assert.Empty(PanelSnapshot.CapturedPanels);
        Assert.Contains("archdiag_test", PanelSnapshot.RegisteredPanels);
        Assert.NotNull(vm);   // ⭐ the BUILD is unaffected by the flag
    }

    private static ArchitectureDiagnosticsWindow MakeWindow(ArchitectureSnapshotDto snapshot) =>
        new ArchitectureDiagnosticsWindow(
            "archdiag_test", "Architecture Diagnostics", "test-perspective",
            new ArchitectureDiagnosticsPanel(new FakeService(snapshot)));
}
