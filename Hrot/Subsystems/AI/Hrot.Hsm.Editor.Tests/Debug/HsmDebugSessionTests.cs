using Fdp.Core;
using Fhsm.Kernel.Data;
using FluentAssertions;
using Hrot.Editor.AiShared.Debug;
using Hrot.Hsm.Editor.Debug;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;
using System.Runtime.CompilerServices;
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

    [Fact]
    public void HeatmapMode_Off_RecordTrace_DoesNotIncrementCounters()
    {
        var session = new HsmDebugSession();
        var assetId = Guid.NewGuid();
        var record = new HsmStateEntered(
            new Entity(1, 1), assetId, Guid.NewGuid(), 0f);
        session.RecordTrace(record);
        session.GetStateEntryCounts(assetId).Should().BeNull();
    }

    [Fact]
    public void HeatmapMode_On_RecordTrace_StateEntered_IncrementsCounter()
    {
        var session = new HsmDebugSession();
        var stableId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        var record = new HsmStateEntered(
            new Entity(1, 1), assetId, stableId, 0f);
        session.HeatmapModeActive = true;
        session.RecordTrace(record);
        session.RecordTrace(record);
        var counts = session.GetStateEntryCounts(assetId);
        counts.Should().NotBeNull();
        counts![stableId].Should().Be(2);
    }

    [Fact]
    public void ResetStateEntryCounts_ClearsAll()
    {
        var session = new HsmDebugSession();
        var stableId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        var record = new HsmStateEntered(
            new Entity(1, 1), assetId, stableId, 0f);
        session.HeatmapModeActive = true;
        session.RecordTrace(record);
        session.ResetStateEntryCounts();
        session.GetStateEntryCounts(assetId)!.Should().BeEmpty();
    }

    [Fact]
    public void GetStateEntryCounts_NotAttached_ReturnsNull()
    {
        var session = new HsmDebugSession();
        session.HeatmapModeActive = true;
        session.Detach();
        session.GetStateEntryCounts(Guid.NewGuid()).Should().BeNull();
    }

    // ---- ECS Update() tests -----------------------------------------------

    private static EntityRepository CreateWorld()
    {
        var world = new EntityRepository();
        world.RegisterComponent<BrainHsm64>();
        world.RegisterComponent<BrainHsm128>();
        world.RegisterComponent<HsmTraceWorkingMemory1024>();
        return world;
    }

    [Fact]
    public void Update_WithNoBrainHsm_SnapshotRemainsNull()
    {
        var world  = CreateWorld();
        var entity = world.CreateEntity();
        var sut    = new HsmDebugSession();

        sut.Update(world, entity);

        sut.GetCurrentStateSnapshot().Should().BeNull();
    }

    [Fact]
    public void Update_WithBrainHsm64_SnapshotIsNotNull()
    {
        var world  = CreateWorld();
        var entity = world.CreateEntity();
        var brain  = new BrainHsm64();
        brain.State.Header.Phase = InstancePhase.Activity;
        world.AddComponent(entity, brain);
        var sut = new HsmDebugSession();

        sut.Update(world, entity);

        sut.GetCurrentStateSnapshot().Should().NotBeNull();
    }

    [Fact]
    public void Update_WithBrainHsm64_SnapshotHasCorrectPhase()
    {
        var world  = CreateWorld();
        var entity = world.CreateEntity();
        var brain  = new BrainHsm64();
        brain.State.Header.Phase = InstancePhase.Activity;
        world.AddComponent(entity, brain);
        var sut = new HsmDebugSession();

        sut.Update(world, entity);

        sut.GetCurrentStateSnapshot()!.Phase.Should().Be(InstancePhase.Activity);
    }

    [Fact]
    public unsafe void Update_WithHsmTraceBuffer_PopulatesTraceHistory()
    {
        var world  = CreateWorld();
        var entity = world.CreateEntity();
        var brain  = new BrainHsm64();
        brain.State.Header.Phase = InstancePhase.Idle;
        world.AddComponent(entity, brain);

        var mem = new HsmTraceWorkingMemory1024();
        mem.LastInstanceId = 1;
        // Write 3 StateEnter headers manually into the ring buffer.
        HsmTraceWorkingMemory1024* memPtr =
            (HsmTraceWorkingMemory1024*)Unsafe.AsPointer(ref mem);
        for (int i = 0; i < 3; i++)
        {
            var hdr = (TraceRecordHeader*)(memPtr->Buffer + mem.WritePos);
            *hdr = default;
            hdr->OpCode     = TraceOpCode.StateEnter;
            hdr->Timestamp  = (ushort)(i + 1);
            hdr->InstanceId = 1;
            mem.WritePos = (ushort)((mem.WritePos + HsmTraceWorkingMemory1024.RecordStride)
                                     % HsmTraceWorkingMemory1024.PayloadBytes);
            if (mem.RecordCount < HsmTraceWorkingMemory1024.CapacityRecords)
                mem.RecordCount++;
        }
        world.AddComponent(entity, mem);
        var sut = new HsmDebugSession();

        sut.Update(world, entity);

        sut.GetRecentTraceHistory(10).Should().HaveCount(3);
    }
}
