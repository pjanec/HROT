using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.ReplayBrowser.Search;
using Hrot.Diagnostics.Breakpoints;
using StructEdit.Reflection;
using Xunit;

namespace Hrot.Diagnostics.Breakpoints.Tests;

// ---- Test-only components (file-scoped to avoid ID conflicts) ---------------

[ComponentId(250)]
file struct AllocTestHealth { public int Current; }

[ComponentId(251)]
file struct AllocTestAmmo { public int Rounds; }

// =============================================================================
// Class 1: DataBreakpointSystemAllocationTests (P11T1)
// =============================================================================

/// <summary>
/// Regression tests verifying that the zero-allocation refactor of
/// <see cref="DataBreakpointSystem.Execute"/> still fires hits correctly.
/// </summary>
[Collection("ComponentRegistry")]
public sealed class DataBreakpointSystemAllocationTests
{
    private static (DataBreakpointManager manager,
                    DataBreakpointSystem system,
                    EntityRepository liveRepo,
                    DebugSnapshotProvider snapshotProvider,
                    MockDebugTimeController tc)
        Setup()
    {
        ComponentTypeRegistry.Clear();
        var liveRepo        = new EntityRepository();
        var preTick         = new EntityRepository();
        var tc              = new MockDebugTimeController();
        var snapshotProvider = new DebugSnapshotProvider(preTick);
        var compiler        = new PredicateCompiler(new ComponentEditServiceBuilder().Build());
        var eventCompiler   = new EventScannerCompiler(new ComponentEditServiceBuilder().Build());
        var manager         = new DataBreakpointManager(
            liveRepo, preTick, snapshotProvider, tc, compiler, eventCompiler);
        var system          = new DataBreakpointSystem(manager);
        return (manager, system, liveRepo, snapshotProvider, tc);
    }

    /// <summary>
    /// Regression: the refactored Execute (reusable buffer + foreach) must still fire
    /// OnHit and pause when a matching entity exists.
    /// </summary>
    [Fact]
    public void DataBreakpointSystem_StillFiresHits_AfterZeroAllocRefactor()
    {
        var (manager, system, liveRepo, snapshotProvider, tc) = Setup();

        liveRepo.RegisterComponent<AllocTestHealth>();

        // Create 3 entities with Health.Current = 50
        for (int i = 0; i < 3; i++)
        {
            var e = liveRepo.CreateEntity();
            liveRepo.AddComponent(e, new AllocTestHealth { Current = 50 });
        }

        // Add BP: Current > 0 (always matches)
        manager.AddBreakpoint(new PropertyMatchDto
        {
            ComponentType = typeof(AllocTestHealth),
            PropertyPath  = "Current",
            Operator      = SearchOperator.GreaterThan,
            Predicate     = new NumericPredicateDto { MinValue = 0.001 },
        });

        // Prime pre-tick snapshot
        snapshotProvider.Execute(liveRepo, 0f);

        system.Execute(liveRepo, 0.016f);

        // BP must have fired: manager paused
        Assert.True(manager.IsPaused);
        Assert.Equal(1, tc.PauseRequestCount);
    }

    /// <summary>
    /// Verifies that <c>_pendingHitsBuffer.Clear()</c> is called between breakpoints so
    /// hits from one breakpoint do not bleed into another's evaluation.
    /// BP-A matches nothing; BP-B matches all 3 entities.
    /// The pause count must be exactly 1 (only BP-B fires once via OccurrenceThreshold).
    /// </summary>
    [Fact]
    public void DataBreakpointSystem_ReusableBuffer_ClearedBetweenBreakpoints()
    {
        var (manager, system, liveRepo, snapshotProvider, tc) = Setup();

        liveRepo.RegisterComponent<AllocTestHealth>();

        // 3 entities with Health.Current = 50
        for (int i = 0; i < 3; i++)
        {
            var e = liveRepo.CreateEntity();
            liveRepo.AddComponent(e, new AllocTestHealth { Current = 50 });
        }

        // BP-A: Current > 100 -- no entity matches (50 is not > 100)
        manager.AddBreakpoint(new PropertyMatchDto
        {
            ComponentType = typeof(AllocTestHealth),
            PropertyPath  = "Current",
            Operator      = SearchOperator.GreaterThan,
            Predicate     = new NumericPredicateDto { MinValue = 100.001 },
        });

        // BP-B: Current > 0 -- all 3 entities match
        manager.AddBreakpoint(new PropertyMatchDto
        {
            ComponentType = typeof(AllocTestHealth),
            PropertyPath  = "Current",
            Operator      = SearchOperator.GreaterThan,
            Predicate     = new NumericPredicateDto { MinValue = 0.001 },
        });

        // Prime pre-tick snapshot
        snapshotProvider.Execute(liveRepo, 0f);

        system.Execute(liveRepo, 0.016f);

        // BP-B fired (at least one entity matched) → paused
        Assert.True(manager.IsPaused);
        // Paused exactly once (re-entrancy guard drops the 2nd/3rd hit in same tick)
        Assert.Equal(1, tc.PauseRequestCount);
    }
}

