using System.Linq;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Hrot.Diagnostics.Breakpoints;

namespace Hrot.Diagnostics.Breakpoints.Tests;

/// <summary>
/// Tests for PendingDebugMutation staging (UBP-P4T1) and ECB drain pipeline (UBP-P4T3).
/// </summary>
[Collection("ComponentRegistry")]
public sealed class PendingMutationTests
{
    // -----------------------------------------------------------------
    // P4T1: StageMutation envelope correctness
    // -----------------------------------------------------------------

    [Fact]
    public void Stage_UnmanagedStruct_StoresSizeAndClassification()
    {
        ComponentTypeRegistry.Clear();
        var (manager, liveRepo, _, _) = ManagerFactory.Create();
        liveRepo.RegisterComponent<TestHealth>();

        var entity = liveRepo.CreateEntity();
        manager.StageMutation(entity, typeof(TestHealth), new TestHealth { Current = 5 });

        Assert.Equal(1, manager.PendingMutationsCount);
        var m = manager.PendingMutationsQueue.Peek();
        Assert.False(m.IsManaged);
        Assert.Equal(Unsafe.SizeOf<TestHealth>(), m.SizeBytes);
        Assert.Equal(ComponentTypeRegistry.GetId(typeof(TestHealth)), m.ComponentTypeId);
    }

    [Fact]
    public void Stage_ManagedRef_StoresClassificationOnly()
    {
        ComponentTypeRegistry.Clear();
        var (manager, liveRepo, _, _) = ManagerFactory.Create();
        liveRepo.RegisterManagedComponent<EntityLabel>();

        var entity = liveRepo.CreateEntity();
        manager.StageMutation(entity, typeof(EntityLabel), new EntityLabel { Name = "test" });

        var m = manager.PendingMutationsQueue.Peek();
        Assert.True(m.IsManaged);
        Assert.Equal(0, m.SizeBytes);
    }

    // -----------------------------------------------------------------
    // P4T3: DrainPendingMutations via ECB
    // -----------------------------------------------------------------

    [Fact]
    public void Drain_UnmanagedPayload_PinnedAndCopiedToECB()
    {
        ComponentTypeRegistry.Clear();
        var liveRepo        = new EntityRepository();
        var preTickSnapshot = new EntityRepository();
        liveRepo.RegisterComponent<TestHealth>();
        preTickSnapshot.RegisterComponent<TestHealth>();

        var entity = liveRepo.CreateEntity();
        liveRepo.AddComponent(entity, new TestHealth { Current = 0 });
        preTickSnapshot.SyncFrom(liveRepo);

        liveRepo.Tick();
        ref var h = ref liveRepo.GetComponentRW<TestHealth>(entity);
        h.Current = 50;

        var tc       = new MockDebugTimeController();
        var provider = new DebugSnapshotProvider(preTickSnapshot);
        var manager  = new DataBreakpointManager(liveRepo, preTickSnapshot, provider, tc);

        var bpId = manager.Add(new Breakpoint { Id = BreakpointId.Invalid, Enabled = true, OccurrenceThreshold = 1, DisplayName = "drain" });
        var bp   = manager.AllBreakpoints.First(b => b.Id == bpId);
        manager.OnHit(bp, entity);

        manager.StageMutation(entity, typeof(TestHealth), new TestHealth { Current = 999 });

        var ecb = (EntityCommandBuffer)((ISimulationView)liveRepo).GetCommandBuffer();
        manager.StepAndDrain(liveRepo);          // ⭐ W5: two steps, not one — see ResumeThenDrain
        ecb.Playback(liveRepo);

        Assert.False(manager.IsPaused);
        Assert.Equal(0, manager.PendingMutationsCount);
        Assert.Equal(999, liveRepo.GetComponent<TestHealth>(entity).Current);
    }

    [Fact]
    public void Drain_ManagedPayload_RoutedViaSetManagedRaw()
    {
        ComponentTypeRegistry.Clear();
        var liveRepo        = new EntityRepository();
        var preTickSnapshot = new EntityRepository();
        liveRepo.RegisterManagedComponent<EntityLabel>();

        var entity = liveRepo.CreateEntity();
        var addEcb = new EntityCommandBuffer();
        addEcb.AddManagedComponent(entity, new EntityLabel { Name = "original" });
        addEcb.Playback(liveRepo);

        preTickSnapshot.SyncFrom(liveRepo);

        liveRepo.Tick();
        var mutEcb = new EntityCommandBuffer();
        mutEcb.SetManagedComponentRaw(entity, ComponentTypeRegistry.GetId(typeof(EntityLabel)), new EntityLabel { Name = "post" });
        mutEcb.Playback(liveRepo);

        var tc       = new MockDebugTimeController();
        var provider = new DebugSnapshotProvider(preTickSnapshot);
        var manager  = new DataBreakpointManager(liveRepo, preTickSnapshot, provider, tc);

        var bpId = manager.Add(new Breakpoint { Id = BreakpointId.Invalid, Enabled = true, OccurrenceThreshold = 1, DisplayName = "drain-managed" });
        var bp   = manager.AllBreakpoints.First(b => b.Id == bpId);
        manager.OnHit(bp, entity);

        manager.StageMutation(entity, typeof(EntityLabel), new EntityLabel { Name = "staged" });

        var ecb = (EntityCommandBuffer)((ISimulationView)liveRepo).GetCommandBuffer();
        manager.StepAndDrain(liveRepo);          // ⭐ W5: two steps, not one — see ResumeThenDrain
        ecb.Playback(liveRepo);

        Assert.Equal("staged", ((ISimulationView)liveRepo).GetManagedComponentRO<EntityLabel>(entity).Name);
    }

