using Fdp.Core;
using Hrot.Editor.AiShared.Debug;
using Hrot.Hsm.Editor.Debug;
using FluentAssertions;
using Xunit;

namespace Hrot.Hsm.Editor.Tests.Debug;

public sealed class HsmDebugSessionTests
{
    private static Entity MakeEntity() => new Entity(1, 1);

    private static Guid AssetId => Guid.Parse("A1A1A1A1-0000-0000-0000-000000000001");

    private static HsmStateEntered MakeEnteredRecord(float t = 0f) =>
        new(MakeEntity(), AssetId, Guid.NewGuid(), t);

    private static HsmTransitionFired MakeFiredRecord(float t = 0f) =>
        new(MakeEntity(), AssetId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            EventId: 1, GuardResult: true, SyncGroupId: 0, SimulationTime: t);

    // -------------------------------------------------------------------------

    [Fact]
    public void Session_IsAttached_OnConstruction()
    {
        var session = new HsmDebugSession();
        session.IsAttached.Should().BeTrue();
    }

    [Fact]
    public void Session_IsNotPaused_OnConstruction()
    {
        var session = new HsmDebugSession();
        session.IsPaused.Should().BeFalse();
    }

    [Fact]
    public void GetCurrentStateSnapshot_ReturnsNull()
    {
        var session = new HsmDebugSession();
        session.GetCurrentStateSnapshot().Should().BeNull();
    }

    [Fact]
    public void RecordTrace_StateEntered_AppearsInHistory()
    {
        var session = new HsmDebugSession();
        var record = MakeEnteredRecord(1.0f);

        session.RecordTrace(record);

        var history = session.GetRecentTraceHistory();
        history.Should().ContainSingle().Which.Should().Be(record);
    }

    [Fact]
    public void RecordTrace_TransitionFired_AppearsInHistory()
    {
        var session = new HsmDebugSession();
        var record = MakeFiredRecord(2.0f);

        session.RecordTrace(record);

        var history = session.GetRecentTraceHistory();
        history.Should().ContainSingle().Which.Should().Be(record);
    }

    [Fact]
    public void TraceHistory_CappedAt200()
    {
        var session = new HsmDebugSession();

        for (int i = 0; i < 250; i++)
            session.RecordTrace(MakeEnteredRecord(i));

        session.GetRecentTraceHistory(int.MaxValue).Count.Should().Be(200);
    }

    [Fact]
    public void GetRecentTraceHistory_RespectsMaxParameter()
    {
        var session = new HsmDebugSession();

        for (int i = 0; i < 50; i++)
            session.RecordTrace(MakeEnteredRecord(i));

        session.GetRecentTraceHistory(10).Count.Should().Be(10);
    }

    [Fact]
    public void GetRecentTraceHistory_ReturnsNewestRecords()
    {
        var session = new HsmDebugSession();

        for (int i = 0; i < 20; i++)
            session.RecordTrace(MakeEnteredRecord(i));

        var recent = session.GetRecentTraceHistory(5);
        recent.Select(r => r.SimulationTime).Should().Equal(15f, 16f, 17f, 18f, 19f);
    }

    [Fact]
    public void Detach_ClearsHistory()
    {
        var session = new HsmDebugSession();
        session.RecordTrace(MakeEnteredRecord());

        session.Detach();

        session.GetRecentTraceHistory().Should().BeEmpty();
    }

    [Fact]
    public void RecordTrace_StateEntered_FiresOnStateEnteredEvent()
    {
        var session = new HsmDebugSession();
        HsmStateEntered? received = null;
        session.OnStateEntered += e => received = e;

        var record = MakeEnteredRecord();
        session.RecordTrace(record);

        received.Should().Be(record);
    }

    [Fact]
    public void RecordTrace_TransitionFired_FiresOnTransitionFiredEvent()
    {
        var session = new HsmDebugSession();
        HsmTransitionFired? received = null;
        session.OnTransitionFired += e => received = e;

        var record = MakeFiredRecord();
        session.RecordTrace(record);

        received.Should().Be(record);
    }

    [Fact]
    public void RaiseBreakpointHit_SetsPausedState()
    {
        var session = new HsmDebugSession();
        var bp = new Breakpoint(new BreakpointId(1), AssetId, Guid.NewGuid(),
            HitCount: 0, Enabled: true, DisplayName: "test");
        var hit = new HsmBreakpointHit(bp, MakeEntity(), Guid.NewGuid(), null, 1.5f);

        session.RaiseBreakpointHit(hit);

        session.IsPaused.Should().BeTrue();
        session.PausedAt.Should().Be(bp);
        session.PausedOnEntity.Should().Be(hit.Self);
    }

    [Fact]
    public void RaiseBreakpointHit_FiresOnBreakpointHitEvent()
    {
        var session = new HsmDebugSession();
        HsmBreakpointHit? received = null;
        session.OnBreakpointHit += h => received = h;

        var bp = new Breakpoint(new BreakpointId(2), AssetId, Guid.NewGuid(),
            HitCount: 0, Enabled: true, DisplayName: "test");
        var hit = new HsmBreakpointHit(bp, MakeEntity(), Guid.NewGuid(), null, 2.0f);

        session.RaiseBreakpointHit(hit);

        received.Should().Be(hit);
    }

    [Fact]
    public void RaiseBreakpointHit_RaisesSessionStateChanged()
    {
        var session = new HsmDebugSession();
        int callCount = 0;
        session.OnSessionStateChanged += () => callCount++;

        var bp = new Breakpoint(new BreakpointId(3), AssetId, Guid.NewGuid(),
            HitCount: 0, Enabled: true, DisplayName: "test");
        var hit = new HsmBreakpointHit(bp, MakeEntity(), null, Guid.NewGuid(), 3.0f);

        session.RaiseBreakpointHit(hit);

        callCount.Should().Be(1);
    }

    [Fact]
    public void Pause_SetsPausedTrue()
    {
        var session = new HsmDebugSession();
        session.Pause();
        session.IsPaused.Should().BeTrue();
    }

    [Fact]
    public void Continue_ClearsPausedState()
    {
        var session = new HsmDebugSession();
        session.Pause();
        session.Continue();
        session.IsPaused.Should().BeFalse();
        session.PausedAt.Should().BeNull();
        session.PausedOnEntity.Should().BeNull();
    }

    [Fact]
    public void StepOver_FiresSessionStateChanged()
    {
        var session = new HsmDebugSession();
        int count = 0;
        session.OnSessionStateChanged += () => count++;
        session.StepOver();
        count.Should().Be(1);
    }

    [Fact]
    public void StepInto_FiresSessionStateChanged()
    {
        var session = new HsmDebugSession();
        int count = 0;
        session.OnSessionStateChanged += () => count++;
        session.StepInto();
        count.Should().Be(1);
    }

    [Fact]
    public void StepOut_FiresSessionStateChanged()
    {
        var session = new HsmDebugSession();
        int count = 0;
        session.OnSessionStateChanged += () => count++;
        session.StepOut();
        count.Should().Be(1);
    }
}
