using Fdp.Core;
using Fbt;
using FluentAssertions;
using Hrot.BTree.Editor.Debug;
using Hrot.Editor.AiShared.Debug;
using Xunit;

namespace Hrot.BTree.Editor.Tests.Debug;

public class BTreeDebugSessionTests
{
    private static readonly Guid SomeAssetId = Guid.NewGuid();
    private static readonly Entity SomeEntity = new(1, 1);

    private static BTreeNodeExecuted MakeNodeExecuted(uint tick = 1) =>
        new(SomeEntity, SomeAssetId, Guid.NewGuid(), NodeStatus.Success, 1.0f, tick);

    private static BTreeAsyncEvent MakeAsyncEvent(BTreeAsyncPhase phase = BTreeAsyncPhase.Issued) =>
        new(SomeEntity, SomeAssetId, Guid.NewGuid(), requestId: 1, treeVersion: 1, phase, SimulationTime: 1.0f);

    [Fact]
    public void Session_IsAttached_OnConstruction()
    {
        var sut = new BTreeDebugSession();
        sut.IsAttached.Should().BeTrue();
    }

    [Fact]
    public void Session_IsNotPaused_OnConstruction()
    {
        var sut = new BTreeDebugSession();
        sut.IsPaused.Should().BeFalse();
    }

    [Fact]
    public void GetCurrentStateSnapshot_ReturnsNull()
    {
        var sut = new BTreeDebugSession();
        sut.GetCurrentStateSnapshot().Should().BeNull();
    }

    [Fact]
    public void RecordNodeExecuted_AppearsInHistory()
    {
        var sut = new BTreeDebugSession();
        var record = MakeNodeExecuted();

        sut.RecordNodeExecuted(record);

        sut.GetRecentNodeHistory().Should().ContainSingle()
           .Which.Should().Be(record);
    }

    [Fact]
    public void RecordAsyncEvent_AppearsInHistory()
    {
        var sut = new BTreeDebugSession();
        var record = MakeAsyncEvent();

        sut.RecordAsyncEvent(record);

        sut.GetRecentAsyncHistory().Should().ContainSingle()
           .Which.Should().Be(record);
    }

    [Fact]
    public void NodeHistory_Capped_At_200()
    {
        var sut = new BTreeDebugSession();

        for (uint i = 0; i < 201; i++)
            sut.RecordNodeExecuted(MakeNodeExecuted(i));

        sut.GetRecentNodeHistory(200).Should().HaveCount(200);
    }

    [Fact]
    public void AsyncHistory_Capped_At_200()
    {
        var sut = new BTreeDebugSession();

        for (int i = 0; i < 201; i++)
            sut.RecordAsyncEvent(MakeAsyncEvent());

        sut.GetRecentAsyncHistory(200).Should().HaveCount(200);
    }

    [Fact]
    public void GetRecentNodeHistory_RespectsMaxParameter()
    {
        var sut = new BTreeDebugSession();

        for (uint i = 0; i < 10; i++)
            sut.RecordNodeExecuted(MakeNodeExecuted(i));

        sut.GetRecentNodeHistory(5).Should().HaveCount(5);
    }

    [Fact]
    public void GetRecentNodeHistory_LastItems_AreNewest()
    {
        var sut = new BTreeDebugSession();
        var first = MakeNodeExecuted(1);
        var last  = MakeNodeExecuted(2);

        sut.RecordNodeExecuted(first);
        sut.RecordNodeExecuted(last);

        sut.GetRecentNodeHistory(1).Should().ContainSingle()
           .Which.Should().Be(last);
    }

    [Fact]
    public void Detach_ClearsNodeHistory()
    {
        var sut = new BTreeDebugSession();
        sut.RecordNodeExecuted(MakeNodeExecuted());

        sut.Detach();

        sut.GetRecentNodeHistory().Should().BeEmpty();
    }

    [Fact]
    public void Detach_ClearsAsyncHistory()
    {
        var sut = new BTreeDebugSession();
        sut.RecordAsyncEvent(MakeAsyncEvent());

        sut.Detach();

        sut.GetRecentAsyncHistory().Should().BeEmpty();
    }

    [Fact]
    public void RecordNodeExecuted_FiresOnNodeExecutedEvent()
    {
        var sut = new BTreeDebugSession();
        BTreeNodeExecuted? fired = null;
        sut.OnNodeExecuted += e => fired = e;
        var record = MakeNodeExecuted();

        sut.RecordNodeExecuted(record);

        fired.Should().Be(record);
    }

    [Fact]
    public void RecordAsyncEvent_Issued_FiresOnAsyncIssuedEvent()
    {
        var sut = new BTreeDebugSession();
        BTreeAsyncEvent? fired = null;
        sut.OnAsyncIssued += e => fired = e;
        var record = MakeAsyncEvent(BTreeAsyncPhase.Issued);

        sut.RecordAsyncEvent(record);

        fired.Should().Be(record);
    }

    [Fact]
    public void RecordAsyncEvent_Resolved_FiresOnAsyncResolvedEvent()
    {
        var sut = new BTreeDebugSession();
        BTreeAsyncEvent? fired = null;
        sut.OnAsyncResolved += e => fired = e;
        var record = MakeAsyncEvent(BTreeAsyncPhase.Resolved);

        sut.RecordAsyncEvent(record);

        fired.Should().Be(record);
    }

    [Fact]
    public void RecordAsyncEvent_Aborted_FiresOnAsyncAbortedEvent()
    {
        var sut = new BTreeDebugSession();
        BTreeAsyncEvent? fired = null;
        sut.OnAsyncAborted += e => fired = e;
        var record = MakeAsyncEvent(BTreeAsyncPhase.Aborted);

        sut.RecordAsyncEvent(record);

        fired.Should().Be(record);
    }

    [Fact]
    public void RaiseBreakpointHit_SetsPausedState()
    {
        var sut = new BTreeDebugSession();
        var bp  = new Breakpoint(new BreakpointId(1), SomeAssetId, Guid.NewGuid(), 0, true, "bp1");
        var hit = new BTreeBreakpointHit(bp, SomeEntity, NodeStatus.Failure, 2.5f);

        sut.RaiseBreakpointHit(hit);

        sut.IsPaused.Should().BeTrue();
        sut.PausedAt.Should().Be(bp);
        sut.PausedOnEntity.Should().Be(SomeEntity);
    }

    [Fact]
    public void RaiseBreakpointHit_FiresOnBreakpointHitEvent()
    {
        var sut = new BTreeDebugSession();
        BTreeBreakpointHit? fired = null;
        sut.OnBreakpointHit += h => fired = h;
        var bp  = new Breakpoint(new BreakpointId(1), SomeAssetId, Guid.NewGuid(), 0, true, "bp1");
        var hit = new BTreeBreakpointHit(bp, SomeEntity, null, 0f);

        sut.RaiseBreakpointHit(hit);

        fired.Should().Be(hit);
    }

    [Fact]
    public void RaiseBreakpointHit_RaisesSessionStateChanged()
    {
        var sut = new BTreeDebugSession();
        bool eventFired = false;
        sut.OnSessionStateChanged += () => eventFired = true;
        var bp  = new Breakpoint(new BreakpointId(1), SomeAssetId, Guid.NewGuid(), 0, true, "bp1");
        var hit = new BTreeBreakpointHit(bp, SomeEntity, null, 0f);

        sut.RaiseBreakpointHit(hit);

        eventFired.Should().BeTrue();
    }
}
