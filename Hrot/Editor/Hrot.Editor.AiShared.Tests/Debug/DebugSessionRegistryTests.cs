using Fdp.Core;
using Hrot.Editor.AiShared.Debug;

namespace Hrot.Editor.AiShared.Tests.Debug;

public sealed class DebugSessionRegistryTests
{
    // Minimal concrete session for registry tests.
    private sealed class SessionA : AiDebugSessionBase
    {
        protected override void OnContinueImpl() { }
        protected override void OnPauseImpl() { }
        protected override void OnStepOverImpl() { }
        protected override void OnStepIntoImpl() { }
        protected override void OnStepOutImpl() { }
    }

    // A second distinct session type to test type-exclusivity.
    private sealed class SessionB : AiDebugSessionBase
    {
        protected override void OnContinueImpl() { }
        protected override void OnPauseImpl() { }
        protected override void OnStepOverImpl() { }
        protected override void OnStepIntoImpl() { }
        protected override void OnStepOutImpl() { }
    }

    // Minimal trace observer for observer registration tests.
    private sealed class StubObserver : IAiTraceObserver
    {
        public void BeginObservingAsset(Guid assetId, TraceLevel level) { }
        public void EndObservingAsset(Guid assetId) { }
        public IReadOnlyList<Entity> GetActiveEntities(Guid assetId) => Array.Empty<Entity>();
    }

    private static DebugSessionRegistry MakeRegistry()
    {
        var r = new DebugSessionRegistry();
        r.RegisterSessionFactory<SessionA>(() => new SessionA());
        r.RegisterSessionFactory<SessionB>(() => new SessionB());
        return r;
    }

    [Fact]
    public void ActiveObservers_IsEmpty_Initially()
    {
        var r = new DebugSessionRegistry();
        Assert.Empty(r.ActiveObservers);
    }

    [Fact]
    public void TryAcquireSession_WhenNoSession_ReturnsTrue()
    {
        var r = MakeRegistry();
        var result = r.TryAcquireSession<SessionA>(out var session);

        Assert.True(result);
        Assert.NotNull(session);
        Assert.Same(r.ActiveSession, session);
    }

    [Fact]
    public void TryAcquireSession_WhenSessionAlreadyActive_ReturnsFalse()
    {
        var r = MakeRegistry();
        r.TryAcquireSession<SessionA>(out _);

        var result = r.TryAcquireSession<SessionA>(out var second);

        Assert.False(result);
        Assert.Null(second);
    }

    [Fact]
    public void TryAcquireSession_WhenSessionAlreadyActive_OtherTypeAlsoFails()
    {
        var r = MakeRegistry();
        r.TryAcquireSession<SessionA>(out _);

        // Different type -- must still fail while any session is active.
        var result = r.TryAcquireSession<SessionB>(out var session);

        Assert.False(result);
        Assert.Null(session);
    }

    [Fact]
    public void ReleaseSession_ClearsActiveSession()
    {
        var r = MakeRegistry();
        r.TryAcquireSession<SessionA>(out var session);
        r.ReleaseSession(session!);

        Assert.Null(r.ActiveSession);
    }

    [Fact]
    public void ReleaseSession_FiresChanged()
    {
        var r = MakeRegistry();
        r.TryAcquireSession<SessionA>(out var session);

        int count = 0;
        r.Changed += () => count++;

        r.ReleaseSession(session!);

        Assert.Equal(1, count);
    }

    [Fact]
    public void ReleaseSession_CallsDetach()
    {
        var r = MakeRegistry();
        r.TryAcquireSession<SessionA>(out var session);

        r.ReleaseSession(session!);

        Assert.False(session!.IsAttached);
    }

    [Fact]
    public void RegisterObserver_AddsToActiveObservers()
    {
        var r = new DebugSessionRegistry();
        var observer = new StubObserver();

        r.RegisterObserver(observer);

        Assert.Single(r.ActiveObservers);
        Assert.Same(observer, r.ActiveObservers[0]);
    }

    [Fact]
    public void RegisterObserver_DisposedToken_RemovesObserver()
    {
        var r = new DebugSessionRegistry();
        var observer = new StubObserver();

        var token = r.RegisterObserver(observer);
        token.Dispose();

        Assert.Empty(r.ActiveObservers);
    }
}
