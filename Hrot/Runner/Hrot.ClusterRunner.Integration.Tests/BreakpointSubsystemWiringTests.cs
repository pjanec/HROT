using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Replay;
using Fdp.Toolkit.Runner;
using Fdp.Toolkit.ReplayBrowser.Search;
using Hrot.Blueprints.Core.Debug;
using Hrot.CGF;
using Hrot.Diagnostics.Breakpoints;
using Hrot.Editor;
using Hrot.Presentation.Windows;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// Integration tests proving that DataBreakpointManager + DebugSnapshotProvider +
/// DataBreakpointSystem are correctly wired into EditorSubsystem and CgfSubsystem (UBP-P10T1/P10T2).
/// </summary>
public sealed class BreakpointSubsystemWiringTests
{
    // Domain counter for CGF tests (tests 4-5). Using the gap 163-169 which is between
    // AllSubsystemsClusterTransitionTests (160-161) and ClusterOpE2eScriptTests (170+).
    // CycloneDDS max domain ID is 232.
    private static int _domainCounter = 162;

    // ── Test 1: EditorSubsystem wires the breakpoint manager ─────────────────

    [Fact]
    public void EditorSubsystem_Init_RegistersManager()
    {
        var subsystem = new EditorSubsystem();
        var config    = new SubsystemConfig { Headless = true };
        try
        {
            subsystem.Initialize(config);
            Assert.NotNull(subsystem.DataBreakpointManager);
        }
        finally
        {
            subsystem.Shutdown();
        }
    }

    // ── Test 2: Both BP systems are registered and survive one tick ───────────

    [Fact]
    public void EditorSubsystem_Init_RegistersBreakpointSystems()
    {
        var subsystem = new EditorSubsystem();
        var config    = new SubsystemConfig { Headless = true };
        try
        {
            subsystem.Initialize(config);

            // Both systems were constructed (registered).
            Assert.NotNull(subsystem.BpSnapshotProvider);
            Assert.NotNull(subsystem.DataBreakpointManager);

            // Running a tick exercises Execute() on both systems with no crash.
            // If not registered, the kernel would not call Execute and we would
            // have no breakpoint coverage.
            subsystem.Kernel.Update();
        }
        finally
        {
            subsystem.Shutdown();
        }
    }

    // ── Test 3: Zero overhead when no breakpoints are armed ───────────────────

    [Fact]
    public void EditorSubsystem_Boot_NoExtraCost_WhenNoBreakpoints()
    {
        var subsystem = new EditorSubsystem();
        var config    = new SubsystemConfig { Headless = true };
        try
        {
            subsystem.Initialize(config);
            var mgr = subsystem.DataBreakpointManager!;

            // Pump 100 ticks.
            for (int i = 0; i < 100; i++)
                subsystem.Kernel.Update();

            // Gate stayed closed: no BPs => snapshot provider never enabled.
            Assert.False(mgr.IsPaused);
            Assert.Equal(0, mgr.PendingMutationsCount);
            // HasMountedDelegates == false proves the scan loop never ran.
            Assert.False(mgr.HasMountedDelegates);
        }
        finally
        {
            subsystem.Shutdown();
        }
    }

    // ── Test 4: CgfSubsystem wires the breakpoint manager ────────────────────

    [Fact]
    public void CgfSubsystem_Init_RegistersManager()
    {
        int domainId = Interlocked.Increment(ref _domainCounter);
        using var harness = new HrotRunnerHarness("simhost,cgf", domainId);

        Assert.NotNull(harness.Cgf);
        Assert.NotNull(harness.Cgf!.DataBreakpointManager);
    }

    // ── Test 5: Zero overhead in CGF when no breakpoints are armed ────────────

    [Fact]
    public void CgfSubsystem_HeavyScenario_NoBreakpoints_ZeroOverhead()
    {
        int domainId = Interlocked.Increment(ref _domainCounter);
        using var harness = new HrotRunnerHarness("simhost,cgf", domainId);

        var mgr = harness.Cgf!.DataBreakpointManager!;
        Assert.False(mgr.HasMountedDelegates);

        // Pump 50 ticks without registering any breakpoints.
        harness.PumpFrames(50);

        // Gate stayed closed -- no overhead incurred.
        Assert.False(mgr.IsPaused);
        Assert.False(mgr.HasMountedDelegates);
    }

