using System;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.ReplayBrowser.Search;
using Hrot.Blueprints.Core.Debug;
using Hrot.Diagnostics.Breakpoints;
using StructEdit.Reflection;

namespace Hrot.Diagnostics.Breakpoints.Tests;

// ---- Test-only components and events ------------------------------------

/// <summary>Test component for snapshot and repo-state tests (ID 200).</summary>
[ComponentId(200)]
internal struct TestHealth { public int Current; }

/// <summary>Test component for predicate-compiler tests (ID 201).</summary>
[ComponentId(201)]
internal struct TestDamage { public float Value; }

/// <summary>Test event for event-scanner tests (ID 99201).</summary>
[EventId(99201)]
internal struct HitTestEvent { public float Damage; }

// ---- Shared test doubles ------------------------------------------------

internal sealed class MockDebugTimeController : IEngineDebugTimeController
{
    public bool IsPausedByDebugger { get; private set; }
    public int  PauseRequestCount  { get; private set; }
    public int  ResumeCount        { get; private set; }
    public int  StepRequestCount   { get; private set; }

    public void RequestPause()
    {
        PauseRequestCount++;
        IsPausedByDebugger = true;
    }

    public void RequestResume()
    {
        ResumeCount++;
        IsPausedByDebugger = false;
    }

    public void RequestStepOneTick()
    {
        StepRequestCount++;
        IsPausedByDebugger = false;
    }
}

// ---- Helper factory -----------------------------------------------------

internal static class ManagerFactory
{
    /// <summary>
    /// Creates a <see cref="DataBreakpointManager"/> with fresh repositories and real compilers.
    /// Also returns the snapshot provider and time controller for assertions.
    /// </summary>
    internal static (DataBreakpointManager manager,
                     EntityRepository liveRepo,
                     DebugSnapshotProvider snapshotProvider,
                     MockDebugTimeController timeController)
        Create()
    {
        var liveRepo         = new EntityRepository();
        var preTickSnapshot  = new EntityRepository();
        var tc               = new MockDebugTimeController();
        var snapshotProvider = new DebugSnapshotProvider(preTickSnapshot);
        var predicateCompiler    = new PredicateCompiler(new ComponentEditServiceBuilder().Build());
        var eventScannerCompiler = new EventScannerCompiler(new ComponentEditServiceBuilder().Build());
        var manager          = new DataBreakpointManager(
            liveRepo, preTickSnapshot, snapshotProvider, tc,
            predicateCompiler, eventScannerCompiler);
        return (manager, liveRepo, snapshotProvider, tc);
    }

    internal static Breakpoint MakeBreakpoint(bool enabled = true, int threshold = 1, string name = "BP") =>
        new Breakpoint
        {
            Id                 = BreakpointId.Invalid, // overwritten by Add
            Enabled            = enabled,
            OccurrenceThreshold = threshold,
            DisplayName        = name
        };
}

// =========================================================================
// UBP-P1T1: DebugSnapshotProvider tests
// =========================================================================

[Collection("ComponentRegistry")]
public sealed class DebugSnapshotProviderTests
{
    /// <summary>
    /// When the gate is off (default), Execute must return immediately without
    /// touching the snapshot repository — verified by inspecting the internal gate flag.
    /// </summary>
    [Fact]
    public void GateOff_DoesNoWork()
    {
        var snapshot = new EntityRepository();
        var provider = new DebugSnapshotProvider(snapshot);
        var live     = new EntityRepository();

        // Execute with gate off -- no exception, no work.
        provider.Execute(live, 0.016f);

        Assert.Equal(0, provider.IsEnabledRaw);
    }

    /// <summary>
    /// When the gate is on, Execute must synchronise the snapshot from the live repo.
    /// Verified by checking that the snapshot contains the entity and component value
    /// that were in the live repo before Execute was called.
    /// </summary>
    [Fact]
    public void GateOn_SyncsSnapshotFromLiveRepo()
    {
        ComponentTypeRegistry.Clear();
        var snapshot = new EntityRepository();
        var live     = new EntityRepository();

        live.RegisterComponent<TestHealth>();
        snapshot.RegisterComponent<TestHealth>();

        var entity = live.CreateEntity();
        live.AddComponent(entity, new TestHealth { Current = 42 });

        var provider = new DebugSnapshotProvider(snapshot);
        provider.SetEnabled(true);

        provider.Execute(live, 0f);

        Assert.True(snapshot.HasComponent<TestHealth>(entity));
        Assert.Equal(42, snapshot.GetComponent<TestHealth>(entity).Current);
    }