    [Fact]
    public void Drain_AppliesAtN_Plus_1_BoundaryNotN()
    {
        ComponentTypeRegistry.Clear();
        var liveRepo        = new EntityRepository();
        var preTickSnapshot = new EntityRepository();
        liveRepo.RegisterComponent<TestHealth>();
        preTickSnapshot.RegisterComponent<TestHealth>();

        var entity = liveRepo.CreateEntity();
        liveRepo.AddComponent(entity, new TestHealth { Current = 0 });
        preTickSnapshot.SyncFrom(liveRepo);

        liveRepo.Tick();
        ref var h = ref liveRepo.GetComponentRW<TestHealth>(entity);
        h.Current = 50;

        var tc       = new MockDebugTimeController();
        var provider = new DebugSnapshotProvider(preTickSnapshot);
        var manager  = new DataBreakpointManager(liveRepo, preTickSnapshot, provider, tc);

        var bpId = manager.Add(new Breakpoint { Id = BreakpointId.Invalid, Enabled = true, OccurrenceThreshold = 1, DisplayName = "boundary" });
        var bp   = manager.AllBreakpoints.First(b => b.Id == bpId);
        manager.OnHit(bp, entity);

        // At tick N (after rewind): liveRepo restored to preTick (Current=0)
        Assert.Equal(0, liveRepo.GetComponent<TestHealth>(entity).Current);

        manager.StageMutation(entity, typeof(TestHealth), new TestHealth { Current = 777 });

        // Still at tick N: mutation not yet applied
        Assert.Equal(0, liveRepo.GetComponent<TestHealth>(entity).Current);

        var ecb = (EntityCommandBuffer)((ISimulationView)liveRepo).GetCommandBuffer();

        // ⭐⭐⭐ W5 SPLIT THIS, and the split makes the rail STRONGER: the restore and the drain are now
        //    separately observable, so this can assert that the restore alone does NOT apply the edit.
        manager.RequestStep();
        Assert.Equal(50, liveRepo.GetComponent<TestHealth>(entity).Current);   // restored, not drained
        Assert.Equal(1,  manager.PendingMutationsCount);                        // ⛔ still queued

        ((Fdp.ModuleHost.Abstractions.IStagedWrites)manager).DrainInto(liveRepo);

        ecb.Playback(liveRepo);

        // After Playback: N+1 boundary, staged mutation applied
        Assert.Equal(777, liveRepo.GetComponent<TestHealth>(entity).Current);
    }

    [Fact]
    public void Manager_CastToIMutationInterceptor_StagesToQueue_WhenPaused()
    {
        ComponentTypeRegistry.Clear();
        var (manager, liveRepo, _, _) = ManagerFactory.Create();
        liveRepo.RegisterComponent<TestHealth>();

        // Pause by triggering a hit.
        var entity = liveRepo.CreateEntity();
        liveRepo.AddComponent(entity, new TestHealth { Current = 0 });
        var preTick = manager.PreTickSnapshot;
        preTick.SyncFrom(liveRepo);

        var bpId = manager.Add(new Breakpoint
        {
            Id = BreakpointId.Invalid, Enabled = true,
            OccurrenceThreshold = 1, DisplayName = "iface"
        });
        var bp = manager.AllBreakpoints.First(b => b.Id == bpId);
        manager.OnHit(bp, entity);

        // Use via interface.
        IMutationInterceptor interceptor = manager;
        Assert.True(interceptor.IsPaused);
        interceptor.StageMutation(entity, typeof(TestHealth), new TestHealth { Current = 77 });

        Assert.Equal(1, manager.PendingMutationsCount);
        // Live repo is rewound; original value unchanged by staging.
        Assert.Equal(0, liveRepo.GetComponent<TestHealth>(entity).Current);
    }

    [Fact]
    public void Manager_CastToIMutationInterceptor_IsPaused_FalseWhenRunning()
    {
        ComponentTypeRegistry.Clear();
        var (manager, _, _, _) = ManagerFactory.Create();

        IMutationInterceptor interceptor = manager;
        Assert.False(interceptor.IsPaused);
    }
}