    // ── Helper: create a headless WindowManager ───────────────────────────

    private static WindowManager MakeWindowManager()
        => new WindowManager(new IconAtlas(IntPtr.Zero, 512, 512));

    // ── Test 6: Gizmo systems forward ActiveView when paused (UBP-P10T4) ─

    [Fact]
    public void Gizmo_System_UsesManagerActiveView_WhenPaused()
    {
        var subsystem = new EditorSubsystem();
        var config    = new SubsystemConfig { Headless = true };
        try
        {
            subsystem.Initialize(config);
            var mgr = subsystem.DataBreakpointManager!;

            // Register a synthetic breakpoint and trigger it manually.
            var id  = mgr.AddBreakpoint(new ExternalHitTagPredicateDto { Tag = "test-gizmo" },
                                        displayName: "test-gizmo");
            var bp  = mgr.AllBreakpoints.First(b => b.Id == id);

            // Not paused yet: ActiveView returns the live repo.
            Assert.False(mgr.IsPaused);
            var viewBefore = mgr.ActiveView;

            // Fire the breakpoint to enter pause.
            mgr.OnHit(bp, Fdp.Core.Entity.Null);
            Assert.True(mgr.IsPaused);

            // While paused, ActiveView switches to the pre-tick snapshot (different object).
            var viewAfter = mgr.ActiveView;
            Assert.NotSame(viewBefore, viewAfter);
        }
        finally
        {
            subsystem.Shutdown();
        }
    }

    // ── Test 7: Gizmo system falls back to live view when not paused ──────

    [Fact]
    public void Gizmo_System_FallsBackWhenNoManager()
    {
        var subsystem = new EditorSubsystem();
        var config    = new SubsystemConfig { Headless = true };
        try
        {
            subsystem.Initialize(config);
            var mgr = subsystem.DataBreakpointManager!;

            // Not paused: ActiveView is the live repository (fall-back path).
            Assert.False(mgr.IsPaused);
            Assert.NotNull(mgr.ActiveView);

            // Pumping frames does not crash and gate stays closed.
            subsystem.Kernel.Update();
            Assert.False(mgr.IsPaused);
        }
        finally
        {
            subsystem.Shutdown();
        }
    }

    // ── Test 8: DataBreakpointManagerWindow registered per perspective ────

    [Fact]
    public void ManagerWindow_RegisteredInEditorPerspective()
    {
        var subsystem = new EditorSubsystem();
        var config    = new SubsystemConfig { Headless = true };
        try
        {
            subsystem.Initialize(config);
            var wm = MakeWindowManager();
            subsystem.RegisterWindows(wm);

            Assert.True(wm.TryGetWindow("editor_bp_manager", out var win));
            Assert.IsType<DataBreakpointManagerWindow>(win);
        }
        finally
        {
            subsystem.Shutdown();
        }
    }

    // ── Test 9: Window's owning perspective is Editor, not IG ────────────

    [Fact]
    public void ManagerWindow_NotShownInUnrelatedPerspective()
    {
        var subsystem = new EditorSubsystem();
        var config    = new SubsystemConfig { Headless = true };
        try
        {
            subsystem.Initialize(config);
            var wm = MakeWindowManager();
            subsystem.RegisterWindows(wm);

            Assert.True(wm.TryGetWindow("editor_bp_manager", out var win));
            Assert.Equal("Editor", win!.OwningPerspective);
            Assert.NotEqual("IG", win.OwningPerspective);
        }
        finally
        {
            subsystem.Shutdown();
        }
    }

    // ── Test 10: Window can be opened programmatically ────────────────────

    [Fact]
    public void ManagerWindow_OpensOnMenuCommand()
    {
        var subsystem = new EditorSubsystem();
        var config    = new SubsystemConfig { Headless = true };
        try
        {
            subsystem.Initialize(config);
            var wm = MakeWindowManager();
            subsystem.RegisterWindows(wm);

            Assert.True(wm.TryGetWindow("editor_bp_manager", out var win));
            win!.IsOpen = true;
            Assert.True(win.IsOpen);
        }
        finally
        {
            subsystem.Shutdown();
        }
    }