    /// <summary>
    /// SetEnabled(false) after SetEnabled(true) must lower the gate flag back to 0.
    /// </summary>
    [Fact]
    public void SetEnabled_Toggle_UpdatesGate()
    {
        var snapshot = new EntityRepository();
        var provider = new DebugSnapshotProvider(snapshot);

        provider.SetEnabled(true);
        Assert.Equal(1, provider.IsEnabledRaw);

        provider.SetEnabled(false);
        Assert.Equal(0, provider.IsEnabledRaw);
    }

    /// <summary>
    /// Execute with gate on but a non-EntityRepository view must throw
    /// <see cref="InvalidOperationException"/>.
    /// </summary>
    [Fact]
    public void Execute_NonEntityRepositoryView_Throws()
    {
        var snapshot = new EntityRepository();
        var provider = new DebugSnapshotProvider(snapshot);

        provider.SetEnabled(true);

        Assert.Throws<InvalidOperationException>(() =>
            provider.Execute(new StubSimulationView(), 0.016f));
    }

    /// <summary>
    /// Repeated calls to Execute with the gate off must not allocate any heap memory
    /// in the steady state.
    /// </summary>
    [Fact]
    public void GateOff_Execute_ZeroAllocations()
    {
        var snapshot = new EntityRepository();
        var provider = new DebugSnapshotProvider(snapshot);
        var live     = new EntityRepository();

        // JIT warmup.
        provider.Execute(live, 0f);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // Use per-thread counter so parallel test-runner threads don't skew the result.
        long before = GC.GetAllocatedBytesForCurrentThread();

        const int Iterations = 10_000;
        for (int i = 0; i < Iterations; i++)
            provider.Execute(live, 0f);

        long after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0L, after - before);
    }

    // Minimal ISimulationView stub for the error-case test above.
    private sealed class StubSimulationView : ISimulationView
    {
        public uint  Tick => 0;
        public float Time => 0f;
        public ref readonly T GetComponentRO<T>(Entity e) where T : unmanaged
            => throw new NotImplementedException();
        public T GetManagedComponentRO<T>(Entity e) where T : class
            => throw new NotImplementedException();
        public bool IsAlive(Entity e) => throw new NotImplementedException();
        public bool HasComponent<T>(Entity e) where T : unmanaged => throw new NotImplementedException();
        public bool HasManagedComponent<T>(Entity e) where T : class => throw new NotImplementedException();
        public System.Collections.Generic.IReadOnlyList<T> ReadManagedEvents<T>()
            => throw new NotImplementedException();
        public IEntityCommandBuffer GetCommandBuffer() => throw new NotImplementedException();
        public ReadOnlySpan<T> ReadEvents<T>() where T : unmanaged => throw new NotImplementedException();
        public QueryBuilder Query() => throw new NotImplementedException();
    }
}

// =========================================================================
// UBP-P1T2: Reference-counted gate tests (DataBreakpointManager)
// =========================================================================

public sealed class SnapshotGateTests
{
    /// <summary>
    /// Adding the first enabled breakpoint must open the snapshot gate (0 to 1).
    /// </summary>
    [Fact]
    public void FirstBreakpointEnabled_MountsSnapshotProvider()
    {
        var (manager, _, provider, _) = ManagerFactory.Create();

        Assert.Equal(0, provider.IsEnabledRaw); // gate off before add

        manager.Add(ManagerFactory.MakeBreakpoint(enabled: true));

        Assert.Equal(1, provider.IsEnabledRaw);
    }

    /// <summary>
    /// Removing the last enabled breakpoint must close the gate (1 to 0).
    /// </summary>
    [Fact]
    public void LastBreakpointRemoved_UnmountsSnapshotProvider()
    {
        var (manager, _, provider, _) = ManagerFactory.Create();

        var id = manager.Add(ManagerFactory.MakeBreakpoint(enabled: true));
        Assert.Equal(1, provider.IsEnabledRaw);

        manager.Remove(id);
        Assert.Equal(0, provider.IsEnabledRaw);
    }

    /// <summary>
    /// Disabling the last enabled breakpoint (without removing it) must close the gate.
    /// Re-enabling it must reopen it.
    /// </summary>
    [Fact]
    public void DisableThenReenable_GateTogglesCorrectly()
    {
        var (manager, _, provider, _) = ManagerFactory.Create();

        var id = manager.Add(ManagerFactory.MakeBreakpoint(enabled: true));
        Assert.Equal(1, provider.IsEnabledRaw);

        manager.SetEnabled(id, false);
        Assert.Equal(0, provider.IsEnabledRaw);

        manager.SetEnabled(id, true);
        Assert.Equal(1, provider.IsEnabledRaw);
    }

