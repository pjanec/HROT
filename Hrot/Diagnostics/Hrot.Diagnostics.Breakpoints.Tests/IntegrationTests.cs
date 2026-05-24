using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Fdp.Core;
using Fdp.Core.FlightRecorder;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replay;
using Fdp.Toolkit.ReplayBrowser.Search;
using Hrot.Diagnostics.Breakpoints;
using StructEdit.Reflection;

namespace Hrot.Diagnostics.Breakpoints.Tests;

// ---- Test-only components (file-scoped to avoid ID conflicts) ---------------

[ComponentId(240)]
file struct E2EHealthComp { public int Value; }

[ComponentId(241)]
file struct E2EAmmoComp { public int Count; }

[ComponentId(242)]
file struct E2ERecorderComp { public int Data; }

// =============================================================================
// UBP-INT1 + INT2 + INT3 — Integration Tests
// =============================================================================

[Collection("ComponentRegistry")]
public sealed class IntegrationTests
{
    // =========================================================================
    // INT1: E2E flow tests
    // =========================================================================

    /// <summary>
    /// Full end-to-end: a PropertyMatch breakpoint fires when Health drops below 10,
    /// the live repo is rewound to pre-tick, and RequestStep restores the post-tick value.
    /// </summary>
    [Fact]
    public void E2E_PropertyMatchBreakpoint_PausesAndStepsCleanly()
    {
        // ── Arrange ──────────────────────────────────────────────────────────────
        ComponentTypeRegistry.Clear();

        var liveRepo = new EntityRepository();
        liveRepo.RegisterComponent<E2EHealthComp>();
        var preTickSnapshot = new EntityRepository();
        preTickSnapshot.RegisterComponent<E2EHealthComp>();

        var tc               = new MockDebugTimeController();
        var snapshotProvider = new DebugSnapshotProvider(preTickSnapshot);
        var compiler         = new PredicateCompiler(new ComponentEditServiceBuilder().Build());
        var mgr              = new DataBreakpointManager(
            liveRepo, preTickSnapshot, snapshotProvider, tc, predicateCompiler: compiler);
        var system           = new DataBreakpointSystem(mgr);

        // Register breakpoint: Health.Value < 10
        mgr.AddBreakpoint(
            new PropertyMatchDto
            {
                ComponentType = typeof(E2EHealthComp),
                PropertyPath  = "Value",
                Predicate     = new NumericPredicateDto { MaxValue = 9.999 },
            },
            displayName: "E2E-HealthBP");

        // Create entity with Health = 20 (above threshold)
        var entity = liveRepo.CreateEntity();
        liveRepo.AddComponent(entity, new E2EHealthComp { Value = 20 });

        // ── Pre-tick snapshot (gate is open because breakpoint is enabled) ──────
        snapshotProvider.Execute(liveRepo, 0.016f);  // captures Health = 20
        Assert.Equal(20, preTickSnapshot.GetComponent<E2EHealthComp>(entity).Value);

        // ── Advance version + mutate to trigger breakpoint ──────────────────────
        liveRepo.Tick();
        ref var h = ref liveRepo.GetComponentRW<E2EHealthComp>(entity);
        h.Value = 5; // < 10 → triggers predicate

        // ── Run system → fires OnHit → pause ────────────────────────────────────
        system.Execute(liveRepo, 0.016f);

        // ── Assert: paused; ActiveView shows pre-tick state (Health = 20) ────────
        Assert.True(mgr.IsPaused);
        var snapshot = (EntityRepository)mgr.ActiveView;
        Assert.Equal(20, snapshot.GetComponent<E2EHealthComp>(entity).Value);

        // Live repo was rewound to pre-tick (Health = 20)
        Assert.Equal(20, liveRepo.GetComponent<E2EHealthComp>(entity).Value);

        // ── Step → unpauses, restores post-tick (Health = 5) ────────────────────
        mgr.RequestStep();
        Assert.False(mgr.IsPaused);
        Assert.Equal(5, liveRepo.GetComponent<E2EHealthComp>(entity).Value);
    }