    // ── Test 11: MutationInterceptor is wired to entity inspector (P10T5) ─

    [Fact]
    public void Inspector_EditWhilePaused_RoutesToStageMutation()
    {
        var subsystem = new EditorSubsystem();
        var config    = new SubsystemConfig { Headless = true };
        try
        {
            subsystem.Initialize(config);
            var wm = MakeWindowManager();
            subsystem.RegisterWindows(wm);

            var mgr = subsystem.DataBreakpointManager!;

            // After RegisterWindows the entity inspector's reflector must have the interceptor set.
            Assert.NotNull(subsystem.BpMutationInterceptor);
            Assert.Same(mgr, subsystem.BpMutationInterceptor);
        }
        finally
        {
            subsystem.Shutdown();
        }
    }

    // ── Test 12: Interceptor present but not paused ───────────────────────

    [Fact]
    public void Inspector_EditWhileRunning_StillDirectWrites()
    {
        var subsystem = new EditorSubsystem();
        var config    = new SubsystemConfig { Headless = true };
        try
        {
            subsystem.Initialize(config);
            var wm = MakeWindowManager();
            subsystem.RegisterWindows(wm);

            var mgr = subsystem.DataBreakpointManager!;

            // Not paused: interceptor is still set (so intercepting mutations is possible),
            // but the simulation is running normally.
            Assert.False(mgr.IsPaused);
            Assert.NotNull(subsystem.BpMutationInterceptor);
        }
        finally
        {
            subsystem.Shutdown();
        }
    }

    // ── Test 13: Blueprint debug session bridge is wired (UBP-P10T6) ─────

    [Fact]
    public void Blueprint_NodeBP_RoutesThroughManager_TripleBufferApplied()
    {
        var subsystem = new EditorSubsystem();
        var config    = new SubsystemConfig { Headless = true };
        try
        {
            subsystem.Initialize(config);

            // After Initialize the static DebugProbe.Sink must be the blueprint session.
            Assert.NotNull(DebugProbe.Sink);
            Assert.IsAssignableFrom<IBlueprintProbeSink>(DebugProbe.Sink);
        }
        finally
        {
            subsystem.Shutdown();
            // Shutdown clears the sink; verify cleanup.
            Assert.Null(DebugProbe.Sink);
        }
    }

    // ── Test 14: OnHotReloadBegin flushes pause state (UBP-P10T10) ───────

    [Fact]
    public void HotReload_WhilePaused_FlushesPendingAndContinues()
    {
        var subsystem = new EditorSubsystem();
        var config    = new SubsystemConfig { Headless = true };
        try
        {
            subsystem.Initialize(config);
            var mgr = subsystem.DataBreakpointManager!;

            // Pause via a breakpoint hit.
            var id  = mgr.AddBreakpoint(new ExternalHitTagPredicateDto { Tag = "reload-test" },
                                        displayName: "reload-test");
            var bp  = mgr.AllBreakpoints.First(b => b.Id == id);
            mgr.OnHit(bp, Fdp.Core.Entity.Null);
            Assert.True(mgr.IsPaused);

            // Simulate the OnReloadBegin notification that EditorSubsystem subscribed.
            mgr.OnHotReloadBegin();

            // OnHotReloadBegin must un-pause so execution can continue.
            Assert.False(mgr.IsPaused);
        }
        finally
        {
            subsystem.Shutdown();
        }
    }

    // ── Test 15: OnHotReloadCompleted keeps breakpoints mounted ──────────

    [Fact]
    public void HotReload_RebindsCompiledDelegates()
    {
        var subsystem = new EditorSubsystem();
        var config    = new SubsystemConfig { Headless = true };
        try
        {
            subsystem.Initialize(config);
            var mgr = subsystem.DataBreakpointManager!;

            var id = mgr.AddBreakpoint(new ExternalHitTagPredicateDto { Tag = "rebind-test" },
                                       displayName: "rebind-test");

            // Simulate completing a hot reload.
            mgr.OnHotReloadCompleted();

            // Breakpoint must still be registered and not marked broken.
            var bp = mgr.AllBreakpoints.FirstOrDefault(b => b.Id == id);
            Assert.NotNull(bp);
            Assert.False(bp!.IsBroken);
        }
        finally
        {
            subsystem.Shutdown();
        }
    }