    /// <summary>
    /// With two enabled breakpoints, disabling one must keep the gate open
    /// because the reference count is still 1.
    /// </summary>
    [Fact]
    public void TwoBreakpoints_DisableOne_GateRemainsOpen()
    {
        var (manager, _, provider, _) = ManagerFactory.Create();

        var id1 = manager.Add(ManagerFactory.MakeBreakpoint(enabled: true, name: "BP1"));
        var id2 = manager.Add(ManagerFactory.MakeBreakpoint(enabled: true, name: "BP2"));
        Assert.Equal(1, provider.IsEnabledRaw);

        manager.SetEnabled(id1, false);
        Assert.Equal(1, provider.IsEnabledRaw); // still open

        manager.SetEnabled(id2, false);
        Assert.Equal(0, provider.IsEnabledRaw); // now closed
    }

    /// <summary>
    /// Adding a disabled breakpoint must not change the gate.
    /// </summary>
    [Fact]
    public void AddDisabledBreakpoint_GateRemainsOff()
    {
        var (manager, _, provider, _) = ManagerFactory.Create();

        manager.Add(ManagerFactory.MakeBreakpoint(enabled: false));

        Assert.Equal(0, provider.IsEnabledRaw);
    }
}

// =========================================================================
// UBP-P1T3: Triple-buffer pause primitives
// =========================================================================

[Collection("ComponentRegistry")]
public sealed class TripleBufferPauseTests
{
    /// <summary>
    /// OnHit must: capture post-tick snapshot from live repo, rewind live repo to
    /// pre-tick snapshot, request pause, and fire both events.
    /// Repository state is verified directly via internal test seam properties.
    /// </summary>
    [Fact]
    public void OnHit_PerformsTripleBufferRewind_AndStateIsCorrect()
    {
        ComponentTypeRegistry.Clear();
        var liveRepo        = new EntityRepository();
        var preTickSnapshot = new EntityRepository();
        liveRepo.RegisterComponent<TestHealth>();
        preTickSnapshot.RegisterComponent<TestHealth>();

        // Create entity and set pre-tick state (value = 100).
        var entity = liveRepo.CreateEntity();
        liveRepo.AddComponent(entity, new TestHealth { Current = 100 });
        preTickSnapshot.SyncFrom(liveRepo);

        // Advance the global version so GetComponentRW bumps the chunk version above
        // the snapshot's chunk version, which lets SyncDirtyChunks detect the change
        // when rewinding liveRepo back to preTickSnapshot in OnHit.
        liveRepo.Tick();

        // Simulate mid-tick mutation: live repo advances to 50.
        ref var h = ref liveRepo.GetComponentRW<TestHealth>(entity);
        h.Current = 50;

        var tc               = new MockDebugTimeController();
        var snapshotProvider = new DebugSnapshotProvider(preTickSnapshot);
        var manager          = new DataBreakpointManager(liveRepo, preTickSnapshot, snapshotProvider, tc);

        bool hitEventFired   = false;
        bool pauseEventFired = false;
        bool pauseEventValue = false;
        manager.OnBreakpointHit    += (_, _) => hitEventFired   = true;
        manager.OnPauseStateChanged += v   => { pauseEventFired = true; pauseEventValue = v; };

        var id = manager.Add(new Breakpoint
        {
            Id = BreakpointId.Invalid, Enabled = true, OccurrenceThreshold = 1, DisplayName = "T"
        });
        var bp = manager.AllBreakpoints[0];

        manager.OnHit(bp, entity);

        // (a) postTickSnapshot captured live value (50) at hit time.
        Assert.Equal(50, manager.PostTickSnapshot.GetComponent<TestHealth>(entity).Current);
        // (b) liveRepo rewound to pre-tick (100).
        Assert.Equal(100, liveRepo.GetComponent<TestHealth>(entity).Current);
        // (c) clock paused.
        Assert.True(tc.IsPausedByDebugger);
        Assert.True(manager.IsPaused);
        // (d) events fired.
        Assert.True(hitEventFired);
        Assert.True(pauseEventFired);
        Assert.True(pauseEventValue);
    }