    /// <summary>
    /// A compound And[Health &lt; 10, Ammo == 0] breakpoint fires only when both
    /// conditions are met simultaneously, not when only one is true.
    /// </summary>
    [Fact]
    public void E2E_CompoundPredicate_FiresOnlyWhenBothConditionsMet()
    {
        ComponentTypeRegistry.Clear();

        var liveRepo = new EntityRepository();
        liveRepo.RegisterComponent<E2EHealthComp>();
        liveRepo.RegisterComponent<E2EAmmoComp>();
        var preTickSnapshot = new EntityRepository();
        preTickSnapshot.RegisterComponent<E2EHealthComp>();
        preTickSnapshot.RegisterComponent<E2EAmmoComp>();

        var tc               = new MockDebugTimeController();
        var snapshotProvider = new DebugSnapshotProvider(preTickSnapshot);
        var compiler         = new PredicateCompiler(new ComponentEditServiceBuilder().Build());
        var mgr              = new DataBreakpointManager(
            liveRepo, preTickSnapshot, snapshotProvider, tc, predicateCompiler: compiler);
        var system           = new DataBreakpointSystem(mgr);

        // Compound: Health < 10 AND Ammo == 0
        mgr.AddBreakpoint(
            new CompoundPredicateDto
            {
                Operator   = LogicalOperator.And,
                Conditions = new System.Collections.Generic.List<SearchPredicateDto>
                {
                    new PropertyMatchDto
                    {
                        ComponentType = typeof(E2EHealthComp),
                        PropertyPath  = "Value",
                        Predicate     = new NumericPredicateDto { MaxValue = 9.999 },
                    },
                    new PropertyMatchDto
                    {
                        ComponentType = typeof(E2EAmmoComp),
                        PropertyPath  = "Count",
                        Predicate     = new NumericPredicateDto { MinValue = -0.001, MaxValue = 0.001 },
                    },
                },
            },
            displayName: "CompoundBP");

        var entity = liveRepo.CreateEntity();
        liveRepo.AddComponent(entity, new E2EHealthComp { Value = 20 });
        liveRepo.AddComponent(entity, new E2EAmmoComp { Count = 5 });

        // ── Tick 1: Health=20, Ammo=5 → both conditions false → no hit ──────────
        snapshotProvider.Execute(liveRepo, 0.016f);
        liveRepo.Tick();
        system.Execute(liveRepo, 0.016f);
        Assert.False(mgr.IsPaused);

        // ── Tick 2: Health=5, Ammo=5 → only health condition met → no hit ───────
        snapshotProvider.Execute(liveRepo, 0.016f);
        liveRepo.Tick();
        liveRepo.GetComponentRW<E2EHealthComp>(entity).Value = 5;
        system.Execute(liveRepo, 0.016f);
        Assert.False(mgr.IsPaused);

        // ── Tick 3: Health=5 (still), Ammo=0 → both conditions met → HIT ────────
        snapshotProvider.Execute(liveRepo, 0.016f);
        liveRepo.Tick();
        liveRepo.GetComponentRW<E2EAmmoComp>(entity).Count = 0;
        system.Execute(liveRepo, 0.016f);
        Assert.True(mgr.IsPaused);
    }

