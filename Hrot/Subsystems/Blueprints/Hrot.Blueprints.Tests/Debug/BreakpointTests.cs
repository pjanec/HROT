using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Compiler.Emit;
using Hrot.Blueprints.Core.Debug;

namespace Hrot.Blueprints.Tests.Debug;

/// <summary>
/// Tests for TASK-DBG-003: breakpoint set/clear, hit-count tracking,
/// re-entrant pause guard, and BreakpointHit event payload (SC1-SC7).
/// </summary>
public sealed class BreakpointTests
{
    private static readonly Guid AssetIdA  = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid GraphId1  = new Guid("11111111-1111-1111-1111-111111111111");
    private static readonly Guid NodeId1   = new Guid("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid NodeId2   = new Guid("dddddddd-dddd-dddd-dddd-dddddddddddd");

    private static Entity E1 => new Entity(1, 0);
    private static Entity E2 => new Entity(2, 0);

    // ---- Helpers ---------------------------------------------------------------

    private static BlueprintDebugSession MakeSession(MockTimeController? tc = null)
        => new BlueprintDebugSession(
            new BlueprintRegistry(),
            new StubSimulationView(),
            tc ?? new MockTimeController());

    private static BlueprintDebugSession MakeSessionWithView(ISimulationView view, MockTimeController tc)
        => new BlueprintDebugSession(new BlueprintRegistry(), view, tc);

    private static DebugMap MakeMap(Guid assetId, ulong structureHash, params DebugMapEntry[] entries)
        => new DebugMap
        {
            AssetId       = assetId,
            BlueprintId   = 1,
            StructureHash = structureHash,
            Entries       = entries,
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

    /// <summary>Configurable simulation view for tick/time verification.</summary>
    private sealed class ConfigurableSimulationView : ISimulationView
    {
        public uint  Tick { get; }
        public float Time { get; }
        public ConfigurableSimulationView(uint tick, float time) { Tick = tick; Time = time; }
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

    // ---- SC1: Re-entrant pause guard ------------------------------------------

    /// <summary>
    /// When a second entity hits the same breakpoint while the session is already paused,
    /// the re-entrant guard must prevent a second RequestPause call.
    /// </summary>
    [Fact]
    public void Breakpoint_FiresOnNodeEntry_RequestsPauseOncePerFrame()
    {
        var tc      = new MockTimeController();
        var session = MakeSession(tc);

        session.SetBreakpoint(AssetIdA, GraphId1, NodeId1);

        // First entity hits the breakpoint.
        ((IBlueprintProbeSink)session).OnNodeEnter(E1, NodeId1.ToString("D"));
        Assert.Equal(1, tc.PauseRequestCount);

        // Second entity hits the same breakpoint while _isPaused == true -- guard fires.
        ((IBlueprintProbeSink)session).OnNodeEnter(E2, NodeId1.ToString("D"));
        Assert.Equal(1, tc.PauseRequestCount); // must still be 1, not 2
    }

    // ---- SC2: Continue clears paused state ------------------------------------

    /// <summary>
    /// Continue() must call RequestResume, clear IsPaused, and clear PausedAt.
    /// </summary>
    [Fact]
    public void Continue_CallsRequestResume_ClearsPausedState()
    {
        var tc      = new MockTimeController();
        var session = MakeSession(tc);

        session.SetBreakpoint(AssetIdA, GraphId1, NodeId1);
        ((IBlueprintProbeSink)session).OnNodeEnter(E1, NodeId1.ToString("D"));

        Assert.True(session.IsPaused);
        Assert.Equal(1, tc.PauseRequestCount);

        session.Continue();

        Assert.Equal(1, tc.ResumeCount);
        Assert.False(session.IsPaused);
        Assert.Null(session.PausedAt);
    }

    // ---- SC3: Hit count increments per hit ------------------------------------

    /// <summary>
    /// HitCount on the Breakpoint record must increment on each actual hit
    /// (interleaved with Continue() to re-arm the re-entrant guard).
    /// </summary>
    [Fact]
    public void Breakpoint_HitCount_IncreasesOnEachHit()
    {
        var session = MakeSession();

        session.SetBreakpoint(AssetIdA, GraphId1, NodeId1);

        ((IBlueprintProbeSink)session).OnNodeEnter(E1, NodeId1.ToString("D"));
        session.Continue();

        ((IBlueprintProbeSink)session).OnNodeEnter(E1, NodeId1.ToString("D"));
        session.Continue();

        ((IBlueprintProbeSink)session).OnNodeEnter(E1, NodeId1.ToString("D"));

        Assert.Equal(3, session.GetBreakpoints()[0].HitCount);
    }

    // ---- SC4: ClearBreakpoint removes from session ----------------------------

    /// <summary>
    /// After ClearBreakpoint, OnNodeEnter for that node must not request a pause.
    /// </summary>
    [Fact]
    public void ClearBreakpoint_RemovesFromSession()
    {
        var tc      = new MockTimeController();
        var session = MakeSession(tc);

        var id = session.SetBreakpoint(AssetIdA, GraphId1, NodeId1);
        session.ClearBreakpoint(id);

        ((IBlueprintProbeSink)session).OnNodeEnter(E1, NodeId1.ToString("D"));

        Assert.Equal(0, tc.PauseRequestCount);
    }

    // ---- SC5: ClearAllBreakpoints removes all ---------------------------------

    /// <summary>
    /// ClearAllBreakpoints must leave IsAnyBreakpointActive as false.
    /// </summary>
    [Fact]
    public void ClearAllBreakpoints_RemovesAll()
    {
        var session = MakeSession();

        session.SetBreakpoint(AssetIdA, GraphId1, NodeId1);
        session.SetBreakpoint(AssetIdA, GraphId1, NodeId2);

        Assert.True(session.IsAnyBreakpointActive);

        session.ClearAllBreakpoints();

        Assert.False(session.IsAnyBreakpointActive);
    }

    // ---- SC6: Structure-hash mismatch marks breakpoints stale (BPF-003) ------

    /// <summary>
    /// Registering a map with a different structure hash must mark breakpoints for
    /// that asset as stale (not clear them) and fire OnBreakpointListChanged.
    /// </summary>
    [Fact]
    public void StructureHashMismatch_ClearsBreakpoints()
    {
        var session = MakeSession();

        var mapV1 = MakeMap(AssetIdA, 0x1111ul,
            new DebugMapEntry(NodeId1, GraphId1, 1, 2));
        session.RegisterDebugMap(mapV1);
        session.SetBreakpoint(AssetIdA, GraphId1, NodeId1);
        Assert.True(session.IsAnyBreakpointActive);

        Guid? firedId = null;
        session.OnBreakpointListChanged += id => firedId = id;

        var mapV2 = MakeMap(AssetIdA, 0x2222ul,
            new DebugMapEntry(NodeId1, GraphId1, 1, 2));
        session.RegisterDebugMap(mapV2);

        // BPF-003: breakpoints are marked stale, not cleared.
        Assert.True(session.IsAnyBreakpointActive);
        Assert.True(session.GetBreakpoints().All(b => b.IsStale));
        Assert.Equal(AssetIdA, firedId);
    }

    // ---- SC7: BreakpointHit carries correct entity, tick, and sim-time --------

    /// <summary>
    /// The BreakpointHit event payload must reflect the exact Tick, SimulationTime,
    /// and Self values from the view and probe call.
    /// </summary>
    [Fact]
    public void HandleBreakpointHit_RecordsCorrectSelf_And_Tick()
    {
        var view    = new ConfigurableSimulationView(tick: 42u, time: 1.5f);
        var tc      = new MockTimeController();
        var session = MakeSessionWithView(view, tc);

        session.SetBreakpoint(AssetIdA, GraphId1, NodeId1);

        BreakpointHit? received = null;
        session.OnBreakpointHit += hit => received = hit;

        ((IBlueprintProbeSink)session).OnNodeEnter(E1, NodeId1.ToString("D"));

        Assert.NotNull(received);
        Assert.Equal(E1,   received!.Self);
        Assert.Equal(42u,  received.Tick);
        Assert.Equal(1.5f, received.SimulationTime);
    }
}