// =============================================================================
// Class 2: ChunkVersionScanTests (P11T2)
// =============================================================================

/// <summary>
/// Tests verifying that <see cref="CompiledComponentPredicate.LastScanVersion"/> is
/// maintained correctly and that unchanged chunks are skipped on subsequent Execute calls.
/// </summary>
[Collection("ComponentRegistry")]
public sealed class ChunkVersionScanTests
{
    private static (DataBreakpointManager manager,
                    DataBreakpointSystem system,
                    EntityRepository liveRepo,
                    DebugSnapshotProvider snapshotProvider,
                    MockDebugTimeController tc)
        Setup()
    {
        ComponentTypeRegistry.Clear();
        var liveRepo         = new EntityRepository();
        var preTick          = new EntityRepository();
        var tc               = new MockDebugTimeController();
        var snapshotProvider = new DebugSnapshotProvider(preTick);
        var compiler         = new PredicateCompiler(new ComponentEditServiceBuilder().Build());
        var eventCompiler    = new EventScannerCompiler(new ComponentEditServiceBuilder().Build());
        var manager          = new DataBreakpointManager(
            liveRepo, preTick, snapshotProvider, tc, compiler, eventCompiler);
        var system           = new DataBreakpointSystem(manager);
        return (manager, system, liveRepo, snapshotProvider, tc);
    }

    /// <summary>
    /// After a first Execute updates LastScanVersion, a second Execute with no intervening
    /// mutations must NOT fire the breakpoint (all chunks unchanged since the scan version).
    /// </summary>
    [Fact]
    public void DataBreakpointSystem_OnSecondExecute_DoesNotFireIfNoMutation()
    {
        var (manager, system, liveRepo, snapshotProvider, tc) = Setup();

        liveRepo.RegisterComponent<AllocTestHealth>();

        // 5 entities with Health.Current = 10
        for (int i = 0; i < 5; i++)
        {
            var e = liveRepo.CreateEntity();
            liveRepo.AddComponent(e, new AllocTestHealth { Current = 10 });
        }

        // BP: Current > 0 (always matches)
        manager.AddBreakpoint(new PropertyMatchDto
        {
            ComponentType = typeof(AllocTestHealth),
            PropertyPath  = "Current",
            Operator      = SearchOperator.GreaterThan,
            Predicate     = new NumericPredicateDto { MinValue = 0.001 },
        });

        // Advance version so component additions are "in the past"
        liveRepo.Tick();

        // Tick 0 pre-tick snapshot
        snapshotProvider.Execute(liveRepo, 0f);

        // Tick 0 Execute: BP fires on first scan (LastScanVersion = 0 → all entities visible)
        system.Execute(liveRepo, 0.016f);
        Assert.True(manager.IsPaused);
        Assert.Equal(1, tc.PauseRequestCount);

        // Resume: restore post-tick state
        manager.RequestContinue();
        Assert.False(manager.IsPaused);

        // Re-sync snapshot for tick 1 (NO mutations to liveRepo)
        snapshotProvider.Execute(liveRepo, 0f);

        // Tick 1 Execute: no mutations since last scan → QueryDelta returns nothing
        system.Execute(liveRepo, 0.016f);

        Assert.False(manager.IsPaused);
        Assert.Equal(1, tc.PauseRequestCount); // still only 1 total pause
    }

