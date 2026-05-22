using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Debug;

namespace Hrot.Blueprints.Tests.Debug;

/// <summary>
/// Tests for TASK-DBG-006: per-entity execution history ring buffer (SC1-SC4).
/// </summary>
public sealed class NodeHistoryTests
{
    private static Entity E1 => new Entity(1, 0);
    private static Entity E2 => new Entity(2, 0);

    // ---- Helpers ---------------------------------------------------------------

    private static BlueprintDebugSession MakeSession(ISimulationView? view = null)
        => new BlueprintDebugSession(
            new BlueprintRegistry(),
            view ?? new StubSimulationView(),
            new MockTimeController());

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
        public IReadOnlyList<T> ReadManagedEvents<T>() => throw new NotImplementedException();
        public IEntityCommandBuffer GetCommandBuffer() => throw new NotImplementedException();
    }

    // ---- SC1: records tick and simtime ----------------------------------------

    /// <summary>
    /// OnNodeEnter must record the current tick and simulation time in the history entry.
    /// </summary>
    [Fact]
    public void OnNodeEnter_RecordsHistoryEntry_WithCorrectFields()
    {
        var view    = new ConfigurableSimulationView(tick: 42, time: 1.5f);
        var session = MakeSession(view);

        ((IBlueprintProbeSink)session).OnNodeEnter(E1, "some-node-id");

        var history = session.GetNodeHistory(E1, maxCount: 10);
        Assert.Equal(1, history.Count);
        Assert.Equal("some-node-id", history[0].NodeId);
        Assert.Equal(42u, history[0].Tick);
        Assert.Equal(1.5f, history[0].SimTime);
    }

    // ---- SC2: entity histories are isolated ------------------------------------

    /// <summary>
    /// Entries for E1 must not appear in E2's history and vice versa.
    /// </summary>
    [Fact]
    public void GetNodeHistory_EntitiesAreIsolated()
    {
        var session = MakeSession();

        ((IBlueprintProbeSink)session).OnNodeEnter(E1, "node-a");
        ((IBlueprintProbeSink)session).OnNodeEnter(E1, "node-a");
        ((IBlueprintProbeSink)session).OnNodeEnter(E2, "node-b");

        var e1History = session.GetNodeHistory(E1);
        var e2History = session.GetNodeHistory(E2);

        Assert.Equal(2, e1History.Count);
        Assert.Equal(1, e2History.Count);
        Assert.DoesNotContain(e1History, h => h.NodeId == "node-b");
        Assert.DoesNotContain(e2History, h => h.NodeId == "node-a");
    }

    // ---- SC3: ring buffer wraps at 256 ----------------------------------------

    /// <summary>
    /// Recording 260 entries into a 256-capacity ring buffer must return exactly 256 entries.
    /// The oldest surviving entry must be #5 (entries 1-4 are overwritten); the last is #260.
    /// </summary>
    [Fact]
    public void ExecutionHistory_RingBuffer_WrapsAt256()
    {
        var session = MakeSession();

        for (int i = 1; i <= 260; i++)
            ((IBlueprintProbeSink)session).OnNodeEnter(E1, $"node-{i:D3}");

        var history = session.GetNodeHistory(E1, 500);

        Assert.Equal(256, history.Count);
        Assert.Equal("node-005", history[0].NodeId);
        Assert.Equal("node-260", history[history.Count - 1].NodeId);
    }

    // ---- SC4: maxCount limits result ------------------------------------------

    /// <summary>
    /// GetNodeHistory with maxCount: 10 must return only the 10 most recent entries
    /// when more than 10 are recorded.
    /// </summary>
    [Fact]
    public void GetNodeHistory_MaxCount_LimitsResult()
    {
        var session = MakeSession();

        for (int i = 0; i < 100; i++)
            ((IBlueprintProbeSink)session).OnNodeEnter(E1, $"entry-{i}");

        var history = session.GetNodeHistory(E1, maxCount: 10);

        Assert.Equal(10, history.Count);
    }
}