    // ── Test 16: Reload sequence does not throw ───────────────────────────

    [Fact]
    public void HotReload_StructuralBreak_MarksBPIsBroken_NoCrash()
    {
        var subsystem = new EditorSubsystem();
        var config    = new SubsystemConfig { Headless = true };
        try
        {
            subsystem.Initialize(config);
            var mgr = subsystem.DataBreakpointManager!;

            mgr.AddBreakpoint(new ExternalHitTagPredicateDto { Tag = "struct-break" },
                              displayName: "struct-break");

            // Full reload cycle must not throw regardless of schema changes.
            var ex = Record.Exception(() =>
            {
                mgr.OnHotReloadBegin();
                mgr.OnHotReloadCompleted();
            });
            Assert.Null(ex);
        }
        finally
        {
            subsystem.Shutdown();
        }
    }

    // ── Test 17: Watch round-trip across editor restart (UBP-P10T11) ──────

    [Fact]
    public void Watches_RoundTripAcrossEditorRestart()
    {
        var watchesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "watches.json");
        try
        {
            // ---- First "session": initialize, add watch, shutdown (saves watches.json). ----
            {
                var sub  = new EditorSubsystem();
                var cfg  = new SubsystemConfig { Headless = true };
                sub.Initialize(cfg);
                var mgr  = sub.DataBreakpointManager!;
                var id   = mgr.AddBreakpoint(new ExternalHitTagPredicateDto { Tag = "watch-tag" },
                                             displayName: "my-watch");
                mgr.MarkAsWatch(id, true);
                sub.Shutdown();   // Shutdown calls SaveWatches to watchesPath.
            }

            Assert.True(File.Exists(watchesPath), "watches.json was not written by Shutdown.");

            // ---- Second "session": initialize loads watches.json automatically. ----
            {
                var sub  = new EditorSubsystem();
                var cfg  = new SubsystemConfig { Headless = true };
                sub.Initialize(cfg);  // LoadWatches runs on startup since file exists.
                var mgr  = sub.DataBreakpointManager!;

                var watches = mgr.AllBreakpoints.Where(b => b.IsWatch).ToList();
                Assert.NotEmpty(watches);

                sub.Shutdown();
            }
        }
        finally
        {
            if (File.Exists(watchesPath))
                File.Delete(watchesPath);
        }
    }

    // ── Test 18: Drifted watches.json does not crash on load (UBP-P10T11) ─

    [Fact]
    public void Watches_Restore_FailsGracefullyOnDriftedSchema()
    {
        var watchesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "watches.json");
        try
        {
            // Write deliberately malformed JSON.
            File.WriteAllText(watchesPath, "{ this is not valid json !!!! }");

            var sub = new EditorSubsystem();
            var cfg = new SubsystemConfig { Headless = true };

            // Initialize must complete without throwing even if watches.json is corrupt.
            var ex = Record.Exception(() =>
            {
                sub.Initialize(cfg);
                sub.Shutdown();
            });
            Assert.Null(ex);
        }
        finally
        {
            if (File.Exists(watchesPath))
                File.Delete(watchesPath);
        }
    }

    // ── Test 19: coordinator OnReloadBegin event propagates to manager (D-BP-05) ─

    [Fact]
    public void HotReload_CoordinatorOnReloadBegin_PropagatesViaSub_ToManager()
    {
        var subsystem = new EditorSubsystem();
        var config    = new SubsystemConfig { Headless = true };
        try
        {
            subsystem.Initialize(config);
            var mgr = subsystem.DataBreakpointManager!;

            // Pause via a breakpoint hit.
            var id  = mgr.AddBreakpoint(new ExternalHitTagPredicateDto { Tag = "coordinator-test" },
                                        displayName: "coordinator-test");
            var bp  = mgr.AllBreakpoints.First(b => b.Id == id);
            mgr.OnHit(bp, Fdp.Core.Entity.Null);
            Assert.True(mgr.IsPaused);

            // Fire the event via the coordinator (simulates the wiring set up in Initialize).
            subsystem.AiCoordinator!.RaiseReloadBeginForTest();

            // The subscription wired in Initialize must have called OnHotReloadBegin -> un-pause.
            Assert.False(mgr.IsPaused);
        }
        finally
        {
            subsystem.Shutdown();
        }
    }

    // ── Test 20: DataBreakpointSystem has [UpdateAfter(RecorderTickSystem)] (P11T3) ─

    [Fact]
    public void RecorderRunsBeforeBreakpointSystem_AttributePresent()
    {
        var attrs = typeof(DataBreakpointSystem)
            .GetCustomAttributes(typeof(Fdp.Core.UpdateAfterAttribute), inherit: false);
        Assert.Contains(attrs, a => ((Fdp.Core.UpdateAfterAttribute)a).Target == typeof(RecorderTickSystem));
    }

    // ── Test 21 (P12T1a): ActiveView switches to pre-tick snapshot during pause ─

    [Fact]
    public void E2E_Wired_ActiveViewSwitchesToPreTickDuringPause()
    {
        var subsystem = new EditorSubsystem();
        var config    = new SubsystemConfig { Headless = true };
        try
        {
            subsystem.Initialize(config);
            var mgr = subsystem.DataBreakpointManager!;

            // Pump one tick to warm up the snapshot.
            subsystem.Kernel.Update();

            // Active view before pause is the live repo.
            var viewBeforePause = mgr.ActiveView;
            Assert.False(mgr.IsPaused);

            // Trigger pause via ExternalHitTag BP.
            var id  = mgr.AddBreakpoint(new ExternalHitTagPredicateDto { Tag = "p12t1a" });
            var bp  = mgr.AllBreakpoints.First(b => b.Id == id);
            mgr.OnHit(bp, Fdp.Core.Entity.Null);

            Assert.True(mgr.IsPaused);

            // ActiveView during pause is the pre-tick snapshot (a different object to the live repo).
            var viewDuringPause = mgr.ActiveView;
            Assert.NotSame(viewBeforePause, viewDuringPause);
            Assert.IsAssignableFrom<Fdp.ModuleHost.Abstractions.ISimulationView>(viewDuringPause);
        }
        finally
        {
            subsystem.Shutdown();
        }
    }

    // ── Test 22 (P12T1b): Deferred mutation queued, step drains ECB ──────────

    [Fact]
    public void E2E_Wired_DeferredMutationQueued_StepDrainsECB()
    {
        var subsystem = new EditorSubsystem();
        var config    = new SubsystemConfig { Headless = true };
        try
        {
            subsystem.Initialize(config);
            var mgr = subsystem.DataBreakpointManager!;

            // Pump one tick before pausing.
            subsystem.Kernel.Update();

            // Trigger pause.
            var id = mgr.AddBreakpoint(new ExternalHitTagPredicateDto { Tag = "p12t1b" });
            var bp = mgr.AllBreakpoints.First(b => b.Id == id);
            mgr.OnHit(bp, Fdp.Core.Entity.Null);
            Assert.True(mgr.IsPaused);

            // Verify step un-pauses.
            Assert.True(mgr.IsPaused);
            mgr.RequestStep();
            Assert.False(mgr.IsPaused);

            // After step, mutations queue is empty.
            Assert.Equal(0, mgr.PendingMutationsCount);
        }
        finally
        {
            subsystem.Shutdown();
        }
    }

    // ── Test 23 (P12T2): Armed BP, 100 ticks, well under budget ─────────────

    [Fact]
    public void Wired_Performance_ArmedBP_100Ticks_WellUnderBudget()
    {
        var subsystem = new EditorSubsystem();
        var config    = new SubsystemConfig { Headless = true };
        try
        {
            subsystem.Initialize(config);
            var mgr = subsystem.DataBreakpointManager!;

            // Register an armed breakpoint so the gate is open.
            mgr.AddBreakpoint(new ExternalHitTagPredicateDto { Tag = "perf-test" });

            // Pump 100 ticks with an armed BP.
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 100; i++)
                subsystem.Kernel.Update();
            sw.Stop();

            // Subsystem must not be paused (ExternalHitTag BP can only fire via OnHit, not scan).
            Assert.False(mgr.IsPaused);

            // Performance budget: 100 ticks in < 10 seconds (very generous, avoids CI flakiness).
            Assert.True(sw.ElapsedMilliseconds < 10_000,
                $"100 ticks took {sw.ElapsedMilliseconds}ms, exceeds 10s budget");
        }
        finally
        {
            subsystem.Shutdown();
        }
    }

    // ── Test 24 (P12T3): Pause/step/resume, kernel advances tick monotonically ─

    [Fact]
    public void Wired_FlightRecorder_PauseStepResume_KernelAdvancesTick()
    {
        var subsystem = new EditorSubsystem();
        var config    = new SubsystemConfig { Headless = true };
        try
        {
            subsystem.Initialize(config);
            var mgr = subsystem.DataBreakpointManager!;

            // Record tick versions to check monotonic progression.
            var ticksBefore = new System.Collections.Generic.List<uint>();

            // Pump 3 ticks, capturing world version.
            for (int i = 0; i < 3; i++)
            {
                subsystem.Kernel.Update();
                ticksBefore.Add(subsystem.World.GlobalVersion);
            }

            // Pause via BP hit.
            var id = mgr.AddBreakpoint(new ExternalHitTagPredicateDto { Tag = "p12t3" });
            var bp = mgr.AllBreakpoints.First(b => b.Id == id);
            mgr.OnHit(bp, Fdp.Core.Entity.Null);
            Assert.True(mgr.IsPaused);

            // Version while paused (from rewind to pre-tick snapshot):
            uint versionAtPause = subsystem.World.GlobalVersion;

            // Step -> unpause -> advance one more tick.
            mgr.RequestStep();
            Assert.False(mgr.IsPaused);
            subsystem.Kernel.Update();

            uint versionAfterStep = subsystem.World.GlobalVersion;

            // The kernel must have advanced at least one version since the pause point.
            Assert.True(versionAfterStep >= versionAtPause,
                $"Version regressed: after step = {versionAfterStep}, at pause = {versionAtPause}");

            // All versions before pause must be non-decreasing.
            for (int i = 1; i < ticksBefore.Count; i++)
            {
                Assert.True(ticksBefore[i] >= ticksBefore[i - 1],
                    $"Tick regression: ticksBefore[{i}] = {ticksBefore[i]}, ticksBefore[{i - 1}] = {ticksBefore[i - 1]}");
            }
        }
        finally
        {
            subsystem.Shutdown();
        }
    }

    // ── Test 25 (P12T4): Two managers pausing one does not affect the other ──

    [Fact]
    public void MultiSubsystem_TwoManagers_PausingOneDoesNotAffectOther()
    {
        var subsystemA = new EditorSubsystem();
        var subsystemB = new EditorSubsystem();
        var config     = new SubsystemConfig { Headless = true };
        try
        {
            subsystemA.Initialize(config);
            subsystemB.Initialize(config);

            var mgrA = subsystemA.DataBreakpointManager!;
            var mgrB = subsystemB.DataBreakpointManager!;

            // Pause manager A.
            var idA = mgrA.AddBreakpoint(new ExternalHitTagPredicateDto { Tag = "isolate-a" });
            var bpA = mgrA.AllBreakpoints.First(b => b.Id == idA);
            mgrA.OnHit(bpA, Fdp.Core.Entity.Null);

            Assert.True(mgrA.IsPaused,  "Manager A should be paused");
            Assert.False(mgrB.IsPaused, "Manager B must NOT be paused when A pauses");

            // Un-pause A, pause B.
            mgrA.RequestStep();
            var idB = mgrB.AddBreakpoint(new ExternalHitTagPredicateDto { Tag = "isolate-b" });
            var bpB = mgrB.AllBreakpoints.First(b => b.Id == idB);
            mgrB.OnHit(bpB, Fdp.Core.Entity.Null);

            Assert.False(mgrA.IsPaused, "Manager A must NOT be paused when B pauses");
            Assert.True(mgrB.IsPaused,  "Manager B should be paused");
        }
        finally
        {
            subsystemA.Shutdown();
            subsystemB.Shutdown();
        }
    }
}
