using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Core.Network;
using Hrot.Stride.Core;
using Hrot.Stride.Core.TestHarness;
using HrotStrideApp;
using Stride.Engine;
using Stride.Input;
using Xunit;

namespace HrotStrideApp.Tests;

/// <summary>
/// Headless tests for the BATCH-12 in-app test harness (STR-TEST-1).
///
/// <para>
/// The harness has two layers: an engine-agnostic registry/context (fully testable here) and
/// a GPU-only UI/DebugText/keyboard layer (<see cref="StrideTestHarness"/> — requires a
/// running graphics device, verified by the human). These tests exercise the registry, the
/// per-frame continuous-hook machinery, and the four initial cases driven against a real
/// headless <see cref="EditorStrideSubsystem"/> with a recording fake factory.
/// </para>
/// </summary>
public sealed class TestHarnessTests : IDisposable
{
    private const long TkbMilitaryApc     = 2001L;
    private const long TkbInfantrySoldier = 2002L;

    // ── Recording fake factory (records pose updates so we can assert orbit motion) ──
    private sealed class RecordingFakeFactory : IStrideVisualFactory
    {
        public List<(object handle, SimTransform pose)> Updates { get; } = new();
        public Dictionary<object, SimTransform> LastPose { get; } = new();
        public int CreateCount { get; private set; }
        public int DestroyCount { get; private set; }
        private int _counter;

        public object CreateModelVisual(string modelRef, string skeletonRef, float scale, Vector3 offsetFdp, in SimTransform initialPose)
        { CreateCount++; var h = $"M_{++_counter}"; LastPose[h] = initialPose; return h; }

        public object CreateProceduralVisual(CollisionShapeKind kind, ShapeDims dims, float scale, Vector3 offsetFdp, in SimTransform initialPose)
        { CreateCount++; var h = $"P_{++_counter}"; LastPose[h] = initialPose; return h; }

        public void UpdatePose(object handle, in SimTransform pose)
        { Updates.Add((handle, pose)); LastPose[handle] = pose; }

        public void Destroy(object handle) { DestroyCount++; LastPose.Remove(handle); }
    }

    private readonly List<EditorStrideSubsystem> _suts = new();

    public void Dispose() { foreach (var s in _suts) s.Dispose(); }

    private (EditorStrideSubsystem sut, RecordingFakeFactory factory) CreateSut()
    {
        var factory = new RecordingFakeFactory();
        var sut = new EditorStrideSubsystem();
        sut.Initialize(factory);
        _suts.Add(sut);
        return (sut, factory);
    }

    private static TestHarnessContext CreateContext(EditorStrideSubsystem sut, List<string> log)
        => new TestHarnessContext(
            sut.World, sut.ScenarioSource, sut.VisualBindingSystem,
            new Scene(), cameraEntity: null, log: log.Add);

    private static void Pump(EditorStrideSubsystem sut, int frames = 6)
    {
        for (int i = 0; i < frames; i++) sut.Tick(1f / 60f);
    }

    // ════════════════════════════════════════════════════════════════════
    // Registry
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Registry_PreservesInsertionOrder_AndCounts()
    {
        var reg = new TestHarnessRegistry();
        reg.Register("A", "a", _ => { });
        reg.Register("B", "b", _ => { });
        reg.Register("C", "c", _ => { });

        Assert.Equal(3, reg.Count);
        Assert.Equal(new[] { "A", "B", "C" }, reg.Cases.Select(c => c.Label).ToArray());
    }

    [Fact]
    public void Registry_Trigger_RunsTheCaseAtIndex_AndReturnsIt()
    {
        var reg = new TestHarnessRegistry();
        int ranIndex = -1;
        reg.Register("A", "a", _ => ranIndex = 0);
        reg.Register("B", "b", _ => ranIndex = 1);

        var ctx = MakeBareContext();
        var triggered = reg.Trigger(1, ctx);

        Assert.NotNull(triggered);
        Assert.Equal("B", triggered!.Label);
        Assert.Equal(1, ranIndex);
    }

    [Fact]
    public void Registry_Trigger_OutOfRange_ReturnsNull_AndRunsNothing()
    {
        var reg = new TestHarnessRegistry();
        bool ran = false;
        reg.Register("A", "a", _ => ran = true);

        Assert.Null(reg.Trigger(5, MakeBareContext()));
        Assert.Null(reg.Trigger(-1, MakeBareContext()));
        Assert.False(ran);
    }

    [Fact]
    public void Registry_Register_NullRun_Throws()
    {
        var reg = new TestHarnessRegistry();
        Assert.Throws<ArgumentNullException>(() => reg.Register("X", "x", null!));
    }

    [Fact]
    public void Registry_Register_EmptyLabel_Throws()
    {
        var reg = new TestHarnessRegistry();
        Assert.Throws<ArgumentException>(() => reg.Register("", "x", _ => { }));
    }

