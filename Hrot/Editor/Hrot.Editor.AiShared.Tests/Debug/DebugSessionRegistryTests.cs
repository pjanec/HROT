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

    // Minimal IAiDebugSession fake that counts Detach calls.
    // Used to verify that SetActiveSession(null) does NOT call Detach() (unlike ReleaseSession).
    private sealed class DetachCountingFake : IAiDebugSession
    {
        public int DetachCallCount { get; private set; }
        public bool IsAttached { get; set; } = true;
        public bool IsPaused => false;
        public Breakpoint? PausedAt => null;
        public Entity? PausedOnEntity => null;
        public bool IsAnyBreakpointActive => false;
        private Action? _onSessionStateChanged;
        event Action? IAiDebugSession.OnSessionStateChanged
        {
            add => _onSessionStateChanged += value;
            remove => _onSessionStateChanged -= value;
        }

        public void Detach() { DetachCallCount++; IsAttached = false; }
        public BreakpointId SetBreakpoint(Guid assetId, Guid elementId) => default;
        public void ClearBreakpoint(BreakpointId id) { }
        public void ClearAllBreakpoints() { }
        public IReadOnlyList<Breakpoint> GetBreakpoints() => Array.Empty<Breakpoint>();
        public void Continue() { }
        public void Pause() { }
        public void StepOver() { }
        public void StepInto() { }
        public void StepOut() { }

        // IAiTraceObserver stubs.
        public void BeginObservingAsset(Guid assetId, TraceLevel level) { }
        public void EndObservingAsset(Guid assetId) { }
        public IReadOnlyList<Entity> GetActiveEntities(Guid assetId) => Array.Empty<Entity>();
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

    // ── SetActiveSession tests ────────────────────────────────────────────────

    [Fact]
    public void SetActiveSession_SetsActiveSessionAndFiresChanged()
    {
        var r = MakeRegistry();
        var session = new SessionA();

        int changedCount = 0;
        r.Changed += () => changedCount++;

        r.SetActiveSession(session);

        Assert.Same(session, r.ActiveSession);
        Assert.Equal(1, changedCount);
    }

    [Fact]
    public void SetActiveSession_NullAfterSet_ClearsAndFiresChanged()
    {
        var r = MakeRegistry();
        var session = new SessionA();
        r.SetActiveSession(session);

        int changedCount = 0;
        r.Changed += () => changedCount++;

        r.SetActiveSession(null);

        Assert.Null(r.ActiveSession);
        Assert.Equal(1, changedCount);
    }

    [Fact]
    public void SetActiveSession_SameReferenceTwice_FiresChangedOnlyOnce()
    {
        var r = MakeRegistry();
        var session = new SessionA();
        r.SetActiveSession(session);

        int changedCount = 0;
        r.Changed += () => changedCount++;

        r.SetActiveSession(session); // same reference — no change

        Assert.Same(session, r.ActiveSession);
        Assert.Equal(0, changedCount); // no redundant event
    }

    [Fact]
    public void SetActiveSession_Null_DoesNotCallDetach()
    {
        var r = new DebugSessionRegistry();
        var session = new DetachCountingFake();
        Assert.True(session.IsAttached);
        Assert.Equal(0, session.DetachCallCount);

        r.SetActiveSession(session);
        r.SetActiveSession(null);

        // SetActiveSession must NOT call Detach — unlike ReleaseSession which does.
        Assert.Equal(0, session.DetachCallCount);
        Assert.True(session.IsAttached); // still attached
        Assert.Null(r.ActiveSession);
    }
}
