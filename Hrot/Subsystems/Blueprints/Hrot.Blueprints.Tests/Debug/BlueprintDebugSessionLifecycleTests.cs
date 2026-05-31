using Fdp.Core;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Debug;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Tests.Mocks;

namespace Hrot.Blueprints.Tests.Debug;

/// <summary>
/// Lifecycle contract tests for BlueprintDebugSession (BPF-033).
/// Verifies IsAttached transitions and DebugProbe.Sink routing.
/// </summary>
[Collection("DebugProbe")]
public sealed class BlueprintDebugSessionLifecycleTests : IDisposable
{
    private sealed class StubTimeController : IEngineDebugTimeController
    {
        public bool IsPausedByDebugger => false;
        public void RequestPause()       { }
        public void RequestResume()      { }
        public void RequestStepOneTick() { }
    }

    private readonly EntityRepository _repo = new();
    private readonly MockEntityCommandBuffer _ecb;
    private readonly MockSimulationView _view;
    private readonly BlueprintDebugSession _session;

    public BlueprintDebugSessionLifecycleTests()
    {
        _ecb     = new MockEntityCommandBuffer(_repo);
        _view    = new MockSimulationView(_repo, _ecb);
        _session = new BlueprintDebugSession(new BlueprintRegistry(), _view, new StubTimeController());
    }

    public void Dispose()
    {
        DebugProbe.Sink = NullProbeSink.Instance;
    }

    [Fact]
    public void IsAttached_IsFalse_BeforeAttach()
    {
        Assert.False(_session.IsAttached);
    }

    [Fact]
    public void IsAttached_IsTrue_AfterAttach()
    {
        _session.Attach();
        Assert.True(_session.IsAttached);
    }

    [Fact]
    public void IsAttached_IsFalse_AfterDetach()
    {
        _session.Attach();
        _session.Detach();
        Assert.False(_session.IsAttached);
    }

    [Fact]
    public void Attach_Routes_DebugProbe_To_Session()
    {
        _session.Attach();
        Assert.Same(_session, DebugProbe.Sink);
    }

    [Fact]
    public void Detach_Resets_DebugProbe_To_NullSink()
    {
        _session.Attach();
        _session.Detach();
        Assert.Same(NullProbeSink.Instance, DebugProbe.Sink);
    }
}
