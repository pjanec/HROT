using Fdp.Core;
using Hrot.Editor.AiShared.Debug;

namespace Hrot.Editor.AiShared.Tests.Debug;

public sealed class AiDebugSessionBaseTests
{
    // Minimal concrete subclass for testing the base-class contract.
    private sealed class TestSession : AiDebugSessionBase
    {
        public TestSession(AiTracerCoordinator? coordinator = null) : base(coordinator) { }

        protected override void OnContinueImpl() { }
        protected override void OnPauseImpl() { }
        protected override void OnStepOverImpl() { }
        protected override void OnStepIntoImpl() { }
        protected override void OnStepOutImpl() { }
    }

    private static TestSession MakeSession() => new TestSession();

    [Fact]
    public void SetBreakpoint_ReturnsUniqueId()
    {
        var s = MakeSession();
        var id1 = s.SetBreakpoint(Guid.NewGuid(), Guid.NewGuid());
        var id2 = s.SetBreakpoint(Guid.NewGuid(), Guid.NewGuid());
        var id3 = s.SetBreakpoint(Guid.NewGuid(), Guid.NewGuid());

        Assert.NotEqual(id1, id2);
        Assert.NotEqual(id2, id3);
        Assert.NotEqual(id1, id3);
    }

    [Fact]
    public void SetBreakpoint_BreakpointAppearsInGetBreakpoints()
    {
        var s = MakeSession();
        var assetId = Guid.NewGuid();
        var elemId = Guid.NewGuid();

        var id = s.SetBreakpoint(assetId, elemId);

        var bps = s.GetBreakpoints();
        Assert.Single(bps);
        Assert.Equal(id, bps[0].Id);
        Assert.Equal(assetId, bps[0].AssetId);
        Assert.Equal(elemId, bps[0].ElementId);
    }

    [Fact]
    public void ClearBreakpoint_RemovesById()
    {
        var s = MakeSession();
        var id1 = s.SetBreakpoint(Guid.NewGuid(), Guid.NewGuid());
        var id2 = s.SetBreakpoint(Guid.NewGuid(), Guid.NewGuid());

        s.ClearBreakpoint(id1);

        var bps = s.GetBreakpoints();
        Assert.Single(bps);
        Assert.Equal(id2, bps[0].Id);
    }

    [Fact]
    public void ClearAllBreakpoints_EmptiesList()
    {
        var s = MakeSession();
        s.SetBreakpoint(Guid.NewGuid(), Guid.NewGuid());
        s.SetBreakpoint(Guid.NewGuid(), Guid.NewGuid());

        s.ClearAllBreakpoints();

        Assert.Empty(s.GetBreakpoints());
    }

    [Fact]
    public void IsAnyBreakpointActive_TrueWhenAnyEnabled()
    {
        var s = MakeSession();
        s.SetBreakpoint(Guid.NewGuid(), Guid.NewGuid()); // Enabled = true by default

        Assert.True(s.IsAnyBreakpointActive);
    }

    [Fact]
    public void Pause_SetsPausedStateAndFiresEvent()
    {
        var s = MakeSession();
        int eventCount = 0;
        s.OnSessionStateChanged += () => eventCount++;

        s.Pause();

        Assert.True(s.IsPaused);
        Assert.Equal(1, eventCount);
    }

    [Fact]
    public void Continue_ClearsPausedState()
    {
        var s = MakeSession();
        s.Pause();

        s.Continue();

        Assert.False(s.IsPaused);
        Assert.Null(s.PausedAt);
        Assert.Null(s.PausedOnEntity);
    }
}
