using System;
using System.Collections.Generic;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.ModuleHost;
using Fdp.Presentation.Icons;
using Fdp.Toolkit.Runner;
using Hrot.Editor;
using Hrot.Presentation.Windows;
using Xunit;

namespace Hrot.Editor.Tests.Windows;

/// <summary>
/// ⭐⭐⭐ <c>BP-327</c> — <see cref="SystemProfilerWindow"/> wired as Editor's global window.
/// 📄 <c>SystemProfilerWindow</c>'s own remarks. Mirrors
/// <c>ExConDerEntityInspectorWindowDumpsItsModelTests</c> — dump rails on a directly constructed
/// window, plus the production composition-root rail proving
/// <see cref="EditorSubsystem.RegisterWindows"/> actually registers it.
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class EditorSystemProfilerWindowDumpsItsModelTests : IDisposable
{
    public EditorSystemProfilerWindowDumpsItsModelTests()
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
        Assert.DoesNotContain("editor_system_profiler_test", PanelSnapshot.RegisteredPanels);   // ⛔ anti-vacuity

        PanelSnapshot.CaptureEnabled = true;
        var stats = new List<ModuleStats>
        {
            new ModuleStats { ModuleName = "EditorBehaviorModule", ExecutionCount = 5, FailureCount = 2 },
        };
        var window = new SystemProfilerWindow(
            "editor_system_profiler_test", "Editor System Profiler", "Editor", () => stats);

        Assert.Contains("editor_system_profiler_test", PanelSnapshot.RegisteredPanels);
        window.SimulateDrawClientArea();

        var vm = PanelSnapshot.TryGet("editor_system_profiler_test");
        Assert.NotNull(vm);
        Assert.Equal(PanelIds.SystemProfiler, vm!.PanelKind);
        var dump = vm.Dump();
        var rows = dump["rows"]!.AsArray();
        Assert.Single(rows);
        Assert.Equal("EditorBehaviorModule", rows[0]!["moduleName"]!.GetValue<string>());
    }

    [Fact]
    public void Window_WithCaptureOff_PublishesNothing_ButStaysRegistered()
    {
        var window = new SystemProfilerWindow(
            "editor_system_profiler_test", "Editor System Profiler", "Editor", () => new List<ModuleStats>());

        Assert.Contains("editor_system_profiler_test", PanelSnapshot.RegisteredPanels);
        var vm = window.SimulateDrawClientArea();

        Assert.Empty(PanelSnapshot.CapturedPanels);
        Assert.NotNull(vm);
    }

    // ── production composition-root rail ─────────────────────────────────────────────────────
    // ⭐⭐⭐ the whole point of this conversion: prove the host is actually REGISTERED by
    // EditorSubsystem.RegisterWindows, not just constructible in a test.
    //
    // ⚠ Skipped: STRUCTURALLY BLOCKED, and PRE-EXISTING (not caused by this change). Measured:
    // EditorSubsystem.RegisterWindows has `if (_headless) return;` at line ~4133, BEFORE the FDP
    // framework panels / ArchitectureDiagnosticsWindow / SystemProfilerWindow registrations
    // (~4150-4230) — so a headless composition-root call never reaches this tail at all (confirmed
    // by reflection: `_kernel` is non-null after Initialize+RegisterWindows, yet NEITHER
    // 'editor_fdp_events' NOR the pre-existing 'editor_architecture_diagnostics' end up registered
    // either). A non-headless Initialize needs live Raylib graphics, unavailable in a unit test —
    // the same reason SimHost's and CGF's composition-root rails are blocked (see their reports).

    [Fact(Skip = "STRUCTURALLY BLOCKED, pre-existing: EditorSubsystem.RegisterWindows early-returns " +
                 "on `_headless` (line ~4133) before reaching the FDP-panel/diagnostics tail this window " +
                 "lives in — confirmed the pre-existing 'editor_architecture_diagnostics' is equally " +
                 "unregistered under Headless=true. Needs a non-headless (Raylib) host to exercise.")]
    public void EditorSubsystem_RegisterWindows_RegistersTheSystemProfilerWindow()
    {
        var subsystem = new EditorSubsystem();
        subsystem.Initialize(new SubsystemConfig { Headless = true });
        var wm = new Fdp.Presentation.WindowManager.WindowManager(new IconAtlas(IntPtr.Zero, 16f, 16f));

        subsystem.RegisterWindows(wm);

        Assert.True(wm.TryGetWindow("editor_system_profiler", out var window),
            "Expected 'editor_system_profiler' to be registered by EditorSubsystem.RegisterWindows.");
        Assert.IsType<SystemProfilerWindow>(window);
    }
}
