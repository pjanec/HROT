using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Compiler.Emit;
using Hrot.Blueprints.Core.Debug;

namespace Hrot.Blueprints.Tests.Debug;

/// <summary>
/// Tests for TASK-DBG-006: hot reload interaction edge cases (SC1-SC4).
/// MultiEntityTests covers SC3+SC4 from the task spec; this file covers additional edge cases.
/// </summary>
public sealed class HotReloadInteractionTests
{
    private static readonly Guid AssetIdA = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid AssetIdB = new Guid("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid GraphId1 = new Guid("11111111-1111-1111-1111-111111111111");
    private static readonly Guid NodeId1  = new Guid("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid NodeId2  = new Guid("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid NodeId3  = new Guid("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

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

    private static DebugMap MakeMapWithPin(Guid assetId, Guid pinId, ulong structureHash = 1)
        => new DebugMap
        {
            AssetId       = assetId,
            BlueprintId   = 1,
            StructureHash = structureHash,
            Entries       = Array.Empty<DebugMapEntry>(),
            Pins          = new[]
            {
                new DebugPinInfo(
                    PinId:                 pinId,
                    NodeId:                Guid.NewGuid(),
                    PinName:               "pin",
                    PinDirection:          "Output",
                    PinKind:               "Data",
                    TypeFullName:          "System.Int32",
                    ValueAccessExpression: ""),
            },
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
        public IReadOnlyList<T> ReadManagedEvents<T>() => throw new NotImplementedException();
        public IEntityCommandBuffer GetCommandBuffer() => throw new NotImplementedException();
    }

    // ---- SC1: not paused -- no spurious resume --------------------------------

    /// <summary>
    /// When the session is not paused, OnHotReloadBegin must not call Continue
    /// and must not change IsPaused.
    /// </summary>
    [Fact]
    public void OnHotReloadBegin_WhenNotPaused_DoesNotCallContinue()
    {
        var tc      = new MockTimeController();
        var session = MakeSession(tc);

        Assert.False(session.IsPaused);
        session.OnHotReloadBegin();

        Assert.Equal(0, tc.ResumeCount);
        Assert.False(session.IsPaused);
    }

    // ---- SC2: all watches marked stale ----------------------------------------

    /// <summary>
    /// OnHotReloadBegin must set IsStale = true on every registered watch,
    /// regardless of which asset the watch belongs to.
    /// </summary>
    [Fact]
    public void OnHotReloadBegin_MarksAllWatchesStale()
    {
        var session = MakeSession();
        var pinIdA  = Guid.NewGuid();
        var pinIdB  = Guid.NewGuid();
        session.AddWatch(AssetIdA, GraphId1, pinIdA, "watchA", typeof(int));
        session.AddWatch(AssetIdB, GraphId1, pinIdB, "watchB", typeof(float));

        session.OnHotReloadBegin();

        var watches = session.GetWatches();
        Assert.All(watches, w => Assert.True(w.IsStale));
    }

    // ---- SC3: completed only clears reloaded-asset watches --------------------

    /// <summary>
    /// OnHotReloadCompleted(assetIds) must clear IsStale only for watches belonging
    /// to the listed asset IDs; other watches must remain stale.
    /// A debug map containing the pin must be registered for the watch to be cleared
    /// (BPF-036: cleared only when pin still exists in the new map).
    /// </summary>
    [Fact]
    public void OnHotReloadCompleted_OnlyClears_ReloadedAssetWatches()
    {
        var session = MakeSession();
        var pinIdA  = Guid.NewGuid();
        var pinIdB  = Guid.NewGuid();
        var watchIdA = session.AddWatch(AssetIdA, GraphId1, pinIdA, "watchA", typeof(int));
        var watchIdB = session.AddWatch(AssetIdB, GraphId1, pinIdB, "watchB", typeof(int));

        // Mark both stale via hot reload begin.
        session.OnHotReloadBegin();

        // Register a new debug map for AssetIdA that contains pinIdA (BPF-036).
        session.RegisterDebugMap(MakeMapWithPin(AssetIdA, pinIdA, structureHash: 2));

        // Reload only AssetIdA.
        session.OnHotReloadCompleted(new[] { AssetIdA });

        var watches = session.GetWatches();
        var watchA  = watches.First(w => w.Id == watchIdA);
        var watchB  = watches.First(w => w.Id == watchIdB);
        Assert.False(watchA.IsStale);
        Assert.True(watchB.IsStale);
    }

    // ---- SC4: new map hash clears only same-asset breakpoints ----------------

    /// <summary>
    /// RegisterDebugMap with a new StructureHash must clear breakpoints only for that
    /// asset, leaving breakpoints for other assets intact.
    /// </summary>
    [Fact]
    public void RegisterDebugMap_NewHash_ClearsBreakpointsForThatAsset()
    {
        var session = MakeSession();

        // Register initial map for AssetIdA.
        session.RegisterDebugMap(MakeMap(AssetIdA, structureHash: 1));

        // Set 2 breakpoints for AssetIdA and 1 for AssetIdB.
        session.SetBreakpoint(AssetIdA, GraphId1, NodeId1);
        session.SetBreakpoint(AssetIdA, GraphId1, NodeId2);
        session.SetBreakpoint(AssetIdB, GraphId1, NodeId3);
        Assert.Equal(3, session.GetBreakpoints().Count);

        // Register new map for AssetIdA with different hash -- marks AssetIdA BPs stale (BPF-003).
        session.RegisterDebugMap(MakeMap(AssetIdA, structureHash: 2));

        // BPF-003: breakpoints are marked stale, not removed. All 3 remain, 2 are stale.
        Assert.Equal(3, session.GetBreakpoints().Count);
        var assetABps = session.GetBreakpoints().Where(b => b.AssetId == AssetIdA).ToList();
        Assert.All(assetABps, b => Assert.True(b.IsStale));
        var assetBBp = session.GetBreakpoints().Single(b => b.AssetId == AssetIdB);
        Assert.False(assetBBp.IsStale);
    }

    // ---- BPF-036: pin-existence check for watch staleness clearing -----------

    /// <summary>
    /// BPF-036: After a hot reload, a watch whose pin still exists in the new debug
    /// map must have IsStale cleared.  A watch whose pin was removed from the new map
    /// must remain stale so the UI shows it as frozen rather than falsely live.
    /// </summary>
    [Fact]
    public void OnHotReloadCompleted_WatchForDeletedPin_RemainsStale()
    {
        var session      = MakeSession();
        var existingPin  = Guid.NewGuid();
        var deletedPin   = Guid.NewGuid();
        var watchIdKept  = session.AddWatch(AssetIdA, GraphId1, existingPin, "kept",    typeof(int));
        var watchIdGone  = session.AddWatch(AssetIdA, GraphId1, deletedPin,  "deleted", typeof(int));

        session.OnHotReloadBegin();

        // New debug map contains existingPin but NOT deletedPin.
        session.RegisterDebugMap(MakeMapWithPin(AssetIdA, existingPin, structureHash: 2));
        session.OnHotReloadCompleted(new[] { AssetIdA });

        var watches   = session.GetWatches();
        var watchKept = watches.First(w => w.Id == watchIdKept);
        var watchGone = watches.First(w => w.Id == watchIdGone);

        Assert.False(watchKept.IsStale, "Watch for a surviving pin must be cleared after reload.");
        Assert.True(watchGone.IsStale,  "Watch for a deleted pin must remain stale after reload.");
    }

    /// <summary>
    /// BPF-036: When no debug map has been registered for a reloaded asset,
    /// all watches for that asset must remain stale (no spurious live signal).
    /// </summary>
    [Fact]
    public void OnHotReloadCompleted_NoDebugMapRegistered_AllWatchesRemainStale()
    {
        var session = MakeSession();
        var pinId   = Guid.NewGuid();
        session.AddWatch(AssetIdA, GraphId1, pinId, "w", typeof(int));

        session.OnHotReloadBegin();

        // Do NOT register any debug map for AssetIdA.
        session.OnHotReloadCompleted(new[] { AssetIdA });

        Assert.All(session.GetWatches(), w => Assert.True(w.IsStale));
    }
}