    /// <summary>
    /// RequestContinue must: restore post-tick state, resume the clock, clear IsPaused,
    /// and fire the pause-state-changed event with false.
    /// </summary>
    [Fact]
    public void RequestContinue_ResumesClockAndClearsPause()
    {
        var (manager, _, _, tc) = ManagerFactory.Create();

        bool pauseEventValue = true;
        manager.OnPauseStateChanged += v => pauseEventValue = v;

        var id = manager.Add(ManagerFactory.MakeBreakpoint(enabled: true));
        manager.OnHit(manager.AllBreakpoints[0], new Entity(1, 0));

        Assert.True(manager.IsPaused);

        manager.RequestContinue();

        Assert.False(manager.IsPaused);
        Assert.Equal(1, tc.ResumeCount);
        Assert.False(pauseEventValue);
    }

    /// <summary>
    /// RequestStep must: restore post-tick state, call RequestStepOneTick, clear IsPaused,
    /// and fire the pause-state-changed event with false.
    /// No events may be injected (clean step).
    /// </summary>
    [Fact]
    public void RequestStep_ResumesWithOneTick_AndClearsPause()
    {
        var (manager, _, _, tc) = ManagerFactory.Create();

        bool pauseEventValue = true;
        manager.OnPauseStateChanged += v => pauseEventValue = v;

        manager.Add(ManagerFactory.MakeBreakpoint(enabled: true));
        manager.OnHit(manager.AllBreakpoints[0], new Entity(1, 0));

        Assert.True(manager.IsPaused);

        manager.RequestStep();

        Assert.False(manager.IsPaused);
        Assert.Equal(1, tc.StepRequestCount);
        Assert.Equal(0, tc.ResumeCount); // RequestResume must NOT be called for a step
        Assert.False(pauseEventValue);
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>CE-035</c> — SUPERSEDES <c>RequestContinue_WhenNotPaused_IsNoOp</c>.</b>
    ///
    /// <para>⚠ This rail previously asserted <c>ResumeCount == 0</c> when not paused. 🔴 That assertion
    /// ENCODED THE DEFECT: after <c>RequestStep()</c>, <c>_isPaused</c> is false while the clock is still
    /// halted, so *"not paused ⇒ do nothing"* made *step, look, continue* leave the operator halted
    /// forever. 📐 The <c>CE-029</c> barrier rail measured it and had to call
    /// <c>controller.RequestResume()</c> directly with a comment saying why.</para>
    ///
    /// <para>⭐ The contract now: the REWIND-UNDO half is conditional on <c>IsPaused</c>; the RESUME half
    /// is not, because only the time controller knows whether time is running. ⇒ no state change to
    /// announce *(no <c>OnPauseStateChanged</c>)*, and a resume that is idempotent when already
    /// running.</para>
    /// </summary>
    [Fact]
    public void RequestContinue_WhenNotPaused_StillResumesTheClock_AndRaisesNoEvent()
    {
        var (manager, _, _, tc) = ManagerFactory.Create();

        int eventCount = 0;
        manager.OnPauseStateChanged += _ => eventCount++;

        manager.RequestContinue();

        Assert.Equal(1, tc.ResumeCount);
        Assert.Equal(0, eventCount);
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>CE-035</c>'s actual gesture: STEP, then CONTINUE.</b>
    ///
    /// <para>⛔ Before the fix this asserted zero resumes — the step cleared <c>IsPaused</c> and the
    /// continue returned early, so the clock stayed halted with no way back through this surface.</para>
    /// </summary>
    [Fact]
    public void ContinueAfterAStepResumesTheClock()
    {
        var (manager, _, _, tc) = ManagerFactory.Create();

        manager.Add(ManagerFactory.MakeBreakpoint(enabled: true));
        manager.OnHit(manager.AllBreakpoints[0], new Entity(1, 0));
        Assert.True(manager.IsPaused);

        manager.RequestStep();
        Assert.False(manager.IsPaused);          // 📐 the measured contract — RequestStep clears it
        Assert.Equal(0, tc.ResumeCount);         // ⛔ a step is not a resume

        manager.RequestContinue();

        Assert.Equal(1, tc.ResumeCount);
    }

    /// <summary>
    /// RequestStep when not paused must be a no-op.
    /// </summary>
    [Fact]
    public void RequestStep_WhenNotPaused_IsNoOp()
    {
        var (manager, _, _, tc) = ManagerFactory.Create();

        int eventCount = 0;
        manager.OnPauseStateChanged += _ => eventCount++;

        manager.RequestStep();

        Assert.Equal(0, tc.StepRequestCount);
        Assert.Equal(0, eventCount);
    }

    /// <summary>
    /// When OccurrenceThreshold is 3, only the 3rd call to OnHit should pause.
    /// The first two must increment HitCount but not pause.
    /// </summary>
    [Fact]
    public void OccurrenceThreshold_PausesOnNthHit()
    {
        var (manager, _, _, tc) = ManagerFactory.Create();

        manager.Add(ManagerFactory.MakeBreakpoint(enabled: true, threshold: 3));
        var entity = new Entity(1, 0);

        // Hit 1: no pause.
        manager.OnHit(manager.AllBreakpoints[0], entity);
        Assert.False(manager.IsPaused);
        Assert.Equal(0, tc.PauseRequestCount);

        // Re-read breakpoint after each hit (record gets updated).
        // Hit 2: no pause.
        manager.OnHit(manager.AllBreakpoints[0], entity);
        Assert.False(manager.IsPaused);
        Assert.Equal(0, tc.PauseRequestCount);

        // Hit 3: pause.
        manager.OnHit(manager.AllBreakpoints[0], entity);
        Assert.True(manager.IsPaused);
        Assert.Equal(1, tc.PauseRequestCount);
    }

    /// <summary>
    /// HitCount on the stored breakpoint record must increment on every OnHit call,
    /// regardless of threshold.
    /// </summary>
    [Fact]
    public void OnHit_AlwaysIncrementsHitCount()
    {
        var (manager, _, _, _) = ManagerFactory.Create();

        manager.Add(ManagerFactory.MakeBreakpoint(enabled: true, threshold: 99));
        var entity = new Entity(1, 0);

        manager.OnHit(manager.AllBreakpoints[0], entity);
        Assert.Equal(1, manager.AllBreakpoints[0].HitCount);

        manager.OnHit(manager.AllBreakpoints[0], entity);
        Assert.Equal(2, manager.AllBreakpoints[0].HitCount);
    }

    /// <summary>
    /// After OnHit rewinds liveRepo to pre-tick state, RequestStep must restore it
    /// to the post-tick state (i.e. what the engine produced before the rewind).
    /// </summary>
    [Fact]
    public void RequestStep_RestoresLiveRepoToPostTickState()
    {
        ComponentTypeRegistry.Clear();
        var liveRepo        = new EntityRepository();
        var preTickSnapshot = new EntityRepository();
        liveRepo.RegisterComponent<TestHealth>();
        preTickSnapshot.RegisterComponent<TestHealth>();

        var entity = liveRepo.CreateEntity();
        liveRepo.AddComponent(entity, new TestHealth { Current = 100 });
        preTickSnapshot.SyncFrom(liveRepo);

        // Advance the global version so GetComponentRW bumps the chunk version above
        // the snapshot's chunk version, enabling SyncDirtyChunks to detect the mutation
        // when OnHit rewinds liveRepo back to preTickSnapshot.
        liveRepo.Tick();

        // Simulate post-tick state: live moves to 50.
        ref var h = ref liveRepo.GetComponentRW<TestHealth>(entity);
        h.Current = 50;

        var tc               = new MockDebugTimeController();
        var snapshotProvider = new DebugSnapshotProvider(preTickSnapshot);
        var manager          = new DataBreakpointManager(liveRepo, preTickSnapshot, snapshotProvider, tc);

        manager.Add(new Breakpoint { Id = BreakpointId.Invalid, Enabled = true, OccurrenceThreshold = 1, DisplayName = "T" });
        manager.OnHit(manager.AllBreakpoints[0], entity);

        // After OnHit: liveRepo rewound to pre-tick (100).
        Assert.Equal(100, liveRepo.GetComponent<TestHealth>(entity).Current);

        manager.RequestStep();

        // After RequestStep: liveRepo restored to post-tick (50).
        Assert.Equal(50, liveRepo.GetComponent<TestHealth>(entity).Current);
    }

    /// <summary>
    /// After OnHit rewinds liveRepo to pre-tick state, RequestContinue must restore it
    /// to the post-tick state so the engine can resume from where it was.
    /// </summary>
    [Fact]
    public void RequestContinue_RestoresLiveRepoToPostTickState()
    {
        ComponentTypeRegistry.Clear();
        var liveRepo        = new EntityRepository();
        var preTickSnapshot = new EntityRepository();
        liveRepo.RegisterComponent<TestHealth>();
        preTickSnapshot.RegisterComponent<TestHealth>();

        var entity = liveRepo.CreateEntity();
        liveRepo.AddComponent(entity, new TestHealth { Current = 100 });
        preTickSnapshot.SyncFrom(liveRepo);

        // Advance the global version so GetComponentRW bumps the chunk version above
        // the snapshot's chunk version, enabling SyncDirtyChunks to detect the mutation
        // when OnHit rewinds liveRepo back to preTickSnapshot.
        liveRepo.Tick();

        // Simulate post-tick state: live moves to 50.
        ref var h = ref liveRepo.GetComponentRW<TestHealth>(entity);
        h.Current = 50;

        var tc               = new MockDebugTimeController();
        var snapshotProvider = new DebugSnapshotProvider(preTickSnapshot);
        var manager          = new DataBreakpointManager(liveRepo, preTickSnapshot, snapshotProvider, tc);

        manager.Add(new Breakpoint { Id = BreakpointId.Invalid, Enabled = true, OccurrenceThreshold = 1, DisplayName = "T" });
        manager.OnHit(manager.AllBreakpoints[0], entity);

        // After OnHit: liveRepo rewound to pre-tick (100).
        Assert.Equal(100, liveRepo.GetComponent<TestHealth>(entity).Current);

        manager.RequestContinue();

        // After RequestContinue: liveRepo restored to post-tick (50).
        Assert.Equal(50, liveRepo.GetComponent<TestHealth>(entity).Current);
    }

    // -------------------------------------------------------------------------
    // P11T12 Work Item C: OccurrenceThreshold validation
    // -------------------------------------------------------------------------

    /// <summary>
    /// Passing <c>occurrenceThreshold: 0</c> must throw <see cref="ArgumentOutOfRangeException"/>
    /// immediately; 0 is not a valid threshold (minimum is 1).
    /// </summary>
    [Fact]
    public void AddBreakpoint_ThresholdZero_Throws()
    {
        ComponentTypeRegistry.Clear();
        var (manager, _, _, _) = ManagerFactory.Create();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            manager.AddBreakpoint(
                new ExternalHitTagPredicateDto { Tag = "t" },
                displayName: "threshold-test",
                occurrenceThreshold: 0));
    }

    /// <summary>
    /// The default <c>occurrenceThreshold</c> (1) must pause on the very first hit.
    /// </summary>
    [Fact]
    public void AddBreakpoint_ThresholdOne_IsDefault_PausesOnFirstHit()
    {
        ComponentTypeRegistry.Clear();
        var (manager, liveRepo, _, tc) = ManagerFactory.Create();

        var bpId = manager.AddBreakpoint(
            new ExternalHitTagPredicateDto { Tag = "fire" },
            displayName: "threshold-one-test",
            occurrenceThreshold: 1);

        var bp = manager.AllBreakpoints.First(b => b.Id == bpId);
        manager.OnHit(bp, Fdp.Core.Entity.Null);

        Assert.True(manager.IsPaused);
        Assert.Equal(1, tc.PauseRequestCount);
    }
}

// ---------------------------------------------------------------------------
// EngineDebugTimeControllerTests
// ---------------------------------------------------------------------------

/// <summary>
/// Verifies the contract of <see cref="IEngineDebugTimeController"/> as implemented
/// by <see cref="MockDebugTimeController"/>, and confirms that
/// <see cref="IBlueprintTimeController"/> still resolves through inheritance.
/// </summary>
public sealed class EngineDebugTimeControllerTests
{
    /// <summary>
    /// <see cref="MockDebugTimeController"/> must satisfy the full
    /// pause / resume / step contract of <see cref="IEngineDebugTimeController"/>.
    /// </summary>
    [Fact]
    public void IEngineDebugTimeController_Implements_PauseResumeStepContract()
    {
        IEngineDebugTimeController tc = new MockDebugTimeController();

        Assert.False(tc.IsPausedByDebugger);

        tc.RequestPause();
        Assert.True(tc.IsPausedByDebugger);

        tc.RequestResume();
        Assert.False(tc.IsPausedByDebugger);

        tc.RequestPause();
        tc.RequestStepOneTick();
        Assert.False(tc.IsPausedByDebugger);
    }

