using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Debug;

namespace Hrot.Blueprints.Tests.Benchmarks;

// Target: < 50ns per call (SC7-13.5 CI gate)
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
public class ProbeOverheadBenchmarks
{
    private Entity _entity;
    private string _nodeId = "";
    private string _pinId  = "";
    private IBlueprintProbeSink _nullSink = NullProbeSink.Instance;
    private IBlueprintProbeSink _sessionSink = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _entity   = new Entity(1, 0);
        _nodeId   = Guid.NewGuid().ToString("D");
        _pinId    = Guid.NewGuid().ToString("D");
        _nullSink = NullProbeSink.Instance;

        // Session for WithBreakpoint_Miss: one breakpoint set for a different node.
        var session = new BlueprintDebugSession(
            new BlueprintRegistry(),
            new BenchmarkSimulationView(),
            new BenchmarkTimeController());
        session.SetBreakpoint(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _sessionSink = session;
    }

    [Benchmark]
    public void OnNodeEnter_NullSink_Overhead()
        => _nullSink.OnNodeEnter(_entity, _nodeId);

    [Benchmark]
    public void OnPinValueChanged_Int_NullSink_Overhead()
        => _nullSink.OnPinValueChanged(_entity, _pinId, 42);

    [Benchmark]
    public void OnNodeEnter_WithBreakpoint_Miss()
        => _sessionSink.OnNodeEnter(_entity, _nodeId);

    // ---- Internal stubs used only inside this benchmark class ----------------

    private sealed class BenchmarkSimulationView : ISimulationView
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

    private sealed class BenchmarkTimeController : IBlueprintTimeController
    {
        public bool PauseWasRequested  { get; private set; }
        public bool IsPausedByDebugger { get; private set; }
        public void RequestPause()        { PauseWasRequested = true; IsPausedByDebugger = true; }
        public void RequestResume()       { IsPausedByDebugger = false; }
        public void RequestStepOneTick()  { }
    }
}