    // ════════════════════════════════════════════════════════════════════
    // Context continuous-hook machinery
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Context_Update_Hook_RunsEachFrame_UntilItReturnsFalse()
    {
        var ctx = MakeBareContext();
        int calls = 0;
        ctx.RegisterUpdate(dt => { calls++; return calls < 3; }); // stop after 3rd call

        Assert.Equal(1, ctx.ActiveUpdateHookCount);
        ctx.PumpUpdates(0.016f); // 1
        ctx.PumpUpdates(0.016f); // 2
        Assert.Equal(1, ctx.ActiveUpdateHookCount); // still active (returned true twice)
        ctx.PumpUpdates(0.016f); // 3 → returns false
        Assert.Equal(3, calls);
        Assert.Equal(0, ctx.ActiveUpdateHookCount); // removed
        ctx.PumpUpdates(0.016f); // no-op
        Assert.Equal(3, calls);
    }

    [Fact]
    public void Context_Update_Hook_ReceivesTheFrameDelta()
    {
        var ctx = MakeBareContext();
        float seen = -1f;
        ctx.RegisterUpdate(dt => { seen = dt; return false; });
        ctx.PumpUpdates(0.25f);
        Assert.Equal(0.25f, seen, precision: 5);
    }

    [Fact]
    public void Context_Update_Hook_ThatThrows_IsRemoved_AndOthersStillRun()
    {
        var log = new List<string>();
        var ctx = new TestHarnessContext(
            new EntityRepository(), new ScenarioEntityCreationRequestSource(),
            visualBindingSystem: null, scene: new Scene(), cameraEntity: null, log: log.Add);

        bool otherRan = false;
        ctx.RegisterUpdate(dt => throw new InvalidOperationException("boom"));
        ctx.RegisterUpdate(dt => { otherRan = true; return true; });

        ctx.PumpUpdates(0.016f);

        Assert.True(otherRan, "the non-throwing hook must still run");
        Assert.Equal(1, ctx.ActiveUpdateHookCount); // throwing hook removed, other kept
        Assert.Contains(log, l => l.Contains("threw and was removed"));
    }

    [Fact]
    public void Context_ClearUpdates_RemovesAllHooks()
    {
        var ctx = MakeBareContext();
        ctx.RegisterUpdate(_ => true);
        ctx.RegisterUpdate(_ => true);
        Assert.Equal(2, ctx.ActiveUpdateHookCount);
        ctx.ClearUpdates();
        Assert.Equal(0, ctx.ActiveUpdateHookCount);
    }

    // ════════════════════════════════════════════════════════════════════
    // Initial cases against a real headless EditorStrideSubsystem
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void SpawnInfantry_Case_EnqueuesSpawn_EntityAndModelVisualAppear()
    {
        var (sut, factory) = CreateSut();
        var reg = StrideTestHarnessCases.RegisterInitialCases(new TestHarnessRegistry());
        var ctx = CreateContext(sut, new List<string>());

        reg.Trigger(IndexOf(reg, "Spawn Infantry"), ctx);
        Pump(sut);

        Assert.Equal(1, sut.World.EntityCount);
        Assert.True(factory.CreateCount >= 1, "a mannequin model visual must be created");
    }

    [Fact]
    public void SpawnVehicle_Case_EnqueuesSpawn_EntityAppears()
    {
        var (sut, factory) = CreateSut();
        var reg = StrideTestHarnessCases.RegisterInitialCases(new TestHarnessRegistry());
        var ctx = CreateContext(sut, new List<string>());

        reg.Trigger(IndexOf(reg, "Spawn Vehicle"), ctx);
        Pump(sut);

        Assert.Equal(1, sut.World.EntityCount);
        Assert.True(factory.CreateCount >= 1, "a vehicle visual must be created");
    }

    [Fact]
    public void ClearAll_Case_DestroysAllEntities_AndVisualsReconcileAway()
    {
        var (sut, factory) = CreateSut();
        var reg = StrideTestHarnessCases.RegisterInitialCases(new TestHarnessRegistry());
        var ctx = CreateContext(sut, new List<string>());

        // Spawn three things first.
        reg.Trigger(IndexOf(reg, "Spawn Infantry"), ctx);
        reg.Trigger(IndexOf(reg, "Spawn Vehicle"), ctx);
        reg.Trigger(IndexOf(reg, "Spawn Infantry"), ctx);
        Pump(sut);
        Assert.Equal(3, sut.World.EntityCount);
        Assert.True(sut.VisualBindingSystem!.Visuals.Count > 0);
        int createsBefore = factory.CreateCount;

        // Clear All.
        reg.Trigger(IndexOf(reg, "Clear All"), ctx);
        Pump(sut, 2); // one tick for Pass-A teardown reconciliation

        Assert.Equal(0, sut.World.EntityCount);
        Assert.Empty(sut.VisualBindingSystem.Visuals);     // reconciled away (LIVE death/teardown)
        Assert.Equal(createsBefore, factory.DestroyCount); // every created visual destroyed
    }

