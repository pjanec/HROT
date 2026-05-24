using System;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.ReplayBrowser.Search;
using Hrot.Diagnostics.Breakpoints;

namespace Hrot.Diagnostics.Breakpoints.Tests;

// ---- Test-only unmanaged component for P9T1 --------------------------------
[ComponentId(230)]
file struct ReloadTestComponent { public int Value; }

[Collection("ComponentRegistry")]
public sealed class HotReloadResilienceTests
{
    // ── P9T1 ──────────────────────────────────────────────────────────────────

    [Fact]
    public void HotReload_StructureCompatible_PreservesBreakpoint()
    {
        // Arrange: real manager with real compiler; register a PropertyMatchDto BP.
        ComponentTypeRegistry.Clear();

        var (mgr, liveRepo, _, _) = ManagerFactory.Create();
        var id = mgr.AddBreakpoint(
            new PropertyMatchDto
            {
                ComponentType = typeof(ReloadTestComponent),
                PropertyPath  = "Value",
                Predicate     = new NumericPredicateDto { MinValue = 0, MaxValue = 10 },
            },
            displayName: "ReloadTestBP");

        // Pre-check: breakpoint is mounted.
        Assert.Single(mgr.MountedComponentPredicates);
        Assert.False(mgr.AllBreakpoints.First(b => b.Id == id).IsBroken);

        // Act: simulate a hot-reload cycle (assembly reloaded; component still exists).
        mgr.OnHotReloadCompleted();

        // Assert: still mounted, not broken.
        Assert.Single(mgr.MountedComponentPredicates);
        Assert.False(mgr.AllBreakpoints.First(b => b.Id == id).IsBroken);
    }

    [Fact]
    public void HotReload_RemovesTargetedField_MarksBreakpointBroken()
    {
        // Arrange: manager with a compiler stub that throws on the second compile call.
        ComponentTypeRegistry.Clear();

        var throwingCompiler = new ThrowOnSecondCompileCompiler();
        var liveRepo         = new EntityRepository();
        var preTickSnapshot  = new EntityRepository();
        var tc               = new MockDebugTimeController();
        var snapshotProvider = new DebugSnapshotProvider(preTickSnapshot);
        var mgr = new DataBreakpointManager(
            liveRepo, preTickSnapshot, snapshotProvider, tc,
            predicateCompiler: throwingCompiler);

        var id = mgr.AddBreakpoint(
            new PropertyMatchDto
            {
                ComponentType = typeof(ReloadTestComponent),
                PropertyPath  = "Value",
                Predicate     = new NumericPredicateDto { MinValue = 0, MaxValue = 10 },
            },
            displayName: "BreaksBP");

        // First compile succeeded (from AddBreakpoint). Now simulate a reload where the field is gone.
        // Act: second compilation attempt should throw -> IsBroken.
        mgr.OnHotReloadCompleted();

        // Assert: marked broken, not crashed.
        var bp = mgr.AllBreakpoints.First(b => b.Id == id);
        Assert.True(bp.IsBroken);
        // DTO retained (so the user can fix it).
        Assert.NotNull(bp.Condition);
    }

    [Fact]
    public void HotReload_NoAccessViolation_DuringActiveBreakpoint()
    {
        // Arrange: 5 breakpoints, 100 reload cycles -> must not throw.
        ComponentTypeRegistry.Clear();

        var (mgr, _, _, _) = ManagerFactory.Create();
        for (int i = 0; i < 5; i++)
        {
            mgr.AddBreakpoint(
                new PropertyMatchDto
                {
                    ComponentType = typeof(ReloadTestComponent),
                    PropertyPath  = "Value",
                    Predicate     = new NumericPredicateDto { MinValue = i, MaxValue = i + 1 },
                },
                displayName: $"BP{i}");
        }

        // Act: 100 reload cycles.
        var ex = Record.Exception(() =>
        {
            for (int cycle = 0; cycle < 100; cycle++)
                mgr.OnHotReloadCompleted();
        });

        // Assert: no exception.
        Assert.Null(ex);
        // All breakpoints still exist and are not broken.
        Assert.All(mgr.AllBreakpoints, bp => Assert.False(bp.IsBroken));
    }

    // ── P9T2 ──────────────────────────────────────────────────────────────────

