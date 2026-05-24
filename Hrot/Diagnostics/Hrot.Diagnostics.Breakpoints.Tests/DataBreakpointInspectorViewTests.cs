using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Hrot.Blueprints.Core.Debug;
using Hrot.Diagnostics.Breakpoints;

namespace Hrot.Diagnostics.Breakpoints.Tests;

// ---------------------------------------------------------------------------
// DataBreakpointInspectorViewTests  (UBP-P3T2)
// ---------------------------------------------------------------------------

/// <summary>
/// Tests verifying that <see cref="IDataBreakpointManager.ActiveView"/> returns
/// the pre-tick snapshot while paused and the live repo after a step.
/// </summary>
[Collection("ComponentRegistry")]
public sealed class DataBreakpointInspectorViewTests
{
    /// <summary>
    /// Creates two repos with divergent state, wires a manager, and triggers a
    /// pause so callers can assert view routing.
    /// liveRepo ends with Value=50f (post-tick); preTickSnapshot has Value=100f.
    /// </summary>
    private static (DataBreakpointManager manager, Entity entity) SetupPausedManager()
    {
        ComponentTypeRegistry.Clear();

        var liveRepo        = new EntityRepository();
        var preTickSnapshot = new EntityRepository();
        liveRepo.RegisterComponent<TestHealthP3>();
        preTickSnapshot.RegisterComponent<TestHealthP3>();

        var entity = liveRepo.CreateEntity();
        liveRepo.AddComponent(entity, new TestHealthP3 { Value = 100f });
        preTickSnapshot.SyncFrom(liveRepo);

        // Advance global version so GetComponentRW detects the dirty chunk.
        liveRepo.Tick();
        ref var h = ref liveRepo.GetComponentRW<TestHealthP3>(entity);
        h.Value = 50f;

        var tc               = new MockDebugTimeController();
        var snapshotProvider = new DebugSnapshotProvider(preTickSnapshot);
        var manager          = new DataBreakpointManager(liveRepo, preTickSnapshot, snapshotProvider, tc);

        var bpId = manager.Add(new Breakpoint
        {
            Id                  = BreakpointId.Invalid,
            Enabled             = true,
            OccurrenceThreshold = 1,
            DisplayName         = "P3T2"
        });
        var registeredBp = manager.AllBreakpoints.First(b => b.Id == bpId);
        manager.OnHit(registeredBp, entity);

        return (manager, entity);
    }

    /// <summary>
    /// While paused, ActiveView must return the pre-tick snapshot (Value=100).
    /// </summary>
    [Fact]
    public void Inspector_DuringPause_ShowsPreTickValues()
    {
        var (manager, entity) = SetupPausedManager();

        Assert.True(manager.IsPaused);
        var view = manager.ActiveView;
        ref readonly var comp = ref view.GetComponentRO<TestHealthP3>(entity);
        Assert.Equal(100f, comp.Value);
    }

    /// <summary>
    /// After RequestStep, ActiveView must return the live repo (Value=50, post-tick).
    /// </summary>
    [Fact]
    public void Inspector_AfterStep_ShowsPostTickValues()
    {
        var (manager, entity) = SetupPausedManager();

        manager.RequestStep();

        Assert.False(manager.IsPaused);
        var view = manager.ActiveView;
        ref readonly var comp = ref view.GetComponentRO<TestHealthP3>(entity);
        Assert.Equal(50f, comp.Value);
    }
}