    /// <summary>
    /// <see cref="IBlueprintTimeController"/> must still derive from
    /// <see cref="IEngineDebugTimeController"/> so callers that hold an
    /// <c>IBlueprintTimeController</c> reference can use it as the base interface.
    /// </summary>
    [Fact]
    public void IBlueprintTimeController_Still_Resolves_Through_Inheritance()
    {
        // IBlueprintTimeController IS-A IEngineDebugTimeController.
        Assert.True(
            typeof(IEngineDebugTimeController).IsAssignableFrom(typeof(IBlueprintTimeController)));
    }
}

// ---------------------------------------------------------------------------
// DataBreakpointSystemTests  (UBP-P2T1 -- component-data path)
// ---------------------------------------------------------------------------

/// <summary>
/// Integration tests for <see cref="DataBreakpointSystem"/> covering the
/// component-predicate path.
/// </summary>
[Collection("ComponentRegistry")]
public sealed class DataBreakpointSystemTests
{
    private static (DataBreakpointManager manager, DataBreakpointSystem system, EntityRepository repo) Setup()
    {
        ComponentTypeRegistry.Clear();
        var repo          = new EntityRepository();
        var preTick       = new EntityRepository();
        var tc            = new MockDebugTimeController();
        var provider      = new DebugSnapshotProvider(preTick);
        var compiler      = new PredicateCompiler(new ComponentEditServiceBuilder().Build());
        var eventCompiler = new EventScannerCompiler(new ComponentEditServiceBuilder().Build());
        var manager       = new DataBreakpointManager(repo, preTick, provider, tc, compiler, eventCompiler);
        var system        = new DataBreakpointSystem(manager);
        return (manager, system, repo);
    }

