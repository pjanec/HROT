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

    // ---- BPF-023: active-state decoding ----------------------------------

    [Fact]
    public unsafe void Update_WithBrainHsm64_ActiveLeafIds_DecodedViaMetadata()
    {
        var world  = CreateWorld();
        var entity = world.CreateEntity();

        var brain = new BrainHsm64();
        brain.State.Header.Phase     = InstancePhase.Activity;
        brain.State.ActiveLeafIds[0] = 1;
        brain.State.ActiveLeafIds[1] = 2;
        world.AddComponent(entity, brain);

        var stableA = new Guid("aa000000-0000-0000-0000-000000000001");
        var stableB = new Guid("bb000000-0000-0000-0000-000000000002");
        var assetId = new Guid("cc000000-0000-0000-0000-000000000001");

        var metadata = new MachineMetadata();
        metadata.StateStableIds[1] = stableA;
        metadata.StateStableIds[2] = stableB;

        var sut = new HsmDebugSession();
        sut.SetMetadata(assetId, metadata);
        sut.Update(world, entity);

        var snap = sut.GetCurrentStateSnapshot();
        snap.Should().NotBeNull();
        snap!.AssetId.Should().Be(assetId);
        snap.ActiveLeafStableIds.Should().HaveCount(2);
        snap.ActiveLeafStableIds.Should().Contain(stableA);
        snap.ActiveLeafStableIds.Should().Contain(stableB);
    }

    [Fact]
    public unsafe void Update_WithBrainHsm64_Slot0xFFFF_NotIncludedInActiveLeaves()
    {
        var world  = CreateWorld();
        var entity = world.CreateEntity();

        var brain = new BrainHsm64();
        brain.State.Header.Phase     = InstancePhase.Activity;
        brain.State.ActiveLeafIds[0] = 5;
        brain.State.ActiveLeafIds[1] = 0xFFFF; // empty slot
        world.AddComponent(entity, brain);

        var stableA  = new Guid("dd000000-0000-0000-0000-000000000005");
        var metadata = new MachineMetadata();
        metadata.StateStableIds[5] = stableA;

        var sut = new HsmDebugSession();
        sut.SetMetadata(Guid.NewGuid(), metadata);
        sut.Update(world, entity);

        var snap = sut.GetCurrentStateSnapshot();
        snap!.ActiveLeafStableIds.Should().HaveCount(1);
        snap.ActiveLeafStableIds[0].Should().Be(stableA);
    }

    // ---- BPF-024: StepOut uses Activity-phase predicate ------------------

    [Fact]
    public unsafe void StepOut_does_not_pause_while_in_Entry_phase()
    {
        var world  = CreateWorld();
        var entity = world.CreateEntity();

        var brain = new BrainHsm64();
        brain.State.Header.Phase = InstancePhase.Entry;
        world.AddComponent(entity, brain);

        var spy = new SpyCoordinator();
        var sut = new HsmDebugSession(spy);
        sut.StepOut();
        sut.Update(world, entity);

        // Still in Entry phase -- StepOut must NOT request a pause yet.
        spy.PauseRequested.Should().BeFalse();
    }

    [Fact]
    public unsafe void StepOut_pauses_when_Activity_phase_reached()
    {
        var world  = CreateWorld();
        var entity = world.CreateEntity();

        var brain = new BrainHsm64();
        brain.State.Header.Phase = InstancePhase.Activity;
        world.AddComponent(entity, brain);

        var spy = new SpyCoordinator();
        var sut = new HsmDebugSession(spy);
        sut.StepOut();
        sut.Update(world, entity);

        spy.PauseRequested.Should().BeTrue();
    }

    [Fact]
    public unsafe void StepOver_pauses_when_MicroStep_changes()
    {
        var world  = CreateWorld();
        var entity = world.CreateEntity();

        var brain = new BrainHsm64();
        brain.State.Header.Phase     = InstancePhase.Entry;
        brain.State.Header.MicroStep = 1;
        world.AddComponent(entity, brain);

        var spy = new SpyCoordinator();
        var sut = new HsmDebugSession(spy);
        sut.StepOver(); // captures MicroStep=1

        brain.State.Header.MicroStep = 2;
        world.SetComponent(entity, brain);
        sut.Update(world, entity);

        spy.PauseRequested.Should().BeTrue();
    }

    // ---- BPF-010: event-queue, timer-slot and history-slot decoding ------

    [Fact]
    public unsafe void HsmSnapshot_DecodeEventQueueTimerSlotsHistorySlots_FromHsmInstance64()
    {
        var world  = CreateWorld();
        var entity = world.CreateEntity();

        var childSid = new Guid("ee000000-0000-0000-0000-000000000007");
        var assetId  = new Guid("ff000000-0000-0000-0000-000000000001");

        var brain = new BrainHsm64();
        brain.State.Header.Phase = InstancePhase.Activity;

        // One event in the shared queue
        brain.State.EventCount = 1;
        var ev = new HsmEvent { EventId = 99, Priority = EventPriority.Normal };
        *(HsmEvent*)brain.State.EventBuffer = ev;

        // One timer slot active, one empty
        brain.State.TimerDeadlines[0] = 150u;
        brain.State.TimerDeadlines[1] = 0u;

        // One history slot with recorded child (flat index 7), one empty
        brain.State.HistorySlots[0] = 7;
        brain.State.HistorySlots[1] = 0xFFFF;

        world.AddComponent(entity, brain);

        var metadata = new MachineMetadata();
        metadata.StateStableIds[7] = childSid;

        var sut = new HsmDebugSession();
        sut.SetMetadata(assetId, metadata);
        sut.Update(world, entity);

        var snap = sut.GetCurrentStateSnapshot();
        snap.Should().NotBeNull();

        snap!.EventQueue.Should().HaveCount(1);
        snap.EventQueue[0].EventId.Should().Be(99);
        snap.EventQueue[0].QueuePosition.Should().Be(0);

        snap.TimerSlots.Should().HaveCount(1);
        snap.TimerSlots[0].SlotIndex.Should().Be(0);
        snap.TimerSlots[0].RemainingTicks.Should().Be(150f);

        snap.HistorySlots.Should().HaveCount(1);
        snap.HistorySlots[0].SlotIndex.Should().Be(0);
        snap.HistorySlots[0].RecordedChildStableId.Should().Be(childSid);
    }
}

file sealed class SpyCoordinator : AiTracerCoordinator
{
    public bool PauseRequested;
    public override void RequestStepOneTick() { }
    public override void RequestPause()       => PauseRequested = true;
    public override void RequestContinue()    { }
}
