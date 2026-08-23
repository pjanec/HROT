using System;
using System.Collections.Generic;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.ModuleHost;
using Fdp.Presentation.Icons;
using Fdp.Toolkit.Runner;
using Hrot.Editor;
using Hrot.Presentation.Windows;
using Xunit;

namespace Hrot.StrideMock.Tests.Windows;

/// <summary>
/// ⭐⭐⭐ <c>BP-327</c> — <see cref="SystemProfilerWindow"/> wired as StrideMock's global window.
/// 📄 <c>SystemProfilerWindow</c>'s own remarks. Mirrors
/// <c>ExConDerEntityInspectorWindowDumpsItsModelTests</c> — dump rails on a directly constructed
/// window, plus the production composition-root rail proving
/// <see cref="StrideMockSubsystem.RegisterWindows"/> actually registers it.
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class StrideMockSystemProfilerWindowDumpsItsModelTests : IDisposable
{
    public StrideMockSystemProfilerWindowDumpsItsModelTests()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    public void Dispose()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    [Fact]
    public void Window_DeclaresItInstrumented_AndDumpsTheRows()
    {
        Assert.DoesNotContain("stridemock_system_profiler_test", PanelSnapshot.RegisteredPanels);   // ⛔ anti-vacuity

        PanelSnapshot.CaptureEnabled = true;
        var stats = new List<ModuleStats>
        {
            new ModuleStats { ModuleName = "StrideMockSyncModule", ExecutionCount = 3, FailureCount = 0 },
        };
        var window = new SystemProfilerWindow(
            "stridemock_system_profiler_test", "StrideMock System Profiler", "StrideMock", () => stats);

        Assert.Contains("stridemock_system_profiler_test", PanelSnapshot.RegisteredPanels);
        window.SimulateDrawClientArea();

        var vm = PanelSnapshot.TryGet("stridemock_system_profiler_test");
        Assert.NotNull(vm);
        Assert.Equal(PanelIds.SystemProfiler, vm!.PanelKind);
        var dump = vm.Dump();
        var rows = dump["rows"]!.AsArray();
        Assert.Single(rows);
        Assert.Equal("StrideMockSyncModule", rows[0]!["moduleName"]!.GetValue<string>());
    }

    [Fact]
    public void Window_WithCaptureOff_PublishesNothing_ButStaysRegistered()
    {
        var window = new SystemProfilerWindow(
            "stridemock_system_profiler_test", "StrideMock System Profiler", "StrideMock", () => new List<ModuleStats>());

        Assert.Contains("stridemock_system_profiler_test", PanelSnapshot.RegisteredPanels);
        var vm = window.SimulateDrawClientArea();

        Assert.Empty(PanelSnapshot.CapturedPanels);
        Assert.NotNull(vm);
    }

    // ── production composition-root rail ─────────────────────────────────────────────────────
    // ⭐⭐⭐ the whole point of this conversion: prove the host is actually REGISTERED by
    // StrideMockSubsystem.RegisterWindows, not just constructible in a test.

    [Fact]
    public void StrideMockSubsystem_RegisterWindows_RegistersTheSystemProfilerWindow()
    {
        var subsystem = new StrideMockSubsystem(new OfflineNetworkFactory());
        subsystem.Initialize(new SubsystemConfig
        {
            DomainId      = 228,
            Headless      = true,
            OwnWindow     = false,
            NodeId        = 701,
            SubsystemName = "StrideMock",
        });
        try
        {
            var wm = new Fdp.Presentation.WindowManager.WindowManager(new IconAtlas(IntPtr.Zero, 16f, 16f));

            subsystem.RegisterWindows(wm);

            Assert.True(wm.TryGetWindow("stridemock_system_profiler", out var window),
                "Expected 'stridemock_system_profiler' to be registered by StrideMockSubsystem.RegisterWindows.");
            Assert.IsType<SystemProfilerWindow>(window);
        }
        finally
        {
            subsystem.Shutdown();
        }
    }
}