    /// <summary>
    /// Execute with no breakpoints must be a no-op (no pause, no exception) and must not
    /// allocate any heap memory in the steady state.
    /// </summary>
    [Fact]
    public void NoBreakpoints_DoesNoWork_ZeroAllocations()
    {
        var (manager, system, repo) = Setup();

        // Warmup JIT.
        system.Execute(repo, 0f);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long before = GC.GetAllocatedBytesForCurrentThread();
        const int Iterations = 10_000;
        for (int i = 0; i < Iterations; i++)
            system.Execute(repo, 0f);
        long after = GC.GetAllocatedBytesForCurrentThread();

        Assert.False(manager.IsPaused);
        Assert.Equal(0L, after - before);
    }

    /// <summary>
    /// A PropertyMatch breakpoint must fire and pause when the compiled predicate
    /// matches an entity in the repository.
    /// </summary>
    [Fact]
    public void PropertyMatch_FiresWhenConditionMet()
    {
        var (manager, system, repo) = Setup();

        repo.RegisterComponent<TestDamage>();
        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new TestDamage { Value = 5.0f });

        bool hitFired = false;
        manager.OnBreakpointHit += (_, _) => hitFired = true;

        manager.Add(new Breakpoint
        {
            Id                 = BreakpointId.Invalid,
            Enabled            = true,
            OccurrenceThreshold = 1,
            DisplayName        = "T",
            Condition          = new PropertyMatchDto
            {
                ComponentType = typeof(TestDamage),
                PropertyPath  = "Value",
                Operator      = SearchOperator.LessThan,
                Predicate     = new NumericPredicateDto { MaxValue = 10.0 }
            }
        });