    [Fact]
    public void HotReloadBegin_DuringPause_ForcesContinueAndFlushesMutations()
    {
        // Arrange: pause the manager with 3 staged mutations.
        // Manual setup is used so preTickSnapshot can hold the entity that survives OnHit's revert.
        ComponentTypeRegistry.Clear();

        var liveRepo         = new EntityRepository();
        var preTickSnapshot  = new EntityRepository();
        var tc               = new MockDebugTimeController();
        var snapshotProvider = new DebugSnapshotProvider(preTickSnapshot);
        var mgr = new DataBreakpointManager(
            liveRepo, preTickSnapshot, snapshotProvider, tc);

        // Create entity and sync it into preTickSnapshot so it survives the OnHit revert.
        var entity = liveRepo.CreateEntity();
        preTickSnapshot.SyncFrom(liveRepo);

        // Put the manager in paused state by firing OnHit directly.
        var bpId = mgr.AddBreakpoint(new PropertyMatchDto(), displayName: "p9t2");
        var bp   = mgr.AllBreakpoints.First();
        mgr.OnHit(bp, entity);
        Assert.True(mgr.IsPaused);

        // Stage 3 mutations; StageMutation only requires a known component type id.
        mgr.StageMutation(entity, typeof(ReloadTestComponent),
            new ReloadTestComponent { Value = 42 });
        mgr.StageMutation(entity, typeof(ReloadTestComponent),
            new ReloadTestComponent { Value = 43 });
        mgr.StageMutation(entity, typeof(ReloadTestComponent),
            new ReloadTestComponent { Value = 44 });
        Assert.Equal(3, mgr.PendingMutationsCount);

        // Act: hot reload begins.
        mgr.OnHotReloadBegin();

        // Assert: unpaused, mutations flushed.
        Assert.False(mgr.IsPaused);
        Assert.Equal(0, mgr.PendingMutationsCount);
    }

    [Fact]
    public void Notification_StepAbandoned_Emitted()
    {
        // Arrange: manager with a notifier stub; put in paused state.
        ComponentTypeRegistry.Clear();

        var notifier = new RecordingBreakpointNotifier();
        var liveRepo         = new EntityRepository();
        var preTickSnapshot  = new EntityRepository();
        var tc               = new MockDebugTimeController();
        var snapshotProvider = new DebugSnapshotProvider(preTickSnapshot);
        var mgr = new DataBreakpointManager(
            liveRepo, preTickSnapshot, snapshotProvider, tc,
            notifier: notifier);

        var bpId = mgr.AddBreakpoint(new PropertyMatchDto(), displayName: "notif-bp");
        var bp   = mgr.AllBreakpoints.First();
        var entity = liveRepo.CreateEntity();
        mgr.OnHit(bp, entity);
        Assert.True(mgr.IsPaused);

        // Act: hot reload begins.
        mgr.OnHotReloadBegin();

        // Assert: notification emitted.
        Assert.Single(notifier.Messages);
        Assert.Contains("abandoned", notifier.Messages[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HotReloadBegin_WhenNotPaused_DoesNothing()
    {
        ComponentTypeRegistry.Clear();
        var (mgr, _, _, _) = ManagerFactory.Create();

        var ex = Record.Exception(() => mgr.OnHotReloadBegin());

        Assert.Null(ex);
        Assert.False(mgr.IsPaused);
        Assert.Equal(0, mgr.PendingMutationsCount);
    }
}

// ---- Test helpers -----------------------------------------------------------

/// <summary>
/// An IPredicateCompiler that succeeds on the first compile per DTO instance,
/// then throws on subsequent calls — simulates a "field removed after hot-reload" scenario.
/// </summary>
file sealed class ThrowOnSecondCompileCompiler : IPredicateCompiler
{
    private int _callCount;

    public Func<EntityRepository, Entity, bool> CompileComponentPredicate(SearchPredicateDto dto)
    {
        if (_callCount++ >= 1)
            throw new InvalidOperationException("Simulated recompile failure: field removed.");
        // Delegate that always returns false (never fires) -- enough for mounting.
        return static (_, _) => false;
    }

    public IReadOnlyList<Type> ExtractMandatoryComponents(SearchPredicateDto dto) =>
        Array.Empty<Type>();
}

/// <summary>
/// Captures Notify calls for assertion in P9T2 tests.
/// </summary>
file sealed class RecordingBreakpointNotifier : IBreakpointNotifier
{
    public List<string> Messages { get; } = new();
    public void Notify(string message) => Messages.Add(message);
}