    /// <summary>
    /// A mutation staged while paused is carried in the ECB and applied at N+1
    /// when the caller performs ECB playback after RequestStep.
    /// </summary>
    [Fact]
    public void E2E_DeferredMutation_AppliedAtNplus1()
    {
        ComponentTypeRegistry.Clear();

        var liveRepo = new EntityRepository();
        liveRepo.RegisterComponent<E2EHealthComp>();
        var preTickSnapshot = new EntityRepository();
        preTickSnapshot.RegisterComponent<E2EHealthComp>();

        var tc               = new MockDebugTimeController();
        var snapshotProvider = new DebugSnapshotProvider(preTickSnapshot);
        var compiler         = new PredicateCompiler(new ComponentEditServiceBuilder().Build());
        var mgr              = new DataBreakpointManager(
            liveRepo, preTickSnapshot, snapshotProvider, tc, predicateCompiler: compiler);
        var system           = new DataBreakpointSystem(mgr);

        mgr.AddBreakpoint(
            new PropertyMatchDto
            {
                ComponentType = typeof(E2EHealthComp),
                PropertyPath  = "Value",
                Predicate     = new NumericPredicateDto { MaxValue = 9.999 },
            });

        var entity = liveRepo.CreateEntity();
        liveRepo.AddComponent(entity, new E2EHealthComp { Value = 20 });

        // Trigger breakpoint to reach paused state
        snapshotProvider.Execute(liveRepo, 0.016f);
        liveRepo.Tick();
        liveRepo.GetComponentRW<E2EHealthComp>(entity).Value = 5;
        system.Execute(liveRepo, 0.016f);
        Assert.True(mgr.IsPaused);

        // Stage mutation: Health = 1000
        mgr.StageMutation(entity, typeof(E2EHealthComp), new E2EHealthComp { Value = 1000 });
        Assert.Equal(1, mgr.PendingMutationsCount);

        // Step: drains ECB, restores post-tick, advances time
        mgr.RequestStep();
        Assert.False(mgr.IsPaused);
        Assert.Equal(0, mgr.PendingMutationsCount);

        // Apply the ECB to liveRepo (simulates kernel's ECB flush at N+1 tick boundary)
        ISimulationView view = liveRepo;
        var ecb = (EntityCommandBuffer)view.GetCommandBuffer();
        ecb.Playback(liveRepo);

        // Assert: deferred mutation applied
        Assert.Equal(1000, liveRepo.GetComponent<E2EHealthComp>(entity).Value);
    }

    // =========================================================================
    // INT2: Performance budget tests
    // =========================================================================

    /// <summary>
    /// 1000 entities, no breakpoints: snapshot gate is closed and system early-exits.
    /// 100 ticks must complete in under 500ms on any reasonable CI machine.
    /// </summary>
    [Fact]
    public void Perf_HeavyScenario_NoBreakpoints_FastPath()
    {
        // 1000 entities, no breakpoints. DebugSnapshotProvider gate is CLOSED
        // (no enabled BPs), so snapshotProvider.Execute() is a no-op.
        // DataBreakpointSystem.Execute() early-returns (HasMountedDelegates == false).
        // 100 ticks must complete in < 500ms on any reasonable CI machine.
        ComponentTypeRegistry.Clear();

        var liveRepo         = new EntityRepository();
        liveRepo.RegisterComponent<E2EHealthComp>();
        var preTickSnapshot  = new EntityRepository();
        preTickSnapshot.RegisterComponent<E2EHealthComp>();
        var tc               = new MockDebugTimeController();
        var snapshotProvider = new DebugSnapshotProvider(preTickSnapshot);
        var mgr              = new DataBreakpointManager(
            liveRepo, preTickSnapshot, snapshotProvider, tc);
        var system           = new DataBreakpointSystem(mgr);

        // Spawn 1000 entities with Health = 20 each
        for (int i = 0; i < 1000; i++)
        {
            var e = liveRepo.CreateEntity();
            liveRepo.AddComponent(e, new E2EHealthComp { Value = 20 });
        }

        // No breakpoints registered → gate closed
        var sw = Stopwatch.StartNew();
        for (int tick = 0; tick < 100; tick++)
        {
            snapshotProvider.Execute(liveRepo, 0.016f);
            liveRepo.Tick();
            system.Execute(liveRepo, 0.016f);
        }
        sw.Stop();

        // < 500ms for 100 ticks of 1000 entities with zero work
        Assert.True(sw.ElapsedMilliseconds < 500,
            $"Expected < 500ms but took {sw.ElapsedMilliseconds}ms");
    }