        system.Execute(repo, 0f);

        Assert.True(manager.IsPaused);
        Assert.True(hitFired);
    }

    /// <summary>
    /// When <see cref="Breakpoint.FilterEntity"/> is set, only that entity should
    /// trigger the breakpoint even if other matching entities exist.
    /// </summary>
    [Fact]
    public void FilterEntity_ScopesPredicateToOneEntity()
    {
        var (manager, system, repo) = Setup();

        repo.RegisterComponent<TestDamage>();
        var e1 = repo.CreateEntity();
        var e2 = repo.CreateEntity();
        repo.AddComponent(e1, new TestDamage { Value = 5.0f });
        repo.AddComponent(e2, new TestDamage { Value = 5.0f });

        int hitCount = 0;
        manager.OnBreakpointHit += (_, _) => hitCount++;

        manager.Add(new Breakpoint
        {
            Id                 = BreakpointId.Invalid,
            Enabled            = true,
            OccurrenceThreshold = 1,
            DisplayName        = "T",
            FilterEntity       = e1,
            Condition          = new PropertyMatchDto
            {
                ComponentType = typeof(TestDamage),
                PropertyPath  = "Value",
                Operator      = SearchOperator.LessThan,
                Predicate     = new NumericPredicateDto { MaxValue = 10.0 }
            }
        });

        system.Execute(repo, 0f);

        Assert.Equal(1, hitCount);
    }

    /// <summary>
    /// A breakpoint with OccurrenceThreshold = 3 must pause only on the third
    /// matching Execute call.
    /// </summary>
    [Fact]
    public void OccurrenceThreshold_PausesOnNthHit()
    {
        var (manager, system, repo) = Setup();

        repo.RegisterComponent<TestDamage>();
        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new TestDamage { Value = 5.0f });

