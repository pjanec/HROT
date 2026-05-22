using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Debug;

namespace Hrot.Blueprints.Tests.Debug;

/// <summary>
/// Contract tests for IBlueprintDebugSession / DebugProbe (TASK-DBG-001 SC1-SC5).
/// </summary>
public sealed class DebugSessionInterfaceTests
{
    private static Entity E1 => new Entity(1, 0);

    // ---- SC1: DebugProbe.NodeEnter with null Sink is allocation-free ----------

    [Fact]
    public void NodeEnter_NullSink_DoesNotThrow()
    {
        DebugProbe.Sink = null;
        // Warm-up to compile the call path.
        NodeEnterWarmup(E1, "n1");
        // Second call must not throw.
        NodeEnterWarmup(E1, "n1");
    }

    [Fact]
    public void NodeEnter_NullSink_ZeroAllocation()
    {
        DebugProbe.Sink = null;
        // Warm-up.
        NodeEnterWarmup(E1, "n1");
        long before = GC.GetAllocatedBytesForCurrentThread();
        NodeEnterWarmup(E1, "n1");
        long after = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(0L, after - before);
    }

    // ---- SC2: DebugProbe.PinValueChanged<int> with null Sink is allocation-free -

    [Fact]
    public void PinValueChanged_NullSink_DoesNotThrow()
    {
        DebugProbe.Sink = null;
        // Warm-up.
        PinValueChangedWarmup(E1, "p1", 42);
        PinValueChangedWarmup(E1, "p1", 42);
    }

    [Fact]
    public void PinValueChanged_NullSink_ZeroAllocation()
    {
        DebugProbe.Sink = null;
        // Warm-up.
        PinValueChangedWarmup(E1, "p1", 42);
        long before = GC.GetAllocatedBytesForCurrentThread();
        PinValueChangedWarmup(E1, "p1", 42);
        long after = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(0L, after - before);
    }

    // ---- SC3: Breakpoint wiring calls RequestPause on mock time controller ----

    [Fact]
    public void OnNodeEnter_MatchingBreakpoint_RequestsPause()
    {
        var timeController = new MockTimeController();
        var session = MakeSession(timeController);

        // Register breakpoint using a Guid nodeId.
        var nodeGuid = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        session.SetBreakpoint(Guid.Empty, Guid.Empty, nodeGuid);

        DebugProbe.Sink = session;
        try
        {
            DebugProbe.NodeEnter(E1, nodeGuid.ToString());
        }
        finally
        {
            DebugProbe.Sink = null;
        }

        Assert.True(timeController.PauseWasRequested);
    }

    [Fact]
    public void OnNodeEnter_NoMatchingBreakpoint_DoesNotRequestPause()
    {
        var timeController = new MockTimeController();
        var session = MakeSession(timeController);

        DebugProbe.Sink = session;
        try
        {
            DebugProbe.NodeEnter(E1, "other-node");
        }
        finally
        {
            DebugProbe.Sink = null;
        }

        Assert.False(timeController.PauseWasRequested);
    }

    // ---- SC4: PinValueChanged record has ValueBytes/ValueType, not Value -------

    [Fact]
    public void PinValueChangedRecord_HasValueBytesProperty()
    {
        var record = new PinValueChanged(E1, "pin-Out", new byte[] { 1, 2, 3, 4 }, typeof(int), 0u);
        Assert.NotNull(record.ValueBytes);
        Assert.Equal(typeof(int), record.ValueType);
        // Ensure no property named "Value" exists on the record type.
        var prop = typeof(PinValueChanged).GetProperty("Value");
        Assert.Null(prop);
    }

    // ---- T1: Detach() clears all state and resets probe ----------------------

    [Fact]
    public void Detach_ClearsAllStateAndNullsProbe()
    {
        var timeController = new MockTimeController();
        var session = MakeSession(timeController);

        DebugProbe.Sink = session;
        session.SetBreakpoint(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        session.AddWatch(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "w1", typeof(int));

        session.Detach();

        Assert.Same(NullProbeSink.Instance, DebugProbe.Sink);
        Assert.Equal(0, session.GetBreakpoints().Count);
        Assert.Equal(0, session.GetWatches().Count);
        Assert.False(session.IsPaused);
    }

    // ---- T2: Detach() calls Continue() first if already paused --------------

    [Fact]
    public void Detach_CallsContinue_WhenPaused()
    {
        var timeController = new MockTimeController();
        var session = MakeSession(timeController);

        session.Pause();
        Assert.True(session.IsPaused);

        int resumesBefore = timeController.ResumeCount;
        session.Detach();

        Assert.True(timeController.ResumeCount > resumesBefore);
        Assert.False(session.IsPaused);
    }

    // ---- Helpers ---------------------------------------------------------------

    // [NoInlining] to make the hot path a separate frame (mirrors AllocationFreeTests pattern).
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void NodeEnterWarmup(Entity entity, string nodeId)
        => DebugProbe.NodeEnter(entity, nodeId);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void PinValueChangedWarmup(Entity entity, string pinId, int value)
        => DebugProbe.PinValueChanged(entity, pinId, value);

    private static BlueprintDebugSession MakeSession(IBlueprintTimeController timeController)
        => new BlueprintDebugSession(new BlueprintRegistry(), new StubSimulationView(), timeController);

    // Minimal no-op ISimulationView stub. BlueprintDebugSession does not call view methods yet.
    private sealed class StubSimulationView : ISimulationView
    {
        public uint Tick => 0;
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
}