    /// <summary>
    /// 1000 entities, one armed breakpoint scanning Health &lt; 10 (no hits).
    /// The scan still runs every tick. 100 ticks must complete in under 5000ms.
    /// </summary>
    [Fact]
    public void Perf_HeavyScenario_OneActiveBreakpoint_FitsBudget()
    {
        // 1000 entities, 1 armed breakpoint scanning Health < 10.
        // All entities have Health = 20 (no hits), but the scan still runs.
        // 100 ticks must complete in < 5000ms on any reasonable CI machine.
        ComponentTypeRegistry.Clear();

        var liveRepo         = new EntityRepository();
        liveRepo.RegisterComponent<E2EHealthComp>();
        var preTickSnapshot  = new EntityRepository();
        preTickSnapshot.RegisterComponent<E2EHealthComp>();
        var tc               = new MockDebugTimeController();
        var snapshotProvider = new DebugSnapshotProvider(preTickSnapshot);
        var compiler         = new PredicateCompiler(new ComponentEditServiceBuilder().Build());
        var mgr              = new DataBreakpointManager(
            liveRepo, preTickSnapshot, snapshotProvider, tc, predicateCompiler: compiler);
        var system           = new DataBreakpointSystem(mgr);

        mgr.AddBreakpoint(
            new PropertyMatchDto
            {
                ComponentType = typeof(E2EHealthComp),
                PropertyPath  = "Value",
                Predicate     = new NumericPredicateDto { MaxValue = 9.999 },
            });

        // Spawn 1000 entities with Health = 20 (above threshold, so no breakpoint fires)
        for (int i = 0; i < 1000; i++)
        {
            var e = liveRepo.CreateEntity();
            liveRepo.AddComponent(e, new E2EHealthComp { Value = 20 });
        }

        var sw = Stopwatch.StartNew();
        for (int tick = 0; tick < 100; tick++)
        {
            snapshotProvider.Execute(liveRepo, 0.016f);
            liveRepo.Tick();
            system.Execute(liveRepo, 0.016f);
        }
        sw.Stop();

        // < 5000ms for 100 ticks of 1000 entities with one active scan
        Assert.True(sw.ElapsedMilliseconds < 5000,
            $"Expected < 5000ms but took {sw.ElapsedMilliseconds}ms");
    }

    // =========================================================================
    // INT3: Recorder invariance test
    // =========================================================================