    /// <summary>
    /// After chunk-version tracking is established (LastScanVersion > 0), adding a new
    /// entity with a mutated component advances the chunk version above LastScanVersion,
    /// so the new entity IS detected on the next Execute.
    /// </summary>
    [Fact]
    public void DataBreakpointSystem_AfterMutation_DetectsNewEntity()
    {
        var (manager, system, liveRepo, snapshotProvider, tc) = Setup();

        liveRepo.RegisterComponent<AllocTestHealth>();

        // 5 entities with Health.Current = 10
        for (int i = 0; i < 5; i++)
        {
            var e = liveRepo.CreateEntity();
            liveRepo.AddComponent(e, new AllocTestHealth { Current = 10 });
        }

        // BP: Current > 0 (always matches)
        manager.AddBreakpoint(new PropertyMatchDto
        {
            ComponentType = typeof(AllocTestHealth),
            PropertyPath  = "Current",
            Operator      = SearchOperator.GreaterThan,
            Predicate     = new NumericPredicateDto { MinValue = 0.001 },
        });

        // Advance version so component additions are "in the past"
        liveRepo.Tick();

        // Tick 0 pre-tick snapshot
        snapshotProvider.Execute(liveRepo, 0f);

        // Tick 0 Execute: fires, pauses
        system.Execute(liveRepo, 0.016f);
        Assert.True(manager.IsPaused);
        Assert.Equal(1, tc.PauseRequestCount);
        manager.RequestContinue();

        // Re-sync + tick 1 Execute (no mutations) → no fire
        snapshotProvider.Execute(liveRepo, 0f);
        system.Execute(liveRepo, 0.016f);
        Assert.False(manager.IsPaused);
        Assert.Equal(1, tc.PauseRequestCount);

        // Advance version and add a NEW entity with AllocTestHealth { Current = 50 }
        liveRepo.Tick();
        var newEntity = liveRepo.CreateEntity();
        liveRepo.AddComponent(newEntity, new AllocTestHealth { Current = 50 });

        // Re-sync snapshot for tick 2
        snapshotProvider.Execute(liveRepo, 0f);

        // Tick 2 Execute: new entity was added → chunk version advanced → BP fires again
        system.Execute(liveRepo, 0.016f);

        Assert.True(manager.IsPaused);
        Assert.Equal(2, tc.PauseRequestCount); // second pause
    }
}

// =============================================================================
// Class 3: MountedAccessorCacheTests (P11T9)
// =============================================================================

/// <summary>
/// Tests verifying that <see cref="DataBreakpointManager.MountedComponentPredicates"/>
/// returns the same cached instance between mutations, and rebuilds after mutation.
/// </summary>
[Collection("ComponentRegistry")]
public sealed class MountedAccessorCacheTests
{
    /// <summary>
    /// Calling MountedComponentPredicates twice with no intervening Add/Remove
    /// must return the same list instance (cache hit).
    /// </summary>
    [Fact]
    public void MountedComponentPredicates_ReturnsSameInstance_BetweenMutations()
    {
        ComponentTypeRegistry.Clear();
        var (manager, liveRepo, _, _) = ManagerFactory.Create();

        liveRepo.RegisterComponent<AllocTestHealth>();

        manager.AddBreakpoint(new PropertyMatchDto
        {
            ComponentType = typeof(AllocTestHealth),
            PropertyPath  = "Current",
            Operator      = SearchOperator.GreaterThan,
            Predicate     = new NumericPredicateDto { MinValue = 0.001 },
        });

        var list1 = manager.MountedComponentPredicates;
        var list2 = manager.MountedComponentPredicates;

        Assert.Same(list1, list2); // same cached instance
    }

    /// <summary>
    /// After adding a second breakpoint the cache must be invalidated and rebuilt,
    /// so the returned list has 2 entries and is a different instance.
    /// </summary>
    [Fact]
    public void MountedComponentPredicates_Invalidated_AfterNewBreakpointAdded()
    {
        ComponentTypeRegistry.Clear();
        var (manager, liveRepo, _, _) = ManagerFactory.Create();

        liveRepo.RegisterComponent<AllocTestHealth>();

        manager.AddBreakpoint(new PropertyMatchDto
        {
            ComponentType = typeof(AllocTestHealth),
            PropertyPath  = "Current",
            Operator      = SearchOperator.GreaterThan,
            Predicate     = new NumericPredicateDto { MinValue = 0.001 },
        });
        var list1 = manager.MountedComponentPredicates;

        // Add another breakpoint -- must invalidate the cache
        manager.AddBreakpoint(new PropertyMatchDto
        {
            ComponentType = typeof(AllocTestHealth),
            PropertyPath  = "Current",
            Operator      = SearchOperator.LessThan,
            Predicate     = new NumericPredicateDto { MaxValue = 1000 },
        });
        var list2 = manager.MountedComponentPredicates;

        Assert.NotSame(list1, list2); // cache was rebuilt
        Assert.Equal(2, list2.Count);
    }
}
