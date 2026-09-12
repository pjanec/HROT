using System;
using System.Collections.Generic;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.ModuleHost;
using Fdp.ModuleHost.Resilience;
using Fdp.Presentation.Panels;
using Hrot.Presentation.Windows;
using Xunit;

namespace Hrot.Presentation.Tests.Windows;

/// <summary>
/// ⭐⭐⭐ <c>BP-327</c> — <see cref="SystemProfilerPanel"/> converted to the <c>PanelSnapshot</c> contract.
/// 📄 <see cref="SystemProfilerWindow"/>'s own remarks. ⚠ Mirrors
/// <c>ArchitectureDiagnosticsWindowDumpsItsSnapshotTests</c> — the panel-level dump rail, separate from
/// the per-host composition-root rails (one per registering subsystem).
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class SystemProfilerWindowDumpsItsSnapshotTests : IDisposable
{
    public SystemProfilerWindowDumpsItsSnapshotTests()
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
        Assert.DoesNotContain("sysprof_test", PanelSnapshot.RegisteredPanels);   // ⛔ anti-vacuity

        var window = MakeWindow(() => new List<ModuleStats>());

        Assert.Contains("sysprof_test", PanelSnapshot.RegisteredPanels);
        Assert.DoesNotContain("sysprof_test", PanelSnapshot.CapturedPanels);
        Assert.Null(PanelSnapshot.TryGet("sysprof_test"));
        Assert.NotNull(window);
    }

    // ── Rail 2 — the dump carries a real field ───────────────────────────────────────────────────

    [Fact]
    public void AfterABuild_TheDumpCarriesTheModuleRows()
    {
        PanelSnapshot.CaptureEnabled = true;
        var stats = new List<ModuleStats>
        {
            new ModuleStats { ModuleName = "PhysicsModule", ExecutionCount = 42, FailureCount = 1, CircuitState = CircuitState.Closed },
        };
        var window = MakeWindow(() => stats);

        window.SimulateDrawClientArea();

        var vm = PanelSnapshot.TryGet("sysprof_test");
        Assert.NotNull(vm);
        Assert.Equal("sysprof_test", vm!.PanelId);
        Assert.Equal(SystemProfilerWindow.Kind, vm.PanelKind);
        Assert.Equal(PanelIds.SystemProfiler, vm.PanelKind);

        var dump = vm.Dump();
        var rows = dump["rows"]!.AsArray();
        Assert.Single(rows);
        Assert.Equal("PhysicsModule", rows[0]!["moduleName"]!.GetValue<string>());
    }

    // ── Rail 3 — the flag gates the DUMP, not the BUILD ──────────────────────────────────────────

    [Fact]
    public void WithCaptureOff_TheProductionPathPublishesNothing()
    {
        var window = MakeWindow(() => new List<ModuleStats>());   // CaptureEnabled stays false

        var vm = window.SimulateDrawClientArea();

        Assert.Empty(PanelSnapshot.CapturedPanels);
        Assert.Contains("sysprof_test", PanelSnapshot.RegisteredPanels);
        Assert.NotNull(vm);   // ⭐ the BUILD is unaffected by the flag
    }

    // ── Rail 4 — a null stats provider result is tolerated ───────────────────────────────────────

    [Fact]
    public void WithNullStats_TheDumpCarriesNoRows_AndDoesNotThrow()
    {
        PanelSnapshot.CaptureEnabled = true;
        var window = MakeWindow(() => null);

        var vm = window.SimulateDrawClientArea();

        Assert.Empty(vm.Rows);
    }

    private static SystemProfilerWindow MakeWindow(Func<List<ModuleStats>?> statsProvider) =>
        new SystemProfilerWindow(
            "sysprof_test", "System Profiler", "test-perspective", statsProvider);
}