        manager.Add(new Breakpoint
        {
            Id                 = BreakpointId.Invalid,
            Enabled            = true,
            OccurrenceThreshold = 3,
            DisplayName        = "T",
            Condition          = new PropertyMatchDto
            {
                ComponentType = typeof(TestDamage),
                PropertyPath  = "Value",
                Operator      = SearchOperator.LessThan,
                Predicate     = new NumericPredicateDto { MaxValue = 10.0 }
            }
        });

        // Tick 1: entity seen for the first time (LastScanVersion = 0 -> full scan).
        system.Execute(repo, 0f);
        Assert.False(manager.IsPaused);

        // Advance version + re-touch the component so the chunk version moves past
        // the LastScanVersion stored after tick 1.  In production this happens naturally
        // because each simulation tick calls repo.Tick() and ECS systems call GetComponentRW.
        repo.Tick();
        repo.GetComponentRW<TestDamage>(entity);

        // Tick 2: component chunk changed -> entity re-detected; HitCount = 2.
        system.Execute(repo, 0f);
        Assert.False(manager.IsPaused);

        repo.Tick();
        repo.GetComponentRW<TestDamage>(entity);

        // Tick 3: entity re-detected; HitCount = 3 >= OccurrenceThreshold -> pause.
        system.Execute(repo, 0f);
        Assert.True(manager.IsPaused);
    }
}

// ---------------------------------------------------------------------------
// DataBreakpointSystemEventTests  (UBP-P2T2 -- event path)
// ---------------------------------------------------------------------------

/// <summary>
/// Integration tests for <see cref="DataBreakpointSystem"/> covering the event-scanner path.
/// </summary>
[Collection("ComponentRegistry")]
public sealed class DataBreakpointSystemEventTests
{
    private static (DataBreakpointManager manager, DataBreakpointSystem system, EntityRepository repo, FdpEventBus bus) Setup()
    {
        ComponentTypeRegistry.Clear();
        var repo          = new EntityRepository();
        var preTick       = new EntityRepository();
        var tc            = new MockDebugTimeController();
        var provider      = new DebugSnapshotProvider(preTick);
        var compiler      = new PredicateCompiler(new ComponentEditServiceBuilder().Build());
        var eventCompiler = new EventScannerCompiler(new ComponentEditServiceBuilder().Build());
        var manager       = new DataBreakpointManager(repo, preTick, provider, tc, compiler, eventCompiler);
        var bus           = new FdpEventBus();
        var system        = new DataBreakpointSystem(manager, bus);
        return (manager, system, repo, bus);
    }

    /// <summary>
    /// A TransientEventPredicateDto with AnyOccurrence = true must fire whenever
    /// at least one event of the target type appears in the bus read buffer.
    /// </summary>
    [Fact]
    public void Bus_AnyOccurrence_Predicate_FiresOnAnyEventOfType()
    {
        var (manager, system, repo, bus) = Setup();

        manager.Add(new Breakpoint
        {
            Id                 = BreakpointId.Invalid,
            Enabled            = true,
            OccurrenceThreshold = 1,
            DisplayName        = "T",
            Condition          = new TransientEventPredicateDto
            {
                EventType     = typeof(HitTestEvent),
                AnyOccurrence = true
            }
        });

        bus.Publish(new HitTestEvent { Damage = 50f });
        bus.SwapBuffers();

        system.Execute(repo, 0f);

        Assert.True(manager.IsPaused);
    }

    /// <summary>
    /// A TransientEventPredicateDto with a payload constraint must fire only when
    /// the event value matches the constraint.
    /// </summary>
    [Fact]
    public void Bus_PayloadConstraint_FiresOnlyWhenPayloadMatches()
    {
        var (manager, system, repo, bus) = Setup();

        manager.Add(new Breakpoint
        {
            Id                 = BreakpointId.Invalid,
            Enabled            = true,
            OccurrenceThreshold = 1,
            DisplayName        = "T",
            Condition          = new TransientEventPredicateDto
            {
                EventType     = typeof(HitTestEvent),
                AnyOccurrence = false,
                PropertyPath  = "Damage",
                Operator      = SearchOperator.GreaterThan,
                TargetValue   = "50"
            }
        });

        // Value below threshold -- must not fire.
        bus.Publish(new HitTestEvent { Damage = 40f });
        bus.SwapBuffers();
        system.Execute(repo, 0f);
        Assert.False(manager.IsPaused);

        // Value above threshold -- must fire.
        bus.Publish(new HitTestEvent { Damage = 80f });
        bus.SwapBuffers();
        system.Execute(repo, 0f);
        Assert.True(manager.IsPaused);
    }
}