    /// <summary>
    /// Recording + breakpoint pause/step cycle produces frames with monotonically
    /// non-decreasing tick values in the .fdp output file.
    /// </summary>
    [Fact]
    public void Recorder_PausedSession_ProducesMonotonicTicks()
    {
        ComponentTypeRegistry.Clear();

        var tempFile = Path.Combine(Path.GetTempPath(), $"int3_recorder_{Guid.NewGuid():N}.fdp");
        try
        {
            // ── Arrange ─────────────────────────────────────────────────────────────
            var liveRepo = new EntityRepository();
            liveRepo.RegisterComponent<E2ERecorderComp>();
            var preTickSnapshot = new EntityRepository();
            preTickSnapshot.RegisterComponent<E2ERecorderComp>();

            var tc               = new MockDebugTimeController();
            var snapshotProvider = new DebugSnapshotProvider(preTickSnapshot);
            var compiler         = new PredicateCompiler(new ComponentEditServiceBuilder().Build());
            var mgr              = new DataBreakpointManager(
                liveRepo, preTickSnapshot, snapshotProvider, tc, predicateCompiler: compiler);
            var bpSystem         = new DataBreakpointSystem(mgr);

            // Set up recorder (blocking = true for deterministic tests)
            var config = new RecordingConfiguration
            {
                FilePath   = tempFile,
                ExerciseId = Guid.NewGuid(),
                Blocking   = true,
            };
            var module   = new RecordingModule(config);
            var captured = new CapturingSystemRegistry();
            module.RegisterSystems(captured);
            var recorderSystem = captured.Systems[0]; // RecorderTickSystem

            // Create entity with Data = 10 (above threshold, no breakpoint yet)
            var entity = liveRepo.CreateEntity();
            liveRepo.AddComponent(entity, new E2ERecorderComp { Data = 10 });

            // Register breakpoint: Data < 5
            mgr.AddBreakpoint(
                new PropertyMatchDto
                {
                    ComponentType = typeof(E2ERecorderComp),
                    PropertyPath  = "Data",
                    Predicate     = new NumericPredicateDto { MaxValue = 4.999 },
                });

            // ── Tick 1: normal frame (no breakpoint) ─────────────────────────────
            liveRepo.SetSingletonUnmanaged(new GlobalTime
            {
                DeltaTime = 0.016f, TimeScale = 1.0f,
                TotalWallTicks = DateTime.UtcNow.Ticks
            });
            snapshotProvider.Execute(liveRepo, 0.016f);
            liveRepo.Tick();
            recorderSystem.Execute(liveRepo, 0.016f); // Frame recorded at GlobalVersion = 1
            Assert.False(mgr.IsPaused);

            // ── Tick 2: trigger breakpoint, pause ────────────────────────────────
            snapshotProvider.Execute(liveRepo, 0.016f);
            liveRepo.Tick();
            liveRepo.GetComponentRW<E2ERecorderComp>(entity).Data = 3; // < 5 → fires
            bpSystem.Execute(liveRepo, 0.016f);
            Assert.True(mgr.IsPaused);
            // Recorder does NOT capture while paused (kernel stops calling it in real engine).
            // In this test, we simply don't call recorderSystem.Execute while paused.

            // ── Step: unpause ─────────────────────────────────────────────────────
            mgr.RequestStep();
            Assert.False(mgr.IsPaused);

            // ── Tick 3: resume, record another frame ─────────────────────────────
            liveRepo.SetSingletonUnmanaged(new GlobalTime
            {
                DeltaTime = 0.016f, TimeScale = 1.0f,
                TotalWallTicks = DateTime.UtcNow.Ticks + 1000
            });
            snapshotProvider.Execute(liveRepo, 0.016f);
            liveRepo.Tick();
            recorderSystem.Execute(liveRepo, 0.016f); // Frame recorded at GlobalVersion = 3

            // ── Flush recorder ───────────────────────────────────────────────────
            module.Dispose(); // blocks until LZ4 buffers flushed

            // ── Assert: .fdp exists ───────────────────────────────────────────────
            Assert.True(File.Exists(tempFile), $"Expected .fdp file at {tempFile}");

            // ── Assert: frame tick values are monotonically non-decreasing ────────
            var ticks = ReadFrameTicks(tempFile);
            Assert.True(ticks.Count >= 2, $"Expected at least 2 frames, got {ticks.Count}");
            for (int i = 1; i < ticks.Count; i++)
            {
                Assert.True(ticks[i] >= ticks[i - 1],
                    $"Frame {i} tick {ticks[i]} < frame {i - 1} tick {ticks[i - 1]} (not monotonic)");
            }
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
            if (File.Exists(tempFile + ".meta.json")) File.Delete(tempFile + ".meta.json");
        }
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    /// <summary>
    /// Reads all frame tick values from a .fdp recording file.
    /// Format: [GlobalHeader: Size bytes] [FrameOuterHeader: Size bytes + CompressedSize bytes]...
    /// </summary>
    private static List<ulong> ReadFrameTicks(string path)
    {
        var ticks = new List<ulong>();
        using var fs = File.OpenRead(path);
        using var br = new BinaryReader(fs);

        // Skip global header
        int globalHeaderSize = RecordingGlobalHeader.Size;
        br.ReadBytes(globalHeaderSize);

        // Read each FrameOuterHeader + skip payload
        int frameHeaderSize = FrameOuterHeader.Size; // 25 bytes
        while (fs.Position + frameHeaderSize <= fs.Length)
        {
            int   compressedSize   = br.ReadInt32();
            int   uncompressedSize = br.ReadInt32();
            ulong tick             = br.ReadUInt64();
            byte  frameType        = br.ReadByte();
            long  wallClockTicks   = br.ReadInt64();

            ticks.Add(tick);

            // Skip compressed payload
            if (compressedSize > 0)
                br.ReadBytes(compressedSize);
        }

        return ticks;
    }
}

// =============================================================================
// File-scoped helpers
// =============================================================================

/// <summary>Captures systems registered by modules (for extracting RecorderTickSystem in INT3).</summary>
file sealed class CapturingSystemRegistry : ISystemRegistry
{
    public readonly List<IEcsModuleSystem> Systems = new();

    public void RegisterSystem<T>(T system) where T : IEcsModuleSystem
        => Systems.Add(system);

    public IEcsModuleSystem RegisterManualSystem<T>(T system) where T : IEcsModuleSystem
    {
        Systems.Add(system);
        return system;
    }
}