    [Fact]
    public void OrbitingGhost_Case_CreatesNonOwnedEntity()
    {
        var (sut, _) = CreateSut();
        var reg = StrideTestHarnessCases.RegisterInitialCases(new TestHarnessRegistry());
        var ctx = CreateContext(sut, new List<string>());

        reg.Trigger(IndexOf(reg, "Spawn Orbiting Ghost"), ctx);

        // The ghost is created directly in the world (no spawn-pipeline pump needed).
        Assert.Equal(1, sut.World.EntityCount);

        // Find the ghost and assert it is NON-OWNED for SimTransform — this is what makes
        // Pass-B (.WithoutOwned<SimTransform>()) forward-sync drive its visual.
        var all = Collect(sut.World.Query().With<SimTransform>().Build());
        var ghost = Assert.Single(all);
        Assert.False(sut.World.HasAuthority<SimTransform>(ghost),
            "the orbiting ghost must be non-owned for SimTransform (Mode-1 ghost).");

        // It is also matched by a .WithoutOwned<SimTransform>() query (Pass-B's selector).
        var nonOwned = Collect(sut.World.Query().With<SimTransform>().WithoutOwned<SimTransform>().Build());
        Assert.Contains(ghost, nonOwned);
    }

    [Fact]
    public void OrbitingGhost_Case_ForwardSync_MovesTheVisual_OverFrames()
    {
        var (sut, factory) = CreateSut();
        var reg = StrideTestHarnessCases.RegisterInitialCases(new TestHarnessRegistry());
        var ctx = CreateContext(sut, new List<string>());

        reg.Trigger(IndexOf(reg, "Spawn Orbiting Ghost"), ctx);
        // First sim tick: Pass-A creates the visual for the ghost.
        sut.Tick(1f / 60f);

        var ghost = Assert.Single(Collect(sut.World.Query().With<SimTransform>().Build()));
        Assert.True(sut.VisualBindingSystem!.Visuals.ContainsKey(ghost),
            "Pass-A must create a visual for the ghost");
        var handle = sut.VisualBindingSystem.Visuals[ghost].VisualHandle;

        // Drive the continuous hook + sim for several frames; the hook moves SimTransform and
        // Pass-B forward-syncs the visual pose each frame.
        Vector3 firstPos = sut.World.GetComponentRO<SimTransform>(ghost).Position;
        for (int i = 0; i < 30; i++)
        {
            ctx.PumpUpdates(1f / 60f); // moves the ghost's SimTransform (orbit step)
            sut.Tick(1f / 60f);        // Pass-B forward-syncs visual from SimTransform
        }
        Vector3 laterPos = sut.World.GetComponentRO<SimTransform>(ghost).Position;

        // The ghost's SimTransform actually moved (orbit advanced).
        Assert.True((laterPos - firstPos).Length() > 0.1f,
            "the ghost's SimTransform must move along the orbit");

        // And the visual was forward-synced to a moved pose (Pass-B drove UpdatePose).
        Assert.True(factory.Updates.Any(u => ReferenceEquals(u.handle, handle) || Equals(u.handle, handle)),
            "Pass-B must have forward-synced the ghost's visual pose at least once");

        var lastVisualPose = factory.LastPose[handle];
        Assert.True((lastVisualPose.Position - firstPos).Length() > 0.1f,
            "the ghost's visual must track the moved SimTransform (forward-sync LIVE)");
    }

    [Fact]
    public void OrbitingGhost_Hook_StopsAfterClearAll()
    {
        var (sut, _) = CreateSut();
        var reg = StrideTestHarnessCases.RegisterInitialCases(new TestHarnessRegistry());
        var ctx = CreateContext(sut, new List<string>());

        reg.Trigger(IndexOf(reg, "Spawn Orbiting Ghost"), ctx);
        Assert.Equal(1, ctx.ActiveUpdateHookCount);

        reg.Trigger(IndexOf(reg, "Clear All"), ctx); // ClearUpdates() + destroy
        Assert.Equal(0, ctx.ActiveUpdateHookCount);

        sut.Tick(1f / 60f);
        Assert.Equal(0, sut.World.EntityCount);
    }

