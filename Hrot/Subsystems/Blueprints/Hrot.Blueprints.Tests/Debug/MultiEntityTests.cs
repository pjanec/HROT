using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Compiler.Emit;
using Hrot.Blueprints.Core.Debug;

namespace Hrot.Blueprints.Tests.Debug;

/// <summary>
/// Tests for TASK-DBG-005: entity filter, active entity tracking, hot reload interaction (SC1-SC4).
/// </summary>
public sealed class MultiEntityTests
{
    private static readonly Guid AssetIdA = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid GraphId1 = new Guid("11111111-1111-1111-1111-111111111111");
    private static readonly Guid NodeId1  = new Guid("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private static Entity E1 => new Entity(1, 0);
    private static Entity E2 => new Entity(2, 0);

    // ---- Helpers ---------------------------------------------------------------

    private static BlueprintDebugSession MakeSession(MockTimeController? tc = null)
        => new BlueprintDebugSession(
            new BlueprintRegistry(),
            new StubSimulationView(),
            tc ?? new MockTimeController());

    private static DebugMap MakeMap(Guid assetId, ulong structureHash)
        => new DebugMap
        {
            AssetId       = assetId,
            BlueprintId   = 1,
            StructureHash = structureHash,
            Entries       = Array.Empty<DebugMapEntry>(),
        };

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
        public ReadOnlySpan<T> ReadEvents<T>() where T : unmanaged => throw new NotImplementedException();
        public QueryBuilder Query() => throw new NotImplementedException();
        public System.Collections.Generic.IReadOnlyList<T> ReadManagedEvents<T>()
            => throw new NotImplementedException();
        public IEntityCommandBuffer GetCommandBuffer() => throw new NotImplementedException();
    }

    // ---- SC1: entity filter blocks non-matching entity -----------------------

    /// <summary>
    /// When an entity filter is set to E1, OnNodeEnter from E2 must not trigger a breakpoint pause.
    /// </summary>
    [Fact]
    public void EntityFilter_Set_SkipsNonMatchingEntity_OnBreakpoint()
    {
        var session = MakeSession();
        session.SetBreakpoint(AssetIdA, GraphId1, NodeId1);
        session.SetEntityFilter(E1);

        ((IBlueprintProbeSink)session).OnNodeEnter(E2, NodeId1.ToString("D"));

        Assert.False(session.IsPaused);
    }

    // ---- SC2: entity filter allows matching entity ---------------------------

    /// <summary>
    /// When an entity filter is set to E1, OnNodeEnter from E1 must trigger a breakpoint pause.
    /// </summary>
    [Fact]
    public void EntityFilter_Set_PausesMatchingEntity()
    {
        var session = MakeSession();
        session.SetBreakpoint(AssetIdA, GraphId1, NodeId1);
        session.SetEntityFilter(E1);

        ((IBlueprintProbeSink)session).OnNodeEnter(E1, NodeId1.ToString("D"));

        Assert.True(session.IsPaused);
    }

    // ---- SC3: OnHotReloadBegin when paused calls Continue --------------------

    /// <summary>
    /// OnHotReloadBegin while the session is paused must call Continue (resume),
    /// leaving IsPaused == false and incrementing MockTimeController.ResumeCount.
    /// </summary>
    [Fact]
    public void OnHotReloadBegin_WhenPaused_CallsContinue()
    {
        var tc      = new MockTimeController();
        var session = MakeSession(tc);
        session.SetBreakpoint(AssetIdA, GraphId1, NodeId1);

        // Hit the breakpoint to enter paused state.
        ((IBlueprintProbeSink)session).OnNodeEnter(E1, NodeId1.ToString("D"));
        Assert.True(session.IsPaused);

        session.OnHotReloadBegin();

        Assert.False(session.IsPaused);
        Assert.Equal(1, tc.ResumeCount);
    }

    // ---- SC4: OnHotReloadCompleted clears stale watches ----------------------

    /// <summary>
    /// OnHotReloadCompleted for an asset must clear the stale flag on watches for that asset.
    /// </summary>
    [Fact]
    public void OnHotReloadCompleted_ClearsStalWatchesAsValid()
    {
        var session = MakeSession();
        var pinId   = Guid.NewGuid();
        session.AddWatch(AssetIdA, GraphId1, pinId, "val", typeof(int));

        // Register map v1 then v2 to trigger stale flag via hash mismatch.
        session.RegisterDebugMap(MakeMap(AssetIdA, 0x1111));
        session.RegisterDebugMap(MakeMap(AssetIdA, 0x2222));

        var watch = session.GetWatches()[0];
        Assert.True(watch.IsStale);

        // Simulate hot reload completing for AssetIdA.
        session.OnHotReloadCompleted(new[] { AssetIdA });

        Assert.False(watch.IsStale);
    }
}