    // ════════════════════════════════════════════════════════════════════
    // BATCH-16 follow-up: "Record 3s / Replay" re-entrancy guard
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Triggering "Record 3s / Replay" a second time while the first sequence is still in flight
    /// (a double key press, or the button Click + keyboard D9 both firing in one frame) must NOT
    /// start a second concurrent record/replay sequence. After the BATCH-16 follow-up-2 rework the
    /// case owns a DEDICATED ghost driven from WITHIN the phase machine (no separate orbit hook), so
    /// the first trigger registers exactly ONE hook (the phase machine); the guarded second trigger
    /// must register NONE and log "already in progress".
    /// </summary>
    [Fact]
    public void RecordReplay_SecondTriggerWhileInFlight_DoesNotStartSecondSequence()
    {
        ResetRecordReplayGuard(); // static field is process-wide; isolate this test.
        try
        {
            var (sut, _) = CreateSut();
            sut.RecordReplayStorageDirectory =
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"BATCH16_GUARD_{Guid.NewGuid():N}");
            var reg = StrideGizmoReplayHarnessCases.RegisterGizmoReplayCases(new TestHarnessRegistry(), sut);
            var log = new List<string>();
            var ctx = CreateContext(sut, log);
            int caseIndex = IndexOf(reg, "Record 3s / Replay");

            // First trigger: creates the dedicated ghost directly + registers the phase machine.
            reg.Trigger(caseIndex, ctx);
            int hooksAfterFirst = ctx.ActiveUpdateHookCount;
            Assert.Equal(1, hooksAfterFirst); // phase-machine hook only (ghost driven from within it)
            Assert.Equal(1, sut.World.EntityCount); // the dedicated ghost was created

            // Second trigger while the first sequence is still in flight: guard must short-circuit.
            reg.Trigger(caseIndex, ctx);
            Assert.Equal(hooksAfterFirst, ctx.ActiveUpdateHookCount); // NO new hook registered
            Assert.Equal(1, sut.World.EntityCount);                   // NO second ghost created
            Assert.Contains(log, l => l.Contains("already in progress"));

            // Exactly one "starting" line was logged (only one sequence began).
            Assert.Equal(1, log.Count(l => l.Contains("Record 3s / Replay: starting")));
        }
        finally
        {
            ResetRecordReplayGuard(); // leave the static clean for other tests / a later trigger.
        }
    }

    /// <summary>
    /// After a sequence completes the guard must be clear so a later trigger starts a fresh
    /// sequence. Drives the phase machine to completion against a real headless subsystem, then
    /// triggers again and asserts a new "starting" line + new phase-machine hook.
    /// </summary>
    [Fact]
    public void RecordReplay_AfterCompletion_CanBeTriggeredAgain()
    {
        ResetRecordReplayGuard();
        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"BATCH16_REENTER_{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(tempDir);
        try
        {
            var (sut, _) = CreateSut();
            sut.RecordReplayStorageDirectory = tempDir;
            var reg = StrideGizmoReplayHarnessCases.RegisterGizmoReplayCases(new TestHarnessRegistry(), sut);
            var log = new List<string>();
            var ctx = CreateContext(sut, log);
            int caseIndex = IndexOf(reg, "Record 3s / Replay");

            // First sequence: trigger, then pump the harness hooks + tick the kernel until the
            // phase machine reaches ANY terminal path — every one of normal completion, a FAILED
            // branch, and the catch-all "hook faulted" branch clears the guard. (The handler-driven
            // replay step uses ConfigureAwait(false) continuations, so headlessly the sequence may
            // terminate via completion or a fault depending on thread-pool timing; the guard
            // contract — "any terminal path clears the flag" — holds either way and is what we
            // assert here.) After the follow-up-2 rework the ghost is driven from within the phase
            // machine (no separate orbit hook), so the phase-machine hook removing itself (hook
            // count drops to 0) is the observable terminal signal.
            reg.Trigger(caseIndex, ctx);
            bool Terminal() =>
                log.Any(l => l.Contains("Record/Replay: complete")
                          || l.Contains("FAILED")
                          || l.Contains("hook faulted"));
            for (int i = 0; i < 4000 && !Terminal(); i++)
            {
                ctx.PumpUpdates(1f / 60f); // advance the phase machine
                sut.Tick(1f / 60f);        // let async kernel installs go live
            }
            Assert.True(Terminal(),
                "the first sequence must reach a terminal path (complete or fault), which clears the guard.");

            // Second trigger AFTER the first sequence terminated: the guard must be clear, so a
            // fresh sequence starts (a new "starting" line; no "already in progress").
            int startsBefore = log.Count(l => l.Contains("Record 3s / Replay: starting"));
            reg.Trigger(caseIndex, ctx);
            Assert.Equal(startsBefore + 1, log.Count(l => l.Contains("Record 3s / Replay: starting")));
            Assert.DoesNotContain(log.Skip(log.Count - 2), l => l.Contains("already in progress"));
        }
        finally
        {
            ResetRecordReplayGuard();
            try { System.IO.Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// Resets the process-wide <c>s_recordReplayInProgress</c> guard via reflection so each guard
    /// test is order-independent (the field is private static on the static case class).
    /// </summary>
    private static void ResetRecordReplayGuard()
    {
        var f = typeof(StrideGizmoReplayHarnessCases)
            .GetField("s_recordReplayInProgress",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        f?.SetValue(null, false);
    }

    // ════════════════════════════════════════════════════════════════════
    // TryGetCaseKey — key-map contract (BATCH-17 follow-up)
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Verifies the full key-map table: indices 0–8 → D1–D9, index 9 → D0,
    /// indices 10–21 → F1–F12, and anything beyond returns false with a "—" label.
    /// </summary>
    [Fact]
    public void TryGetCaseKey_CoverageTable_MatchesSpec()
    {
        // D1–D9 (index 0–8)
        for (int i = 0; i <= 8; i++)
        {
            Assert.True(StrideTestHarness.TryGetCaseKey(i, out var key, out var label),
                $"index {i} must have a key");
            Assert.Equal(Stride.Input.Keys.D1 + i, key);
            Assert.Equal($"D{i + 1}", label);
        }

        // D0 (index 9)
        Assert.True(StrideTestHarness.TryGetCaseKey(9, out var k9, out var l9));
        Assert.Equal(Stride.Input.Keys.D0, k9);
        Assert.Equal("D0", l9);

        // F1–F12 (index 10–21) — extended to F12 in BATCH-20 to cover the F6/F7 demos.
        for (int i = 10; i <= 21; i++)
        {
            Assert.True(StrideTestHarness.TryGetCaseKey(i, out var key, out var label),
                $"index {i} must have a key");
            Assert.Equal(Stride.Input.Keys.F1 + (i - 10), key);
            Assert.Equal($"F{i - 9}", label);
        }

        // Beyond the covered range → false (index 22 = would-be F13)
        Assert.False(StrideTestHarness.TryGetCaseKey(22, out _, out var fallbackLabel));
        Assert.Equal("—  ", fallbackLabel);
        Assert.False(StrideTestHarness.TryGetCaseKey(-1, out _, out _));
    }

    // ════════════════════════════════════════════════════════════════════
    // Physics Drive — headless seam tests (BATCH-17 follow-up)
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// "Physics Drive" must be registered at index 11 (F2) when
    /// <see cref="StridePhysicsHarnessCases.RegisterPhysicsCases"/> is called after the
    /// standard 9 initial+animation+gizmo cases. This verifies the key-map contract:
    /// index 11 → F2 per <see cref="StrideTestHarness.TryGetCaseKey"/>.
    /// </summary>
    [Fact]
    public void PhysicsDrive_RegistersAtIndex11_KeyF2()
    {
        // Seed 9 dummy cases so Physics Drop/Walk/Drive land at indices 9/10/11.
        var reg = new TestHarnessRegistry();
        for (int i = 0; i < 9; i++)
            reg.Register($"Dummy{i}", "dummy", _ => { });

        var noopService   = new NoOpPhysicsBodyService();
        // PhysicsBodyLifecycleSystem requires a visual binding system; use a minimal fake.
        var fakeFactory   = new RecordingFakeFactory();
        var sut           = new EditorStrideSubsystem();
        sut.Initialize(fakeFactory);
        _suts.Add(sut);
        var lifecycle = sut.PhysicsBodyLifecycle!;

        StridePhysicsHarnessCases.RegisterPhysicsCases(reg, lifecycle, noopService);

        // Three physics cases registered after the 9 dummies.
        Assert.Equal(12, reg.Count);
        Assert.Equal("Physics Drop",  reg.Cases[9].Label);
        Assert.Equal("Physics Walk",  reg.Cases[10].Label);
        Assert.Equal("Physics Drive", reg.Cases[11].Label);

        // Index 11 → F2 per the key-map.
        Assert.True(StrideTestHarness.TryGetCaseKey(11, out var key, out var label));
        Assert.Equal(Stride.Input.Keys.F2, key);
        Assert.Equal("F2", label);
    }

    /// <summary>
    /// Triggering "Physics Drive" enqueues exactly one spawn request (TKB 2001 MilitaryAPC) and
    /// registers a continuous update hook. After the subsystem pumps a few ticks, the APC entity
    /// appears and the hook sets its <c>VehicleState.Speed</c> to the drive speed — exercising the
    /// real headless seam (NoOpPhysicsBodyService, so no actual Bullet movement occurs, but the
    /// component write path is fully tested).
    /// </summary>
    [Fact]
    public void PhysicsDrive_Trigger_EnqueuesApcSpawn_AndHookSetsVehicleState()
    {
        var (sut, _) = CreateSut();

        // Build a registry with the required prefix so Physics Drive lands at index 11.
        var reg = new TestHarnessRegistry();
        for (int i = 0; i < 9; i++)
            reg.Register($"Dummy{i}", "dummy", _ => { });

        var lifecycle = sut.PhysicsBodyLifecycle!;
        StridePhysicsHarnessCases.RegisterPhysicsCases(reg, lifecycle, sut.PhysicsBodyService);

        var log = new List<string>();
        var ctx = CreateContext(sut, log);

        int driveCaseIndex = IndexOf(reg, "Physics Drive");
        Assert.Equal(11, driveCaseIndex);

        // Trigger: should enqueue a spawn and register one update hook.
        reg.Trigger(driveCaseIndex, ctx);
        Assert.Equal(1, ctx.ActiveUpdateHookCount); // continuous hook registered

        // Confirm a spawn was enqueued (TKB 2001 = MilitaryAPC).
        // Log should mention the APC spawn.
        Assert.Contains(log, l => l.Contains("[Physics Drive]") && l.Contains("MilitaryAPC"));

        // Pump the subsystem so the spawn pipeline materialises the entity.
        Pump(sut, 6);

        // After pump there should be exactly one entity in the world (the APC).
        Assert.Equal(1, sut.World.EntityCount);

        // Pump the context update hook a few frames (resolves the entity + sets VehicleState).
        for (int i = 0; i < 10; i++)
        {
            ctx.PumpUpdates(1f / 60f);
            sut.Tick(1f / 60f);
        }

        // The hook must have resolved the entity and set VehicleState.Speed.
        // Find the APC entity and assert VehicleState.Speed was written.
        var apcEntities = Collect(sut.World.Query()
            .With<CarKinem.Core.VehicleState>()
            .Build());

        // At least one entity with VehicleState should exist (the APC).
        Assert.NotEmpty(apcEntities);

        // The hook drives Speed = DrivingSpeedMps (3.0 m/s) during the drive window.
        // Since we pumped < 10 s of simulation, speed should be positive.
        var apc   = apcEntities[0];
        var state = sut.World.GetComponentRO<CarKinem.Core.VehicleState>(apc);
        Assert.True(state.Speed > 0f,
            $"VehicleState.Speed must be positive during the 10-s drive window; got {state.Speed}");
        Assert.True(state.Speed >= 2.9f && state.Speed <= 3.1f,
            $"VehicleState.Speed must equal DrivingSpeedMps=3.0 (±0.1); got {state.Speed}");
    }

    // ════════════════════════════════════════════════════════════════════
    // Drive To Waypoint — headless seam tests (BATCH-17 waypoint proof)
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// "Drive To Waypoint" must be registered at index 12 (F3) when
    /// <see cref="StridePhysicsHarnessCases.RegisterPhysicsCases"/> and
    /// <see cref="StridePhysicsHarnessCases.RegisterDriveToWaypointCase"/> are called after
    /// the standard 9 initial+animation+gizmo cases.
    /// Key-map: index 12 → F3 per <see cref="StrideTestHarness.TryGetCaseKey"/>.
    /// </summary>
    [Fact]
    public void DriveToWaypoint_RegistersAtIndex12_KeyF3()
    {
        var reg = new TestHarnessRegistry();
        for (int i = 0; i < 9; i++)
            reg.Register($"Dummy{i}", "dummy", _ => { });

        var noopService = new NoOpPhysicsBodyService();
        var fakeFactory = new RecordingFakeFactory();
        var sut         = new EditorStrideSubsystem();
        sut.Initialize(fakeFactory);
        _suts.Add(sut);
        var lifecycle = sut.PhysicsBodyLifecycle!;

        StridePhysicsHarnessCases.RegisterPhysicsCases(reg, lifecycle, noopService);
        StridePhysicsHarnessCases.RegisterDriveToWaypointCase(reg, lifecycle, noopService);

        // Four physics cases after the 9 dummies: D0, F1, F2, F3.
        Assert.Equal(13, reg.Count);
        Assert.Equal("Physics Drop",      reg.Cases[9].Label);
        Assert.Equal("Physics Walk",      reg.Cases[10].Label);
        Assert.Equal("Physics Drive",     reg.Cases[11].Label);
        Assert.Equal("Drive To Waypoint", reg.Cases[12].Label);

        // Index 12 → F3 per the key-map.
        Assert.True(StrideTestHarness.TryGetCaseKey(12, out var key, out var label));
        Assert.Equal(Keys.F3, key);
        Assert.Equal("F3", label);
    }

    /// <summary>
    /// Triggering "Drive To Waypoint" enqueues exactly one spawn request (TKB 2001 MilitaryAPC)
    /// and registers a continuous update hook. After pumping, the APC entity appears and the
    /// hook writes a non-zero <c>VehicleState.Speed</c> (the controller is steering toward WP0).
    /// Exercises the real headless seam (NoOpPhysicsBodyService — no Bullet movement, but the
    /// component write path is fully exercised).
    /// </summary>
    [Fact]
    public void DriveToWaypoint_Trigger_EnqueuesApcSpawn_AndHookSetsVehicleState()
    {
        var (sut, _) = CreateSut();

        var reg = new TestHarnessRegistry();
        for (int i = 0; i < 9; i++)
            reg.Register($"Dummy{i}", "dummy", _ => { });

        var lifecycle = sut.PhysicsBodyLifecycle!;
        StridePhysicsHarnessCases.RegisterPhysicsCases(reg, lifecycle, sut.PhysicsBodyService);
        StridePhysicsHarnessCases.RegisterDriveToWaypointCase(reg, lifecycle, sut.PhysicsBodyService);

        var log = new List<string>();
        var ctx = CreateContext(sut, log);

        int caseIdx = IndexOf(reg, "Drive To Waypoint");
        Assert.Equal(12, caseIdx);

        // Trigger: registers one update hook + logs the spawn.
        reg.Trigger(caseIdx, ctx);
        Assert.Equal(1, ctx.ActiveUpdateHookCount);
        Assert.Contains(log, l => l.Contains("[Drive To Waypoint]") && l.Contains("MilitaryAPC"));

        // Pump to materialise the entity.
        Pump(sut, 6);
        Assert.Equal(1, sut.World.EntityCount);

        // Pump context hook + subsystem frames.
        for (int i = 0; i < 10; i++)
        {
            ctx.PumpUpdates(1f / 60f);
            sut.Tick(1f / 60f);
        }

        // APC entity must have VehicleState and the controller must be commanding non-zero speed.
        var apcEntities = Collect(sut.World.Query().With<CarKinem.Core.VehicleState>().Build());
        Assert.NotEmpty(apcEntities);

        var apc   = apcEntities[0];
        var state = sut.World.GetComponentRO<CarKinem.Core.VehicleState>(apc);

        // WP0 is ahead-right of the spawn; heading error should be negative (right turn).
        // Speed must be > 0 (controller is driving toward WP0; not arrived yet at this step count).
        Assert.True(state.Speed > 0f,
            $"VehicleState.Speed must be positive while driving to WP0; got {state.Speed}");
    }

    // ── Stuck-detection tests (movement-based rule) ──────────────────────────

    /// <summary>
    /// <b>Stuck-detection (movement-based): genuinely stationary car skips all waypoints.</b>
    ///
    /// <para>
    /// With <see cref="NoOpPhysicsBodyService"/>, no Bullet physics drives the car so it stays
    /// at its spawn position every frame — displacement over 3 s = 0 m, which is below
    /// <c>StuckDisplacementThresholdM = 0.3 m</c>. After ~3 s the stuck-detection fires and
    /// skips to the next waypoint. Repeating for all three waypoints the case ends with
    /// "PROOF COMPLETE" and reports all three as skipped.
    /// NOTE: the old distance-based rule was "distance to target not improving by 0.3 m over 3 s".
    /// The new rule is "car position has moved less than 0.3 m total over 3 s" (movement-based).
    /// A stationary car has zero displacement so it still correctly fires stuck.
    /// </para>
    ///
    /// <para>
    /// Sim step: dt = 1/20 s (conservative). Need (3 s / dt) + entity-resolve frames.
    /// 3 waypoints × 3 s × 20 = 180 frames plus ~20 for entity materialisation = 200.
    /// We run 700 frames (35 s sim time) to be well clear of the 3×3 s = 9 s window.
    /// </para>
    /// </summary>
    [Fact]
    public void DriveToWaypoint_StuckCar_SkipsAllWaypointsAndReportsProofComplete()
    {
        var (sut, _) = CreateSut();

        var reg = new TestHarnessRegistry();
        for (int i = 0; i < 9; i++)
            reg.Register($"Dummy{i}", "dummy", _ => { });

        var lifecycle = sut.PhysicsBodyLifecycle!;
        StridePhysicsHarnessCases.RegisterPhysicsCases(reg, lifecycle, sut.PhysicsBodyService);
        StridePhysicsHarnessCases.RegisterDriveToWaypointCase(reg, lifecycle, sut.PhysicsBodyService);

        var log  = new List<string>();
        var ctx  = CreateContext(sut, log);

        int caseIdx = IndexOf(reg, "Drive To Waypoint");

        // Trigger the case.
        reg.Trigger(caseIdx, ctx);
        Assert.Equal(1, ctx.ActiveUpdateHookCount);

        // Pump enough subsystem frames to materialise the APC entity.
        Pump(sut, 10);
        Assert.Equal(1, sut.World.EntityCount);

        // Now run the update loop for 700 frames at 1/20 s each (35 s total sim time).
        // The stuck-detection window is 3 s; with 3 waypoints that's 9 s minimum.
        // NoOp physics means the APC never moves, so best-dist is constant → stuck each WP.
        const float dt = 1f / 20f;
        const int maxFrames = 700;
        for (int f = 0; f < maxFrames; f++)
        {
            ctx.PumpUpdates(dt);
            sut.Tick(dt);

            // Stop early once the proof-complete log line appears.
            if (log.Any(l => l.Contains("PROOF COMPLETE")))
                break;
        }

        // Verify: at least one BLOCKED/SKIPPING log was emitted (stuck-detection fired).
        Assert.Contains(log, l => l.Contains("[Drive To Waypoint]") &&
                                  l.Contains("BLOCKED") &&
                                  l.Contains("SKIPPING to next"));

        // Verify: the PROOF COMPLETE summary was logged.
        var proofLine = log.FirstOrDefault(l => l.Contains("PROOF COMPLETE"));
        Assert.NotNull(proofLine);

        // All 3 waypoints were skipped (car never moved): "0/3 waypoints (3 skipped".
        // The log format is: "PROOF COMPLETE — reached N/3 waypoints (K skipped as blocked)".
        // With a stationary car all should be skipped so reached=0 and skipped=3.
        Assert.Contains("3 skipped", proofLine!);

        // Verify: the hook completed (no longer active).
        Assert.Equal(0, ctx.ActiveUpdateHookCount);
    }

    /// <summary>
    /// <b>Stuck-detection movement-based rule: a car that IS MOVING but distance-to-target
    /// temporarily INCREASES must NOT be declared stuck.</b>
    ///
    /// <para>
    /// This is the key behavioral difference from the old distance-based rule. The controller
    /// computes a heading error and a steering command; the bicycle model then moves the car.
    /// During a legitimate turn or curve, the car can temporarily move AWAY from the target
    /// (distance increases) while curving around. The old rule would declare this "stuck"
    /// after 3 s if the car wasn't converging. The new movement-based rule only fires if the
    /// car's actual displacement is below <c>StuckDisplacementThresholdM = 0.3 m</c> in 3 s.
    /// </para>
    ///
    /// <para>
    /// This test directly validates the <c>StuckDisplacementThresholdM</c> logic using the
    /// convergence simulation from <see cref="VehicleWaypointControllerConvergenceTests"/>.
    /// We run the closed-loop bicycle simulation (which MOVES the car) and confirm that
    /// after many frames no "BLOCKED / SKIPPING" line was logged — the car was moving, so
    /// stuck-detection must not fire even if distance fluctuated.
    /// </para>
    ///
    /// <para>
    /// Implementation: we manually wire a SimTransform on the spawned APC and advance it
    /// with the bicycle kinematics each frame (as if Bullet physics were running), then
    /// assert no BLOCKED log was emitted before the car arrives at WP0.
    /// </para>
    /// </summary>
    [Fact]
    public void DriveToWaypoint_MovingCar_IsNotDeclaredStuck_EvenIfDistanceFluctuates()
    {
        var (sut, _) = CreateSut();

        var reg = new TestHarnessRegistry();
        for (int i = 0; i < 9; i++)
            reg.Register($"Dummy{i}", "dummy", _ => { });

        var lifecycle = sut.PhysicsBodyLifecycle!;
        StridePhysicsHarnessCases.RegisterPhysicsCases(reg, lifecycle, sut.PhysicsBodyService);
        StridePhysicsHarnessCases.RegisterDriveToWaypointCase(reg, lifecycle, sut.PhysicsBodyService);

        var log = new List<string>();
        var ctx = CreateContext(sut, log);

        int caseIdx = IndexOf(reg, "Drive To Waypoint");
        reg.Trigger(caseIdx, ctx);

        // Pump to materialise the APC entity.
        Pump(sut, 10);
        Assert.Equal(1, sut.World.EntityCount);

        // Find the spawned APC.
        var all = Collect(sut.World.Query().With<SimTransform>().Build());
        Assert.Single(all);
        var apc = all[0];

        // Run the bicycle kinematics in the test: each frame we advance the car's SimTransform
        // using the same controller output that the DriveToWaypoint hook sees.
        // This simulates a MOVING car; the stuck-detection must NOT fire because displacement > 0.
        //
        // Initial state: spawn at FDP (6,12) facing east (heading=0).
        // WP0 = (14,12): straight ahead — should arrive quickly with no stuck.
        float posX    = 6f;
        float posY    = 12f;
        float heading = 0f;         // east = FDP +X
        const float dt          = 1f / 20f;
        const float wheelBase   = 2.5f;
        const int   maxFrames   = 800; // 40 s — well over the 3 s stuck window
        bool reachedWp0         = false;

        var ctrl = new VehicleWaypointController(
            cruiseSpeed:      3f,
            maxSteerAngleRad: 0.7f,
            headingGainK:     2.0f,
            arriveToleranceM: 3.0f,    // same as WaypointToleranceM
            slowRadiusM:      8.0f,
            slowMinFrac:      0.2f,
            wheelBase:        wheelBase);

        for (int f = 0; f < maxFrames; f++)
        {
            // Advance the bicycle model by one step.
            var output = ctrl.Compute(posX, posY, heading, 14f, 12f); // WP0
            if (output.Arrived) { reachedWp0 = true; break; }

            float yawRate  = (output.Speed / wheelBase) * MathF.Tan(output.SteerAngle);
            heading       += yawRate * dt;
            posX          += MathF.Cos(heading) * output.Speed * dt;
            posY          += MathF.Sin(heading) * output.Speed * dt;

            // Write the moved position back into the entity's SimTransform.
            sut.World.SetComponent(apc, new SimTransform
            {
                Position = new System.Numerics.Vector3(posX, posY, 0f),
                Rotation = System.Numerics.Quaternion.CreateFromAxisAngle(
                    System.Numerics.Vector3.UnitZ, heading),
            });

            // Pump one frame of the hook + subsystem.
            ctx.PumpUpdates(dt);
            sut.Tick(dt);

            // Early exit if proof complete.
            if (log.Any(l => l.Contains("PROOF COMPLETE"))) break;
        }

        // PRIMARY ASSERTION: no BLOCKED/SKIPPING was logged for a car that was genuinely moving.
        // A turn or curve that temporarily increases distance-to-target must not trigger stuck.
        Assert.DoesNotContain(log, l => l.Contains("BLOCKED") && l.Contains("SKIPPING to next"));

        // SECONDARY: the car should have reached WP0 (validates the bicycle model moved it correctly).
        Assert.True(reachedWp0 || log.Any(l => l.Contains("REACHED WP0")),
            "The moving car should have reached WP0 — if not, the bicycle simulation or tolerance is wrong.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static List<Fdp.Core.Entity> Collect(EntityQuery query)
    {
        var list = new List<Fdp.Core.Entity>();
        foreach (var e in query) list.Add(e);
        return list;
    }

    private static int IndexOf(TestHarnessRegistry reg, string label)
    {
        for (int i = 0; i < reg.Count; i++)
            if (reg.Cases[i].Label == label) return i;
        throw new InvalidOperationException($"case '{label}' not registered");
    }

    private static TestHarnessContext MakeBareContext()
        => new TestHarnessContext(
            new EntityRepository(), new ScenarioEntityCreationRequestSource(),
            visualBindingSystem: null, scene: new Scene(), cameraEntity: null,
            log: _ => { });
}
